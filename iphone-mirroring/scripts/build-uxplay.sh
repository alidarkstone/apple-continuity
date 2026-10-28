#!/usr/bin/env bash
# Builds UxPlay (open-source AirPlay mirroring receiver, GPL-3.0) in an MSYS2 UCRT64
# shell and gathers it, the GStreamer plugins it uses and every DLL they need into one
# folder that runs on a plain Windows PC (no MSYS2 or Bonjour install needed).
#
#   scripts/build-uxplay.sh [output-folder]
set -euo pipefail

# UxPlay 1.74 brings its own mDNS, so Apple's Bonjour is not needed on Windows.
UXPLAY_REF="${UXPLAY_REF:-534e2cc}"
OUT="$(realpath -m "${1:-uxplay}")"
P=mingw-w64-ucrt-x86_64

pacman -S --needed --noconfirm git $P-cmake $P-ninja $P-gcc $P-pkgconf $P-libplist $P-openssl \
    $P-gstreamer $P-gst-plugins-base $P-gst-plugins-good $P-gst-plugins-bad $P-gst-libav

src="$(mktemp -d)"
git clone --quiet https://github.com/FDH2/UxPlay "$src"
git -C "$src" checkout --quiet "$UXPLAY_REF"
cmake -S "$src" -B "$src/build" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$src/build"

rm -rf "$OUT"
mkdir -p "$OUT/lib/gstreamer-1.0" "$OUT/libexec/gstreamer-1.0"
cp "$src/build/uxplay.exe" /ucrt64/bin/gst-inspect-1.0.exe "$OUT/"
cp /ucrt64/libexec/gstreamer-1.0/gst-plugin-scanner.exe "$OUT/libexec/gstreamer-1.0/"
cp "$src/LICENSE" "$OUT/UxPlay-LICENSE.txt"
echo "https://github.com/FDH2/UxPlay/tree/$(git -C "$src" rev-parse HEAD)" > "$OUT/UxPlay-SOURCE.txt"

# Only the plugins UxPlay's pipelines use (mirroring video + AirPlay audio + cover art).
plugins=(coreelements app typefindfunctions playback videoconvertscale videorate audioconvert
         audioresample volume level autodetect videofilter jpeg imagefreeze audioparsers
         videoparsersbad d3d11 libav wasapi wasapi2 directsound debugutilsbad)
for p in "${plugins[@]}"; do
    cp "/ucrt64/lib/gstreamer-1.0/libgst$p.dll" "$OUT/lib/gstreamer-1.0/"
done

for f in "$OUT"/*.exe "$OUT"/lib/gstreamer-1.0/*.dll "$OUT"/libexec/gstreamer-1.0/*.exe; do
    ldd "$f" | awk '$3 ~ /^\/ucrt64\/bin\// { print $3 }'
done | sort -u | xargs -r cp -t "$OUT/"

du -sh "$OUT"
