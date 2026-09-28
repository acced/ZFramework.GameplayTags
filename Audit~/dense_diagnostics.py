#!/usr/bin/env python3
"""Preserve every timed-loop allocation anomaly; run independent allocation and bit-count experiments."""
import argparse, json, os, pathlib, statistics, subprocess, tempfile
from integer_audit import project
ROOT=pathlib.Path(__file__).resolve().parent.parent

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--results',required=True);args=parser.parse_args()
    results=pathlib.Path(args.results).resolve();diagnostics=results/'diagnostics';diagnostics.mkdir(exist_ok=True)
    anomalies=[]
    for path in sorted(results.glob('prepared-*-r*.json')):
        for row in json.loads(path.read_text())['rows']:
            if row['status']=='measured' and any(x != 0 for x in row['allocated_bytes']):
                anomalies.append({'file':path.name,'operation':row['operation'],'members':row['size'],'definitions':row['universe'],
                    'distribution':row['distribution'],'iterations':row['iterations'],'bytes_per_operation':row['allocated_bytes'],
                    'total_bytes_per_sample':[round(x*row['iterations']) for x in row['allocated_bytes']]})
    (diagnostics/'timed-allocation-anomalies.json').write_text(json.dumps(anomalies,indent=2))
    print('ALL TIMED ALLOCATION ANOMALIES: '+json.dumps(anomalies),flush=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    def run(command,log,cwd):
        p=subprocess.run(list(map(str,command)),cwd=cwd,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=1200)
        (diagnostics/log).write_text(p.stdout);print(p.stdout,flush=True)
        if p.returncode:raise RuntimeError(log+' failed')
    with tempfile.TemporaryDirectory(prefix='dense-diagnostics-') as directory:
        work=pathlib.Path(directory)
        (work/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
        (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        project(work/'Allocation.csproj',[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/DenseAllocationProbe.cs'])
        run(['dotnet','build',work/'Allocation.csproj','-c','Release','-o',work/'allocation'],'allocation-build.log',work)
        run(['dotnet','exec',work/'allocation/Allocation.dll',diagnostics/'allocation-only.json'],'allocation-only.log',work)
        project(work/'Kernel.csproj',[ROOT/'Audit~/DenseKernelProbe.cs'])
        run(['dotnet','build',work/'Kernel.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-kernel/','-o',work/'kernel'],'kernel-build.log',work)
        for r in range(3):run(['dotnet','exec',work/'kernel/Kernel.dll',diagnostics/('kernel-'+str(r)+'.json')],'kernel-'+str(r)+'.log',work)
    table={}
    for r in range(3):
        for row in json.loads((diagnostics/('kernel-'+str(r)+'.json')).read_text())['rows']:
            table.setdefault((row['words'],row['kernel']),[]).append(row)
    comparison=[]
    for words in sorted({key[0] for key in table}):
        medians={name:statistics.median(statistics.median(s['ns']) for s in values) for (length,name),values in table.items() if length==words}
        comparison.append({'words':words,'median_ns':medians,'or_then_carry_vs_fused_swar':medians['union-then-carry-save']/medians['fused-union-swar']})
    (diagnostics/'kernel-comparison.json').write_text(json.dumps(comparison,indent=2))
    print('KERNEL COMPARISON (not full API): '+json.dumps(comparison),flush=True)
    print('No anomaly was removed from raw results or reclassified as a zero-allocation sample.',flush=True)

if __name__=='__main__':main()
