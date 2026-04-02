# ─────────────────────────────────────────────────────────────────────────────
# build-windows.ps1 – builds the Windows USB/IP LAN Client EXE
# ─────────────────────────────────────────────────────────────────────────────
<#
.SYNOPSIS
    Builds the USB/IP LAN Client for Windows 10/11.

.NOTES
    Requires .NET 6 SDK: https://dotnet.microsoft.com/download/dotnet/6.0
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "windows-client"
$outDir     = Join-Path $PSScriptRoot "release\windows"
$usbipInstaller = Join-Path $outDir "usbip-win2-installer.exe"

Write-Host "`n  Building USB/IP LAN Client …`n"

# Restore packages
dotnet restore "$projectDir\UsbIpClient.csproj"

Write-Host "`n  Downloading usbip-win2 installer …`n"
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/vadimgrn/usbip-win2/releases/latest" -Headers @{ "User-Agent" = "usbip-lan-build/1.0" }
    $asset = $release.assets | Where-Object { $_.name -like "*x64-release.exe" } | Select-Object -First 1
    if ($asset) {
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $usbipInstaller -UseBasicParsing
        Write-Host "  ✓ usbip-win2 installer cached at $usbipInstaller" -ForegroundColor Green
    }
    else {
        Write-Host "  ! No x64 release asset found for usbip-win2; Install.ps1 will download it at install time." -ForegroundColor Yellow
    }
}
catch {
    Write-Host "  ! Could not cache usbip-win2 installer now; Install.ps1 will download it if needed." -ForegroundColor Yellow
}

# Build + publish as single EXE
dotnet publish "$projectDir\UsbIpClient.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $outDir `
    /p:EnableWindowsTargeting=true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=false

$exe = Join-Path $outDir "USBIPClient.exe"
if (Test-Path $exe) {
    Write-Host "`n  ✓ EXE built: $exe" -ForegroundColor Green
    # Copy the installer script alongside the EXE
    Copy-Item "$projectDir\Install.ps1" $outDir -Force
    Write-Host "  ✓ Install.ps1 copied to $outDir"

    Remove-Item (Join-Path $outDir '*.pdb') -Force -ErrorAction SilentlyContinue

    $zipPath = Join-Path $PSScriptRoot "release\windows\usbip-client-windows-x64-release.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $archiveItems = @("$outDir\USBIPClient.exe", "$outDir\Install.ps1")
    if (Test-Path $usbipInstaller) {
        $archiveItems += $usbipInstaller
    }
    Compress-Archive -Path $archiveItems -DestinationPath $zipPath -Force
    Write-Host "  ✓ Installer bundle created: $zipPath"
} else {
    Write-Host "  Build failed – EXE not found." -ForegroundColor Red
    exit 1
}
