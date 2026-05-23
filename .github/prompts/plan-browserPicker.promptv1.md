# Plan: BrowserPicker — Seletor de Navegador para Windows

## TL;DR

Aplicativo WPF .NET 6 self-contained que: (1) detecta navegadores via registry Windows, (2) exibe janela frameless para seleção, (3) abre navegador com URL fornecida, (4) registra-se como handler HTTP/HTTPS. Um único `.exe` com suporte a `--register` e `--uninstall` (com UAC automático), complementado por script PowerShell unificado.

---

## Estrutura de Implementação

### Bloco A: Núcleo do Projeto (.NET 6, WPF)

- **Ficheiro**: `BrowserPicker.csproj`

  - `<PublishSingleFile>true</PublishSingleFile>`
  - `<SelfContained>true</SelfContained>`
  - `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
  - `<StartupObject>BrowserPicker.Program</StartupObject>` (evita conflito com o `Main()` auto-gerado pelo `App.xaml`)
  - `App.xaml` com build action `Page` em vez de `ApplicationDefinition` (caso contrário o WPF gera um segundo `Main()` e o conflito persiste):
    ```xml
    <ApplicationDefinition Remove="App.xaml" />
    <Page Include="App.xaml" />
    ```
  - Dependência explícita para extração de ícones (não incluída por defeito em projetos WPF .NET 6; sem ela, falha em runtime com `PlatformNotSupportedException`):
    ```xml
    <PackageReference Include="System.Drawing.Common" Version="6.0.0" />
    ```
- **Classe**: `Program.cs`

  - `[STAThread]` decorador no `Main()` (obrigatório para MessageBox WPF funcionar)
  - Parseia argumentos: `--register`, `--uninstall`, `--uninstall --confirmed`, `<url>`, ou vazio
  - Ramo `--register` → chama `BrowserRegistration.Register()` dentro de try/catch; se `UnauthorizedAccessException`, relança o próprio processo com `ProcessStartInfo { Verb = "runas" }` passando `--register` como argumento; termina sem UI
  - Ramo `--uninstall` → chama `BrowserRegistration.Unregister(confirmed: false)`
  - Ramo `--uninstall --confirmed` → chama `BrowserRegistration.Unregister(confirmed: true)` (salta a MessageBox de confirmação — utilizador já confirmou antes da elevação)
  - Ramo normal → instancia e corre a app WPF manualmente:
    ```csharp
    var app = new App();
    app.Run(new MainWindow(url));
    ```
  - **Nota**: `App.xaml` deve ter `x:Class="BrowserPicker.App"` mas **sem** `StartupUri` (o `MainWindow` é passado diretamente pelo `Program.cs`)
- **Classe**: `BrowserItem.cs`

  - Modelo: `Name`, `ExecutablePath`, `Icon` (BitmapImage)

---

### Bloco B: Deteção de Navegadores

- **Classe**: `BrowserRegistry.cs`

  - Método: `GetInstalledBrowsers()` → `List<BrowserItem>`
  - Lê: `HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet` + `HKEY_CURRENT_USER\SOFTWARE\Clients\StartMenuInternet`
  - Consolida (user sobrescreve machine), remove duplicatas
- **Classe**: `IconExtractor.cs`

  - Método: `ExtractIcon(executablePath)` → `BitmapImage`
  - Usa `System.Drawing.Icon.ExtractAssociated()`
  - Fallback: ícone genérico (32x32 placeholder) se falha

---

### Bloco C: Interface Gráfica (WPF)

- **Ficheiro**: `MainWindow.xaml`

  - `WindowStyle="None"`, `AllowsTransparency="True"`
  - Fundo: #1E1E1E, tamanho ~400x500px
  - `ListBox` com `ItemsControl` (ícone 32x32 + nome)
  - `ShowInTaskbar="False"`, `WindowStartupLocation="CenterScreen"`
- **Ficheiro**: `MainWindow.xaml.cs`

  - Carrega `BrowserRegistry.GetInstalledBrowsers()` ao iniciar
  - **Eventos**:
    - Clique item → `UrlHandler.OpenUrlInBrowser()` → fecha
    - ESC → fecha
    - Clique fora janela → fecha
  - **Edge cases**:
    - Nenhum navegador: dialog → abre `ms-settings:defaultapps` → fecha
    - Múltiplos com mesmo nome: exibir caminho na tooltip ou versão
- **Ficheiro**: `App.xaml`

  - Recursos WPF (cores, fontes, estilos escuros)

---

### Bloco D: Gestão de Registry (Registo/Desinstalação)

- **Classe**: `BrowserRegistration.cs`

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

  **Método `Unregister(bool confirmed)` (requer elevação):**

  - Se `confirmed == false`: pede confirmação via `System.Windows.MessageBox` (WPF nativo, sem dependências externas): "Tem a certeza que deseja remover o BrowserPicker? [Sim/Não]"
    - Se "Não": cancela e retorna sem fazer nada
  - Se `confirmed == true`: salta a MessageBox (utilizador já confirmou antes da elevação UAC)
  - Prossegue com a remoção:
    - Remove: `HKEY_LOCAL_MACHINE\SOFTWARE\BrowserPicker` (completo)
    - Remove: `HKEY_CLASSES_ROOT\BrowserPickerProtocol` (completo)
    - Remove: entrada `BrowserPicker` de `HKEY_LOCAL_MACHINE\SOFTWARE\RegisteredApplications`
  - **NÃO apaga a pasta** (o exe está a executar-se dentro dela; Windows vai bloquear)
  - Trata `UnauthorizedAccessException`:
    - Se `confirmed == false`: relança o próprio processo com `ProcessStartInfo { Verb = "runas" }` passando `--uninstall --confirmed` como argumentos e termina (utilizador não verá segunda confirmação)
    - Se `confirmed == true` (já elevado): a GPO está a bloquear mesmo com elevação → MessageBox "Erro: GPO pode estar limitando acesso ao registry"
  - Em caso de sucesso: MessageBox "Registry removido com sucesso. A pasta será removida pelo script de desinstalação."
  - **Nota**: A pasta `%ProgramFiles%\BrowserPicker\` é apagada pelo script PowerShell após o exe terminar

---

### Bloco E: Abertura de URLs

- **Classe**: `UrlHandler.cs`
  - Método: `OpenUrlInBrowser(BrowserItem browser, string url)`
    - Constrói `ProcessStartInfo`
    - Argumento: URL (escape se contém aspas/espaços)
    - Se URL vazia, abre o navegador sem argumento
    - Inicia processo, fecha aplicação

---

### Bloco F: Manifest & Compilação

- **Ficheiro**: `app.manifest`

  - `<requestedExecutionLevel level="asInvoker" />` (elevação UAC feita pelo script, não pelo manifest)
- **Build**: `dotnet publish -c Release -r win-x64 --self-contained`

  - Saída: `bin/Release/net6.0-windows/win-x64/publish/BrowserPicker.exe`

---

### Bloco G: Script PowerShell

- **Ficheiro**: `Install-BrowserPicker.ps1`

  **Modo 1** (instalar, default):

  ```
  .\Install-BrowserPicker.ps1
  ```

  - Copia `.exe` para `$env:ProgramFiles\BrowserPicker\`
  - Executa `BrowserPicker.exe --register` com elevação (`-Verb RunAs`)
  - Valida entradas no registry
  - Cria atalho no Start Menu (opcional)
  - Exibe sucesso ou erro

  **Modo 2** (desinstalar):

  ```
  .\Install-BrowserPicker.ps1 -Uninstall
  ```

  - Executa `BrowserPicker.exe --uninstall` com elevação (`-Verb RunAs`)
  - **Aguarda fim do processo** (`WaitForExit()`) — critical: o exe está em execução dentro da pasta
  - **Após conclusão**, remove a pasta `$env:ProgramFiles\BrowserPicker\` completamente com `Remove-Item -Path ... -Recurse -Force`
  - Valida remoção do registry com `Get-Item` em `HKEY_LOCAL_MACHINE\SOFTWARE\BrowserPicker` (deve não existir)
  - Exibe sucesso ou erro no final

---

## Ficheiros a Criar

| Ficheiro                      | Tipo | Descrição                         |
| ----------------------------- | ---- | ----------------------------------- |
| `BrowserPicker.csproj`      | XML  | Configuração .NET 6 WPF           |
| `Program.cs`                | C#   | Entry point, parser de argumentos   |
| `MainWindow.xaml`           | XAML | UI frameless                        |
| `MainWindow.xaml.cs`        | C#   | Lógica UI, eventos                 |
| `App.xaml`                  | XAML | Recursos globais WPF                |
| `App.xaml.cs`               | C#   | Startup WPF                         |
| `BrowserItem.cs`            | C#   | Modelo dados                        |
| `BrowserRegistry.cs`        | C#   | Leitura registry navegadores        |
| `IconExtractor.cs`          | C#   | Extração ícones `.exe`         |
| `BrowserRegistration.cs`    | C#   | Registo/desinstalação no registry |
| `UrlHandler.cs`             | C#   | Abertura URLs em navegadores        |
| `app.manifest`              | XML  | Manifest com `asInvoker`          |
| `Install-BrowserPicker.ps1` | PS1  | Script instalar/desinstalar         |

---

## Fases de Implementação

1. **Setup projeto** (.csproj, Program.cs com parser)
2. **Deteção** (BrowserRegistry + IconExtractor)
3. **UI** (MainWindow XAML/code-behind, App.xaml)
4. **URL handler** (UrlHandler.cs)
5. **Registry ops** (BrowserRegistration.cs com elevação)
6. **Manifest** (app.manifest)
7. **Script PS** (Install-BrowserPicker.ps1)
8. **Build & teste**

---

## Verificação

| Critério                            | Comando/Teste                                                                                                                                                      |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Build único                         | `dotnet publish -c Release -r win-x64 --self-contained` → 1 `.exe`                                                                                            |
| Listar navegadores                   | `.\BrowserPicker.exe` → abre janela com Chrome, Firefox, Edge, etc.                                                                                             |
| URL normal                           | `.\BrowserPicker.exe https://github.com` → clique abre em navegador                                                                                             |
| Sem URL                              | `.\BrowserPicker.exe` → janela vazia de URL, apenas seleção                                                                                                   |
| Registo                              | `.\BrowserPicker.exe --register` (elevação) → verifica registry com `regedit`                                                                               |
| Settings Windows                     | *Definições > Aplicativos > Aplicativos padrão* → "BrowserPicker" em navegadores                                                                             |
| Handler protocolo                    | Clique link HTTP na web → abre BrowserPicker                                                                                                                      |
| Desinstalação (exe) sem elevação | `.\BrowserPicker.exe --uninstall` → pede confirmação (MessageBox), auto-eleva com `--confirmed` se necessário, remove registry, **não apaga pasta** |
| Desinstalação (exe) já elevado    | `.\BrowserPicker.exe --uninstall --confirmed` → salta confirmação, remove registry diretamente                                                                |
| UI comportamento                     | ESC fecha, clique fora fecha, sem taskbar, centralizada                                                                                                            |
| Ícones                              | Aparecem corretamente para Chrome, Firefox, Edge                                                                                                                   |
| Script instalar                      | `.\Install-BrowserPicker.ps1` → copia, regista, valida sucesso                                                                                                  |
| Script desinstalar                   | `.\Install-BrowserPicker.ps1 -Uninstall` → executa exe com elevação, aguarda, remove pasta `%ProgramFiles%\BrowserPicker\`                                  |
