#!/usr/bin/env python3
"""Attribute timed allocation telemetry without changing shipping code or waiving the release gate."""
import json, os, pathlib, subprocess, tempfile
from dense_audit import benchmark_fixture
from integer_audit import project
ROOT=pathlib.Path(__file__).resolve().parent.parent
OUT=ROOT/'artifacts/measurement-probe'
OUT.mkdir(parents=True,exist_ok=True)
ENV=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')

def replace(text,old,new):
    if text.count(old)!=1: raise RuntimeError('Fixture context not unique: '+old)
    return text.replace(old,new)

with tempfile.TemporaryDirectory(prefix='allocation-attribution-') as tmp:
    root=pathlib.Path(tmp)
    (root/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
    (root/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    original=benchmark_fixture((ROOT/'Audit~/FourWay.cs').read_text(),dense=True,prepared=True)
    for mode in ('original','phased','worker'):
        text=original
        if mode!='original':
            text=replace(text,'var allocations = new double[7];','var allocations = new double[7];\n            var timerStartBytes = new long[7];\n            var actionBytes = new long[7];\n            var timerEndBytes = new long[7];\n            var allocationOnlyBytes = new long[7];')
            text=replace(text,'long start = Stopwatch.GetTimestamp();','long start = Stopwatch.GetTimestamp();\n                long beforeAction = GC.GetAllocatedBytesForCurrentThread();')
            text=replace(text,'long elapsed = Stopwatch.GetTimestamp() - start;','long afterAction = GC.GetAllocatedBytesForCurrentThread();\n                long elapsed = Stopwatch.GetTimestamp() - start;\n                long afterTimer = GC.GetAllocatedBytesForCurrentThread();\n                timerStartBytes[sample] = beforeAction - allocated;\n                actionBytes[sample] = afterAction - beforeAction;\n                timerEndBytes[sample] = afterTimer - afterAction;')
            text=replace(text,'GC.KeepAlive(sink);','GC.KeepAlive(sink);\n                long onlyStart = GC.GetAllocatedBytesForCurrentThread();\n                for (int i = 0; i < iterations; i++) action();\n                allocationOnlyBytes[sample] = GC.GetAllocatedBytesForCurrentThread() - onlyStart;')
            text=replace(text,'ns, allocated_bytes = allocations,','ns, allocated_bytes = allocations, timerStartBytes, actionBytes, timerEndBytes, allocationOnlyBytes,')
        if mode=='worker':
            start='        BenchmarkUniverse(10000);'
            end='        var result = new { variant = args[1]'
            text=replace(text,start,'        Exception workerError = null;\n        var thread = new System.Threading.Thread(() => { try {\n'+start)
            text=replace(text,end,'        } catch (Exception e) { workerError = e; } });\n        thread.Start(); thread.Join();\n        if (workerError != null) throw new Exception("Worker failed", workerError);\n'+end)
        host=root/mode;host.mkdir()
        fixture=host/'Probe.cs';fixture.write_text(text)
        (OUT/(mode+'-fixture.cs')).write_text(text)
        project(host/'Probe.csproj',[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',fixture],'CANDIDATE')
        commands=[(['dotnet','build',host/'Probe.csproj','-c','Release','-o',host/'out'],mode+'-build.log')]
        for r in range(2): commands.append((['dotnet','exec',host/'out/Probe.dll',OUT/(mode+'-'+str(r)+'.json'),mode],mode+'-'+str(r)+'.log'))
        for command,name in commands:
            p=subprocess.run(list(map(str,command)),cwd=host,env=ENV,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=300)
            (OUT/name).write_text(p.stdout);print(p.stdout,flush=True)
            if p.returncode: raise RuntimeError(name+' failed')
    report=[]
    for mode in ('original','phased','worker'):
        for r in range(2):
            payload=json.loads((OUT/(mode+'-'+str(r)+'.json')).read_text())
            for row in payload['rows']:
                if row['status']!='measured':raise RuntimeError('Invalid probe row')
                if any(row['allocated_bytes']) or any(row.get('allocationOnlyBytes',[])):
                    report.append({'mode':mode,'round':r,**{k:row[k] for k in ('operation','size','universe','distribution','iterations','allocated_bytes','timerStartBytes','actionBytes','timerEndBytes','allocationOnlyBytes') if k in row}})
    (OUT/'attribution.json').write_text(json.dumps(report,indent=2))
    print('ALLOCATION ATTRIBUTION '+json.dumps(report),flush=True)
    print('Diagnostic only. Original raw samples retained. Production and strict gates unchanged.',flush=True)
