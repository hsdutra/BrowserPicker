# Plan: BrowserPicker — Seletor de Navegador para Windows

## TL;DR
Aplicativo WinUI 3 (Windows App SDK, Unpackaged) self-contained que: (1) detecta navegadores via registry Windows, (2) exibe janela com Mica e sem barra de título para seleção, (3) abre navegador com URL fornecida, (4) registra-se como handler HTTP/HTTPS. Um único `.exe` com suporte a `--register` e `--uninstall` (com UAC automático), complementado por script PowerShell unificado.

---

## Stack Técnica

- WinUI 3 com Windows App SDK 1.5+ (Unpackaged App, sem MSIX)
- `<TargetFramework>net6.0-windows10.0.19041.0</TargetFramework>`
- Self-contained, PublishSingleFile, RuntimeIdentifier: win-x64
- Pacotes NuGet obrigatórios:
  ```xml
  <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.5.*" />
  <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.22621.*" />
  <PackageReference Include="System.Drawing.Common" Version="6.0.0" />
  ```

---

## Estrutura de Implementação

### Bloco A: Entry Point (`Program.cs`)

- `[STAThread]` no `Main()`
- `WinRT.ComWrappersSupport.InitializeComWrappers()` antes de tudo
- Parseia argumentos: `--register`, `--uninstall`, `--uninstall --confirmed`, `<url>`, ou vazio
- Ramo `--register` → chama `BrowserRegistration.Register()` dentro de try/catch; se `UnauthorizedAccessException`, relança o próprio processo com `ProcessStartInfo { Verb = "runas" }` passando `--register` como argumento; termina sem UI
- Ramo `--uninstall` → define `Environment.SetEnvironmentVariable("BROWSERPICKER_MODE", "uninstall")` e chama `Application.Start(_ => new App())` — **não** chama `BrowserRegistration.Unregister()` diretamente
- Ramo `--uninstall --confirmed` → define `Environment.SetEnvironmentVariable("BROWSERPICKER_MODE", "uninstall-confirmed")` e chama `Application.Start(_ => new App())` — salta o `ContentDialog` de confirmação (utilizador já confirmou antes da elevação)
- Ramo normal → define URL em variável de ambiente e chama `Application.Start(_ => new App())`
- **Motivo**: `ContentDialog` exige `XamlRoot` ativo, que só existe após `Application.Start()`; toda a lógica de UI de desinstalação ocorre dentro do ciclo WinUI 3

### Bloco B: Deteção de Navegadores (`BrowserRegistry.cs`)

- Método: `async Task<List<BrowserItem>> GetInstalledBrowsersAsync()`
- Lê: `HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet` + `HKEY_CURRENT_USER\SOFTWARE\Clients\StartMenuInternet`
- Consolida (user sobrescreve machine), remove duplicatas
- Método é `async` para aguardar `IconExtractor.ExtractIconAsync()` por cada entrada

`BrowserItem.cs`: modelo com `Name`, `ExecutablePath`, `Icon` (`Microsoft.UI.Xaml.Media.Imaging.BitmapImage`)

### Bloco C: Extração de Ícones (`IconExtractor.cs`)

- Método: `async Task<BitmapImage> ExtractIconAsync(string executablePath)`
- Pipeline de conversão (WPF não existe no WinUI 3 — `CreateBitmapSourceFromHBitmap()` não está disponível):
  1. `System.Drawing.Icon.ExtractAssociated(executablePath)` → `System.Drawing.Icon`
  2. `icon.ToBitmap()` → `System.Drawing.Bitmap`
  3. `bitmap.Save(memoryStream, ImageFormat.Png)` → `MemoryStream`
  4. `new BitmapImage()` (WinUI 3: `Microsoft.UI.Xaml.Media.Imaging.BitmapImage`)
  5. `await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream())`
- Fallback: ícone genérico 32x32 embutido como recurso se qualquer passo falha
- Requer `System.Drawing.Common` (sem ele, `PlatformNotSupportedException` em runtime)
- **Nota**: método é `async` porque `SetSourceAsync()` é assíncrono; `BrowserRegistry.GetInstalledBrowsersAsync()` deve aguardar cada ícone

### Bloco D: Interface Gráfica (WinUI 3, `MainWindow`)

**Configuração da janela:**
- `ExtendsContentIntoTitleBar = true` (remove barra de título, mantém bordas e cantos arredondados nativos do Windows 11)
- `SetTitleBar(null)` (janela não precisa ser arrastada)
- `AppWindow.IsShownInSwitchers = false` (não aparece na taskbar nem no Alt+Tab)
- `SystemBackdrop = new MicaBackdrop()` (efeito Mica nativo do Windows 11)
- `Application.RequestedTheme` ligado ao tema do sistema (claro/escuro automático)
- Centralizar via `AppWindow.Move()` com `DisplayArea.GetFromWindowId()`
- Tamanho: ~400x500px

