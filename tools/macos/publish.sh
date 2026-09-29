#!/bin/bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
    echo "Run this script on macOS (Apple codesign is required)." >&2
    exit 1
fi

rid="${1:-osx-arm64}"
case "$rid" in
    osx-arm64|osx-x64) ;;
    *) echo "Usage: bash tools/macos/publish.sh [osx-arm64|osx-x64]" >&2; exit 1 ;;
esac

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_dir="$(cd "$script_dir/../.." && pwd)"
output_dir="$repo_dir/artifacts/publish/$rid-bundle"
mkdir -p "$output_dir"
# Use a new directory on every run so removed dependencies cannot survive a rebuild.
stage_dir="$(mktemp -d "$output_dir/build.XXXXXX")"
bundle="$stage_dir/OsuMusicPlayer.app"
mkdir -p "$bundle/Contents/MacOS"
payload="$bundle/Contents/Resources/payload"

dotnet publish "$repo_dir/src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj" \
    -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=false -p:PublishTrimmed=false \
    -o "$payload"
cp "$script_dir/Info.plist" "$bundle/Contents/Info.plist"
chmod +x "$payload/OsuMusicPlayer.App"
# Managed assemblies and PDBs are resources, not nested Mach-O code bundles.
cat > "$bundle/Contents/MacOS/OsuMusicPlayer" <<'LAUNCHER'
#!/bin/sh
bundle_macos_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec "$bundle_macos_dir/../Resources/payload/OsuMusicPlayer.App" "$@"
LAUNCHER
chmod +x "$bundle/Contents/MacOS/OsuMusicPlayer"

identity="${MACOS_SIGNING_IDENTITY:--}"
sign_options=(--force --sign "$identity")
if [[ "$identity" != - ]]; then
    sign_options+=(--timestamp --options runtime)
fi

# Sign native dependencies before the outer bundle; do not use --deep to sign.
while IFS= read -r -d '' native_file; do
    if /usr/bin/file -b "$native_file" | /usr/bin/grep -q 'Mach-O'; then
        codesign "${sign_options[@]}" "$native_file"
    fi
done < <(/usr/bin/find "$payload" -type f ! -name OsuMusicPlayer.App -print0)
codesign "${sign_options[@]}" --entitlements "$script_dir/entitlements.plist" "$payload/OsuMusicPlayer.App"
codesign "${sign_options[@]}" "$bundle"
codesign --verify --deep --strict --verbose=2 "$bundle"

archive="$stage_dir/OsuMusicPlayer-$rid.zip"
ditto -c -k --keepParent "$bundle" "$archive"
echo "App: $bundle"
echo "Archive: $archive"
if [[ "$identity" == - ]]; then
    echo "Local ad-hoc build: Gatekeeper may require explicit approval in Privacy & Security."
    echo "For distribution, set MACOS_SIGNING_IDENTITY to a Developer ID Application certificate, then notarize."
fi
