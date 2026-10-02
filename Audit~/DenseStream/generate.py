#!/usr/bin/env python3
"""No numeric crossover: compare monotone insertion with prefix-aware bounded merge."""
import pathlib,importlib.util,hashlib,json,difflib
ROOT=pathlib.Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('bounded_parent',ROOT/'Audit~/DenseBounded/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block,bounded=parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block,parent.bounded

MONOTONE='''private void AppendDenseToMicro(DirectArraySet source)
        {
            int at = 0;
            var forward = new DenseRecordCursor(source);
            while (forward.MoveNext())
            {
                Entry value = forward.Current, key = value & ~Mask;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at < used && (Read(at) & ~Mask) == key)
                {
                    Entry before = Read(at);
                    Write(at, before | value);
                    count += Pop(value & ~before & Mask);
                }
                else
                {
                    ReserveEntries(used + 1);
                    if (at < used) Array.Copy(entries, at, entries, at + 1, used - at);
                    Write(at, value);
                    used++;
                    count += Pop(value & Mask);
                }
                // Source keys are strictly increasing; never search the old prefix again.
                at++;
            }
        }'''

STREAM='''private void AppendDenseToMicro(DirectArraySet source)
        {
            int at = 0, duplicates = 0, originalCount = count;
            var forward = new DenseRecordCursor(source);
            bool pending = forward.MoveNext();
            while (pending)
            {
                Entry value = forward.Current, key = value & ~Mask;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at == used)
                {
                    // All remaining keys follow the destination. No reverse scan needed.
                    do { PutRecord(forward.Current); } while (forward.MoveNext());
                    count = originalCount + source.count - duplicates;
                    return;
                }
                Entry before = Read(at);
                if ((before & ~Mask) != key) break; // First insertion inside the old range.
                Write(at++, before | value);
                duplicates += Pop(before & value & Mask);
                pending = forward.MoveNext();
            }
            if (!pending)
            {
                count = originalCount + source.count - duplicates;
                return;
            }

            // Continue from the first internal gap, without rescanning the prefix.
            int length = used, remainingRecords = 0;
            do
            {
                remainingRecords++;
                Entry key = forward.Current & ~Mask;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at == used || (Read(at) & ~Mask) != key) length++;
            } while (forward.MoveNext());
            while (EntryCapacity < length) ReserveEntries(EntryCapacity + 1);

            int lastId = (int)(forward.Current >> MaskBits) << Shift;
            var reverse = new DenseReverseCursor(source, lastId);
            int read = used - 1, write = length - 1;
            pending = reverse.MoveNext();
            while (read >= 0 && pending)
            {
                Entry a = Read(read), b = reverse.Current;
                Entry ka = a & ~Mask, kb = b & ~Mask;
                if (ka > kb) { Write(write--, a); read--; }
                else if (kb > ka)
                {
                    Write(write--, b);
                    pending = --remainingRecords != 0 && reverse.MoveNext();
                }
                else
                {
                    Write(write--, a | b);
                    duplicates += Pop(a & b & Mask);
                    read--;
                    pending = --remainingRecords != 0 && reverse.MoveNext();
                }
            }
            while (pending)
            {
                Write(write--, reverse.Current);
                pending = --remainingRecords != 0 && reverse.MoveNext();
            }
            // Previously merged prefix is untouched by reverse output.
            used = length;
            count = originalCount + source.count - duplicates;
        }'''

def monotone(code):return code.replace(block(code,'private void AppendDenseToMicro('),MONOTONE,1)
def stream(code):return code.replace(block(code,'private void AppendDenseToMicro('),STREAM,1)

def generate(out,pinned=None):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    originals=parent.generate(out/'pinned',pinned)
    direct=(out/'pinned/direct/DirectArraySet.cs').read_text()
    f=originals['fixed'][0].read_text();b=originals['bounded'][0].read_text()
    sources=dict(direct=direct,fixed=f,bounded=b,monotone=monotone(f),stream=stream(b))
    files={};manifest={}
    for name,code in sources.items():
        folder=out/name;folder.mkdir(exist_ok=True)
        core=folder/'DirectArraySet.cs';core.write_text(code)
        builder=folder/'BulkBuilders.cs';builder.write_bytes(originals['fixed'][1].read_bytes())
        if name!='direct':files[name]=[core,builder]
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (core,builder)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff((f if name=='monotone' else b).splitlines(True),code.splitlines(True),fromfile='fixed' if name=='monotone' else 'bounded',tofile=name)))
        assert code[:code.index('public TagRegistry Registry')]==direct[:direct.index('public TagRegistry Registry')]
        for sig in ('private struct Records','public struct Enumerator','public bool HasTagExact(','public void RemoveTags(','private void ReserveEntries(','public DirectArraySet(DirectArraySet source)'):
            assert block(code,sig)==block(direct,sig)
        if name=='stream':
            for sig in ('private struct DenseReverseCursor','private struct DenseRecordCursor','private static void UnionCore(','private void CopyDenseToMicro('):
                assert block(code,sig)==block(b,sig)
    (out/'generation.json').write_text(json.dumps(dict(parent_commit='cffe3c54ec881352ea727a30371122c378bf4839',sources=manifest,new_instance_fields=0,new_cursor_fields=0,numeric_thresholds=0),indent=2))
    return files
