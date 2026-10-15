#!/usr/bin/env bash
# Untimed checks only. Invoke only after the diagnostic's source/protocol gates pass.
set -euo pipefail
umask 077
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
DOTNET=${DOTNET:-$(command -v dotnet || true)}
[[ -n "$DOTNET" && -x "$DOTNET" ]] || { echo 'Set DOTNET to an installed .NET 8 SDK executable' >&2; exit 1; }
# Resolve before switching to the output-local SDK-selection directory.
DOTNET=$(python3 -c 'import pathlib,sys; print(pathlib.Path(sys.argv[1]).resolve())' "$DOTNET")
out=${1:?Provide a NEW absolute correctness-artifact directory}
[[ "$out" = /* && ! -e "$out" ]] || { echo 'Require a new absolute correctness directory' >&2; exit 1; }
verify_inputs() {
  python3 - "$repo" "$here/correctness-inputs.sha256" <<'PY'
import hashlib,pathlib,sys
repo = pathlib.Path(sys.argv[1])
for line in pathlib.Path(sys.argv[2]).read_text().splitlines():
    expected, name = line.split("  ", 1)
    actual = hashlib.sha256((repo / name).read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit("Input hash mismatch: " + name)
print("PASS pinned correctness input hashes")
PY
}
verify_inputs
# mkdir (without -p) also rejects an output created concurrently after the check.
mkdir "$out"
out=$(cd "$out" && pwd)
python3 "$here/verify_sources.py" > "$out/sources.before.json"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$out/dotnet-home" NUGET_PACKAGES="$out/packages"
cat > "$out/global.json" <<'JSON'
{"sdk":{"version":"8.0.425","rollForward":"disable"}}
JSON
cd "$out"
"$DOTNET" --info > "$out/dotnet-info.txt"
[[ "$("$DOTNET" --version)" == 8.0.425 ]] || { echo 'Expected .NET SDK 8.0.425' >&2; exit 1; }
for label in R RMinusC1; do
  runtime="$here/Sources/$label/Runtime"
  mkdir "$out/$label"
  "$DOTNET" build "$repo/Tests~/Roadmap/Roadmap.csproj" -c Release -v quiet --disable-build-servers -p:UseSharedCompilation=false \
    --configfile "$repo/Tests~/Roadmap/NuGet.Config" \
    -p:RuntimePath="$runtime" -p:PhaseApis=true -p:OptimizationProbes=true \
    -p:BaseIntermediateOutputPath="$out/$label/obj-roadmap/" \
    -p:OutputPath="$out/$label/bin-roadmap/" \
    > "$out/$label/roadmap.build.log" 2>&1 || { cat "$out/$label/roadmap.build.log"; exit 1; }
  "$DOTNET" "$out/$label/bin-roadmap/Roadmap.dll" | tee "$out/$label/roadmap.log"
  "$DOTNET" build "$repo/Tests~/C12Correctness/C12Correctness.csproj" -c Release -v quiet --disable-build-servers -p:UseSharedCompilation=false \
    --configfile "$repo/Tests~/C12Correctness/NuGet.Config" \
    -p:RuntimePath="$runtime" \
    -p:BaseIntermediateOutputPath="$out/$label/obj-c12/" \
    -p:OutputPath="$out/$label/bin-c12/" \
    > "$out/$label/c12.build.log" 2>&1 || { cat "$out/$label/c12.build.log"; exit 1; }
  "$DOTNET" "$out/$label/bin-c12/C12Correctness.dll" | tee "$out/$label/c12.log"
  "$DOTNET" build "$repo/Tests~/Roadmap/Compatibility.csproj" -c Release -v quiet --disable-build-servers -p:UseSharedCompilation=false \
    --configfile "$repo/Tests~/Roadmap/NuGet.Config" \
    -p:RuntimePath="$runtime" \
    -p:BaseIntermediateOutputPath="$out/$label/obj-compat/" \
    -p:OutputPath="$out/$label/bin-compat/" \
    > "$out/$label/compat.build.log" 2>&1 || { cat "$out/$label/compat.build.log"; exit 1; }
  echo "PASS netstandard2.1 / C# 9 compatibility compile: $label" | tee "$out/$label/compat.log"
done
for suite in roadmap c12; do
  reference=$(grep '^TRANSCRIPT ' "$out/R/$suite.log")
  candidate=$(grep '^TRANSCRIPT ' "$out/RMinusC1/$suite.log")
  [[ -n "$reference" && "$reference" == "$candidate" ]] || { echo "FAIL $suite differential transcripts" >&2; exit 1; }
  printf 'PASS %s R/RMinusC1 differential transcript: %s\n' "$suite" "$reference" | tee "$out/$suite.differential.log"
done
python3 "$here/verify_sources.py" > "$out/sources.after.json"
cmp "$out/sources.before.json" "$out/sources.after.json"
verify_inputs
printf '%s\n' '{"status":"PASS","sources":["R","RMinusC1"],"suites":["Roadmap","C12Correctness","netstandard2.1/CSharp9"],"differential_transcripts_equal":true,"timing":false}' > "$out/summary.json"
