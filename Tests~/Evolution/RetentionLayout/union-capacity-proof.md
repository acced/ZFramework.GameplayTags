# Adaptive union: retained-capacity argument

Applies to the adaptive queue candidate with TagStorage.cs SHA-256 `850faebde8e85235c858a7510b6c21264b223024288f606e3b5c6f7c8a735f01`, compared with original `f7c99546dbf029d70896c0f05e5119b19ffd6d26`, for successfully completed same-input calls.

- Factory seed selection and copying are unchanged. All explicit additions occur in the unchanged pass before the adaptive switch, so explicit-index array growth is identical.
- Both paths insert only previously absent members of the final ancestor closure. AddTotal never removes entries, including when the ordered sweep temporarily subtracts prefix contributions. Count therefore increases one at a time from the same seed count to the same final count.
- Entry-capacity growth and bucket rehash thresholds depend on that count sequence, not insertion IDs/order. Identical seed buffers therefore produce identical final Entries, ExplicitIndices, and bucket capacities.
- The adaptive sweep adds no object fields or managed heap scratch. Int32Sort sorts the existing buffer in place.
- The same single ArrayPool<int>.Shared.Rent(source.ExplicitCount) request and finally Return(clearArray:false) are unchanged. The requested pool size class does not grow.

The fixed correctness matrix and focused deep-chain/count-normalization tests check equivalent final contents and counts. The separate structural audit checks the original 24 retained intersection fixtures unchanged; it is not mislabeled as a Union-specific cohort measurement.

This establishes equal result storage structure and requested pool capacity. It does not establish identical global pool residency/trim timing, concurrent pool availability, OS heap size, JIT/code memory, or Unity/IL2CPP behavior. GC retained-heap estimates remain approximate and are not used to override this distinction.
