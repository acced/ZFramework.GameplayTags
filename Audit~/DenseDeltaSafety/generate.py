#!/usr/bin/env python3
"""Keep PR22 controls; isolate count repair from count-independent bounded batching."""
from pathlib import Path
import importlib.util,hashlib,json,difflib
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('delta_parent',ROOT/'Audit~/DenseCacheDelta/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream,wrapped,delta=(parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block,parent.bounded,parent.monotone,parent.stream,parent.wrapped,parent.delta)


def safe(code):
    old=block(code,'private void AppendDenseSmall(')
    new=old.replace('int n = 0, at = 0, tail = 0, duplicates = 0;',
                    'int n = 0, at = 0, tail = 0, missingMembers = 0;')
    new=new.replace('duplicates += Pop(before & value & Mask);',
                    'count += Pop(value & ~before & Mask);')
    new=new.replace('missing[n++] = value;',
                    'missing[n++] = value;\n                    missingMembers += Pop(value & Mask);')
    new=new.replace('count += source.count - duplicates;', 'count += missingMembers;')
    assert 'duplicates' not in new and new!=old
    # Only published matching records update Count before potentially throwing growth.
    return code.replace(old,new,1)

BATCH='''private const int AppendBatchRecords = 32;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendDenseToMicro(DirectArraySet source)
        {
            // One bounded stack allocation per call, reused for ALL source sizes.
            // No source.Count crossover and no second source scan.
            Span<Entry> missing = stackalloc Entry[AppendBatchRecords];
            int n = 0, at = 0, tail = 0, missingMembers = 0;
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry value = iterator.Current, key = value & ~Mask;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at < used && (Read(at) & ~Mask) == key)
                {
                    Entry before = Read(at);
                    Write(at++, before | value);
                    count += Pop(value & ~before & Mask);
                }
                else
                {
                    if (n == AppendBatchRecords)
                    {
                        MergeMissingBatch(missing, n, tail, missingMembers);
                        // Every buffered key is smaller than this source key.
                        // All n inserted records precede this old insertion index.
                        at += n;
                        n = missingMembers = 0;
                    }
                    missing[n++] = value;
                    missingMembers += Pop(value & Mask);
                    tail = at;
                }
            }
            if (n != 0) MergeMissingBatch(missing, n, tail, missingMembers);
        }

        private void MergeMissingBatch(Span<Entry> missing, int n, int tail, int members)
        {
            int length = used + n;
            // This is the only throwing allocation site before moving old records.
            // Preserve scalar insertion's growth ladder/total bytes/final capacity.
            while (EntryCapacity < length) ReserveEntries(EntryCapacity + 1);
            if (tail < used) Array.Copy(entries, tail, entries, tail + n, used - tail);
            int read = tail - 1, write = tail + n - 1, pending = n - 1;
            while (read >= 0 && pending >= 0)
            {
                Entry a = Read(read), b = missing[pending];
                // Keys are missing from the target; equality is impossible.
                if ((a & ~Mask) > (b & ~Mask)) { Write(write--, a); read--; }
                else { Write(write--, b); pending--; }
            }
            while (pending >= 0) Write(write--, missing[pending--]);
            used = length;
            count += members;
        }'''

def batch(code):
    return code.replace(block(code,'private void AppendDenseToMicro('),BATCH,1)

def variants(direct):
    f=fixed(prior.split_cursor(direct));s=stream(bounded(bulk(f)))
    m=monotone(f);d=delta(wrapped(s,True))
    assert hashlib.sha256(m.encode()).hexdigest()=='9b35405aaeae45ad4d2807fd73ee0771e44b0412ae0b9902fb2d59ad941bd8c0'
    assert hashlib.sha256(s.encode()).hexdigest()=='dbb866191177c49a879f7580854e720c99fd62d8ce98cde2b7fe903cfe083ae0'
    return dict(monotone=m,stream=s,delta32=d,safe32=safe(d),batch32=batch(m))

def generate(out,pinned=None):
    out=Path(out);out.mkdir(parents=True,exist_ok=True)
    original=parent.generate(out/'pinned',pinned)
    direct=(out/'pinned/direct/DirectArraySet.cs').read_text()
    sources=variants(direct)
    assert sources['delta32']==original['delta32'][0].read_text()
    files={};manifest={}
    for name,code in dict(direct=direct,**sources).items():
        folder=out/name;folder.mkdir(exist_ok=True)
        a=folder/'DirectArraySet.cs';a.write_text(code)
        b=folder/'BulkBuilders.cs';b.write_bytes(original['delta32'][1].read_bytes())
        if name!='direct':files[name]=[a,b]
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (a,b)}
        baseline=sources['monotone'] if name=='batch32' else sources['delta32']
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(baseline.splitlines(True),code.splitlines(True),fromfile='fixed-control',tofile=name)))
        assert code[:code.index('public TagRegistry Registry')]==direct[:direct.index('public TagRegistry Registry')]
        for sig in ('private struct Records','public struct Enumerator','public bool HasTagExact(','public void RemoveTags(','private void ReserveEntries(','public DirectArraySet(DirectArraySet source)'):
            assert block(code,sig)==block(direct,sig)
    assert block(sources['safe32'],'private void AppendDenseUncached(')==block(sources['delta32'],'private void AppendDenseUncached(')
    (out/'generation.json').write_text(json.dumps(dict(parent_commit='7ca87b196a1eb88b282c7160781b3ff98fa73d06',sources=manifest,new_instance_fields=0,stack_payload_bytes=128,batch_source_count_threshold=False,production_promoted=False),indent=2))
    return files
