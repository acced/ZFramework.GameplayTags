#!/usr/bin/env python3
"""Independent sample/tree reconstruction plus singleton-specific incremental gates."""
import pathlib,sys,subprocess,json,statistics,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[2]
PIN='2866adfc6bfc21a11177c6b5873fbc54afebfe13'
text=subprocess.check_output(['git','show',PIN+':Audit~/ArrayBuild/recheck.py'],cwd=ROOT).decode()
text=text.replace("'arrays'","'remove'").replace("'growth'","'single'").replace("'kernel'","'direct'")
text=text.replace('Array builder experiment','Singleton record experiment')
root=pathlib.Path(sys.argv[sys.argv.index('--root')+1]);out=root/'results'
(out/'resolved-recheck.py').write_text(text)
exec(compile(text,str(out/'resolved-recheck.py'),'exec'),{'__name__':'__main__'})
rows=json.loads((out/'comparison.json').read_text())
keys=('stage','variant','contract','operation','members','definitions','distribution')
idx={tuple(row[k] for k in keys):row for row in rows}
increments={};fields={}
for row in rows:
    if row['variant'] not in ('remove','single'):continue
    key=tuple(row[k] for k in keys);ref=idx[(key[0],'direct',*key[2:])]
    a,b=row['value'],ref['value']
    assert a['bytes']==b['bytes'],('Allocation changed',key,a['bytes'],b['bytes'])
    eligible=a.get('input_single_records');assert eligible==b.get('input_single_records')
    group='/'.join((row['stage'],row['variant'],row['contract'],row['operation'],'singleA' if eligible else 'multiA'))
    g=increments.setdefault(group,{'cases':0,'lower_median':0,'faster5_every_round':0,'slower5_every_round':0})
    g['cases']+=1;g['lower_median']+=int(a['ns']<b['ns'])
    g['faster5_every_round']+=int(all(x<=.95*y for x,y in zip(a['round_medians'],b['round_medians'])))
    g['slower5_every_round']+=int(all(x>1.05*y for x,y in zip(a['round_medians'],b['round_medians'])))
(out/'singleton-increments.json').write_text(json.dumps(increments,indent=2))
tests={p.stem:json.loads(p.read_text()) for p in out.glob('*-tests.json')}
for name,value in tests.items():assert value.get('failures',0)==0,name
meta={'head':json.loads((out/'sources.json').read_text())['head'],'architecture':json.loads((out/'host.json').read_text())['machine'],
      'tests':tests,'allocation_equal_all_rows':True,'incremental_gates':increments,'production_promoted':False,'unity_run':False}
(out/'SINGLETON_REVIEW.json').write_text(json.dumps(meta,indent=2))
print('SINGLETON_TESTS '+json.dumps(tests),flush=True)
print('SINGLETON_INCREMENTS '+json.dumps({k:v for k,v in increments.items() if k.startswith('small/') and any(x in k for x in ('/union/','/union_into/','/copy_append','/copy_remove'))}),flush=True)
# Make the deliverable self-contained; source.zip is exact tested checkout, classes are literal generated sources.
import zipfile
with zipfile.ZipFile(root/'Singleton_Source_And_Evidence.zip','w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for folder in (root/'results',root/'references'):
        for path in sorted(folder.rglob('*')):
            if path.is_file():z.write(path,path.relative_to(root))
    for name in ('source.zip','SOURCE_COMMIT.txt','SOURCE_TREE.txt'):z.write(root/name,name)
print('SINGLETON_BUNDLE_SHA256 '+hashlib.sha256((root/'Singleton_Source_And_Evidence.zip').read_bytes()).hexdigest(),flush=True)
