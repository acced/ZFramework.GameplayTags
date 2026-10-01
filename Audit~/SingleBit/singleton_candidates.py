#!/usr/bin/env python3
"""Count==used experiment. Literal DirectArray control; no extra state or encoding."""
import pathlib,sys,difflib,hashlib,json
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Audit~/ArrayBuild'))
from build_candidates import candidates as array_candidates,body,once
from run_triad import replace_body

REMOVE_SINGLE='''                // Every nonzero record contains >=1 bit. Equality proves one bit per record.
                // Removal preserves that property; other need NOT have singleton records.
                if (count == used)
                {
                    Entry[] sa = entries, sb = other.entries;
                    int san = used, sbn = other.used, si = 0, sj = 0, sw = 0;
                    while (si < san && sj < sbn)
                    {
                        Entry x = sa[si], y = sb[sj], kx = x & ~Mask, ky = y & ~Mask;
                        if (kx < ky) { sa[sw++] = x; si++; }
                        else if (ky < kx) sj++;
                        else
                        {
                            if ((x & y & Mask) == 0) sa[sw++] = x;
                            si++; sj++;
                        }
                    }
                    if (si < san)
                    {
                        if (sw != si) Array.Copy(sa, si, sa, sw, san - si);
                        sw += san - si;
                    }
                    used = sw;
                    count = sw;
                    return;
                }
'''
APPEND_SINGLE='''                    // Snapshot the predicate before changing used/count.
                    // At least one single-bit side bounds each overlapping mask to one bit.
                    if (count == used || other.count == other.used)
                    {
                        ReserveEntries(length);
                        Entry[] sa = entries, sb = other.entries;
                        int si = used - 1, sj = other.used - 1, sw = length - 1, sd = 0;
                        while (si >= 0 && sj >= 0)
                        {
                            Entry x = sa[si], y = sb[sj], kx = x & ~Mask, ky = y & ~Mask;
                            if (kx > ky) { sa[sw--] = x; si--; }
                            else if (ky > kx) { sa[sw--] = y; sj--; }
                            else { sa[sw--] = x | y; sd += (x & y & Mask) != 0 ? 1 : 0; si--; sj--; }
                        }
                        while (sj >= 0) sa[sw--] = sb[sj--];
                        used = length;
                        count += other.count - sd;
                        return;
                    }
'''

def candidates(source,out):
    prior=array_candidates(source,out/'pinned-array-generator')
    control=prior['arrays'].read_text().replace('DirectArraySet','DirectControlSet')
    files={'control':out/'DirectControlSet.cs'}
    files['control'].write_text(control)
    removal=control.replace('DirectControlSet','SingleRemoveSet')
    sig='public void RemoveTags('
    text=body(removal,sig)
    marker='''                if (entries != null && other.entries != null)
                {
'''
    text=once(text,marker,marker+REMOVE_SINGLE)
    removal=replace_body(removal,sig,text)
    files['remove']=out/'SingleRemoveSet.cs';files['remove'].write_text(removal)
    both=removal.replace('SingleRemoveSet','SingleBitSet')
    sig='private void AppendCore('
    text=body(both,sig);text=once(text,marker,marker+APPEND_SINGLE)
    both=replace_body(both,sig,text)
    sig='private static int MergeArrays('
    general=body(both,sig)
    simple=once(general,'duplicates += Pop(x & y & Mask);','duplicates += (x & y & Mask) != 0 ? 1 : 0;')
    both=replace_body(both,sig,'\n            if (singleOverlap)\n            {\n'+simple+'\n            }\n            else\n            {\n'+general+'\n            }')
    both=once(both,'Entry[] output, out int mergedRecords)','Entry[] output, bool singleOverlap, out int mergedRecords)')
    both=once(both,'b.used, result.entries, out int mergedRecords);',
        'b.used, result.entries, a.count == a.used || b.count == b.used, out int mergedRecords);')
    files['single']=out/'SingleBitSet.cs';files['single'].write_text(both)
    unchanged=['public bool HasTagExact(', 'private static int DenseUnion(', 'private static int DenseDifference(',
               'public void CopyFrom(', 'private void CopyCore(', 'private void ReserveEntries(', 'private static int CountRecords(']
    for label,p in files.items():
        text=p.read_text().replace(p.stem,'DirectControlSet')
        for sig in unchanged:
            if body(text,sig)!=body(control,sig):raise RuntimeError('Unexpected change: '+label+' '+sig)
        a=control[:control.index('        public DirectControlSet(')]
        b=text[:text.index('        public DirectControlSet(')]
        if a!=b:raise RuntimeError('Changed state or property layout')
        if label!='control':
            (out/(label+'.patch')).write_text(''.join(difflib.unified_diff(control.splitlines(True),text.splitlines(True),fromfile='DirectControlSet.cs',tofile=p.name)))
    (out/'single-bit-manifest.json').write_text(json.dumps({'predicate_scope':'array-backed Micro only; Dense/inline/mixed fallback unchanged',
        'no_new_fields':True,'unchanged_methods':unchanged,'generated':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in files.values()}},indent=2))
    return files
