#!/usr/bin/env bash
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
dotnet=${DOTNET:-$(command -v dotnet || true)}
[[ -n "$dotnet" && -x "$dotnet" ]] || { echo 'Set DOTNET to an installed .NET 8 SDK.' >&2; exit 1; }
[[ $# -gt 0 ]] || { echo 'Usage: run.sh label=/absolute/Runtime [label=/absolute/Runtime ...]' >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$here/obj/dotnet-home}" NUGET_PACKAGES="${NUGET_PACKAGES:-$here/obj/packages}"
mkdir -p "$here/results"
reference=''
for spec in "$@"; do
 label=${spec%%=*}; runtime=${spec#*=}
 [[ "$spec" == *=* && "$label" =~ ^[A-Za-z0-9_-]+$ && -d "$runtime" ]] || { echo "Invalid label=Runtime: $spec" >&2; exit 1; }
 runtime=$(cd "$runtime" && pwd)
 [[ ! -e "$here/results/$label.build.log" && ! -e "$here/results/$label.log" && ! -e "$here/bin/$label" && ! -e "$here/obj/$label" ]] || { echo "Refusing to overwrite existing evidence/output for $label; use a new label." >&2; exit 1; }
 "$dotnet" build "$here/C12Correctness.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:BaseIntermediateOutputPath="$here/obj/$label/" -p:OutputPath="$here/bin/$label/" > "$here/results/$label.build.log" 2>&1 || { cat "$here/results/$label.build.log"; exit 1; }
 "$dotnet" "$here/bin/$label/C12Correctness.dll" | tee "$here/results/$label.log"
 digest=$(grep '^TRANSCRIPT ' "$here/results/$label.log")
 [[ -n "$reference" ]] || reference="$digest"
 [[ "$digest" == "$reference" ]] || { echo "FAIL transcript mismatch: $label" >&2; exit 1; }
done
echo 'PASS C12 supplemental differential transcripts'
