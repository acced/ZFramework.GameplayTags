#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using GameplayTags;
using UnityEditor;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace GameplayTags.Samples
{
    /// <summary>
    /// Run in the target Unity Editor, outside Play Mode or with gameplay paused.
    /// Uses existing registered tags; does not reset the registry or alter assets.
    /// Profiler markers isolate each warmed allocation batch. Enable allocation
    /// call stacks for diagnosis; disable profiling when comparing timings.
    /// </summary>
    internal static class GameplayTagIntersectionDiagnostics
    {
        private static int s_Sink;

        [MenuItem("Tools/Gameplay Tags/Diagnose General Intersection Allocations")]
        private static void Run()
        {
            ReadOnlySpan<GameplayTag> tags = GameplayTagManager.GetAllTags();
            int size = Math.Min(1024, tags.Length / 2);
            size &= ~1;
            if (size < 32)
            {
                Debug.LogWarning("Intersection diagnostic needs at least 64 registered tags. It never reinitializes the registry.");
                return;
            }
            MethodInfo counterMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            if (counterMethod == null)
            {
                Debug.LogWarning("This runtime lacks a per-thread allocation counter. Use the GC.Alloc call-stack profiler instead.");
                return;
            }
            var counter = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), counterMethod);
            var left = new List<GameplayTag>(size);
            var right = new List<GameplayTag>(size);
            for (int i = 0; i < size; i++) left.Add(tags[i]);
            for (int i = 0; i < size / 2; i++) right.Add(tags[i * 2]);
            for (int i = 0; i < size / 2; i++) right.Add(tags[size + i]);
            var random = new Random(710923);
            Shuffle(left, random); Shuffle(right, random);
            var a = new GameplayTagContainer(); var b = new GameplayTagContainer();
            foreach (GameplayTag tag in left) a.AddTag(tag);
            foreach (GameplayTag tag in right) b.AddTag(tag);
            for (int i = 0; i < left.Count; i += 4) { a.RemoveTag(left[i]); a.AddTag(left[i]); }
            var output = new GameplayTagContainer();
            var workspace = new GameplayTagIntersectionWorkspace();
            var sortBuffer = new int[size + 6];
            Debug.Log("General-intersection diagnostic; E=" + size + ", overlap=50%, registry=" + tags.Length +
                ". Warm capacities only; fresh/growing outputs are not allocation-free. Read marker GC.Alloc counts separately.");
            Measure("GameplayTags.Intersection.Default.Reused", () =>
            {
                GameplayTagContainer.Intersection(output, a, b);
                s_Sink = unchecked(s_Sink + output.TagCount);
            }, counter);
            if (output.ExplicitTagCount != size / 2) throw new Exception("Default intersection cardinality mismatch");
            Measure("GameplayTags.Intersection.Workspace.Reused", () =>
            {
                GameplayTagContainer.Intersection(output, a, b, workspace);
                s_Sink = unchecked(s_Sink + output.TagCount);
            }, counter);
            if (output.ExplicitTagCount != size / 2) throw new Exception("Workspace intersection cardinality mismatch");
            Measure("GameplayTags.Diagnostic.BCL.ArraySort.Segment", () =>
            {
                for (int i = 0; i < size; i++) sortBuffer[i + 3] = size - i;
                Array.Sort(sortBuffer, 3, size);
                s_Sink = unchecked(s_Sink + sortBuffer[3]);
            }, counter);
        }

        private static void Measure(string marker, Action operation, Func<long> counter)
        {
            const int iterations = 1000;
            // Delegate creation, initialization, JIT, buffers and marker registration
            // are outside the observed batch. No logging occurs inside it.
            for (int i = 0; i < 200; i++) operation();
            Profiler.BeginSample(marker); Profiler.EndSample(); counter();
            Profiler.BeginSample(marker);
            long startBytes = counter();
            for (int i = 0; i < iterations; i++) operation();
            long bytes = counter() - startBytes;
            Profiler.EndSample();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) operation();
            double ns = (Stopwatch.GetTimestamp() - start) * (1e9 / Stopwatch.Frequency) / iterations;
            Debug.Log(marker + ": " + bytes + " bytes / " + iterations + " calls; " + ns.ToString("F1") +
                " ns/call (timing includes Action invocation; profiling can distort it). Check GC.Alloc events under the marker.");
        }

        private static void Shuffle<T>(IList<T> values, Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T temporary = values[i]; values[i] = values[j]; values[j] = temporary;
            }
        }
    }
}
#endif
