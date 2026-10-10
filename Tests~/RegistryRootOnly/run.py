#!/usr/bin/env python3
"""Compile an immutable source snapshot and run registry startup/memory measurements."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, default=Path("/tmp/gameplaytags-dotnet/dotnet"))
    parser.add_argument("--mono", type=Path, help="compile against .NET Framework 4.7.2 references and run with this standalone Mono")
    parser.add_argument("--nuget", type=Path, default=Path("/tmp/gameplaytags-nuget"))
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--variant", default="standalone")
    parser.add_argument("--round", type=int, default=0)
    parser.add_argument("--samples", type=int, default=1)
    parser.add_argument("--quick", action="store_true")
    parser.add_argument("--compile-only", action="store_true")
    parser.add_argument("--revision", help="optional declared full lowercase 40-character commit SHA; omit for uncommitted snapshots")
    args = parser.parse_args()
    if args.revision is not None and not re.fullmatch(r"[0-9a-f]{40}", args.revision):
        parser.error("--revision must be a full lowercase 40-character commit SHA")
    if args.samples < 1 or args.round < 0:
        parser.error("samples must be positive and round nonnegative")
    args.dotnet = args.dotnet.resolve()
    args.mono = args.mono.resolve() if args.mono else None
    args.nuget = args.nuget.resolve()
    args.source = args.source.resolve()
    args.out = args.out.resolve()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    output = args.out.parent / (args.variant + "-registry-build")
    output.mkdir(exist_ok=True)
    sdk = sorted((args.dotnet.parent / "sdk").glob("8.*"))[-1]
    pack = sorted((args.dotnet.parent / "packs/Microsoft.NETCore.App.Ref").glob("8.*"))[-1]
    runtime = sorted((args.dotnet.parent / "shared/Microsoft.NETCore.App").glob("8.*"))[-1]
    sources = sorted((args.source / "Runtime").glob("**/*.cs"))
    sources = [s for s in sources if s.name != "GameObjectGameplayTagContainer.cs"]
    sources.append(Path(__file__).resolve().with_name("Program.cs"))
    harness = Path(__file__).resolve()
    checked = sources + [harness]
    hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in checked}
    assembly = output / ("RegistryBench.exe" if args.mono else "RegistryBench.dll")
    flags = ["-nologo", "-optimize+", "-langversion:9.0", "-nullable:disable", "-deterministic+", "-target:exe", '-out:"' + str(assembly) + '"']
    if args.mono:
        framework = args.nuget / "microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2"
        packages = [args.nuget / path for path in (
            "system.memory/4.5.5/lib/net461/System.Memory.dll",
            "system.buffers/4.5.1/lib/net461/System.Buffers.dll",
            "system.runtime.compilerservices.unsafe/4.5.3/lib/net461/System.Runtime.CompilerServices.Unsafe.dll",
            "system.numerics.vectors/4.5.0/lib/net46/System.Numerics.Vectors.dll")]
        references = [framework / name for name in ("mscorlib.dll", "System.dll", "System.Core.dll", "System.Runtime.Serialization.dll")]
        references += packages
        for package in packages:
            shutil.copy2(package, output / package.name)
    else:
        references = sorted((pack / "ref/net8.0").glob("*.dll"))
    flags += ['-r:"' + str(p) + '"' for p in references]
    flags += ['"' + str(p) + '"' for p in sources]
    response = output / "compile.rsp"
    response.write_text("\n".join(flags))
    compile_command = [str(args.dotnet), str(sdk / "Roslyn/bincore/csc.dll"), "@" + str(response)]
    subprocess.run(compile_command, check=True)
    assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtime.name},
        "configProperties": {"System.Runtime.TieredCompilation": False, "System.GC.Server": False}}}, indent=2))
    command = [str(args.mono or args.dotnet), str(assembly), "--out", str(args.out), "--variant", args.variant,
               "--round", str(args.round), "--samples", str(args.samples)]
    if args.quick:
        command.append("--quick")
    def capture(command):
        result = subprocess.run(command, capture_output=True, text=True)
        return {"Command": command, "ExitCode": result.returncode, "Stdout": result.stdout, "Stderr": result.stderr}

    # A separate metadata-only host process runs before measurements. The fixed
    # benchmark Program.cs remains byte-identical; no probe warms its process.
    probe_source = output / "RuntimeProbe.cs"
    probe_source.write_text("""using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
