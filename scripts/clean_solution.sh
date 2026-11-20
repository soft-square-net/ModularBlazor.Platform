#!/usr/bin/env bash
set -euo pipefail

TARGET_DIR="${1:-.}"

if [ ! -d "$TARGET_DIR" ]; then
  echo "Error: Directory '$TARGET_DIR' does not exist." >&2
  exit 1
fi

echo "Cleaning 'bin' and 'obj' folders under '$TARGET_DIR'..."
find "$TARGET_DIR" -type d \( -name "bin" -o -name "obj" \) -prune -exec rm -rf -- '{}' +
echo "Done."