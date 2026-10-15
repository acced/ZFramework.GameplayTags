# Immutable C1/C2 comparison sources

- B1: reuse `Tests~/Roadmap/Baselines/B1/Runtime`, the existing complete seven-file snapshot of `dac77e7`. Its original provenance remains in `Tests~/Roadmap/Baselines/ORIGIN.md`.
- PR4: `Tests~/C12Correctness/Baselines/PR4/Runtime` is the complete seven-file Runtime snapshot at commit `352d304299ecbb7486e2c6a56ba71874c90fe21b`.
- C1: `Tests~/C12Correctness/Baselines/C1/Runtime` is the complete seven-file candidate after C1 and before C2. Relative to PR4, only `GameplayTagContainerAlgorithms.cs` changes: null/empty copy clears and returns early, validated singleton copy uses Add, and general/fallback traversal stays unchanged. This snapshot was frozen before C2 implementation. Its content is identified by the repository-relative SHA256 manifest; no unpublished commit identifier is claimed.
- current: the checked-out root `Runtime` is the candidate under test. It is intentionally not another immutable baseline.

Snapshots are never modified to make candidates pass. Vendoring PR4 and C1 makes comparison reproducible from a source archive without network access or shallow-history assumptions. The C1 and C2 runtime changes should remain separate commits; these fixtures and CI wiring belong in a later publication commit.
