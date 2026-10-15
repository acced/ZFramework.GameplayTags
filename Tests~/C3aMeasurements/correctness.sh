#!/usr/bin/env bash
# Portable untimed semantic/compatibility check of the two pinned experimental sources.
set -euo pipefail
umask 077
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
DOTNET=${DOTNET:-$(command -v dotnet || true)}
[[ -n "$DOTNET" && -x "$DOTNET" ]] || { echo 'Set DOTNET to an installed .NET8 SDK' >&2; exit 1; }
out=${1:?Provide a NEW absolute correctness-artifact directory}
[[ "$out" = /* && ! -e "$out" ]] || { echo 'Require new absolute correctness directory' >&2; exit 1; }
(cd "$repo" && sha256sum -c "$here/correctness-inputs.sha256")
mkdir -p "$out"
python3 "$here/verify_sources.py" > "$out/sources.before.json"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_HOME="$out/dotnet-home" NUGET_PACKAGES="$out/packages"
# Select the measured SDK even when the host defaults to a newer installed SDK.
cat > "$out/global.json" <<'JSON'
{"sdk":{"version":"8.0.425","rollForward":"disable"}}
JSON
cd "$out"
"$DOTNET" --info > "$out/dotnet-info.txt"
[[ "$("$DOTNET" --version)" == 8.0.425 ]] || { echo 'Expected .NET SDK8.0.425' >&2; exit 1; }
for label in current792 C3a; do
  runtime="$here/Sources/$label/Runtime"
  mkdir -p "$out/$label"
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
  reference=$(grep '^TRANSCRIPT ' "$out/current792/$suite.log")
  candidate=$(grep '^TRANSCRIPT ' "$out/C3a/$suite.log")
  [[ -n "$reference" && "$reference" == "$candidate" ]] || { echo "FAIL $suite differential transcripts" >&2; exit 1; }
  printf 'PASS %s current792/C3a differential transcript: %s\n' "$suite" "$reference" | tee "$out/$suite.differential.log"
done
python3 "$here/verify_sources.py" > "$out/sources.after.json"
cmp "$out/sources.before.json" "$out/sources.after.json"
(cd "$repo" && sha256sum -c "$here/correctness-inputs.sha256")
printf '%s\n' '{"status":"PASS","sources":["current792","C3a"],"suites":["Roadmap","C12Correctness","netstandard2.1/CSharp9"],"differential_transcripts_equal":true,"timing":false}' > "$out/summary.json"
