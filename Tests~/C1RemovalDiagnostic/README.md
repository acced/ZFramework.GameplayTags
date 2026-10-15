# C1-removal diagnostic: mixed results, investigation stopped

The [completed outcome](OUTCOME.md) leaves the cause unconfirmed and production Runtime unchanged. Lifecycle 1024 and retained restore-plus-copy each had three slower pairs and one faster pair after removing C1. Prepared single-copy had three faster pairs and one slower pair, but all eight samples were short at the fixed pool cap. Small-copy losses and all C2/lifecycle guards remain in the report. No rollback, follow-on variant, favorable rerun, profiling, or repeated A/A is authorized by this result.

R is exact Runtime at `792ce83f1c2a9aeaadc045c192d70081d866d903`. RMinusC1 changes only the `CopyValidated` body to exact `352d304299ecbb7486e2c6a56ba71874c90fe21b`; C2 and all other Runtime bytes remain unchanged. See [source proof](source-proof.json), [exact patch](CopyValidated-removal.patch), and [source/semantic receipts](SOURCE_AND_SEMANTIC_RECEIPTS.md).

## Inspect the preserved evidence without running .NET

Run from the repository root with Python 3 and SHA256 utilities available:

```sh
H="$PWD/Tests~/C1RemovalDiagnostic"
(cd "$H" && sha256sum -c SHA256SUMS)
python3 "$H/verify_sources.py"
python3 "$H/read_evidence.py"
(cd "$H/Evidence/TimedHarness" && sha256sum -c measurement-inputs.sha256)
```

The evidence reader verifies 145 invocations, 121 numerical samples, all original stdout/terminal/stderr byte ranges, fixed counts, and frozen inputs. It recreates every numerical CSV byte-identically in a temporary directory using the frozen Python summarizer; it invokes no .NET target. An optional `--label` argument, using an exact label from [call-index.jsonl](Evidence/call-index.jsonl), prints that original stdout record.

The source verifier works from a source archive. If both historical git objects are available, `python3 "$H/verify_sources.py" --history` additionally verifies the exact historical trees and PR4 baseline. It requires no network access.

## Reproduce correctness separately

Both sources already passed the original Roadmap and C12 suites, equal differential transcripts, and .NET Standard 2.1 / C# 9 compilation. [Semantic logs](Evidence/Semantics/) preserve the completed results. With installed SDK 8.0.425, choose a new absolute output directory whose parent already exists:

```sh
H="$PWD/Tests~/C1RemovalDiagnostic"
DOTNET=/absolute/path/to/dotnet \
  bash "$H/correctness.sh" /new/absolute/c1-removal-correctness
```

The runner creates an output-local `global.json` pinned to SDK 8.0.425 with `rollForward` disabled, then runs all dotnet commands from that directory. It does not depend on the host's default SDK, download an SDK/package, or invoke the measurement harness. Build/intermediate outputs are separate for each source and suite; input/source hashes are checked before and after, and each suite's transcripts must match. CI runs this correctness path only, without timing thresholds.

## Frozen measurement protocol and publication identity

The [protocol](PROTOCOL.md) and [12-cell grid](cells.json) define the completed bounded collection: 24 fixtures, up to 48 fresh R-only calibration pilots, four AB/BA/BA/AB pairs per cell, at most 168 target launches, and one 600-second combined budget. The actual collection used 24 fixtures, 25 pilots, and 96 formal samples. All validated and exited zero. Eight formal samples and fourteen pilots were short; every short is retained. See [quality](Evidence/QUALITY.json), [all paired observations](Evidence/paired.csv), [absolute timings](Evidence/absolute-summary.csv), and [separate prepared-copy results](Evidence/single-copy-diagnostic-paired.csv).

The prepared pool used at most 16,384 targets with 128 MiB backing payload and a 144 MiB setup-allocation ceiling. Setup was outside counters and every prepared target was used once and postvalidated. Actual setup GC and memory/cache context are reported separately from timed copy counters. These observations are not a pure isolated-kernel result or a historical causal reconstruction.

The original measured README, protocol, manifest, and executable inputs remain byte-identical under [Evidence/TimedHarness](Evidence/TimedHarness/), identified in [Evidence/IDENTITY.json](Evidence/IDENTITY.json). The original historical input manifest is preserved under `Evidence/TimedHarness` and verifies that directory. The root `measurement-inputs.sha256` differs only in the hash of this updated publication README and verifies the current root files. This README and the outcome report were written after collection. The final [packaging identity](PACKAGING_IDENTITY.json) records publication differences. No executable inputs, Runtime sources, counts, or raw numerical/terminal evidence were changed for publication.

This investigation is stopped. A future timing run would require a new explicit approval and review of the frozen source/protocol/semantic gates; `--reviewed` is only an operator assertion and grants no authority. For that separately approved case, the preserved runner syntax is:

```sh
H="$PWD/Tests~/C1RemovalDiagnostic"
DOTNET=/absolute/path/to/dotnet DOTNET_SDK_VERSION=8.0.425 \
  bash "$H/run_bounded.sh" --reviewed /new/absolute/build /new/absolute/evidence
python3 "$H/summarize.py" /new/absolute/evidence
```

Never run the summarizer over the already packaged evidence directory: existing outputs are preserved and overwrite is refused. No concurrent build/test/benchmark/diagnostic is allowed during a newly approved collection. Failures or budget exhaustion preserve partial evidence and end that collection; do not retry, resume, change counts, or replace samples.

This .NET 8 Linux diagnostic does not establish Unity Mono/IL2CPP or device performance.
