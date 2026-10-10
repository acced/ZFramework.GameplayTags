# General intersection allocation diagnostic (Unity Editor)

Copy `Editor/GameplayTagIntersectionDiagnostics.cs` into an `Assets/Editor` folder
that can reference the GameplayTags runtime assembly. The sample is intentionally
under `Samples~` and is not compiled automatically into the package.

Use **Tools > Gameplay Tags > Diagnose General Intersection Allocations** with
at least 64 existing registered tags. The sample uses up to 1024 explicit tags per
input, shuffled insertion and actual swap-delete/reinsert operations, and exactly
50% explicit overlap. More than eight tags are excluded, forcing general rebuild.
It does not replace the registry or write project assets.

The default call and explicit-workspace call each warm up 200 times, then measure
1000 operations over retained output/workspace capacity. Read per-thread bytes in
the console. Capture `GC.Alloc` call stacks in the CPU Profiler under:

- `GameplayTags.Intersection.Default.Reused`
- `GameplayTags.Intersection.Workspace.Reused`
- `GameplayTags.Diagnostic.BCL.ArraySort.Segment` (positive diagnostic control;
  its allocation behavior depends on the installed Mono/BCL revision).

The byte counter does not count allocation *events*. For event counts and the
responsible call stacks use the Profiler. Default/workspace are expected to show
zero warmed bytes/events, excluding first-time allocation and capacity growth.
The BCL control is not required to allocate on every .NET/Mono implementation.

Delegate creation, logging and setup occur outside the measured batch. The timing
loop is separate from the allocation batch but still includes `Action` invocation;
disable Deep Profiling and allocation-call-stack collection for timing comparisons.
Repeat the original project's alternating benchmark for production timings; this
sample is not a replacement for that benchmark or an Editor version certification.