**Lista de navegadores:**
- `ListView` com `DataTemplate` (ícone 32x32 + nome)
- Carrega via `await BrowserRegistry.GetInstalledBrowsersAsync()` no evento `Loaded`
- Clique num item → `UrlHandler.OpenUrlInBrowser()` → fecha
- Tecla ESC → fecha
- Clique fora da janela → fecha
- Se nenhum navegador encontrado: `ContentDialog` com mensagem → abre `ms-settings:defaultapps` → fecha
- Múltiplos com mesmo nome: exibir caminho do executável na tooltip

### Bloco E: Registo e Desinstalação (`BrowserRegistration.cs`)

**Método `Register()` (requer elevação):**
- Cria: `HKEY_LOCAL_MACHINE\SOFTWARE\BrowserPicker`
  - `DisplayName` = "BrowserPicker"
  - `Description` = "Select your default browser"
  - `ApplicationIcon` = caminho do ícone
- Cria: `HKEY_LOCAL_MACHINE\SOFTWARE\BrowserPicker\Capabilities`
  - `ApplicationName` = "BrowserPicker"
  - `ApplicationDescription` = "Browser Selection Tool"
- Cria: `HKEY_LOCAL_MACHINE\SOFTWARE\BrowserPicker\Capabilities\URLAssociations`
  - `http` = `BrowserPickerProtocol`
  - `https` = `BrowserPickerProtocol`
- Cria: `HKEY_CLASSES_ROOT\BrowserPickerProtocol`
  - `(Default)` = "BrowserPicker Protocol"
  - `shell\open\command` = `"C:\Program Files\BrowserPicker\BrowserPicker.exe" "%1"`
- Adiciona: `HKEY_LOCAL_MACHINE\SOFTWARE\RegisteredApplications`
  - `BrowserPicker` = `Software\BrowserPicker\Capabilities`

**Método `Unregister()` (requer elevação):**
- Remove apenas registry (sem UI, sem confirmação — isso é responsabilidade da `UninstallWindow`):
  - Remove: `HKEY_LOCAL_MACHINE\SOFTWARE\BrowserPicker` (completo)
  - Remove: `HKEY_CLASSES_ROOT\BrowserPickerProtocol` (completo)
  - Remove: entrada `BrowserPicker` de `HKEY_LOCAL_MACHINE\SOFTWARE\RegisteredApplications`
- **NÃO apaga a pasta** (o exe está em execução; apagada pelo script PowerShell após o processo terminar)
- Lança `UnauthorizedAccessException` se GPO bloquear (tratada pela `UninstallWindow`)

**`App.xaml.cs` — `OnLaunched`:**
- Lê `Environment.GetEnvironmentVariable("BROWSERPICKER_MODE")`
- Se `"uninstall"` ou `"uninstall-confirmed"` → instancia `UninstallWindow`
- Caso contrário → instancia `MainWindow` com a URL

**`UninstallWindow` (Window auxiliar mínima):**
- Criada e tornada imediatamente invisível via `AppWindow.Hide()`
- Lê `Environment.GetEnvironmentVariable("BROWSERPICKER_MODE")`
- Se `"uninstall-confirmed"` → salta o `ContentDialog` de confirmação e chama `BrowserRegistration.Unregister()` diretamente
- Se `"uninstall"` → mostra `ContentDialog` de confirmação primeiro; se confirmado → chama `BrowserRegistration.Unregister()`
  - Em caso de `UnauthorizedAccessException` → relança o próprio processo com `ProcessStartInfo { Verb = "runas" }` passando `--uninstall --confirmed` como argumentos e termina; se a relança também falhar com `UnauthorizedAccessException` (GPO a bloquear mesmo com elevação) → `ContentDialog` "Erro: GPO pode estar limitando acesso ao registry"
  - Em caso de sucesso → `ContentDialog` "Registry removido com sucesso. A pasta será removida pelo script de desinstalação."
- Após qualquer resposta (confirmado, cancelado, ou erro) → `Application.Current.Exit()`
### Bloco F: Abertura de URLs (`UrlHandler.cs`)

- Método: `OpenUrlInBrowser(BrowserItem browser, string url)`
- Constrói `ProcessStartInfo` com o executável e a URL como argumento (escape de aspas e espaços)
- Se URL vazia, abre o navegador sem argumento
- Inicia processo e fecha a aplicação

