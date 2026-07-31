#!/usr/bin/env bash
# Installs maui-forge from NuGet, sets up PATH, and creates a Desktop launcher.
#
# Windows equivalent: install.ps1

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TOOLS_DIR="$HOME/.dotnet/tools"
APP_SUPPORT="$HOME/Library/Application Support/MauiForge"
DESKTOP="$HOME/Desktop"
COMMAND_FILE="$DESKTOP/MAUI Forge.command"

echo ""
echo "  ╔══════════════════════════════════════╗"
echo "  ║       MAUI Forge Installer           ║"
echo "  ╚══════════════════════════════════════╝"
echo ""

# ── 1. Install / update dotnet tool ──────────────────────────
echo "  [1/4] Installing dotnet tool..."
DOTNET_CLI_UI_LANGUAGE=en-US dotnet tool install -g CWSoftware.MauiForge 2>/dev/null || \
DOTNET_CLI_UI_LANGUAGE=en-US dotnet tool update -g CWSoftware.MauiForge 2>/dev/null || true
echo "         ✓ dotnet tool ready"

# ── 2. Copy icon to permanent location ────────────────────────
echo "  [2/4] Setting up icon..."
mkdir -p "$APP_SUPPORT"

# Prefer .icns (native Mac format), fall back to .png
if [[ -f "$SCRIPT_DIR/assets/icon.icns" ]]; then
    cp "$SCRIPT_DIR/assets/icon.icns" "$APP_SUPPORT/icon.icns"
    echo "         ✓ icon.icns copied"
elif [[ -f "$SCRIPT_DIR/assets/icon.png" ]]; then
    cp "$SCRIPT_DIR/assets/icon.png" "$APP_SUPPORT/icon.png"
    echo "         ✓ icon.png copied"
else
    echo "         ⚠ No icon found — skipping"
fi

# ── 3. Create Desktop launcher (.command) ─────────────────────
echo "  [3/4] Creating Desktop launcher..."

cat > "$COMMAND_FILE" << 'LAUNCHER'
#!/bin/bash
# MAUI Forge Launcher — starts the web dashboard and opens your browser.
# Drag this file to the Dock for one-click access.

# Ensure dotnet tools are on PATH
export PATH="$HOME/.dotnet/tools:$PATH"

# Kill any existing instance (avoid port conflicts)
pkill -f "maui-forge" 2>/dev/null || true

# Start maui-forge in the background
nohup maui-forge --web > /dev/null 2>&1 &

# Wait briefly for the server to start, then open browser
sleep 2
open "http://localhost:5123" 2>/dev/null || true

echo "MAUI Forge is running at http://localhost:5123"
LAUNCHER

chmod +x "$COMMAND_FILE"
echo "         ✓ Launcher created on Desktop"

# ── 4. Set up PATH in shell profile ───────────────────────────
echo "  [4/4] Configuring PATH..."

if [[ "$SHELL" == */zsh ]]; then
    PROFILES=("$HOME/.zprofile" "$HOME/.zshrc")
elif [[ "$SHELL" == */bash ]]; then
    PROFILES=("$HOME/.bash_profile" "$HOME/.bashrc")
else
    PROFILES=("$HOME/.profile")
fi

if ! echo "$PATH" | grep -q "$TOOLS_DIR"; then
    PROFILE_HAS_TOOLS=false
    for f in "${PROFILES[@]}"; do
        if [[ -f "$f" ]] && grep -q "$TOOLS_DIR" "$f" 2>/dev/null; then
            PROFILE_HAS_TOOLS=true
            break
        fi
    done

    if ! $PROFILE_HAS_TOOLS; then
        TARGET_PROFILE="${PROFILES[0]}"
        cat >> "$TARGET_PROFILE" << 'EOF'

# .NET tools
export PATH="$PATH:$HOME/.dotnet/tools"
EOF
        echo "         ✓ Added ~/.dotnet/tools to $TARGET_PROFILE"
    else
        echo "         ✓ PATH already configured"
    fi
fi

# ── Done ──────────────────────────────────────────────────────
echo ""
echo "  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "   MAUI Forge installed successfully!"
echo ""
echo "   🚀 Launch now:"
echo "      Double-click 'MAUI Forge.command' on your Desktop"
echo ""
echo "   💡 Tip: Drag MAUI Forge.command to your Dock"
echo "           for one-click access."
echo ""
echo "   📟 Or run from terminal anytime:"
echo "      maui-forge"
echo "  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Auto-open on first install
if [[ -f "$COMMAND_FILE" ]]; then
    echo "   Opening MAUI Forge now..."
    open "$COMMAND_FILE" 2>/dev/null || true
fi
