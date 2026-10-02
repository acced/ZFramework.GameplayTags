# Mixed operations experiment (2026-10-02)

Base: PR14 documentation head f1d7243b0f9f8dffcb7963417b31ed47801c8ebc.
Generated controls must byte-match measured db0ffc25448a5df399651550acf627d210482aeb.

Four mutually exclusive builds: control, cursor-only, dense-result-only, combined.
Do not import all generated classes into Unity together. Production files are untouched.

The cursor loads Dense ulong words once and skips zero words, emitting only nonempty local
records. It adds an ulong to the temporary Records value (and its containing enumerator),
not to each stored set. This selected DirectArray baseline is Micro16, with its existing
2^20-ID admission limit. A local attempt to compile MICRO_WIDE failed in the unchanged,
uint-only SIMD query of BOTH the control and candidate. No wide-support claim is made and
the unrelated query algorithm is not modified in this experiment.
No concurrent mutation during enumeration is supported.

The dense-result specialization applies only after public owner/alias/empty/same-input
guards and the homogeneous Dense branch. Exactly one source is Dense. Copy it into the
independent output, OR local records and count only newly inserted bits. Every destination
word is overwritten, so dirty outputs need no Clear. No output shares a mutable buffer.
Existing same-input and in-place paths remain intact; an alias is not an independent result.

Count is eager. Reservations, ordinary copy capacity, growth and exception boundaries are
unchanged. No pool, COW, extra index, cached density or implicit conversion.
Cold start and native Unity/IL2CPP/Burst/device acceptance are not supplied by Linux .NET.

Benchmarks cover both source orders, same/mixed representations, output Micro/Dense,
independent Union, dirty UnionInto, actual copy-then-append/remove and reused copy-then-mutate,
ordered enumeration and exact lookup. Removal peer is the alternating existing half-subset.
Copies preserve source capacity; all algorithms use identical prepared source capacity.
Fresh allocations and Count are timed. Query and sufficient-capacity Into/mutation are 0 B.
All seven samples, GC collection counts and same-binary A/A are retained.
Pair lifecycles start from sorted input, pay preparation of BOTH operands and compare keep,
convert-one, convert-both and directly build-both. A sequence contains 0/1/16 independent
Unions, not accumulated state. No selector or per-row oracle is implemented.

Reproduce with SDK 8.0.425:
    python3 Audit~/MixedOperations/run.py --output artifacts/results --rounds 3
Archive HEAD to artifacts/source.zip before running review:
    python3 Audit~/MixedOperations/review.py --root artifacts

The primary full CI is authoritative. Local checks may use the preexisting SDK 8.0.423 /
runtime 8.0.29 and are separately labelled, never mixed with remote .NET 8.0.31 timings.
Three-round 5% gates screen candidates; they are not confidence intervals or proof of
uniform superiority. A/A instability and all negative results must remain.
