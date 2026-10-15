#!/usr/bin/env python3
"""Run the public-API bulk-operation regressions without MSBuild or NuGet.

The selected checkout supplies Runtime only; the tests always come from this
script's checkout. This permits the same test harness to verify both revisions.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys


def version_key(path):
    return tuple(int(part) for part in path.name.split("-")[0].split(".") if part.isdigit())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"), help="installed .NET host")
    parser.add_argument("--source", type=Path, help="runtime checkout; defaults to this repository")
    parser.add_argument("--output-dir", type=Path, help="defaults to artifacts/bulk-tests")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    source = (args.source or repository).resolve()
    output = (args.output_dir or repository / "artifacts/bulk-tests").resolve()
    if not args.dotnet:
        parser.error(".NET SDK 8 is required; pass --dotnet /path/to/dotnet")
    dotnet = Path(args.dotnet).resolve()
    sdk_root = dotnet.parent
    sdks = sorted((sdk_root / "sdk").glob("*"), key=version_key)
    packs = sorted((sdk_root / "packs/Microsoft.NETCore.App.Ref").glob("8.*"), key=version_key)
    runtimes = sorted((sdk_root / "shared/Microsoft.NETCore.App").glob("8.*"), key=version_key)
    if not sdks or not packs or not runtimes:
        parser.error("installed .NET SDK, .NET 8 runtime and .NET 8 reference pack are required")

    # Build a separate portable assembly with the existing, unchanged runner.
    # The test assembly cannot accidentally use Runtime internals as its oracle.
    print("Bulk regression runtime source: " + str(source), flush=True)
    subprocess.run([sys.executable, str(repository / "Tests~/run.py"), "build",
                    "--dotnet", str(dotnet), "--source", str(source),
                    "--output-dir", str(output)], check=True)
    runtime_assembly = output / "GameplayTags.Runtime/GameplayTags.Runtime.dll"
    test_directory = output / "GameplayTags.Bulk.Tests"
    test_directory.mkdir(parents=True, exist_ok=True)
    assembly = test_directory / "GameplayTags.Bulk.Tests.dll"
    references = sorted((packs[-1] / "ref/net8.0").glob("*.dll"))
    references.append(runtime_assembly)
    flags = ["-nologo", "-optimize+", "-langversion:9.0", "-nullable:disable", "-deterministic+",
             "-target:exe", '-out:"' + str(assembly) + '"']
    flags += ['-r:"' + str(path) + '"' for path in references]
    flags += ['"' + str(repository / "Tests~/Bulk/Program.cs") + '"']
    response = test_directory / "compile.rsp"
    response.write_text("\n".join(flags), encoding="utf-8")
    subprocess.run([str(dotnet), str(sdks[-1] / "Roslyn/bincore/csc.dll"),
                    "@" + str(response)], check=True)
    shutil.copy2(runtime_assembly, test_directory / runtime_assembly.name)
    options = {"tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtimes[-1].name}}
    assembly.with_suffix(".runtimeconfig.json").write_text(
        json.dumps({"runtimeOptions": options}, indent=2), encoding="utf-8")
    print("Compiled GameplayTags.Bulk.Tests [net8.0; public Runtime reference]", flush=True)
    subprocess.run([str(dotnet), str(assembly)], check=True)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode if error.returncode > 0 else 1)
