# Micro-bitmap pilot — not a Runtime migration

The baseline remains Auto `84729cc3b6e1b4681328806df2d7c8a1d8f568f6`.
Only `Audit~` experiment files and a dedicated workflow are added. Runtime,
Editor, Samples, Tests and package version are unchanged and checked byte-for-byte.

## Hypothesis being tested

A packed record contains a block address and a bitmap, not a member ID:

- Micro16: high 16 bits identify a 16-ID interval, low 16 bits are membership.
  One `uint` can represent multiple members. Registry size is explicitly limited
  to 2^20 IDs; out-of-range registries are rejected, never truncated.
- Micro32: high 32 bits identify a 32-ID interval, low 32 bits are membership.
  This separately compiled `ulong` record format covers larger int-index spaces.
- Each configuration combines its micro records with contiguous `ulong[]` Dense.
  Selection is deterministic at construction: compare reserved member payload
  against the full dense word payload, using the record width. Ordinary mutation
  and EnsureCapacity NEVER change an instance's encoding. Explicit CopyFrom
  into a differently prepared result performs a measured/visible conversion.
- Each instance has one effective membership buffer; a single micro record can
  be inline. No raw member list, page table, pool, COW, deferred Count or shared
  mutable result is used. Physically this is compressed micro-bitmap OR Dense,
  not an always-full-universe Dense vector.

The encoding idea is inspired by tinyset's address-plus-mask storage:
https://github.com/droundy/tinyset . The kernel is independently implemented;
its sorted-record algorithms and benchmark results are not tinyset results.

## Configurations and contracts

`micro16` uses binary lookup and upper-bound fresh union reserve; `scan16` also
scans at most eight records; `exact16` counts output records before fresh union
allocation; `micro32` uses wide records. Do not combine per-row winners into an
imaginary product. All variants use identical eager cardinality semantics.

Public entry points establish ownership once; internal construction/union/copy
kernels use that fact. Supported output aliases are retained. Copies allocate
only actual micro records rather than spare input capacity. A copy is independent
and mutable; it does NOT promise to preserve source reserve. Dense copies clone
the complete owned word buffer. Reserve guarantees arbitrary member placement.
The Into union path does not allocate when the actual output fits prepared space.

Portable SWAR bit counts are used in the main measurement, not a .NET-only
hardware intrinsic substituted into a Unity claim. Dense OR/difference loops
process four words at a time with separate cardinality accumulators.

## Validation

Run from a checkout containing the fixed objects and reference ZIPs:

```sh
python3 'Audit~/micro_pilot.py' --references artifacts/references --output artifacts/results --rounds 3
```

The workflow exports all references and runs this command. `--cpu N` pins this
process and descendants where Linux affinity is available. Local and remote
runs MUST remain separate datasets. SDK and runtime details are archived.

Tests cover mixed encodings, 4096 exhaustive set pairs, dirty outputs, both
union aliases, independent results, random mutation, encoding boundaries,
prepared allocation with a positive control, and actual-result capacity.
`MicroStress.cs` additionally exercises all 65536 16-bit masks and repeated
mutation across the preparation selection threshold. Wide address tests use
an explicitly minimal owner solely to exercise arithmetic, not as a substitute
for production registry construction tests.

Primary benchmark: exact query, new independent union, actual copy then append,
actual copy then remove. Prepared output is a separate contract. Six fixed
source references plus old Auto's forced Dense are retained. Original fixed-key
queries remain; mixed probes are separate 128-query batches. Seven samples per
case, rotating/reversed independent processes, uniform warmup/collect/finalizer/
collect protocol. Construction, allocation, initialization, copying and Count
are INSIDE timing. Literal reference methods are not rewritten; adapters are
saved. All rows, allocations, failures and slower measurements remain visible.

Each candidate/contract has 252 measured rows, 168 primary rows, for three
registries (10000, 65536, 262144 explicit definitions), seven member counts
(0,1,8,32,128,1024,4096), contiguous and scattered inputs. Actual registries also
contain implicit parents. A provisional performance margin requires 5% lower
latency than every included reference in EACH process round; this is not a
statistical confidence interval. A green job is successful test execution and
archiving, not successful performance promotion.

## Limitations

This is a gate-one pilot, not the full public GameplayTags API. Hierarchy,
FrozenQuery integration, asset import, Inspector, release migration and native
Unity/Mono/Android/iOS IL2CPP were not migrated or validated. Full-overlap,
disjoint and random member relocation are correctness-tested; their complete
performance matrix and all cold conversion/long-lived memory costs still need
separate evaluation before any promotion. The >2^20 wide build is checked for
address correctness, not benchmarked on a >2^20 real registry here.

No production promotion, merge or stable release is authorized by this file.
Results belong in the workflow artifact and PR, with exact tested commit and
source hashes. Failed performance targets must not be relabelled as success.
