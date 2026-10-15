#!/usr/bin/env bash
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dotnet=${DOTNET:-$(command -v dotnet || true)}
if [[ -z "$dotnet" || ! -x "$dotnet" ]]; then echo "Set DOTNET to an installed .NET 8 SDK executable." >&2; exit 1; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$here/obj/dotnet-home}" NUGET_PACKAGES="${NUGET_PACKAGES:-$here/obj/packages}"
mkdir -p "$here/results"
versions=("${@:-B0 B1 current}")
# Allow either explicit positional versions or a single space-separated list.
for version in ${versions[*]}; do
 runtime="$here/Baselines/$version/Runtime"; phase=true; optimization_probes=false
 [[ "$version" == current ]] && optimization_probes=true
 [[ "$version" == current ]] && runtime="$repo/Runtime"
 [[ "$version" == B0 ]] && phase=false
 "$dotnet" build "$here/Roadmap.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:PhaseApis="$phase" -p:OptimizationProbes="$optimization_probes" -p:BaseIntermediateOutputPath="$here/obj/$version/" -p:OutputPath="$here/bin/$version/" > "$here/results/$version.build.log" 2>&1 || { cat "$here/results/$version.build.log"; exit 1; }
 "$dotnet" "$here/bin/$version/Roadmap.dll" | tee "$here/results/$version.log"
done
reference=""
for version in ${versions[*]}; do
 digest=$(grep '^TRANSCRIPT ' "$here/results/$version.log")
 [[ -z "$reference" ]] && reference="$digest"
 [[ "$digest" == "$reference" ]] || { echo "FAIL differential: $version differs"; exit 1; }
done
echo "PASS differential transcript equality: ${versions[*]}"
for version in ${versions[*]}; do
 runtime="$here/Baselines/$version/Runtime"
 [[ "$version" == current ]] && runtime="$repo/Runtime"
 if [[ -f "$runtime/GameplayTagSetAlgorithms.cs" ]]; then
  "$dotnet" build "$here/Kernels.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:BaseIntermediateOutputPath="$here/obj/kernels-$version/" -p:OutputPath="$here/bin/kernels-$version/" > "$here/results/$version.kernels.build.log" 2>&1 || { cat "$here/results/$version.kernels.build.log"; exit 1; }
  "$dotnet" "$here/bin/kernels-$version/Kernels.dll" | tee "$here/results/$version.kernels.log"
 fi
done
for version in ${versions[*]}; do
 runtime="$here/Baselines/$version/Runtime"
 [[ "$version" == current ]] && runtime="$repo/Runtime"
 "$dotnet" build "$here/Compatibility.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:BaseIntermediateOutputPath="$here/obj/compat-$version/" -p:OutputPath="$here/bin/compat-$version/" > "$here/results/$version.compat.build.log" 2>&1 || { cat "$here/results/$version.compat.build.log"; exit 1; }
 echo "PASS netstandard2.1 / C# 9 compatibility compile: $version"
 if [[ -f "$runtime/RegistrySnapshot.cs" ]]; then
  "$dotnet" build "$here/Snapshot.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:BaseIntermediateOutputPath="$here/obj/snapshot-$version/" -p:OutputPath="$here/bin/snapshot-$version/" > "$here/results/$version.snapshot.build.log" 2>&1 || { cat "$here/results/$version.snapshot.build.log"; exit 1; }
  "$dotnet" "$here/bin/snapshot-$version/Snapshot.dll" | tee "$here/results/$version.snapshot.log"
 fi
done
