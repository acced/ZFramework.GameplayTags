# Preparation v3: one real candidate and an honest lifecycle frontier

Base: PR13 measured `aae745466fde9bec4e11d986886572be4c6bec2e`; branch parent is its documentation head. Production sources and previous experiments remain unchanged.

## Candidate

The generator creates three mutually exclusive build targets. v1 is PR12; v2 is PR13; v3 retains v2 fresh sorted/unsorted construction and `CopyAsStorage`, but restores the exact v1 `ResetFromSortedUnique`, `ValidateSortedHandles`, and existing fill methods. No tiny-input fast path is added. Restoring these methods can lose v2 singleton/empty benefits: those rows must remain.

After validating storage/capacity and allocating the independent target, `CopyAsStorage` returns immediately for `Count==0`. It does not borrow storage, skip target allocation, shorten requested capacity or bypass invalid-option checks. The Count invariant guarantees no occupied bits; newly allocated storage is zeroed. Existing source capacity and membership are untouched. Ordinary clone still retains source capacity. Explicit conversion still reserves min(registry.Count,max(Count,requested)). Nonempty paths pay an extra branch and must be measured too.

Strict public inputs remain strict. Fresh validation may allocate before discovering a late-invalid handle, without publishing a partial object. Reset admits the whole input before mutation. Caller spans must not be concurrently modified. Micro16 retains its original 2^20-ID format limit. Array/Dense have no new such restriction.

## Benchmarks

`run.py` compiles v1/v2/v3 with the same pinned SDK8.0.425, runtime target net8.0, Release, no tiering/ReadyToRun, one allowed CPU. Seven samples in three independently launched rounds, deterministic shuffled candidate order. Calibration targets 0.5ms subject to an8MiB allocation cap, IDENTICAL for every strategy. All samples and GC collection counters remain. Absolute results must not be mixed with previous calibrations/hosts.

Construction covers two registries, six counts (0,1,2,8,128,4096), three localities, two seeds, sorted/reverse/shuffled/duplicate inputs, fresh and sorted-reset contracts. v1/v2/v3 and pre-sized Add controls all run. A/A labels share the exact same binary.

Focused conversions compare v2/v3 for 0,8,4096 members across both directions and same-kind copies; fresh direct, constructor+CopyFrom and preallocated CopyFrom all retain identical target capacity. This is a focused conversion matrix, not a claim to rerun every prior size and lifecycle combination.

## Lifecycle frontier

Input-origin:

- keep: build source representation from sorted handles, then unchanged independent Unions;
- convert: build source representation AND convert it, then unchanged Unions;
- direct: build target representation directly from the same handles, then unchanged Unions.

Live-origin:

- keep: use the existing set; no unnecessary clone;
- convert: explicitly produce an independent target;
- direct/rebuild: export the existing set to an independently owned RuntimeTag[] INSIDE timing, then construct the target. Export allocation and enumeration are not free.

Every strategy uses the SAME peer object stored in the ORIGINAL source representation. This measures mixed kernels rather than silently preparing an ideal peer for each strategy. Inputs and peer are not mutated. Keep/live/zero-uses intentionally retains the source identity, is not a clone and must not be ranked as an independent result-construction API. Other preparations and all actual Union outputs remain independently owned.

The reserve is N+17 in this lifecycle protocol; keep versus target may have different physical layout/bytes by design. Source creation is excluded only for an explicitly pre-existing source. Peer preparation is excluded equally for all. Counts, source/peer identity, input digests and allocation are checked. Uses 0,1,16 cover preparation-only and repeated Union. This frontier does not yet cover weighted queries/append/remove, cold rotating actors, or a trained selector. No optimal-mode oracle is deployed.

There are still two separate compiled families (Array/Dense and Micro/Dense), NOT a unified Array/Micro/Dense class, cross-family bridge or automatic conversion. No new per-instance fields, online counters, pools, COW, deferred Count or hidden hot-path preparation.

## Reproduction

```
mkdir -p artifacts
git archive --format=zip HEAD > artifacts/source.zip
python3 'Audit~/PreparationV3/run.py' --output artifacts/results --rounds 3
python3 'Audit~/PreparationV3/review.py' --root artifacts
```

The runner keeps exact generated C#, binaries, project files, caller disassembly and full original regressions. `collect.py` downloads the two run artifacts by exact name/id, verifies archive digest and source tree, repeats raw reconstruction, compares generated code across CPUs, and creates source ZIP plus CSV and raw evidence.

At submission: not yet measured/accepted. CI success alone is not performance approval. Retain all regressions and A/A instability. No production migration or universal-four-metric claim. Linux x64/ARM64 .NET and a separately compiled netstandard2.1 assembly run by a net8 host do not substitute for real Unity/Mono/IL2CPP/Burst/mobile validation.
