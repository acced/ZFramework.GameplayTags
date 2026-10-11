# Reusable Union output (candidate API)

This overload replaces an existing ordinary set container with the explicit union
of two inputs:

```csharp
var output = new GameplayTagContainer();
GameplayTagContainer.Union(output, left, right);
```

The original `Union(left, right)` factory remains available and unchanged.
`output` must be non-null. Inputs follow the existing container contract: tags
belong to the current initialized registry, mutation is not concurrent, and returned
storage remains valid for the duration of the operation. Each input storage getter
is evaluated once. Counted inputs
contribute membership, not their multiplicities.

For a distinct output, the implementation copies the same preferred seed as the
factory and appends the other input into reusable storage. Capacity may grow when
needed; first use, growth and pooled scratch are not claimed to be allocation-free.
The output is a complete, independently mutable container with normal ancestor
reference counts. Its serialized explicit-tag cache is invalidated as usual.

When output storage aliases either input, including a storage proxy, this version
deliberately constructs a temporary normal result from the captured inputs using
the same seed algorithm as the factory, then performs Copy. This preserves the
factory's normalization and sparse-index behavior without reading source getters
a second time. Aliased calls therefore
retain factory-result allocations; there is no alias speed or zero-allocation
claim.

Like other mutating operations, this overload does not promise strong exception
atomicity. A failure after output mutation begins may leave partially replaced
output. The operands are captured once before replacement, in left-to-right getter order.
Concurrent mutation of returned storage is unsupported.

## Evidence boundary

This API is under evaluation on the draft optimization branch. It is not an
accepted optimization version merely because its correctness or CI job passes.
The unchanged Evolution, legacy, registry and Copy guards remain required.
`Tests~/UnionOutput` compares observable output replacement through this overload
against `Copy(output, Union(left, right))`; that baseline includes both operations
and must not be reported as isolated factory timing. Its cold-output rows have
warmed code/pools. `Tests~/UnionOutputCold` separately measures true first Apply
calls in fresh processes, with alias and distinct-output capacity/pool checks.
Standalone .NET/Mono evidence does not establish Unity Editor/Player or IL2CPP
behavior.
