#!/usr/bin/env python3
"""One candidate: PR13 fresh APIs + exact PR12 reset chain + empty conversion.
Old source stays immutable. Generated alternatives are separate build targets.
"""
import pathlib, importlib.util, subprocess, hashlib, json, difflib, sys
ROOT = pathlib.Path(__file__).resolve().parents[2]
PIN = 'aae745466fde9bec4e11d986886572be4c6bec2e'

def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def method(text, signature):
    start = text.index(signature)
    brace = text.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        if text[end] == '{': depth += 1
        elif text[end] == '}': depth -= 1
        end += 1
    return text[start:end]

def generate(out):
    out = pathlib.Path(out); out.mkdir(parents=True, exist_ok=True)
    for rel in ('Audit~/PreparationV2/Builders.cs.in', 'Audit~/PreparationV2/generate.py',
                'Audit~/PreparationV2/Extras.cs', 'Audit~/PreparationV2/review.py'):
        expected = subprocess.check_output(['git', 'show', PIN + ':' + rel], cwd=ROOT)
        if (ROOT / rel).read_bytes() != expected:
            raise RuntimeError('Pinned PR13 dependency changed: ' + rel)
    previous = load(ROOT / 'Audit~/PreparationV2/generate.py', 'fixed_prep13')
    files = previous.generate(out / 'controls')
    files['v3'] = {}
    manifest = {}
    for kind in ('runtime', 'micro'):
        v1 = files['v1'][kind][1].read_text()
        v2 = files['v2'][kind][1].read_text()
        candidate = v2
        signatures = ('public void ResetFromSortedUnique(', 'private static void ValidateSortedHandles(')
        for signature in signatures:
            candidate = candidate.replace(method(candidate, signature), method(v1, signature), 1)
        # Replace only inside the conversion factory: options and reservation stay ahead of exit.
        signature = 'public ' + ('RuntimeTagSet' if kind == 'runtime' else 'DirectArraySet') + ' CopyAsStorage('
        before = method(candidate, signature)
        line = 'var result = new ' + ('RuntimeTagSet' if kind == 'runtime' else 'DirectArraySet') + '(Registry, reserved, storage == ' + ('TagSetStorage' if kind == 'runtime' else 'BitmapLayout') + '.Dense);'
        assert before.count(line) == 1
        after = before.replace(line, line + '\n            // Count==0 implies no member bits; new storage is already zero initialized.\n            // Still allocate the independent result and honor the explicit target capacity.\n            if (Count == 0) return result;', 1)
        candidate = candidate.replace(before, after, 1)
        for signature in signatures:
            assert method(candidate, signature) == method(v1, signature)
        for signature in ('private void FillSortedHandles(', 'private void FillSortedIds('):
            assert method(candidate, signature) == method(v1, signature)
        for signature in ('public static ', 'private void ValidateAndFillSorted('):
            assert method(candidate, signature) == method(v2, signature)
        folder = out / 'v3' / kind; folder.mkdir(parents=True, exist_ok=True)
        core = folder / files['v2'][kind][0].name
        core.write_bytes(files['v2'][kind][0].read_bytes())
        builder = folder / 'BulkBuilders.cs'; builder.write_text(candidate)
        (folder / 'v2-v3.patch').write_text(''.join(difflib.unified_diff(v2.splitlines(True), candidate.splitlines(True), fromfile='PR13-v2', tofile='combined-v3')))
        files['v3'][kind] = [core, builder]
        manifest[kind] = {'core_sha256': hashlib.sha256(core.read_bytes()).hexdigest(),
                          'v3_builder_sha256': hashlib.sha256(builder.read_bytes()).hexdigest(),
                          'reset_and_validation_exact_PR12': True, 'new_fields': 0,
                          'fresh_methods_PR13': True, 'empty_conversion_skips_scan_after_allocation': True}
    (out / 'generation-v3.json').write_text(json.dumps({'base': PIN, 'variants': manifest}, indent=2))
    return files

if __name__ == '__main__': generate(sys.argv[1])
