#!/usr/bin/env bash
# Operator assertion only: approval and both semantic passes must already exist.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
[[ ${1:-} = --reviewed ]] || { echo 'Require explicit source/protocol approval and two-source semantic pass before --reviewed' >&2; exit 1; }
build=${2:?New absolute build output}; out=${3:?New absolute evidence output}
[[ "$build" = /* && "$out" = /* && ! -e "$build" && ! -e "$out" && "$build" != "$out" ]] || { echo 'Require separate new absolute build/evidence paths' >&2; exit 1; }
# Single combined 600s budget: TERM after595s, allow5s protected child cleanup.
# No retry/resume/count adjustment is authorized if any stage fails or times out.
exec timeout --signal=TERM --kill-after=5s 595s bash -c '
  set -euo pipefail
  bash "$1/build_snapshots.sh" "$2"
  python3 "$1/run_comparison.py" freeze --manifest "$2/manifest.json" --out "$3"
  python3 "$1/run_comparison.py" fixtures --manifest "$2/manifest.json" --out "$3"
  python3 "$1/run_comparison.py" calibrate --manifest "$2/manifest.json" --out "$3" --reviewed
  exec python3 "$1/run_comparison.py" compare --manifest "$2/manifest.json" --out "$3" --reviewed
' _ "$here" "$build" "$out"
