#!/usr/bin/env bash
# Builds maui-forge as a self-contained binary and installs it to ~/.local/bin

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/MauiForge/MauiForge.csproj"
OUT_DIR="$SCRIPT_DIR/dist"
BIN_DIR="$HOME/.local/bin"

# Detect architecture
ARCH="$(uname -m)"
if [[ "$ARCH" == "arm64" ]]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

echo "Building maui-forge for $RID..."

dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    --output "$OUT_DIR" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

if [[ ! -f "$OUT_DIR/maui-forge" ]]; then
    echo "Build failed — binary not found."
    exit 1
fi

chmod +x "$OUT_DIR/maui-forge"

# Symlink into ~/.local/bin (no sudo needed)
mkdir -p "$BIN_DIR"
ln -sf "$OUT_DIR/maui-forge" "$BIN_DIR/maui-forge"

# Add to PATH if not already there
SHELL_RC=""
if [[ "$SHELL" == */zsh ]]; then
    SHELL_RC="$HOME/.zshrc"
elif [[ "$SHELL" == */bash ]]; then
    SHELL_RC="$HOME/.bash_profile"
fi

if [[ -n "$SHELL_RC" ]] && ! grep -q 'local/bin' "$SHELL_RC" 2>/dev/null; then
    echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$SHELL_RC"
    echo "Added ~/.local/bin to PATH in $SHELL_RC"
fi

echo "Done. Run: maui-forge"
echo "(Restart terminal or: source $SHELL_RC)"
