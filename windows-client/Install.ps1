#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Instalador de USB/IP LAN Client para Windows 10/11.

.DESCRIPTION
    Este script:
      1. Descarga e instala el driver usbip-win2 (kernel driver para USB/IP en Windows).
      2. Copia el ejecutable USBIPClient.exe al directorio de instalación.
      3. Crea un acceso directo en el escritorio.
      4. (Opcional) Configura el inicio automático.

    Requisitos:
      - Windows 10 versión 1903 o superior
      - PowerShell 5.1+
      - Conexión a Internet para descargar usbip-win2

.NOTES
    usbip-win2 de Vadim Grn: https://github.com/vadimgrn/usbip-win2
#>

[CmdletBinding()]
param(
    [string]$InstallDir  = "$env:ProgramFiles\USBIPClient",
    [switch]$Silent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─── helpers ──────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) {
    Write-Host "`n  [*] $msg" -ForegroundColor Cyan
}

function Write-OK([string]$msg) {
    Write-Host "      OK  $msg" -ForegroundColor Green
}

function Write-Fail([string]$msg) {
    Write-Host "      ERR $msg" -ForegroundColor Red
}

function Test-Admin {
    $cur = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal] $cur).IsInRole(
        [Security.Principal.WindowsBuiltInRole] "Administrator")
}

# ─── checks ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ╔══════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "  ║   USB/IP LAN Client – Instalador     ║" -ForegroundColor Magenta
Write-Host "  ╚══════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

if (-not (Test-Admin)) {
    Write-Fail "Este script debe ejecutarse como Administrador."
    exit 1
}

$osInfo = Get-CimInstance Win32_OperatingSystem
Write-Host "  Sistema: $($osInfo.Caption) Build $($osInfo.BuildNumber)"
if ([int]$osInfo.BuildNumber -lt 18362) {
    Write-Fail "Se requiere Windows 10 versión 1903 (Build 18362) o superior."
    exit 1
}

# ─── create install dir ───────────────────────────────────────────────────────

Write-Step "Creando directorio de instalación: $InstallDir"
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}
Write-OK "Directorio listo."

# ─── download usbip-win2 ──────────────────────────────────────────────────────

Write-Step "Descargando usbip-win2 …"

$apiUrl  = "https://api.github.com/repos/vadimgrn/usbip-win2/releases/latest"
$headers = @{ "User-Agent" = "usbip-lan-installer/1.0" }

try {
    $release  = Invoke-RestMethod -Uri $apiUrl -Headers $headers
    $asset    = $release.assets | Where-Object { $_.name -like "*x64*.zip" -or $_.name -like "*.zip" } | Select-Object -First 1

    if (-not $asset) {
        Write-Fail "No se encontró el archivo ZIP en la última release de usbip-win2."
        Write-Host "  Descarga manualmente desde: https://github.com/vadimgrn/usbip-win2/releases"
        exit 1
    }

    $zipPath = Join-Path $env:TEMP "usbip-win2.zip"
    Write-Host "      Descargando $($asset.name) …"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing

    Write-Step "Extrayendo archivos de usbip-win2 …"
    $extractDir = Join-Path $env:TEMP "usbip-win2-extracted"
    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $extractDir

    # Copy usbip.exe and driver files
    Get-ChildItem $extractDir -Recurse -Include "usbip.exe","*.sys","*.inf","*.cat" |
        ForEach-Object {
            Copy-Item $_.FullName $InstallDir -Force
        }

    Write-OK "usbip-win2 instalado."

} catch {
    Write-Fail "Error descargando usbip-win2: $_"
    Write-Host ""
    Write-Host "  Instalación manual de usbip-win2:" -ForegroundColor Yellow
    Write-Host "    1. Ve a https://github.com/vadimgrn/usbip-win2/releases"
    Write-Host "    2. Descarga el archivo ZIP más reciente (x64)"
    Write-Host "    3. Extrae y copia usbip.exe y los archivos .sys/.inf/.cat a $InstallDir"
    Write-Host "    4. Ejecuta este script de nuevo."
    Write-Host ""
}

