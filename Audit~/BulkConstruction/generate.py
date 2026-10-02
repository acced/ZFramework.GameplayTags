#!/usr/bin/env python3
"""Add isolated bulk-construction APIs to fixed PR11 Array/Dense and Micro classes."""
import pathlib, sys, hashlib, difflib, json, subprocess, importlib.util
ROOT = pathlib.Path(__file__).resolve().parents[2]
PIN = '0587201567aa21edb60055acf5028faf8d659b39'
DEPENDENCIES = ('Runtime/RuntimeTagSet.cs', 'Audit~/MicroBitmapSet.cs',
    'Audit~/Crossover/prepare.py', 'Audit~/AdaptiveResearch/variants.py',
    'Audit~/ArrayBuild/build_candidates.py', 'Audit~/Fusion/DenseFusion.cs')

def fill_method(handles):
    name = 'FillSortedHandles' if handles else 'FillSortedIds'
    signature = 'ReadOnlySpan<RuntimeTag> values' if handles else 'int[] values, int n'
    size = '            int n = values.Length;\n' if handles else ''
    read = lambda at: 'values[' + at + ']' + ('.Id' if handles else '')
    array = ('for (int j = 0; j < n; j++) m_Ids[j] = values[j].Id;'
             if handles else 'Array.Copy(values, m_Ids, n);')
    return '''        private void %s(%s)
        {
%s#if BULK_MICRO
            ulong[] words = dense;
#else
            ulong[] words = m_Words;
#endif
            if (words != null)
            {
                int i = 0;
                while (i < n)
                {
                    int id = %s; i++;
                    int word = id >> 6;
                    ulong mask = 1UL << (id & 63);
                    while (i < n && (%s >> 6) == word)
                    { mask |= 1UL << (%s & 63); i++; }
                    words[word] = mask;
                }
            }
#if BULK_MICRO
            else if (n != 0)
            {
                // A null entries buffer is sufficient only for a single possible record.
                // Reservation is in arbitrary member units, not an optimistic local layout.
                if (entries == null)
                {
                    Entry mask = 0;
                    for (int i = 0; i < n; i++) mask |= Bit(%s);
                    inline = Key(%s) | mask;
                    used = 1;
                }
                else
                {
                    Entry[] output = entries;
                    int i = 0, write = 0;
                    while (i < n)
                    {
                        int id = %s; i++;
                        Entry key = Key(id), mask = Bit(id);
                        while (i < n && Key(%s) == key)
                        { mask |= Bit(%s); i++; }
                        output[write++] = key | mask;
                    }
                    used = write;
                }
            }
            count = n;
#else
            else { %s }
            m_Count = n;
#endif
        }
''' % (name, signature, size, read('i'), read('i'), read('i'), read('i'), read('0'), read('i'), read('i'), read('i'), array)

def generate(out):
    out = pathlib.Path(out); out.mkdir(parents=True, exist_ok=True)
    for rel in DEPENDENCIES:
        expected = subprocess.check_output(['git', 'show', PIN + ':' + rel], cwd=ROOT)
        if (ROOT / rel).read_bytes() != expected:
            raise RuntimeError('Pinned dependency changed: ' + rel)
    spec = importlib.util.spec_from_file_location('bulk_fixed_crossover', ROOT/'Audit~/Crossover/prepare.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    variants, micro = module.generate(out/'controls')
    sources = {'runtime': ('GameplayTags','RuntimeTagSet','TagSetStorage',variants['binary']),
               'micro': ('GameplayTags.Experiments','DirectArraySet','BitmapLayout',micro.read_text())}
    template = (ROOT/'Audit~/BulkConstruction/Builders.cs.in').read_text()
    result = {}; manifest = {}
    for kind, (ns, cls, mode, source) in sources.items():
        old = 'public sealed class ' + cls
        if source.count(old) != 1: raise RuntimeError('Class declaration changed')
        partial = source.replace(old, 'public sealed partial class ' + cls, 1)
        body = template.replace('__NAMESPACE__',ns).replace('__TYPE__',cls).replace('__MODE__',mode)
        body = body.replace('__FILL_METHODS__',fill_method(True)+'\n'+fill_method(False))
        target = out/kind; target.mkdir(exist_ok=True)
        core = target/(cls+'.cs'); added = target/'BulkBuilders.cs'
        core.write_text(partial); added.write_text(body)
        (target/'partial-only.patch').write_text(''.join(difflib.unified_diff(source.splitlines(True),partial.splitlines(True),fromfile='fixed-control',tofile='partial-compilation')))
        manifest[kind] = {'class':cls,'core_sha256':hashlib.sha256(partial.encode()).hexdigest(),
            'builders_sha256':hashlib.sha256(body.encode()).hexdigest(),
            'existing_methods_changed':False, 'per_instance_fields_added':0}
        result[kind] = [core,added]
    (out/'generation.json').write_text(json.dumps({'base':PIN,'variants':manifest},indent=2))
    return result
if __name__ == '__main__': generate(sys.argv[1])
