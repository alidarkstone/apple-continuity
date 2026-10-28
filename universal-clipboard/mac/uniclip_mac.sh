#!/bin/bash
# Mac <-> Windows otomatik pano senkronu (metin).
# Kullanim:  UNICLIP_URL=http://192.168.1.50:8765/clip UNICLIP_TOKEN=xxxx ./uniclip_mac.sh
# Mac'te kopyaladiginiz metin ~0.5 sn icinde Windows panosuna gecer;
# Windows'ta kopyaladiginiz metin de Mac panosuna gelir.
URL="${UNICLIP_URL:?UNICLIP_URL ayarlayin}"
TOKEN="${UNICLIP_TOKEN:?UNICLIP_TOKEN ayarlayin}"
DEVICE="$(scutil --get ComputerName 2>/dev/null || echo Mac)"
last="$(pbpaste)"
tick=0
while true; do
  cur="$(pbpaste)"
  if [[ "$cur" != "$last" && -n "$cur" ]]; then
    printf '%s' "$cur" | curl -s -m 5 -X POST -H "X-Token: $TOKEN" -H "X-Device: $DEVICE" \
      -H "Content-Type: text/plain; charset=utf-8" --data-binary @- "$URL" >/dev/null
    last="$cur"
  fi
  # Her ~2 sn'de bir Windows panosunu cek
  if (( tick % 4 == 0 )); then
    win="$(curl -s -m 3 -H "X-Token: $TOKEN" "$URL")"
    if [[ $? -eq 0 && -n "$win" && "$win" != "$last" && "$win" != "yetkisiz" ]]; then
      printf '%s' "$win" | pbcopy
      last="$win"
    fi
  fi
  tick=$((tick + 1))
  sleep 0.5
done
