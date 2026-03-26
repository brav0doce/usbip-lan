#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# build-android.sh – builds the Android USB/IP Server APK
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

cd "$(dirname "$0")/android-server"

echo "Building USB/IP Server APK …"

chmod +x ./gradlew

# Debug build (fast)
./gradlew assembleDebug

APK_PATH=$(find . -name "*.apk" -path "*/debug/*" | head -1)
echo ""
echo "✓ APK built: $APK_PATH"
echo ""
echo "Install on device:"
echo "  adb install -r $APK_PATH"
