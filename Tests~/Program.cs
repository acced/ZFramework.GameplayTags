using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using GameplayTags;

internal static class Program
{
    private static readonly Dictionary<string, GameplayTag> Tags = new Dictionary<string, GameplayTag>(StringComparer.Ordinal);
    private static string[] Names;
    private static long assertions;
    private static int failures;

    private static int Main()
    {
        FixtureRegistration.Register(FixtureNames());
        foreach (GameplayTag tag in GameplayTagManager.GetAllTags()) Tags.Add(tag.Name, tag);
        Names = Tags.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Run("registry and strict ancestor relationships", Registry);
        Run("set closure, duplicate insertion and ancestor reference counts", SetBasics);
        Run("copy, union, intersection, aliases and reuse", SetOperations);
        Run("queries, parent/child extraction and requirements", Queries);
        Run("randomized set operations and dense swap deletion", RandomizedSets);
        Run("randomized count model and bulk aliases", RandomizedCounts);
        Run("count events and committed callback state", CountEvents);
        Run("callback reentrancy, exceptions and recovery", CallbackBehavior);
        Run("hierarchy multiplicity, reparenting and bulk removal", Hierarchy);
        Run("hierarchy callback transaction and exception recovery", HierarchyCallbacks);
        Run("randomized hierarchy contributions", RandomizedHierarchy);
        Run("binding and pooling lifecycle", BindingAndPooling);
        Run("name serialization and round trip", Serialization);
        Console.WriteLine($"{assertions:N0} assertions; {failures} failed groups; seed 872341 / 710923 / 312991.");
        return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<string> FixtureNames()
    {
        for (int root = 0; root < 4; root++)
            for (int branch = 0; branch < 4; branch++)
                for (int leaf = 0; leaf < 4; leaf++)
                    yield return $"R{root}.B{branch}.L{leaf}";
        foreach (string name in new[]
        {
            "A.B.C", "A.B.D", "A.Z.Leaf", "A.Z.Deep.Leaf", "A_B.Child",
            "A0.B.Child", "Case.Child", "case.Child", "0Leading._Part.叶子"
        }) yield return name;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + exception);
        }
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static GameplayTag T(string name) { return Tags[name]; }
    private static HashSet<string> Set(params string[] names) { return new HashSet<string>(names, StringComparer.Ordinal); }
    private static bool ChildOf(string child, string parent) { return child.StartsWith(parent + ".", StringComparison.Ordinal); }

    // Reference hierarchy is derived solely from strings, never runtime IDs,
    // ParentTags, storage order, or the runtime's relationship implementations.
    private static IEnumerable<string> AncestorsAndSelf(string name)
    {
        yield return name;
        for (int dot = name.LastIndexOf('.'); dot >= 0; dot = name.LastIndexOf('.'))
        {
            name = name.Substring(0, dot);
            yield return name;
        }
    }

    private static HashSet<string> Closure(IEnumerable<string> names)
    {
        HashSet<string> result = Set();
        foreach (string name in names)
            foreach (string ancestor in AncestorsAndSelf(name)) result.Add(ancestor);
        return result;
    }

    private static List<string> Read(GameplayTagEnumerator enumerator)
    {
        List<string> names = new List<string>();
        foreach (GameplayTag tag in enumerator) names.Add(tag.Name);
        return names;
    }

    private static void Same(IEnumerable<string> actual, IEnumerable<string> expected, string context)
    {
        string[] a = actual.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        string[] e = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Check(a.SequenceEqual(e), context + " expected [" + string.Join(",", e) + "] got [" + string.Join(",", a) + "]");
    }

    private static GameplayTagContainer Container(IEnumerable<string> names)
    {
        GameplayTagContainer container = new GameplayTagContainer();
        foreach (string name in names) container.AddTag(T(name));
        return container;
    }

    private static void AssertSet(IGameplayTagContainer actual, HashSet<string> explicitNames, string context)
    {
        HashSet<string> closure = Closure(explicitNames);
        Check(actual.ExplicitTagCount == explicitNames.Count, context + " explicit count");
        Check(actual.TagCount == closure.Count, context + " closure count");
        Check(actual.IsEmpty == (explicitNames.Count == 0), context + " empty");
        Same(Read(actual.GetExplicitTags()), explicitNames, context + " explicit enumeration");
        Same(Read(actual.GetTags()), closure, context + " closure enumeration");
        foreach (string name in Names)
        {
            Check(actual.HasTag(T(name)) == closure.Contains(name), context + " HasTag " + name);
            Check(actual.HasTagExact(T(name)) == explicitNames.Contains(name), context + " HasTagExact " + name);
        }
    }

