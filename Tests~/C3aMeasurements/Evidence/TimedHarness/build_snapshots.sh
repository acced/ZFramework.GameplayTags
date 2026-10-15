#!/usr/bin/env bash
# Build-only preparation after parent approval. No fixture or measurement invocation.
set -euo pipefail
umask 077
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
dotnet=${DOTNET:-$(command -v dotnet || true)}
[[ -n "$dotnet" && -x "$dotnet" ]] || { echo 'Set DOTNET to an installed .NET8 SDK' >&2; exit 1; }
build=${1:?Provide a NEW absolute build-artifact directory}
[[ "$build" = /* && ! -e "$build" ]] || { echo 'Require new absolute build directory' >&2; exit 1; }
mkdir -p "$build"
python3 "$here/verify_sources.py" > "$build/sources.before.json"
(cd "$here"; sha256sum Program.cs UnityStubs.cs C3aMeasurements.csproj NuGet.Config) > "$build/compile.before.sha256"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_HOME="$build/dotnet-home"
"$dotnet" --info > "$build/sdk-runtime.txt"
for version in current792 C3a; do
  case "$version" in
    current792) runtime="$repo/../c3a-implementation/baseline/Runtime" ;;
    C3a) runtime="$repo/Runtime" ;;
  esac
  "$dotnet" build "$here/C3aMeasurements.csproj" -c Release -v quiet --disable-build-servers \
    -p:UseSharedCompilation=false -p:RuntimePath="$runtime" -p:BaseIntermediateOutputPath="$build/obj-$version/" \
    -p:OutputPath="$build/bin-$version/" > "$build/$version.build.log" 2>&1
done
python3 "$here/verify_sources.py" > "$build/sources.after.json"
cmp "$build/sources.before.json" "$build/sources.after.json"
(cd "$here"; sha256sum Program.cs UnityStubs.cs C3aMeasurements.csproj NuGet.Config) > "$build/compile.after.sha256"
cmp "$build/compile.before.sha256" "$build/compile.after.sha256"
python3 - "$build" "$dotnet" "$here" <<'PY'
import hashlib,json,pathlib,sys
b,dotnet,here=map(pathlib.Path,sys.argv[1:]);source=json.loads((b/'sources.before.json').read_text())
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
manifest={'compile_input_files':{name:sha(here/name) for name in ['Program.cs','UnityStubs.cs','C3aMeasurements.csproj','NuGet.Config']},'dotnet':str(dotnet.resolve()),'dotnet_sha256':sha(dotnet),'source_pins_sha256':sha(here/'source_pins.json'),'sdk_runtime_sha256':sha(b/'sdk-runtime.txt'),'versions':{v:{'dll':str(b/f'bin-{v}/C3aMeasurements.dll'),'source_id':e['source_id'],'source_files':e['files']} for v,e in source.items()}}
(b/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
PY