### Bloco G: Manifest (`app.manifest`)

- `<requestedExecutionLevel level="asInvoker" />` (elevação UAC feita pelo script, não pelo manifest)

### Bloco H: Script PowerShell (`Install-BrowserPicker.ps1`)

**Modo instalação (default):**
```
.\Install-BrowserPicker.ps1
```
- Copia `BrowserPicker.exe` para `$env:ProgramFiles\BrowserPicker\`
- Executa `BrowserPicker.exe --register` com elevação (`-Verb RunAs`)
- Valida entradas no registry
- Exibe sucesso ou erro

**Modo desinstalação:**
```
.\Install-BrowserPicker.ps1 -Uninstall
```
- Executa `BrowserPicker.exe --uninstall` com elevação
- Aguarda fim do processo (`WaitForExit()`) — **critical**: o exe está em execução dentro da pasta
- Após conclusão, remove `$env:ProgramFiles\BrowserPicker\` com `Remove-Item -Recurse -Force`
- Valida remoção verificando `HKEY_LOCAL_MACHINE\SOFTWARE\BrowserPicker` (não deve existir)
- Exibe sucesso ou erro

---

## Ficheiros a Criar

| Ficheiro | Tipo | Descrição |
|---|---|---|
| `BrowserPicker.csproj` | XML | Configuração WinUI 3, self-contained |
| `Program.cs` | C# | Entry point, parser de argumentos |
| `MainWindow.xaml` | XAML | UI com Mica, frameless |
| `MainWindow.xaml.cs` | C# | Lógica UI, eventos |
| `App.xaml` | XAML | Recursos globais WinUI 3 |
| `App.xaml.cs` | C# | Startup WinUI 3 |
| `BrowserItem.cs` | C# | Modelo de dados |
| `BrowserRegistry.cs` | C# | Leitura registry navegadores |
| `IconExtractor.cs` | C# | Extração de ícones |
| `BrowserRegistration.cs` | C# | Registo e desinstalação no registry |
| `UrlHandler.cs` | C# | Abertura de URLs |
| `UninstallWindow.xaml` | XAML | Window auxiliar mínima para fluxo de desinstalação |
| `UninstallWindow.xaml.cs` | C# | Lógica de confirmação, chamada a `Unregister()`, `Exit()` |
| `app.manifest` | XML | Manifest com `asInvoker` |
| `Install-BrowserPicker.ps1` | PS1 | Script instalar/desinstalar |

---

## Fases de Implementação

1. **Setup projeto** (`BrowserPicker.csproj`, `Program.cs` com parser, `App.xaml`)
2. **Deteção** (`BrowserRegistry.cs`, `BrowserItem.cs`, `IconExtractor.cs`)
3. **UI** (`MainWindow.xaml` + code-behind)
4. **URL handler** (`UrlHandler.cs`)
5. **Registry ops** (`BrowserRegistration.cs` com elevação e ContentDialog auxiliar)
6. **Manifest** (`app.manifest`)
7. **Script PS** (`Install-BrowserPicker.ps1`)
8. **Build & teste**

---

## Verificação

| Critério | Comando/Teste |
|---|---|
| Build único | `dotnet publish -c Release -r win-x64 --self-contained` → 1 `.exe` |
| Listar navegadores | `.\BrowserPicker.exe` → janela Mica com Chrome, Firefox, Edge, etc. |
| URL normal | `.\BrowserPicker.exe https://github.com` → clique abre em navegador |
| Sem URL | `.\BrowserPicker.exe` → janela abre sem URL, apenas seleção |
| Registo | `.\BrowserPicker.exe --register` (elevação) → verifica registry com `regedit` |
| Settings Windows | *Definições > Aplicativos > Aplicativos padrão* → "BrowserPicker" em navegadores |
| Handler protocolo | Clique link HTTP na web → abre BrowserPicker |
| Desinstalação (exe) | `.\BrowserPicker.exe --uninstall` → ContentDialog de confirmação, remove registry, **não apaga pasta** |
| UI comportamento | ESC fecha, clique fora fecha, sem taskbar, sem Alt+Tab, centralizada |
| Efeito Mica | Fundo translúcido Mica visível no Windows 11 |
| Ícones | Aparecem corretamente para Chrome, Firefox, Edge |
| Script instalar | `.\Install-BrowserPicker.ps1` → copia, regista, valida sucesso |
| Script desinstalar | `.\Install-BrowserPicker.ps1 -Uninstall` → executa exe, aguarda, remove pasta `%ProgramFiles%\BrowserPicker\` |
