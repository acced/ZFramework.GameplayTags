# Count == used: single-bit record experiment

Base: PR8 `2866adfc6bfc21a11177c6b5873fbc54afebfe13`. No production migration.

For canonical MICRO records every nonempty mask has at least one bit. The sum of mask cardinalities equals the number of records iff every mask has one bit. Dense uses a different meaning for `used`; the predicate is only considered inside the established Micro array path.

Candidates generated literally from fixed DirectArraySet:

- DirectControlSet: unchanged PR8 direct array implementation.
- SingleRemoveSet: only specialize removal when the LHS has Count == used. At the end Count equals retained record length. RHS may have multiple bits per record.
- SingleBitSet: additionally specialize independent array merge and in-place append when either side has Count == used. Each overlapping bit mask then has cardinality 0 or 1. Union still computes a.Count + b.Count - duplicates, NEVER Count = used.

No additional object fields, stored flags, raw member IDs, pool, COW, deferred counts, mode switching or mutable sharing. No lazy compaction, no capacity-policy changes, no new growth branch. Lookup, copying, CountRecords and Dense fusion remain identical. Specialized loops are separated from the general loop once per operation, not a predicate rechecked per element. Inline/mixed paths retain the base implementation.

Tests: untouched full Micro suite, all 65,536 16-bit masks through public APIs with single/multiple-record counterpart, invariant transitions, same interval/different bits, alias/ownership, actual-result capacity, retained-array/inline states, seeded churn, allocations. Hardware-disabled suites and a separately compiled Standard 2.1 scalar library are exercised. Original PR8 size/iteration/GC protocol retained; only untimed input singleton metadata added. Three rounds/seven samples, all unsuccessful/slower rows retained. No speedup is asserted before results; Unity/IL2CPP/Burst are not executed by this workflow.
