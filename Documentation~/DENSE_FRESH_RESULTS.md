# Dense-only: independent fresh results, not only prepared output

Date: 2026-09-28. Incoming implementation: `3a61c3f7ea8909ecf32284210999d30e6c17961a`.

## Scope and decision

This pass completes the missing **large registry / few members / fresh independent result** check. The source remains a full Dense bitmap with occupancy summaries. No Sparse/Auto mode, member-ID list, compressed page directory, pool, copy-on-write, borrowed result, or deferred cardinality was added.

Two construction experiments were rejected after comparative local measurements: directly filling a fresh union from OR-ed summaries, and a whole-buffer clone path for sufficiently occupied copies. They did not show consistent improvement; some measured paths regressed. The shipping `Runtime/RuntimeTagSet.cs` is therefore byte-for-byte unchanged from the incoming commit. New work in this pass is regression coverage, diagnostic measurement, and an explicit release finding—not an unmeasured performance claim.

Keeping the simpler measured implementation is intentional. A larger special-case implementation is not accepted merely because it sounds faster.

## Added acceptance

- `Audit~/DenseFreshTests.cs` verifies newly allocated copies and unions, empty/self operands, independent mutable ownership, foreign registry rejection, member words, all summary levels, eager Count and later mutation. It covers 0/1/63/64/65/4095/4096/4097/262145 registry sizes, contiguous/scattered members, and small/large cardinalities. Local .NET execution completed 685,554 assertions with zero failures; native Unity execution is not claimed.
- `Audit~/DenseFreshCosts.cs` adds 108 diagnostic scenarios across 10,000, 65,536 and 262,144 explicit definitions. It separates raw zero-array creation, reserving an empty set, fresh copy, fresh union, copy+append and copy+remove. Actual result allocation and Count maintenance remain inside timing.
- `Audit~/dense_fresh.py` runs the new regression and cost fixture, checks every expected scenario, retains every seven-sample process result, and projects 32 large-registry/few-member cases directly from the **same run's complete comparison**. A non-winner is not removed or turned into a passing ranking.
- `.github/workflows/dense-runtime.yml` executes this step in addition to the existing complete comparisons, block/index/count tests and allocation diagnostics.

All variants retain the common `settled-setup-v2` GC / pending-finalizer preparation. This happens before timing. There is no allocation subtraction, timing subtraction, outlier trimming, or change to the old allocating-operation sequence. Controls diagnose costs; they are not a mathematical CPU lower bound and must not be subtracted from result times.

## The cost that occupancy summaries do not remove

For U registered tags, the member payload has `W = ceil(U / 64)` words. The full allocation also includes `ceil(W/64)` summary words, then the next summary level, through one root word.

In the 262,144-definition fixture, implicit ancestors yield 266,241 registered tags:

```text
member words: 4,161
summary words: 66 + 2 + 1
payload: (4,161 + 66 + 2 + 1) * 8 = 33,840 bytes
```

The recorded Linux x64 .NET run allocates 33,912 bytes for a non-empty independent result: payload plus object/array overhead. This remains true when the character has only eight tags. The directory avoids scanning empty member words; it does not shrink the allocated array.

For illustration, the **incoming** CI run `36401740268`, source `3a61c3f`, reported the following for 262,144 definitions, eight scattered members. These are historical measurements, not a new run or Unity results:

| Operation | Indexed Dense | Previous Auto | Indexed Dense allocation |
|---|---:|---:|---:|
| Resolved exact lookup | 3.11 ns | 8.10 ns | 0 B |
| Fresh union | 1,860.99 ns | 64.15 ns | 33,912 B |
| Independent copy + append | 1,955.57 ns | 102.13 ns | 33,912 B |
| Independent copy + remove | 1,808.61 ns | 56.23 ns | 33,912 B |

The latest artifact contains the rerun's full, authoritative numbers. This table demonstrates why fast queries and prepared bulk updates do not establish a fresh-result win.

## Read the new artifact

```text
results/comparison.json                       # complete allocating comparison
results/prepared-comparison.json              # separate prepared-output comparison
results/fresh-results/tests.json              # fresh mutable-ownership regressions
results/fresh-results/costs-r*.json            # all raw control and operation samples
results/fresh-results/cost-summary.json        # median of process medians, no selection
results/fresh-results/large-registry-small-members.json
results/fresh-results/summary.json
```

Reproduce after running the existing complete audit:

```bash
python3 'Audit~/dense_fresh.py' --results artifacts/results --rounds 3
```

A successful job means these checks executed correctly. It does **not** mean every Dense operation won. `all_large_small_targets_met` and `all_performance_targets_met` remain separate from correctness, and stable release approval remains false. Actual Unity/IL2CPP, target-device CPU and managed/native/peak-memory acceptance are still required.

## Algorithm context

The 64-way directory and bit operations remain based on the references already recorded in `DENSE_ONLY.md`: [fast_set](https://hitonanode.github.io/cplib-cpp/data_structure/fast_set.hpp.html), [LLVM data-structure guidance](https://llvm.org/docs/ProgrammersManual.html), and the attributed portable bit-count implementation. [.NET GC documentation](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals) describes managed allocation and initialization. No algorithm is credited with eliminating a physical full-universe allocation that it still performs.

## 中文结论

本轮补齐大注册表、少成员的独立新建结果验收。新结果的数组、对象、准确 Count 和后续可修改性都没有从计时中移走。局部构造快路径实测没有稳定收益，已撤回；运行时保持上一版，不增加缓存或回退 Sparse。当前完整 Dense 契约仍未达到所有场景第一。预分配复用是另一种已单独测量的使用方式，不能拿它冒充新建结果的成绩。