    private static void Registry()
    {
        HashSet<int> ids = new HashSet<int>();
        foreach (string name in Names)
        {
            GameplayTag tag = T(name);
            Check(tag.RuntimeIndex > 0 && ids.Add(tag.RuntimeIndex), "unique nonzero ID " + name);
            Check(GameplayTagManager.RequestTag(name) == tag, "name lookup " + name);
            Check(GameplayTagManager.RequestTag(name, out GameplayTag found) && found == tag, "try lookup " + name);
            Check(tag.Label == name.Substring(name.LastIndexOf('.') + 1), "label " + name);
            Check(tag.HierarchyLevel == name.Count(c => c == '.') + 1, "level " + name);
            string[] ancestors = AncestorsAndSelf(name).ToArray();
            Check(tag.ParentTag == (ancestors.Length > 1 ? T(ancestors[1]) : GameplayTag.None), "direct parent " + name);
            Same(tag.ParentTags.ToArray().Select(t => t.Name), ancestors.Skip(1), "parents " + name);
            Same(tag.HierarchyTags.ToArray().Select(t => t.Name), ancestors, "hierarchy " + name);
            Same(tag.ChildTags.ToArray().Select(t => t.Name), Names.Where(n => ChildOf(n, name)), "descendants " + name);
            foreach (string other in Names)
            {
                Check(tag.IsChildOf(T(other)) == ChildOf(name, other), "IsChildOf " + name + "/" + other);
                Check(tag.IsParentOf(T(other)) == ChildOf(other, name), "IsParentOf " + name + "/" + other);
            }
        }
        Check(!GameplayTagManager.RequestTag("NotRegistered", out GameplayTag missing) && missing == GameplayTag.None,
            "missing cold-path lookup");
        Check(T("Case.Child") != T("case.Child"), "ordinal case-sensitive identity");
    }

    private static void SetBasics()
    {
        GameplayTagContainer container = new GameplayTagContainer();
        AssertSet(container, Set(), "new");
        container.AddTag(T("A.B.C"));
        container.AddTag(T("A.B.C"));
        container.AddTag(T("A.B.D"));
        container.AddTag(T("A.B"));
        AssertSet(container, Set("A.B.C", "A.B.D", "A.B"), "shared ancestors");
        container.RemoveTag(T("A.B.C"));
        AssertSet(container, Set("A.B.D", "A.B"), "one sibling removed");
        container.RemoveTag(T("A.B"));
        AssertSet(container, Set("A.B.D"), "explicit parent removed but implied parent retained");
        container.RemoveTag(T("A.B.C"));
        AssertSet(container, Set("A.B.D"), "missing removal is a no-op");
        container.RemoveTag(T("A.B.D"));
        AssertSet(container, Set(), "last contributor removed");
        container.AddTag(T("A.B"));
        container.AddTag(T("A.B.C"));
        container.RemoveTag(T("A.B.C"));
        AssertSet(container, Set("A.B"), "explicit parent survives last child");
        container.Clear();
        container.AddTag(T("R3.B3.L3"));
        AssertSet(container, Set("R3.B3.L3"), "reuse after clear");
    }

