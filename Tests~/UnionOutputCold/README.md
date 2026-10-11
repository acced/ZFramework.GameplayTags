# Reusable Union: true first-use cold probes

This is the self-contained CI coordinator for the previously frozen reusable-output Union allocation fixtures. It runs no timing, calibration, preliminary correctness invocation or code warmup. Every case/mode/variant starts a new managed process; the fixture measures its first Apply invocation before its existing validation and one warmed repeat.

## Frozen workload

The two C# programs and projects are byte-identical to the earlier alias-first-use and nonalias-first-use probes. `fixtures.json` pins their SHA-256 values and the unchanged ordered cases:

- 15 alias cases, including either side, skewed closure/explicit counts, equal-closure ties, normal-set preference, counted operands and storage proxies.
- Six distinct cases, each with fresh and reserved output: 12 cases.
- Each case runs once in allocation mode and once in audit mode, in separate fresh processes. Two source variants therefore produce 108 fixture processes per backend.

All existing public name/count/source-preservation/normalized-drain assertions remain inside the programs. Nothing was added to their measured bodies. Allocation mode has no EventListener; audit mode's managed event instrumentation is excluded from allocation comparisons.

## Invocation

Use a fresh output directory outside this package. Paths and executable locations are supplied by CI; no workstation paths are embedded.

```sh
python3 Tests~/UnionOutputCold/run.py \
  --dotnet "$(command -v dotnet)" \
  --variant original=/path/to/original-checkout \
  --revision original=FULL_40_HEX_ORIGINAL_REVISION \
  --variant candidate=/path/to/candidate-checkout \
  --revision candidate=FULL_40_HEX_CANDIDATE_REVISION \
  --api-variant candidate \
  --out /path/to/new/cold-dotnet
```

For a standalone Mono CI leg, add `--mono "$(command -v mono)"` and use another output directory. The same SDK builds net472 against the fixed reference/package dependencies; the supplied Mono executable runs every probe. Its actual version is recorded, so a Mono 6.8 CI observation is distinct from a smoke on another Mono version.

The first variant always uses original factory Union followed by Copy. Labels named with `--api-variant` compile the existing `REUSABLE_UNION` path. Comparisons cover the original first variant against every later variant and each adjacent predecessor pair. For original, previous and candidate, both original comparisons and previous-to-candidate are retained, including when previous and candidate both use the API. These comparisons reuse the same reports and start no additional fixture processes. Revision labels require exactly 40 hexadecimal characters.

For explicitly uncommitted preparation only, `--source-patch candidate=/path/to/patch` identifies that source as the declared base revision plus the patch hash. It is never represented as the exact base commit. Revision/patch declarations do not themselves verify Git provenance; every source file is independently hashed and hashes remain authoritative. Normal published CI should use exact revision labels without this option.

## Raw evidence and provenance

`preliminary-environment.json` is written before any child command. `environment.json` is updated atomically before/after each command and finalized on success or failure. Full raw stdout/stderr are streamed to disk, including compiler/probe failures and partial timeout output. Every raw stream has a SHA-256 checksum; unchanged fixtures do not emit a separate numeric workload checksum.

The manifest records source/fixture/project/coordinator hashes, declared revisions and optional patch hashes, SDK/runtime inventory, actual host executable hashes/version, platform/CPU/affinity, safe known effective runtime settings, command order, and compiled output hashes including runtime configuration/dependencies. Unknown prefixed environment-variable names are recorded without copying arbitrary potentially sensitive values.

For .NET, native COREHOST_TRACE is enabled for each fixture process. Its raw trace and resolved CoreCLR path/hash identify the actually selected CLR; the SDK version is not mislabeled as the CLR version. Tracing is outside the unchanged managed fixture and adds no managed measurement code. For Mono, the exact supplied engine version/hash is captured. No claim is made that a local Mono smoke reproduces CI's different Mono version.

Source/harness and all completed build outputs are re-enumerated and rehashed at the end. Host binaries and every observed CoreCLR are also rehashed; CoreCLR path/hash changes between children invalidate the run. Added, missing or modified files invalidate the run. `observations.json` preserves every successful fixture payload and raw checksum; `comparison.json` preserves allocation, capacity/mask and pool comparisons. On failure, inspect `failure.txt`, raw output and the final manifest. An abrupt external kill may prevent finalization; preliminary metadata and already-streamed bytes still remain.

## Interpretation and gates

The coordinator flags any managed cold/warm allocation increase, output/source backing-capacity or active-mask difference, or changed .NET pool observations. It preserves all results before exiting nonzero. It does not require each row to improve, claim aliases are faster, or turn one-shot retained-heap noise into a stable retained-memory verdict.

`FirstCallBufferAllocatedEvents` is limited to the first Apply window. `ObservedAllocationSizes` is the unchanged listener's full observed BufferAllocated sequence through setup, first call, warm call, validation and drain; it is not exclusively a cold-call trace and is not a Rent-argument trace. True first-use misses are observable in the existing cases that need pooled scratch.

Even on .NET, a tiny no-pool path may never initialize the ArrayPool EventSource. Equal raw/unobserved payloads are labeled accordingly and do not establish no rents or verified pool equivalence. Mono may not expose the ArrayPool EventSource. Its raw audit payloads are retained, but missing events never mean no rent and are not used to assert pool equivalence. The implementation's unchanged queue-request proof remains separate from these observations.

Cold/warm bytes, output/source capacities and active masks are compared directly. Whole-GC retained heap deltas are preserved verbatim but intentionally not treated as stable retained-memory acceptance. The separate 28-row UnionOutput timing/retained suite remains unchanged and is still needed for performance decisions.
