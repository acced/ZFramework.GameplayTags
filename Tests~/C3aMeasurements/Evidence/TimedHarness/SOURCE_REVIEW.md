# Preparation source review

## Narrow harness changes

The origin Program is exactly the published/timed C12 Program SHA256 `62638366804b87989b49a15d14fc15daf1dce8a7d93ef7fb0698825d77ac14d7`. Unity stubs, NuGet config and project contents are copied unchanged (project filename changes only). C3a Program adds two operations (public factory `Union` and nonalias `SetUnion`), explicit overlap/disjoint geometry, and exact lifecycle-intersection copy representatives. Existing lifecycle and late C2 operation bodies are unchanged. Source/seed setup geometry remains identical to C12 for retained copy sentinels, lifecycle and late C2 cells.

Core duplicate Name representatives use distinct equal strings. The expected union starts with lhs then rhs, so grouping keeps lhs representatives. `Assert` checks every element's Name reference, ordered uniqueness, enumeration and default/null spare backing slots. It checks source and seed after measurement too. Factory/nonalias result cannot be either input. Actual capacity is read only after raw numeric flush and validation; fresh/retained sizes are not inferred from allocation byte ratios.

Lifecycle1024 still builds1024 lhs values; source has1024 values and overlap512; union1536; intersection512; CopyFrom is1024←512 before Clear. Public Union directly enters the nonalias kernel, not C2 append/alias. Lifecycle1 is full-overlap, with input/output cardinality1. Separate copy512 target1024 shares intersection-style lhs references and has source and target backing capacity1024 (the512-member lifecycle intersection reserves1024); restoration is intentionally included inside timing.

The retained setunion target is restored by CopyFrom(lhs) every iteration before union. CopyFrom validation and mutation are part of the reported public workflow. Factory union constructs only the actual public result inside counters; it does not rebuild inputs. Actual public factory capacity is lhs.Count+rhs.Count, even when the final union is smaller. Late C2 new workflows rebuild targets inside counters and include growth; retained workflows restore within capacity2048. Avoiding a2047 upper-bound reservation is distinct from shrinking, which is not claimed.

## Runner and finite plan

Based on the hardened C12 run_original.py: protected spawn/assignment, dedicated process groups, protected kill/reap, raw/stderr/launch/terminal preservation, tiering-disabled sanitized child environment, exclusive outputs, fixed four-pair comparison. New source verifier checks exact baseline and candidate bytes before building and every collection stage. Build manifest also pins Program/project/stubs/NuGet bytes; freeze rejects a harness changed after compilation. Count hash is checked on entry and exit to comparison. No A/A action or historical count import exists.

31 cells ×4 pairs ×2 versions =248 formal invocations. Up to31×4=124 current792-only pilot invocations plus62 fixture invocations. Each pilot count is bounded before invocation; final measured baseline count is frozen and reused by both versions. Inadequate/capped/short rows cannot be removed by this runner. Proposed900s aggregate wall cap and60s child cap are explicit; outer cleanup allowance is5s.

## Checks performed during preparation

- Read available repository context; no AGENTS.md or checkout .agents/skills were present.
- Compared all16 baseline Runtime files to exact commit792ce83 using read-only git show; all matched.
- Checked candidate working Runtime against implementation snapshot; exactly one Runtime source differs from baseline.
- Parsed Python files without invoking any .NET target; checked shell syntax.
- Checked31 unique keys, exact operation counts/geometries, baseline cap/version/checksum bounds, and matched helper/script input fields.
- Checked summary generation using fabricated small JSON samples in a temporary directory, never actual target measurements.

These static checks do not establish C# compilation or fixture execution. Those remain explicitly not run by this worker and require parent approval. The existing semantic suite result is owned by the parent and is not recertified here.
