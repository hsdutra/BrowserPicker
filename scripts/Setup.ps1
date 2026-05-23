#Requires -Version 5.1
<#
.SYNOPSIS
    Instala, desinstala ou verifica o BrowserPicker sem necessitar de build.
    Coloque este ficheiro na mesma pasta que BrowserPicker.exe e execute-o.

.EXAMPLE
    .\Deploy-BrowserPicker.ps1
#>
param (
    [ValidateSet('install','uninstall','verify')]
    [string]$Action = ''
)

$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

# ---------------------------------------------------------------------------
# Verificar que BrowserPicker.exe esta ao lado deste script
# ---------------------------------------------------------------------------
$_ExeCheck = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) 'BrowserPicker.exe'
if (-not (Test-Path $_ExeCheck)) {
    Write-Host ""
    Write-Host "ERRO: BrowserPicker.exe nao encontrado na mesma pasta que este script." -ForegroundColor Red
    Write-Host "      Coloque BrowserPicker.exe em:" -ForegroundColor Red
    Write-Host "      $(Split-Path -Parent $MyInvocation.MyCommand.Definition)" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Pressione Enter para fechar"
    exit 1
}

# ---------------------------------------------------------------------------
# Menu interativo
# ---------------------------------------------------------------------------
if ($Action -eq '') {
    Write-Host ""
    Write-Host "================================================"
    Write-Host "  BrowserPicker -- Instalacao / Desinstalacao"
    Write-Host "================================================"
    Write-Host ""
    Write-Host "  [1] Instalar"
    Write-Host "  [2] Desinstalar"
    Write-Host "  [3] Verificar estado"
    Write-Host ""
    $choice = Read-Host "  Escolha uma opcao (1, 2 ou 3)"
    switch ($choice) {
        '1' { $Action = 'install'   }
        '2' { $Action = 'uninstall' }
        '3' { $Action = 'verify'    }
        default { Write-Host "Opcao invalida. A sair."; exit 1 }
    }
    Write-Host ""
}

# ---------------------------------------------------------------------------
# Auto-elevacao UAC (passa a accao escolhida para a janela elevada)
# ---------------------------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "  A pedir elevacao UAC - o output aparece na nova janela elevada." -ForegroundColor Cyan
    $psArgs = '-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', $MyInvocation.MyCommand.Definition, '-Action', $Action
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList $psArgs
    exit
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppName    = 'BrowserPicker'
$InstallDir = "$env:ProgramFiles\$AppName"
$ExeDest    = Join-Path $InstallDir "$AppName.exe"
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ExeSrc     = Join-Path $ScriptDir "$AppName.exe"

# ---------------------------------------------------------------------------
# Funcao auxiliar: parar o processo
# ---------------------------------------------------------------------------
function Stop-App {
    $proc = Get-Process -Name $AppName -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "  A fechar $AppName em execucao..."
        $proc | Stop-Process -Force
        $proc | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    }
}

