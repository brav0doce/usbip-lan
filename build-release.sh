#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
WIN_RELEASE_DIR="$ROOT_DIR/release/windows"

echo "Building Android release..."
"$ROOT_DIR/build-android.sh"

mkdir -p "$WIN_RELEASE_DIR"
rm -f "$ROOT_DIR/release/android/usbip-server-release.apk"
rm -f "$WIN_RELEASE_DIR"/*.pdb "$WIN_RELEASE_DIR"/*.zip

echo "Preparing Windows release package..."

# Prefer a fresh Windows build if PowerShell is available on a Windows host.
if command -v powershell.exe >/dev/null 2>&1; then
    powershell.exe -ExecutionPolicy Bypass -File "$ROOT_DIR/build-windows.ps1"
fi

WIN_EXE_SRC="$ROOT_DIR/windows-client/bin/Release/net6.0-windows/win-x64/publish/USBIPClient.exe"
if [ -f "$ROOT_DIR/release/windows/USBIPClient.exe" ]; then
    WIN_EXE_SRC="$ROOT_DIR/release/windows/USBIPClient.exe"
elif [ -f "$ROOT_DIR/windows-client/bin/Release/net6.0-windows/win-x64/USBIPClient.exe" ]; then
    WIN_EXE_SRC="$ROOT_DIR/windows-client/bin/Release/net6.0-windows/win-x64/USBIPClient.exe"
fi

if [ ! -f "$WIN_EXE_SRC" ]; then
    echo "Windows EXE not found. Expected at: $WIN_EXE_SRC"
    echo "Build on Windows with build-windows.ps1 and rerun this script."
    exit 1
fi

if [ "$WIN_EXE_SRC" != "$WIN_RELEASE_DIR/USBIPClient.exe" ]; then
    cp "$WIN_EXE_SRC" "$WIN_RELEASE_DIR/USBIPClient.exe"
fi
cp "$ROOT_DIR/windows-client/Install.ps1" "$WIN_RELEASE_DIR/Install.ps1"

if command -v zip >/dev/null 2>&1; then
    (cd "$WIN_RELEASE_DIR" && zip -9 -q -r usbip-client-windows-x64-release.zip USBIPClient.exe Install.ps1)
    echo "Windows installer bundle: release/windows/usbip-client-windows-x64-release.zip"
else
    echo "zip command not found; left unpacked files in release/windows/."
fi

echo ""
echo "Release artifacts generated:"
echo "  - release/android/usbip-server-release.apk"
echo "  - release/windows/USBIPClient.exe"
echo "  - release/windows/Install.ps1"
