#!/usr/bin/env bash
set -euo pipefail
root=$(git rev-parse --show-toplevel)
cd "$root"
out=$1
mkdir -p "$out/logs" "$out/fixtures"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
run() { local name=$1; shift; "$@" 2>&1 | tee "$out/logs/$name.log"; }
run sdk dotnet --info
run runtime dotnet build GameplayTags.Runtime.csproj -c Release --nologo
run optimization dotnet run --project .verification/tests/OptimizationTests.csproj -c Release
run rules dotnet run --project .verification/tests/RuleTests.csproj -c Release
run metadata-build dotnet build .verification/metadata/Metadata.csproj -c Release -o "$out/reader" --nologo
run fixture-desktop dotnet build .verification/fixture/Fixture.csproj -c Release -o "$out/fixtures/desktop" --nologo
run fixture-android dotnet build .verification/fixture/Fixture.csproj -c Release -o "$out/fixtures/android" -p:DefineConstants=TARGET_ANDROID%3BDLC --nologo
run fixture-plugin dotnet build .verification/fixture/Fixture.csproj -c Release -o "$out/fixtures/plugin" -p:DefineConstants=PLUGIN -p:FixtureName=PlayerPlugin --nologo
run fixture-derived dotnet build .verification/fixture/Fixture.csproj -c Release -o "$out/fixtures/derived" -p:DefineConstants=DERIVED --nologo
runner="$out/reader/Metadata.dll"
project="$out/project"
runtime="$out/reader/ZFramework.GameplayTags.dll"
pc="$out/fixtures/desktop/PlayerFixture.dll"
android="$out/fixtures/android/PlayerFixture.dll"
plugin="$out/fixtures/plugin/PlayerPlugin.dll"
run metadata-seed dotnet "$runner" seed "$project"
run metadata-first-build dotnet "$runner" build-desktop-plugin "$project" "$runtime" "$pc" "$plugin"
run provider-pc dotnet build .verification/provider/Provider.csproj -c Release -o "$out/provider-pc" -p:GeneratedSource="$project/Assets/GameplayTags.Generated.cs" --nologo
run metadata-pc-accepted dotnet "$runner" accept-desktop-plugin "$project" "$runtime" "$pc" "$plugin" "$out/provider-pc/GeneratedFixture.dll"
run metadata-target-change dotnet "$runner" build-android-dlc-plugin "$project" "$runtime" "$android" "$plugin" "$out/provider-pc/GeneratedFixture.dll"
run provider-android dotnet build .verification/provider/Provider.csproj -c Release -o "$out/provider-android" -p:GeneratedSource="$project/Assets/GameplayTags.Generated.cs" --nologo
run metadata-android-accepted dotnet "$runner" accept-android-dlc-plugin "$project" "$runtime" "$android" "$plugin" "$out/provider-android/GeneratedFixture.dll"
run metadata-missing dotnet "$runner" missing-runtime "$project"
run metadata-derived dotnet "$runner" derived "$project" "$runtime" "$out/fixtures/derived/PlayerFixture.dll"
# Compile-only, separate project and restore state. Not Unity engine execution.
run editor-netstandard dotnet build .verification/editorcompile/EditorCompile.csproj -c Release -o "$out/editor-portable" --nologo
python3 .verification/package.py "$out/package"
printf 'PASS managed correctness, pool policies, compiled-fixture Player metadata and packaging. Unity execution/performance not run.\n' | tee "$out/SUMMARY.txt"
