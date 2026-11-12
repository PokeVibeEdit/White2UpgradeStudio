#!/usr/bin/env bash
set -euo pipefail

# Build a self-contained linux-x64 AppImage from the editor project.
# Requires .NET 8, curl, rsvg-convert (librsvg2-bin) and standard Linux build utilities.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
VERSION="${1:-0.0.0-dev}"
DIST="$ROOT/dist"
APPDIR="$ROOT/.appimage-build/White2UpgradeStudio.AppDir"
TOOL="$ROOT/.appimage-build/appimagetool.AppImage"
TARGET="$DIST/White2UpgradeStudio-v${VERSION}-x86_64.AppImage"

mkdir -p "$DIST" "$ROOT/.appimage-build"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

dotnet publish "$ROOT/White2UpgradeEditor/White2UpgradeEditor.csproj" \
    -c Release -r linux-x64 --self-contained true \
    "-p:Version=$VERSION" -o "$APPDIR/usr/bin"

# Avoid writable application state inside AppImage; the filesystem is read-only.
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/usr/bin/env sh
set -eu
HERE="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec "$HERE/usr/bin/White2UpgradeEditor" "$@"
APPRUN
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/White2UpgradeEditor"

cat > "$APPDIR/white2upgrade-studio.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=White 2 Upgrade Studio
Comment=Experimental Pokémon Generation V ROM editor
Exec=White2UpgradeEditor
Icon=white2upgrade-studio
Categories=Development;Education;
Terminal=false
StartupNotify=true
DESKTOP
cp "$APPDIR/white2upgrade-studio.desktop" "$APPDIR/usr/share/applications/white2upgrade-studio.desktop"

rsvg-convert -w 256 -h 256 "$ROOT/assets/white2upgrade-studio.svg" -o "$APPDIR/white2upgrade-studio.png"
cp "$APPDIR/white2upgrade-studio.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/white2upgrade-studio.png"

if [[ ! -f "$TOOL" ]]; then
    curl --fail --location --retry 3 --silent --show-error \
      "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" \
      --output "$TOOL"
    chmod +x "$TOOL"
fi

# AppImage tool is itself an AppImage; extraction mode works in CI without FUSE.
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" "$APPDIR" "$TARGET"
chmod +x "$TARGET"
echo "Created $TARGET"
