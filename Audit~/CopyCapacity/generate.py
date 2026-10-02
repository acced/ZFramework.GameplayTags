#!/usr/bin/env python3
"""An algebraic capacity proof, not a measured cardinality cutoff."""
from pathlib import Path
import importlib.util, hashlib, json, difflib
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('commit_parent',ROOT/'Audit~/CopyCommit/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block=(parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block)
FAST='''private void CopyDenseToMicro(DirectArraySet source)
        {
            Clear();
            // Nonzero records <= members AND <= registry's possible record keys.
            // This upper bound certifies absence of buffer growth for the whole copy.
            if (EntryCapacity < Math.Min(source.count, MaxEntries))
            {
                CopyDenseGrowing(source);
                return;
            }
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext()) Write(used++, iterator.Current);
            // No potentially failing member allocation; CopyCore commits known Count.
        }'''
GROW=parent.EAGER.replace('CopyDenseToMicro(', 'CopyDenseGrowing(',1).replace('            Clear();\n','',1)
def variants(direct):
    originals=parent.variants(direct);control=originals['control']
    old=block(control,'private void CopyDenseToMicro(')
    both=FAST+'\n\n        '+GROW
    guarded=control.replace(old,both,1)
    assert guarded.replace(both,old,1)==control
    return dict(control=control,eager=originals['eager'],guarded=guarded)
def generate(out,pinned=None):
    out=Path(out);out.mkdir(parents=True,exist_ok=True)
    original=parent.generate(out/'pinned',pinned)
    direct=(out/'pinned/direct/DirectArraySet.cs').read_text();sources=variants(direct)
    assert sources['control']==original['control'][0].read_text()
    assert sources['eager']==original['eager'][0].read_text()
    files={};manifest={}
    for name,code in dict(direct=direct,**sources).items():
        folder=out/name;folder.mkdir(exist_ok=True)
        a=folder/'DirectArraySet.cs';a.write_text(code)
        b=folder/'BulkBuilders.cs';b.write_bytes(original['control'][1].read_bytes())
        if name!='direct':files[name]=[a,b]
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (a,b)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(sources['control'].splitlines(True),code.splitlines(True),fromfile='fixed-PR24-control',tofile=name)))
    (out/'generation.json').write_text(json.dumps(dict(parent_commit='4da164547cc35a9808f0eb2032425c0e499451d4',sources=manifest,new_instance_fields=0,capacity_bound='min(source.Count, MaxEntries)',fitted_threshold=False),indent=2))
    return files
