"""Materialize audited source payload once; never publishes main or executes payload code."""
import base64, hashlib, json, pathlib, subprocess, zlib
root = pathlib.Path(__file__).resolve().parents[2]
here = root / 'Tools~/Hardening'
marker = here / 'materialized.json'
if marker.exists():
    raise SystemExit(0)
encoded = ''.join((here / f'payload-{i}.txt').read_text().strip() for i in range(5))
compressed = base64.b64decode(encoded, validate=True)
assert hashlib.sha256(compressed).hexdigest() == '5e86a928b7e35269aa38808c903f412a8a0655e40d7e67b508b027c084ff8077'
data = json.loads(zlib.decompress(compressed))
allowed = {'Tools~/Hardening/hardening.patch', 'Tools~/Hardening/uploaded-storage-fragment.txt', 'Tools~/Hardening/uploaded-regression-fragment.txt', 'Tests~/Hardening/Program.cs', 'Tests~/Hardening/Hardening.csproj'}
assert set(data) == allowed | {'expected_runtime'}
for name in allowed:
    dest = root / name
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(data[name], encoding='utf-8', newline='\n')
for folder in ['Runtime', 'Editor', 'Tests~']:
    for p in (root / folder).rglob('*.cs'):
        p.write_text(p.read_text(encoding='utf-8-sig'), encoding='utf-8', newline='\n')
p = root / 'Runtime/TagStorage.cs'
s = p.read_text()
start = s.index('        internal void BuildFromExplicitIds(')
end = s.index('        private static int ArrayCapacityFor(', start)
s = s[:start] + data['Tools~/Hardening/uploaded-storage-fragment.txt'] + s[end:]
p.write_text(s, encoding='utf-8', newline='\n')
for name, expected in data['expected_runtime'].items():
    p = root / 'Runtime' / name
    assert hashlib.sha256(p.read_bytes()).hexdigest() == expected, f'Uploaded baseline mismatch: {name}'
    dest = root / 'Tests~/Hardening/Baseline/Runtime' / name
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(p.read_bytes())
p = root / 'Tests~/Program.cs'
s = p.read_text()
assert 'private static void BulkIntersections()' not in s
pos = s.index('    private static void AssertQueries(')
s = s[:pos] + data['Tools~/Hardening/uploaded-regression-fragment.txt'] + s[pos:]
anchor = '        Run("copy, union, intersection, aliases and reuse", SetOperations);'
assert anchor in s
s = s.replace(anchor, anchor + '\n        Run("uploaded bulk intersection regression", BulkIntersections);')
p.write_text(s, encoding='utf-8', newline='\n')
subprocess.run(['git','apply','--whitespace=nowarn', str(here / 'hardening.patch')], cwd=root, check=True)
manifest = {'upstream': '21e2d93d85380fb9267284c5469fb21d31d21a78', 'uploaded_archive_sha256': 'dfc429acae4db5f3e3d37160567b41c6b09e8d07a6dfd84087e3e2c39a1cb6f4', 'normalized_uploaded_runtime_sha256': data['expected_runtime'], 'materialization_only': True, 'validation': 'not-yet-run'}
marker.write_text(json.dumps(manifest, indent=2) + '\n')
for i in range(5): (here / f'payload-{i}.txt').unlink()