internal static class RuntimeProbe {
    static void Main() {
        Type[] required = { typeof(object), typeof(System.Linq.Enumerable), typeof(DataContractSerializer), typeof(Span<int>), typeof(System.Buffers.ArrayPool<int>) };
        foreach (Type type in required) Console.WriteLine("required|" + type.Assembly.FullName + "|" + type.Assembly.Location);
        try { Console.WriteLine("host|" + Process.GetCurrentProcess().MainModule.FileName); } catch (Exception error) { Console.WriteLine("host-unavailable|" + error.GetType().FullName); }
        foreach (Assembly item in AppDomain.CurrentDomain.GetAssemblies())
            if (!item.IsDynamic) Console.WriteLine("assembly|" + item.FullName + "|" + item.Location);
    }
}
""")
    probe = output / ("RuntimeProbe.exe" if args.mono else "RuntimeProbe.dll")
    probe_flags = ["-nologo", "-optimize+", "-langversion:9.0", "-deterministic+", "-target:exe", '-out:"' + str(probe) + '"']
    probe_flags += ['-r:"' + str(path) + '"' for path in references] + ['"' + str(probe_source) + '"']
    probe_rsp = output / "probe.rsp"
    probe_rsp.write_text("\n".join(probe_flags))
    probe_compile = capture([str(args.dotnet), str(sdk / "Roslyn/bincore/csc.dll"), "@" + str(probe_rsp)])
    if probe_compile["ExitCode"]:
        raise RuntimeError("Runtime provenance probe compile failed: " + probe_compile["Stdout"] + probe_compile["Stderr"])
    if not args.mono:
        shutil.copy2(assembly.with_suffix(".runtimeconfig.json"), probe.with_suffix(".runtimeconfig.json"))
    probe_run = capture([str(args.mono or args.dotnet), str(probe)])
    if probe_run["ExitCode"]:
        raise RuntimeError("Runtime provenance probe failed: " + probe_run["Stdout"] + probe_run["Stderr"])
    runtime_files = {args.dotnet, args.mono or args.dotnet, sdk / "Roslyn/bincore/csc.dll"}
    for line in probe_run["Stdout"].splitlines():
        if line.startswith(("required|", "assembly|", "host|")):
            path = Path(line.rsplit("|", 1)[-1])
            if path.is_file(): runtime_files.add(path.resolve())
    if not args.mono:
        runtime_files.update(runtime.glob("*.so"))
    runtime_hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(runtime_files)}
    cpuinfo = Path("/proc/cpuinfo")
    cpu_model = next((line.split(":", 1)[1].strip() for line in cpuinfo.read_text().splitlines() if line.startswith("model name")), "unavailable") if cpuinfo.exists() else "unavailable"
    artifacts = sorted(path for path in output.iterdir() if path.is_file() and path.suffix in (".dll", ".exe", ".json", ".rsp", ".cs"))
    manifest = {"SourceHashes": hashes, "AssemblySHA256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
                "ReferenceHashes": {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in references},
                "Command": command, "Source": str(args.source), "Harness": str(Path(__file__).resolve()),
                "Platform": platform.platform(), "Machine": platform.machine(), "RuntimeExecutable": str(args.mono or args.dotnet),
                "Notes": "Fixture strings, oracles and retained-memory samples are outside timed regions. Frozen array payload excludes array headers, names, dictionary and snapshot wrapper. Retained GC deltas include all frozen state but exclude input declarations, the preallocated holder array and the discarded builder."}
    manifest.update({
        "DeclaredRevision": args.revision,
        "RevisionLabelPolicy": "Full lowercase 40-character SHA when supplied; not proof of archive identity. Omit for uncommitted snapshots; source hashes are authoritative.",
        "CompileCommand": compile_command, "SDKDirectory": str(sdk), "SDKVersion": sdk.name,
        "ReferencePackDirectory": str(pack), "SelectedDotnetRuntimeDirectory": str(runtime),
        "DotnetInfo": capture([str(args.dotnet), "--info"]),
        "HostVersion": capture([str(args.mono or args.dotnet), "--version"]),
        "RuntimeProbeCompile": probe_compile, "RuntimeProbe": probe_run, "RuntimeBinaryHashes": runtime_hashes,
        "ArtifactHashes": {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in artifacts},
        "CPUModel": cpu_model, "Processor": platform.processor(), "Python": platform.python_version(),
        "Affinity": sorted(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else None,
        "Settings": {"TieredCompilation": False, "ServerGC": False},
        "Mode": "smoke-only" if args.quick else "full", "Quick": args.quick,
        "MeasurementPolicy": "Quick or fewer than 14 paired rounds never constitute timing acceptance. Separate metadata probe runs before all timed processes."
    })
    top = subprocess.run(["git", "-C", str(args.source), "rev-parse", "--show-toplevel"], capture_output=True, text=True)
    if top.returncode == 0 and Path(top.stdout.strip()).resolve() == args.source:
        manifest["GitHead"] = subprocess.check_output(["git", "-C", str(args.source), "rev-parse", "HEAD"], text=True).strip()
        manifest["GitStatus"] = subprocess.check_output(["git", "-C", str(args.source), "status", "--porcelain"], text=True)
        diff = subprocess.check_output(["git", "-C", str(args.source), "diff", "HEAD", "--", "Runtime"])
        manifest["RuntimeDiffSHA256"] = hashlib.sha256(diff).hexdigest()
    else:
        manifest["GitHead"] = "archive or detached source; SourceHashes are authoritative"
    args.out.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2))
    print(" ".join(command), flush=True)
    if not args.compile_only:
        env = dict(os.environ, DOTNET_TieredCompilation="0", DOTNET_gcServer="0", COMPlus_TieredCompilation="0", COMPlus_gcServer="0")
        subprocess.run(command, check=True, env=env)
        if hashes != {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in checked}:
            raise RuntimeError("Source files changed during sampling")


if __name__ == "__main__":
    main()
