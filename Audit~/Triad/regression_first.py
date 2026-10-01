#!/usr/bin/env python3
"""Reproduce the first pilot's singleton-copy defect, verify its correction, then run full triad."""
import argparse, hashlib, json, os, pathlib, subprocess, sys, tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Audit~'))
from integer_audit import project

p=argparse.ArgumentParser();p.add_argument('--output',required=True);p.add_argument('--rounds',type=int,default=3);a=p.parse_args()
out=pathlib.Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True)
old_commit='9718e9df1bc7a47c054242a78ab1a524d45b346f'
old=subprocess.check_output(['git','show',old_commit+':Audit~/Triad/RobinMicroBitmapSet.cs'],cwd=ROOT).decode()
current=(ROOT/'Audit~/Triad/RobinMicroBitmapSet.cs').read_text()
added='''            // The source may have an allocated table but only one live record.
            // ReserveRecords(1) leaves an inline destination; do not call PlaceUnique on it.
            if (source.used == 1 && slots == null)
            {
                inline = source.inline;
                if (source.slots != null)
                    for (int i = 0; i < source.slots.Length; i++)
                        if (source.slots[i] != 0) { inline = source.slots[i]; break; }
                used = 1; count = source.count; return;
            }
'''
marker='            Clear(); ReserveRecords(source.used);'
if old.count(marker)!=1 or current != old.replace(marker,added+marker):raise RuntimeError('Correction differs from reviewed minimal fix')
env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
with tempfile.TemporaryDirectory(prefix='copy-regression-') as d:
    h=pathlib.Path(d);(h/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    (h/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
    for name,source in [('old',old),('fixed',current)]:
        cs=h/(name+'.cs');cs.write_text(source)
        (out/(name+'-RobinMicroBitmapSet.cs')).write_text(source)
        project(h/(name+'.csproj'),[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/MicroBitmapSet.cs',cs,ROOT/'Audit~/Triad/CopyRegression.cs'])
        commands=[('build',['dotnet','build',str(h/(name+'.csproj')),'-c','Release','-p:BaseIntermediateOutputPath=obj-'+name+'/', '-o',str(h/name)]),
                  ('run',['dotnet','exec',str(h/name/(name+'.dll')),name])]
        for stage,cmd in commands:
            r=subprocess.run(cmd,cwd=h,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=180)
            (out/(name+'-'+stage+'.log')).write_text(r.stdout);print(r.stdout,flush=True)
            if r.returncode:raise RuntimeError(name+' '+stage+' failed')
            if name=='old' and stage=='run' and 'EXPECTED_OLD_FAILURE NullReferenceException' not in r.stdout:raise RuntimeError('Missing specific reproduction')
            if name=='fixed' and stage=='run' and 'COPY_REGRESSION' not in r.stdout:raise RuntimeError('Missing fixed pass')
(out/'regression.json').write_text(json.dumps({'old_commit':old_commit,'old_failure_reproduced':'NullReferenceException','fixed_passed':True,
    'minimal_fix_verified':True,'old_sha256':hashlib.sha256(old.encode()).hexdigest(),'fixed_sha256':hashlib.sha256(current.encode()).hexdigest()},indent=2))
subprocess.run([sys.executable,str(ROOT/'Audit~/Triad/run_triad.py'),'--output',str(out/'full'),'--rounds',str(a.rounds)],cwd=ROOT,env=env,check=True)