    private static void SetOperations()
    {
        GameplayTagContainer a = Container(new[] { "A.B.C", "A.B.D", "R0.B0" });
        GameplayTagContainer b = Container(new[] { "A.B.D", "R1.B1.L1" });
        GameplayTagContainer empty = new GameplayTagContainer();
        GameplayTagContainer copy = a.Clone();
        GameplayTagContainer.Copy(copy, copy);
        AssertSet(copy, Set("A.B.C", "A.B.D", "R0.B0"), "self copy");
        GameplayTagContainer.Copy(copy, empty);
        AssertSet(copy, Set(), "copy empty clears destination");
        GameplayTagContainer.Copy(copy, a);
        copy.RemoveTag(T("A.B.C"));
        AssertSet(a, Set("A.B.C", "A.B.D", "R0.B0"), "copy storage independent");
        AssertSet(GameplayTagContainer.Union(a, b), Set("A.B.C", "A.B.D", "R0.B0", "R1.B1.L1"), "union");
        AssertSet(GameplayTagContainer.Union(a, empty), Set("A.B.C", "A.B.D", "R0.B0"), "union rhs empty");
        AssertSet(GameplayTagContainer.Union(empty, a), Set("A.B.C", "A.B.D", "R0.B0"), "union lhs empty");
        AssertSet(GameplayTagContainer.Intersection(a, b), Set("A.B.D"), "intersection closure rebuilt");
        AssertSet(GameplayTagContainer.Intersection(Container(new[] { "A.B.C" }), Container(new[] { "A.B.D" })),
            Set(), "sibling intersection has no orphaned implied parents");
        AssertSet(GameplayTagContainer.Intersection(a, empty), Set(), "intersection empty");
        GameplayTagContainer.Intersection(copy, a, b);
        AssertSet(copy, Set("A.B.D"), "intersection replaces populated destination");
        GameplayTagContainer.Copy(copy, a);
        GameplayTagContainer.Intersection(copy, copy, b);
        AssertSet(copy, Set("A.B.D"), "intersection output aliases lhs");
        GameplayTagContainer.Copy(copy, b);
        GameplayTagContainer.Intersection(copy, a, copy);
        AssertSet(copy, Set("A.B.D"), "intersection output aliases rhs");
        copy.AddTags(copy);
        AssertSet(copy, Set("A.B.D"), "add self");
        copy.RemoveTags(copy);
        AssertSet(copy, Set(), "remove self");
        copy.AddTags(a);
        copy.RemoveTags(b);
        AssertSet(copy, Set("A.B.C", "R0.B0"), "bulk removes available members and ignores absent members");
        List<GameplayTag> added = new List<GameplayTag>();
        List<GameplayTag> removed = new List<GameplayTag>();
        a.GetDiffExplicitTags(b, added, removed);
        Same(added.Select(t => t.Name), new[] { "A.B.C", "R0.B0" }, "diff added");
        Same(removed.Select(t => t.Name), new[] { "R1.B1.L1" }, "diff removed");

        GameplayTagCountContainer counted = new GameplayTagCountContainer();
        counted.AddTag(T("A.B.C")); counted.AddTag(T("A.B.C")); counted.AddTag(T("A.B.D"));
        GameplayTagContainer.Copy(copy, counted);
        copy.RemoveTag(T("A.B.C"));
        AssertSet(copy, Set("A.B.D"), "count-to-set copy normalizes multiplicity");
        copy.RemoveTag(T("A.B.D"));
        AssertSet(copy, Set(), "normalized closure clears after last set removal");

        GameplayTagContainer reserved = new GameplayTagContainer();
        reserved.EnsureCapacity(Names.Length);
        foreach (string name in Names) reserved.AddTag(T(name));
        foreach (string name in Names.Reverse()) reserved.RemoveTag(T(name));
        AssertSet(reserved, Set(), "reserved storage add/remove all");
    }

    private static void AssertQueries(GameplayTagContainer holder, HashSet<string> holderNames,
        GameplayTagContainer requirements, HashSet<string> requiredNames, string context)
    {
        HashSet<string> closure = Closure(holderNames);
        Check(holder.HasAny(requirements) == requiredNames.Any(closure.Contains), context + " any");
        Check(holder.HasAll(requirements) == requiredNames.All(closure.Contains), context + " all");
        Check(holder.HasAnyExact(requirements) == requiredNames.Any(holderNames.Contains), context + " any exact");
        Check(holder.HasAllExact(requirements) == requiredNames.All(holderNames.Contains), context + " all exact");
    }

