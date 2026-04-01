#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# build-android.sh – builds the Android USB/IP Server APK safely avoiding VB shared folder IO locks
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
TMP_BUILD_DIR="/tmp/android-build-tmp"

echo "Setting up temporary isolated build directory..."

rm -rf "$TMP_BUILD_DIR"
mkdir -p "$TMP_BUILD_DIR"

# Copy sources only. Excluding local Gradle/build caches avoids long stalls
# on shared folders and keeps the temp build deterministic.
(
	cd "$ROOT_DIR/android-server"
	tar --exclude='.gradle' --exclude='build' --exclude='app/build' -cf - .
) | (
	cd "$TMP_BUILD_DIR"
	tar -xf -
)

cd "$TMP_BUILD_DIR"
chmod +x ./gradlew

echo "Compiling the App (Release Mode)..."
./gradlew assembleRelease --no-daemon

mkdir -p "$ROOT_DIR/release/android"
rm -f "$ROOT_DIR/release/android/usbip-server-release.apk"

APK_SRC="app/build/outputs/apk/release/app-release.apk"
if [ ! -f "$APK_SRC" ]; then
	APK_SRC="app/build/outputs/apk/release/app-release-unsigned.apk"
fi

cp "$APK_SRC" "$ROOT_DIR/release/android/usbip-server-release.apk"

echo ""
echo "✓ APK built safely!"
echo "Install on your device using adb:"
echo "  adb install -r release/android/usbip-server-release.apk"
