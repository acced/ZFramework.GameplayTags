#!/usr/bin/env python3
"""Fixed 2x2 ablation: empty CopyCore branch and private-helper call boundary."""
from pathlib import Path
import importlib.util,hashlib,json,difflib
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('capacity_parent',ROOT/'Audit~/CopyCapacity/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block=(parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block)
PINS={'control': '0866e5b8868dfac28955539e9257861fca0a88cb5ca349fd2c13744b673f32cb', 'eager': '52bcdff3284e4da01324c40236b3dda38899f91035a60b53f3c3f62bbb4f3ab2', 'guarded': '9e83b5d7523832417b2a47b01fd13d15bbd6a494883560d5e80189db2919379c'}

def isolate(code):
    signature='private void CopyDenseToMicro('
    assert code.count(signature)==1
    return code.replace(signature,'[MethodImpl(MethodImplOptions.NoInlining)]\n        '+signature,1)

def empty_source(code):
    before=block(code,'private void CopyCore(')
    after=before.replace('{','{\n            // Admission and self-alias checks already ran. Preserve target capacity.\n            if (source.count == 0) { Clear(); return; }',1)
    return code.replace(before,after,1)

def variants(direct):
    old=parent.variants(direct)
    for n,h in PINS.items():assert hashlib.sha256(old[n].encode()).hexdigest()==h,n
    g=old['guarded']
    return dict(control=old['control'],eager=old['eager'],guarded=g,
        isolated=isolate(g),empty=empty_source(g),boundary=isolate(empty_source(g)))

def generate(out,pinned=None):
    out=Path(out);out.mkdir(parents=True,exist_ok=True)
    original=parent.generate(out/'pinned',pinned)
    direct=(out/'pinned/direct/DirectArraySet.cs').read_text();sources=variants(direct)
    files={};manifest={}
    for name,code in dict(direct=direct,**sources).items():
        folder=out/name;folder.mkdir(exist_ok=True)
        a=folder/'DirectArraySet.cs';a.write_text(code)
        b=folder/'BulkBuilders.cs';b.write_bytes(original['guarded'][1].read_bytes())
        if name!='direct':files[name]=[a,b]
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (a,b)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(sources['guarded'].splitlines(True),code.splitlines(True),fromfile='fixed-PR25-guarded',tofile=name)))
        assert code[:code.index('public TagRegistry Registry')]==direct[:direct.index('public TagRegistry Registry')]
    return_manifest=dict(parent_commit='01bf2353401adb1877e33a585c9b3074339bde4a',sources=manifest,
        new_instance_fields=0,public_api_changed=False,capacity_or_growth_changed=False,
        factors=['empty-source short circuit in CopyCore','NoInlining CopyDenseToMicro'],production_promoted=False)
    (out/'generation.json').write_text(json.dumps(return_manifest,indent=2))
    return files
