# Gameplay tag source generator

The generator reads semantic assembly attributes. It supports attribute aliases, fully
qualified names, constant expressions, named constructor arguments, and `TagName = ...`
property overrides. Unrelated attributes with the same short name are ignored. Invalid
tag names produce the `GT001` compile-time diagnostic; runtime registration trusts input.

Each compilation generates accessors only for its own assembly declarations. The
generated `AllGameplayTags` types are internal to that assembly. Code in another
assembly should resolve the tag through `GameplayTagManager.RequestTag` or use a
public wrapper owned by the declaring assembly; it cannot directly reference another
assembly's internal generated types.

```csharp
[assembly: GameplayTags.GameplayTag("Character.Invincible")]

var invincible = GameplayTags.AllGameplayTags.Character.Invincible.Get();
```

Generated handles resolve by name once per generated type and then return a cached
`GameplayTag`. Numeric IDs cannot be embedded safely: they belong to the complete runtime
registry, which can include declarations from other assemblies or an explicit startup set.
Call `GameplayTagManager.Initialize(...)` before first accessing a generated handle if
using explicit initialization.

Each cached tag type has an explicit static constructor. This prevents the CLR's
`beforefieldinit` optimization from resolving its name before a preceding explicit
registry initialization in the same caller method.

Tags are emitted in ordinal sibling order. Ordinary identifiers keep their spelling;
C# keywords use `@`. Labels that start with a digit, collide with `Get`/`s_Tag`, or start
with the reserved `__gt_` prefix use `__gt_` followed by four lowercase hexadecimal digits
per UTF-16 character. For example, tag `Get` becomes `__gt_004700650074`. A label equal to
its enclosing generated class uses the encoded spelling plus `__child`. This mapping is
deterministic and avoids collisions without changing the runtime tag name. `Tag` is a
normal label and no longer truncates a generated path.

Build with `dotnet build SourceGenerator~/GameplayTags.SourceGenerator.csproj`. In a .NET
consumer add a project reference with `OutputItemType="Analyzer"` and
`ReferenceOutputAssembly="false"`. Unity excludes folders ending in `~`: compile the
generator, copy only `GameplayTags.SourceGenerator.dll` into the Unity project, and mark
it with the `RoslynAnalyzer` asset label. In that DLL's Plugin Inspector, clear
`Any Platform`, `Editor`, and `Standalone` so Unity treats it as a compiler analyzer
instead of a runtime plugin. The generator targets Roslyn 3.8 / C# 9 and
netstandard2.0; the runtime targets netstandard2.1.

The generator target and Unity installation steps follow the [Unity 2022.3 source
generator documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html).
The version-pinned semantic and generated-compilation regression harness is documented
in [Tests/README.md](Tests/README.md).
