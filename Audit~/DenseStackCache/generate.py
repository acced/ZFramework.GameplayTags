#!/usr/bin/env python3
"""A single predeclared bounded stack-buffer candidate; no fitted selector."""
import pathlib, importlib.util, hashlib, json, difflib
ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('stack_parent', ROOT/'Audit~/DenseStream/generate.py')
parent = importlib.util.module_from_spec(spec); spec.loader.exec_module(parent)
EXPECTED, BASE, prior, fixed, bulk, block, bounded = parent.EXPECTED, parent.BASE, parent.prior, parent.fixed, parent.bulk, parent.block, parent.bounded
monotone, stream = parent.monotone, parent.stream
PIN = '0d097a1546c51ea3676ef35817428f346da46293'
HASHES = {'fixed':'e88aecd5cc3795b44a1ac82da684396a2b4e3a746c8c998392a18fd5596d23c0',
          'monotone':'9b35405aaeae45ad4d2807fd73ee0771e44b0412ae0b9902fb2d59ad941bd8c0',
          'stream':'dbb866191177c49a879f7580854e720c99fd62d8ce98cde2b7fe903cfe083ae0'}
CACHED = '''private void AppendDenseToMicro(DirectArraySet source)
        {
            // Each nonzero record owns at least one member. This bounds storage,
            // not a claim that 32 is a measured CPU crossover.
            if (source.count > 32) { AppendDenseUncached(source); return; }
            AppendDenseCached(source);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendDenseCached(DirectArraySet source)
        {
            // Admission, registry identity, nonempty source and Micro destination
            // were established by the callers. Only initialized slots are read.
            // A separate frame bounds lifetime even inside repeated public calls.
            Span<Entry> records = stackalloc Entry[32];
            int sourceRecords = 0, at = 0, length = used;
            var iterator = new DenseRecordCursor(source);
            while (iterator.MoveNext())
            {
                Entry value = iterator.Current, key = value & ~Mask;
                records[sourceRecords++] = value;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at == used || (Read(at) & ~Mask) != key) length++;
            }
            // Same exact capacity staircase and managed bytes as scalar insertion.
            while (EntryCapacity < length) ReserveEntries(EntryCapacity + 1);
            int read = used - 1, pending = sourceRecords - 1;
            int write = length - 1, duplicates = 0;
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
            used = length;
            count += source.count - duplicates;
        }

        '''

def cached(code):
    old = block(code, 'private void AppendDenseToMicro(')
    replacement = CACHED + old.replace('AppendDenseToMicro(', 'AppendDenseUncached(', 1)
    return code.replace(old, replacement, 1)

def generate(out, pinned=None):
    out = pathlib.Path(out); out.mkdir(parents=True, exist_ok=True)
    originals = parent.generate(out/'pinned', pinned)
    direct = (out/'pinned/direct/DirectArraySet.cs').read_text()
    sources = {n: originals[n][0].read_text() for n in HASHES}
    for name, text in sources.items(): assert hashlib.sha256(text.encode()).hexdigest() == HASHES[name], name
    sources['cache32'] = cached(sources['stream'])
    # Keep the unmodified base file for the independently reconstructing reviewer.
    (out/'direct').mkdir(exist_ok=True)
    (out/'direct/DirectArraySet.cs').write_text(direct)
    files = {}; manifest = {}
    for name, code in sources.items():
        folder = out/name; folder.mkdir(exist_ok=True)
        core = folder/'DirectArraySet.cs'; core.write_text(code)
        builder = folder/'BulkBuilders.cs'; builder.write_bytes(originals['fixed'][1].read_bytes())
        files[name] = [core, builder]
        manifest[name] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in files[name]}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(sources['stream'].splitlines(True), code.splitlines(True), fromfile='PR20-stream', tofile=name)))
        assert code[:code.index('public TagRegistry Registry')] == direct[:direct.index('public TagRegistry Registry')]
        for sig in ('private struct Records', 'public struct Enumerator', 'public bool HasTagExact(', 'public void RemoveTags(', 'private void ReserveEntries(', 'public DirectArraySet(DirectArraySet source)'):
            assert block(code,sig) == block(direct,sig)
    expected_uncached = block(sources['stream'], 'private void AppendDenseToMicro(').replace('AppendDenseToMicro(', 'AppendDenseUncached(', 1)
    assert block(sources['cache32'], 'private void AppendDenseUncached(') == expected_uncached
    for sig in ('private struct DenseRecordCursor','private struct DenseReverseCursor','private static void UnionCore(','private void CopyDenseToMicro('):
        assert block(sources['cache32'],sig) == block(sources['stream'],sig)
    (out/'generation.json').write_text(json.dumps(dict(parent_commit=PIN,sources=manifest,new_instance_fields=0,public_iterator_changed=False,stack_record_limit=32,micro16_stack_payload_bytes=128,threshold_fitted=False),indent=2))
    return files
