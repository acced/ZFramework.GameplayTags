#!/usr/bin/env python3
"""Isolate Dense-to-Micro copy failure consistency; no new append algorithm."""
from pathlib import Path
import importlib.util, hashlib, json, difflib
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('copy_parent',ROOT/'Audit~/DenseDeltaSafety/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block=(parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block)
PARENT='c4794476af257f9230d6908fa48bf5b0e88ac5ea'
EAGER='''private void CopyDenseToMicro(DirectArraySet source)
        {
            Clear();
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry entry = iterator.Current;
                PutRecord(entry);
                // The successful write is now part of the observable partial copy.
                count += Pop(entry & Mask);
            }
        }'''
CHECKPOINT='''private void CopyDenseToMicro(DirectArraySet source)
        {
            Clear();
            int committed = 0;
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                // PutRecord can allocate only when the current record buffer is full.
                // Publish the exact prefix count BEFORE that potentially failing growth.
                if (used == EntryCapacity)
                    for (; committed < used; committed++) count += Pop(Read(committed) & Mask);
                PutRecord(iterator.Current);
            }
            // CopyCore commits source.count on success. With sufficient capacity,
            // no partial-prefix counting was needed. No concurrent observers/callbacks.
        }'''
def variants(direct):
    control=parent.variants(direct)['batch32']
    old=block(control,'private void CopyDenseToMicro(')
    return dict(control=control,eager=control.replace(old,EAGER,1),checkpoint=control.replace(old,CHECKPOINT,1))
def generate(out,pinned=None):
    out=Path(out);out.mkdir(parents=True,exist_ok=True)
    originals=parent.generate(out/'pinned',pinned)
    direct=(out/'pinned/direct/DirectArraySet.cs').read_text()
    sources=variants(direct)
    assert sources['control']==originals['batch32'][0].read_text()
    files={};manifest={}
    for name,code in dict(direct=direct,**sources).items():
        folder=out/name;folder.mkdir(exist_ok=True)
        core=folder/'DirectArraySet.cs';core.write_text(code)
        builder=folder/'BulkBuilders.cs';builder.write_bytes(originals['batch32'][1].read_bytes())
        if name!='direct':files[name]=[core,builder]
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (core,builder)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(sources['control'].splitlines(True),code.splitlines(True),fromfile='PR24-batch32',tofile=name)))
        if name!='direct':
            old=block(code,'private void CopyDenseToMicro(')
            assert code.replace(old,block(sources['control'],'private void CopyDenseToMicro('),1)==sources['control']
    (out/'generation.json').write_text(json.dumps(dict(parent=PARENT,sources=manifest,only_copy_helper_changed=True,new_instance_fields=0,production_promoted=False),indent=2))
    return files
