#!/usr/bin/env bash
set -euo pipefail
root=$(git rev-parse --show-toplevel)
cd "$root"
out=${1:-"$root/.hardening-results"}
mkdir -p "$out"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
run() { local name=$1; shift; "$@" 2>&1 | tee "$out/$name.log"; }
run runtime-build dotnet build GameplayTags.Runtime.csproj -c Release --nologo
run main-tests dotnet run --project Tests~/GameplayTags.Tests.csproj -c Release
run bulk-tests dotnet run --project Tests~/Bulk/GameplayTags.Bulk.Tests.csproj -c Release
run registry-tests dotnet run --project Tests~/Registry/GameplayTags.Registry.Tests.csproj -c Release
run deep-tests dotnet run --project Tests~/DeepHierarchy/GameplayTags.DeepHierarchy.Tests.csproj -c Release
run hardening-build dotnet build Tests~/Hardening/Hardening.csproj -c Release --nologo
runner=Tests~/Hardening/bin/Release/net8.0/Hardening.dll
run hardening-cold dotnet "$runner"
run hardening-initialized dotnet "$runner" --initialized
run hardening-registry dotnet "$runner" --invalid-registry
run unity-compile dotnet build Tools~/Hardening/UnityCompile.csproj -c Release --nologo
run generator-tests dotnet run --project SourceGenerator~/Tests/GameplayTags.SourceGenerator.Tests.csproj -c Release
run baseline-build dotnet build Tests~/Hardening/Hardening.csproj -c Release --nologo -p:RuntimePath="$root/Tests~/Hardening/Baseline/Runtime" -p:BaseIntermediateOutputPath="$out/baseline-obj/" -p:OutputPath="$out/baseline-bin/"
run baseline-cold dotnet "$out/baseline-bin/Hardening.dll" --baseline
run baseline-initialized dotnet "$out/baseline-bin/Hardening.dll" --baseline --initialized
run baseline-registry dotnet "$out/baseline-bin/Hardening.dll" --baseline --invalid-registry
echo 'PASS all candidate correctness/compatibility gates; baseline failures are diagnostic reproductions.' | tee "$out/SUMMARY.txt"
