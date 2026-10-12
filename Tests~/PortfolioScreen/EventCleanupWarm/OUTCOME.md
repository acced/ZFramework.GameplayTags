# Event cleanup: completed warm screen and stop

Status: **held unqualified, 0/10**. No further source variant, sampling or full qualification is queued for this series.

[Run 38217103753](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38217103753), exact commit `7037e76a43127939ab9545b2e1056441202961e6`, completed both backends. The tested source remains exact f7 plus patch `805651f029a280cb3be2e8e8c10edd4fa208db981852895fc118625080a91090`; branch-root Runtime is different and was not the benchmark candidate.

- .NET artifact `11731199542`: 29,756 bytes, SHA-256 `2cf63c0bb1941abf2de56297b1e0b50794e00971577c27dd0afe6719db2f65eb`.
- Mono artifact `11731219657`: 23,215 bytes, SHA-256 `073788f714ea317cac002704fb8a7b994c3aa59519f901fed482cd08f26d668c`.
- SDK 8.0.425, CLR 8.0.31 and standalone Mono 6.8.0.105; fixed requested runtime settings and exact identities are in provenance. The unchanged Screen has no new per-row runtime-switch observation. Actual Unity was not run.

Independent raw review confirms paired iterations, events/checksums and zero warm bytes throughout. Both backends completed the original twelve rows in four alternating pairs. The [separate first-use results](../EventCleanup/COLD_SCREEN.md) remain intact; no cold sample was rerun here.

## Every warm row

Ratios are original/candidate time (greater than 1 is faster). All four pairs are retained. Shortest batch and the original below-200-ms flag remain visible; a short flag is not a discarded sample.

### net

| Row | Four speedup ratios | Slower candidate pairs | Minimum batch ms | Short |
|---|---|---:|---:|---|
| dense-path/d64 | 0.957771, 0.997770, 0.916205, 0.993974 | 4/4 | 199.730 | True |
| exhausted-path/d64 | 0.995560, 1.085934, 0.999931, 1.006615 | 2/4 | 185.207 | True |
| silent/d1 | 0.991335, 0.993510, 0.996748, 0.999873 | 4/4 | 197.937 | True |
| global-only/d1 | 1.107126, 1.122617, 1.121287, 1.125097 | 0/4 | 178.219 | True |
| global-only/d4 | 1.036038, 1.011422, 1.035686, 1.039412 | 0/4 | 193.285 | True |
| one-hit/d1 | 1.084686, 1.121590, 1.124040, 1.092039 | 0/4 | 178.487 | True |
| one-miss/d4 | 0.998884, 1.000066, 0.982532, 1.000540 | 2/4 | 200.410 | False |
| huge-gaps/d4 | 0.995446, 0.991913, 1.000347, 1.010655 | 2/4 | 198.225 | True |
| huge-midpoint/d1 | 1.117419, 1.117362, 1.132794, 1.122125 | 0/4 | 179.401 | True |
| huge-late-miss/d1 | 1.003685, 1.002316, 0.996391, 0.999954 | 2/4 | 197.885 | True |
| clear-cycle/d4 | 1.024523, 1.057646, 0.814158, 1.049743 | 1/4 | 181.613 | True |
| bulk-reset/8xd4 | 0.983463, 1.008727, 1.004224, 1.002479 | 1/4 | 171.448 | True |

### mono

| Row | Four speedup ratios | Slower candidate pairs | Minimum batch ms | Short |
|---|---|---:|---:|---|
| dense-path/d64 | 0.926952, 0.961924, 0.999703, 0.999559 | 4/4 | 200.446 | False |
| exhausted-path/d64 | 0.974178, 1.004408, 1.016843, 0.998611 | 2/4 | 196.164 | True |
| silent/d1 | 0.980958, 0.934159, 1.017894, 0.989523 | 3/4 | 198.800 | True |
| global-only/d1 | 1.219574, 1.238446, 1.207326, 1.244460 | 0/4 | 158.561 | True |
| global-only/d4 | 1.170112, 1.089012, 1.142560, 1.159115 | 0/4 | 166.594 | True |
| one-hit/d1 | 1.237774, 1.270218, 1.277812, 1.264631 | 0/4 | 154.747 | True |
| one-miss/d4 | 0.999316, 1.104058, 0.993462, 0.950989 | 3/4 | 191.060 | True |
| huge-gaps/d4 | 0.992441, 1.140603, 0.954147, 0.884589 | 3/4 | 201.771 | False |
| huge-midpoint/d1 | 1.250547, 1.286458, 1.269438, 1.248719 | 0/4 | 151.689 | True |
| huge-late-miss/d1 | 0.997841, 0.922702, 1.022207, 0.970760 | 3/4 | 189.241 | True |
| clear-cycle/d4 | 1.194854, 1.176332, 1.151544, 1.114157 | 0/4 | 171.762 | True |
| bulk-reset/8xd4 | 1.024455, 1.011235, 1.112726, 0.993248 | 1/4 | 197.994 | True |

## Decision and bounded native comparison

Small one-event/global/midpoint paths improve in every pair on both runtimes, with roughly 10–12% .NET and 23–26% Mono speedup for the simplest cases. However, dense64 is slower in all four pairs on both backends, with approximately 200 ms minimum batches and zero GC. .NET silent is also slightly adverse in four pairs; the clear-cycle outlier and every mixed Mono control remain in the table. These results block promotion of the current shape.

One separately bounded, untimed .NET native comparison examined direct event-index comparison instead of the new remainder local. Exactly two builds and two correctness targets were run, each passing 7,601,762 assertions with identical output. This alternate spelling removes one instruction once per fully-drained flush, but leaves the dispatch loop, saved rbp/rbx registers and 40-byte stack frame unchanged. Positive exceptional cleanup adds two memory reads; total native size grows 303 to 304 bytes and IL 168 to 176. Register renaming and instruction addresses do not establish a performance improvement. No per-event work reduction addresses the dense64 loss, and no causal explanation of the original slowdown is claimed.

Independent review agrees to stop rather than time that spelling/layout variant. The native diagnostic has no Mono or performance result. Original warm gains, all adverse observations and cold uncertainty remain preserved; no version is counted.
