# Dense Clear: bounded probe outcome

The wrapper candidate remains **unqualified; total 0/10**. Both backend jobs in
[run 38208468997](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38208468997)
completed at exact commit `9bbf86b275a0f6701295ceca8bdf3d1f9a559689`.
The source patch is `23eddbeb558d27f5f977a9e2ea73997ea1dd5af550776ce68050c4452fe71ef0`;
root Runtime was not changed by this probe commit.

## Decision

Each backend retained all 48 first-use calls and four paired warm processes per
source. First-use managed allocations and GC counts were zero, and paired full
state/capacity checks matched. Nevertheless, every one of the six nonempty hashed
fixtures was slower in all three first-use pairs on both runtimes:

- .NET 8.0.31: roughly 0.20–0.27 ms before, 0.34–0.40 ms after.
- Standalone Mono 6.8.0.105: roughly 0.12–0.15 ms before, 0.15–0.19 ms after.

The boundary is the first public Clear after the declared fixture and observer
setup; it is not universal cold-process startup. The extra latency's mechanism
is not established. Allocation parity does not erase the observed timing cost.
This stops promotion to broad qualification under the existing comprehensive
goal. No further source repair, repeated sampling, or full matrix is queued.

Warm isolated Clear shows substantial descriptive dense gains: 64/1024 entries
approximately 4.79x/10.10x on .NET and 4.73x/11.94x on Mono. However, all summed
Clear batches hit the predeclared caps and remain short. Mono empty/tiny rows
also have weak timer resolution. The .NET nine-entry hashed row is slower in
three of four pairs. These caveats and every adverse sample remain intact.

## Exact public artifacts

- .NET artifact `11727834900`, SHA-256
  `6f6d4387695b01a24d747dab6a8e429dc3726881ab5f87856f3f76a34434e9de`
- Mono artifact `11728835649`, SHA-256
  `1a9541b47a5cd81caa045d7e0b48273b52784723f347ca8b0a64f06cb2251cb2`

Both archives were digest-verified and their raw allocation, timing, paired
identity and state records independently reviewed. The original fixed sources,
protocol, maps and reports have not been changed. Unity Editor/Player, IL2CPP
and Alex were not executed. Green CI means the bounded probe completed.