# ============================================================================
# VERIFICAR
# ============================================================================
function Invoke-Verify {
    if (-not (Get-PSDrive -Name HKCR -ErrorAction SilentlyContinue)) {
        New-PSDrive -Name HKCR -PSProvider Registry -Root HKEY_CLASSES_ROOT | Out-Null
    }

    $checks = [System.Collections.Generic.List[PSCustomObject]]::new()
    function Add-Check([string]$Group, [string]$Name, [scriptblock]$Test) {
        try   { $ok = [bool]($Test.Invoke()) }
        catch { $ok = $false }
        $checks.Add([PSCustomObject]@{ Group = $Group; Name = $Name; OK = $ok })
    }

    # Processo
    Add-Check "Processo" "BrowserPicker em execucao na bandeja" {
        $null -ne (Get-Process -Name $AppName -ErrorAction SilentlyContinue)
    }

    # Ficheiros
    Add-Check "Ficheiros" "BrowserPicker.exe existe em Program Files" { Test-Path $ExeDest }
    Add-Check "Ficheiros" "Pasta BrowserPicker existe em Program Files" { Test-Path $InstallDir }

    # Autostart
    Add-Check "Autostart" "Iniciar com o Windows (HKCU\Run)" {
        $null -ne (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $AppName -ErrorAction SilentlyContinue)
    }

    # Apps instaladas (Add/Remove Programs)
    Add-Check "Apps instaladas" "Entrada em Definicoes > Aplicacoes instaladas" {
        Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
    }
    Add-Check "Apps instaladas" "UninstallString configurado" {
        $v = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Name "UninstallString" -EA Stop).UninstallString
        $v -like "*--uninstall*"
    }

    # Registry: HKLM\SOFTWARE\BrowserPicker
    Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "Chave BrowserPicker existe" {
        Test-Path "HKLM:\SOFTWARE\BrowserPicker"
    }
    Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "Valor DisplayName existe" {
        (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker" -Name "DisplayName" -EA Stop).DisplayName -ne $null
    }
    Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "Subchave Capabilities existe" {
        Test-Path "HKLM:\SOFTWARE\BrowserPicker\Capabilities"
    }
    Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "URLAssociations http -> BrowserPickerProtocol" {
        (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker\Capabilities\URLAssociations" -Name "http" -EA Stop).http -eq "BrowserPickerProtocol"
    }
    Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "URLAssociations https -> BrowserPickerProtocol" {
        (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker\Capabilities\URLAssociations" -Name "https" -EA Stop).https -eq "BrowserPickerProtocol"
    }

    # Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol
    Add-Check "Registry: HKLM\Classes\BrowserPickerProtocol" "Chave ProgId existe (HKLM)" {
        Test-Path "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol"
    }
    Add-Check "Registry: HKLM\Classes\BrowserPickerProtocol" "shell\open\command existe (HKLM)" {
        Test-Path "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command"
    }
    Add-Check "Registry: HKLM\Classes\BrowserPickerProtocol" "Comando aponta para BrowserPicker.exe (HKLM)" {
        $cmd = (Get-ItemProperty "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command" -Name "(Default)" -EA Stop)."(Default)"
        $cmd -like "*BrowserPicker.exe*"
    }
    Add-Check "Registry: HKLM\Classes\BrowserPickerProtocol" "EditFlags = 2 (HKLM)" {
        (Get-ItemProperty "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol" -Name "EditFlags" -EA Stop).EditFlags -eq 2
    }

    # Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol (obrigatorio para Windows 11 UserChoice)
    Add-Check "Registry: HKCU\Classes\BrowserPickerProtocol" "Chave ProgId existe (HKCU)" {
        Test-Path "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol"
    }
    Add-Check "Registry: HKCU\Classes\BrowserPickerProtocol" "shell\open\command existe (HKCU)" {
        Test-Path "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command"
    }
    Add-Check "Registry: HKCU\Classes\BrowserPickerProtocol" "Comando aponta para BrowserPicker.exe (HKCU)" {
        $cmd = (Get-ItemProperty "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command" -Name "(Default)" -EA Stop)."(Default)"
        $cmd -like "*BrowserPicker.exe*"
    }
    Add-Check "Registry: HKCU\Classes\BrowserPickerProtocol" "Comando inclui argumento %1 (HKCU)" {
        $cmd = (Get-ItemProperty "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command" -Name "(Default)" -EA Stop)."(Default)"
        $cmd -like '*"%1"*'
    }

    # Registry: HKCR
    Add-Check "Registry: HKCR\BrowserPickerProtocol" "shell\open\command registado (HKCR)" {
        Test-Path "HKCR:\BrowserPickerProtocol\shell\open\command"
    }

    # Registry: RegisteredApplications
    Add-Check "Registry: RegisteredApplications" "Entrada BrowserPicker registada" {
        $null -ne (Get-ItemProperty "HKLM:\SOFTWARE\RegisteredApplications" -Name $AppName -EA SilentlyContinue)
    }
    Add-Check "Registry: RegisteredApplications" "Aponta para Software\BrowserPicker\Capabilities" {
        (Get-ItemProperty "HKLM:\SOFTWARE\RegisteredApplications" -Name $AppName -EA Stop).$AppName -eq "Software\BrowserPicker\Capabilities"
    }

    # Registry: StartMenuInternet
    Add-Check "Registry: StartMenuInternet" "Chave StartMenuInternet\BrowserPicker existe" {
        Test-Path "HKLM:\SOFTWARE\Clients\StartMenuInternet\$AppName"
    }
    Add-Check "Registry: StartMenuInternet" "DefaultIcon registado" {
        Test-Path "HKLM:\SOFTWARE\Clients\StartMenuInternet\$AppName\DefaultIcon"
    }
    Add-Check "Registry: StartMenuInternet" "shell\open\command registado" {
        Test-Path "HKLM:\SOFTWARE\Clients\StartMenuInternet\$AppName\shell\open\command"
    }
    Add-Check "Registry: StartMenuInternet" "Capabilities\URLAssociations\http registado" {
        (Get-ItemProperty "HKLM:\SOFTWARE\Clients\StartMenuInternet\$AppName\Capabilities\URLAssociations" -Name "http" -EA Stop).http -eq "BrowserPickerProtocol"
    }
    Add-Check "Registry: StartMenuInternet" "Capabilities\URLAssociations\https registado" {
        (Get-ItemProperty "HKLM:\SOFTWARE\Clients\StartMenuInternet\$AppName\Capabilities\URLAssociations" -Name "https" -EA Stop).https -eq "BrowserPickerProtocol"
    }

    # ---------------------------------------------------------------------------
    # Exibir resultados
    # ---------------------------------------------------------------------------
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "  BrowserPicker -- Estado de Instalacao"         -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan

    $currentGroup = $null
    foreach ($c in $checks) {
        if ($c.Group -ne $currentGroup) {
            Write-Host ""
            Write-Host "[ $($c.Group) ]" -ForegroundColor Yellow
            $currentGroup = $c.Group
        }
        if ($c.OK) {
            Write-Host "  [OK]      $($c.Name)" -ForegroundColor Green
        } else {
            Write-Host "  [AUSENTE] $($c.Name)" -ForegroundColor DarkGray
        }
    }

    $passed          = @($checks | Where-Object { $_.OK }).Count
    $total           = $checks.Count
    $exeOK           = ($checks | Where-Object { $_.Name -like "*BrowserPicker.exe existe*" } | Select-Object -First 1).OK
    $procOK          = ($checks | Where-Object { $_.Group -eq "Processo" } | Select-Object -First 1).OK
    $gruposReg       = $checks | Where-Object { $_.Group -like "Registry:*" }
    $registryOK      = @($gruposReg | Where-Object { $_.OK }).Count
    $registryTotal   = @($gruposReg).Count
    $registryCompleto = $registryOK -ge ($registryTotal - 2)

    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ("  RESULTADO: $passed/$total verificacoes passaram") -ForegroundColor $(if ($passed -eq $total) { 'Green' } else { 'Yellow' })
    Write-Host ""
    Write-Host "  VEREDITO" -ForegroundColor Cyan
    Write-Host "------------------------------------------------" -ForegroundColor Cyan

    if ($exeOK -and $registryCompleto -and $procOK) {
        Write-Host "  INSTALADO E APTO PARA FUNCIONAR" -ForegroundColor Green
        Write-Host ""
        Write-Host "  Proximo passo: va a Definicoes > Aplicativos > Aplicativos" -ForegroundColor White
        Write-Host "  padrao e defina BrowserPicker como navegador padrao."       -ForegroundColor White
    } elseif ($exeOK -and $registryCompleto -and -not $procOK) {
        Write-Host "  INSTALADO MAS NAO EM EXECUCAO" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  O BrowserPicker esta instalado e registado," -ForegroundColor White
        Write-Host "  mas o processo nao esta em execucao."        -ForegroundColor White
        Write-Host "  Inicie o programa ou reinstale para restaurar o autostart." -ForegroundColor White
    } elseif ($exeOK -or $registryOK -gt 0) {
        Write-Host "  INSTALADO MAS NAO APTO PARA FUNCIONAR" -ForegroundColor Yellow
        Write-Host ""
        $ausentes = $checks | Where-Object { -not $_.OK }
        foreach ($a in $ausentes) { Write-Host "  ! Ausente: $($a.Name)" -ForegroundColor Yellow }
        Write-Host ""
        Write-Host "  Execute a opcao [1] Instalar para corrigir." -ForegroundColor White
    } else {
        Write-Host "  NAO INSTALADO" -ForegroundColor DarkGray
    }
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ""
}

# ============================================================================
# DESINSTALAR
# ============================================================================
if ($Action -eq 'uninstall') {
    Write-Host "[$AppName] A desinstalar..."

    Stop-App

    Write-Host "  A remover entradas de registry..."
    Remove-Item 'HKLM:\SOFTWARE\BrowserPicker'                              -Recurse -Force -EA SilentlyContinue
    Remove-Item 'HKLM:\SOFTWARE\Classes\BrowserPickerProtocol'              -Recurse -Force -EA SilentlyContinue
    Remove-Item 'HKLM:\SOFTWARE\Classes\BrowserPickerHTML'                  -Recurse -Force -EA SilentlyContinue
    Remove-Item 'HKCU:\SOFTWARE\Classes\BrowserPickerProtocol'              -Recurse -Force -EA SilentlyContinue
    Remove-Item 'HKCU:\SOFTWARE\Classes\BrowserPickerHTML'                  -Recurse -Force -EA SilentlyContinue
    Remove-Item "HKLM:\SOFTWARE\Clients\StartMenuInternet\$AppName"         -Recurse -Force -EA SilentlyContinue
    Remove-Item "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Recurse -Force -EA SilentlyContinue
    Remove-ItemProperty 'HKLM:\SOFTWARE\RegisteredApplications'             -Name $AppName  -EA SilentlyContinue
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $AppName -EA SilentlyContinue

    if (Test-Path $InstallDir) {
        Write-Host "  A remover pasta $InstallDir ..."
        $removed = $false
        for ($i = 1; $i -le 6; $i++) {
            try {
                Remove-Item -Recurse -Force -Path $InstallDir -EA Stop
                $removed = $true
                break
            } catch {
                if ($i -lt 6) {
                    Write-Host "  Aguardando libertacao de ficheiros (tentativa $i/6)..."
                    Start-Sleep -Seconds 1
                }
            }
        }
        if (-not $removed) {
            Write-Warning "Nao foi possivel remover $InstallDir. Remova manualmente."
        }
    }

    Write-Host "[$AppName] Desinstalacao concluida." -ForegroundColor Green
    Write-Host ""
    Invoke-Verify
    Read-Host "`nPressione Enter para fechar"
    exit 0
}

# ============================================================================
# VERIFICAR (accao directa)
# ============================================================================
if ($Action -eq 'verify') {
    Invoke-Verify
    Read-Host "Pressione Enter para fechar"
    exit 0
}

# ============================================================================
# INSTALAR
# ============================================================================
Write-Host "[$AppName] A instalar..."

# Se o .csproj existir na pasta pai do script, publicar automaticamente
$ProjectDir = Split-Path -Parent $ScriptDir
$CsprojPath = Join-Path $ProjectDir 'BrowserPicker.csproj'
$PublishExe = Join-Path $ProjectDir 'bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\BrowserPicker.exe'

if (Test-Path $CsprojPath) {
    Write-Host "  Projeto encontrado. A publicar (dotnet publish)..."
    $publishResult = & dotnet publish $CsprojPath -c Release -r win-x64 --self-contained 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish falhou:`n$publishResult"
        Read-Host "`nPressione Enter para fechar"
        exit 1
    }
    Write-Host "  Publicacao concluida."
    Copy-Item -Path $PublishExe -Destination $ExeSrc -Force
}

if (-not (Test-Path $ExeSrc)) {
    Write-Error "BrowserPicker.exe nao encontrado em: $ScriptDir`nColoque o exe na mesma pasta que este script ou execute a partir da pasta do projeto."
    Read-Host "`nPressione Enter para fechar"
    exit 1
}

Stop-App

if (-not (Test-Path $InstallDir)) {
    Write-Host "  A criar pasta $InstallDir ..."
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

Write-Host "  A copiar $AppName.exe para $InstallDir ..."
Copy-Item -Path $ExeSrc -Destination $ExeDest -Force

# Copiar app.ico se existir ao lado do script
$IcoSrc = Join-Path $ScriptDir 'app.ico'
if (Test-Path $IcoSrc) {
    Write-Host "  A copiar app.ico ..."
    Copy-Item -Path $IcoSrc -Destination (Join-Path $InstallDir 'app.ico') -Force
}

Write-Host "  A registar como gestor de protocolo http/https ..."
$proc = Start-Process -FilePath $ExeDest -ArgumentList '--register' -PassThru -Wait
if ($proc.ExitCode -ne 0) {
    Write-Warning "O processo --register terminou com codigo $($proc.ExitCode)."
}

# Configurar autostart
$regRunPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $regRunPath -Name $AppName -Value "`"$ExeDest`"" -Force
Write-Host "  Autostart configurado (pode desativar no menu da bandeja)."

if (Test-Path "HKLM:\SOFTWARE\BrowserPicker") {
    Write-Host "[$AppName] Instalacao concluida com sucesso." -ForegroundColor Green
    Write-Host ""
    Write-Host "  Para definir como navegador predefinido:" -ForegroundColor White
    Write-Host "  Definicoes > Aplicacoes > Aplicacoes predefinidas > Navegador Web" -ForegroundColor White
} else {
    Write-Warning "[$AppName] Instalacao pode ter falhado - chave de registry nao encontrada."
}

Write-Host ""
Invoke-Verify
Read-Host "`nPressione Enter para fechar"
