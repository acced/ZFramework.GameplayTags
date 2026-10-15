# B1.1 public operation comparison

Identities: B0 is original db37fed; B1 is PR #4 dac77e7; B11 is this working source. P1 is excluded. The retained baseline source trees and source-before/source-after inventories make these identities explicit.

The focused stage adds 55 whole-public-operation cells at n=0,8,1024: capacity construction, copy, contained/ordered-before/ordered-after/overlap/late subset-miss append, target union aliases left/right/self, and target intersections with 0/1/2 matches. Append and alias union include CopyFrom restoration inside measurement, labelled as whole cycles; these are not isolated merge-kernel costs. Inputs are selected from a fully Ordinal-sorted universe, with explicit disjoint-bound assertions. Targets are pre-reserved for reuse; retained backing capacity is reported independently of allocation during timing. Source and result names/order are validated after every batch. Sixteen additional n=1024 rows use four-member head/tail/spread/mixed inputs in both operand directions for intersection and CopyFrom+ExceptWithExact cycles, exposing short-prefix and tail-search behavior of the integrated comparison-work heuristic.

Focused counts are derived once from exact B1, targeting100ms, then shared across B1/B11. Four fixed balanced orders: B1/B11, B11/B1, B11/B1, B1/B11. A late missing subset member exposes the extra containment scan. First preparation/JIT is excluded. Empty/small caps and any short intervals remain visible; a target is not a guaranteed duration.

The following full stage reuses the earlier151-case public API workload with B0/B1/B11, including new/reused outputs, lifecycle, enumeration, exact/hierarchical queries, serialization and rebuild. B0-only calibration targets50ms. Three orders are B0/B1/B11, B11/B1/B0, B1/B0/B11; they are not fully position-balanced. The same legal B0 target-intersection compositions are retained, without claiming either is universally strongest. Public B1 HasExact/enumerator logic is unchanged; any timing movement cannot be attributed to an integer backend.

Both stages retain bytes/GC, raw batch duration, short/capped labels and incomplete array-payload census (no object headers/string payload/whole heap). Allocation and time share the same batch. All observer work shown by the harness is included. No forced GC. CoreCLR results are not Mono/IL2CPP/Unity performance claims.

The full-lifecycle row's retained-container census uses a newly built representative input container outside measurement. It is a storage proxy, not the retained payload of the measured union/intersection/cleared-output tuple. Other retained-container rows inspect the selected operation's result or input container; none of these array-payload figures is a whole-heap measurement.

Run with an installed .NET8 SDK (`DOTNET` or PATH):
- `bash Tests~/Roadmap/focused-b11.sh`
- `bash Tests~/Roadmap/benchmark-b11.sh`

Both support `--build-only`. Public runs follow the separate small algorithm-attribution experiment; its candidate-specific count caps are not substituted for this shared-count public comparison. No noisy performance threshold is added to correctness CI.
