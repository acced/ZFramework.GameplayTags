# Repaired Union: fixed-map public review package

Status: **0 qualified optimizations**. Historical f53 qualification is reopened.
The exact repaired source has scoped semantic/cold results in `semantic-summary.json`;
CI timing and the broader gate remain pending. No universal performance or allocation
claim follows from this package. No new measurement was used to prepare this derivative.

## Source and operation identity

`Program.cs`, `Comparator.csproj`, `candidate.patch`, all Runtime source bytes and
both maps are unchanged from the frozen twelve-variant experiment. Only supporting
provenance/documentation and disclosure logging changed. The statistical comparison
function is unchanged. `source-identities.json` records repository-relative paths
and exact hashes for:

- Original: f7c99546dbf029d70896c0f05e5119b19ffd6d26.
- Historical f53: f53c5fb94c22c2750f54c33dcfdee443a72bf595.
- Repaired: f53 plus patch SHA256
  d8f2ffe730a3462c35808f5d1959a8eaa8597240a774ec589400443e7b00ba89.

The repaired snapshot includes all 44 Runtime files; 21 are C# files. The other
23 metadata/assembly-definition files are authenticated but not measured.
At execution, exact C# sets and raw Git blobs are verified against both historical
commits and the repaired package path at GITHUB_SHA. Root Runtime is recorded only
as a Git tree identifier; builds use the package snapshots. Use a full Git checkout.

The five old strategies are literal left Copy+AddTags, literal right Copy+AddTags,
a frozen seed/alias-aware strategy, Clear+AddTags+AddTags, and factory Union+Copy.
They run on original and historical f53; historical output Union and repaired
output Union complete twelve variants. Unsafe alias, proxy, stateful-getter and
legacy-overflow cases are not relabelled as equivalent literal chains. Actual
routes and factory fallbacks are explicit in `expected-fixtures.json` and Program.
Public-semantic equality does not imply identical capacities or private layout.

The legal route is selected once during setup. Every measured operation uses the
same delegate signature with interface-typed inputs. Literal independent chains
pay no avoidable safety branch; real seed/alias decisions remain measured. Concrete
caller argument types can generate different code. Alias rows include the required
restore pair. Cold first-use excludes fixture/delegate construction and does not
claim pristine JIT state. The separate historical generic API benchmark is unchanged.

## Fixed traversal and cold gate

All twelve variants traverse 45 timed rows and four 32-output retained cohorts,
for fourteen rounds. Even rounds use the fixed forward order; odd rounds reverse
all twelve. There are no new pilots, calibration, row omissions or adaptive reruns.
Adding the repaired variant and omitting prior pilots change orchestration context,
not measured bodies or warmups. Previous eleven-variant evidence remains separate.

Before warm timing, collect **1,476 fresh cold calls/backend**: 41 fixtures times
three replicates times twelve variants. Complete the fixed cold stage even after
adverse byte/state observations. Then stop before timing if candidate cold or warm
bytes exceed any of the eleven controls, or FirstOwned/WarmOwned differ from f53
output. Per-control deltas, grouped minima and all adverse results are retained.
Structural failure, crash, timeout or changed immutable input fails immediately;
missing observations never become passes.

Two deliberately nonrepeatable fixtures, `fallback/incoherent-enumerator` and
`fallback/stateful-indices`, remain correctness-only and are excluded from both
cold and warm sampling. True-empty/dirty output results belong to the separate
semantic summary, not an expansion of the 41 cold fixtures. The summary retains
18 .NET pool-only review flags; this Program does not collect pool events. Local
Mono 6.12.0.199 evidence does not substitute for actual CI Mono 6.8.0.105 behavior.

`derivation/net.json` and `derivation/mono.json` contain only numeric inputs, relative
sample IDs and hashes. Seven rows use max(old count, ceil(500,000,000 / fastest
reported ns/op over five original strategies times fourteen rounds)): three n1024
warm overlaps and every alias/fallback-alias warm row. Other 38 counts are unchanged.
Candidate data never determines counts. `verify_derivation.py` checks the formula.
The fastest observation is not a median and cannot guarantee future 500 ms batches.

