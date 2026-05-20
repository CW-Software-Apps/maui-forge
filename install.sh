#!/usr/bin/env bash
# Installs maui-forge from NuGet and ensures ~/.dotnet/tools is on PATH

set -euo pipefail

TOOLS_DIR="$HOME/.dotnet/tools"

echo "Installing maui-forge..."
DOTNET_CLI_UI_LANGUAGE=en-US dotnet tool install -g CWSoftware.MauiForge 2>&1 | grep -v "^$" || \
DOTNET_CLI_UI_LANGUAGE=en-US dotnet tool update -g CWSoftware.MauiForge

# Detect shell profile
if [[ "$SHELL" == */zsh ]]; then
    PROFILES=("$HOME/.zprofile" "$HOME/.zshrc")
elif [[ "$SHELL" == */bash ]]; then
    PROFILES=("$HOME/.bash_profile" "$HOME/.bashrc")
else
    PROFILES=("$HOME/.profile")
fi

# Check if tools dir is already in PATH
if echo "$PATH" | grep -q "$TOOLS_DIR"; then
    echo ""
    echo "✓ maui-forge installed. Run: maui-forge"
    exit 0
fi

# Check if any profile already exports it
PROFILE_HAS_TOOLS=false
for f in "${PROFILES[@]}"; do
    if [[ -f "$f" ]] && grep -q "$TOOLS_DIR" "$f" 2>/dev/null; then
        PROFILE_HAS_TOOLS=true
        break
    fi
done

TARGET_PROFILE="${PROFILES[0]}"

if ! $PROFILE_HAS_TOOLS; then
    echo ""
    echo "Adding ~/.dotnet/tools to PATH in $TARGET_PROFILE ..."
    cat >> "$TARGET_PROFILE" << EOF

# .NET tools
export PATH="\$PATH:$TOOLS_DIR"
EOF
    echo "Done."
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo " maui-forge installed successfully!"
echo ""
echo " To use it now, run:"
echo "   source $TARGET_PROFILE"
echo ""
echo " Or open a new terminal and type:"
echo "   maui-forge"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
