#!/usr/bin/env python3
"""Private Dense traversal; never enlarge public Records/Enumerator or stored sets."""
import pathlib, importlib.util, hashlib, json, difflib
ROOT=pathlib.Path(__file__).resolve().parents[2]
PIN="5d863777ac5c7a546785105ac0c4740fa4360ad5"
spec=importlib.util.spec_from_file_location("pr15_generation",ROOT/"Audit~/MixedOperations/generate.py")
prior=importlib.util.module_from_spec(spec);spec.loader.exec_module(prior)
EXPECTED=prior.EXPECTED
block=prior.block

CURSOR="""
        // Private to materialization kernels. Public Records/Enumerator remain unchanged.
        // Tail bits are zero by the source invariant. No concurrent source mutation.
        private struct DenseRecordCursor
        {
            private readonly ulong[] words;
            private int next;
            private ulong pending;
            public DenseRecordCursor(ulong[] source)
            { words = source; next = 0; pending = 0; }
            public bool MoveNext(out Entry record)
            {
                while (pending == 0)
                {
                    if (next == words.Length) { record = 0; return false; }
                    pending = words[next++];
                }
                int offset = Low(pending) & ~((1 << Shift) - 1);
                Entry bits = (Entry)(pending >> offset) & Mask;
                pending &= ~((ulong)Mask << offset);
                record = Key(((next - 1) << 6) + offset) | bits;
                return true;
            }
        }
"""
UNION_METHOD="""
        // Exactly one Dense input, one Micro input, independent Micro output.
        // Existing UnionCore admission/alias/empty guards establish these conditions.
        private static void UnionDenseToMicro(DirectArraySet full, DirectArraySet local, DirectArraySet result)
        {
            result.Clear();
            var records = new DenseRecordCursor(full.dense);
            Entry x;
            bool hasDense = records.MoveNext(out x);
            int i = 0, duplicates = 0;
            while (hasDense && i < local.used)
            {
                Entry y = local.Read(i), kx = x & ~Mask, ky = y & ~Mask;
                if (kx < ky)
                { result.PutRecord(x); hasDense = records.MoveNext(out x); }
                else if (ky < kx)
                { result.PutRecord(y); i++; }
                else
                {
                    result.PutRecord(x | y);
                    duplicates += Pop(x & y & Mask);
                    i++;
                    hasDense = records.MoveNext(out x);
                }
            }
            while (hasDense)
            { result.PutRecord(x); hasDense = records.MoveNext(out x); }
            while (i < local.used) result.PutRecord(local.Read(i++));
            result.count = full.count + local.count - duplicates;
        }
"""
COPY_METHOD="""
        // Called only for Dense source and Micro destination, after Require/alias guards.
        private void CopyDenseToMicro(DirectArraySet source)
        {
            Clear();
            if (source.count == 0) return;
            var records = new DenseRecordCursor(source.dense);
            Entry record;
            while (records.MoveNext(out record)) PutRecord(record);
        }
"""

def add_cursor(text):
    marker="        private struct Records"
    assert text.count(marker)==1
    return text.replace(marker,CURSOR+"\n"+marker,1)

def scoped_union(text):
    marker="""            if (result.dense == null && a.dense == null && b.dense == null)
            {"""
    assert text.count(marker)==1
    branch="""            if (result.dense == null && ((a.dense == null) != (b.dense == null)))
            {
                UnionDenseToMicro(a.dense != null ? a : b, a.dense == null ? a : b, result);
                return;
            }
"""
    text=text.replace(marker,branch+marker,1)
    marker="        private struct Records"
    return text.replace(marker,UNION_METHOD+"\n"+marker,1)

def scoped_copy(text):
    old=block(text,"private void CopyCore(")
    marker="""            else
            {
                Clear(); var it = new Records(source);"""
    assert old.count(marker)==1
    new=old.replace(marker,"""            else if (dense == null)
            {
                CopyDenseToMicro(source);
            }
"""+marker,1)
    text=text.replace(old,new,1)
    marker="        private struct Records"
    return text.replace(marker,COPY_METHOD+"\n"+marker,1)

def variant_sources(original):
    # Control is PR15's accepted-candidate 'dense' build, NOT PR14 or rejected combined.
    base=prior.dense_result(original)
    return {"control":base,"union":scoped_union(add_cursor(base)),
            "copy":scoped_copy(add_cursor(base)),
            "combined":scoped_copy(scoped_union(add_cursor(base)))}

def generate(out):
    out=pathlib.Path(out);out.mkdir(parents=True,exist_ok=True)
    fixed=prior.generate(out/"prior")
    original=fixed["control"][0].read_text()
    basefolder=out/"base";basefolder.mkdir(exist_ok=True)
    (basefolder/"PR14_DirectArraySet.cs").write_text(original)
    sources=variant_sources(original)
    assert sources["control"]==fixed["dense"][0].read_text()
    result={};manifest={}
    for name,code in sources.items():
        folder=out/name;folder.mkdir(exist_ok=True)
        core=folder/"DirectArraySet.cs";core.write_text(code)
        builder=folder/"BulkBuilders.cs";builder.write_bytes(fixed["control"][1].read_bytes())
        result[name]=[core,builder]
        for sig in ("private struct Records","public struct Enumerator","public bool HasTagExact(",
                    "private void ReserveEntries(","private void AppendCore(","public void RemoveTags("):
            assert block(code,sig)==block(sources["control"],sig),sig
        assert code[:code.index("public TagRegistry Registry")]==sources["control"][:sources["control"].index("public TagRegistry Registry")]
        (folder/"changes.patch").write_text("".join(difflib.unified_diff(sources["control"].splitlines(True),code.splitlines(True),fromfile="PR15_dense",tofile=name)))
        manifest[name]={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in result[name]}
    (out/"generation.json").write_text(json.dumps({"base":PIN,"sources":manifest,
        "public_records_unchanged":True,"public_enumerator_unchanged":True,"new_set_fields":0,
        "capacity_policy_changed":False,"private_cursor":"ulong[] words, int next, ulong pending"},indent=2))
    return result
