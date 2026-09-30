#!/usr/bin/env python3
"""Supplemental identical-input comparison: old Auto, rejected full indexed Dense, shipping packed bitmap."""
import argparse,hashlib,json,math,os,pathlib,statistics,subprocess,tempfile,zipfile
from integer_audit import project,archive_tree
from packed_audit import fixture
ROOT=pathlib.Path(__file__).resolve().parent.parent
REFERENCES={
    'auto':('84729cc3b6e1b4681328806df2d7c8a1d8f568f6','f0131462eb38065f833f898b7123e41dda9e0978'),
    'indexed-dense':('3781d1b6c66997ec37fcb6223cb1a1d60c7e19fb','b1e2d7d91dc49512ad2edfcaae6417c6a0b0fbb2')}

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True);p.add_argument('--rounds',type=int,default=3);a=p.parse_args()
    if a.rounds<1:p.error('rounds must be positive')
    output=pathlib.Path(a.output).resolve();output.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    def run(cmd,log,cwd):
        r=subprocess.run(list(map(str,cmd)),cwd=cwd,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=900)
        (output/log).write_text(r.stdout);print(r.stdout,flush=True)
        if r.returncode:raise RuntimeError(log+' failed')
    with tempfile.TemporaryDirectory(prefix='packed-dense-control-') as directory:
        work=pathlib.Path(directory);(work/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}');(work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        sources={};inventory={};builds={}
        for name,(sha,tree) in REFERENCES.items():
            path=work/(name+'.zip')
            with path.open('wb') as f:subprocess.run(['git','archive','--format=zip',sha],cwd=ROOT,stdout=f,check=True)
            source=work/name;source.mkdir()
            with zipfile.ZipFile(path) as z:
                if z.comment.decode('ascii')!=sha or archive_tree(z)!=tree:raise RuntimeError('Wrong reference')
                for entry in z.infolist():
                    pth=pathlib.PurePosixPath(entry.filename)
                    if pth.is_absolute() or '..' in pth.parts or (entry.external_attr>>16)&0o170000==0o120000:raise RuntimeError('Invalid archive entry')
                z.extractall(source)
            sources[name]=source;inventory[name]={'commit':sha,'tree':tree}
        sources['candidate']=ROOT
        inventory['candidate']={'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'runtime_sha256':hashlib.sha256((ROOT/'Runtime/RuntimeTagSet.cs').read_bytes()).hexdigest()}
        original=(ROOT/'Audit~/FourWay.cs').read_text()
        for name,source in sources.items():
            host=work/('host-'+name);host.mkdir()
            includes=[source/'Runtime/**/*.cs',ROOT/'Audit~/UnityStubs.cs']
            for prepared in (False,True):
                label=name+('-prepared' if prepared else '')
                code=fixture(original,packed=name!='auto',prepared=prepared)
                if name=='indexed-dense':code=code.replace('ranked-packed-bitmap','full-indexed-dense')
                path=host/(label+'.cs');path.write_text(code);(output/(label+'-fixture.cs')).write_text(code)
                csproj=host/(label+'.csproj');project(csproj,includes+[path],'CANDIDATE')
                out=host/('out-'+label)
                run(['dotnet','build',csproj,'-c','Release','-p:BaseIntermediateOutputPath=obj-'+label+'/','-o',out],label+'-build.log',host)
                builds[label]=(out/(label+'.dll'),host)
        for round_number in range(a.rounds):
            order=['auto','indexed-dense','candidate'];offset=round_number%3;order=order[offset:]+order[:offset]
            for prepared in (False,True):
                for name in (order if not prepared else list(reversed(order))):
                    label=name+('-prepared' if prepared else '');dll,cwd=builds[label]
                    run(['dotnet','exec',dll,output/(label+'-r'+str(round_number)+'.json'),label,'Auto'],label+'-r'+str(round_number)+'.log',cwd)
        summaries={}
        for prepared in (False,True):
            tables={};expected=None;digests={}
            for name in sources:
                label=name+('-prepared' if prepared else '');table={}
                for round_number in range(a.rounds):
                    rows=json.loads((output/(label+'-r'+str(round_number)+'.json')).read_text())['rows'];seen=set()
                    for row in rows:
                        key=(row['operation'],row['size'],row['universe'],row['distribution'])
                        if key in seen or row['status']!='measured':raise RuntimeError('Invalid workload result: '+str(row))
                        if len(row['ns'])!=7 or len(row['allocated_bytes'])!=7:raise RuntimeError('Missing samples')
                        if any(not math.isfinite(x) or x<=0 for x in row['ns']):raise RuntimeError('Invalid timing')
                        if any(not math.isfinite(x) or x<0 or (prepared and x!=0) for x in row['allocated_bytes']):raise RuntimeError('Invalid allocation')
                        if key in digests and digests[key]!=row['inputDigest']:raise RuntimeError('Input mismatch')
                        digests[key]=row['inputDigest'];seen.add(key);table.setdefault(key,[]).append(row)
                    if expected is None:expected=seen
                    if seen!=expected:raise RuntimeError('Missing cases')
                tables[name]=table
            comparison=[]
            for key in sorted(expected):
                metrics={name:{'median_ns':statistics.median(statistics.median(row['ns']) for row in table[key]),'bytes_per_op':statistics.median(statistics.median(row['allocated_bytes']) for row in table[key]),'raw':table[key]} for name,table in tables.items()}
                old=metrics['auto']['median_ns'];dense=metrics['indexed-dense']['median_ns'];candidate=metrics['candidate']['median_ns']
                comparison.append({'operation':key[0],'members':key[1],'definitions':key[2],'distribution':key[3],
                    'ratio_to_auto':candidate/old,'ratio_to_indexed_dense':candidate/dense,'variants':metrics})
            label='prepared' if prepared else 'fresh'
            (output/(label+'-comparison.json')).write_text(json.dumps(comparison,indent=2))
            summaries[label]={'rows':len(comparison),'faster_than_auto':sum(r['ratio_to_auto']<1 for r in comparison),
                'faster_than_indexed_dense':sum(r['ratio_to_indexed_dense']<1 for r in comparison),
                'not_faster_than_auto':[{k:v for k,v in r.items() if k!='variants'} for r in comparison if r['ratio_to_auto']>=1]}
            for r in comparison:
                if r['operation']!='exact_miss' and (r['definitions'],r['members'],r['distribution']) in [(10000,1024,'contiguous'),(10000,1024,'scattered'),(262144,1,'scattered'),(262144,8,'scattered'),(262144,128,'scattered')]:
                    print('DENSE_CONTROL',label,json.dumps({k:v for k,v in r.items() if k!='variants'}),json.dumps({k:{x:y for x,y in v.items() if x!='raw'} for k,v in r['variants'].items()}),flush=True)
        summary={'references':inventory,'results':summaries,'protocol':'settled-setup-v2','rounds':a.rounds,'release_approved':False,'native_unity':'not_run','note':'Supplemental fixture; do not combine these samples with the full-library comparison.'}
        (output/'summary.json').write_text(json.dumps(summary,indent=2));print('DENSE_CONTROL_SUMMARY',json.dumps(summary),flush=True)
if __name__=='__main__':main()
