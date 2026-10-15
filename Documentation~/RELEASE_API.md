# Release API and migration contract

This release retains `GameplayTag` name identity, the `m_Name` and
`m_GameplayTags` serialized fields, and Ordinal-sorted explicit members. There
are no runtime integer handles, ancestor-member expansion, caches, new indexing
backends or schema migrations. Existing assets need no bulk rewrite.

## Reusable operations

```csharp
var result = new GameplayTagContainer(64);
result.CopyFrom(source);                       // replaces, validates incoming names
result.SetUnion(left, right);                  // replaces; target may alias either input
result.SetIntersectionExact(left, right);      // replaces; explicit names only
result.IntersectWithExact(mask);               // in-place explicit intersection
result.ExceptWithExact(remove);                // in-place exact difference
result.EnsureCapacity(128);                    // reserve only; does not add members
result.Clear();                               // removes members, retains capacity
```

| API | Null / self | Registration and representative behavior |
|---|---|---|
| `AddTag(GameplayTag)` | Invalid/unregistered input is rejected | Validates the name; duplicate membership is unchanged |
| `AddTag(string)` | Failed resolution returns false | Request path applies normalization/redirects |
| `CopyFrom` | Null clears; self is a no-op | Validates incoming names; copies source representatives |
| `AppendTags` | Null/self is a no-op | Validates incoming names, preserves existing unknown members; target wins equal names |
| `SetUnion` / `Union` | Null operands are empty; aliases supported | Revalidates names, including self-union; left input wins equal names |
| `SetIntersectionExact` / `IntersectionExact` | Either null gives empty; aliases supported | Preserves matching unknown names; left input wins equal names |
| `ExceptWithExact` / `RemoveTags` | Null removal is a no-op; self clears | Preserves surviving unknown names and their representatives |
| `HasTagExact` | Name lookup only | No redirect or implicit-parent membership |
| `HasTag` | Empty query is false | A child can match its ancestor; direction is not symmetric |

Unknown nonempty serialized names are preserved by deserialize/identity queries,
but validated copy/append/union reject incoming unknown names. Do not use a
validated copy to round-trip unknown serialized data. A registry rebuild does
not rewrite existing names. `IsValid` means nonempty, not currently registered.

`Count`, indexing, enumeration and exact intersection concern explicit members.
For example, `{State.Burning}` matches ancestor `State` hierarchically but does
not contain `State` exactly. Its explicit intersection with `{State.Frozen}` is
empty. Name order must remain Ordinal, including punctuation such as `A!`.

## Mutation and memory

Callers must provide exclusive mutation/registry stability during an operation;
this is not a thread-safe container. The optimized validated batch path invokes
no warning callback. Unknown/uninitialized routes retain legacy live traversal,
warning reentrancy, sorted partial state and exception order. Do not treat a
batch as atomic. Resource-exhaustion rollback and identical private capacity
are not promised.

No operation promises unconditional zero allocation. Reuse benefits apply when
capacity is sufficient and the validated route is selected; aliases containing
unknown names can need compatibility snapshots. New result objects and growth
can allocate. `Clear` releases references from used member slots but keeps the
backing array. `EnsureCapacity` rejects negative values and never shrinks storage.
No permanent registration cache is created by validation reuse.

Do not mutate a container while enumerating or use a mutable container as a
hash-map key while changing its members. No new fail-fast enumerator contract is
introduced. Query expressions remain mutable and are evaluated without a
compiled cache; changes take effect on the next evaluation.

## Upgrade check

Remove duplicate package installations, preserve existing `.meta` GUIDs, import
the candidate, and run the opt-in Unity tests in a disposable project. Verify
Inspector/prefab/asset round trips in the actual project separately. Generated
C# tags still resolve names; no integer-table regeneration or serialized ID
migration is introduced. Runtime and Editor source bytes are frozen to the
selected trees recorded in the package manifest.
