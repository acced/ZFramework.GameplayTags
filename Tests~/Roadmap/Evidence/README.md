# Preserved B1.1 measurements

- `focused-final` and `full-final`: first experimental policy, represented by `Baselines/B11First/Runtime`.
- `narrow-final`: B1 / First / Narrow factory-allocation comparison.
- `full-confirmation`: the narrowed experimental policy, represented by `Baselines/B11Narrow/Runtime`.
- `core-final`: final production `Runtime`, with B1 intersection/difference policies restored.

Historical source inventories refer to the then-tested candidate as `Runtime/`. They are observations preserved verbatim, not manifests claiming every file still matches current production. Raw CSV, count maps, source identities and sanitized runtime context are copied verbatim. All short/capped/adverse rows remain. Build/command logs and private workspace records are excluded.

The initial adverse +51.2% lifecycle observation remains in `full-final`; subsequent measurements do not establish its cause or erase it. Final core still has losing timing rows. These portable .NET observations do not qualify Unity/Mono/IL2CPP performance or establish universal improvement.

See `Documentation~/B11_REPRODUCE.md` for correctness and frozen-count replay instructions. Reproduction launchers remain the executed files; their original source hashes are retained even when packaging adds documentation.
