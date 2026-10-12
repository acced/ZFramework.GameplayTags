# Prior local rejection screens

These observations are exploratory and do not qualify either candidate. They remain adverse/uncertain evidence when interpreting the fixed CI screen; no samples were removed.

## Registry immediate-parent lookup

Four alternating pairs per backend completed with matching nine-case differential correctness output. Long shared-prefix construction saved substantial measured allocation. On .NET, the unique depth-two row was slower in all four pairs, with after/before timing ratios 1.21648, 3.18441, 1.36197 and 1.40349. Its paired allocation changes were mixed: -399.75, +246.5, -54.375 and +521.375 bytes/build. The unchanged root controls also showed large process dispersion, so this does not isolate the cause of the slowdown. Mono reported exactly equal allocation for that depth-two row in every sample, while unchanged controls also shifted materially. The fixed CI screen retains the shallow/root adverse controls alongside the shared-prefix gains.

## Hierarchical bulk snapshot avoidance

The focused correctness runs matched 468,567 checks and digest b76530c0ef95992e on both sources and both local runtimes. First-bulk snapshot allocation reductions were 56/280/4,120 bytes on .NET and 64/288/4,128 bytes on standalone Mono for small/64/1,024-tag cases. Warm allocation was zero. Most local timing rows were short, and unchanged manual controls shifted similarly to the affected operations. These results establish a work/allocation-reduction lead, not a timing pass. The CI screen preserves all 35 rows, including self, proxy and parent fallbacks.

Local Mono was 6.12.0.199. The CI Mono screen uses its declared 6.8.0.105 runtime. Neither is Unity Editor/Player execution. A completed four-pair screen is only a reason to consider full qualification; it cannot establish global non-regression or count as a successful optimization version.