Deep depths 128/1024/3000 retain caps 4096/64/8 and four warmups; they remain
strictly diagnostic. Core rows retain 64 warmups. Short and adverse rows remain
visible. The unchanged comparison uses a 50 ms short-batch flag and approximate
per-row 99% paired intervals, with no familywise acceptance rule.

## Commands and execution requirements

Plan and verification commands start no compiler, runtime, cold or timing process:

```
python3 Tests~/UnionRepairedStrategies/verify.py
python3 Tests~/UnionRepairedStrategies/verify_derivation.py
python3 Tests~/UnionRepairedStrategies/coordinate.py --backend net --mode plan
```

After separate semantic/static review and execution authorization, supply a matching
`execution-clearance.json` using the example schema. It is outside the frozen
manifest so it can bind that manifest's final hash. Required review files must
match their declared hashes. Public clearance should contain only review status,
source/manifest hashes and relative review filenames.

```
python3 Tests~/UnionRepairedStrategies/coordinate.py \
  --backend net --mode measure --dotnet "$DOTNET" \
  --git-repository "$GITHUB_WORKSPACE" \
  --out "$GITHUB_WORKSPACE/.artifacts/union-repaired-strategies" \
  --clearance "$GITHUB_WORKSPACE/Tests~/UnionRepairedStrategies/execution-clearance.json"
```

For Mono use `--backend mono --mono "$MONO"`. `--mode test` performs only source/tool
checks, twelve builds, correctness and fixture listing; no cold or timed operation.
Snapshot commit defaults to GITHUB_SHA and package path to the checkout-relative
location. Supply explicit `--snapshot-commit` and `--snapshot-package-path` for an
external package. Output must be new/empty. No sample/count/variant overrides exist.

Required selected SDK: 10.0.401. Install SDK 8.0.425 as well to supply CLR 8.0.31.
Standalone Mono must be 6.8.0.105. Runtime drift fails for separate review. Tiering
and server GC remain disabled. Selected versions, CPU/affinity and required switches
are recorded, without full host inventory transcripts.

## Public disclosure and resource limits

The logging adapter executes the same commands with regular-file stdout/stderr.
Tool/build/Git diagnostic files are temporary and outside publishable outputs;
they are removed after reading. Those transcript contents are never persisted in
public reports. Their hashes cannot reconstruct the omitted diagnostic contents.
Numerical correctness/cold/warm reports remain unmodified, with
immediate and final hashes. Commands use package/repository/output-relative names
and `$DOTNET`/`$MONO`; binary hashes remain available. Error status/type/exit code and omitted-stream hashes
are retained without private traceback contents. This intentionally reduces failure
diagnostics and changes logging overhead; it does not alter measured operations.

**Upload only the paths in `PUBLIC-ARTIFACTS.txt`, including on failure/timeout.**
Never upload the entire output tree: unchanged build intermediates and PDBs may
contain machine paths. Per-variant bin/obj trees are excluded; their hashes remain.
The explicit upload allowlist preserves numerical/raw reports and failed-stage
status without redisclosing build metadata or full command environments.

All immutable source/map/completed-binary checks run before/after every child and
at exit. All 66 pairwise comparisons, including every repaired-versus-control pair,
are retained. Completion is capture completion, not performance acceptance.

The enforced runner budget is 10,800 seconds, including builds, cold, setup, warmup,
cohorts, loops, validation and logging; each child is bounded by 300 seconds and
remaining runner time. A 190-minute CI job allows final upload. Partial runs fail.
Resource projections in `resource-estimate.json` are conditional, not candidate
measurements: historical-output reference roughly 42.4/49.8 minutes (.NET/Mono),
with planning reserve 106.1/116.5 minutes. Logging-adapter cost is unmeasured.
Projected 4.34/2.13 TB is cumulative allocation churn, not live memory.
