#!/usr/bin/env python3
"""Pinned five-way ablation: fixed slices and in-place mixed append, separately."""
import pathlib, importlib.util, hashlib, json, difflib
ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = 'b23ce46f8b74033f18456734f4615b56bae90ede'
spec = importlib.util.spec_from_file_location('dense_local_fixed', ROOT/'Audit~/DenseLocal/generate.py')
prior = importlib.util.module_from_spec(spec); spec.loader.exec_module(prior)
block = prior.block
EXPECTED = {'direct': 'd6ce0b476ce9b7b0f50081423ce6c225e8ffd618abba6f5f6782c4763e44a7a3',
            'split': 'e708bfc9bf406999c1bab41d05143a164574f57413019525c9e943372d4b475d',
            'builder': '22644785ab3d126f340c711d2bf45576461e090b31e4bcfaea0a4f6f003f8567'}
def sha(data): return hashlib.sha256(data).hexdigest()

FIXED = '''private struct DenseRecordCursor
        {
            private readonly ulong[] words;
            private ulong pending;
            private int nextId;
            public Entry Current;
            public DenseRecordCursor(DirectArraySet source)
            {
                words = source.dense;
                nextId = source.count == 0 ? words.Length << 6 : 0;
                pending = 0;
                Current = 0;
            }
            public bool MoveNext()
            {
                if (pending == 0)
                {
                    int word = (nextId + 63) >> 6;
                    do
                    {
                        if (word == words.Length) return false;
                        pending = words[word++];
                    } while (pending == 0);
                    nextId = (word - 1) << 6;
                }
                // At most four fixed 16-bit slices per nonzero word; no Low().
                // Shift only by MaskBits, never by 64 (C# masks shift counts).
                while (true)
                {
                    Entry bits = (Entry)pending & Mask;
                    int id = nextId;
                    pending >>= MaskBits;
                    nextId += 1 << Shift;
                    if (bits != 0) { Current = Key(id) | bits; return true; }
                }
            }
        }'''

BULK = '''private void AppendDenseToMicro(DirectArraySet source)
        {
            // First pass counts the exact union records without changing this set.
            int length = used, at = 0;
            var forward = new DenseRecordCursor(source);
            while (forward.MoveNext())
            {
                Entry key = forward.Current & ~Mask;
                while (at < used && (Read(at) & ~Mask) < key) at++;
                if (at == used || (Read(at) & ~Mask) != key) length++;
            }
            // Preserve the old incremental growth sizes and total allocated bytes.
            // A single ReserveEntries(length) would change the allocation contract.
            while (EntryCapacity < length) ReserveEntries(EntryCapacity + 1);

            int read = used - 1, write = length - 1, duplicates = 0;
            var reverse = new DenseReverseCursor(source);
            bool pending = reverse.MoveNext();
            while (read >= 0 && pending)
            {
                Entry a = Read(read), b = reverse.Current;
                Entry ka = a & ~Mask, kb = b & ~Mask;
                // write >= read; unread destination records cannot be overwritten.
                if (ka > kb) { Write(write--, a); read--; }
                else if (kb > ka) { Write(write--, b); pending = reverse.MoveNext(); }
                else
                {
                    Write(write--, a | b);
                    duplicates += Pop(a & b & Mask);
                    read--;
                    pending = reverse.MoveNext();
                }
            }
            while (pending)
            {
                Write(write--, reverse.Current);
                pending = reverse.MoveNext();
            }
            // Any remaining left prefix is already in its final location.
            used = length;
            count += source.count - duplicates;
        }'''

REVERSE = '''        // Operation-local reverse source stream for safe in-place merging.
        private struct DenseReverseCursor
        {
            private readonly ulong[] words;
            private ulong pending;
            private int nextId;
            public Entry Current;
            public DenseReverseCursor(DirectArraySet source)
            {
                words = source.dense;
                nextId = source.count == 0 ? 0 : words.Length << 6;
                pending = 0;
                Current = 0;
            }
            public bool MoveNext()
            {
                if (pending == 0)
                {
                    int word = (nextId >> 6) - 1;
                    do
                    {
                        if (word < 0) return false;
                        pending = words[word--];
                    } while (pending == 0);
                    nextId = (word + 2) << 6;
                }
                while (true)
                {
                    Entry bits = (Entry)(pending >> (64 - MaskBits)) & Mask;
                    nextId -= 1 << Shift;
                    pending <<= MaskBits;
                    if (bits != 0) { Current = Key(nextId) | bits; return true; }
                }
            }
        }

'''

def fixed(code):
    return code.replace(block(code, 'private struct DenseRecordCursor'), FIXED, 1)

def bulk(code):
    code = code.replace(block(code, 'private void AppendDenseToMicro('), BULK, 1)
    return code.replace('        private struct Records', REVERSE+'        private struct Records', 1)

def generate(out, pinned=None):
    out = pathlib.Path(out); out.mkdir(parents=True, exist_ok=True)
    if pinned is None:
        original = prior.generate(out/'pinned')
    else:
        pinned = pathlib.Path(pinned)
        original = {n: [pinned/n/'DirectArraySet.cs', pinned/n/'BulkBuilders.cs'] for n in ('direct','split')}
    direct = original['direct'][0].read_text(); split = original['split'][0].read_text()
    builder = original['direct'][1].read_bytes()
    assert sha(direct.encode()) == EXPECTED['direct']
    assert sha(split.encode()) == EXPECTED['split']
    assert sha(builder) == EXPECTED['builder']
    assert split == prior.split_cursor(direct)
    sources = {'direct': direct, 'split': split, 'fixed': fixed(split),
               'bulk': bulk(split), 'combined': bulk(fixed(split))}
    protected = ('private struct Records', 'public struct Enumerator', 'public bool HasTagExact(',
                 'private void ReserveEntries(', 'public void RemoveTags(', 'public DirectArraySet(DirectArraySet source)')
    files = {}; manifest = {}
    for name, code in sources.items():
        assert code[:code.index('public TagRegistry Registry')] == direct[:direct.index('public TagRegistry Registry')]
        for signature in protected: assert block(code,signature) == block(direct,signature), signature
        folder = out/name; folder.mkdir(exist_ok=True)
        core = folder/'DirectArraySet.cs'; core.write_text(code)
        b = folder/'BulkBuilders.cs'; b.write_bytes(builder)
        files[name] = [core,b]; manifest[name] = {p.name:sha(p.read_bytes()) for p in files[name]}
        (folder/'changes.patch').write_text(''.join(difflib.unified_diff(split.splitlines(True),code.splitlines(True),fromfile='PR17-split',tofile=name)))
    (out/'generation.json').write_text(json.dumps({'base':BASE,'sources':manifest,'new_set_fields':0,
        'public_iterator_changed':False,'allocation_growth_policy_changed':False},indent=2))
    return files
