#!/usr/bin/env bash
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
dotnet=${DOTNET:-$(command -v dotnet || true)}
if [[ -z "$dotnet" || ! -x "$dotnet" ]]; then echo "Set DOTNET to an installed .NET 8 SDK executable." >&2; exit 1; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_TieredCompilation=0
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$here/obj/benchmark-dotnet-home}" NUGET_PACKAGES="${NUGET_PACKAGES:-$here/obj/packages}"
out=${BENCHMARK_OUT:-$here/results/core-final}
mkdir -p "$out"
inventory() {
 python3 - "$root" "$here" <<'PYHASH'
import hashlib,pathlib,sys
root,here=map(pathlib.Path,sys.argv[1:])
files=list((root/'Runtime').glob('*.cs'))+list((here/'Baselines').glob('*/Runtime/*.cs'))+[here/n for n in ('BenchmarkCore.cs','BenchmarkCore.csproj','core-b11.sh','UnityStubs.cs','NuGet.Config')]
for p in sorted(files): print(hashlib.sha256(p.read_bytes()).hexdigest(),p.relative_to(root))
PYHASH
}
inventory > "$out/source.before.sha256"
trap 'inventory > "$out/source.after.sha256"' EXIT
python3 - "$root" "$here" "$out" <<'PYIDENTITY'
import csv,hashlib,json,pathlib,sys
root,here,out=map(pathlib.Path,sys.argv[1:])
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def inventory(p):return {line.split(None,1)[1]:line.split(None,1)[0] for line in p.read_text().splitlines()}
full_sha="85ef89dff332434c9d2822405e60212a88f313853380b3428a63ba543e4c9d99";focus_sha="de212c784634a48f01a39083ad62b9012ce0ed1c78f7066fc1eaf355664a23df"
paths={"full-final":here/'results/full-final/iterations.csv',"focused-final":here/'results/focused-final/iterations.csv'}
assert digest(paths['full-final'])==full_sha, 'Original full map changed'
assert digest(paths['focused-final'])==focus_sha, 'Original focused map changed'
maps={source:{r[0]:int(r[1]) for r in csv.reader(path.open())} for source,path in paths.items()}
assert len(maps['full-final'])==151 and len(maps['focused-final'])==55
old=inventory(here/'results/full-final/source.before.sha256')
checks={name:sha for name,sha in old.items() if name.startswith('Tests~/Roadmap/Baselines/B1/Runtime/') or name in ['Tests~/Roadmap/UnityStubs.cs','Tests~/Roadmap/NuGet.Config']}
checks.update({'Runtime/AssemblyInfo.cs': 'f1a839c47d30932d2aa70197d42572ee541e33dfed1f81c214e28ef3880ad1e4', 'Runtime/GameplayTag.cs': '811610e3ab03098c750180acc9e4b2c101a9732924c4ec32029b46bf0b613758', 'Runtime/GameplayTagContainer.cs': 'cde7d94d624ab6f5b1b6305a21e82b8371cf59895a879834fa3677b1ee4d6866', 'Runtime/GameplayTagContainerAlgorithms.cs': '3a98190d288ca445fcb0146b3d100a88f2a875f3e335c38d01395ea77326a7ba', 'Runtime/GameplayTagManager.cs': 'fd627ed71f631aa6e2341568db5903908ab4309d7db468b0a1a6bba3411dd3ae', 'Runtime/GameplayTagQuery.cs': '57f38ae71f0fbcf7c8b0ac8be8d2c2d0052435a594bcecbdc43d8ba2bc0fef62', 'Runtime/GameplayTagSettings.cs': '99826726b17ac0553e3a218a6024ec91471a7a3267dafbe1a2c0cb2da31b5122'})
for name,sha in checks.items():assert digest(root/name)==sha,name
keys=['new-union','new-exact-intersection','target-copy-nonempty','target-intersection-nonempty','full-lifecycle'];selected=[]
for n in [0,1,8,64,1024]:
 for key in keys:selected.append((str(n)+':'+key,'full-final',50))
 for key in maps['focused-final']:
  if key.startswith(str(n)+':') and not key.endswith(':focus-copy'):selected.append((key,'focused-final',100))
assert len(selected)==77 and len({key for key,_,_ in selected})==77
(out/'iterations.csv').write_text(''.join(key+','+str(maps[source][key])+'\n' for key,source,_ in selected))
with (out/'count-provenance.csv').open('w') as f:
 w=csv.writer(f);w.writerow(['n','case','count_source','count_target_ms','iterations','source_map_sha256'])
 for key,source,target in selected:
  n,case=key.split(':',1);w.writerow([n,case,source,target,maps[source][key],full_sha if source=='full-final' else focus_sha])
(out/'source-identity-audit.json').write_text(json.dumps({'original_full_map_sha256':full_sha,'original_focused_map_sha256':focus_sha,'selected_count_map_sha256':digest(out/'iterations.csv'),'full_rows':25,'focused_rows':52,'calibration_performed':False,'verified_source_hashes':checks},indent=2)+'\n')
PYIDENTITY
"$dotnet" --version > "$out/sdk-version.txt"
for version in B1 B11Core; do
 runtime="$here/Baselines/B1/Runtime"
 [[ "$version" == B11Core ]] && runtime="$root/Runtime"
 "$dotnet" build "$here/BenchmarkCore.csproj" -c Release -v quiet -p:RuntimePath="$runtime" -p:PhaseApis=true -p:BaseIntermediateOutputPath="$here/obj/core-$version/" -p:OutputPath="$here/bin/core-$version/" > "$out/$version.build.log" 2>&1 || { cat "$out/$version.build.log"; exit 1; }
done
if [[ "${1:-}" == --build-only ]]; then inventory > "$out/source.after.sha256"; cmp "$out/source.before.sha256" "$out/source.after.sha256"; exit 0; fi
orders=("B1 B11Core" "B11Core B1" "B11Core B1" "B1 B11Core")
for round in 1 2 3 4; do
 for version in ${orders[$((round-1))]}; do
  "$dotnet" "$here/bin/core-$version/BenchmarkCore.dll" measure "$version" "$out/iterations.csv" "$round" > "$out/$version.round$round.csv" 2> "$out/$version.round$round.environment.log"
 done
done
inventory > "$out/source.after.sha256"
cmp "$out/source.before.sha256" "$out/source.after.sha256" || { echo "Source changed during benchmark; results not comparable" >&2; exit 1; }
python3 - "$here" "$out" <<'PY'
import csv,hashlib,json,pathlib,statistics,sys
here,out=map(pathlib.Path,sys.argv[1:]);audit=json.loads((out/'source-identity-audit.json').read_text())
for file,key in [(out/'iterations.csv','selected_count_map_sha256'),(here/'results/full-final/iterations.csv','original_full_map_sha256'),(here/'results/focused-final/iterations.csv','original_focused_map_sha256')]:assert hashlib.sha256(file.read_bytes()).hexdigest()==audit[key],file
rows=[]
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
