#!/usr/bin/env bash
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
dotnet=${DOTNET:-$(command -v dotnet || true)}
if [[ -z "$dotnet" || ! -x "$dotnet" ]]; then echo "Set DOTNET to an installed .NET 8 SDK executable." >&2; exit 1; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_TieredCompilation=0
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$here/obj/benchmark-dotnet-home}" NUGET_PACKAGES="${NUGET_PACKAGES:-$here/obj/packages}"
out=${BENCHMARK_OUT:-$here/results/narrow-final}
mkdir -p "$out"
inventory() {
 python3 - "$root" "$here" <<'PYHASH'
import hashlib,pathlib,sys
root,here=map(pathlib.Path,sys.argv[1:])
files=list((root/'Runtime').glob('*.cs'))+list((here/'Baselines').glob('*/Runtime/*.cs'))+[here/n for n in ('BenchmarkNarrow.cs','BenchmarkNarrow.csproj','narrow-b11.sh','UnityStubs.cs','NuGet.Config')]
for p in sorted(files): print(hashlib.sha256(p.read_bytes()).hexdigest(),p.relative_to(root))
PYHASH
}
inventory > "$out/source.before.sha256"
trap 'inventory > "$out/source.after.sha256"' EXIT
"$dotnet" --version > "$out/sdk-version.txt"
for version in B1 B11First B11Narrow; do
 runtime="$here/Baselines/$version/Runtime"
 [[ "$version" == B11Narrow ]] && runtime="$root/Runtime"
 "$dotnet" build "$here/BenchmarkNarrow.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:PhaseApis=true -p:BaseIntermediateOutputPath="$here/obj/narrow-$version/" -p:OutputPath="$here/bin/narrow-$version/" > "$out/$version.build.log" 2>&1 || { cat "$out/$version.build.log"; exit 1; }
done
if [[ "${1:-}" == --build-only ]]; then inventory > "$out/source.after.sha256"; cmp "$out/source.before.sha256" "$out/source.after.sha256"; exit 0; fi
"$dotnet" "$here/bin/narrow-B1/BenchmarkNarrow.dll" calibrate B1 "$out/iterations.csv" > "$out/calibrate.log" 2>&1
# Every pair has each precedence twice; absolute source positions are not balanced.
orders=("B1 B11First B11Narrow" "B11Narrow B11First B1" "B11First B1 B11Narrow" "B11Narrow B1 B11First")
for round in 1 2 3 4; do
 for version in ${orders[$((round-1))]}; do
  "$dotnet" "$here/bin/narrow-$version/BenchmarkNarrow.dll" measure "$version" "$out/iterations.csv" "$round" > "$out/$version.round$round.csv" 2> "$out/$version.round$round.environment.log"
 done
done
inventory > "$out/source.after.sha256"
cmp "$out/source.before.sha256" "$out/source.after.sha256" || { echo "Source changed during benchmark; results not comparable" >&2; exit 1; }
python3 - "$out" <<'PY'
import csv,pathlib,statistics,sys
out=pathlib.Path(sys.argv[1]);rows=[]
for file in sorted(out.glob('*.round*.csv')):rows.extend(csv.DictReader(file.open()))
with (out/'raw.csv').open('w') as f:
 w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
groups={}
for r in rows:groups.setdefault((r['version'],r['n'],r['case'],r['adapter']),[]).append(r)
with (out/'summary.csv').open('w') as f:
 w=csv.writer(f);w.writerow(['version','n','case','adapter','rounds','median_ns','min_ns','max_ns','median_bytes','gc0_total','gc1_total','gc2_total','retained_container_array_payload_max','shared_registry_array_payload_max'])
 for key,rs in groups.items():
  ns=[float(r['ns_per_op']) for r in rs]
  w.writerow([*key,len(rs),statistics.median(ns),min(ns),max(ns),statistics.median(float(r['bytes_per_op']) for r in rs),*[sum(int(r[k]) for r in rs) for k in ['gc0','gc1','gc2']],*[max(int(r[k]) for r in rs) for k in ['retained_container_array_payload','shared_registry_array_payload']]])
print('Wrote',out/'raw.csv','and',out/'summary.csv')
PY
