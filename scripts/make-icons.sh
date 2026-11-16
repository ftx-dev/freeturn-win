#!/bin/bash
# Иконки и логотипы трёх вариантов (standard, black, x) для приложения. Нужен macOS (AppKit).
set -euo pipefail
cd "$(dirname "$0")/.."
OUT=src/FreeTurnX/Resources
TMP="$(mktemp -d)"
for v in standard black x; do
  for s in 16 20 24 32 40 48 64 128 256; do swift scripts/make-logo.swift "$v" tile "$s" "$TMP/$v-$s.png"; done
  python3 scripts/pack-ico.py "$OUT/icon-$v.ico" "$TMP/$v"-*.png
  swift scripts/make-logo.swift "$v" tile 512 "$OUT/logo-$v.png"
done
cp "$OUT/icon-standard.ico" assets/FreeTurnX.ico
swift scripts/make-bmp-ico.swift "$OUT/logo-standard.png" assets/installer.ico
rm -rf "$TMP"
