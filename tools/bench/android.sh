#!/usr/bin/env bash
# Board / crowd / effects bench on a USB-connected Android phone (design/08 §2, §4; art/board/README.md).
# Each case starts the app with BoardBench switches (LaunchArgs reads them from the "unity" intent
# extra), waits for the bench to finish and keeps its "[Bench]" lines in artifacts/bench/android/.
#
# usage: tools/bench/android.sh [--install] suite|soak|sim|<name> "<switches>"
#   --install   install artifacts/player/Android/Relics.apk first
#   suite       base board, 300 units skinned / VAT, VAT + 30/60/120 effects (~10 s each)
#   soak        300 units VAT + 30 effects capped at 30 fps for 10 min, logging heat every 30 s
#   sim         battle logic stress test in the interpreter (SimBench: 8v8, summons, 300 units; a few minutes)
#
# Needs: USB debugging on, the local CDN on port 8000 (python -m http.server 8000 in artifacts/cdn).
set -euo pipefail
cd "$(dirname "$0")/../.."

ADB=${ADB:-/d/play/Editor/6000.3.25f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}
PKG=${PKG:-com.bigtaoo.Relics}
ACTIVITY=com.unity3d.player.UnityPlayerGameActivity
OUT=artifacts/bench/android
mkdir -p "$OUT"

if [ "${1:-}" = "--install" ]; then
  "$ADB" install -r artifacts/player/Android/Relics.apk
  shift
fi
"$ADB" get-state >/dev/null
# The player asks the CDN at 127.0.0.1:8000 for the resource version (BootConfig.LocalCdnRoot).
"$ADB" reverse tcp:8000 tcp:8000 >/dev/null
curl -sf -o /dev/null http://127.0.0.1:8000/ || { echo "local CDN is not running on port 8000"; exit 1; }
echo "device: $("$ADB" shell getprop ro.product.model | tr -d '\r'), Android $("$ADB" shell getprop ro.build.version.release | tr -d '\r')"

run() { # name, switches, timeout seconds
  local name=$1 args=$2 limit=${3:-120}
  "$ADB" logcat -c
  "$ADB" shell am start -S -n "$PKG/$ACTIVITY" -e unity "'$args'" >/dev/null
  local waited=0
  until "$ADB" logcat -d -s Unity | grep -q "\[Bench\] memory MB\|\[SimBench\] done"; do
    sleep 2
    waited=$((waited + 2))
    if [ "$waited" -ge "$limit" ]; then echo "== $name: no result after ${limit}s"; break; fi
  done
  "$ADB" logcat -d -s Unity | grep -a "\[Bench\]\|\[SimBench\]\|Exception" | sed 's/^.*\[\(Sim\)\{0,1\}Bench\] //' > "$OUT/$name.log" || true
  "$ADB" shell am force-stop "$PKG"
  echo "== $name ($args)"
  grep -v "^scene board" "$OUT/$name.log" | head -20
}

case "${1:-suite}" in
  suite)
    run base     "-bench"
    run skin254  "-bench -crowd 254"
    run vat254   "-bench -crowd 254 -vat"
    run fx30     "-bench -crowd 254 -vat -fx 30"
    run fx60     "-bench -crowd 254 -vat -fx 60"
    run fx120    "-bench -crowd 254 -vat -fx 120"
    ;;
  soak)
    run soak "-bench -crowd 254 -vat -fx 30 -fps 30 -duration 600" 720
    ;;
  sim)
    run sim "-simbench -seeds 3" 900
    ;;
  *)
    run "$1" "$2" "${3:-120}"
    ;;
esac
