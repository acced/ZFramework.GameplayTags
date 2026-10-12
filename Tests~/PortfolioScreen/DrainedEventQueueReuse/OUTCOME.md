# Drained event queue reuse: closed after repeated warmed .NET cost

**Decision: close this exact candidate without acceptance. Qualification remains 0/10.** The [fixed portability screen](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38310914293), run **38310914293** at head **576579c725529960910daaaca09ba31141a276b5**, reproduced the memory saving but triggered its predeclared .NET warm-cost rejection gate. No further source variant, timing round or qualification is queued.

On .NET, the already-grown-original chain1025 comparison is slower in all four candidate pairs, by **5.07%–6.69%**, with every original and candidate batch above **282 ms**, zero measured allocation and zero GC. Chain9 is also slower in three of four pairs. The frozen rule closes when any warm row is slower in at least three of four pairs; it was selected before this run. This is a descriptive rejection, not a statistical regression estimate or a claim about its cause. Smaller retained queues and avoided first-use allocations do not cancel the observed recurring cost.

Mono completed with mixed warm directions and no row reaching that rejection threshold. Its completion is not optimization acceptance and does not override the .NET rejection. Earlier cold uncertainty remains intact. Root Runtime is unchanged.

## Complete fixed sequence and semantic evidence

Each backend completed exactly **two builds, two untimed semantic targets, 40 fresh cold calls and eight four-row warm processes**, with no pilot: 40 cold rows and 32 warm rows per backend. All 144 measured rows across both backends have valid operation/semantic records and exit code zero. All cold comparisons passed before warm timing was admitted. The .NET terminal state is `rejected / warm-cost-gate / CLOSE_REPEATED_OBSERVED_WARM_COST`; Mono is `complete / completed / REQUIRES_REVIEW_NO_AUTOMATIC_CONTINUATION`.

Both runtimes pass the unchanged core and focused queue fixtures, the R2 metadata gate, all 20 cold and 16 warm logical-state comparisons, and the intentional physical-capacity assertions. Metadata covers 38 Runtime types and 367 methods, with zero unrelated metadata/raw-IL differences; only the allowed Enqueue body changes. Each semantic target records 162 queue observations. The common complete semantic snapshot is 52,377 bytes, SHA-256 `092e52599aebee55d49bfe5dd39178d198a97cab07d04c61dd6654b2ce1c9626`.

Exact public callback order, captured multicast behavior, logical pending suffixes, storage/callback/source state, cleanup and recovery remain covered. Every retained event slot is checked default; queue indices finish at zero. Warm pre/post retained-array identities remain stable. Capacity and array identity differences between variants are intentional, so no full private-graph or resource-failure equivalence is claimed. The saved public reports contain snapshot identities and successful byte-equality checks; raw private snapshots are not uploaded.

Actual runtimes are **.NET 8.0.31** and **Mono 6.8.0.105**, the latter reporting Environment.Version **4.0.30319.42000**. Both have 8-byte pointers and measure GameplayTag and deferred-event managed sizes as 16 bytes in both variants. Stopwatch frequencies are 1,000,000,000 and 10,000,000 ticks/second respectively. SDK 8.0.425 and the frozen reference/dependency setup are used. These are standalone runtime results, not Unity Editor/Player or IL2CPP qualification.

## Cold allocation and retained capacities

Each table entry is identical in all four pairs for that shape. Bytes cover the whole first AddTag plus synchronous reentrant dispatch, with holder/registry/delegate/trace/source setup outside the boundary. The first bridge/runtime compilation belongs to the cold call. All **80 cold GC0/GC1/GC2 triples are (0, 0, 0)**. No paired allocation increase occurs.

| Shape | .NET original → candidate B (delta) | Mono original → candidate B (delta) | Event slots original → candidate, both runtimes |
|---|---:|---:|---:|
| chain-1 | 360 → 360 (+0) | 552 → 552 (+0) | 8 → 8 |
| chain-8 | 360 → 360 (+0) | 552 → 552 (+0) | 8 → 8 |
| chain-9 | 640 → 360 (-280) | 840 → 552 (-288) | 16 → 8 |
| chain-1025 | 65,832 → 360 (-65,472) | 66,088 → 552 (-65,536) | 2,048 → 8 |
| full-with-pending | 1,072 → 1,072 (+0) | 1,288 → 1,288 (+0) | 16 → 16 |

The chain1025 first-use saving is **65,472 B on .NET** and **65,536 B on Mono**; chain9 saves 280 B and 288 B respectively. The retained queue falls from 2,048 to 8 slots for the long chain. Capacity is structural evidence, not a whole-heap byte measurement; cumulative growth allocation includes transient arrays. The full-with-pending control preserves the original resize and equal allocation/capacity.

