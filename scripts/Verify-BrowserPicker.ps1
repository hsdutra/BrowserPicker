# Verify-BrowserPicker.ps1
# Verifica o estado actual do BrowserPicker e emite um veredito.
# Cobre todos os checks de instalacao e desinstalacao num unico script.

#Requires -Version 5.1
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

# Registar drive HKCR: (nao existe por omissao)
if (-not (Get-PSDrive -Name HKCR -ErrorAction SilentlyContinue)) {
    New-PSDrive -Name HKCR -PSProvider Registry -Root HKEY_CLASSES_ROOT | Out-Null
}

$AppName = 'BrowserPicker'
$ExePath = "C:\Program Files\BrowserPicker\BrowserPicker.exe"

# ---------------------------------------------------------------------------
# Acumulador de checks
# ---------------------------------------------------------------------------
$checks = [System.Collections.Generic.List[PSCustomObject]]::new()

function Add-Check {
    param([string]$Group, [string]$Name, [scriptblock]$Test)
    try   { $ok = [bool]($Test.Invoke()) }
    catch { $ok = $false }
    $script:checks.Add([PSCustomObject]@{ Group = $Group; Name = $Name; OK = $ok })
}

# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------

# Processo
Add-Check "Processo" "BrowserPicker em execucao na bandeja" {
    $null -ne (Get-Process -Name $AppName -ErrorAction SilentlyContinue)
}

# Ficheiros
Add-Check "Ficheiros" "BrowserPicker.exe existe em Program Files" {
    Test-Path "C:\Program Files\BrowserPicker\BrowserPicker.exe"
}
Add-Check "Ficheiros" "Pasta BrowserPicker existe em Program Files" {
    Test-Path "C:\Program Files\BrowserPicker"
}

# Autostart
Add-Check "Autostart" "Iniciar com o Windows (HKCU\Run)" {
    $null -ne (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
               -Name $AppName -ErrorAction SilentlyContinue)
}

# Registry: HKLM\SOFTWARE\BrowserPicker
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "Chave BrowserPicker existe" {
    Test-Path "HKLM:\SOFTWARE\BrowserPicker"
}
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "Valor DisplayName existe" {
    (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker" -Name "DisplayName" -ErrorAction Stop).DisplayName -ne $null
}
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "Valor Description existe" {
    (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker" -Name "Description" -ErrorAction Stop).Description -ne $null
}
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker" "Valor ApplicationIcon existe" {
    (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker" -Name "ApplicationIcon" -ErrorAction Stop).ApplicationIcon -ne $null
}

# Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities" "Subchave Capabilities existe" {
    Test-Path "HKLM:\SOFTWARE\BrowserPicker\Capabilities"
}
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities" "Valor ApplicationName existe" {
    (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker\Capabilities" -Name "ApplicationName" -ErrorAction Stop).ApplicationName -ne $null
}
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities" "Valor ApplicationDescription existe" {
    (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker\Capabilities" -Name "ApplicationDescription" -ErrorAction Stop).ApplicationDescription -ne $null
}

# Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities\URLAssociations
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities\URLAssociations" "Subchave URLAssociations existe" {
    Test-Path "HKLM:\SOFTWARE\BrowserPicker\Capabilities\URLAssociations"
}
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities\URLAssociations" "Protocolo http aponta para BrowserPickerProtocol" {
    (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker\Capabilities\URLAssociations" -Name "http" -ErrorAction Stop).http -eq "BrowserPickerProtocol"
}
Add-Check "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities\URLAssociations" "Protocolo https aponta para BrowserPickerProtocol" {
    (Get-ItemProperty "HKLM:\SOFTWARE\BrowserPicker\Capabilities\URLAssociations" -Name "https" -ErrorAction Stop).https -eq "BrowserPickerProtocol"
}

# Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol
Add-Check "Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol" "Chave BrowserPickerProtocol existe (HKLM)" {
    Test-Path "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol"
}
Add-Check "Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol" "Subchave shell\open\command existe (HKLM)" {
    Test-Path "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command"
}
Add-Check "Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol" "Comando aponta para BrowserPicker.exe (HKLM)" {
    $cmd = (Get-ItemProperty "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command" -Name "(Default)" -ErrorAction Stop)."(Default)"
    $cmd -like "*BrowserPicker.exe*"
}
Add-Check "Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol" "EditFlags = 2 (HKLM)" {
    (Get-ItemProperty "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol" -Name "EditFlags" -ErrorAction Stop).EditFlags -eq 2
}
Add-Check "Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol" "DefaultIcon registado (HKLM)" {
    Test-Path "HKLM:\SOFTWARE\Classes\BrowserPickerProtocol\DefaultIcon"
}

# Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol (obrigatorio para Windows 11 UserChoice)
Add-Check "Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol" "Chave BrowserPickerProtocol existe (HKCU)" {
    Test-Path "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol"
}
Add-Check "Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol" "Subchave shell\open\command existe (HKCU)" {
    Test-Path "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command"
}
Add-Check "Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol" "Comando aponta para BrowserPicker.exe (HKCU)" {
    $cmd = (Get-ItemProperty "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command" -Name "(Default)" -ErrorAction Stop)."(Default)"
    $cmd -like "*BrowserPicker.exe*"
}
Add-Check "Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol" "EditFlags = 2 (HKCU)" {
    (Get-ItemProperty "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol" -Name "EditFlags" -ErrorAction Stop).EditFlags -eq 2
}
Add-Check "Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol" "DefaultIcon registado (HKCU)" {
    Test-Path "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol\DefaultIcon"
}
Add-Check "Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol" "Comando inclui argumento %1 para a URL (HKCU)" {
    $cmd = (Get-ItemProperty "HKCU:\SOFTWARE\Classes\BrowserPickerProtocol\shell\open\command" -Name "(Default)" -ErrorAction Stop)."(Default)"
    $cmd -like '*"%1"*'
}

# Registry: HKCR
Add-Check "Registry: HKCR\BrowserPickerProtocol" "Subchave shell\open\command registada (HKCR)" {
    Test-Path "HKCR:\BrowserPickerProtocol\shell\open\command"
}

# Registry: HKLM\SOFTWARE\RegisteredApplications
Add-Check "Registry: HKLM\SOFTWARE\RegisteredApplications" "Entrada BrowserPicker em RegisteredApplications" {
    (Get-ItemProperty "HKLM:\SOFTWARE\RegisteredApplications" -Name $AppName -ErrorAction Stop).$AppName -ne $null
}
Add-Check "Registry: HKLM\SOFTWARE\RegisteredApplications" "Entrada aponta para Software\BrowserPicker\Capabilities" {
    (Get-ItemProperty "HKLM:\SOFTWARE\RegisteredApplications" -Name $AppName -ErrorAction Stop).$AppName -eq "Software\BrowserPicker\Capabilities"
}

# Registry: HKLM\SOFTWARE\Clients\StartMenuInternet\BrowserPicker
Add-Check "Registry: HKLM\SOFTWARE\Clients\StartMenuInternet\BrowserPicker" "Chave StartMenuInternet\BrowserPicker existe" {
    Test-Path "HKLM:\SOFTWARE\Clients\StartMenuInternet\BrowserPicker"
}
Add-Check "Registry: HKLM\SOFTWARE\Clients\StartMenuInternet\BrowserPicker" "DefaultIcon registado" {
    Test-Path "HKLM:\SOFTWARE\Clients\StartMenuInternet\BrowserPicker\DefaultIcon"
}
Add-Check "Registry: HKLM\SOFTWARE\Clients\StartMenuInternet\BrowserPicker" "shell\open\command registado" {
    Test-Path "HKLM:\SOFTWARE\Clients\StartMenuInternet\BrowserPicker\shell\open\command"
}
Add-Check "Registry: HKLM\SOFTWARE\Clients\StartMenuInternet\BrowserPicker" "Capabilities\URLAssociations\http registado" {
    (Get-ItemProperty "HKLM:\SOFTWARE\Clients\StartMenuInternet\BrowserPicker\Capabilities\URLAssociations" -Name "http" -ErrorAction Stop).http -eq "BrowserPickerProtocol"
}
Add-Check "Registry: HKLM\SOFTWARE\Clients\StartMenuInternet\BrowserPicker" "Capabilities\URLAssociations\https registado" {
    (Get-ItemProperty "HKLM:\SOFTWARE\Clients\StartMenuInternet\BrowserPicker\Capabilities\URLAssociations" -Name "https" -ErrorAction Stop).https -eq "BrowserPickerProtocol"
}

# ---------------------------------------------------------------------------
# Exibir resultados
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  BrowserPicker -- Estado de Instalacao"        -ForegroundColor Cyan
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

# ---------------------------------------------------------------------------
# Calcular metricas para o veredito
# ---------------------------------------------------------------------------
$exePresente   = ($checks | Where-Object { $_.Name -like "*BrowserPicker.exe*" } | Select-Object -First 1).OK
$processoAtivo = ($checks | Where-Object { $_.Group -eq "Processo" } | Select-Object -First 1).OK

