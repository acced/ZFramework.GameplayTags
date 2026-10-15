# Generator regression harness

From the repository root:

```sh
dotnet run --project SourceGenerator~/Tests/GameplayTags.SourceGenerator.Tests.csproj -c Release
```

This executable runs Roslyn 3.8 against in-memory consumer compilations and verifies:

- Semantic attribute identity, including aliases and fully qualified names.
- Constant expressions, named constructor arguments, and `TagName` property overrides.
- Rejection of unrelated same-short-name attributes.
- Compilable generated output for keywords, digits, `Get`, `s_Tag`, repeated enclosing
  class names, reserved escape prefixes, Unicode labels, and ordinary `Tag` labels.
- Byte-identical generated output after declaration-order reversal.
- The `GT001` diagnostic for invalid tag names.
- Emitted assembly metadata omits `BeforeFieldInit` on cached tag types; an explicit
  registry initialization followed by `Get()` in the same method runs in that order.

The generator project and this harness both pin `Microsoft.CodeAnalysis.CSharp` to
3.8.0. The harness targets .NET 8 only to run tests; the generator targets netstandard2.0
and emits C# 9-compatible code. The repository's direct Roslyn runner can additionally
exercise the same harness using the installed SDK's compiler libraries when NuGet or
MSBuild is unavailable; that is a separate compiler-version smoke check.
