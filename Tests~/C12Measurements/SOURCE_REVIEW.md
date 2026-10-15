# C1 and C2: source changes and correctness argument

## Scope

Only `Runtime/GameplayTagContainerAlgorithms.cs` changes. No representation, general size threshold, intersection, difference, non-alias union kernel, or overwrite-copy strategy changes. C1 and C2 are separate candidates; this document makes no performance claim. Execution and measurement evidence must be reported separately.

## C1: exact empty and singleton copy paths

- A null or empty source clears the destination and returns. The prior registration helper already returned immediately for these inputs, without initializing the manager. Capacity is retained and `List.Clear` releases references in previously occupied slots.
- Nonempty sources retain the existing order: `AllRegistered(source)`, destination clear, capacity reservation, then copying.
- A validated singleton is written with `List.Add`; larger validated sources still use `AddRange`. Source-side tag/string representatives are copied, including equal names backed by different string objects.
- Failed validation retains the original indexed `AddTag` loop and its live `source.Count` condition. Unknown-name warnings, callback mutation, callback exceptions, sorted partial destination state, and no-initialization behavior follow the existing fallback.
- Public self-copy is still excluded by the unchanged `CopyFrom` entry point. No alias snapshot or overwrite-copy path is introduced.

C1 source snapshot SHA256: `7d4aa2f4023247c7d9703a2747aa5a3b4d26863577885796aa308eef06d871c4`.

## C2: reuse containment scan progress

The existing direct-tail append and containment count/range eligibility checks remain. If a containment scan is skipped, both starts remain zero, retaining a full merge. `TryApplyContainedSource` now returns its destination and source cursors as well as its containment result.

### Resolved-prefix invariant

At a failed comparison, let the returned starts be `d` and `s`. Every source member before `s` has already matched a destination member before `d`; any unmatched destination members before `d` also belong in the final union. All those prefix names precede the remaining source names. The destination prefix `[0,d)` is therefore final and need not participate in merging.

Ordinary append and left-alias union retain destination representatives. Right-alias union requests source-preferred equality, so the scan has already installed the incoming left-input representatives for matched members in the resolved prefix. The remaining merge uses the same representative rule. Neither source contents nor names change during this validated, callback-free operation.

### Suffix growth and reverse merge

Only `source[s..]` is appended to grow logical count. When `s == 0`, the original `AddRange` is retained; otherwise an indexed `Add` loop avoids a temporary collection. Capacity is reserved for `oldCount + (source.Count - s)`, with subtraction grouped before addition.

Reverse merging reads destination indices down to `d` and source indices down to `s`. Expanded suffix space keeps writes at or beyond unread destination elements. Neither merge nor tail loops write the resolved prefix. Each equality emits one representative instead of two and leaves one unused slot in the suffix.

After merging, the unused slots form the contiguous gap `[d,write]`. Removing `write - d + 1` slots at `d`, only when `write >= d`, compacts the merged suffix and clears the removed trailing references. The prefix is not removed or shifted. Removing from index zero would violate this invariant and is deliberately avoided.

### Capacity and resource failures

Partial-containment failures intentionally use a tighter reservation than the previous `oldCount + source.Count`. Retained backing capacity can therefore differ even when logical output is identical. Existing capacity remains available; this is not an explicit shrink operation. Logical output, representative identity, and clearing of unused trailing references require separate assertions from capacity expectations.

As before, right-alias containment scanning may replace matched representatives before a later allocation fails. The operation remains nontransactional under resource exhaustion; no rollback guarantee is added. Reduced reservation can change whether or when resource exhaustion occurs. The correctness argument concerns normal completion, and does not claim identical out-of-memory behavior or unchanged retained capacity.

C1+C2 source snapshot SHA256: `55d8352d53a3528a75181a4fe02d3796b41e4cf177716235dd7d85964c1ea9f6`.

## Review and verification boundary

Independent source review cleared both concrete changes. Source review is not execution evidence. Test results, allocation observations, timings, and Unity backend acceptance should be cited only from the separately recorded runs. Important cases include first/middle/last missing source members, unmatched destination prefix, multiple later gaps, full containment, skipped scans, both alias directions, and representative identity before and after the failure cursor.