## Every cold latency pair

Pair order within each row is **AB, BA, BA, AB**, where A is original and B is candidate. Ratios above one are adverse. Each duration is one complete fresh call in milliseconds. Tables retain all pairs, including outliers; exact ticks/frequencies and unrounded ratios remain in the hashed raw reports.

### .NET 8.0.31 cold

| Shape | Original ms, pairs 1–4 | Candidate ms, pairs 1–4 | Candidate/original, pairs 1–4 | Slower |
|---|---|---|---|---|
| chain-1 | 4.656889, 4.626918, 4.695971, 4.735065 | 4.780626, 4.714292, 4.745390, 4.675523 | 1.026570742829, 1.018883844494, 1.010523702127, 0.987425304616 | 3/4 |
| chain-8 | 4.641931, 4.603740, 4.580787, 4.624248 | 4.642863, 4.694680, 4.736588, 4.681454 | 1.000200778512, 1.019753504759, 1.034011841197, 1.012370876302 | 4/4 |
| chain-9 | 4.673620, 4.670544, 4.608369, 4.598437 | 4.619048, 4.670423, 4.659684, 4.649062 | 0.988323398137, 0.999974092954, 1.011135176024, 1.011009175509 | 2/4 |
| chain-1025 | 4.685630, 4.691751, 4.702647, 4.710922 | 4.648060, 4.629825, 4.642259, 4.694282 | 0.991981867967, 0.986801089828, 0.987158721460, 0.996467782740 | 0/4 |
| full-with-pending | 5.209383, 5.115598, 5.100620, 5.226716 | 5.128082, 5.175860, 5.118213, 5.120658 | 0.984393353301, 1.011780049957, 1.003449188530, 0.979708482343 | 2/4 |

### Mono 6.8.0.105 cold

| Shape | Original ms, pairs 1–4 | Candidate ms, pairs 1–4 | Candidate/original, pairs 1–4 | Slower |
|---|---|---|---|---|
| chain-1 | 1.744800, 1.103700, 1.131500, 1.097300 | 1.093400, 1.097800, 1.136300, 1.123100 | 0.626662081614, 0.994654344478, 1.004242156430, 1.023512257359 | 2/4 |
| chain-8 | 1.134200, 1.108200, 1.119500, 1.171500 | 1.117100, 1.150800, 1.224100, 1.150400 | 0.984923293952, 1.038440714672, 1.093434569004, 0.981988903116 | 2/4 |
| chain-9 | 1.177300, 1.139100, 1.151600, 1.166400 | 1.171600, 1.185400, 1.159600, 1.161400 | 0.995158413319, 1.040646124133, 1.006946856547, 0.995713305898 | 2/4 |
| chain-1025 | 1.218500, 1.211300, 1.254300, 1.245200 | 1.382800, 1.259200, 1.185600, 1.201700 | 1.134837915470, 1.039544291257, 0.945228414255, 0.965065852875 | 2/4 |
| full-with-pending | 1.228500, 1.278800, 1.267100, 1.184000 | 1.290600, 1.259000, 1.287500, 1.236500 | 1.050549450549, 0.984516734439, 1.016099755347, 1.044341216216 | 3/4 |

.NET cold chain1 and chain8 remain adverse in 3/4 and 4/4 pairs; Mono cold pending is adverse in 3/4. The Mono first original chain1 call at 1.744800 ms is retained. New .NET chain1025 cold ratios below one do not erase the earlier local cold costs or explain their cause. No cold-latency pass is claimed.

## Already-grown-original warm comparison

Both variants receive 128 complete setup cycles through the same public callbacks. The original keeps its naturally grown queue; candidate keeps its natural capacity. Each measured cycle includes AddTag and full public restoration, every restoration notification, observer order/checksum work and bookkeeping. There is no subtraction. Chain rows restore with RemoveTag(A, N); pending removes the leaf and then Z. Every cycle ends empty without count accumulation. Pending at capacity 16 is an ordering/workflow control, not repeated growth.

All **64 warm batches allocate 0 B** (therefore 0 B/cycle), and all **64 GC0/GC1/GC2 triples are (0, 0, 0)**. Capacities stay constant in every pair:

| Row | Shared cycles/batch | Notifications/cycle, including restoration | Original → candidate event slots |
|---|---:|---:|---:|
| single | 3,655,224 | 2 | 8 → 8 |
| chain9 | 906,602 | 10 | 16 → 8 |
| chain1025 | 9,309 | 1,026 | 2,048 → 8 |
| pending | 547,456 | 18 | 16 → 16 |

