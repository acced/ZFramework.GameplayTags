#!/usr/bin/env python3
"""Read only this workflow's completed artifacts; verify and preserve both architectures."""
import csv,hashlib,io,json,os,pathlib,shutil,subprocess,sys,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
run=os.environ['GITHUB_RUN_ID'];repo=os.environ['GITHUB_REPOSITORY']
out=ROOT/'adaptive-delivery';out.mkdir(exist_ok=True)
def api(path):return subprocess.check_output(['gh','api',path])
items=json.loads(api(f'/repos/{repo}/actions/runs/{run}/artifacts?per_page=100'))['artifacts']
reviews={};sources=[];tables=[]
for arch in ('x64','arm64'):
    wanted=f'adaptive-{arch}-{run}';found=[x for x in items if x['name']==wanted and not x['expired']]
    if len(found)!=1:raise RuntimeError('Missing unique artifact '+wanted)
    artifact=found[0];data=api(f'/repos/{repo}/actions/artifacts/{artifact["id"]}/zip')
    digest=hashlib.sha256(data).hexdigest();assert artifact['digest']=='sha256:'+digest
    (out/(arch+'-raw.zip')).write_bytes(data);target=ROOT/('adaptive-review-'+arch);target.mkdir(exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for e in z.infolist():
            p=pathlib.PurePosixPath(e.filename)
            if p.is_absolute() or '..' in p.parts:raise RuntimeError('Unsafe artifact')
        z.extractall(target)
    proc=subprocess.run([sys.executable,ROOT/'Audit~/AdaptiveResearch/review.py','--root',target],text=True,capture_output=True)
    (out/(arch+'-recheck.log')).write_text(proc.stdout+proc.stderr);assert proc.returncode==0
    review=json.loads((target/'results/review.json').read_text());reviews[arch]=review
    shutil.copy2(target/'results/REPORT.md',out/(arch+'-REPORT.md'))
    with (target/'results/all.csv').open() as f:tables.extend(list(csv.DictReader(f)))
    sources.append((target/'source.zip').read_bytes())
    if arch=='x64':shutil.copytree(target/'results/generated',out/'generated',dirs_exist_ok=True)
    else:
        for p in (target/'results/generated').rglob('*'):
            if p.is_file():assert p.read_bytes()==(out/'generated'/p.relative_to(target/'results/generated')).read_bytes()
    print('CPU_REVIEW '+json.dumps({'arch':arch,'artifact':artifact['id'],'sha256':digest,'summary':review}),flush=True)
assert sources[0]==sources[1]
(out/'ExperimentSource.zip').write_bytes(sources[0])
with (out/'AllArchitectures.csv').open('w',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(tables[0]));writer.writeheader();writer.writerows(tables)
(out/'CrossReview.json').write_text(json.dumps(reviews,indent=2))
readme='''# Adaptive GameplayTags research evidence

The user removed the Dense-only restriction. This pilot preserves original global tag semantics and compares:
- Original Auto and A/A (the exact same binary under two labels).
- Original Auto + previously verified DenseFusion, with sparse paths unchanged.
- Original Auto + boolean SIMD lookup of small ordered integer arrays.
- Original Auto + direct independent copy construction (same capacity and representation).
- Combined candidate.
- PR8 DirectArray as a micro-bitmap reference.

Production Runtime in the repository was not changed. Generated RuntimeTagSet.cs files are mutually exclusive experimental alternatives, NOT files to compile all together. They use the unchanged runtime context and, for DenseFusion variants, the existing Audit~/Fusion/DenseFusion.cs backend. The complete reproduction script is in ExperimentSource.zip.

Both CPU reports retain complete operation timing, exact allocation, same-input digests and A/A diagnostics. Do not select the fastest row from different candidates or CPU runs and call that a single implementation. No universally-leading or Unity release claim is authorized by a green test workflow.
'''
(out/'README.md').write_text(readme)
# Each raw archive includes original snapshots, generated fixtures, tests, hashes, environment and all samples.
with zipfile.ZipFile(ROOT/'GameplayTags_Adaptive_SourceAndEvidence.zip','w',zipfile.ZIP_DEFLATED) as z:
    for p in out.rglob('*'):
        if p.is_file():z.write(p,p.relative_to(out))
print('DELIVERY_SHA256 '+hashlib.sha256((ROOT/'GameplayTags_Adaptive_SourceAndEvidence.zip').read_bytes()).hexdigest(),flush=True)
