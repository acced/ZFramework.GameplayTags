#!/usr/bin/env python3
"""Negative-control attribution. Neither negative-control timings nor dirty samples are rankings."""
import json, os, pathlib, re, subprocess, tempfile
from dense_audit import benchmark_fixture
from integer_audit import project
ROOT=pathlib.Path(__file__).resolve().parent.parent
OUT=ROOT/'artifacts/measurement-probe';OUT.mkdir(parents=True,exist_ok=True)
ENV=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
def replace(text,old,new):
    if text.count(old)!=1:raise RuntimeError('Fixture context not unique: '+old)
    return text.replace(old,new)
with tempfile.TemporaryDirectory(prefix='allocation-attribution-') as tmp:
    root=pathlib.Path(tmp)
    (root/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
    (root/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    original=benchmark_fixture((ROOT/'Audit~/FourWay.cs').read_text(),dense=True,prepared=True)
    # Restore the old measurement environment ONLY in these diagnostic fixture copies.
    original,count=re.subn(r'            // BEGIN SETTLED SETUP:.*?            // END SETTLED SETUP\n','',original,flags=re.S)
    if count!=1:raise RuntimeError('Missing settled preparation marker')
    modes=('original','noop','settled','noop-settled')
    for mode in modes:
        text=original
        if 'noop' in mode:
            text=replace(text,'            verify();\n            for (int i = 0; i < 100; i++) action();',
                '            if (operation == "exact" || operation == "exact_miss") action = () => checksum++;\n            verify();\n            for (int i = 0; i < 100; i++) action();')
        if 'settled' in mode:
            text=replace(text,'            var ns = new double[7];',
                '            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);\n            GC.WaitForPendingFinalizers();\n            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);\n            var ns = new double[7];')
        text=text.replace('measurement_protocol = "settled-setup-v2"','measurement_protocol = "diagnostic-'+mode+'"')
        text=replace(text,'var allocations = new double[7];','var allocations = new double[7];\n            var collections = new int[7];')
        text=replace(text,'long allocated = GC.GetAllocatedBytesForCurrentThread();',
            'int beforeCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);\n                long allocated = GC.GetAllocatedBytesForCurrentThread();')
        text=replace(text,'GC.KeepAlive(sink);',
            'collections[sample] = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) - beforeCollections;\n                GC.KeepAlive(sink);')
        text=replace(text,'ns, allocated_bytes = allocations,','ns, allocated_bytes = allocations, collections,')
        host=root/mode;host.mkdir();fixture=host/'Probe.cs';fixture.write_text(text)
        (OUT/(mode+'-fixture.cs')).write_text(text)
        project(host/'Probe.csproj',[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',fixture],'CANDIDATE')
        commands=[(['dotnet','build',host/'Probe.csproj','-c','Release','-o',host/'out'],mode+'-build.log')]
        for r in range(2):commands.append((['dotnet','exec',host/'out/Probe.dll',OUT/(mode+'-'+str(r)+'.json'),mode],mode+'-'+str(r)+'.log'))
        for command,name in commands:
            p=subprocess.run(list(map(str,command)),cwd=host,env=ENV,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=300)
            (OUT/name).write_text(p.stdout);print(p.stdout,flush=True)
            if p.returncode:raise RuntimeError(name+' failed')
    report=[]
    for mode in modes:
        for r in range(2):
            payload=json.loads((OUT/(mode+'-'+str(r)+'.json')).read_text())
            for row in payload['rows']:
                if row['status']!='measured':raise RuntimeError('Invalid probe row')
                if any(row['allocated_bytes']):
                    report.append({'mode':mode,'round':r,**{k:row[k] for k in ('operation','size','universe','distribution','iterations','allocated_bytes','collections')}})
    (OUT/'attribution.json').write_text(json.dumps(report,indent=2))
    print('NEGATIVE CONTROL ATTRIBUTION '+json.dumps(report),flush=True)
    print('Diagnostic only: no samples were subtracted, replaced, or discarded.',flush=True)
