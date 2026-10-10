# Structural retention supplement

Compiles the frozen Evolution Program.cs unchanged and calls its exact registry and input helpers. Reconstructs its 24 fresh result/result+workspace retention factories and validates result semantics with the frozen validator. Does not replace full-GC measurements.

Reflection records reachable instance object identities (deduplicated within each graph), reference edges, array element types/ranks/lengths/lower bounds, string lengths, and recursive instance-field layout descriptors including value-type array elements. Static fields and the static registry are never traversed. Scalar values are irrelevant to storage shape and excluded. Empty shared arrays remain identified as reachable, not asserted to be newly allocated or exclusively owned. There is no inferred byte size, Marshal estimate, timing, or cross-runtime comparison.

Compare only within the same runtime/reference build. Identical layout and graph capacity descriptors establish equal managed storage structure, not an absolute GC heap size; differing arrays require explicit capacity review. Native runtime/JIT/allocator overhead and static retention are outside this bounded audit.

Run `python run.py --source SNAPSHOT --revision EXACT_SHA --out RESULT.json --variant LABEL [--dotnet PATH] [--mono PATH]`. Compile manifests include declared revision, exact source/reference/assembly hashes, resolved runtime executable paths, runtime version/.NET SDK information and commands. Baseline and candidate must use the same harness and runtime. No acceptance timing runs are launched.
