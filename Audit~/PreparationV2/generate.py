#!/usr/bin/env python3
"""Preserve PR12, generate v1/v2 side by side. No production edits."""
import pathlib, sys, subprocess, importlib.util, hashlib, json, difflib
ROOT=pathlib.Path(__file__).resolve().parents[2]
PIN='6c7e18dadf6b384046249ba951227109e8d36fde'

def fresh_fill():
    admit='''RuntimeTag tag = values[i]; RequireHandle(Registry, tag);
                    int id = tag.Id;
                    if (id <= previous) throw new ArgumentException("Input must be strictly increasing and unique.", "tags");
                    previous = id;'''
    return '''        // Only the fresh factory calls this, with length > 1 and reserved capacity.
        // Invalid partial writes remain exclusively in an unpublished new object.
        private void ValidateAndFillSorted(ReadOnlySpan<RuntimeTag> values)
        {
            int n = values.Length, previous = -1;
#if BULK_MICRO
            ulong[] words = dense;
#else
            ulong[] words = m_Words;
#endif
            if (words != null)
            {
                int at = -1; ulong mask = 0;
                for (int i = 0; i < n; i++)
                {
                    ADMISSION
                    int word = id >> 6;
                    if (word != at)
                    { if (at >= 0) words[at] = mask; at = word; mask = 0; }
                    mask |= 1UL << (id & 63);
                }
                words[at] = mask;
            }
#if BULK_MICRO
            else if (entries == null)
            {
                // n>1, capacity>=n and no entries implies the whole registry fits one record.
                Entry mask = 0;
                for (int i = 0; i < n; i++)
                { ADMISSION
                    mask |= Bit(id);
                }
                inline = Key(previous) | mask; used = 1;
            }
            else
            {
                Entry[] output = entries; Entry key = 0, mask = 0;
                int write = 0;
                for (int i = 0; i < n; i++)
                {
                    ADMISSION
                    Entry nextKey = Key(id);
                    if (nextKey != key && mask != 0)
                    { output[write++] = key | mask; mask = 0; }
                    key = nextKey; mask |= Bit(id);
                }
                output[write++] = key | mask; used = write;
            }
            count = n;
#else
            else
            {
                int[] output = m_Ids;
                for (int i = 0; i < n; i++)
                { ADMISSION
                    output[i] = id;
                }
            }
            m_Count = n;
#endif
        }
'''.replace('ADMISSION',admit)

def generate(out):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    for rel in ('Audit~/BulkConstruction/Builders.cs.in','Audit~/BulkConstruction/generate.py',
        'Audit~/BulkConstruction/BulkProbe.cs','Audit~/BulkConstruction/BulkTests.cs'):
        if (ROOT/rel).read_bytes()!=subprocess.check_output(['git','show',PIN+':'+rel],cwd=ROOT):
            raise RuntimeError('Fixed PR12 input changed: '+rel)
    spec=importlib.util.spec_from_file_location('fixed_bulk12',ROOT/'Audit~/BulkConstruction/generate.py')
    old=importlib.util.module_from_spec(spec);spec.loader.exec_module(old)
    base=old.generate(out/'v1')
    template=(ROOT/'Audit~/PreparationV2/Builders.cs.in').read_text()
    result={'v1':base,'v2':{}}
    manifest={}
    for kind,ns,cls,mode in [('runtime','GameplayTags','RuntimeTagSet','TagSetStorage'),
        ('micro','GameplayTags.Experiments','DirectArraySet','BitmapLayout')]:
        folder=out/'v2'/kind;folder.mkdir(parents=True,exist_ok=True)
        core=folder/(cls+'.cs');core.write_bytes(base[kind][0].read_bytes())
        body=template.replace('__NAMESPACE__',ns).replace('__TYPE__',cls).replace('__MODE__',mode)
        body=body.replace('__FRESH_FILL__',fresh_fill()).replace('__FILL_METHODS__',old.fill_method(True)+'\n'+old.fill_method(False))
        added=folder/'BulkBuilders.cs';added.write_text(body)
        assert core.read_bytes()==base[kind][0].read_bytes()
        (folder/'builders-v1-v2.patch').write_text(''.join(difflib.unified_diff(base[kind][1].read_text().splitlines(True),body.splitlines(True),fromfile='PR12',tofile='preparation-v2')))
        result['v2'][kind]=[core,added]
        manifest[kind]={'core_sha256':hashlib.sha256(core.read_bytes()).hexdigest(),
            'v1_builder_sha256':hashlib.sha256(base[kind][1].read_bytes()).hexdigest(),
            'v2_builder_sha256':hashlib.sha256(added.read_bytes()).hexdigest(),
            'existing_set_methods_unchanged':True,'new_instance_fields':0}
    (out/'generation-v2.json').write_text(json.dumps({'base':PIN,'variants':manifest},indent=2))
    return result
if __name__=='__main__':generate(sys.argv[1])
