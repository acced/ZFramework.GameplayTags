# Standalone validation

The tests are console applications with no test-framework package dependency.
They exercise the actual runtime C# sources, not a translation of the algorithms.
The main suite references the portable `netstandard2.1` runtime project and runs
on .NET 8. Test failures return a nonzero process exit code.

```sh
dotnet build GameplayTags.Runtime.csproj -c Release
dotnet run --project Tests~/GameplayTags.Tests.csproj -c Release
dotnet run --project Tests~/Registry/GameplayTags.Registry.Tests.csproj -c Release
dotnet run --project Tests~/DeepHierarchy/GameplayTags.DeepHierarchy.Tests.csproj -c Release
dotnet run --project SourceGenerator~/Tests/GameplayTags.SourceGenerator.Tests.csproj -c Release
```

The source-generator project uses its pinned Roslyn 3.8 package dependencies;
its first normal build needs NuGet access. The runtime, main tests, registry tests
and deep-hierarchy tests have no external package dependencies.

## What is validated

The main suite registers fixtures through assembly attributes before first use.
Its independent reference model uses ordinal string prefixes for ancestry,
`HashSet<string>` for explicit membership, and `Dictionary<string, int>` for
reference counts. Expected results never depend on runtime IDs, storage layout,
enumeration order, or the runtime's ancestor methods.

Coverage includes:

- Strict ancestor relationships, case-sensitive identity, labels and parent/child lists.
- Shared-parent contributions, duplicate insertion, missing removal, promotion
  from tiny storage, hash collisions, dense swap deletion, and reuse after clear.
- Copying empty/self/count containers; union; intersection followed by closure;
  output aliasing; bulk self-add/remove; explicit diffs.
- Exact/hierarchical any/all predicates, unrelated tags between a parent and a
  child, default requirements, and requirements covered across two holders.
- 12,000 randomized set steps, 7,000 randomized count steps and 1,400 hierarchy
  steps, with fixed seeds and full model comparisons after each operation.
- Count transitions, subscription removal, bulk commit before callbacks,
  reentrant queued notifications, callback exceptions and subsequent recovery.
- Reparenting repeated contributions, preserving a parent's own counts, and
  completed transfers visible to callbacks throughout a hierarchy chain.
- Bind/unbind and pool leases, name-based serialization, duplicate/missing name
  normalization, and a real `DataContractSerializer` round trip.

The separate registry executable validates random forests and deterministic IDs
under declaration reordering. The deep-hierarchy executable exercises containers
with one explicit leaf and 3,000 implied/self tags, including enumeration,
queries, cloning, aliases and count propagation. Source-generator tests cover
semantic attributes, aliases, constants, named overrides, deterministic output,
identifier collisions, and diagnostics.

## Direct compiler fallback

Some restricted execution environments have an installed SDK but cannot supply
the process metadata required by the `dotnet` CLI or MSBuild. `run.py` invokes
the SDK's Roslyn compiler directly against its installed reference packs. It
does not download packages or change platform access controls.

```sh
python3 Tests~/run.py all --dotnet /path/to/dotnet --unity-check
```

Individual modes are `build`, `test`, `deep`, `registry`, `generator`, and
`benchmark`. Add `--output-dir /path/to/output` to move build intermediates out of
the default `artifacts/direct` directory. This fallback requires an installed
.NET 8 runtime, its reference pack, the .NET Standard 2.1 reference pack, and an
SDK containing Roslyn. The fallback's generator smoke test uses that SDK's
Roslyn assemblies; the normal source-generator project verifies the pinned
Roslyn 3.8 package API separately.

`--unity-check` compiles the Unity-conditional adapter and serialization
interfaces with the small `UnityStubs.cs` compile shim. This catches C# API and
conditional-compilation errors. Unity Editor behavior, component lifecycle,
Unity serialization, Mono and IL2CPP must be verified in an actual Unity project.
No benchmark result here establishes performance on those backends.

Invalid handles, mutation while enumerating, cyclic hierarchy assignment,
duplicate pool release and concurrent mutation are caller-precondition
violations; this suite does not make them part of the supported hot-path API.
