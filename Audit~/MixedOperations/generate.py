#!/usr/bin/env python3
"""Pinned PR14 controls and two independently measurable changes; no production edits."""
import pathlib, importlib.util, hashlib, json, difflib
ROOT = pathlib.Path(__file__).resolve().parents[2]
PIN = "db0ffc25448a5df399651550acf627d210482aeb"
EXPECTED = {
    "micro/DirectArraySet.cs": "3f959454b6b0ff8ab32df6138dcb1fba25bd5770dff1e3101a53ffc1b0be5527",
    "micro/BulkBuilders.cs": "22644785ab3d126f340c711d2bf45576461e090b31e4bcfaea0a4f6f003f8567",
    "runtime/RuntimeTagSet.cs": "b0c3cb2e00c65b66791c0f7c6b8f71057632b9167a3c9c53c74e3e1af42be61d",
    "runtime/BulkBuilders.cs": "c76b615803061b2ea6d78c4e9d058f69a1e558777cb9eb9b836b5eac1132c042",
}

def block(text, signature):
    start = text.index(signature)
    at = text.index("{", start); level = 1; end = at + 1
    while level:
        if text[end] == "{": level += 1
        elif text[end] == "}": level -= 1
        end += 1
    return text[start:end]

def cursor(text):
    old = block(text, "private struct Records")
    new = """private struct Records
        {
            private readonly DirectArraySet set;
            private int at;
            private ulong pending;
            public Entry Current;
            public Records(DirectArraySet value)
            {
                set = value;
                at = value.dense != null && value.count == 0 ? value.dense.Length : 0;
                pending = 0;
                Current = 0;
            }
            public bool MoveNext()
            {
                if (set.dense == null)
                {
                    if (at >= set.used) return false;
                    Current = set.Read(at++);
                    return true;
                }
                // Each dense word is loaded once. Only occupied local records are emitted.
                // Tail bits outside the registry are already zero by the set invariant.
                while (pending == 0)
                {
                    if (at >= set.dense.Length) return false;
                    pending = set.dense[at++];
                }
                int shift = Low(pending) & ~((1 << Shift) - 1);
                Entry bits = (Entry)(pending >> shift) & Mask;
                pending &= ~((ulong)Mask << shift);
                Current = Key(((at - 1) << 6) + shift) | bits;
                return true;
            }
        }"""
    return text.replace(old, new, 1)

def dense_result(text):
    marker = """            if (result.dense == null && a.dense == null && b.dense == null)
            {"""
    assert text.count(marker) == 1
    special = """            // Earlier guards have handled output aliases, empty/same input and all-Dense.
            // Here exactly one source is Dense; overwrite every output word before merging.
            if (result.dense != null && (a.dense != null || b.dense != null))
            {
                DirectArraySet full = a.dense != null ? a : b;
                DirectArraySet local = a.dense != null ? b : a;
                ulong[] output = result.dense;
                Array.Copy(full.dense, output, output.Length);
                int members = full.count;
                for (int i = 0; i < local.used; i++)
                {
                    Entry entry = local.Read(i);
                    int id = (int)(entry >> MaskBits) << Shift;
                    int word = id >> 6, shift = id & 63;
                    Entry incoming = entry & Mask;
                    ulong before = output[word];
                    members += Pop(incoming & ~(Entry)(before >> shift));
                    output[word] = before | ((ulong)incoming << shift);
                }
                result.count = members;
                return;
            }
"""
    return text.replace(marker, special + marker, 1)

def generate(out, fixed=None):
    out = pathlib.Path(out); out.mkdir(parents=True, exist_ok=True)
    if fixed is None:
        spec = importlib.util.spec_from_file_location("mixed_fixed_v3", ROOT/"Audit~/PreparationV3/generate.py")
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        fixed_files = module.generate(out/"pinned")["v3"]
    else:
        fixed = pathlib.Path(fixed)
        fixed_files = {kind: [fixed/kind/name for name in
            (("DirectArraySet.cs","BulkBuilders.cs") if kind=="micro" else ("RuntimeTagSet.cs","BulkBuilders.cs"))]
            for kind in ("micro","runtime")}
    for kind, files in fixed_files.items():
        for path in files:
            assert hashlib.sha256(path.read_bytes()).hexdigest() == EXPECTED[kind+"/"+path.name], str(path)
    original = fixed_files["micro"][0].read_text()
    sources = {"control": original, "cursor": cursor(original),
               "dense": dense_result(original), "combined": dense_result(cursor(original))}
    result = {}; manifest = {}
    for name, code in sources.items():
        folder = out/name; folder.mkdir(exist_ok=True)
        core = folder/"DirectArraySet.cs"; core.write_text(code)
        builder = folder/"BulkBuilders.cs"; builder.write_bytes(fixed_files["micro"][1].read_bytes())
        result[name] = [core, builder]
        (folder/"changes.patch").write_text("".join(difflib.unified_diff(
            original.splitlines(True), code.splitlines(True), fromfile=PIN, tofile=name)))
        # Public boundary, homogeneous kernels, instance state and capacity policy stay fixed.
        for sig in ("public bool HasTagExact(", "private void ReserveEntries(",
                    "private void AppendCore(", "public void RemoveTags("):
            assert block(code, sig) == block(original, sig)
        header = code[:code.index("public TagRegistry Registry")]
        assert header == original[:original.index("public TagRegistry Registry")]
        manifest[name] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in result[name]}
    (out/"generation.json").write_text(json.dumps({"base":PIN,"sources":manifest,
        "new_set_fields":0,"cursor_extra_field":"ulong pending in temporary Records value",
        "capacity_policy_changed":False}, indent=2))
    return result
