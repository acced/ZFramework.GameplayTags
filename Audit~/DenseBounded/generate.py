#!/usr/bin/env python3
"""Reuse facts already computed by mixed append's first pass; no density threshold."""
import pathlib,importlib.util,hashlib,json,difflib
ROOT=pathlib.Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('bulk_fixed_parent',ROOT/'Audit~/DenseBulk/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block=parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block

def bounded(code):
    old=block(code,'private void AppendDenseToMicro(')
    new=old.replace('int length = used, at = 0;', 'int length = used, at = 0, sourceRecords = 0;',1)
    new=new.replace('Entry key = forward.Current & ~Mask;', 'sourceRecords++;\n                Entry key = forward.Current & ~Mask;',1)
    new=new.replace('var reverse = new DenseReverseCursor(source);',
        '// Current retains the final emitted record after exhaustion.\n            int lastId = (int)(forward.Current >> MaskBits) << Shift;\n            var reverse = new DenseReverseCursor(source, lastId);',1)
    assert new.count('pending = reverse.MoveNext();')==4
    new=new.replace('pending = reverse.MoveNext();','pending = --sourceRecords != 0 && reverse.MoveNext();')
    new=new.replace('bool pending = --sourceRecords != 0 && reverse.MoveNext();','bool pending = reverse.MoveNext();',1)
    code=code.replace(old,new,1)
    old=block(code,'private struct DenseReverseCursor')
    new=old.replace('DenseReverseCursor(DirectArraySet source)','DenseReverseCursor(DirectArraySet source, int lastId)',1)
    new=new.replace('nextId = source.count == 0 ? 0 : words.Length << 6;',
        '// Nonempty source and last record were established by the first pass.\n                nextId = ((lastId >> 6) + 1) << 6;',1)
    assert new!=old
    return code.replace(old,new,1)

def generate(out,pinned=None):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    originals=parent.generate(out/'pinned',pinned)
    core={n:originals[n][0].read_text() for n in ('direct','fixed','combined')}
    core['bounded']=bounded(core['combined'])
    result={};manifest={}
    for n,text in core.items():
        folder=out/n;folder.mkdir(exist_ok=True)
        a=folder/'DirectArraySet.cs';a.write_text(text)
        b=folder/'BulkBuilders.cs';b.write_bytes(originals['direct'][1].read_bytes())
        if n!='direct':result[n]=[a,b]
        manifest[n]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (a,b)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(core['combined'].splitlines(True),text.splitlines(True),fromfile='PR18-combined',tofile=n)))
        assert text[:text.index('public TagRegistry Registry')]==core['direct'][:core['direct'].index('public TagRegistry Registry')]
        for sig in ('private struct Records','public struct Enumerator','private void ReserveEntries(','public bool HasTagExact(','public void RemoveTags('):
            assert block(text,sig)==block(core['direct'],sig)
    (out/'generation.json').write_text(json.dumps(dict(parent_commit='79d4dc4c16fc5d3c831e2d1e0efcfa0c66847d66',sources=manifest,new_instance_fields=0,new_cursor_fields=0),indent=2))
    return result
