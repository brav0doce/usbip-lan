#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
WIN_RELEASE_DIR="$ROOT_DIR/release/windows"
USBIP_INSTALLER="$WIN_RELEASE_DIR/usbip-win2-installer.exe"

echo "Building Android release..."
"$ROOT_DIR/build-android.sh"

mkdir -p "$WIN_RELEASE_DIR"
rm -f "$ROOT_DIR/release/android/usbip-server-release.apk"
rm -f "$WIN_RELEASE_DIR"/*.pdb "$WIN_RELEASE_DIR"/*.zip "$USBIP_INSTALLER"

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

echo "Downloading usbip-win2 installer for the bundle..."
usbip_api_json="$(curl -fsSL -H 'User-Agent: usbip-lan-build/1.0' https://api.github.com/repos/vadimgrn/usbip-win2/releases/latest || true)"
usbip_asset_url="$(printf '%s' "$usbip_api_json" | sed -n 's/.*"browser_download_url": *"\([^"]*USBip-[^"]*-x64-release\.exe\)".*/\1/p' | head -n1)"
if [ -n "$usbip_asset_url" ]; then
    if curl -fsSL "$usbip_asset_url" -o "$USBIP_INSTALLER"; then
        echo "Cached usbip-win2 installer: $USBIP_INSTALLER"
    else
        echo "Could not download usbip-win2 installer; Install.ps1 will fetch it at install time."
    fi
else
    echo "Could not resolve usbip-win2 installer URL; Install.ps1 will fetch it at install time."
fi

if command -v zip >/dev/null 2>&1; then
    bundle_items=(USBIPClient.exe Install.ps1)
    if [ -f "$USBIP_INSTALLER" ]; then
        bundle_items+=(usbip-win2-installer.exe)
    fi
    (cd "$WIN_RELEASE_DIR" && zip -9 -q -r usbip-client-windows-x64-release.zip "${bundle_items[@]}")
    echo "Windows installer bundle: release/windows/usbip-client-windows-x64-release.zip"
else
    echo "zip command not found; left unpacked files in release/windows/."
fi

echo ""
echo "Release artifacts generated:"
echo "  - release/android/usbip-server-release.apk"
echo "  - release/windows/USBIPClient.exe"
echo "  - release/windows/Install.ps1"
