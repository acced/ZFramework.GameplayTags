# Preserved local evidence: memory benefit, latency unresolved

All local stages used .NET 8.0.31/x64. Qualification remains 0/10. This public portability package is a distinct combined-assembly context; its future .NET results cannot overwrite these observations. Mono has not been measured for this candidate.

The first semantic/native collector stage remains STOP/incomplete: its mid-process copies were exactly 40,960 bytes and truncated. Both targets had passed logical semantics, but buffered native output could not establish the required provenance. No result from that attempt was relabeled. A separately authorized collector-only repair removed the copy and sole forced PrepareMethod, reading complete natural dumps after process exit. It passed: Enqueue 121→142 native bytes, QueueChange 203 unchanged with four out-of-line Enqueue calls, all eleven other captures normalized equal. This was local .NET natural evidence only, not a portable native or latency pass.

Cold stage: two builds, 40 fresh targets, 20 exact paired state snapshots, zero GC and no paired allocation increase. Chain9 allocates 640→360 B, saving 280 B with capacity 16→8. Chain1025 allocates 65,832→360 B, saving 65,472 B with capacity 2048→8. Chain1/8 stay 360 B/capacity 8; pending stays 1,072 B/capacity 16. Physical capacity is not total retained-heap measurement.

Every cold pair ratio and time remains below. Pair order is AB, BA, BA, AB; ratio > 1 is adverse.

| Cold shape | Original ms 1–4 | Candidate ms 1–4 | Ratios 1–4 |
|---|---|---|---|
| chain-1 | 4.100097, 4.061389, 4.115812, 4.596004 | 3.947498, 4.538799, 5.944497, 4.187279 | 0.96278161224, 1.11754845448, 1.44430722297, 0.911069485579 |
| chain-8 | 4.842407, 4.054729, 3.921889, 4.033828 | 4.722726, 4.111936, 4.193589, 4.176112 | 0.975284811871, 1.01410871109, 1.06927784035, 1.03527269879 |
| chain-9 | 4.281541, 4.604678, 3.949341, 4.620612 | 6.621617, 4.55269, 3.987548, 4.166808 | 1.54654994545, 0.98870974257, 1.00967427224, 0.901787036003 |
| chain-1025 | 4.350715, 4.390551, 4.067459, 7.069652 | 4.423354, 4.771649, 4.462514, 6.679138 | 1.01669587642, 1.08679958392, 1.09712574853, 0.944761920389 |
| full-with-pending | 4.579951, 4.570927, 4.496185, 4.629055 | 4.692891, 4.548814, 4.776797, 5.492926 | 1.02465965247, 0.995162250458, 1.06241113299, 1.18661929919 |

Cold chain8, chain1025, pending were slower in 3/4 pairs; chain9 includes 1.546549945 and chain1 includes 1.444307223. No outlier was discarded and no causal/statistical nonregression claim is established.

The one warmed-holder falsifier used the already-grown original API with complete mutation/restoration costs included. Two builds, one original pilot, eight measured processes/32 rows; all 16 paired states exact, all pilot/measured allocated bytes 0 and GC triples 0. Capacities remain 8/8, 16/8, 2048/8, 16/16. Slower counts 2/4, 1/4, 2/4, 2/4 did not trigger the declared 3/4 closure rule. This is no latency pass. 22/32 intervals were below 200 ms, minimum 178.837203 ms, all retained.

| Warm row | Original batch ms 1–4 | Candidate batch ms 1–4 | Ratios 1–4 |
|---|---|---|---|
| single | 219.92862, 206.627773, 211.7485, 224.314736 | 226.45189, 219.617879, 206.386569, 217.206124 | 1.02966085087, 1.06286718291, 0.974677832429, 0.968309652202 |
| chain9 | 205.864231, 178.837203, 195.428295, 197.453006 | 185.371172, 187.193346, 180.839631, 183.917038 | 0.900453522691, 1.04672485847, 0.925350297919, 0.931447141402 |
| chain1025 | 186.87629, 189.214501, 187.015779, 187.099405 | 181.4474, 195.695283, 181.704374, 192.197912 | 0.970949284149, 1.03425097953, 0.97159916116, 1.02725025769 |
| pending | 183.735518, 189.118687, 190.869215, 212.120826 | 183.808525, 197.915173, 182.688116, 197.772176 | 1.00039734832, 1.04651304501, 0.957137671468, 0.932356241155 |

The fastest of the four ORIGINAL warm times, and only those original observations, supplies the frozen 250 ms shared portability counts. No candidate times or CI pilot are used. All original observations and hashes are in count-map.json. This longer distinct screen cannot retroactively make local short samples long, clear unresolved cold latency or explain mixed timing signs. R2 metadata/queue semantics remain mandatory. No Mono, Unity or IL2CPP behavior is inferred from local .NET results.

Source/build/result bindings for every stage are retained as cryptographic identities in source-pins.json; each result manifest binds its complete saved evidence. No local command paths, environment or raw failure text is published.
