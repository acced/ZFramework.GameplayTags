# Cache refinement: only missing records, one old-tail move

Separate follow-up after the asymmetric cache32 benchmark found regressions. Three fixed builds: PR20 monotone, original PR22 cache32, delta32. This is NOT the main full2952case matrix, and its absolute timing must not be spliced into primary data.

Delta32 replaces only AppendDenseSmall. The Count<=32 guard, NoInlining,128B Micro16 stack budget, large-source stream fallback and every other public method stay unchanged. Fixed controls are regenerated and checked by SHA256. No additional representation, set/cursor field or numeric threshold.

Existing record keys are matched and ORed during the single Dense pass, counting original overlap once. Only absent keys are cached. Every cached key is absent from the old target, so reverse merge needs no equality branch. n cached records is exactly the record-count increase. If n==0, there is no reverse pass and no unchanged-tail rewriting. If n>0, tail is the old lower-bound index for the final missing key: all missing keys precede that tail. Shift the old tail right by n once, then reverse-merge only the old prefix and cached missing keys. Tail may include matches already updated earlier, and copying preserves those updates. Source keys are unique, so those matches are not double-counted. Count is original target+source-original overlap.

Exact final capacity and original growth staircase remain; no temporary heap arrays. For n>0 and tail<used, result has at least2records so reservation establishes array storage before Array.Copy; no extra null check needed. OOM can leave prefix updates with old count, as in the existing stream contract, not a new strong-exception guarantee. External invalid input checks still precede helpers.

Execute complete inherited correctness suites (including544 new cache cases,1175prefix cases,1920growth comparisons,masks,aliases,continued mutation), separate Standard2.1 and hardware-disabled delta. Measure192cache-boundary plus128asymmetric operations using original unchanged timer, three shuffled process rounds and same-binaryA/A. No all-public/full-lifecycle performance acceptance from this focused matrix. Compile actual helper disassembly, report stack and method-code changes.

```sh
python3 'Audit~/DenseCacheDelta/run.py' --output artifacts/results --rounds 3
python3 'Audit~/DenseCacheDelta/review.py' --root artifacts/results
```

No production merge or nativeUnity/IL2CPP/Burst/phone claim. All negative results retained; the method is not accepted simply because it does less source-level work.
