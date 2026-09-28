#!/usr/bin/env python3
"""Additional comparison with the immediately preceding indexed-Dense implementation."""
import argparse, json, os, pathlib, subprocess, tempfile
import dense_results
from integer_audit import project

ROOT=pathlib.Path(__file__).resolve().parent.parent
PREVIOUS='08c27ff39ce7c4fae70320ccdac07d14f0f6fd06'
PREVIOUS_TREE='740b7dcc0a6c9c40f33a1dee334c2edf8d838332'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results',required=True)
    parser.add_argument('--previous',required=True)
    parser.add_argument('--rounds',type=int,default=3)
    args=parser.parse_args()
    if args.rounds<1:parser.error('rounds must be positive')
    destination=pathlib.Path(args.results).resolve()/'previous-pass';destination.mkdir(exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    with tempfile.TemporaryDirectory(prefix='shipping-count-tests-') as directory:
        host=pathlib.Path(directory)
        (host/'global.json').write_text(json.dumps({'sdk':{'version':'8.0.425','rollForward':'disable'}}))
        (host/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        project(host/'CountTests.csproj',[ROOT/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs',ROOT/'Audit~/DenseCountTests.cs'])
        for command,log in [(['dotnet','build',host/'CountTests.csproj','-c','Release','-o',host/'out'],'count-build.log'),
                            (['dotnet','exec',host/'out/CountTests.dll',destination/'count-tests.json'],'count-tests.log')]:
            p=subprocess.run(list(map(str,command)),cwd=host,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=1200)
            (destination/log).write_text(p.stdout);print(p.stdout,flush=True)
            if p.returncode:raise RuntimeError(log+' failed')
    # This is a separately named report. The initial-5b151da comparison and all raw samples stay intact.
    # The shared verifier reconstructs this exact Git tree and rejects a mismatching archive.
    dense_results.INITIAL=PREVIOUS
    dense_results.INITIAL_TREE=PREVIOUS_TREE
    dense_results.refinement(pathlib.Path(args.previous).resolve(),destination,'dotnet',args.rounds)
    print('Immediate-predecessor comparison completed: '+PREVIOUS,flush=True)

if __name__=='__main__':main()
