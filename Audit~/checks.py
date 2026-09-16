"""Build/packaging checks for the audit host, not a Unity implementation."""
import hashlib
import json
import pathlib
import re
import subprocess
from xml.sax.saxutils import escape


def source_digest(root):
    root = pathlib.Path(root)
    paths = [p for folder in ("Runtime", "Editor", "Samples~", "Tests")
             for p in (root / folder).rglob("*") if p.is_file()]
    paths += [root / "package.json"]
    digest = hashlib.sha256()
    for path in sorted(paths):
        name = path.relative_to(root).as_posix().encode()
        data = path.read_bytes()
        digest.update(len(name).to_bytes(4, "big") + name)
        digest.update(len(data).to_bytes(8, "big") + data)
    return digest.hexdigest()


def verify_package(root):
    root = pathlib.Path(root)
    package = json.loads((root / "package.json").read_text(encoding="utf-8"))
    if package["name"] != "zframework.gameplaytag":
        raise RuntimeError("Unexpected package identity")
    if not (root / "LICENSE").is_file():
        raise RuntimeError("Missing license")
    guids = {}
    for path in root.rglob("*.meta"):
        if ".git" in path.parts:
            continue
        match = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", path.read_text(), re.M)
        if not match or match[1] in guids:
            raise RuntimeError("Missing or duplicate Unity GUID: " + str(path))
        guids[match[1]] = str(path)
    for folder in ("Runtime", "Editor", "Tests", "Samples~"):
        for path in (root / folder).rglob("*"):
            if path.suffix in (".cs", ".asmdef") and not pathlib.Path(str(path) + ".meta").is_file():
                raise RuntimeError("Missing Unity metadata: " + str(path))
    assemblies = {}
    for path in root.rglob("*.asmdef"):
        definition = json.loads(path.read_text())
        name = definition["name"]
        if name in assemblies:
            raise RuntimeError("Duplicate assembly: " + name)
        assemblies[name] = definition
    for path in [root/"README.md", root/"README_CN.md"]:
        text = path.read_text(encoding="utf-8")
        for link in re.findall(r"\]\(([^)]+)\)", text):
            if link.startswith(("#", "https:", "http:")):
                continue
            if not (path.parent / link.split("#")[0]).exists():
                raise RuntimeError("Broken local documentation link: " + link)
        if "canAct.Matches(" in text or "`query.Matches(owned)`" in text:
            raise RuntimeError("Stale Query API example: " + path.name)
    return {"name": package["name"], "version": package["version"], "metadata_guids": len(guids),
            "assemblies": sorted(assemblies), "source_digest": source_digest(root)}


def compile_documentation(root, host, dll, dotnet, run):
    """Compile exact complete class blocks, independently so translated examples may share names."""
    root, host = pathlib.Path(root), pathlib.Path(host)
    results = []
    for document in ("README.md", "README_CN.md"):
        text = (root/document).read_text(encoding="utf-8")
        count = 0
        for index, block in enumerate(re.findall(r"```csharp\s*\n(.*?)```", text, re.S)):
            if not re.search(r"\bpublic\s+(?:sealed\s+)?class\s+\w+", block):
                continue
            stem = f"Docs_{document.replace('.', '_')}_{index}"
            source, project = host/(stem+".cs"), host/(stem+".csproj")
            source.write_text(block, encoding="utf-8")
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion>'
                '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                '</PropertyGroup><ItemGroup><Compile Include="'+escape(str(source))+'"/>'
                '<Reference Include="Audit"><HintPath>'+escape(str(dll))+'</HintPath></Reference>'
                '</ItemGroup></Project>', encoding="utf-8")
            run([dotnet, "build", project, "-c", "Release", "-o", host/("out-"+stem),
                 "-p:BaseIntermediateOutputPath=obj-"+stem+"/"], stem+".log", host)
            results.append({"document": document, "block": index, "compiled": True})
            count += 1
        if count < 2:
            raise RuntimeError("Missing complete examples in " + document)
    return results


def compile_split_assemblies(root, host, dotnet, run):
    """Checks actual assembly boundaries/friend names using an explicitly labeled API facade."""
    root, host = pathlib.Path(root), pathlib.Path(host)
    definitions = [
        ("UnityFacade", [host/"UnityStubs.cs"], []),
        ("GameplayTags", [root/"Runtime/**/*.cs"], ["UnityFacade"]),
        ("GameplayTags.Editor", [root/"Editor/**/*.cs"], ["UnityFacade", "GameplayTags"]),
        ("GameplayTags.BasicUsage", [root/"Samples~/BasicUsage/**/*.cs"], ["UnityFacade", "GameplayTags"]),
        ("NativeTestFacade", [host/"NativeTestStubs.cs"], ["UnityFacade"]),
        ("GameplayTags.Tests", [root/"Tests/Editor/**/*.cs"], ["UnityFacade", "NativeTestFacade", "GameplayTags", "GameplayTags.Editor"]),
        ("GameplayTags.Runtime.Tests", [root/"Tests/Runtime/**/*.cs"], ["UnityFacade", "NativeTestFacade", "GameplayTags"]),
    ]
    dlls, results = {}, []
    for name, includes, references in definitions:
        project = host/(name+".csproj")
        items = ''.join('<Compile Include="'+escape(str(path))+'"/>' for path in includes)
        items += ''.join('<Reference Include="'+ref+'"><HintPath>'+escape(str(dlls[ref]))+
                         '</HintPath></Reference>' for ref in references)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<TargetFramework>netstandard2.1</TargetFramework><LangVersion>9.0</LangVersion>'
            '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
            '<DefineConstants>UNITY_EDITOR</DefineConstants>'
            '<AssemblyName>'+name+'</AssemblyName></PropertyGroup><ItemGroup>'+items+
            '</ItemGroup></Project>', encoding="utf-8")
        output = host/("split-"+name)
        run([dotnet,"build",project,"-c","Release","-o",output,
             "-p:BaseIntermediateOutputPath=obj-split-"+name+"/"],"split-"+name+".log",host)
        dlls[name] = output/(name+".dll")
        results.append({"assembly": name, "compiled": True,
                        "limitation": "API facade, not native Unity"})
    return results
