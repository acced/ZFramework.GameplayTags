# Historical lifecycle JIT diagnostics at 792ce83f

## Result

All three natural lifecycle validation captures passed. For the observed 1024-target ← 512-source lifecycle copy, the native caller explicitly calls CopyValidated in B11, C1, and current792. CopyValidated therefore remains out of line at this actual callsite in all three captures.

C1 and current792 have identical normalized CopyValidated listings, including their instructions, branches, helper calls, and reported 362-byte code size. B11's listing differs and is 211 bytes. The 151-byte difference is code-size evidence, not a speed or regression measurement.

No CPU/sample-stack or GC-pause result is available: the first EventPipe attempt failed during Unix-socket setup, before launching its workload. The approved elevated retry also failed during environment bootstrap before Python or the tracer ran. No further route was tried.

## Scope and validation

- Sources: immutable vendored B11, immutable vendored C1, and the seven Runtime files verified byte-for-byte against 792ce83f1c2a9aeaadc045c192d70081d866d903. The diagnostic sources exclude the experimental C3a candidate. Production Runtime was subsequently restored to current792 after the separate C3a experiment; this does not change these frozen diagnostic identities.
- SDK: verified Microsoft .NET SDK 8.0.425; runtime Microsoft.NETCore.App 8.0.31; Linux x64.
- Three identical Release harness builds succeeded with zero warnings/errors. Program.cs and UnityStubs.cs are byte-identical to the original C12Measurements harness at that commit; no Runtime edits, synthetic copy wrapper, or forced inlining/no-inlining.
- Three fresh-process validation-only JIT targets ran. Each executed 64 warmups plus one requested lifecycle, 65 lifecycles total. All returned PASS with sink 499265. No formal timing, allocated-byte, or GC-count sample was emitted.
- Tiering disabled, matching the existing protocol. Natural generated caller code was captured together with Runtime methods. All raw dumps and launch/terminal receipts remain preserved.

The actual lifecycle constructs a shuffled 1024-member target, queries, removes/re-adds a tag, enumerates, produces a distinct 1536-member union and a distinct 512-member intersection, copies the intersection into the original 1024-member target, and clears it. The copy source is the 512 even tags T01024..T02046 and preserves left-input representatives; this is not the unrelated retained-copy fixture's odd-name geometry.

## Native observations

The natural step closure is Program+<>c__DisplayClass10_0:<Run>b__10(int). Its lifecycle branch contains, in order:

1. Calls to public Union and IntersectionExact.
2. A comparison of the original target and resulting intersection, matching CopyFrom's self-alias guard.
3. A direct native call to GameplayTagContainerAlgorithms.CopyValidated.
4. The subsequent Clear implementation and its Array.Clear helper call.

There is no standalone CopyFrom listing. The caller's observed alias guard and direct CopyValidated call are positive instruction evidence; mere absence of CopyFrom output is not used to infer inlining or nonexecution. The closure also contains dormant branches for other harness operations; their presence in generated code does not mean those workflows ran.

| Captured method | B11 bytes | C1 bytes | current792 bytes | Normalized comparison |
|---|---:|---:|---:|---|
| Natural step closure | 1138 | 1138 | 1138 | All equal |
| CopyValidated | 211 | 362 | 362 | C1 = current792; B11 differs |
| AllRegistered | 164 | 164 | 164 | All equal |
| Union | 155 | 155 | 155 | All equal |
| UnionSorted | 1148 | 1148 | 1148 | All equal |
| IntersectionExact | 156 | 156 | 156 | All equal |
| IntersectionExactSorted | 596 | 596 | 596 | All equal |
| EnsureCapacity | 103 | 103 | 103 | All equal |
| List<GameplayTag>.AddRange | 436 | 436 | 436 | All equal |
| List<GameplayTag>.CopyTo | 24 | 24 | 24 | All equal |
| List<GameplayTag>.AddWithResize | 126 | 126 | 126 | All equal |

B11→C1 CopyValidated differences include the earlier null/empty guard and singleton branch, additional singleton Add/write-barrier and AddWithResize paths, and changed register usage. The C1 r13 save replaces B11’s padding push, so this does not increase stack-frame consumption. The JIT describes B11 as partially interruptible and C1/current792 as fully interruptible. Both bulk routes still call AllRegistered, clear the target, call EnsureCapacity, and tail-call List.AddRange; C1/current792 additionally test Count==1 before their bulk dispatch. The observed source Count is 512, so the singleton body is not selected by this lifecycle. These observations do not quantify which differences matter to execution time.

Publication normalization masks only the observed canonical 12-digit native-address forms beginning with 0x7 and generated G_M label IDs. It preserves packed UTF-16 comparison constants, including 0x6C006F00720074 and 0x74006E006F0063, and all other numeric immediates and offsets. The initial private quick comparison used a broader mask; this publication correction retains those constants without changing any equality/difference finding. Raw address-bearing dumps are retained. A normalized textual match does not certify identical addresses/alignment, every transitive native helper, scheduling, or performance. This is one diagnostic capture per source, not a replication-based performance experiment.

## Trace attempts and limits

The separately planned trace targets used the unchanged full lifecycle, 64 warmups plus 4096 fixed requested iterations, an explicit CLR GC provider at keyword 0x1/Informational, and the official sampled-thread profile. They were not formal timing runs.

- Initial B11 attempt: dotnet-trace exited 2 with SocketException 13, Permission denied while creating its Unix-domain socket server. No workload validation output or .nettrace file was produced. The stage stopped; C1/current792 were not attempted.
- One approved elevated retry: environment bootstrap failed with a read-only-filesystem error before Python or dotnet-trace execution. This is a separate infrastructure receipt, not a second workload result.
- No profiler workload ran. No saved trace exists to decode. The offline decoder remains prepared but uncompiled/unexecuted. No CPU-hotspot, sampled-stack, pause-duration, or GC-causality conclusion is available.

The legacy cpu-sampling profile, had it succeeded, would have provided managed-thread stack samples rather than precise on-CPU attribution. The proposed offline decoder preserves observed SuspendEE/RestartEE event pairs and unknown/missing/lost-event gaps. It cannot be replaced with collection counts or inferred pause durations.

## Published evidence and reproduction

- Evidence/: sanitized normalized native listings and comparisons, validation results, binary hashes, original-artifact integrity hashes, and sanitized trace-attempt status.
- SOURCE_MANIFEST.json: exact Runtime source identities and content hashes. The current792 snapshot is shared with ../C3aMeasurements/Sources/current792/Runtime.
- HARNESS_ORIGIN.json and harness/: unchanged original Program.cs/UnityStubs.cs with provenance and the diagnostic project.
- TOOL_PROVENANCE.json: official SDK/tool sources, versions, archive/package hashes, and signature verification.
- build.py, run_diagnostics.py, analyze_jit.py: portable reproduction and saved-text analysis; see README.md.
- decoder/ and decode_traces.py: optional offline event analysis source. It was prepared but not compiled or executed because profiling produced no trace.
- SHA256SUMS and PRIVACY_SCAN.json: publication inventory and static sanitization checks.

Original raw dumps and environment-specific failure receipts remain privately retained. No .nettrace, executable, SDK archive, tool cache, protected path, or recorded host environment is included in this package. Build/capture/decoder adaptations were syntax-checked only. Saved-text normalization was reapplied to the existing raw dumps; no new build or workload target was launched.

No production Runtime source or historical/formal evidence was changed by this diagnostic investigation. Nothing here establishes a speedup, explains a prior timing difference causally, or certifies Unity Mono/IL2CPP behavior.
