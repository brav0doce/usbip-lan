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
$outDir     = Join-Path $PSScriptRoot "dist\windows"

Write-Host "`n  Building USB/IP LAN Client …`n"

# Restore packages
dotnet restore "$projectDir\UsbIpClient.csproj"

# Build + publish as single EXE
dotnet publish "$projectDir\UsbIpClient.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $outDir `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=false

$exe = Join-Path $outDir "USBIPClient.exe"
if (Test-Path $exe) {
    Write-Host "`n  ✓ EXE built: $exe" -ForegroundColor Green
    # Copy the installer script alongside the EXE
    Copy-Item "$projectDir\Install.ps1" $outDir -Force
    Write-Host "  ✓ Install.ps1 copied to $outDir"
} else {
    Write-Host "  Build failed – EXE not found." -ForegroundColor Red
    exit 1
}
