#!/usr/bin/env python3
"""Operation-local Dense cursor; no replacement of public enumeration or Records."""
import pathlib, importlib.util, hashlib, json, difflib
ROOT=pathlib.Path(__file__).resolve().parents[2]
PIN='10d076a635eb46a5df5f0f7eb12279cd8c81e51c'
spec=importlib.util.spec_from_file_location('fixed_mixed_generator',ROOT/'Audit~/MixedOperations/generate.py')
old=importlib.util.module_from_spec(spec);spec.loader.exec_module(old)
block=old.block

def split_cursor(text):
    # Admission and alias guards stay in their existing callers. Only mixed-source
    # operations enter helpers; ordinary Micro enumeration keeps its exact code/layout.
    before=block(text,'private void CopyCore(')
    anchor='''            else
            {
                Clear(); var it = new Records(source);'''
    assert before.count(anchor)==1
    after=before.replace(anchor,'''            else if (source.dense != null)
            {
                CopyDenseToMicro(source);
            }
'''+anchor,1)
    text=text.replace(before,after,1)
    before=block(text,'private void AppendCore(')
    anchor='''            var it = new Records(other);'''
    assert before.count(anchor)==1
    after=before.replace(anchor,'''            if (other.dense != null)
            {
                AppendDenseToMicro(other);
                return;
            }
'''+anchor,1)
    text=text.replace(before,after,1)
    before=block(text,'private static void UnionCore(')
    anchor='''            result.Clear(); var left = new Records(a); var right = new Records(b);'''
    assert before.count(anchor)==1
    after=before.replace(anchor,'''            if (result.dense == null && ((a.dense == null) != (b.dense == null)))
            {
                UnionDenseToMicro(a.dense != null ? a : b, a.dense == null ? a : b, result);
                return;
            }
'''+anchor,1)
    text=text.replace(before,after,1)
    helpers='''        // All callers have already established Dense source / Micro destination,
        // common registry, exclusive output and stable inputs for this operation.
        private void CopyDenseToMicro(DirectArraySet source)
        {
            Clear();
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext()) PutRecord(iterator.Current);
        }

        private void AppendDenseToMicro(DirectArraySet source)
        {
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry e = iterator.Current, key = e & ~Mask;
                int at = Find(key);
                if (at >= 0)
                {
                    Entry before = Read(at);
                    Write(at, before | e);
                    count += Pop(e & ~before & Mask);
                }
                else
                {
                    at = ~at;
                    ReserveEntries(used + 1);
                    if (at < used) Array.Copy(entries, at, entries, at + 1, used - at);
                    Write(at, e);
                    used++;
                    count += Pop(e & Mask);
                }
            }
        }

        private static void UnionDenseToMicro(DirectArraySet full, DirectArraySet local, DirectArraySet result)
        {
            result.Clear();
            var iterator = new DenseRecordCursor(full);
            bool pending = iterator.MoveNext();
            int at = 0, total = 0;
            while (pending || at < local.used)
            {
                Entry value;
                if (!pending)
                {
                    value = local.Read(at++);
                }
                else if (at == local.used)
                {
                    value = iterator.Current;
                    pending = iterator.MoveNext();
                }
                else
                {
                    Entry x = iterator.Current, y = local.Read(at);
                    Entry kx = x & ~Mask, ky = y & ~Mask;
                    if (kx < ky) { value = x; pending = iterator.MoveNext(); }
                    else if (ky < kx) { value = y; at++; }
                    else { value = x | y; at++; pending = iterator.MoveNext(); }
                }
                // Same ordered writes, capacity growth and eager counting as control.
                result.PutRecord(value);
                total += Pop(value & Mask);
            }
            result.count = total;
        }

        // A temporary local used only by the three helpers above. In particular it
        // is NOT a field of DirectArraySet, Records or the public Enumerator.
        private struct DenseRecordCursor
        {
            private readonly ulong[] words;
            private int nextWord;
            private ulong pending;
            public Entry Current;
            public DenseRecordCursor(DirectArraySet source)
            {
                words = source.dense;
                nextWord = source.count == 0 ? words.Length : 0;
                pending = 0;
                Current = 0;
            }
            public bool MoveNext()
            {
                while (pending == 0)
                {
                    if (nextWord == words.Length) return false;
                    pending = words[nextWord++];
                }
                int shift = Low(pending) & ~((1 << Shift) - 1);
                Entry bits = (Entry)(pending >> shift) & Mask;
                pending &= ~((ulong)Mask << shift);
                Current = Key(((nextWord - 1) << 6) + shift) | bits;
                return true;
            }
        }

'''
    anchor='        private struct Records'
    assert text.count(anchor)==1
    return text.replace(anchor,helpers+anchor,1)

def generate(out):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    fixed=old.generate(out/'pinned')
    direct=fixed['dense'][0].read_text()
    sources={'direct':direct,'shared':fixed['combined'][0].read_text(),'split':split_cursor(direct)}
    result={};manifest={}
    protected=('public struct Enumerator','private struct Records','public bool HasTagExact(',
               'private void ReserveEntries(','public void RemoveTags(',
               'public DirectArraySet(DirectArraySet source)')
    for name,code in sources.items():
        folder=out/name;folder.mkdir(exist_ok=True)
        core=folder/'DirectArraySet.cs';core.write_text(code)
        builder=folder/'BulkBuilders.cs';builder.write_bytes(fixed['dense'][1].read_bytes())
        result[name]=[core,builder]
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(direct.splitlines(True),code.splitlines(True),fromfile='PR15-direct',tofile=name)))
        assert code[:code.index('public TagRegistry Registry')]==direct[:direct.index('public TagRegistry Registry')]
        if name=='split':
            for signature in protected:assert block(code,signature)==block(direct,signature),signature
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in result[name]}
    (out/'generation.json').write_text(json.dumps(dict(base=PIN,sources=manifest,
        public_iterator_changed=False,new_set_fields=0,
        changes='split uses a Dense-only stack cursor in mixed CopyCore, AppendCore and Micro-output UnionCore'),indent=2))
    return result
