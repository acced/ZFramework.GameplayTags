#!/usr/bin/env python3
"""Refine only the cached small helper: store missing records, shift old tail once."""
from pathlib import Path
import importlib.util,hashlib,json,difflib
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('cache_parent',ROOT/'Audit~/DenseCache/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
EXPECTED,BASE,prior,fixed,bulk,block,bounded,monotone,stream,wrapped=(parent.EXPECTED,parent.BASE,parent.prior,parent.fixed,parent.bulk,parent.block,parent.bounded,parent.monotone,parent.stream,parent.wrapped)
DELTA='''private void AppendDenseSmall(DirectArraySet source)
        {
            // The same Count<=32 admission and fixed stack budget as cache32.
            Span<Entry> missing = stackalloc Entry[AppendCacheRecords];
            int n = 0, at = 0, tail = 0, duplicates = 0;
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry value = iterator.Current, key = value & ~Mask;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at < used && (Read(at) & ~Mask) == key)
                {
                    Entry before = Read(at);
                    Write(at++, before | value);
                    duplicates += Pop(before & value & Mask);
                }
                else
                {
                    missing[n++] = value;
                    // First old key after the last missing key, before any shifts.
                    tail = at;
                }
            }
            if (n != 0)
            {
                int length = used + n;
                while (EntryCapacity < length) ReserveEntries(EntryCapacity + 1);
                // All new keys precede this tail. Move it once; don't self-write it.
                // n>0 and tail<used imply array storage after required reservation.
                if (tail < used) Array.Copy(entries, tail, entries, tail + n, used - tail);
                int read = tail - 1, write = tail + n - 1, pending = n - 1;
                while (read >= 0 && pending >= 0)
                {
                    Entry a = Read(read), b = missing[pending];
                    // Missing keys cannot equal any old key; matches were handled above.
                    if ((a & ~Mask) > (b & ~Mask)) { Write(write--, a); read--; }
                    else { Write(write--, b); pending--; }
                }
                while (pending >= 0) Write(write--, missing[pending--]);
                used = length;
            }
            count += source.count - duplicates;
        }'''
def delta(code):
    before=block(code,'private void AppendDenseSmall(')
    result=code.replace(before,DELTA,1)
    assert '[MethodImpl(MethodImplOptions.NoInlining)]\n        '+DELTA in result
    return result

def generate(out,pinned=None):
    out=Path(out);out.mkdir(parents=True,exist_ok=True)
    originals=parent.generate(out/'pinned',pinned)
    direct=(out/'pinned/direct/DirectArraySet.cs').read_text()
    control=originals['cache32'][0].read_text()
    assert hashlib.sha256(control.encode()).hexdigest()=='e09e2e7494a702899310fb799a5c5e0ba978482ce9b431bee775e09b4453975c'
    sources=dict(direct=direct,monotone=originals['monotone'][0].read_text(),cache32=control,delta32=delta(control))
    files={};manifest={}
    for name,code in sources.items():
        folder=out/name;folder.mkdir(exist_ok=True)
        a=folder/'DirectArraySet.cs';a.write_text(code)
        b=folder/'BulkBuilders.cs';b.write_bytes(originals['cache32'][1].read_bytes())
        if name!='direct':files[name]=[a,b]
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (a,b)}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(control.splitlines(True),code.splitlines(True),fromfile='cache32',tofile=name)))
        assert code[:code.index('public TagRegistry Registry')]==direct[:direct.index('public TagRegistry Registry')]
    (out/'generation.json').write_text(json.dumps(dict(source_cache_commit='3854fda5c923e44cadb081e774d708030cf1e2a3',sources=manifest,new_instance_fields=0,stack_payload_bytes=128,only_delta_small_helper_changed=True),indent=2))
    return files
