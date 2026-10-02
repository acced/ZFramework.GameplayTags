#!/usr/bin/env python3
"""Download exact completed archives, rerun their archived verifier, retain all data."""
import pathlib,subprocess,os,json,hashlib,zipfile,io,csv,sys

def extract(z,path):
    path=path.resolve();path.mkdir(parents=True,exist_ok=True)
    for name in z.namelist():
        p=(path/name).resolve()
        if p!=path and path not in p.parents:raise ValueError('unsafe ZIP path')
    z.extractall(path)

def main():
    out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
    repo=os.environ['GITHUB_REPOSITORY'];runid=os.environ['GITHUB_RUN_ID']
    api=lambda url:subprocess.check_output(['gh','api',url])
    artifacts=json.loads(api(f'/repos/{repo}/actions/runs/{runid}/artifacts'))['artifacts']
    combined=[];compared=[];negative=[];meta={};reference=None;generation=None
    for arch in ('x64','arm64'):
        matches=[a for a in artifacts if a['name']==f'dense-bulk-{arch}-{runid}' and not a['expired']]
        assert len(matches)==1
        item=matches[0];data=api(f'/repos/{repo}/actions/artifacts/{item["id"]}/zip')
        digest=hashlib.sha256(data).hexdigest();assert item['digest']=='sha256:'+digest
        (out/(arch+'-original.zip')).write_bytes(data)
        folder=out/arch
        with zipfile.ZipFile(io.BytesIO(data)) as z:extract(z,folder)
        results=folder/'results';source=(results/'source.zip').read_bytes()
        generated=(results/'generated/generation.json').read_bytes()
        if reference is None:
            reference=source;generation=generated
            with zipfile.ZipFile(io.BytesIO(source)) as z:extract(z,out/'source')
        else:assert source==reference and generated==generation
        before=json.loads((results/'recheck.json').read_text())
        p=subprocess.run(['python3',str(out/'source/Audit~/DenseBulk/review.py'),'--root',str(results)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (out/(arch+'-independent-recheck.log')).write_text(p.stdout)
        if p.returncode:raise RuntimeError(p.stdout[-16000:])
        after=json.loads((results/'recheck.json').read_text());assert before==after
        for filename,bucket in (('All_Results.csv',combined),('Comparisons.csv',compared)):
            with (results/filename).open(encoding='utf-8-sig',newline='') as f:
                bucket.extend(dict(architecture=arch,**r) for r in csv.DictReader(f))
        negative.extend(dict(architecture=arch,**r) for r in json.loads((results/'regressions.json').read_text()))
        meta[arch]=dict(artifact=item['id'],archive_sha256=digest,**after)
        print('DENSE_BULK_COLLECT_'+arch+' '+json.dumps({k:v for k,v in meta[arch].items() if k not in ('tests','AA')}),flush=True)
    assert meta['x64']['head']==meta['arm64']['head'] and not meta['x64']['smoke'] and not meta['arm64']['smoke']
    for name,rows in (('All_Architectures.csv',combined),('All_Comparisons.csv',compared)):
        with (out/name).open('w',encoding='utf-8-sig',newline='') as f:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    (out/'Source_Tested.zip').write_bytes(reference)
    (out/'All_Regressions.json').write_text(json.dumps(negative,indent=2))
    (out/'COLLECTION.json').write_text(json.dumps(dict(architectures=meta,cross_architecture_sources_equal=True,aggregate_rows=len(combined),comparison_rows=len(compared),production_promoted=False,nativeUnity='not_run'),indent=2))
if __name__=='__main__':main()
