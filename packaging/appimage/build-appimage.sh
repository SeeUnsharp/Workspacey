#!/usr/bin/env bash
# Builds a self-contained Workspacey AppImage: artifacts/Workspacey-<version>-x86_64.AppImage
#
# Requirements: .NET 10 SDK, curl, sha256sum. appimagetool is downloaded (pinned + checksum verified)
# into artifacts/tools on first run; set APPIMAGETOOL=/path/to/appimagetool to use your own instead.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PACKAGING="$ROOT/packaging/appimage"
ARTIFACTS="$ROOT/artifacts"
APPDIR="$ARTIFACTS/appimage/Workspacey.AppDir"

RID="linux-x64"
ARCH="x86_64"

APPIMAGETOOL_VERSION="1.9.1"
# SHA-256 of appimagetool-x86_64.AppImage 1.9.1. Upstream publishes no checksum; this one was
# recorded when the tool was first downloaded (trust on first use). A mismatch aborts the build.
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"

VERSION="$(dotnet msbuild "$ROOT/src/Workspacey.App/Workspacey.App.csproj" -getProperty:Version)"
OUTPUT="$ARTIFACTS/Workspacey-$VERSION-$ARCH.AppImage"

echo "==> Publishing Workspacey $VERSION ($RID, self-contained)"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
dotnet publish "$ROOT/src/Workspacey.App/Workspacey.App.csproj" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:DebugType=none \
    --output "$APPDIR/usr/bin"

echo "==> Assembling AppDir"
install -m 755 "$PACKAGING/AppRun" "$APPDIR/AppRun"
install -m 644 "$PACKAGING/workspacey.desktop" "$APPDIR/workspacey.desktop"
install -m 644 "$ROOT/src/Workspacey.App/Assets/workspacey.svg" "$APPDIR/workspacey.svg"
install -D -m 644 "$ROOT/src/Workspacey.App/Assets/workspacey.svg" \
    "$APPDIR/usr/share/icons/hicolor/scalable/apps/workspacey.svg"
install -D -m 644 "$PACKAGING/workspacey.desktop" "$APPDIR/usr/share/applications/workspacey.desktop"
ln -sf workspacey.svg "$APPDIR/.DirIcon"

if [[ -z "${APPIMAGETOOL:-}" ]]; then
    APPIMAGETOOL="$ARTIFACTS/tools/appimagetool-$APPIMAGETOOL_VERSION-$ARCH.AppImage"
    if [[ ! -x "$APPIMAGETOOL" ]]; then
        echo "==> Downloading appimagetool $APPIMAGETOOL_VERSION"
        mkdir -p "$(dirname "$APPIMAGETOOL")"
        curl -fsSL -o "$APPIMAGETOOL.part" \
            "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-$ARCH.AppImage"
        echo "$APPIMAGETOOL_SHA256  $APPIMAGETOOL.part" | sha256sum --check --quiet
        chmod +x "$APPIMAGETOOL.part"
        mv "$APPIMAGETOOL.part" "$APPIMAGETOOL"
    fi
fi

echo "==> Building $OUTPUT"
# Extract-and-run: works on systems without FUSE (containers, CI).
ARCH="$ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" --no-appstream "$APPDIR" "$OUTPUT"

echo "==> Done: $OUTPUT"