Counts were frozen from the fastest of four preserved **original-only** local observations, using integer ceiling to target 250 ms, shared across backends with no CI pilot. Candidate data did not select counts. Every warm interval in this run exceeds 250 ms: minimum **282.266791 ms on .NET** and **333.052700 ms on Mono**. This does not retroactively lengthen earlier short local samples.

### .NET 8.0.31 warm

| Row | Original batch ms, pairs 1–4 | Candidate batch ms, pairs 1–4 | Candidate/original, pairs 1–4 | Slower |
|---|---|---|---|---|
| single | 297.789812, 295.293871, 295.938851, 294.958072 | 297.390415, 295.666051, 295.934878, 294.375524 | 0.998658795621, 1.001260371571, 0.999986574929, 0.998024980310 | 1/4 |
| chain9 | 296.590538, 302.156787, 295.548925, 296.258461 | 302.625319, 298.652646, 299.655897, 300.247752 | 1.020347179788, 0.988402904880, 1.013896081672, 1.013465576600 | 3/4 |
| chain1025 | 283.113054, 282.266791, 282.999345, 283.199287 | 297.511231, 296.579541, 298.605715, 302.142170 | 1.050856634113, 1.050706460896, 1.055146311381, 1.066888879561 | 4/4 |
| pending | 293.627635, 301.384906, 302.532881, 293.666068 | 295.068441, 294.588924, 293.982100, 297.428566 | 1.004906915522, 0.977450821641, 0.971736027596, 1.012812164598 | 2/4 |

### Mono 6.8.0.105 warm

| Row | Original batch ms, pairs 1–4 | Candidate batch ms, pairs 1–4 | Candidate/original, pairs 1–4 | Slower |
|---|---|---|---|---|
| single | 434.307300, 439.866400, 465.272100, 452.764700 | 441.414000, 440.984300, 436.683900, 443.312700 | 1.016363298522, 1.002541453496, 0.938555954677, 0.979123814202 | 2/4 |
| chain9 | 390.316400, 384.552100, 401.620600, 400.469800 | 391.832600, 402.764500, 390.677700, 393.719700 | 1.003884540849, 1.047360032620, 0.972753140651, 0.983144546730 | 2/4 |
| chain1025 | 349.507700, 337.935900, 333.052700, 370.554400 | 346.681300, 348.489200, 353.484900, 351.011500 | 0.991913196762, 1.031228703432, 1.061348249091, 0.947260375265 | 2/4 |
| pending | 365.607600, 367.778200, 363.352900, 372.396700 | 373.733800, 361.222700, 362.790400, 365.028800 | 1.022226562030, 0.982175398107, 0.998451918232, 0.980214915975 | 1/4 |

The .NET chain1025 and chain9 rows trigger the frozen gate without relying on allocation, GC or short-batch adjustments. Mono warm slower counts are 2/4, 2/4, 2/4 and 1/4; these mixed observations remain visible and do not establish cross-runtime latency equivalence. Backends and assembly contexts are not pooled.

## Preserved earlier evidence

- The first local collector stage remains **STOP/incomplete** because its mid-process native copies were truncated. Its successful semantic evidence never repaired the missing native provenance.
- A separately authorized collector-only repair passed complete local natural captures: Enqueue grew from 121 to 142 native bytes; QueueChange stayed 203 bytes with four out-of-line Enqueue calls, and all eleven other captured contexts normalized equal. This CI stage performs **no native capture** and makes no new native-inlining portability claim.
- Earlier local cold chain8, chain1025 and pending were each adverse in 3/4 pairs. Chain9 included ratio 1.546549945 and chain1 included 1.444307223. Those observations remain in [the complete local evidence](LOCAL_EVIDENCE.md).
- The earlier warmed-holder test did not reach its rejection threshold, but retained 22/32 intervals below 200 ms, minimum 178.837203 ms. This separate combined-assembly replication does not replace those samples or establish a cause for their mixed signs.

The source/collector fixes and all prior source/build/result bindings remain frozen. The candidate is now closed/held under the specified gate. No added sampling, code rearrangement, alternate guard, source variant or full qualification is authorized by these results. Memory savings remain real scoped findings; acceptance and a latency pass remain false.

## Source, map and artifact identities

