#!/usr/bin/env python3
"""Offline Roslyn runner for environments where the dotnet CLI/MSBuild is unavailable.

Normal development can use the supplied .csproj files and dotnet run/build.
This runner uses the same C# sources and installed SDK reference assemblies.
It does not download packages, patch the runtime, or require Unity.
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
    parser.add_argument("mode", choices=("build", "test", "deep", "registry", "generator", "benchmark", "all"))
    parser.add_argument("--dotnet", default=shutil.which("dotnet"), help="path to the installed .NET host")
    parser.add_argument("--source", type=Path, help="runtime checkout; defaults to this repository")
    parser.add_argument("--output-dir", type=Path, help="build output; defaults to artifacts/direct")
    parser.add_argument("--baseline", action="store_true", help="include original repository's missing compile dependency shims")
    parser.add_argument("--quick", action="store_true", help="short benchmark smoke run")
    parser.add_argument("--json", type=Path, help="benchmark JSON result path")
    parser.add_argument("--unity-check", action="store_true", help="also compile Unity-conditional code against compile-only stubs")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parent.parent
    source = (args.source or repository).resolve()
    output = (args.output_dir or repository / "artifacts/direct").resolve()
    output.mkdir(parents=True, exist_ok=True)
    if not args.dotnet:
        parser.error(".NET SDK 8 is required; pass --dotnet /path/to/dotnet or use dotnet run with the projects")
    dotnet = Path(args.dotnet).resolve()
    sdk_root = dotnet.parent
    sdk_versions = sorted((sdk_root / "sdk").glob("*"), key=version_key)
    if not sdk_versions:
        parser.error("SDK not found next to the dotnet host")
    sdk = sdk_versions[-1]
    csc = sdk / "Roslyn/bincore/csc.dll"
    roslyn = csc.parent
    net8_packs = sorted((sdk_root / "packs/Microsoft.NETCore.App.Ref").glob("8.*"), key=version_key)
    runtimes = sorted((sdk_root / "shared/Microsoft.NETCore.App").glob("8.*"), key=version_key)
    if not net8_packs or not runtimes:
        parser.error("installed .NET 8 runtime and Microsoft.NETCore.App.Ref 8.x pack are required")
    net8_references = net8_packs[-1] / "ref/net8.0"
    standard_references = sdk_root / "packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1"
    core_sources = sorted((source / "Runtime").glob("**/*.cs"))
    core_sources = [path for path in core_sources if path.name != "GameObjectGameplayTagContainer.cs"]
    if args.baseline:
        core_sources.append(repository / "Benchmarks~/BaselineDependencies.cs")

    def compile_assembly(name, sources, *, portable=False, executable=False, references=(), defines=(), benchmark=False):
        directory = output / name
        directory.mkdir(parents=True, exist_ok=True)
        assembly = directory / (name + ".dll")
        reference_dir = standard_references if portable else net8_references
        all_references = list(sorted(reference_dir.glob("*.dll"))) + list(references)
        if not all_references:
            raise RuntimeError("Missing reference assemblies: " + str(reference_dir))
        flags = ["-nologo", "-optimize+", "-langversion:9.0", "-nullable:disable", "-deterministic+",
                 "-target:exe" if executable else "-target:library", '-out:"' + str(assembly) + '"']
        flags += ["-define:" + symbol for symbol in defines]
        if args.baseline:
            flags.append("-define:BASELINE")
        flags += ['-r:"' + str(path) + '"' for path in all_references]
        flags += ['"' + str(path.resolve()) + '"' for path in sources]
        response = directory / "compile.rsp"
        response.write_text("\n".join(flags), encoding="utf-8")
        subprocess.run([str(dotnet), str(csc), "@" + str(response)], check=True)
        for reference in references:
            shutil.copy2(reference, directory / Path(reference).name)
        if executable:
            options = {"tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtimes[-1].name}}
            if benchmark:
                options["configProperties"] = {"System.Runtime.TieredCompilation": False, "System.GC.Server": False}
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": options}, indent=2), encoding="utf-8")
        print("Compiled " + name + (" [netstandard2.1]" if portable else " [net8.0]"), flush=True)
        return assembly

    def run(assembly, *arguments):
        subprocess.run([str(dotnet), str(assembly), *map(str, arguments)], check=True)

    mode = args.mode
    if mode in ("build", "test", "deep", "all"):
        core = compile_assembly("GameplayTags.Runtime", core_sources, portable=True)
        if mode in ("test", "all"):
            tests = compile_assembly("GameplayTags.Tests", [repository / "Tests~/Program.cs", repository / "Tests~/FixtureRegistration.cs"],
                                     references=[core], executable=True)
            run(tests)
        if mode in ("deep", "all"):
            deep = compile_assembly("GameplayTags.DeepHierarchy.Tests", [repository / "Tests~/DeepHierarchy/Program.cs"],
                                    references=[core], executable=True)
            run(deep)
    if args.unity_check:
        unity_sources = core_sources + [source / "Runtime/GameObjectGameplayTagContainer.cs", repository / "Tests~/UnityStubs.cs"]
        compile_assembly("GameplayTags.UnityCompileCheck", unity_sources, portable=True, defines=["UNITY_5_3_OR_NEWER"])
    if mode in ("registry", "all"):
        registry = compile_assembly("GameplayTags.Registry.Tests", core_sources + [repository / "Tests~/Registry/Program.cs"], executable=True)
        run(registry)
    if mode in ("generator", "all"):
        generator = compile_assembly("GameplayTags.SourceGenerator.Tests",
            [repository / "SourceGenerator~/GameplayTagsSourceGenerator.cs", repository / "SourceGenerator~/Tests/GeneratorCheck.cs"],
            references=[roslyn / "Microsoft.CodeAnalysis.dll", roslyn / "Microsoft.CodeAnalysis.CSharp.dll"], executable=True)
        run(generator)
    if mode == "benchmark":
        benchmark = compile_assembly("GameplayTags.Benchmarks", core_sources +
            [repository / "Tests~/FixtureRegistration.cs", repository / "Benchmarks~/Program.cs"], executable=True, benchmark=True)
        arguments = []
        if args.quick: arguments.append("--quick")
        if args.json:
            result_path = args.json.resolve()
            result_path.parent.mkdir(parents=True, exist_ok=True)
            arguments += ["--out", result_path]
        run(benchmark, *arguments)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode if error.returncode > 0 else 1)
