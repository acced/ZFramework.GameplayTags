# Capacity-certified copy: same correctness, boundary proof before hot loop

Separate follow-up within PR25. Do not replace or mix the prior 4da1645 run's measurements. control and eager remain byte-identical to that experiment and are remeasured here. This is a new complete candidate, not a table assembled from methods' historical best values.

Prior run37013478442 completed x64/ARM64 and downloaded-original rechecks. Both eager/checkpoint had zero invalid states in3931injected growth points per CPU. But checkpoint/eager's168nonempty added copy cases had15fast/0slow onx64 and0fast/13slow onARM64. Do not adopt checkpoint universally because it does fewer popcounts. Full prior raw data/negative samples remain in that artifact.

## New mechanism

Let r be source nonzero record count,N source member count,K the registry's possible record keys. Every source record contains >=1member and has one distinct valid key, so r<=min(N,K). If existing EntryCapacity>=min(N,K), the entire copy is certified unable to grow. It can use Write directly, skipping the redundant PutRecord capacity/representation dispatch and any partial-prefix counting. Normal completion still assigns source.Count in unchanged CopyCore.

If this bound does not prove sufficient capacity, use the already correct eager loop in a private CopyDenseGrowing helper. The test is conservative: packed inputs can fit even though the bound fails. This is deliberately NOT an estimated CPU cutoff, record-count prescan or observed-data threshold. No allocation, conversion, pool, stack workspace or persistent metadata added.

Clear keeps capacity unchanged. Registry identity, alias and Dense-source/Micro-target admission still occur in existing public/core paths. Source and destination buffers cannot alias and no mutation/concurrent observation/callbacks are permitted inside the private operation. Certified path has no member-buffer allocation site; fallback keeps exact Count after every published record. This remains a basic partial-result guarantee for collection-buffer allocation failure, not rollback or recovery from arbitrary runtime/OS/stack failure.

No original append, query, removal, enumerator, clone, builder, homogeneous kernel, fields or capacity ladder changed. Only CopyDenseToMicro and its added growing helper differ. Both exact bound certification and fallback overhead are timed. The primary eager fix is a simpler competitor and must not be discarded.

## Validation

Identical fixture/source files from CopyCommit:3931actual-growth-point injections and3873normal configurations, publiccopy/emptyappend/bothalias/sameinput/emptyinput shortcuts, dirty outputs/highoffset/multibit records, retry/remove/reuse, homogeneous Micro controls. Old control defect must reproduce. Each repair must have0invalid states in this scope. Fault hooks are separate binaries. Inherited full mixed/local/bulk and original Micro+protectedRuntime suites; guardedHW-disabled and independentStandard2.1host. No Unity/IL2CPP/Burst/phone claim.

All2520prior public cases and192copy cases rerun per candidate,same-binaryA/A,7samples×3shuffledrounds,SDK8.0.425 onx64/ARM64. Same full-operation allocation/capacity/ownership/Count contracts; no sample exclusion. Archived verifiers rerun after downloading originals. No first-place or no-regression claim before results.

```sh
python3 'Audit~/CopyCapacity/run.py' --output artifacts/results --rounds 3
python3 'Audit~/CopyCapacity/review.py' --root artifacts/results
```

Full-history checkout required. All generation and work stays in Audit~, production unchanged. Initial local guarded preflight used SDK8.0.423/.NET8.0.29:3931injections0invalid,3873normal cases passed; not substituted for pinned CI timings.
