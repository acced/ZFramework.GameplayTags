# Registry regression harness

From the repository root:

```sh
dotnet run --project Tests~/Registry/GameplayTags.Registry.Tests.csproj -c Release
```

This standalone executable compiles the runtime sources into its own assembly so it can
inspect the immutable builder outputs without adding test-only APIs to the library.
It validates public behavior plus the structural invariants behind constant-time hierarchy
checks: dense DFS IDs, ordinal siblings, exact immediate parents, strict None/self
semantics, shared hierarchy paths, and exclusive subtree ends.

Fifty seeded random forests are checked against an independent string-prefix ancestor
oracle for every tag pair. Each forest is rebuilt in reverse declaration order to check
ID determinism. A 3,000-level chain verifies iterative construction and exactly 3,000
pooled hierarchy slots. The harness also checks name-based serialization callbacks,
correct .NET callback signatures, explicit/implicit metadata, and opt-in name validation.
