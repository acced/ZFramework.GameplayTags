#!/usr/bin/env python3
"""Experiments on the original unrestricted Auto representation; no production edits."""
import difflib,pathlib
AUTO='84729cc3b6e1b4681328806df2d7c8a1d8f568f6'

def once(text,old,new):
    if text.count(old)!=1: raise RuntimeError('Pinned fragment mismatch: '+old[:100])
    return text.replace(old,new,1)

def dense(text):
    text=once(text,'using System;','using System;\nusing GameplayTags.Experiments;')
    text=once(text,'''                    int added = 0;
                    for (int i = 0; i < m_Words.Length; i++)
                    {
                        ulong before = m_Words[i], incoming = other.m_Words[i];
                        added += Bits.Count(incoming & ~before);
                        m_Words[i] = before | incoming;
                    }
                    m_Count += added;''','''                    m_Count = DenseFusion.Union(m_Words, other.m_Words, m_Words);''')
    text=once(text,'''                    int removed = 0;
                    for (int i = 0; i < m_Words.Length; i++)
                    {
                        ulong old = m_Words[i], incoming = other.m_Words[i];
                        removed += Bits.Count(old & incoming);
                        m_Words[i] = old & ~incoming;
                    }
                    m_Count -= removed;''','''                    m_Count = DenseFusion.Difference(m_Words, other.m_Words, m_Words);''')
    text=once(text,'''                    int count = 0;
                    for (int i = 0; i < result.m_Words.Length; i++)
                    {
                        ulong word = left.m_Words[i] | right.m_Words[i];
                        result.m_Words[i] = word;
                        count += Bits.Count(word);
                    }
                    result.m_Count = count;''','''                    result.m_Count = DenseFusion.Union(left.m_Words, right.m_Words, result.m_Words);''')
    return text

def simd(text):
    text=once(text,'using System;','using System;\nusing System.Numerics;\nusing System.Runtime.CompilerServices;')
    text=once(text,'            : IndexOf(id) >= 0;', '            : ContainsSparseId(id);')
    marker='        private int IndexOf(int id)'
    helper='''        // Only boolean membership changes. Ordered insertion and DFS lower bounds
        // still use the unchanged IndexOf. No loads beyond the active prefix.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ContainsSparseId(int id)
        {
            int n = m_Count, width = Vector<int>.Count;
            if (Vector.IsHardwareAccelerated && n >= width && n <= 32)
            {
                int[] values = m_Ids;
                var target = new Vector<int>(id);
                var found = Vector<int>.Zero;
                int i = 0;
                for (; i <= n - width; i += width)
                    found = Vector.BitwiseOr(found, Vector.Equals(new Vector<int>(values, i), target));
                if (!Vector.EqualsAll(found, Vector<int>.Zero)) return true;
                for (; i < n; i++) if (values[i] == id) return true;
                return false;
            }
            return IndexOf(id) >= 0;
        }
'''
    return once(text,marker,helper+marker)

def copy(text):
    return once(text,'''        public RuntimeTagSet(RuntimeTagSet source)
            : this(Required(source).Registry, source.Capacity, source.Storage) { CopyFrom(source); }''','''        // Explicit independent ownership. Preserve source capacity, representation,
        // eager Count and the empty-buffer policy; do not silently trim or fuse.
        public RuntimeTagSet(RuntimeTagSet source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Registry = source.Registry;
            m_Count = source.m_Count;
            if (source.m_Words != null)
            {
                int n = source.m_Words.Length;
                m_Words = n == 0 ? Array.Empty<ulong>() : new ulong[n];
                Array.Copy(source.m_Words, m_Words, n);
            }
            else
            {
                int n = source.m_Ids.Length;
                m_Ids = n == 0 ? Array.Empty<int>() : new int[n];
                Array.Copy(source.m_Ids, m_Ids, m_Count);
            }
        }''')

def generate(original,out):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    variants={'auto':original,'dense':dense(original),'simd':simd(original),'copy':copy(original),
              'combined':copy(simd(dense(original)))}
    for name,source in variants.items():
        p=out/name;p.mkdir(exist_ok=True)
        (p/'RuntimeTagSet.cs').write_text(source)
        (p/'changes.patch').write_text(''.join(difflib.unified_diff(original.splitlines(True),source.splitlines(True),fromfile=AUTO+':Runtime/RuntimeTagSet.cs',tofile=name+'/RuntimeTagSet.cs')))
    return variants
