# Existing-API Union strategy supplement

This is a separate frozen comparison of original preexisting public API compositions against accepted reusable Union V1. It preserves the existing factory+Copy controls and makes no performance/acceptance claim before a separately authorized measured run. Read SCOPE.md before interpreting results.

Pinned default variants use original f7c99546 and accepted f53c5fb, with literal left/right Copy+AddTags, a frozen seed/alias-aware composition, Clear+AddTags+AddTags, both original factory+Copy controls, and accepted output Union. Every row reports the operation it actually executes, including factory fallback. Protocol version 4 uses all five original-f7 strategies for slowest-original-per-row calibration. Accepted/candidate measurements never set iteration counts.

Untimed correctness/build smoke:

    source /tmp/gameplaytags-env.sh
    python3 run-default.py --dotnet /tmp/gameplaytags-dotnet/dotnet \
      --out /absolute/new-net-output --test-only

Add --mono /tmp/gameplaytags-mono/bin/mono for standalone Mono. Run backends separately. No SDK or runtime is installed by this package.

A future explicitly authorized full measurement:

    python3 run-default.py --dotnet /tmp/gameplaytags-dotnet/dotnet \
      --out /absolute/new-measured-output --samples 14 --batch-ms 200

Each process-cold fixture runs in a fresh child process, three replicates by default; --cold-only omits warm timings. --skip-cold runs only warm timing and does not establish cold behavior. --iterations-file accepts an exact complete frozen map. For custom explicitly labelled subsets, invoke run.py with --variant LABEL=SOURCE, --revision LABEL=40hex, and --strategy LABEL=STRATEGY once per label. No default variant should be silently removed from a published best-existing-strategy comparison.

Outputs:

- environment.json and preliminary-environment.json: source/harness/binary provenance and failure-safe command records
- command-logs/: untruncated raw stdout/stderr, including failures
- *-tests.txt: untimed semantic guards, real runtime identity, route and complete-owned-array hashes
- fixtures-and-routes.json: immutable row domain and actual strategy route
- cold/: fresh-process first-call and warm-operation-only allocations plus owned-state snapshots
- raw/: paired warm reports, fresh-result loop allocations and retained cohorts
- iterations.tsv and comparison.json: shared count map and per-row pairwise review statistics

Before a measured run, run verify.py and verify the supplied MANIFEST.json SHA-256 independently. The runner validates it too. Validation evidence is kept under validation/ outside the immutable package file set. A passed smoke is not the full repository suite, Unity, IL2CPP, performance evidence or a universal noninferiority certificate. Count remains 1/10.

## Protocol 4 resource plan and exclusions (unchanged from protocol 3)

The full default run has 43 semantic/owned-state fixtures, 45 timed rows (33 core, 12 deep diagnostic), four retained-result rows, and 41 process-cold fixtures. The two excluded cold fixtures are fallback/incoherent-enumerator and fallback/stateful-indices. They remain explicit correctness/owned-state fixtures with verified factory fallback and getter behavior, but do not describe a repeatable warm operation. They are also excluded from warm timing. fixtures-and-routes.json records those exact exclusions; this is not full cold coverage of all 43 semantic fixtures.

All five original-f7 strategies run a predeclared pilot. Per row, the largest original ns/op determines one shared count map; no accepted/candidate timing feeds calibration. The deep pilot uses two measured calls and four warmup calls; core pilots use 128 measured calls and 64 warmups. Deep shared counts are capped at 4096 for depth128, 64 for depth1024 and 8 for depth3000. Each deep full-prefix append batch therefore has roughly 34–36 million possible ancestor-path visits at the cap, plus warmup and setup work. Fast deep rows can be short. All deep rows are labelled diagnostic and cannot establish acceptance/noninferiority, even when a ratio is large. No actual batch-duration guarantee follows from the pilot. flat1024-focus.json exposes the meaningful normal-size comparison without dropping other rows.

Per backend, default measurement work includes 1353 cold processes (41 x 11 x 3), five original-only pilot processes and 154 paired warm processes (11 x 14), plus 11 builds, 11 correctness processes, 11 fixture-list processes and three host inventory commands: 1548 child commands total. Registry/source construction repeats inside each executable. Each benchmark process also includes 64 core/four deep warmups per timed row, fixture construction and assertions, GC boundaries, and four 32-object retention cohorts. The 159 pilot/paired processes build 20,352 retained-cohort outputs in total. These costs are included in the budget; the measured loops alone are not the run's cost.

The default whole-run bound is 10,800 seconds (three hours) and each child is limited to 300 seconds, further reduced to remaining whole-run time. Budget accounting starts before builds and covers cold, pilots, all warmup/cohort/setup work inside children and final validation. Expiry fails the run and retains partial artifacts; rows are never silently skipped. Final failure logging/auditing is still attempted. Use --time-budget-seconds and --child-timeout-seconds only as a separately reviewed execution-budget change. These are hard bounds, not measured runtime estimates. The parent CI must always upload the output directory on success, failure, or timeout.

--test-only builds, runs untimed correctness and emits fixture/route/owned-state observations. It never invokes cold, pilot or timed modes. --cold-only omits every pilot and warm timing process. Default measurement requires exactly one original-f7 variant of each of the five old strategies, so custom subsets cannot accidentally calibrate from accepted/candidate performance. A complete reviewed --iterations-file override remains subject to all predeclared deep caps and is hashed independently.

Protocol 1 and 2 were retired before any cold/allocation/timed execution and are preserved under validation/freeze-v1 and validation/freeze-v2. Protocol 3 changes were predeclared before measurements: five-original calibration, bounded deep pilot/warmup/counts, explicit cold exclusions, and runner/child time bounds. The original repository UnionOutput fixtures remain untouched.

## Protocol 4 dispatch fairness and cold boundary

The legal domain/strategy route is selected once during fixture setup. Every measured strategy invokes a delegate with the identical signature (GameplayTagContainer output, IGameplayTagContainer left, IGameplayTagContainer right). Literal Copy+AddTags and Clear+AddTags chains no longer pay an avoidable per-operation health/fallback branch. The seed heuristic still pays its real type/count decisions, and the alias heuristic still pays real object-identity checks and cache clearing. No actual source getter is captured, suppressed or replaced.

Creating the route delegate occurs before both cold allocation markers for every strategy. The first cold marker measures the first selected-operation invocation, not route/delegate creation. Delegate creation can affect runtime/JIT/cache state, so this is explicitly a first invocation after uniform delegate setup, not an assertion that no related code has ever been resolved. No selected operation or Union call is prewarmed. Source/registry construction and alias restoration retain the previously documented boundaries. The original frozen generic UnionOutput comparison remains separate: interface-typed operands and delegate dispatch here may generate different code from a concrete-typed caller, and this supplement does not establish a universal best-old-API result.

Protocol 3 is preserved under validation/freeze-v3 and passed all 11 variants on both .NET and standalone Mono before retirement. Protocol 4 changes only domain-route dispatch placement, common delegate invocation and its cold-boundary disclosure. All strategies, fixtures, assertions, counts, calibration, diagnostic rules and resource bounds remain as predeclared. No protocol has executed a real cold allocation, calibration pilot or timing sweep locally.
