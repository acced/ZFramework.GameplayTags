# DirectArraySet runtime integration

The package's `GameplayTags` Runtime assembly now also contains the selected PR26
exact-membership set under its existing public namespace,
`GameplayTags.Experiments`. The name is preserved so callers of the admitted kernel
do not need a namespace migration. This is published experimental PR26-based source with managed verification.
Native Unity Editor and device gates remain outstanding; see the CI package README.

Reference `GameplayTags` and import `GameplayTags.Experiments`. No generated Audit
source, experimental compilation symbol, or Unity API stub belongs in a consuming
Unity project. Unity 2021.3 / C# 9 / .NET Standard 2.1 remain the package targets.
The existing assembly definition keeps `allowUnsafeCode: false`.

```csharp
using GameplayTags;
using GameplayTags.Experiments;

public static class ExactTagPreparation
{
    public static DirectArraySet Prepare(TagRegistry registry, RuntimeTag[] input)
    {
        return DirectArraySet.FromUnordered(registry, input, storage: BitmapLayout.Micro);
    }

    public static RuntimeTagSet PrepareHierarchy(DirectArraySet source)
    {
        var result = new RuntimeTagSet(source.Registry, source.Count);
        foreach (RuntimeTag tag in source) result.AddTag(tag);
        return result;
    }
}
```

`FromUnordered` validates ownership, deduplicates and owns its storage. It does not
change or retain the input array. `FromSortedUnique` requires strictly increasing,
unique registry handles. `ResetFromSortedUnique` validates the full input before
mutating the target. `CopyAsStorage` explicitly copies into Micro or Dense storage;
Auto is only a construction policy. Layout never changes automatically.

The fixed Micro16 format stores sixteen membership bits per address record.
DirectArraySet rejects registries containing more than 2^20 IDs, including ancestor
IDs, for every layout. The existing TagRegistry and RuntimeTagSet limits are
unchanged. The unselected Micro32, linear-scan and exact-allocation experimental
branches are absent from production source.

The public surface includes exact membership, add/remove, count, capacity
reservation, ordered enumeration, copy, union, append, removal and the builders.
The separate query bridge adds `set.HasTag(handle)` for membership in a tag's
subtree and `set.Matches(frozenQuery)` for all six frozen expression types.
Both read the set's active representation directly, without allocation, a
delegate, a temporary RuntimeTagSet or a second membership representation.
`HasTag` returns false for a default handle; `Matches` returns false for a null
query. Both reject another registry's identity, including an empty foreign query.

RuntimeTagSet's public APIs, normal-build behavior and storage layout are unchanged.
The checked-build review exposed three inherited signed-to-unsigned bounds guards:
one in TagRegistry.GetTagAt and two in RuntimeTagSet's internal enumerator. Those
casts now explicitly use `unchecked`, preserving the intended unsigned bounds test
when a negative index or exhausted reverse cursor is examined. This restores the
normal ArgumentOutOfRangeException/end-of-iteration behavior in checked builds.
Registry limits are unchanged. The earlier frozen source and its failed checked
results are retained separately for attribution.

The remaining existing-source edit adds `partial` to FrozenGameplayTagQuery's
class declaration. The separate evaluator repeats the current evaluation semantics with
DirectArraySet as its input type. No public frozen-query overload is added, so
existing `query.Matches(null)` calls remain unambiguous. The bridge has separate
correctness/allocation evidence; exact-set kernel timing does not measure it.

DirectArraySet has no filter or intersection API. Public enumeration still supports
an explicit, independently owned transfer when an existing API requires a
RuntimeTagSet, as shown above. Such a transfer takes member-by-member work and
storage; this integration does not claim zero-copy conversion or a faster
RuntimeTagSet implementation.

The portable dense helper uses scalar arithmetic without unsafe code. A separate
.NET 8 build enables guarded AVX2/NEON paths and BitOperations, with unsafe enabled
only for that target's project. Those desktop paths do not establish native Unity,
Mono, IL2CPP, Android or iOS performance. Managed evidence must identify the
targeted GameplayTags DLL and the copy actually loaded by each separate test host.
Kernel timings from an executable embedding source are not substituted for
cross-assembly timings of this library.

Native `DirectArraySetPlayerTests` accompany the original runtime tests. Managed
tests and compile-only Unity facades do not certify native Player execution. Native
Editor/device acceptance and release approval remain outstanding.
