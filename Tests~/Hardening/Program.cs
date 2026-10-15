using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using GameplayTags;

internal static class HardeningTests
{
    static int failures, passed;
    static bool baseline;
    static GameplayTag T(string n) => GameplayTagManager.RequestTag(n);
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
    }
    static GameplayTagContainer Set(params string[] names)
    {
        var c = new GameplayTagContainer(); foreach (var n in names) c.AddTag(T(n)); return c;
    }
    static void Same(GameplayTagContainer c, params string[] names)
    {
        var got = c.GetExplicitTags().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal);
        Check(got.SequenceEqual(names.OrderBy(n => n, StringComparer.Ordinal)), "set mismatch");
    }
    static int Main(string[] args)
    {
        baseline = args.Contains("--baseline");
        if (args.Contains("--invalid-registry"))
        {
            Test("null registrations", () => Throws<ArgumentNullException>(() => GameplayTagManager.Initialize((GameplayTagRegistration[])null)));
            Test("invalid declaration rejected before publish", () => {
                Throws<ArgumentException>(() => GameplayTagManager.Initialize(new GameplayTagRegistration("A..B")));
                Check(!GameplayTagManager.IsInitialized, "bad registry published");
            });
            return Finish();
        }
        if (args.Contains("--initialized"))
        {
            GameplayTagManager.Initialize(new GameplayTagRegistration("A.B"), new GameplayTagRegistration("A.C"), new GameplayTagRegistration("Z"));
            RunInitialized(); return Finish();
        }
        // These callbacks must not initialize the global registry.
        Test("serialization callbacks do not freeze registry", () => {
            var c = new GameplayTagContainer { m_SerializedExplicitTags = new List<string>{"A.B", "A.C"} };
            c.OnAfterDeserialize(); c.OnBeforeSerialize();
            Check(!GameplayTagManager.IsInitialized, "callback initialized registry");
            c.Clear(); Check(!GameplayTagManager.IsInitialized, "clear initialized registry");
        });
        if (!GameplayTagManager.IsInitialized)
            GameplayTagManager.Initialize(new GameplayTagRegistration("A.B"), new GameplayTagRegistration("A.C"),
                new GameplayTagRegistration("Z"));
        // Baseline can initialize an empty registry in its callback. Skip dependent
        // checks in that process; a second mode initializes before callbacks.
        if (T("A.B").RuntimeIndex == 0)
        {
            Console.WriteLine("Baseline callback froze empty registry; run --initialized for independent reproductions.");
            return Finish();
        }
        RunInitialized();
        return Finish();
    }
    static void RunInitialized()
    {
        Test("unbound tag works without struct callback", () => {
            var raw = new GameplayTag { m_Name = "A.B" };
            Check(raw.RuntimeIndex == T("A.B").RuntimeIndex && raw == T("A.B"), "unbound identity");
        });
        Test("name edit invalidates ID", () => {
            var x = T("A.B"); x.m_Name = "A.C";
            Check(x == T("A.C") && x != T("A.B"), "stale cached ID");
        });
        if (!baseline) Test("backing field overwrite cannot retain stale ID", () => {
            object boxed = T("A.B");
            typeof(GameplayTag).GetField("m_Name", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).SetValue(boxed, "A.C");
            var tag = (GameplayTag)boxed; Check(tag == T("A.C"), "direct serialized field overwrite");
        });
        Test("unknown serialized name survives callback", () => {
            var x = new GameplayTag { m_Name = "Missing.Tag" }; x.OnBeforeSerialize(); x.OnAfterDeserialize();
            Check(x.Name == "Missing.Tag" && x.RuntimeIndex == 0, "unknown name lost");
        });
        Test("standalone DataContract round trip", () => {
            var serializer = new DataContractSerializer(typeof(GameplayTag));
            using var stream = new MemoryStream(); serializer.WriteObject(stream, T("A.B")); stream.Position = 0;
            var x = (GameplayTag)serializer.ReadObject(stream); Check(x == T("A.B"), "roundtrip");
        });
        Test("None cannot corrupt set", () => {
            var c = new GameplayTagContainer(); Check(!c.TryAddTag(default), "None inserted");
            Check(c.TagCount == 0 && c.ExplicitTagCount == 0 && !c.HasTagExact(default), "None visible");
        });
        Test("null copy and algebra inputs", () => {
            GameplayTagContainer nil = null; var a = Set("A.B"); var d = Set("Z");
            GameplayTagContainer.Copy(d, nil); Same(d);
            Same(GameplayTagContainer.Union(a, nil), "A.B"); Same(GameplayTagContainer.Union(nil, a), "A.B");
            Same(GameplayTagContainer.Intersection(a, nil));
            GameplayTagContainer.Intersection(d, nil, a); Same(d);
            d.AddTags(nil); d.RemoveTags(nil); Check(!GameplayTagContainerExtensionMethods.HasTag(nil, T("A")), "null holder");
        });
        Test("shared Empty cannot be changed", () => {
            Throws<InvalidOperationException>(() => GameplayTagContainer.Empty.AddTag(T("A.B")));
            Check(GameplayTagContainer.Empty.IsEmpty, "global empty changed");
        });
        Test("negative capacity rejected", () => Throws<ArgumentOutOfRangeException>(() => new GameplayTagContainer(-1)));
        if (!baseline) Test("huge capacity terminates with exception", () => Throws<ArgumentOutOfRangeException>(() => new GameplayTagContainer(int.MaxValue)));
        Test("nonpositive count rejected", () => {
            var c = new GameplayTagCountContainer();
            Throws<ArgumentOutOfRangeException>(() => c.AddTag(T("A.B"), 0));
            Throws<ArgumentOutOfRangeException>(() => c.AddTag(T("A.B"), -1));
            Check(c.IsEmpty && c.TagCount == 0, "invalid amount mutated");
        });
        Test("None count rejected", () => {
            var c = new GameplayTagCountContainer(); Throws<ArgumentException>(() => c.AddTag(default)); Check(c.IsEmpty, "none count");
        });
        Test("excess removal preserves counts", () => {
            var c = new GameplayTagCountContainer(); c.AddTag(T("A.B"), 2);
            Throws<ArgumentOutOfRangeException>(() => c.RemoveTag(T("A.B"), 3));
            Check(c.GetTagCount(T("A")) == 2 && c.GetExplicitTagCount(T("A.B")) == 2, "overremove mutated");
        });
        Test("root overflow rejected before mutation", () => {
            var c = new GameplayTagCountContainer(); c.AddTag(T("A.B"), int.MaxValue);
            Throws<OverflowException>(() => c.AddTag(T("A.C")));
            Check(c.GetTagCount(T("A")) == int.MaxValue && c.GetExplicitTagCount(T("A.C")) == 0, "overflow mutated");
        });
        Test("bulk overflow rejects before counts or notifications change", () => {
            var c = new GameplayTagCountContainer(); c.AddTag(T("A.B"), int.MaxValue - 1); int notifications = 0;
            c.OnAnyTagCountChange += (_, __) => notifications++;
            Throws<OverflowException>(() => c.AddTags(Set("A.B", "A.C")));
            Check(c.GetTagCount(T("A")) == int.MaxValue - 1 && c.GetExplicitTagCount(T("A.C")) == 0, "bulk partial mutation");
            c.AddTag(T("Z")); Check(notifications == 1, "stale event queue");
        });
        Test("reparent overflow leaves old link and counts intact", () => {
            var old = new GameplayTagCountContainer(); var next = new GameplayTagCountContainer(); next.AddTag(T("A.B"), int.MaxValue);
            var child = new GameplayTagHierarchicalContainer { Parent = old }; child.AddTag(T("A.C"));
            Throws<OverflowException>(() => child.Parent = next);
            Check(ReferenceEquals(child.Parent, old) && old.GetTagCount(T("A")) == 1 && child.GetTagCount(T("A")) == 1, "reparent partial mutation");
        });
        Test("hierarchical bulk overflow leaves child unchanged", () => {
            var parent = new GameplayTagCountContainer(); parent.AddTag(T("A.B"), int.MaxValue - 1);
            var child = new GameplayTagHierarchicalContainer { Parent = parent };
            Throws<OverflowException>(() => child.AddTags(Set("A.B", "A.C")));
            Check(child.IsEmpty && parent.GetTagCount(T("A")) == int.MaxValue - 1, "bulk chain partial mutation");
        });
        Test("self-parent cycle rejected", () => {
            var c = new GameplayTagHierarchicalContainer(); Throws<ArgumentException>(() => c.Parent = c);
        });
        Test("indirect parent cycle rejected", () => {
            var a = new GameplayTagHierarchicalContainer(); var b = new GameplayTagHierarchicalContainer(); a.Parent = b;
            Throws<ArgumentException>(() => b.Parent = a);
        });
        Test("chain overflow preserves child", () => {
            var parent = new GameplayTagCountContainer(); parent.AddTag(T("A.B"), int.MaxValue);
            var child = new GameplayTagHierarchicalContainer { Parent = parent };
            Throws<OverflowException>(() => child.AddTag(T("A.C")));
            Check(child.IsEmpty && parent.GetTagCount(T("A")) == int.MaxValue, "chain partial update");
        });
        Test("invalid initial bind does not leak callback", () => {
            var c = new GameplayTagCountContainer(); var b = new GameplayTagContainerBinds(c); int calls = 0;
            Throws<ApplicationException>(() => b.Bind(T("A"), _ => { calls++; throw new ApplicationException(); }));
            c.AddTag(T("A.B")); Check(calls == 1, "initial observer retained"); b.Dispose();
        });
        Test("enumerator fails after mutation", () => {
            var c = Set("A.B"); var e = c.GetExplicitTags(); Check(e.MoveNext(), "first"); c.AddTag(T("A.C"));
            Throws<InvalidOperationException>(() => e.MoveNext()); Throws<InvalidOperationException>(() => { var t = e.Current; });
        });
        Test("same-tag add and strict ancestor totals", () => {
            var c = Set("A.B", "A.C", "A"); c.RemoveTag(T("A.B")); Check(c.HasTag(T("A")), "shared parent lost");
            c.RemoveTag(T("A")); Check(c.HasTag(T("A")) && !c.HasTagExact(T("A")), "promotion count");
            c.RemoveTag(T("A.C")); Check(c.TagCount == 0, "closure survives drain");
        });
        Test("pool copied lease cannot return new rental", () => {
            var a = GameplayTagContainerPool.Get(out var first); var stale = a; a.Dispose();
            var b = GameplayTagContainerPool.Get(out var second); second.AddTag(T("A.B")); stale.Dispose();
            Check(second.HasTagExact(T("A.B")), "stale lease cleared new borrower"); b.Dispose();
        });
        Test("pool default lease dispose is harmless", () => default(GameplayTagContainerPool.PooledContainer).Dispose());
        Test("pool duplicate raw return rejected", () => {
            var c = GameplayTagContainerPool.Get(); GameplayTagContainerPool.Release(c);
            Throws<InvalidOperationException>(() => GameplayTagContainerPool.Release(c));
        });
        Test("pool cross-thread return rejected", () => {
            var c = GameplayTagContainerPool.Get(); Exception error = null;
            var thread = new Thread(() => { try { GameplayTagContainerPool.Release(c); } catch (Exception e) { error = e; } });
            thread.Start(); thread.Join(); Check(error is InvalidOperationException, "cross-thread return"); GameplayTagContainerPool.Release(c);
        });
        Test("randomized alias intersections preserve and drain closure", () => {
            var r = new Random(6249); string[] universe = { "A", "A.B", "A.C", "Z" };
            for (int i = 0; i < 1000; i++) {
                var x = universe.Where(_ => r.Next(2) == 0).ToArray(); var y = universe.Where(_ => r.Next(2) == 0).ToArray();
                var a = Set(x); var b = Set(y); var d = i % 3 == 0 ? a : i % 3 == 1 ? b : Set("Z");
                GameplayTagContainer.Intersection(d, a, b); var expected = x.Intersect(y).ToArray(); Same(d, expected);
                foreach (var n in expected) d.RemoveTag(T(n)); Check(d.TagCount == 0, "drain closure");
            }
        });
    }
    static int Finish()
    {
        Console.WriteLine($"RESULT passed={passed} failed={failures} baseline={baseline}");
        return baseline ? 0 : failures == 0 ? 0 : 1;
    }
}
