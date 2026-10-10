#!/usr/bin/env python3
"""Compile an immutable snapshot and audit the 24 frozen retention fixture graphs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--revision", required=True, help="declared exact source revision; source hashes remain authoritative")
    parser.add_argument("--dotnet", type=Path, default=Path("/tmp/gameplaytags-dotnet/dotnet"))
    parser.add_argument("--mono", type=Path, help="compile against .NET Framework 4.7.2 references and run with this standalone Mono")
    parser.add_argument("--nuget", type=Path, default=Path("/tmp/gameplaytags-nuget"))
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--variant", default="standalone")
    parser.add_argument("--compile-only", action="store_true")
    args = parser.parse_args()
    args.dotnet = args.dotnet.resolve()
    args.mono = args.mono.resolve() if args.mono else None
    args.nuget = args.nuget.resolve()
    args.source = args.source.resolve()
    args.out = args.out.resolve()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    output = args.out.parent / (args.variant + "-retention-layout-build")
    output.mkdir(exist_ok=True)
    sdk = sorted((args.dotnet.parent / "sdk").glob("8.*"))[-1]
    pack = sorted((args.dotnet.parent / "packs/Microsoft.NETCore.App.Ref").glob("8.*"))[-1]
    runtime = sorted((args.dotnet.parent / "shared/Microsoft.NETCore.App").glob("8.*"))[-1]
    sources = sorted((args.source / "Runtime").glob("**/*.cs"))
    sources = [s for s in sources if s.name != "GameObjectGameplayTagContainer.cs"]
    sources.append(Path(__file__).resolve().with_name("Program.cs"))
    sources.append(Path(__file__).resolve().parent.parent / "Program.cs")
    harness = Path(__file__).resolve()
    checked = sources + [harness]
    hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in checked}
    assembly = output / ("RetentionLayoutAudit.exe" if args.mono else "RetentionLayoutAudit.dll")
    flags = ["-nologo", "-optimize+", "-langversion:9.0", "-nullable:disable", "-deterministic+", "-target:exe", "-main:RetentionLayout", '-out:"' + str(assembly) + '"']
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
    subprocess.run([str(args.dotnet), str(sdk / "Roslyn/bincore/csc.dll"), "@" + str(response)], check=True)
    assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtime.name},
        "configProperties": {"System.Runtime.TieredCompilation": False, "System.GC.Server": False}}}, indent=2))
    command = [str(args.mono or args.dotnet), str(assembly), "--out", str(args.out)]
    manifest = {"DeclaredRevision": args.revision, "SourceHashesAuthoritative": True,
                "DotnetInfo": subprocess.check_output([str(args.dotnet), "--info"], text=True),
                "RuntimeVersion": subprocess.check_output([str(args.mono or args.dotnet), "--version"], text=True),
                "SourceHashes": hashes, "AssemblySHA256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
                "ReferenceHashes": {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in references},
                "Command": command, "Source": str(args.source), "Harness": str(Path(__file__).resolve()),
                "Platform": platform.platform(), "Machine": platform.machine(), "RuntimeExecutable": str(args.mono or args.dotnet),
                "Notes": "Structural retention audit, exact 24 frozen root fixtures. No timing or byte-size claims. Instance fields only; shared references deduplicated; static registry excluded."}
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
