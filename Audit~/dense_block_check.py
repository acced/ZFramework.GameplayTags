#!/usr/bin/env python3
"""Execute block-directory and shipping count regressions before any benchmark gate."""
import json, os, pathlib, subprocess, tempfile
from integer_audit import project
ROOT=pathlib.Path(__file__).resolve().parent.parent
OUT=ROOT/'artifacts/block-regression';OUT.mkdir(parents=True,exist_ok=True)
env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
with tempfile.TemporaryDirectory(prefix='dense-block-check-') as directory:
    work=pathlib.Path(directory)
    (work/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
    (work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    for name in ('DenseBlockTests','DenseCountTests'):
        host=work/name;host.mkdir()
        proj=host/(name+'.csproj')
        project(proj,[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~'/(name+'.cs')])
        for command,log in [(['dotnet','build',proj,'-c','Release','-o',host/'out'],name+'-build.log'),
                            (['dotnet','exec',host/'out'/(name+'.dll'),OUT/(name+'.json')],name+'-tests.log')]:
            p=subprocess.run(list(map(str,command)),cwd=host,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=300)
            (OUT/log).write_text(p.stdout);print(p.stdout,flush=True)
            if p.returncode:raise RuntimeError(log+' failed')
