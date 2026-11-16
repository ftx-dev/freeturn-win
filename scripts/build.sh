#!/bin/bash
# Сборка FreeTurnX для Windows на macOS/Linux: Go-часть - нативно, .NET и NSIS - в Docker.
#   scripts/build.sh            - x64 и arm64
#   scripts/build.sh x64        - только одна архитектура
# Результат - папка dist/ только с готовыми установщиками (всё остальное встроено в них):
#   dist/FreeTurnX-<версия>-x64-setup.exe, dist/FreeTurnX-<версия>-arm64-setup.exe, dist/SHA256SUMS.txt
# В build/ - промежуточные файлы: бинарники по архитектурам и загрузчик WebView2, который вшивается в установщик.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
ARCHS=("${@:-x64 arm64}")
[ $# -eq 0 ] && ARCHS=(x64 arm64)
NUGET="$HOME/.nuget-docker"
mkdir -p build dist "$NUGET"

# Загрузчик WebView2 Runtime (Evergreen) - ставится, только если его нет.
[ -s build/MicrosoftEdgeWebview2Setup.exe ] || curl -fsSL -o build/MicrosoftEdgeWebview2Setup.exe "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

docker image inspect freeturnx-nsis >/dev/null 2>&1 || docker build -q -t freeturnx-nsis installer >/dev/null

for arch in "${ARCHS[@]}"; do
  case "$arch" in
    x64) goarch=amd64; rid=win-x64 ;;
    arm64) goarch=arm64; rid=win-arm64 ;;
    *) echo "build: неизвестная архитектура $arch" >&2; exit 2 ;;
  esac
  out="build/$arch"
  rm -rf "$out" && mkdir -p "$out"
  echo "== $arch: Go"
  (cd helper && GOOS=windows GOARCH=$goarch go build -trimpath -ldflags "-checklinkname=0 -s -w -X github.com/samosvalishe/free-turn-proxy/mobile.version=v4.1.5" -o "../$out/" ./cmd/...)
  (cd third_party/amnezia-box && GOOS=windows GOARCH=$goarch go build -trimpath -ldflags "-s -w" -tags "with_gvisor,with_wireguard,with_awg" -o "../../$out/amnezia-box.exe" ./cmd/sing-box)
  echo "== $arch: .NET"
  docker run --rm -v "$ROOT:/src" -v "$NUGET:/root/.nuget" -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
    dotnet publish src/FreeTurnX -c Release -r "$rid" -o "/src/$out/app" -p:Version="$VERSION" >/dev/null
  mv "$out/app/FreeTurnX.exe" "$out/" && rm -rf "$out/app"
  echo "== $arch: установщик"
  docker run --rm -v "$ROOT:/src" -w /src freeturnx-nsis \
    makensis -V2 -DVERSION="$VERSION" -DARCH="$arch" -DSRC="$out" -DOUT="dist/FreeTurnX-$VERSION-$arch-setup.exe" /src/installer/freeturnx.nsi
done

# Контрольные суммы - их сверяет автообновление перед установкой.
rm -f dist/SHA256SUMS.txt
(cd dist && shasum -a 256 FreeTurnX-"$VERSION"-*-setup.exe > SHA256SUMS.txt)
ls -lh dist/
