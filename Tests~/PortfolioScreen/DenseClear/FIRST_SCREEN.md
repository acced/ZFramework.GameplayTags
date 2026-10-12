# First dense Clear screen: held

Exact candidate commit: `2ed65cc16ebd6c60aa8ba84c969b90e18c135406`.
[Both backend jobs completed](https://github.com/acced/ZFramework.GameplayTags/actions/runs/38200701706).
All twelve rows, one baseline-only pilot and four paired rounds are retained. This is rejection screening only; qualified count remains **0/10**.

Both runtimes showed roughly 4–8% gains in several 64/65/1024-item refill + Clear cycles. These times include the identical public refill operation and are not isolated Clear timings. Actual fast batches below the 200 ms target remain flagged.

.NET empty linear Clear increased about 4.45→5.62 ns and empty retained-table Clear about 4.57→5.85 ns. Both were slower in all four pairs with approximately 200 ms batches, zero allocation and zero GC. Sparse16 and fresh63 include large isolated adverse samples; those were retained rather than discarded. Mono's empty paths improved, but that does not clear the .NET loss. The exact source is held before full qualification.

A bounded untimed inspection of the existing local .NET binaries found that both variants call TagStorage.Clear out-of-line from the same public-call context. Thus a lost-inlining explanation was not supported there. The new Clear native body had a larger saved-register frame. One repair hypothesis is a small reset wrapper with separately outlined bucket work; neither that observation nor the proposal proves a causal explanation or timing improvement. Both count resets must remain unconditional, including when live Count is zero. Any repair retains this same twelve-row protocol and prior negative evidence.

Artifact identities:
- .NET `11725835348`, ZIP SHA-256 `a721a08e7ed336d176ad86d7ae5ebdab1f8a2ccb75afd7f40207994e94d8f436`
- Mono `11725845895`, ZIP SHA-256 `a18d923cc4d5af523756f55104816a4271ee9976815f39f07f1ba7dbee5f92c1`

Actual Unity Editor/Player, IL2CPP and Alex performance remains unmeasured.
