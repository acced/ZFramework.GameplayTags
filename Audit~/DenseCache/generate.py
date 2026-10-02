#!/usr/bin/env python3
"""One bounded stack-buffer experiment; preserve three fixed PR20 controls."""
import pathlib, importlib.util, hashlib, json, difflib
ROOT=pathlib.Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('stream_parent',ROOT/'Audit~/DenseStream/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream=(parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block,parent.bounded,parent.monotone,parent.stream)
PINS={'fixed':'e88aecd5cc3795b44a1ac82da684396a2b4e3a746c8c998392a18fd5596d23c0','monotone':'9b35405aaeae45ad4d2807fd73ee0771e44b0412ae0b9902fb2d59ad941bd8c0','stream':'dbb866191177c49a879f7580854e720c99fd62d8ce98cde2b7fe903cfe083ae0'}

WRAPPER='''private const int AppendCacheRecords = 32;

        private void AppendDenseToMicro(DirectArraySet source)
        {
            if (source.count <= AppendCacheRecords) AppendDenseSmall(source);
            else AppendDenseUncached(source);
        }
'''

CACHED='''[MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendDenseSmall(DirectArraySet source)
        {
            // Admitted Dense source, Micro destination, common registry, no alias.
            // Every nonzero record contains a member: records <= Count <= 32.
            // NoInlining keeps this bounded stack lifetime outside caller loops.
            Span<Entry> records = stackalloc Entry[AppendCacheRecords];
            int n = 0, at = 0, length = used;
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry value = iterator.Current, key = value & ~Mask;
                records[n++] = value;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at == used || (Read(at) & ~Mask) != key) length++;
            }
            // Keep scalar insertion's capacity ladder and total allocation bytes.
            while (EntryCapacity < length) ReserveEntries(EntryCapacity + 1);
            int read = used - 1, pending = n - 1, write = length - 1, duplicates = 0;
            while (read >= 0 && pending >= 0)
            {
                Entry a = Read(read), b = records[pending];
                Entry ka = a & ~Mask, kb = b & ~Mask;
                if (ka > kb) { Write(write--, a); read--; }
                else if (kb > ka) { Write(write--, b); pending--; }
                else
                {
                    Write(write--, a | b);
                    duplicates += Pop(a & b & Mask);
                    read--; pending--;
                }
            }
            while (pending >= 0) Write(write--, records[pending--]);
            // Only [0,n) was read; every element was written by the source scan.
            // The remaining left prefix is already at its final positions.
            used = length;
            count += source.count - duplicates;
        }
'''

def wrapped(code, cache):
    old=block(code,'private void AppendDenseToMicro(')
    large=old.replace('AppendDenseToMicro(', 'AppendDenseUncached(',1)
    small=CACHED if cache else '[MethodImpl(MethodImplOptions.NoInlining)]\n        '+old.replace('AppendDenseToMicro(','AppendDenseSmall(',1)
    result=code.replace(old,WRAPPER+'\n        '+small+'\n        '+large,1)
    assert block(result,'private void AppendDenseUncached(').replace('AppendDenseUncached(','AppendDenseToMicro(',1)==old
    return result

def generate(out,pinned=None):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    originals=parent.generate(out/'pinned',pinned)
    direct=(out/'pinned/direct/DirectArraySet.cs').read_text()
    sources={n:originals[n][0].read_text() for n in ('fixed','monotone','stream')}
    for n,h in PINS.items():assert hashlib.sha256(sources[n].encode()).hexdigest()==h,n
    sources['dispatch']=wrapped(sources['stream'],False)
    sources['cache32']=wrapped(sources['stream'],True)
    files={};manifest={}
    for n,code in dict(direct=direct,**sources).items():
        folder=out/n;folder.mkdir(exist_ok=True)
        a=folder/'DirectArraySet.cs';a.write_text(code)
        b=folder/'BulkBuilders.cs';b.write_bytes(originals['fixed'][1].read_bytes())
        if n!='direct':files[n]=[a,b]
        manifest[n]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (a,b)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(sources['stream'].splitlines(True),code.splitlines(True),fromfile='PR20-stream',tofile=n)))
        assert code[:code.index('public TagRegistry Registry')]==direct[:direct.index('public TagRegistry Registry')]
        for signature in ('private struct Records','public struct Enumerator','public bool HasTagExact(','public void RemoveTags(','private void ReserveEntries(','public DirectArraySet(DirectArraySet source)'):
            assert block(code,signature)==block(direct,signature),signature
        for signature in ('private static void UnionCore(','private struct DenseRecordCursor','private void CopyDenseToMicro('):
            assert block(code,signature)==block(sources['stream'],signature),signature
    (out/'generation.json').write_text(json.dumps(dict(parent_commit='0d097a1546c51ea3676ef35817428f346da46293',sources=manifest,new_instance_fields=0,stack_payload_bytes=128,cache_record_limit=32,performance_crossover_validated=False),indent=2))
    return files
