# Selected bridge split factor

Exact candidate Runtime DLL:1c032a39cf571ba4ef80ff22c12dd99049b01e1ce0a2cd34040868d899188fa3. The exact-membership kernel, builders, hardware-gated count helpers and query evaluator bodies are unchanged from v3. The change is a small inlined range dispatch with separate Dense and Micro helpers.

The same 54 fixed-actor/hot-query cells per backend were measured three ways in each of three bracketed rounds: existing legacy public API as A/A, old-v3 Direct bridge, and split Direct bridge. Every sample is valid and every operation remains0B, including charged remove→match→add→match. Minimum actual samples are 5.01 ms default and 5.33 ms nohw.

| Comparison | Backend | Median wins | Regressions | Uncertain/small |
|---|---|---:|---:|---:|
|split / old Direct|default|10|0|44|
|split / old Direct|nohw|19|0|35|
|split / legacy API|default|14|0|40|
|split / legacy API|nohw|25|2|27|

All 54 cells remain in the report; selection is based on the whole matrix, not only parent-miss. The two remaining nohw losses versus legacy are8-member exact-hit in Micro/Auto, about7.7%/6.8%; neither is a clear regression versus the old Direct bridge. The previous empty-query concern remains historical evidence and is not silently removed, but it does not pass this factor's regression gate.

For 4096-member Dense/Auto parent-miss, the split is about0.70–0.72×legacy and0.56–0.72×old Direct in these paired runs. Source representation and fixed-actor/hot-query limits remain explicit. Charged mutation improvements still include different Sparse/Micro insertion/removal costs and are not attributed solely to frozen evaluation.

Native evidence explains a concrete code-generation improvement: the default Dense helper is 216 bytes and now has pre-loop range guards plus a 17-byte fast interior loop without per-word bounds checks, with a checked fallback loop retained. The historical f98c4538 v2 capture of the old combined Direct method is 601 bytes and its 33-byte interior loop keeps per-word checks; its AnyInRange source remained unchanged in v3, but this native capture is specifically from v2. EvaluateDirect grows from 450 to 510 bytes because the small dispatch inlines; evaluator semantics remain unchanged. Nohw captures also retain separate helpers and explicit safe range handling.

Recommendation: retain the split candidate. The full reference-host query/builder/mixed cohorts against the exact admitted baseline are complete; see FINAL_PERFORMANCE_READOUT.md. The prior broad v2 cohort and targeted v3 count evidence remain separately linked and are not relabeled as this final DLL's measurements.
