#!/usr/bin/env bash
# Build-only preparation. Does not calibrate or invoke any measurement target.
set -euo pipefail
umask 077
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
work=$(cd "$repo/.." && pwd)
dotnet=${DOTNET:-$(command -v dotnet || true)}
[[ -n "$dotnet" && -x "$dotnet" ]] || { echo "Set DOTNET to an installed .NET8 SDK" >&2; exit 1; }
build=${1:?Provide a NEW absolute build-artifact directory}
[[ "$build" = /* && ! -e "$build" ]] || { echo 'Require new absolute build directory' >&2; exit 1; }
mkdir -p "$build"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_HOME="$build/dotnet-home"
"$dotnet" --info > "$build/sdk-runtime.txt"
for version in B1 B11 C1 C1-C2; do
  case "$version" in
    B1) runtime="$repo/Tests~/Roadmap/Baselines/B1/Runtime" ;;
    B11) runtime="$repo/Tests~/C12Correctness/Baselines/PR4/Runtime" ;;
    C1) runtime="$repo/Tests~/C12Correctness/Baselines/C1/Runtime" ;;
    C1-C2) runtime="$repo/Runtime" ;;
  esac
  (cd "$runtime"; sha256sum ./*.cs) > "$build/$version.source.before.sha256"
  "$dotnet" build "$here/C12Measurements.csproj" -c Release -v quiet \
    -p:RuntimePath="$runtime" -p:BaseIntermediateOutputPath="$build/obj-$version/" \
    -p:OutputPath="$build/bin-$version/" > "$build/$version.build.log" 2>&1
  (cd "$runtime"; sha256sum ./*.cs) > "$build/$version.source.after.sha256"
  cmp "$build/$version.source.before.sha256" "$build/$version.source.after.sha256"
done
python3 - "$build" "$dotnet" "$repo" "$work" <<'PY'
import hashlib,json,pathlib,sys
b=pathlib.Path(sys.argv[1]);ids={'B1':'dac77e7e7d5bad1ba7e77ed9fca7824d50187a72','B11':'352d304299ecbb7486e2c6a56ba71874c90fe21b','C1':'isolated-C1-snapshot','C1-C2':'isolated-C1-plus-C2-snapshot'}
manifest={'dotnet':str(pathlib.Path(sys.argv[2]).resolve()),'versions':{v:{'dll':str(b/f'bin-{v}/C12Measurements.dll'),'source_id':identity,'source_inventory_sha256':hashlib.sha256((b/f'{v}.source.before.sha256').read_bytes()).hexdigest()} for v,identity in ids.items()}}
repo,work=map(pathlib.Path,sys.argv[3:5]);controls={}
for version in ids:
    runtime={'B1':repo/'Tests~/Roadmap/Baselines/B1/Runtime','B11':repo/'Tests~/C12Correctness/Baselines/PR4/Runtime','C1':repo/'Tests~/C12Correctness/Baselines/C1/Runtime','C1-C2':repo/'Runtime'}[version]
    source=(runtime/'GameplayTagContainer.cs').read_text()
    entry=next(line for line in source.splitlines() if 'public Enumerator GetEnumerator()' in line)
    body=source[source.index('        public struct Enumerator'):]
    payload=entry+'\n'+body
    (b/f'{version}.enumeration-control.cs.txt').write_text(payload)
    controls[version]=hashlib.sha256(payload.encode()).hexdigest()
assert len(set(controls.values()))==1,'Enumeration source differs across builds'
(b/'unchanged-enumeration-source.json').write_text(json.dumps(controls,indent=2)+'\n')
(b/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
PY