- Base Runtime revision: `f7c99546dbf029d70896c0f05e5119b19ffd6d26`.
- Executed public package seal: `281b3069c07486c9de1ad24550814129101f08666c95fe4d1ce15ed352b72858`.
- Shared count-map SHA-256: `043f65932520af6d4eb064fb7b5e62468430459831477d4c1643ad5dcd204fdd`.
- Original GameplayTagCountContainer.cs SHA-256: `2507f999f41b18019d3a5478f9d42599792dfd7a0e517730309e54cfa99ee858`.
- Candidate GameplayTagCountContainer.cs SHA-256: `b50388ee614ea4431630683bffe586e106c4c905982437bc41b273f7b13c87f2`.

The complete 44-file original/candidate Runtime maps in both provenance reports match the frozen source pins exactly. Every extracted artifact member was byte-compared with its saved ZIP entry: exactly the eight allowed reports per backend, with no extra members. Public artifacts carry numerical/boolean/hash evidence, validated runtime identities and fixed failure markers; raw commands, environment dumps and private target output are not published.

### .NET identities

- Saved artifact ZIP SHA-256: `62527bb00b2f98464fde7c76bd67002153f2e53c09aa2fbe914981625d5a8030`.
- Toolchain-manifest digest: `746fe6b7489cd3a7f19a00deae9fb46eb494410b84a9140c12ad755e905221f7`.
- Original Test.dll: `f4402b46dc6eb0b594504e6341aa1441278473c478f746d1e60f519c2202c64a`.
- Candidate Test.dll: `b098c7fc2edd104a168ef8365433c27344495ea35f29bcc74edf012959f715f2`.

| Allowed report | SHA-256 |
|---|---|
| config.json | `99908ede44a0b9956d7c054c3391b52d22f5024be4e7c33c766e974c6d5a593d` |
| correctness.json | `4e43a9f02721bcfb4a2bd8af1462b3dfc71c7499777a315bdf48535b53bbb2ea` |
| count-map.json | `043f65932520af6d4eb064fb7b5e62468430459831477d4c1643ad5dcd204fdd` |
| provenance.json | `f1c299440a5b0c75c4302963d6c4501853d1a9956360b9586378116241cfba28` |
| raw/cold.json | `1f6ceebeaa2e63f4f8e429fece2de5174531ae306e931e2f7731879d6c839080` |
| raw/warm.json | `b9a3e60fe3336b28171099a6626a57d1b59ecca9bdc8f57bce27d5a45703fd9d` |
| status.json | `cad1960a2fe7af862eed7006fa580ada6a7fea4a7553b95b1284c58cbe9a19af` |
| summary.json | `452db3cec5c9a0da84a823e2c3a66f0acae8277a042f63b6d9150ded4c4690cc` |

### Mono identities

- Saved artifact ZIP SHA-256: `998c549c7b2a176051a47b44fb3209718d3a9ff62a0fe22c0dbb52cf484f34be`.
- Toolchain-manifest digest: `62d029979dd416c84e73b20cb8e9c110c5dcd7da875f2beda3f06018d5cce0d6`.
- Original Test.exe: `de7f5671ff94f4828716b87a1e541e5ccc09910451728cde968d162c72840839`.
- Candidate Test.exe: `f0cbe6af72204487c61aa84f8d99b81b3b1901404d8ac6a04140caf702ed04d6`.

| Allowed report | SHA-256 |
|---|---|
| config.json | `c5314c216e9b58698b45db5e09596c6c233c8831768d43b42038369bd456ab6a` |
| correctness.json | `c1a7c22f36af883f9dbca5475b27bdc8e942d6849ba808235ddc8947b60e2904` |
| count-map.json | `043f65932520af6d4eb064fb7b5e62468430459831477d4c1643ad5dcd204fdd` |
| provenance.json | `789a0bb112564fd95b9338093322919f26b0b0ef83b5ef3cd05244a3c7259b23` |
| raw/cold.json | `dcceca661071e8ee8ca802ae36afcb78b85311fc961d5d226068982948566f97` |
| raw/warm.json | `f9b88fc0004a88979852e79c3a2a8fc6a4dbd154aea5f81f6b6a818d01ad95dc` |
| status.json | `72a00b0a578751b826a5bdf56fe0c685a265eea08ba5601c528765d758127e51` |
| summary.json | `b2ab531b348b12edfadb91ceb0f9d1df0490fb28233e5ab61e037a5a343059f2` |

The tool manifest covers the pinned .NET compiler/runtime/reference inputs, Mono executable and copied package DLLs; it is not an attestation of every installed Mono framework/GAC/native dependency. Build hashes above are the recorded emitted identities; binaries and private metadata files were intentionally not included in the public ZIPs.

**Final disposition: closed/held, no latency pass, no optimization acceptance, qualification 0/10.** This report is a saved-evidence closeout, not a new execution.
