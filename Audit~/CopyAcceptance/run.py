#!/usr/bin/env python3
"""Read two completed, fixed CI artifacts. Do not generate new timing samples."""
from pathlib import Path
import csv
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile

RUNS = {
    'commit': dict(run=37013478442, artifact=11229292462,
                   sha='95ac409cc1b630b9be80e5c3e9731c0f33f30b6b3650196f33fe423cf3e74f04',
                   head='4da164547cc35a9808f0eb2032425c0e499451d4', reviewer='CopyCommit'),
    'capacity': dict(run=37015815469, artifact=11230426954,
                     sha='056dacb5bfe91c22aee657ba9f4e8252d84f4b87c12a5bb3913bb716ef1acf9c',
                     head='ff1e1e5aa523ea5989374925acd5b1828cc11cc6', reviewer='CopyCapacity'),
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def api(path):
    return subprocess.check_output(['gh', 'api', path], timeout=180)


def extract(data, destination):
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for entry in archive.infolist():
            path = (destination / entry.filename).resolve()
            if path != destination and destination not in path.parents:
                raise ValueError('Unsafe archive path: ' + entry.filename)
        archive.extractall(destination)


def read_csv(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def write_csv(path, rows):
    if not rows:
        return
    with path.open('w', encoding='utf-8-sig', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def stats(rows):
    return dict(cases=len(rows), fast=sum(r['fast'] for r in rows),
                slow=sum(r['slow'] for r in rows), stable=sum(r['stable'] for r in rows),
                fastStable=sum(r['fast'] and r['stable'] for r in rows),
                slowStable=sum(r['slow'] and r['stable'] for r in rows))


def table(rows, keys):
    if not rows:
        return '(none)\n'
    lines = ['|' + '|'.join(keys) + '|', '|' + '|'.join('---' for _ in keys) + '|']
    for row in rows:
        values = [f'{row[k]:.3f}' if isinstance(row[k], float) else str(row[k]) for k in keys]
        lines.append('|' + '|'.join(v.replace('|', '/') for v in values) + '|')
    return '\n'.join(lines) + '\n'


def main():
    out = Path(sys.argv[1]).resolve()
    delivery = out / 'delivery'
    delivery.mkdir(parents=True, exist_ok=True)
    repo = os.environ['GITHUB_REPOSITORY']
    metadata, faults, groups, focus, all_results, all_comparisons = {}, [], [], [], [], []
    identities = {}
    codegen, tests, aa, counts = {}, {}, {}, {}

    for phase, pin in RUNS.items():
        run = json.loads(api(f'/repos/{repo}/actions/runs/{pin["run"]}'))
        assert run['status'] == 'completed' and run['conclusion'] == 'success'
        assert run['head_sha'] == pin['head']
        artifact = json.loads(api(f'/repos/{repo}/actions/artifacts/{pin["artifact"]}'))
        assert not artifact['expired'] and artifact['digest'] == 'sha256:' + pin['sha']
        raw = api(f'/repos/{repo}/actions/artifacts/{pin["artifact"]}/zip')
        assert digest(raw) == pin['sha']
        (out / (phase + '-original.zip')).write_bytes(raw)
        root = out / phase
        extract(raw, root)
        collection = json.loads((root / 'COLLECTION.json').read_text())
        source = (root / 'Source_Tested.zip').read_bytes()
        (delivery / (phase + '-Source_Tested.zip')).write_bytes(source)
        metadata[phase] = dict(pin, bytes=len(raw), source_sha256=digest(source))

        for arch in ('x64', 'arm64'):
            results = root / arch / 'results'
            before = json.loads((results / 'recheck.json').read_text())
            assert before['head'] == pin['head'] and not before['smoke']
            assert before['source_verified'] and before['allocation_contracts_equal']
            process = subprocess.run([
                'python3', str(root / 'source' / 'Audit~' / pin['reviewer'] / 'review.py'),
                '--root', str(results)], capture_output=True, text=True, timeout=240)
            (out / f'{phase}-{arch}-recheck.log').write_text(process.stdout + process.stderr)
            if process.returncode:
                raise RuntimeError(process.stdout[-10000:] + process.stderr[-10000:])
            assert before == json.loads((results / 'recheck.json').read_text())
            assert before['head'] == collection['architectures'][arch]['head']
            label = phase + '/' + arch
            counts[label] = {k: before[k] for k in ('aggregate_rows', 'raw_rows', 'timing_samples', 'zero_allocation_samples')}
            tests[label] = before['tests']
            aa[label] = before['AA']
            generation = json.loads((results / 'generated' / 'generation.json').read_text())
            names = ('control', 'eager', 'checkpoint') if phase == 'commit' else ('control', 'eager', 'guarded')
            identities[label] = {}
            codegen[label] = {}
            for name in names:
                core = (results / 'generated' / name / 'DirectArraySet.cs').read_bytes()
                core_hash = digest(core)
                assert generation['sources'][name]['DirectArraySet.cs'] == core_hash
                identities[label][name] = core_hash
                if arch == 'arm64':
                    assert identities[phase + '/x64'][name] == core_hash
                if phase == 'capacity' and name in ('control', 'eager'):
                    assert identities['commit/' + arch][name] == core_hash
                if arch == 'x64':
                    target = delivery / ('Generated_' + phase) / name
                    target.mkdir(parents=True, exist_ok=True)
                    for filename in ('DirectArraySet.cs', 'BulkBuilders.cs', 'changes.patch'):
                        shutil.copyfile(results / 'generated' / name / filename, target / filename)
                fault = before['faults'][name]
                assert fault['failures'] == 0 and fault['cases'] == fault['injected']
                if name == 'control':
                    assert fault['minimalBad'] == 1 and fault['invalid']
                else:
                    assert not fault['invalid']
                faults.append(dict(phase=phase, architecture=arch, variant=name,
                                   normal=fault['normal'], injected=fault['injected'],
                                   invalid=len(fault['invalid']), minimalBad=fault['minimalBad'],
                                   checks=fault['checks']))
                text = (results / (name + '-codegen.log')).read_text()
                shutil.copyfile(results / (name + '-codegen.log'), delivery / f'{phase}-{arch}-{name}-codegen.log')
                methods = []
                for match in re.finditer(r'Assembly listing for method ([^\n]+)\n(.*?); Total bytes of code (\d+)', text, re.S):
                    method, body, size = match.group(1), match.group(2), int(match.group(3))
                    if any(token in method for token in ('CopyDense', 'CopyCore', 'CopyCaseLoop', 'CopyLoop')):
                        methods.append(dict(method=method, bytes=size,
                                            calls=[line.strip() for line in body.splitlines() if re.search(r'\b(call|bl)\s', line)]))
                codegen[label][name] = methods

        rows = []
        for row in read_csv(root / 'All_Comparisons.csv'):
            r = dict(row, phase=phase)
            r['shape'] = json.loads(row['shape'])
            r['fast'], r['slow'], r['stable'] = (row[k] == 'True' for k in ('faster5', 'slower5', 'AAstable'))
            for key in ('universe', 'members'):
                r[key] = int(row[key])
            for key in ('baseline_ns', 'candidate_ns', 'bytes', 'ratio'):
                r[key] = float(row[key])
            rows.append(r)
        for arch, candidate, control, stage in sorted({(r['architecture'], r['candidate'], r['control'], r['stage']) for r in rows}):
            subset = [r for r in rows if (r['architecture'], r['candidate'], r['control'], r['stage']) == (arch, candidate, control, stage)]
            if stage == 'copy':
                partitions = [('empty_source', [r for r in subset if r['members'] == 0]),
                              ('nonempty_fresh', [r for r in subset if r['members'] > 0 and r['operation'] in ('fresh_copy', 'union_empty_fresh')]),
                              ('nonempty_prepared', [r for r in subset if r['members'] > 0 and r['operation'] not in ('fresh_copy', 'union_empty_fresh')])]
            else:
                direct_copy = lambda r: r['operation'] == 'copy_from' and r['left'] == 'Dense' and r['output'] == 'Micro' and r['members'] > 0
                partitions = [('direct_nonempty_copy', [r for r in subset if direct_copy(r)]),
                              ('other_including_indirect_copy', [r for r in subset if not direct_copy(r)])]
            for name, partition in partitions:
                groups.append(dict(phase=phase, architecture=arch, candidate=candidate, control=control,
                                   stage=stage, group=name, **stats(partition)))
        for r in rows:
            if r['stage'] == 'copy' and r['universe'] == 262144 and r['members'] in (8, 4096) and r['operation'] in ('fresh_copy', 'prepared_copy') and (r['members'] != 8 or r['distribution'] == 'scattered'):
                focus.append({k:r[k] for k in ('phase','architecture','candidate','control','operation','members','distribution','baseline_ns','candidate_ns','bytes','fast','slow','stable')})
        all_comparisons.extend(rows)
        all_results.extend(dict(phase=phase, **r) for r in read_csv(root / 'All_Architectures.csv'))

    # Preserve phase labels. Identical code is not permission to splice timings across runs.
    encoded = [dict(r, shape=json.dumps(r['shape'], sort_keys=True)) for r in all_comparisons]
    write_csv(delivery / 'All_Comparisons.csv', encoded)
    write_csv(delivery / 'All_Results.csv', all_results)
    negative = [r for r in all_comparisons if r['slow']]
    (delivery / 'All_Regressions.json').write_text(json.dumps(negative, indent=2))
    summary = dict(metadata=metadata, source_identity=identities, repeated_review_equal=True,
                   faults=faults, groups=groups, focus=focus, tests=tests, counts=counts,
                   codegen=codegen, AA=aa, nativeUnity='not_run', production_promoted=False,
                   new_timing_samples=0)
    (delivery / 'Acceptance.json').write_text(json.dumps(summary, indent=2))
    report = '# PR25：复制异常一致性与容量证明——冻结证据验收\n\n'
    report += '本流程只复核两次已完成CI，不产生新的性能样本。commit/capacity两批绝对耗时分别报告，不能拼接。所有快/慢是三轮及同二进制A/A的5%筛选，不是置信区间。\n\n'
    report += '## 故障注入\n' + table(faults, ['phase','architecture','variant','normal','injected','invalid','minimalBad'])
    report += '\n## 分组性能\n' + table(groups, ['phase','architecture','candidate','control','stage','group','cases','fast','slow','stable','fastStable','slowStable'])
    report += '\n## 固定目标（ns/完整操作）\n' + table(focus, ['phase','architecture','candidate','control','operation','members','distribution','baseline_ns','candidate_ns','bytes','fast','slow','stable'])
    report += '\n## 交付边界\nSource_Tested为确切Git快照、不含历史。同名生成候选必须分别编译。原始ZIP、所有负面读数和实际机器码独立保留。故障是数组增长点的模拟OOM，不是真实进程内存耗尽，也不是任意mixedUnion错误路径。无原生产修改、合并或Unity/IL2CPP/Burst/手机验收。本次添加的是独立验收脚本，不改变已测算法。\n'
    (delivery / '结果验收.md').write_text(report)
    for name, value in (('COUNTS',counts),('FAULTS',faults),('GROUPS',groups),('FOCUS',focus),('CODEGEN',codegen)):
        print('COPY_ACCEPTANCE_' + name + ' ' + json.dumps(value), flush=True)
    worst = sorted(negative, key=lambda r:r['ratio'], reverse=True)[:18]
    print('COPY_ACCEPTANCE_WORST ' + json.dumps([{k:r[k] for k in ('phase','architecture','candidate','control','stage','operation','universe','members','distribution','baseline_ns','candidate_ns','ratio','stable')} for r in worst]), flush=True)
    print('COPY_ACCEPTANCE_OK ' + json.dumps(metadata), flush=True)


if __name__ == '__main__':
    main()
