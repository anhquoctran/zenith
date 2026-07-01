#!/usr/bin/env bash
#
# Downloads FFmpeg shared libraries for Zenith Screen Recorder.
# Usage: ./download-ffmpeg.sh [platform]
# Platforms: linux-x64 (default), win-x64, osx-arm64
#

set -euo pipefail

PLATFORM="${1:-linux-x64}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
LIB_DIR="$PROJECT_ROOT/lib/ffmpeg"

FFMPEG_VERSION="n7.1"
BASE_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest"

REQUIRED_LIBS=("avcodec-63" "avdevice-63" "avfilter-12" "avformat-63" "avutil-61" "swresample-7" "swscale-10")

check_ffmpeg_present() {
    local dir="$1"
    local ext="$2"
    [[ ! -d "$dir" ]] && return 1
    for lib in "${REQUIRED_LIBS[@]}"; do
        [[ ! -f "$dir/${lib}${ext}" ]] && return 1
    done
    return 0
}

case "$PLATFORM" in
    win-x64)
        OUTPUT_DIR="$LIB_DIR/win-x64"
        if command -v curl &> /dev/null; then
            URL=$(curl -s https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest | grep -o 'https://[^"]*win64-gpl-shared-7\.1\.zip' | head -n 1)
        else
            URL=$(wget -qO- https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest | grep -o 'https://[^"]*win64-gpl-shared-7\.1\.zip' | head -n 1)
        fi
        EXT=".dll"
        ;;
    linux-x64)
        OUTPUT_DIR="$LIB_DIR/linux-x64"
        if command -v curl &> /dev/null; then
            URL=$(curl -s https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest | grep -o 'https://[^"]*linux64-gpl-shared-7\.1\.tar\.xz' | head -n 1)
        else
            URL=$(wget -qO- https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest | grep -o 'https://[^"]*linux64-gpl-shared-7\.1\.tar\.xz' | head -n 1)
        fi
        EXT=".so"
        ;;
    osx-arm64)
        echo "[WARN] No automated download for macOS."
        echo "       Install FFmpeg via Homebrew: brew install ffmpeg"
        echo "       Then copy shared libraries to: $LIB_DIR/osx-arm64/"
        exit 1
        ;;
    *)
        echo "[ERROR] Unknown platform: $PLATFORM"
        echo "        Supported: win-x64, linux-x64, osx-arm64"
        exit 1
        ;;
esac

# Check if already present
if check_ffmpeg_present "$OUTPUT_DIR" "$EXT"; then
    echo "[OK] FFmpeg libraries already present in $OUTPUT_DIR"
    exit 0
fi

echo "[INFO] Downloading FFmpeg $FFMPEG_VERSION for $PLATFORM..."
echo "       URL: $URL"

TEMP_DIR="$LIB_DIR/.ffmpeg-temp"
mkdir -p "$TEMP_DIR" "$OUTPUT_DIR"

cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

ARCHIVE_FILE="$TEMP_DIR/ffmpeg-archive"

if command -v curl &> /dev/null; then
    curl -L -o "$ARCHIVE_FILE" "$URL"
elif command -v wget &> /dev/null; then
    wget -O "$ARCHIVE_FILE" "$URL"
else
    echo "[ERROR] Neither curl nor wget found. Please install one."
    exit 1
fi

echo "[OK] Download complete."
echo "[INFO] Extracting archive..."

if [[ "$PLATFORM" == "win-x64" ]]; then
    unzip -q "$ARCHIVE_FILE" -d "$TEMP_DIR"
    BIN_DIR=$(find "$TEMP_DIR" -maxdepth 2 -name "bin" -type d | head -1)
    if [[ -n "$BIN_DIR" ]]; then
        cp "$BIN_DIR"/*.dll "$OUTPUT_DIR/" 2>/dev/null || true
    fi
else
    tar -xf "$ARCHIVE_FILE" -C "$TEMP_DIR"
    LIB_SO_DIR=$(find "$TEMP_DIR" -maxdepth 2 -name "lib" -type d | head -1)
    if [[ -n "$LIB_SO_DIR" ]]; then
        cp "$LIB_SO_DIR"/lib*.so* "$OUTPUT_DIR/" 2>/dev/null || true
    fi
fi

# Verify
if check_ffmpeg_present "$OUTPUT_DIR" "$EXT"; then
    echo "[OK] FFmpeg $FFMPEG_VERSION installed successfully for $PLATFORM!"
else
    echo "[WARN] Some FFmpeg libraries may be missing. Check $OUTPUT_DIR"
fi
