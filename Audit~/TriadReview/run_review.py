#!/usr/bin/env python3
"""Untimed code-generation review of the exact previously measured triad commit.
Does not change any candidate, benchmark, measurement, or production source.
"""
import argparse
import hashlib
import importlib.util
import io
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
MEASURED = '8b547544ca0dbca1f55e166114b4ebc89208eb34'
TREE = '7cf0c50077a775cf6d4129ad5f45fa0a20c3f6c2'
AUTO = '84729cc3b6e1b4681328806df2d7c8a1d8f568f6'
HARNESS = r'''using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using GameplayTags;
using GameplayTags.Experiments;

internal static class ProbeHarness
{
    // Only the wrapper is NoInlining. The actual measured method is unchanged.
    // Its normal inlining into this wrapper exposes the generated vector body.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ProbeSimd(OrderedSimdBitmapSet set, RuntimeTag tag) => set.HasTagExact(tag);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ProbeRobin(RobinMicroBitmapSet set, RuntimeTag tag) => set.HasTagExact(tag);

    private static int Main()
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<GameplayTagSettings>();
        var definitions = new List<GameplayTagDefinition>();
        for (int i = 0; i < 4096; i++)
            definitions.Add(new GameplayTagDefinition("T" + i.ToString("D4"), "", "Default", false, true));
        settings.ReplaceAll(definitions, new List<GameplayTagRedirect>(),
            new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
        TagRegistry registry = TagRegistry.Create(settings);
        int assertions = 0;
        foreach (int size in new[] { 0, 1, 7, 8, 9, 15, 16, 31, 32, 33 })
        {
            var baseline = new MicroBitmapSet(registry, size, BitmapLayout.Micro);
            var simd = new OrderedSimdBitmapSet(registry, size, BitmapLayout.Micro);
            var robin = new RobinMicroBitmapSet(registry, size, BitmapLayout.Micro);
            for (int i = 0; i < size; i++)
            {
                RuntimeTag tag = registry.GetTagAt(i * 64 + 3);
                baseline.AddTag(tag); simd.AddTag(tag); robin.AddTag(tag);
            }
            for (int i = 0; i < registry.Count; i++)
            {
                RuntimeTag tag = registry.GetTagAt(i);
                bool expected = baseline.HasTagExact(tag);
                if (ProbeSimd(simd, tag) != expected || ProbeRobin(robin, tag) != expected)
                    throw new Exception("Probe mismatch at size=" + size + ", id=" + i);
                assertions += 2;
            }
            if (ProbeSimd(simd, default) || ProbeRobin(robin, default)) throw new Exception("Default handle");
            assertions += 2;
        }
        Console.WriteLine("PROBE_PASS assertions=" + assertions + " failed=0 vector=" +
            Vector.IsHardwareAccelerated + " lanes=" + Vector<uint>.Count);
        return 0;
    }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = pathlib.Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')

    def git(*arguments):
        return subprocess.check_output(['git', *arguments], cwd=ROOT)

    def run(command, name, cwd, extra=None):
        result = subprocess.run(list(map(str, command)), cwd=cwd,
            env=dict(env, **(extra or {})), text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, timeout=240)
        (output / name).write_text(result.stdout, encoding='utf-8')
        if result.returncode:
            print(result.stdout, flush=True)
            raise RuntimeError(name + ' failed: ' + str(result.returncode))
        return result.stdout

    if git('rev-parse', MEASURED + '^{tree}').decode().strip() != TREE:
        raise RuntimeError('Unexpected measured tree')
    for path in ('Runtime', 'Editor', 'Samples~', 'Tests', 'package.json'):
        if git('diff', '--name-only', AUTO, 'HEAD', '--', path).strip():
            raise RuntimeError('Production changed: ' + path)
    with tempfile.TemporaryDirectory(prefix='triad-probe-review-') as temp:
        work = pathlib.Path(temp)
        checkout = work / 'measured'
        checkout.mkdir()
        with zipfile.ZipFile(io.BytesIO(git('archive', '--format=zip', MEASURED))) as archive:
            for name in archive.namelist():
                p = pathlib.PurePosixPath(name)
                if p.is_absolute() or '..' in p.parts:
                    raise RuntimeError('Unsafe archive path')
            archive.extractall(checkout)
        generated = output / 'generated'
        generated.mkdir(exist_ok=True)
        path = checkout / 'Audit~/Triad/run_triad.py'
        spec = importlib.util.spec_from_file_location('measured_triad_generator', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        baseline = checkout / 'Audit~/MicroBitmapSet.cs'
        files = module.generate(baseline.read_text(), generated)
        harness = generated / 'ProbeHarness.cs'
        harness.write_text(HARNESS)
        project = work / 'ProbeReview.csproj'
        module.project(project, [checkout / 'Runtime/**/*.cs', checkout / 'Audit~/UnityStubs.cs',
            baseline, files['simd'], checkout / 'Audit~/Triad/RobinMicroBitmapSet.cs', harness])
        (work / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        (work / 'global.json').write_text(json.dumps({'sdk': {'version': '8.0.425', 'rollForward': 'disable'}}))
        run(['dotnet', '--info'], 'environment.txt', work)
        run(['dotnet', 'build', project, '-c', 'Release', '-o', work / 'out'], 'build.log', work)
        command = ['dotnet', 'exec', work / 'out/ProbeReview.dll']
        native = run(command, 'probe-codegen.log', work, {'COMPlus_JitDisasm': 'ProbeHarness:Probe*'})
        fallback = run(command, 'probe-nohw.log', work, {'DOTNET_EnableHWIntrinsic': '0'})
        if 'PROBE_PASS' not in native or 'PROBE_PASS' not in fallback:
            raise RuntimeError('Missing probe result')
        vector_lines = [line.strip() for line in native.splitlines()
            if re.search(r'\b(vpcmpeq\w*|vpcmp\w*|vpand\w*|vptest\w*)\b', line.lower())]
        result = {
            'measured_commit': MEASURED, 'measured_tree': TREE,
            'review_commit': git('rev-parse', 'HEAD').decode().strip(),
            'production_unchanged': True, 'untimed_diagnostic': True,
            'native_probe_result': next(line for line in native.splitlines() if 'PROBE_PASS' in line),
            'fallback_probe_result': next(line for line in fallback.splitlines() if 'PROBE_PASS' in line),
            'simd_inlined_wrapper_captured': 'ProbeHarness:ProbeSimd' in native,
            'vector_instruction_lines': vector_lines,
            'generated_simd_sha256': hashlib.sha256(files['simd'].read_bytes()).hexdigest(),
            'robin_sha256': hashlib.sha256((checkout / 'Audit~/Triad/RobinMicroBitmapSet.cs').read_bytes()).hexdigest(),
            'new_timings_produced': False, 'native_unity': 'not_run', 'arm64': 'not_run',
            'release_approved': False,
        }
        (output / 'codegen-review.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        print('CODEGEN_REVIEW ' + json.dumps(result), flush=True)


if __name__ == '__main__':
    main()