    private static void Queries()
    {
        HashSet<string> names = Set("A", "A.B.C", "A.Z", "R0.B0.L0", "R2.B2.L2");
        GameplayTagContainer holder = Container(names);
        foreach (string name in Names)
        {
            List<GameplayTag> actual = new List<GameplayTag>();
            holder.GetExplicitParentTags(T(name), actual);
            Same(actual.Select(t => t.Name), names.Where(n => ChildOf(name, n)), "explicit parents " + name);
            actual.Clear();
            holder.GetParentTags(T(name), actual);
            Same(actual.Select(t => t.Name), Closure(names).Where(n => ChildOf(name, n)), "parents " + name);
            actual.Clear();
            holder.GetExplicitChildTags(T(name), actual);
            Same(actual.Select(t => t.Name), names.Where(n => ChildOf(n, name)), "explicit children " + name);
            actual.Clear();
            holder.GetChildTags(T(name), actual);
            Same(actual.Select(t => t.Name), Closure(names).Where(n => ChildOf(n, name)), "children " + name);
        }
        List<HashSet<string>> queries = new List<HashSet<string>>
        {
            Set(), Set("A"), Set("A.B"), Set("A.B", "R0.B0"), Set("A", "R1", "R2"),
            Set("A.B.C", "A.Z", "R2.B2.L2"), Set("A.B.D", "R1.B1", "R3.B3.L3")
        };
        foreach (HashSet<string> query in queries) AssertQueries(holder, names, Container(query), query, "query fixture");
        GameplayTagContainer single = Container(new[] { "A.B.C" });
        AssertQueries(single, Set("A.B.C"), Container(new[] { "A.B.C" }), Set("A.B.C"), "singleton same ID");
        AssertQueries(single, Set("A.B.C"), Container(new[] { "A.B.D" }), Set("A.B.D"), "singleton different ID");
        AssertQueries(single, Set("A.B.C"), Container(new[] { "A.B" }), Set("A.B"), "singleton implied parent");
        AssertQueries(new GameplayTagContainer(), Set(), Container(new[] { "A.B.C" }), Set("A.B.C"), "singleton empty holder");
        IGameplayTagContainer interfaceHolder = single;
        Check(interfaceHolder.HasAllExact(Container(new[] { "A.B.C" })), "singleton interface same ID");
        Check(!interfaceHolder.HasAllExact(Container(new[] { "A.B.D" })), "singleton interface different ID");
        GameplayTagRequirements defaults = default;
        Check(defaults.Matches(new GameplayTagContainer()), "default requirements match");
        GameplayTagContainer requiredA = Container(new[] { "R0" });
        GameplayTagContainer requiredB = Container(new[] { "R2" });
        Check(GameplayTagContainerExtensionMethods.HasAll(holder, requiredA, requiredB), "combined requirements covered");
        requiredB.AddTag(T("R3"));
        Check(!GameplayTagContainerExtensionMethods.HasAll(holder, requiredA, requiredB), "combined requirements retain disjoint missing tag");
        GameplayTagContainer staticTags = Container(new[] { "R0.B0.L0" });
        GameplayTagContainer dynamicTags = Container(new[] { "R2.B2.L2" });
        GameplayTagRequirements requirements = new GameplayTagRequirements(null, Container(new[] { "R0", "R2" }));
        Check(requirements.Matches(staticTags, dynamicTags), "requirements use union of holders");
        requirements.m_ForbiddenTags = Container(new[] { "R2" });
        Check(!requirements.Matches(staticTags, dynamicTags), "forbidden tag in either holder fails");
    }

    private static HashSet<string> RandomSet(Random random, int max)
    {
        HashSet<string> names = Set();
        int count = random.Next(max + 1);
        while (names.Count < count) names.Add(Names[random.Next(Names.Length)]);
        return names;
    }

    private static void RandomizedSets()
    {
        Random random = new Random(872341);
        GameplayTagContainer container = new GameplayTagContainer();
        HashSet<string> expected = Set();
        for (int step = 0; step < 12000; step++)
        {
            string name = Names[random.Next(Names.Length)];
            switch (random.Next(12))
            {
                case 0: case 1: case 2: case 3:
                    container.AddTag(T(name)); expected.Add(name); break;
                case 4: case 5: case 6:
                    container.RemoveTag(T(name)); expected.Remove(name); break;
                case 7:
                    HashSet<string> add = RandomSet(random, 24);
                    container.AddTags(Container(add)); expected.UnionWith(add); break;
                case 8:
                    HashSet<string> remove = RandomSet(random, 24);
                    container.RemoveTags(Container(remove)); expected.ExceptWith(remove); break;
                case 9:
                    if (random.Next(5) == 0) { container.Clear(); expected.Clear(); }
                    else { container.AddTags(container); }
                    break;
                case 10:
                    HashSet<string> query = RandomSet(random, 16);
                    AssertQueries(container, expected, Container(query), query, "random query " + step);
                    break;
                case 11:
                    GameplayTagContainer.Copy(container, container);
                    break;
            }
            AssertSet(container, expected, "random set step " + step);
        }
        // Repeatedly exercise every stored key, bucket collisions, promotion from
        // tiny storage to hashing, backward-shift deletion and dense swap repair.
        for (int pass = 0; pass < 12; pass++)
        {
            container.Clear(); expected.Clear();
            foreach (string name in Names.OrderBy(_ => random.Next())) { container.AddTag(T(name)); expected.Add(name); }
            foreach (string name in Names.OrderBy(_ => random.Next()))
            {
                container.RemoveTag(T(name)); expected.Remove(name);
                AssertSet(container, expected, "dense deletion pass " + pass);
            }
        }
    }

