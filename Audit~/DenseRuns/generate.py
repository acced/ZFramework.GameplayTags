#!/usr/bin/env python3
"""One Dense pass, two owned sorted runs. Fixed no-buffer/128B-buffer ablation."""
import pathlib,importlib.util,hashlib,json,difflib
ROOT=pathlib.Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('runs_parent',ROOT/'Audit~/DenseStream/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
BASE=parent.BASE
block=parent.block
NAMES=('monotone','stream','rotate','buffer32')

def run_merge(code,capacity):
    append=(ROOT/'Audit~/DenseRuns/Append.cs.txt').read_text().strip()
    helpers=(ROOT/'Audit~/DenseRuns/Merge.cs.txt').read_text()
    init='Span<Entry> scratch = Span<Entry>.Empty;' if capacity==0 else f'Span<Entry> scratch = stackalloc Entry[{capacity}];'
    helpers=helpers.replace('__SCRATCH_INIT__',init)
    if capacity==0:
        # The no-scratch control has no unused span plumbing or buffered branches.
        start=helpers.index('                if (left <= scratch.Length')
        end=helpers.index('                // The larger side',start)
        helpers=helpers[:start]+helpers[end:]
        start=helpers.index('        private static void MergeSmallRun(')
        helpers=helpers[:start]
        helpers=helpers.replace('            Span<Entry> scratch = Span<Entry>.Empty;\n','')
        helpers=helpers.replace(', Span<Entry> scratch', '').replace(', scratch)', ')')
    code=code.replace(block(code,'private void AppendDenseToMicro('),append,1)
    code=code.replace('        private struct Records',helpers+'        private struct Records',1)
    return code

def generate(out,pinned=None):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    originals=parent.generate(out/'pinned',pinned)
    f=originals['monotone'][0].read_text()
    codes=dict(monotone=f,stream=originals['stream'][0].read_text(),rotate=run_merge(f,0),buffer32=run_merge(f,32))
    # Keep exact fixed reference at the root for provenance/reviewer compatibility.
    sources={'direct':(out/'pinned/direct/DirectArraySet.cs').read_text(),**codes}
    manifest={};result={}
    for n,code in sources.items():
        folder=out/n;folder.mkdir(exist_ok=True)
        core=folder/'DirectArraySet.cs';core.write_text(code)
        builder=folder/'BulkBuilders.cs';builder.write_bytes(originals['monotone'][1].read_bytes())
        if n!='direct':result[n]=[core,builder]
        for sig in ('private struct Records','public struct Enumerator','public bool HasTagExact(','public void RemoveTags(','private void ReserveEntries(','public DirectArraySet(DirectArraySet source)'):
            assert block(code,sig)==block(f,sig),sig
        assert code[:code.index('public TagRegistry Registry')]==f[:f.index('public TagRegistry Registry')]
        manifest[n]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (core,builder)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(f.splitlines(True),code.splitlines(True),fromfile='PR20-monotone',tofile=n)))
    (out/'generation.json').write_text(json.dumps(dict(parent='0d097a1546c51ea3676ef35817428f346da46293',sources=manifest,new_instance_fields=0,member_scratch_bytes={'rotate':0,'buffer32':128},recursive_scratch_reused=True),indent=2))
    return result
