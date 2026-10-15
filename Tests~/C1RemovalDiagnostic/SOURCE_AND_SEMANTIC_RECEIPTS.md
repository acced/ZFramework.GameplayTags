# C1-removal source and semantic receipts

## Exact source scope

R contains all 16 Runtime files at `792ce83f1c2a9aeaadc045c192d70081d866d903`, including assembly definition and Unity metadata. Its git Runtime tree is `1e42d9659e26e385b267f877a9e3e493ce0eb916`. The checked-out production Runtime is byte-identical.

RMinusC1 replaces only the body of `GameplayTagContainerAlgorithms.CopyValidated` with the exact implementation from `352d304299ecbb7486e2c6a56ba71874c90fe21b`. All seven C# files in the existing `Tests~/C12Correctness/Baselines/PR4/Runtime` snapshot were independently compared against that historical commit. The replacement body is byte-identical to its PR4 source. The method signature, C2, every other method, and every other Runtime file remain byte-identical to R.

- R algorithms file SHA256: `55d8352d53a3528a75181a4fe02d3796b41e4cf177716235dd7d85964c1ea9f6`
- RMinusC1 algorithms file SHA256: `d3c1a98223da310d7cfef0d7c61a7e5b8716b27322048f3a20e1a2f7ea8b24ea`
- R CopyValidated body SHA256: `9914b6309c6b90cc61bafffd7a4216e4383179ef06b9832e2c02bcab16f190a6`
- RMinusC1 CopyValidated body SHA256: `34540b34b313f5a94a8b933304afe08df883798af7ba91a438555ebd1d3d773d`

`source_pins.json`, `source-proof.json`, `CopyValidated-removal.patch`, and `verify_sources.py` provide the full inventory and read-only source proof. The default verifier works from a source archive; `--history` additionally checks the exact git objects when available. An independent review cleared the literal one-body source delta and portable correctness gates before measurement.

## Completed correctness checks

Both R and RMinusC1 passed the unchanged suites with separate build and intermediate directories:

- Roadmap: 1,002,001 assertions each, including exact-fit containment/representative checks and core capacity probes.
- C12 supplement: 197,333 assertions each, including copy identities, warning callback behavior, aliases, source mutation, initialization, retained capacity, and cleared trailing references.
- .NET Standard 2.1 / C# 9 compatibility compilation: both passed.
- Roadmap differential transcript, equal for both: `6EA12A348CF620CDC4FC89C09BEC0C8933A7B22A8542A2214F0CB4CE011A948F`.
- C12 differential transcript, equal for both: `7D22FEB7F987696BD817E126DD29BABFF5F0DCFE622C617391FA1E2D8B053BA8`.
- Before/after Runtime inventories are byte-identical. The runner verifies the pinned suite/source inputs before and after execution.

The runner selected SDK `8.0.425` using output-local `global.json` with `rollForward` disabled. It did not rely on the host default SDK. These are untimed behavioral and compatibility checks; they establish no performance result and do not certify Unity Editor/Player, Mono, IL2CPP, or device behavior.

The public correctness workflow addition reuses this runner and performs no measurement. The collection outcome is reported separately after all raw evidence is available.