# ─── install kernel driver ────────────────────────────────────────────────────

Write-Step "Instalando driver de kernel USB/IP …"

$infFile = Get-ChildItem $InstallDir -Filter "*.inf" | Select-Object -First 1
if ($infFile) {
    try {
        $result = Start-Process "pnputil.exe" `
            -ArgumentList "/add-driver `"$($infFile.FullName)`" /install" `
            -Wait -PassThru -NoNewWindow

        if ($result.ExitCode -eq 0) {
            Write-OK "Driver instalado correctamente."
        } else {
            Write-Host "      Advertencia: pnputil devolvió código $($result.ExitCode)" -ForegroundColor Yellow
            Write-Host "      El driver puede necesitar firma habilitada (test signing)."
        }
    } catch {
        Write-Host "      No se pudo instalar el driver automáticamente: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "      No se encontró archivo .inf – saltando instalación del driver." -ForegroundColor Yellow
}

# ─── copy client executable ───────────────────────────────────────────────────

Write-Step "Copiando USB/IP LAN Client …"

$srcExe = Join-Path $PSScriptRoot "USBIPClient.exe"
if (Test-Path $srcExe) {
    Copy-Item $srcExe $InstallDir -Force
    Write-OK "USBIPClient.exe copiado."
} else {
    Write-Host "      USBIPClient.exe no encontrado en $PSScriptRoot" -ForegroundColor Yellow
    Write-Host "      Coloca el ejecutable junto a este script e instala de nuevo."
}

# ─── add to PATH ──────────────────────────────────────────────────────────────

Write-Step "Añadiendo $InstallDir al PATH del sistema …"

$currentPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($currentPath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$currentPath;$InstallDir", "Machine")
    Write-OK "PATH actualizado."
} else {
    Write-OK "Ya estaba en el PATH."
}

# ─── desktop shortcut ─────────────────────────────────────────────────────────

Write-Step "Creando acceso directo en el escritorio …"

$desktopPath = [Environment]::GetFolderPath("CommonDesktopDirectory")
$shortcutPath = Join-Path $desktopPath "USB-IP LAN Client.lnk"

$exePath = Join-Path $InstallDir "USBIPClient.exe"
if (Test-Path $exePath) {
    $shell = New-Object -ComObject WScript.Shell
    $sc    = $shell.CreateShortcut($shortcutPath)
    $sc.TargetPath       = $exePath
    $sc.WorkingDirectory = $InstallDir
    $sc.Description      = "USB/IP LAN Client – conecta dispositivos USB de Android vía red"
    $sc.Save()
    Write-OK "Acceso directo creado en el escritorio."
} else {
    Write-Host "      No se pudo crear el acceso directo (ejecutable no encontrado)." -ForegroundColor Yellow
}

# ─── done ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║   Instalación completada.                                    ║" -ForegroundColor Green
Write-Host "  ║                                                              ║" -ForegroundColor Green
Write-Host "  ║   Pasos siguientes:                                          ║" -ForegroundColor Green
Write-Host "  ║     1. Abre la app 'USB/IP Server' en tu Android             ║" -ForegroundColor Green
Write-Host "  ║     2. Activa el servidor con el interruptor                 ║" -ForegroundColor Green
Write-Host "  ║     3. Ejecuta 'USB-IP LAN Client' en Windows                ║" -ForegroundColor Green
Write-Host "  ║     4. Pulsa 'Buscar Servidores' (o se detecta automático)   ║" -ForegroundColor Green
Write-Host "  ║     5. Selecciona un dispositivo y pulsa 'Conectar'          ║" -ForegroundColor Green
Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

if (-not $Silent) {
    $exePath = Join-Path $InstallDir "USBIPClient.exe"
    if (Test-Path $exePath) {
        $ans = Read-Host "  ¿Abrir USB/IP LAN Client ahora? [S/n]"
        if ($ans -ne "n" -and $ans -ne "N") {
            Start-Process $exePath
        }
    }
}
