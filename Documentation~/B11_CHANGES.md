# B1.1: scoped refinements of PR #4

Baseline is exact B1 commit dac77e7e7d5bad1ba7e77ed9fca7824d50187a72. The original B0 and B1 sources remain separate test/benchmark inputs. This change retains name-sorted List storage; no P1 integer representation, tag layout, HasTagExact or public enumerator change is included.

## Implemented mechanisms

- Validation is per input per call. Already-validated batch results do not call IsRegistered again. Unknown/uninitialized paths retain live AddTag traversal, warning behavior, reentrant mutation and exception ordering.
- Fast copy uses AddRange; callback-sensitive copy keeps its separate legacy loop.
- Append directly reads the distinct source while merging backward into target storage. No temporary incoming tag array. Empty/strictly ordered append uses direct AddRange.
- A count/range-qualified containment scan avoids growth for fully contained input. A late miss can pay an extra scan before merge; this is measured explicitly. Right-alias union keeps lhs representatives even for distinct equal-name string objects.
- Fully validated SetUnion aliases reuse existing storage. Self-union is only an identity after validation; unknown self-union still filters through the compatible path.
- Constructor chaining creates one List on each successful path, retaining invalid-capacity and null-copy behavior.
- Intersection and difference retain the exact B1 merge kernels and fresh-factory minimum-input capacity policy. The separately measured sparse/skew policy is deferred because common small intersections and some whole workflows became slower. All public reusable-output APIs remain available.

Fast-path validation is not permanent registration caching. Concurrent container mutation/registry replacement is outside the existing single-operation stability assumption. Resource-exhaustion atomicity is not promised: allocation timing and, for right-alias containment, partial identity writes before an allocation failure can differ.

## Validation and measurements

Final differential checks passed B0 981,930 assertions, B1 978,409, and B1.1 1,002,001, with identical shared transcript 6EA12A348CF620CDC4FC89C09BEC0C8933A7B22A8542A2214F0CB4CE011A948F. The final core suite retains the same counts and transcript. Extra core-only assertions verify contained capacity and restored B1 intersection-capacity behavior; baseline adapter counts differ. All three compile for .NET Standard 2.1 / C#9.

See Tests~/Roadmap for public differential checks and B11_BENCHMARK.md for the finite public comparison. Tests~/B11Dispatch is a separately labelled algorithm-attribution experiment; its internal pre-reserved kernels and candidate-specific pilot caps do not replace publicAPI evidence.

Unity 2021.3 Editor/Player, Mono and IL2CPP remain unverified. Timing movement in unchanged exact lookup or enumeration must not be attributed to integer mapping; this B1.1 has none.

The first B1.1 source and both initial datasets are preserved. Its n1024 lifecycle median was 703.706 µs versus B1 465.445 µs (+51.2%); the later factory-only narrowing is evaluated separately. Component allocation accounting explains +72 B/cycle, not that latency. The subsequent confirmation still showed small-intersection costs, so production restores B1 intersection/difference policies. The final core is measured separately; earlier policy datasets are retained as experimental evidence and are not relabelled as core timings.
