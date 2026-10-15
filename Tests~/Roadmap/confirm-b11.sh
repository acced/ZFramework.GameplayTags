#!/usr/bin/env bash
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
dotnet=${DOTNET:-$(command -v dotnet || true)}
if [[ -z "$dotnet" || ! -x "$dotnet" ]]; then echo "Set DOTNET to an installed .NET 8 SDK executable." >&2; exit 1; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_TieredCompilation=0
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$here/obj/benchmark-dotnet-home}" NUGET_PACKAGES="${NUGET_PACKAGES:-$here/obj/packages}"
out=${BENCHMARK_OUT:-$here/results/full-confirmation}
mkdir -p "$out"
inventory() {
 python3 - "$root" "$here" <<'PYHASH'
import hashlib,pathlib,sys
root,here=map(pathlib.Path,sys.argv[1:])
files=list((root/'Runtime').glob('*.cs'))+list((here/'Baselines').glob('*/Runtime/*.cs'))+[here/n for n in ('Benchmark.cs','Benchmark.csproj','confirm-b11.sh','UnityStubs.cs','NuGet.Config')]
for p in sorted(files): print(hashlib.sha256(p.read_bytes()).hexdigest(),p.relative_to(root))
PYHASH
}
inventory > "$out/source.before.sha256"
trap 'inventory > "$out/source.after.sha256"' EXIT
# Confirm the old full screen's count map and source identities before any build/run.
python3 - "$root" "$here" "$out" <<'PYIDENTITY'
import hashlib,json,pathlib,sys
root,here,out=map(pathlib.Path,sys.argv[1:])
def read_inventory(path):return {line.split(None,1)[1]:line.split(None,1)[0] for line in path.read_text().splitlines()}
original=read_inventory(here/'results/full-final/source.before.sha256')
narrow=read_inventory(here/'results/narrow-final/source.before.sha256')
counts=here/'results/full-final/iterations.csv';count_bytes=counts.read_bytes();count_sha=hashlib.sha256(count_bytes).hexdigest()
assert count_sha=="85ef89dff332434c9d2822405e60212a88f313853380b3428a63ba543e4c9d99", 'Historical count map changed'
assert len(count_bytes.decode().splitlines())==151, 'Expected 151 original count rows'
checks={}
for name,digest in original.items():
 if name.startswith('Tests~/Roadmap/Baselines/B0/Runtime/') or name.startswith('Tests~/Roadmap/Baselines/B1/Runtime/') or name in ['Tests~/Roadmap/Benchmark.cs','Tests~/Roadmap/Benchmark.csproj','Tests~/Roadmap/UnityStubs.cs','Tests~/Roadmap/NuGet.Config']:
  assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest,name
  checks[name]=digest
for name,digest in narrow.items():
 if name.startswith('Runtime/'):
  assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest,name
  checks[name]=digest
(out/'iterations.csv').write_bytes(count_bytes)
(out/'source-identity-audit.json').write_text(json.dumps({'count_source':'results/full-final/iterations.csv','count_sha256':count_sha,'calibration_performed':False,'original_B0_B1_and_harness_verified':True,'current_matches_tested_Narrow':True,'verified_source_hashes':checks},indent=2)+'\n')
PYIDENTITY
"$dotnet" --version > "$out/sdk-version.txt"
for version in B0 B1 B11Narrow; do
 runtime="$here/Baselines/$version/Runtime"; phase=true
 [[ "$version" == B11Narrow ]] && runtime="$root/Runtime"
 [[ "$version" == B0 ]] && phase=false
 "$dotnet" build "$here/Benchmark.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:PhaseApis="$phase" -p:BaseIntermediateOutputPath="$here/obj/confirmation-$version/" -p:OutputPath="$here/bin/confirmation-$version/" > "$out/$version.build.log" 2>&1 || { cat "$out/$version.build.log"; exit 1; }
done
if [[ "${1:-}" == --build-only ]]; then inventory > "$out/source.after.sha256"; cmp "$out/source.before.sha256" "$out/source.after.sha256"; exit 0; fi
orders=("B0 B1 B11Narrow" "B11Narrow B1 B0" "B1 B0 B11Narrow")
for round in 1 2 3; do
 for version in ${orders[$((round-1))]}; do
  "$dotnet" "$here/bin/confirmation-$version/Benchmark.dll" measure "$version" "$out/iterations.csv" "$round" > "$out/$version.round$round.csv" 2> "$out/$version.round$round.environment.log"
 done
done
cmp "$here/results/full-final/iterations.csv" "$out/iterations.csv" || { echo "Count map changed during confirmation" >&2; exit 1; }
inventory > "$out/source.after.sha256"
cmp "$out/source.before.sha256" "$out/source.after.sha256" || { echo "Source changed during benchmark; results not comparable" >&2; exit 1; }
python3 - "$out" <<'PY'
import csv, pathlib, statistics, sys
out=pathlib.Path(sys.argv[1]); rows=[]
for file in sorted(out.glob('*.round*.csv')):
 rows.extend(csv.DictReader(file.open()))
with (out/'raw.csv').open('w') as f:
 w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
groups={}
for r in rows: groups.setdefault((r['version'],r['n'],r['case'],r['adapter']),[]).append(r)
with (out/'summary.csv').open('w') as f:
 w=csv.writer(f);w.writerow(['version','n','case','adapter','rounds','median_ns','min_ns','max_ns','median_bytes','gc0_total','gc1_total','gc2_total','retained_container_array_payload_max','shared_registry_array_payload_max'])
 for key,rs in groups.items():
  ns=[float(r['ns_per_op']) for r in rs]
  w.writerow([*key,len(rs),statistics.median(ns),min(ns),max(ns),statistics.median(float(r['bytes_per_op']) for r in rs),*[sum(int(r[k]) for r in rs) for k in ['gc0','gc1','gc2']],*[max(int(r[k]) for r in rs) for k in ['retained_container_array_payload','shared_registry_array_payload']]])
print('Wrote',out/'raw.csv','and',out/'summary.csv')
PY