$gruposRegistry = @(
    "Registry: HKLM\SOFTWARE\BrowserPicker",
    "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities",
    "Registry: HKLM\SOFTWARE\BrowserPicker\Capabilities\URLAssociations",
    "Registry: HKLM\SOFTWARE\Classes\BrowserPickerProtocol",
    "Registry: HKCU\SOFTWARE\Classes\BrowserPickerProtocol",
    "Registry: HKCR\BrowserPickerProtocol",
    "Registry: HKLM\SOFTWARE\RegisteredApplications",
    "Registry: HKLM\SOFTWARE\Clients\StartMenuInternet\BrowserPicker"
)
$checksRegistry   = @($checks | Where-Object { $_.Group -in $gruposRegistry })
$registryOK       = @($checksRegistry | Where-Object { $_.OK }).Count
$registryTotal    = $checksRegistry.Count
$registryPresente = $registryOK -gt 0
$registryCompleto = $registryOK -ge ($registryTotal - 1)   # tolera 1 falha menor

$total  = $checks.Count
$passed = @($checks | Where-Object { $_.OK }).Count
$failed = $total - $passed

# ---------------------------------------------------------------------------
# Veredito
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ("  RESULTADO: $passed/$total verificacoes passaram") -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Yellow' })
Write-Host ""
Write-Host "  VEREDITO" -ForegroundColor Cyan
Write-Host "------------------------------------------------" -ForegroundColor Cyan

if ($exePresente -and $registryCompleto -and $processoAtivo) {
    Write-Host "  INSTALADO E APTO PARA FUNCIONAR" -ForegroundColor Green
    Write-Host ""
    Write-Host "  O BrowserPicker esta instalado, correctamente registado" -ForegroundColor White
    Write-Host "  e em execucao na bandeja do sistema." -ForegroundColor White
    Write-Host ""
    Write-Host "  Proximo passo: va a Definicoes > Aplicativos > Aplicativos" -ForegroundColor White
    Write-Host "  padrao e defina BrowserPicker como navegador padrao." -ForegroundColor White

} elseif ($exePresente -and $registryCompleto -and -not $processoAtivo) {
    Write-Host "  INSTALADO MAS NAO EM EXECUCAO" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  O BrowserPicker esta instalado e registado," -ForegroundColor White
    Write-Host "  mas o processo nao esta em execucao." -ForegroundColor White
    Write-Host "  Inicie o programa ou reinstale para restaurar o autostart." -ForegroundColor White

} elseif ($exePresente -or $registryPresente) {
    Write-Host "  INSTALADO MAS NAO APTO PARA FUNCIONAR" -ForegroundColor Yellow
    Write-Host ""
    if (-not $exePresente)   { Write-Host "  ! Executavel em falta em Program Files."   -ForegroundColor Yellow }
    if (-not $processoAtivo) { Write-Host "  ! Processo nao esta em execucao."          -ForegroundColor Yellow }
    $ausentes = $checksRegistry | Where-Object { -not $_.OK }
    foreach ($a in $ausentes) {
        Write-Host "  ! Ausente: $($a.Name)" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "  Execute o instalador para corrigir a instalacao." -ForegroundColor White

} elseif (-not $exePresente -and -not $processoAtivo -and $registryPresente) {
    Write-Host "  DESINSTALADO COM VESTIGIOS ($registryOK entrada(s) de registry)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  O executavel e o processo foram removidos, mas existem" -ForegroundColor White
    Write-Host "  entradas de registry que nao foram totalmente limpas:" -ForegroundColor White
    $vestKeys = @($checksRegistry | Where-Object { $_.OK })
    foreach ($v in $vestKeys) {
        Write-Host "  > $($v.Name)" -ForegroundColor DarkYellow
    }
    Write-Host ""
    Write-Host "  Nota: se o BrowserPicker estava definido como navegador" -ForegroundColor White
    Write-Host "  padrao, o Windows deve ter pedido para escolher um substituto." -ForegroundColor White

} else {
    Write-Host "  COMPLETAMENTE DESINSTALADO" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Nenhum ficheiro, processo ou entrada de registry detectada." -ForegroundColor White
    Write-Host ""
    Write-Host "  Nota: se o BrowserPicker estava definido como navegador" -ForegroundColor White
    Write-Host "  padrao, o Windows deve ter pedido para escolher um substituto." -ForegroundColor White
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
