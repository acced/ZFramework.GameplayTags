using System;
using System.Linq;
using System.Runtime.CompilerServices;
using GameplayTags;
#if BULK_MICRO
using Set = GameplayTags.Experiments.DirectArraySet;
using Layout = GameplayTags.Experiments.BitmapLayout;
#else
using Set = GameplayTags.RuntimeTagSet;
using Layout = GameplayTags.TagSetStorage;
#endif
internal static partial class BulkProbe
{
    static Set ConvertIndependent(Set source, Layout target, int capacity)
    {
#if PREPARATION_V2
        return source.CopyAsStorage(target, capacity);
#else
        var result = new Set(source.Registry, Math.Max(source.Count, capacity), target);
        result.CopyFrom(source);
        return result;
#endif
    }

    // 'input' starts with caller-owned sorted resolved handles. 'live' starts with a live set.
    // A live rebuild includes exporting handles (allocation + enumeration), not a free snapshot.
    // The same prebuilt peer is used by all strategies in the ORIGINAL source representation.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int LifecycleLoop(TagRegistry registry, RuntimeTag[] input, Set live, Set peer,
        Layout from, Layout target, int reserve, bool liveOrigin, int strategy, int uses, int repeats)
    {
        for (int i = 0; i < repeats; i++)
        {
            Set prepared;
            Layout resultLayout;
            if (strategy == 0)
            {
                prepared = liveOrigin ? live : Set.FromSortedUnique(registry, input, reserve, from);
                resultLayout = from;
            }
            else if (strategy == 1)
            {
                var original = liveOrigin ? live : Set.FromSortedUnique(registry, input, reserve, from);
                prepared = ConvertIndependent(original, target, reserve);
                resultLayout = target;
            }
            else
            {
                RuntimeTag[] handles = input;
                if (liveOrigin)
                {
                    handles = live.Count == 0 ? Array.Empty<RuntimeTag>() : new RuntimeTag[live.Count];
                    int at = 0;
                    foreach (RuntimeTag tag in live) handles[at++] = tag;
                }
                prepared = Set.FromSortedUnique(registry, handles, reserve, target);
                resultLayout = target;
            }
            sink = prepared;
            for (int j = 0; j < uses; j++) sink = Set.Union(prepared, peer, resultLayout);
        }
        return sink.Count;
    }

    static object FrontierTests()
    {
        long begin = assertions;
        foreach (int leaves in new[] {0, 1, 15, 64, 65, 4096})
        {
            var reg = Registry(leaves);
            foreach (Layout from in new[] {(Layout)1, Layout.Dense})
            foreach (Layout to in new[] {(Layout)1, Layout.Dense})
            foreach (int reserve in new[] {0, 1, 32, leaves + 100})
            {
                var source = new Set(reg, reserve, from);
                if (reg.Count != 0) { source.AddTag(reg.GetTagAt(reg.Count - 1)); source.Clear(); }
                long originalBytes = source.BufferBytes;
                var copy = ConvertIndependent(source, to, reserve);
                Verify(copy, Array.Empty<int>());
                var control = new Set(reg, reserve, to); control.CopyFrom(source);
                SameStorage(copy, control);
                Check(!ReferenceEquals(source, copy), "empty conversion must own its result");
                if (reg.Count != 0)
                {
                    copy.AddTag(reg.GetTagAt(0)); Verify(source, Array.Empty<int>());
                    source.AddTag(reg.GetTagAt(reg.Count - 1)); Verify(copy, new[] {0});
                    source.Clear();
                }
                Check(source.BufferBytes == originalBytes, "conversion changed source capacity");
#if PREPARATION_V2
                Throws<ArgumentOutOfRangeException>(() => source.CopyAsStorage(Layout.Auto));
                Throws<ArgumentOutOfRangeException>(() => source.CopyAsStorage((Layout)99));
                Throws<ArgumentOutOfRangeException>(() => source.CopyAsStorage(to, -1));
#endif
            }
        }
        var r = Registry(513);
        var ids = new[] {0, 1, 15, 16, 63, 64, 512, 513};
        var rhs = new[] {1, 2, 16, 65, 510, 513};
        var tags = Tags(r, ids);
        foreach (Layout from in new[] {(Layout)1, Layout.Dense})
        foreach (Layout to in new[] {(Layout)1, Layout.Dense})
        foreach (bool origin in new[] {false, true})
        foreach (int strategy in new[] {0, 1, 2})
        foreach (int uses in new[] {0, 1, 4})
        {
            var source = Set.FromSortedUnique(r, tags, 64, from);
            var peer = Set.FromSortedUnique(r, Tags(r, rhs), 64, from);
            LifecycleLoop(r, tags, source, peer, from, to, 64, origin, strategy, uses, 2);
            Verify(sink, uses == 0 ? ids : ids.Concat(rhs));
            Verify(source, ids); Verify(peer, rhs);
            Check(tags.Select(t => t.RuntimeIndex).SequenceEqual(ids), "lifecycle mutated input");
            if (!(origin && strategy == 0 && uses == 0))
            {
                Check(!ReferenceEquals(sink, source), "result must be independent");
                sink.Clear(); Verify(source, ids); Verify(peer, rhs);
            }
        }
        return new { assertions = assertions - begin, failures = 0,
            note = "live keep+zero uses intentionally retains source identity; not a clone benchmark" };
    }

    static void FrontierMatrix(bool smoke)
    {
        foreach (int u in smoke ? new[] {10000} : new[] {10000, 262144})
        {
            var reg = Registry(u);
            foreach (int seed in smoke ? new[] {20261107} : new[] {20261107, 20261119})
            foreach (string distribution in new[] {"contiguous", "clusters4", "scattered"})
            foreach (int n in smoke ? new[] {0, 8} : new[] {0, 8, 128, 4096})
            {
                int[] sequence = Sequence(u, n * 2, distribution, seed);
                int[] x = sequence.Take(n).OrderBy(i => i).ToArray();
                int[] y = x.Take(n / 2).Concat(sequence.Skip(n).Take(n - n / 2)).OrderBy(i => i).ToArray();
                RuntimeTag[] input = Tags(reg, x); string digest = Digest(x);
                int reserve = n + 17;
                foreach (Layout from in new[] {(Layout)1, Layout.Dense})
                {
                    Layout target = from == Layout.Dense ? (Layout)1 : Layout.Dense;
                    var live = Set.FromSortedUnique(reg, input, reserve, from);
                    var peer = Set.FromSortedUnique(reg, Tags(reg, y), reserve, from);
                    foreach (bool liveOrigin in new[] {false, true})
                    foreach (int uses in smoke ? new[] {0, 1} : new[] {0, 1, 16})
                    {
                        int first = (seed + n + round + uses) % 3;
                        for (int s = 0; s < 3; s++)
                        {
                            int strategy = (s + first) % 3;
                            string mode = strategy == 0 ? "keep" : strategy == 1 ? "convert" : "direct";
                            string origin = liveOrigin ? "live" : "input";
                            string operation = origin + "_plus_" + uses + "_unions";
                            var shape = new { registryCount = reg.Count, words = (reg.Count + 63) / 64,
                                blocks16 = x.Select(id => id >> 4).Distinct().Count(), reserve,
                                source = from.ToString(), target = target.ToString(), peerStorage = from.ToString(),
                                peerDigest = Digest(y), sourceBuffer = live.BufferBytes, origin, uses,
                                rule = "no selector; target and keep have distinct layouts; every strategy pays its real preparation" };
                            LifecycleLoop(reg, input, live, peer, from, target, reserve, liveOrigin, strategy, uses, 1);
                            Verify(sink, uses == 0 ? x : x.Concat(y));
                            Measure(u, n, distribution, seed, from + "->" + target, operation, mode, digest,
                                count => LifecycleLoop(reg, input, live, peer, from, target, reserve, liveOrigin, strategy, uses, count),
                                liveOrigin && strategy == 0 && uses == 0, shape);
                            Verify(sink, uses == 0 ? x : x.Concat(y));
                            Verify(live, x); Verify(peer, y);
                            Check(Digest(input.Select(t => t.RuntimeIndex)) == digest, "input changed");
                        }
                    }
                }
            }
        }
    }
}
