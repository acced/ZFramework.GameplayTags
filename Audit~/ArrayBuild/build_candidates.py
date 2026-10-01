#!/usr/bin/env python3
"""Generate literal single-variable micro-array candidates; never edit controls."""
import pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Audit~/Triad'))
from run_triad import generate,replace_body

def body(text,signature):
    if text.count(signature)!=1:raise RuntimeError('Method not unique: '+signature)
    start=text.index('{',text.index(signature));end=start+1;depth=1
    while depth:
        depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start+1:end-1]

def once(text,old,new):
    if text.count(old)!=1:raise RuntimeError('Unexpected source fragment: '+old[:100])
    return text.replace(old,new,1)

COUNT_ARRAYS='''
            // Resolve storage once, not through Read() for every record.
            if (a.entries != null && b.entries != null)
            {
                Entry[] x = a.entries, y = b.entries;
                int an = a.used, bn = b.used, ai = 0, bi = 0, length = 0;
                while (ai < an && bi < bn)
                {
                    Entry ak = x[ai] & ~Mask, bk = y[bi] & ~Mask;
                    if (ak <= bk) ai++;
                    if (bk <= ak) bi++;
                    length++;
                }
                return length + an - ai + bn - bi;
            }
'''
MERGE_HELPER='''
        // Independent destination only: public aliases were handled before this call.
        // The caller reserves at least the actual merged record count.
        private static int MergeArrays(Entry[] a, int an, Entry[] b, int bn, Entry[] output)
        {
            int i = 0, j = 0, w = 0, duplicates = 0;
            while (i < an && j < bn)
            {
                Entry x = a[i], y = b[j], kx = x & ~Mask, ky = y & ~Mask;
                if (kx < ky) { output[w++] = x; i++; }
                else if (ky < kx) { output[w++] = y; j++; }
                else { output[w++] = x | y; duplicates += Pop(x & y & Mask); i++; j++; }
            }
            if (i < an) Array.Copy(a, i, output, w, an - i);
            else if (j < bn) Array.Copy(b, j, output, w, bn - j);
            return duplicates;
        }
'''
UNION_ARRAYS='''                if (a.entries != null && b.entries != null && result.entries != null)
                {
                    int duplicatesArray = MergeArrays(a.entries, a.used, b.entries, b.used, result.entries);
                    // Record count, unlike member count, is not derivable from duplicate bits.
                    // Derive it in the merge below without a second traversal.
                    result.used = mergedRecords;
                    result.count = a.count + b.count - duplicatesArray;
                    return;
                }
'''
# Return both record length and duplicate cardinality in one traversal.
MERGE_HELPER=MERGE_HELPER.replace('Entry[] output)','Entry[] output, out int mergedRecords)')
MERGE_HELPER=MERGE_HELPER.replace('            return duplicates;','            mergedRecords = w + an - i + bn - j;\n            return duplicates;')
UNION_ARRAYS=UNION_ARRAYS.replace('b.used, result.entries);','b.used, result.entries, out int mergedRecords);')

APPEND_ARRAYS='''                if (entries != null && other.entries != null)
                {
@@GROW@@
                    ReserveEntries(length);
                    Entry[] a = entries, b = other.entries;
                    int ai = used - 1, bi = other.used - 1, wi = length - 1, duplicateBits = 0;
                    while (ai >= 0 && bi >= 0)
                    {
                        Entry x = a[ai], y = b[bi], kx = x & ~Mask, ky = y & ~Mask;
                        if (kx > ky) { a[wi--] = x; ai--; }
                        else if (ky > kx) { a[wi--] = y; bi--; }
                        else { a[wi--] = x | y; duplicateBits += Pop(x & y & Mask); ai--; bi--; }
                    }
                    while (bi >= 0) a[wi--] = b[bi--];
                    used = length;
                    count += other.count - duplicateBits;
                    return;
                }
                ReserveEntries(length);'''
GROW='''                    // Growth owns a new array anyway. Merge directly into it instead
                    // of first copying the old prefix and then moving it again.
                    if (length > entries.Length)
                    {
                        int capacity = (int)Math.Min(MaxEntries,
                            Math.Max(length, Math.Max(4L, 2L * EntryCapacity)));
                        var next = new Entry[capacity];
                        int duplicateBitsNew = MergeArrays(entries, used, other.entries, other.used, next, out int newUsed);
                        entries = next;
                        used = newUsed;
                        count += other.count - duplicateBitsNew;
                        return;
                    }'''
REMOVE_ARRAYS='''                if (entries != null && other.entries != null)
                {
                    Entry[] a = entries, b = other.entries;
                    int an = used, bn = other.used, ai = 0, bi = 0, wi = 0, removedBits = 0;
                    while (ai < an && bi < bn)
                    {
                        Entry x = a[ai], y = b[bi], kx = x & ~Mask, ky = y & ~Mask;
                        if (kx < ky) { a[wi++] = x; ai++; }
                        else if (ky < kx) bi++;
                        else
                        {
                            Entry deleted = x & y & Mask;
                            Entry remaining = x & ~deleted;
                            removedBits += Pop(deleted);
                            if ((remaining & Mask) != 0) a[wi++] = remaining;
                            ai++; bi++;
                        }
                    }
                    if (ai < an)
                    {
                        if (wi != ai) Array.Copy(a, ai, a, wi, an - ai);
                        wi += an - ai;
                    }
                    used = wi;
                    count -= removedBits;
                    return;
                }
'''

def candidates(source,out):
    out.mkdir(parents=True,exist_ok=True)
    old=generate(source,out)
    control=old['simd'].read_text().replace('OrderedSimdBitmapSet','KernelControlSet')
    control=replace_body(control,'private static int DenseUnion(ulong[] a, ulong[] b, ulong[] output)',
        '            return DenseFusion.Union(a,b,output);')
    control=replace_body(control,'private static int DenseDifference(ulong[] a, ulong[] b)',
        '            return DenseFusion.Difference(a,b,a);')
    files={'control':out/'KernelControlSet.cs'};files['control'].write_text(control)
    for label,name,grow in [('arrays','DirectArraySet',False),('growth','DirectGrowthSet',True)]:
        text=control.replace('KernelControlSet',name)
        sig='private static int CountRecords('
        text=replace_body(text,sig,COUNT_ARRAYS+body(text,sig))
        sig='private static void UnionCore('
        inner=body(text,sig)
        inner=once(inner,'                int i = 0, j = 0, write = 0, duplicates = 0;',
            UNION_ARRAYS+'                int i = 0, j = 0, write = 0, duplicates = 0;')
        text=replace_body(text,sig,inner)
        sig='private void AppendCore('
        inner=body(text,sig)
        inner=once(inner,'                int length = CountRecords(this, other); ReserveEntries(length);',
            '                int length = CountRecords(this, other);\n'+APPEND_ARRAYS.replace('@@GROW@@',GROW if grow else ''))
        text=replace_body(text,sig,inner)
        sig='public void RemoveTags('
        inner=body(text,sig)
        inner=once(inner,'                int i = 0, j = 0, w = 0, removed = 0;',
            REMOVE_ARRAYS+'                int i = 0, j = 0, w = 0, removed = 0;')
        text=replace_body(text,sig,inner)
        text=once(text,'        public void AssertInvariants()',MERGE_HELPER+'\n        public void AssertInvariants()')
        files[label]=out/(name+'.cs');files[label].write_text(text)
    return files
