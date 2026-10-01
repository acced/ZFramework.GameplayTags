#!/usr/bin/env python3
"""Fixed PR10 source -> independent query variants, no production edits."""
import pathlib,sys,hashlib,difflib,json
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path[:0]=[str(ROOT/'Audit~/AdaptiveResearch'),str(ROOT/'Audit~/ArrayBuild')]
import variants
from build_candidates import candidates

def generate(out):
 out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
 source=(ROOT/'Runtime/RuntimeTagSet.cs').read_text()
 # Same direct-copy and DenseFusion in every query candidate.
 base=variants.copy(variants.dense(source))
 helper='''
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool QueryLinear(int id)
        {
            int[] a = m_Ids; int n = m_Count;
            for (int i = 0; i < n; i++) if (a[i] == id) return true;
            return false;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool QueryVector(int id)
        {
            int[] a = m_Ids; int n = m_Count, w = System.Numerics.Vector<int>.Count, i = 0;
            if (System.Numerics.Vector.IsHardwareAccelerated && n >= w)
            {
                var target = new System.Numerics.Vector<int>(id);
                var found = System.Numerics.Vector<int>.Zero;
                for (; i <= n - w; i += w)
                    found |= System.Numerics.Vector.Equals(new System.Numerics.Vector<int>(a,i),target);
                if (!System.Numerics.Vector.EqualsAll(found,System.Numerics.Vector<int>.Zero)) return true;
            }
            for (; i < n; i++) if (a[i] == id) return true;
            return false;
        }
'''
 variants_out={'original':source,'binary':base}
 for name,expression in [('linear','QueryLinear(id)'),('vector','QueryVector(id)'),
   ('bounded','m_Count <= 2 ? QueryLinear(id) : (m_Count >= System.Numerics.Vector<int>.Count && m_Count <= 2 * System.Numerics.Vector<int>.Count ? QueryVector(id) : IndexOf(id) >= 0)')]:
  code=base.replace(': IndexOf(id) >= 0;',': ('+expression+');')
  assert code!=base
  code=code.replace('        private int IndexOf(int id)',helper+'\n        private int IndexOf(int id)')
  variants_out[name]=code
 for name,code in variants_out.items():
  folder=out/name;folder.mkdir(exist_ok=True);(folder/'RuntimeTagSet.cs').write_text(code)
  (folder/'changes.patch').write_text(''.join(difflib.unified_diff(source.splitlines(True),code.splitlines(True),fromfile='Auto84729cc',tofile=name)))
 micros=candidates((ROOT/'Audit~/MicroBitmapSet.cs').read_text(),out/'micro-generated')
 # Normalize copy capacity to the same independent-copy contract as Array/Dense.
 micro=micros['arrays'].read_text()
 old='            else if (used <= 1) inline = used == 0 ? 0 : source.Read(0);\n            else { entries = new Entry[used]; Array.Copy(source.entries, entries, used); }'
 new='            else if (source.entries == null) inline = used == 0 ? 0 : source.inline;\n            else { entries = new Entry[source.entries.Length]; Array.Copy(source.entries, entries, used); }'
 assert micro.count(old)==1
 normalized=micro.replace(old,new)
 (out/'micro-copy-capacity.patch').write_text(''.join(difflib.unified_diff(micro.splitlines(True),normalized.splitlines(True),fromfile='PR8-compact-copy',tofile='same-capacity-copy')))
 micros['arrays'].write_text(normalized)
 (out/'hashes.json').write_text(json.dumps({k:hashlib.sha256(v.encode()).hexdigest() for k,v in variants_out.items()},indent=2))
 return variants_out,micros['arrays']
if __name__=='__main__':generate(sys.argv[1])