    private static Dictionary<string, int> CountClosure(Dictionary<string, int> explicitCounts)
    {
        Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> pair in explicitCounts)
            foreach (string ancestor in AncestorsAndSelf(pair.Key))
                totals[ancestor] = Get(totals, ancestor) + pair.Value;
        return totals;
    }

    private static int Get(Dictionary<string, int> counts, string name)
    {
        return counts.TryGetValue(name, out int count) ? count : 0;
    }

    private static void Increment(Dictionary<string, int> counts, string name, int amount)
    {
        int count = Get(counts, name) + amount;
        if (count > 0) counts[name] = count; else counts.Remove(name);
    }

    private static void AssertCounts(IGameplayTagCountContainer container, Dictionary<string, int> explicitCounts, string context)
    {
        AssertSet(container, new HashSet<string>(explicitCounts.Keys, StringComparer.Ordinal), context);
        Dictionary<string, int> totals = CountClosure(explicitCounts);
        foreach (string name in Names)
        {
            Check(container.GetExplicitTagCount(T(name)) == Get(explicitCounts, name), context + " explicit multiplicity " + name);
            Check(container.GetTagCount(T(name)) == Get(totals, name), context + " closure multiplicity " + name);
        }
    }

    private static void RandomizedCounts()
    {
        Random random = new Random(710923);
        GameplayTagCountContainer container = new GameplayTagCountContainer();
        Dictionary<string, int> expected = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int step = 0; step < 7000; step++)
        {
            string name = Names[random.Next(Names.Length)];
            switch (random.Next(14))
            {
                case 0: case 1: case 2: case 3: case 4:
                    container.AddTag(T(name)); Increment(expected, name, 1); break;
                case 5: case 6: case 7: case 8:
                    container.RemoveTag(T(name)); if (Get(expected, name) > 0) Increment(expected, name, -1); break;
                case 9:
                    HashSet<string> add = RandomSet(random, 8);
                    container.AddTags(Container(add)); foreach (string added in add) Increment(expected, added, 1); break;
                case 10:
                    HashSet<string> remove = RandomSet(random, 8);
                    container.RemoveTags(Container(remove));
                    foreach (string removed in remove) if (Get(expected, removed) > 0) Increment(expected, removed, -1);
                    break;
                case 11:
                    container.AddTags(container);
                    foreach (string item in expected.Keys.ToArray()) Increment(expected, item, 1);
                    break;
                case 12:
                    container.RemoveTags(container);
                    foreach (string item in expected.Keys.ToArray()) Increment(expected, item, -1);
                    break;
                case 13:
                    if (random.Next(4) == 0) { container.Clear(); expected.Clear(); }
                    break;
            }
            AssertCounts(container, expected, "random count step " + step);
        }
    }

    private static string Event(string kind, GameplayTag tag, int count) { return kind + ":" + tag.Name + ":" + count; }

    private static void CountEvents()
    {
        GameplayTagCountContainer container = new GameplayTagCountContainer();
        List<string> actual = new List<string>();
        List<string> expected = new List<string>();
        Dictionary<string, int> state = new Dictionary<string, int>(StringComparer.Ordinal);
        container.OnAnyTagCountChange += (tag, count) =>
        {
            actual.Add(Event("global.any", tag, count));
            AssertCounts(container, state, "state committed before global callback");
        };
        container.OnAnyTagNewOrRemove += (tag, count) => actual.Add(Event("global.edge", tag, count));
        OnTagCountChangedDelegate specificAny = (tag, count) => actual.Add(Event("specific.any", tag, count));
        OnTagCountChangedDelegate specificEdge = (tag, count) => actual.Add(Event("specific.edge", tag, count));
        container.RegisterTagEventCallback(T("A.B"), GameplayTagEventType.AnyCountChange, specificAny);
        container.RegisterTagEventCallback(T("A.B"), GameplayTagEventType.NewOrRemoved, specificEdge);

        void Change(string name, bool add)
        {
            Dictionary<string, int> before = CountClosure(state);
            Increment(state, name, add ? 1 : -1);
            Dictionary<string, int> after = CountClosure(state);
            foreach (string ancestor in AncestorsAndSelf(name))
            {
                int previous = Get(before, ancestor), current = Get(after, ancestor);
                expected.Add(Event("global.any", T(ancestor), current));
                if (ancestor == "A.B") expected.Add(Event("specific.any", T(ancestor), current));
                if ((previous == 0) != (current == 0))
                {
                    expected.Add(Event("global.edge", T(ancestor), current));
                    if (ancestor == "A.B") expected.Add(Event("specific.edge", T(ancestor), current));
                }
            }
            if (add) container.AddTag(T(name)); else container.RemoveTag(T(name));
            Same(actual, expected, "event multiset after change");
        }
        Change("A.B.C", true); Change("A.B.C", true); Change("A.B.D", true);
        Change("A.B.C", false); Change("A.B.C", false); Change("A.B.D", false);
        Change("A.B.C", true); Change("A.B.C", true);
        foreach (string name in CountClosure(state).Keys)
        {
            expected.Add(Event("global.any", T(name), 0));
            expected.Add(Event("global.edge", T(name), 0));
            if (name == "A.B")
            {
                expected.Add(Event("specific.any", T(name), 0)); expected.Add(Event("specific.edge", T(name), 0));
            }
        }
        state.Clear(); container.Clear();
        Same(actual, expected, "Clear emits all zero transitions to both event kinds");
        container.RemoveTagEventCallback(T("A.B"), GameplayTagEventType.AnyCountChange, specificAny);
        container.RemoveTagEventCallback(T("A.B"), GameplayTagEventType.NewOrRemoved, specificEdge);
        actual.Clear(); state["A.B.C"] = 1; container.AddTag(T("A.B.C"));
        Check(actual.All(e => !e.StartsWith("specific.", StringComparison.Ordinal)), "specific callbacks unregistered");

        GameplayTagCountContainer bulk = new GameplayTagCountContainer();
        Dictionary<string, int> bulkState = new Dictionary<string, int> { ["A.B.C"] = 1, ["A.B.D"] = 1 };
        bulk.OnAnyTagCountChange += (_, __) => AssertCounts(bulk, bulkState, "whole bulk operation committed before event");
        bulk.AddTags(Container(bulkState.Keys));
    }

    private sealed class DeliberateCallbackException : Exception { }

    private static void CallbackBehavior()
    {
        GameplayTagCountContainer container = new GameplayTagCountContainer();
        int depth = 0, maxDepth = 0;
        bool added = false;
        container.OnAnyTagCountChange += (tag, count) =>
        {
            depth++; maxDepth = Math.Max(maxDepth, depth);
            if (tag == T("A.B.C") && count == 1 && !added)
            {
                added = true;
                Check(container.GetTagCount(T("A")) == 1, "first mutation fully committed");
                container.AddTag(T("A.B.D"));
            }
            depth--;
        };
        container.AddTag(T("A.B.C"));
        AssertCounts(container, new Dictionary<string, int> { ["A.B.C"] = 1, ["A.B.D"] = 1 }, "reentrant add");
        Check(maxDepth == 1, "reentrant callbacks are queued, not recursively dispatched");

        GameplayTagCountContainer throwing = new GameplayTagCountContainer();
        OnTagCountChangedDelegate bad = (_, __) => throw new DeliberateCallbackException();
        throwing.OnAnyTagCountChange += bad;
        bool propagated = false;
        try { throwing.AddTag(T("A.B.C")); } catch (DeliberateCallbackException) { propagated = true; }
        Check(propagated, "callback exception propagated");
        AssertCounts(throwing, new Dictionary<string, int> { ["A.B.C"] = 1 }, "throw leaves full mutation committed");
        throwing.OnAnyTagCountChange -= bad;
        int recovered = 0;
        throwing.OnAnyTagCountChange += (_, __) => recovered++;
        throwing.RemoveTag(T("A.B.C"));
        Check(recovered == 3, "pending callbacks cleared and next dispatch recovers");
        AssertCounts(throwing, new Dictionary<string, int>(), "remove after callback exception");

        GameplayTagCountContainer clearing = new GameplayTagCountContainer();
        bool cleared = false;
        clearing.OnAnyTagCountChange += (tag, count) =>
        {
            if (tag == T("A.B.C") && count == 1 && !cleared) { cleared = true; clearing.Clear(); }
        };
        clearing.AddTag(T("A.B.C"));
        AssertCounts(clearing, new Dictionary<string, int>(), "reentrant clear");
    }

    private static void Hierarchy()
    {
        GameplayTagCountContainer grandparent = new GameplayTagCountContainer();
        GameplayTagHierarchicalContainer left = new GameplayTagHierarchicalContainer { Parent = grandparent };
        GameplayTagCountContainer right = new GameplayTagCountContainer();
        GameplayTagHierarchicalContainer child = new GameplayTagHierarchicalContainer { Parent = left };
        child.AddTag(T("A.B.C")); child.AddTag(T("A.B.C")); child.AddTag(T("A.B.D"));
        Dictionary<string, int> state = new Dictionary<string, int> { ["A.B.C"] = 2, ["A.B.D"] = 1 };
        AssertCounts(child, state, "child multiplicity"); AssertCounts(left, state, "parent multiplicity");
        AssertCounts(grandparent, state, "grandparent multiplicity");
        right.AddTag(T("A.B.C"));
        child.Parent = right;
        AssertCounts(left, new Dictionary<string, int>(), "old parent detached");
        AssertCounts(grandparent, new Dictionary<string, int>(), "old grandparent detached");
        AssertCounts(right, new Dictionary<string, int> { ["A.B.C"] = 3, ["A.B.D"] = 1 }, "new parent exact transfer");
        child.Parent = right;
        AssertCounts(right, new Dictionary<string, int> { ["A.B.C"] = 3, ["A.B.D"] = 1 }, "same-parent assignment no-op");
        child.RemoveTags(Container(new[] { "A.B.D", "R0" }));
        AssertCounts(child, new Dictionary<string, int> { ["A.B.C"] = 2 }, "bulk hierarchy removal only requested tags");
        AssertCounts(right, new Dictionary<string, int> { ["A.B.C"] = 3 }, "unrelated child contributions preserved");
        child.Clear();
        AssertCounts(child, new Dictionary<string, int>(), "child cleared");
        AssertCounts(right, new Dictionary<string, int> { ["A.B.C"] = 1 }, "clear preserves parent own contribution");
        child.Parent = left;
        child.OnAnyTagCountChange += (_, __) =>
        {
            Check(child.GetExplicitTagCount(T("A.B.C")) == 1, "child committed before callback");
            Check(left.GetExplicitTagCount(T("A.B.C")) == 1, "parent committed before child callback");
            Check(grandparent.GetExplicitTagCount(T("A.B.C")) == 1, "grandparent committed before child callback");
        };
        child.AddTag(T("A.B.C"));
    }

    private static void RandomizedHierarchy()
    {
        Random random = new Random(312991);
        GameplayTagHierarchicalContainer[] nodes = Enumerable.Range(0, 3).Select(_ => new GameplayTagHierarchicalContainer()).ToArray();
        Dictionary<string, int>[] local = Enumerable.Range(0, 3).Select(_ => new Dictionary<string, int>(StringComparer.Ordinal)).ToArray();
        int[] parents = { -1, -1, 0 };
        nodes[2].Parent = nodes[0];
        for (int step = 0; step < 1400; step++)
        {
            int node = random.Next(nodes.Length);
            string name = Names[random.Next(Names.Length)];
            if (random.Next(6) == 0)
            {
                int next = random.Next(3) - 1;
                nodes[2].Parent = next < 0 ? null : nodes[next]; parents[2] = next;
            }
            else if (random.Next(2) == 0 || Get(local[node], name) == 0)
            {
                nodes[node].AddTag(T(name)); Increment(local[node], name, 1);
            }
            else { nodes[node].RemoveTag(T(name)); Increment(local[node], name, -1); }
            for (int target = 0; target < nodes.Length; target++)
            {
                Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int source = 0; source < nodes.Length; source++)
                {
                    int ancestor = source;
                    while (ancestor >= 0 && ancestor != target) ancestor = parents[ancestor];
                    if (ancestor != target) continue;
                    foreach (KeyValuePair<string, int> pair in local[source]) Increment(totals, pair.Key, pair.Value);
                }
                AssertCounts(nodes[target], totals, "random hierarchy " + step + " node " + target);
            }
        }
    }

    private static void HierarchyCallbacks()
    {
        for (int throwAt = 0; throwAt < 3; throwAt++)
        {
            GameplayTagCountContainer grandparent = new GameplayTagCountContainer();
            GameplayTagHierarchicalContainer parent = new GameplayTagHierarchicalContainer { Parent = grandparent };
            GameplayTagHierarchicalContainer child = new GameplayTagHierarchicalContainer { Parent = parent };
            IGameplayTagCountContainer[] chain = { child, parent, grandparent };
            OnTagCountChangedDelegate bad = (_, __) => throw new DeliberateCallbackException();
            chain[throwAt].OnAnyTagCountChange += bad;
            bool propagated = false;
            try { child.AddTag(T("A.B.C")); } catch (DeliberateCallbackException) { propagated = true; }
            Check(propagated, "hierarchy callback exception propagated at level " + throwAt);
            foreach (IGameplayTagCountContainer node in chain)
                AssertCounts(node, new Dictionary<string, int> { ["A.B.C"] = 1 }, "entire hierarchy committed despite throw");
            chain[throwAt].OnAnyTagCountChange -= bad;
            int recovered = 0;
            foreach (IGameplayTagCountContainer node in chain)
                node.OnAnyTagCountChange += (_, __) =>
                {
                    recovered++;
                    foreach (IGameplayTagCountContainer member in chain) Check(member.IsEmpty, "chain committed before recovery callback");
                };
            child.RemoveTag(T("A.B.C"));
            Check(recovered == 9, "all hierarchy queues recovered without stale callbacks");
        }

        GameplayTagCountContainer oldRoot = new GameplayTagCountContainer();
        GameplayTagCountContainer newRoot = new GameplayTagCountContainer();
        GameplayTagHierarchicalContainer oldParent = new GameplayTagHierarchicalContainer { Parent = oldRoot };
        GameplayTagHierarchicalContainer newParent = new GameplayTagHierarchicalContainer { Parent = newRoot };
        GameplayTagHierarchicalContainer moving = new GameplayTagHierarchicalContainer { Parent = oldParent };
        newParent.AddTag(T("A.B.C"));
        moving.AddTag(T("A.B.C")); moving.AddTag(T("A.B.C"));
        int notifications = 0;
        OnTagCountChangedDelegate verifyTransfer = (_, __) =>
        {
            notifications++;
            Check(oldRoot.IsEmpty && oldParent.IsEmpty, "old chain detached before reparent callback");
            Check(newParent.GetExplicitTagCount(T("A.B.C")) == 3 && newRoot.GetExplicitTagCount(T("A.B.C")) == 3,
                "new chain exact transfer committed before reparent callback");
            Check(moving.GetExplicitTagCount(T("A.B.C")) == 2 && ReferenceEquals(moving.Parent, newParent),
                "moving child stable and parent reference committed");
        };
        oldRoot.OnAnyTagCountChange += verifyTransfer; newRoot.OnAnyTagCountChange += verifyTransfer;
        oldParent.OnAnyTagCountChange += verifyTransfer; newParent.OnAnyTagCountChange += verifyTransfer;
        moving.Parent = newParent;
        Check(notifications == 12, "reparent emits one change per affected tag and chain member");
    }

    private static void Serialization()
    {
        GameplayTagContainer container = Container(new[] { "R0.B0.L0" });
        container.m_SerializedExplicitTags = new List<string> { "A.B.D", "A.B.C", "A.B.C", "NotRegistered" };
        container.OnAfterDeserialize();
        AssertSet(container, Set("A.B.C", "A.B.D"), "deserialize rebuilds and normalizes explicit membership");
        container.OnBeforeSerialize();
        Check(container.m_SerializedExplicitTags.SequenceEqual(new[] { "A.B.C", "A.B.D" }), "serialization uses stable ordered names");
        DataContractSerializer serializer = new DataContractSerializer(typeof(GameplayTagContainer));
        using (MemoryStream stream = new MemoryStream())
        {
            serializer.WriteObject(stream, container);
            stream.Position = 0;
            GameplayTagContainer restored = (GameplayTagContainer)serializer.ReadObject(stream);
            AssertSet(restored, Set("A.B.C", "A.B.D"), "DataContractSerializer round trip");
            restored.RemoveTag(T("A.B.C")); restored.RemoveTag(T("A.B.D"));
            AssertSet(restored, Set(), "round trip reconstructs closure reference counts");
        }
    }

    private static void BindingAndPooling()
    {
        GameplayTagCountContainer counted = new GameplayTagCountContainer();
        GameplayTagContainerBinds binds = new GameplayTagContainerBinds(counted);
        List<bool> values = new List<bool>();
        binds.Bind(T("A.B"), values.Add);
        counted.AddTag(T("A.B.C")); counted.AddTag(T("A.B.C")); counted.RemoveTag(T("A.B.C")); counted.Clear();
        Check(values.SequenceEqual(new[] { false, true, false }), "binding receives current state and edges only");
        binds.UnbindAll(); counted.AddTag(T("A.B.C"));
        Check(values.Count == 3, "unbound callbacks not retained");
        using (GameplayTagContainerPool.Get(out GameplayTagContainer pooled)) pooled.AddTag(T("A.B.C"));
        using (GameplayTagContainerPool.Get(out GameplayTagContainer pooled)) AssertSet(pooled, Set(), "pooled container cleared");
    }
}
