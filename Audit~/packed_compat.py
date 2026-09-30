#!/usr/bin/env python3
"""Port the old semantic suite to a single bitmap representation. Shipping files are never changed."""
import argparse,json,os,pathlib,shutil,subprocess,tempfile
from integer_audit import project
from checks import verify_package,compile_documentation,compile_split_assemblies
ROOT=pathlib.Path(__file__).resolve().parent.parent

def adapt(text):
    changes=[]
    def replace(old,new):
        nonlocal text
        if old not in text:raise RuntimeError('Reconcile legacy test adapter: '+old)
        changes.append({'old':old,'new':new,'occurrences':text.count(old)})
        text=text.replace(old,new)
    replace('private static readonly TagSetStorage[] Modes = { TagSetStorage.Sparse, TagSetStorage.Dense };','private enum FixtureMode { Bitmap }\n    private static readonly FixtureMode[] Modes = { FixtureMode.Bitmap };')
    replace('Throws<ArgumentOutOfRangeException>(() => new RuntimeTagSet(large, 0, (TagSetStorage)99));','Check(typeof(RuntimeTagSet).GetProperty("Storage") == null);')
    replace('Check(new RuntimeTagSet(large, 8).Storage == TagSetStorage.Sparse);','Check(new RuntimeTagSet(large, 8).ReservedMemberCapacity >= 8);')
    replace('Check(new RuntimeTagSet(large, 1024).Storage == TagSetStorage.Dense);','Check(new RuntimeTagSet(large, 1024).ReservedMemberCapacity >= 1024);')
    replace('Check(new RuntimeTagSet(large, 1024).BufferBytes == 1256);','Check(new RuntimeTagSet(large, 8).BufferBytes < 1256);')
    replace('Check(set.Storage == mode, "storage changed implicitly");','Check(typeof(RuntimeTagSet).GetProperty("Storage") == null, "storage selector must be absent");')
    replace('TagSetStorage','FixtureMode')
    replace('FixtureMode.Sparse','FixtureMode.Bitmap');replace('FixtureMode.Dense','FixtureMode.Bitmap')
    replace('FixtureMode mode, int capacity = 0','FixtureMode mode = FixtureMode.Bitmap, int capacity = 0')
    replace(', FixtureMode.Bitmap)',')')
    for capacity,mode in [('Math.Max(capacity, input.Length)','mode'),('registry.Count','mode'),('registry.Count','outputMode'),('0','mode'),('registry.Count','rm')]:
        replace('new RuntimeTagSet(registry, '+capacity+', '+mode+')','new RuntimeTagSet(registry, '+capacity+')')
    replace('Modes[random.Next(2)]','Modes[random.Next(Modes.Length)]')
    replace('empty.Capacity == 0 && one.Capacity == 1','empty.BufferBytes == 24 && one.ReservedMemberCapacity >= 1')
    replace('long before = GC.GetAllocatedBytesForCurrentThread();','GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();\n                long before = GC.GetAllocatedBytesForCurrentThread();')
    replace('eight-storage-combinations-exhaustive-six-element-sets','single-bitmap-exhaustive-six-element-sets')
    replace('frozen-query-random-reference-and-both-storages','frozen-query-random-reference-bitmap')
    return text,changes

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True);p.add_argument('--dotnet',default='dotnet');a=p.parse_args()
    output=pathlib.Path(a.output).resolve();output.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    def run(command,log,cwd):
        result=subprocess.run(list(map(str,command)),cwd=cwd,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=600)
        (output/log).write_text(result.stdout);print(result.stdout,flush=True)
        if result.returncode:raise RuntimeError(log+' failed')
        return 0
    package=verify_package(ROOT)
    with tempfile.TemporaryDirectory(prefix='packed-compat-') as temp:
        host=pathlib.Path(temp)
        (host/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}')
        (host/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        text,changes=adapt((ROOT/'Audit~/IntegerTests.cs').read_text())
        (host/'PortedTests.cs').write_text(text);(output/'PortedTests.cs').write_text(text)
        (output/'adaptations.json').write_text(json.dumps(changes,indent=2))
        for name in ['UnityStubs.cs','NativeTestStubs.cs']:shutil.copy2(ROOT/'Audit~'/name,host/name)
        sources=[ROOT/(d+'/**/*.cs') for d in ('Runtime','Editor','Samples~')]
        project(host/'Audit.csproj',sources+[host/'UnityStubs.cs',host/'PortedTests.cs',ROOT/'Audit~/IntegerTestSupport.cs'])
        run([a.dotnet,'build',host/'Audit.csproj','-c','Release','-o',host/'out'],'build.log',host)
        dll=host/'out/Audit.dll';run([a.dotnet,'exec',dll,output/'legacy'],'legacy.log',host)
        docs=compile_documentation(ROOT,host,dll,a.dotnet,run)
        split=compile_split_assemblies(ROOT,host,a.dotnet,run)
        project(host/'Generated.csproj',[output/'legacy/GeneratedBindings.cs'],references=[dll],executable=False)
        run([a.dotnet,'build',host/'Generated.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-generated/','-o',host/'generated'],'generated.log',host)
        summary={'package':package,'documentation':docs,'split':split,'native_unity':'not_run','release_approved':False}
        (output/'summary.json').write_text(json.dumps(summary,indent=2));print('COMPATIBILITY',json.dumps(summary),flush=True)
if __name__=='__main__':main()
