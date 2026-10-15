"""Bounded same-host before/after screen, not a universal performance acceptance test."""
import csv, hashlib, json, os, pathlib, statistics, subprocess, sys
root=pathlib.Path(__file__).resolve().parents[2]
out=pathlib.Path(sys.argv[1]).resolve()
out.mkdir(parents=True,exist_ok=False)
env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1')
versions={'uploaded':root/'Tests~/Hardening/Baseline/Runtime','hardened':root/'Runtime'}
binaries={}
for name,source in versions.items():
    b=out/name;b.mkdir()
    cmd=['dotnet','build',str(root/'Tools~/Hardening/Benchmark.csproj'),'-c','Release','--nologo',f'-p:RuntimePath={source}',f'-p:BaseIntermediateOutputPath={b}/obj/',f'-p:OutputPath={b}/bin/']
    with (b/'build.log').open('w') as f:subprocess.run(cmd,cwd=root,env=env,stdout=f,stderr=subprocess.STDOUT,check=True,timeout=180)
    binaries[name]=b/'bin/Benchmark.dll'
subprocess.run(['dotnet','build-server','shutdown'],env=env,stdout=subprocess.DEVNULL,check=True)
headers='case,n,calls,ns_per_op,bytes_per_op,batch_ms,gc0,gc1,gc2,status,sink'.split(',')
ops=['hit','miss','hierarchy','delete-add','enumerate','new-shuffled','new-sorted','copy','union-new','intersection-new','intersection-reuse','clear-refill','copy-clear','count-cycle']
rows=[];pilots=[];counts={}
def run(name,op,n,calls):
    p=subprocess.run(['dotnet',str(binaries[name]),op,str(n),str(calls)],cwd=root,env=env,text=True,capture_output=True,check=True,timeout=90)
    values=next(csv.reader([p.stdout.strip().splitlines()[-1]]));assert len(values)==len(headers)
    row=dict(zip(headers,values));assert row['status']=='PASS'
    return row
with (out/'pilots.csv').open('w') as pf,(out/'raw.csv').open('w') as rf:
    pw=csv.DictWriter(pf,fieldnames=['version']+headers);pw.writeheader()
    rw=csv.DictWriter(rf,fieldnames=['version','pair','short']+headers);rw.writeheader()
    for n in (8,1024):
        for op in ops:
            c=1024
            # Baseline-only pilot doubles to >=100ms, with an explicit maximum.
            while True:
                r=run('uploaded',op,n,c);pw.writerow(dict(version='uploaded',**r));pf.flush()
                if float(r['batch_ms'])>=100 or c>=16777216:break
                c*=2
            counts[f'{n}:{op}']=c
            for pair,order in enumerate([('uploaded','hardened'),('hardened','uploaded'),('hardened','uploaded'),('uploaded','hardened')]):
                for name in order:
                    r=run(name,op,n,c);r=dict(version=name,pair=pair,short=float(r['batch_ms'])<100,**r)
                    rw.writerow(r);rf.flush();rows.append(r)
with (out/'comparison.csv').open('w') as f:
    w=csv.writer(f);w.writerow(['n','case','uploaded_median_ns','hardened_median_ns','paired_median_after_before','slower_pairs','uploaded_B','hardened_B'])
    for n in (8,1024):
        for op in ops:
            a=sorted([r for r in rows if r['version']=='uploaded' and r['n']==str(n) and r['case']==op],key=lambda r:r['pair'])
            b=sorted([r for r in rows if r['version']=='hardened' and r['n']==str(n) and r['case']==op],key=lambda r:r['pair'])
            ratios=[float(y['ns_per_op'])/float(x['ns_per_op']) for x,y in zip(a,b)]
            w.writerow([n,op,statistics.median(float(x['ns_per_op']) for x in a),statistics.median(float(x['ns_per_op']) for x in b),statistics.median(ratios),sum(r>1 for r in ratios),statistics.median(float(x['bytes_per_op']) for x in a),statistics.median(float(x['bytes_per_op']) for x in b)])
meta={'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'sdk':subprocess.check_output(['dotnet','--version'],cwd=root,text=True).strip(),'tiered_compilation':False,'formal_samples':len(rows),'short_samples':sum(r['short'] for r in rows),'counts':counts,'limitations':['Linux x64 .NET only; no Unity/Mono/IL2CPP run','Four paired observations, no A/A, no universal acceptance','Methods include loop/delegate and consumer; no overhead subtraction','Allocation measurements do not measure retained memory or GC pauses'],'source_sha256':{name:{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.cs')} for name,source in versions.items()}}
(out/'identity.json').write_text(json.dumps(meta,indent=2)+'\n')
print(json.dumps({k:meta[k] for k in ['source_commit','formal_samples','short_samples']}))
