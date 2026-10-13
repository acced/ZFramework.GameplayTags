# Phase 1: compatible ordered-list merge candidate

This draft implements the roadmap's B1 on main 4f03bb16e8e5f9403b7fd67440da078fc3532a8d. It retains name identity, string-sorted storage and existing serialized data. The separate P1 integer/snapshot architecture remains experimental and is not included in this production change.

## Changes

- Merge-based Union, IntersectionExact and FilterExact.
- Batched RemoveTags/difference compaction, ordered append fast path.
- Reusable target APIs: EnsureCapacity, SetUnion, SetIntersectionExact, IntersectWithExact and ExceptWithExact.
- Targets may alias either input; existing null/self behavior is preserved.
- Incoming Copy/Append/Union members retain registration validation; exact intersection/filter/difference preserve matching/surviving unknown serialized names. Append does not purge existing unknown target members.
- Unknown or not-yet-initialized inputs use the original public per-Add route to preserve warning/RegistryChanged observation, mutation and exception ordering.

## Validation and measured scope

The portable differential suite and .NET Standard 2.1 / C# 9 compile pass locally. See Tests~/Roadmap/README.md for reproduction and limitations. Full Unity validation remains outstanding.

A descriptive three-round CoreCLR 8.0.31 comparison at n=1024 measured B0/B1 medians (ns per whole public operation): new shuffled build 469732/323761, new Union 275290/132488, new exact intersection 100744/13622, full lifecycle 797794/472810. This is evidence of large-set gains, not universal performance acceptance. Small/empty operations, alias scratch allocation and runtime/host variability remain relevant. The full separately delivered roadmap dataset had 495 short batches among 1359 rows; no performance result is regenerated or enforced by this PR's correctness CI. No P1 result is presented as a B1 result.

This remains a draft compatibility candidate for review. It does not reopen or merge the closed earlier PR, and does not change the default architecture to P1.
