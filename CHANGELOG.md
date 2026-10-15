# Changelog

## 1.1.0-rc.1 — 2026-10-15

Selected implementation: B1.1 + C1 + C2, Runtime frozen to `792ce83f`.
This is the release-candidate packaging/qualification of that implementation,
not an additional runtime optimization and not a stable publication.

### Included since 1.0.0

- Ordered bulk union/intersection/difference and reusable-output APIs.
- Per-call validation reuse with compatible unknown/uninitialized fallback.
- Direct-source append and alias-union storage reuse, without the previous
  validated-path temporary incoming arrays or cloned lists.
- Single-list constructor initialization.
- Empty/singleton copy special cases (C1).
- Containment-failure cursor reuse and suffix-only merge/reservation (C2).

### Release engineering

- Frozen Runtime/Editor identity and deterministic, allowlisted UPM archive.
- SHA-256 inventory, archive readback verification and packaging regression tests.
- Opt-in Unity contract/serialization tests plus a separately labelled portable
  compile-only check. Existing portable differential/compatibility CI is retained.
- Installation, API/migration and pending Unity release-gate documentation.
- Experimental sources/raw data remain in the repository, not the lean tarball.

### Not included / known limitations

P1, C3a and the C1-removal diagnostic are not production implementations.
Historical timing losses, short batches and A/A variation remain published.
C1 removal did not confirm the cause of the historical lifecycle regression;
that investigation is closed without a causal conclusion. General performance,
Unity Editor/Player, Mono, IL2CPP and device acceptance remain outstanding.

## 1.0.0

Initial name-based hierarchical tags, ordered containers, queries, redirects,
settings, editor tooling and basic usage sample. See repository history for the
original implementation and all subsequent experimental evidence.
