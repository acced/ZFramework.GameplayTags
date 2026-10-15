using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameplayTags;
using UnityEngine;

// Link the unchanged Roadmap harness. Reuse its serialized fixture, oracle,
// representative-identity checks and deterministic transcript rather than fork them.
internal static class C12Correctness
{
    static object Call(string name, params object[] args)
    {
        try { return typeof(Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Check(bool value, string why) => Call("Check", value, why);
    static GameplayTag Raw(string name) => (GameplayTag)Call("Raw", name);
    static GameplayTagContainer Fixture(params string[] names) => (GameplayTagContainer)Call("Fresh", (object)names);
    static void Init(params string[] names) => Call("Init", names, Array.Empty<GameplayTagRedirect>());
    static void State(GameplayTagContainer c, IEnumerable<string> names, string why) => Call("State", c, names, why);
    static Dictionary<string, string> Reps(GameplayTagContainer first, GameplayTagContainer second = null) =>
        (Dictionary<string, string>)Call("Representatives", first, second);
    static void Identity(GameplayTagContainer c, Dictionary<string, string> expected, string why) => Call("SameRepresentatives", c, expected, why);
    static string Name(int i) => "N" + i.ToString("D4");
    static string[] Range(int count, int start = 0) => Enumerable.Range(start, count).Select(Name).ToArray();
    static List<GameplayTag> Tags(GameplayTagContainer c) => (List<GameplayTag>)typeof(GameplayTagContainer)
        .GetField("m_GameplayTags", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    static void CleanTail(GameplayTagContainer c, string why)
    {
        // .NET-only memory-retention diagnostic, not a List version/resize contract.
        // Inspect only slots outside Count, never assert capacity growth or _version.
        var list = Tags(c);
        var array = (GameplayTag[])typeof(List<GameplayTag>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(list);
        var name = typeof(GameplayTag).GetField("m_Name", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = list.Count; i < array.Length; i++) Check(name.GetValue(array[i]) == null, why + " cleared trailing slot " + i);
    }
    static void CopyMatrix()
    {
        Init(Range(256));
        int[] sizes = { 0, 1, 2, 4, 8, 16, 32, 64 };
        foreach (int sourceCount in sizes)
        foreach (int targetCount in sizes)
        foreach (int spare in new[] { 0, 1, 128 })
        foreach (bool equalNames in new[] { false, true })
        {
            var source = Fixture(Range(sourceCount));
            var target = Fixture(Range(targetCount, equalNames ? 0 : 128));
            Tags(target).Capacity = targetCount + spare;
            var expected = Reps(source);
            string label = $"copy {sourceCount}/{targetCount}/+{spare}/same={equalNames}";
            target.CopyFrom(source);
            State(target, expected.Keys, label); Identity(target, expected, label);
            State(source, expected.Keys, label + " source unchanged"); CleanTail(target, label);
        }
        foreach (int count in sizes)
        {
            var target = Fixture(Range(count)); var representatives = Reps(target);
            target.CopyFrom(target); State(target, representatives.Keys, "self copy " + count); Identity(target, representatives, "self copy");
            target.CopyFrom(null); State(target, Array.Empty<string>(), "null copy " + count); CleanTail(target, "null copy");
        }
    }
    sealed class CallbackFailure : Exception { }
    static void CopyCallbacks()
    {
        foreach (bool initialized in new[] { true, false })
        foreach (bool singleton in new[] { true, false })
        foreach (string action in new[] { "observe", "target", "source", "throw" })
        {
            Init("A", "Z", "ZZ");
            var source = Fixture(singleton ? new[] { "U" } : new[] { "A", "U", "Z" });
            var target = Fixture("Z");
            string label = $"callback initialized={initialized}/singleton={singleton}/{action}";
            int warnings = 0, events = 0;
            Action observer = () => { events++; State(target, Array.Empty<string>(), label + " init observes clear"); };
            if (!initialized)
            {
                var settings = (GameplayTagSettings)Call("Settings", new[] { "A", "Z", "ZZ" }, Array.Empty<GameplayTagRedirect>());
                GameplayTagManager.ResetForTests(); Resources.LoadCalls = 0; Resources.DefaultAsset = settings;
                GameplayTagManager.RegistryChanged += observer;
            }
            Debug.WarningHook = message =>
            {
                if (!message.ToString().EndsWith(": U", StringComparison.Ordinal)) return;
                warnings++;
                State(target, singleton ? Array.Empty<string>() : new[] { "A" }, label + " warning partial");
                if (action == "target") target.AddTag(Raw("ZZ"));
                if (action == "source") { source.RemoveTag(Raw("U")); source.AddTag(Raw("ZZ")); }
                if (action == "throw") throw new CallbackFailure();
            };
            bool thrown = false;
            try { target.CopyFrom(source); }
            catch (CallbackFailure) { thrown = true; }
            finally { Debug.WarningHook = null; GameplayTagManager.RegistryChanged -= observer; Resources.DefaultAsset = null; }
            Check(warnings == 1, label + " exactly one warning");
            Check(thrown == (action == "throw"), label + " throw propagation");
            Check(events == (initialized ? 0 : 1), label + " init event count");
            if (!initialized) Check(Resources.LoadCalls == 1, label + " one settings load");
            var expected = new List<string>();
            if (!singleton) expected.Add("A");
            if (action != "throw")
            {
                if (!singleton) expected.Add(action == "source" ? "ZZ" : "Z");
                if (action == "target") expected.Add("ZZ");
            }
            State(target, expected, label + " final"); CleanTail(target, label);
            if (action == "source") State(source, singleton ? new[] { "ZZ" } : new[] { "A", "Z", "ZZ" }, label + " live source");
        }
        foreach (string kind in new[] { "empty", "null", "self" })
        {
            var target = Fixture("U"); var empty = Fixture();
            GameplayTagManager.ResetForTests(); Resources.LoadCalls = 0;
            int warnings = 0; Debug.WarningHook = _ => warnings++;
            try { target.CopyFrom(kind == "self" ? target : kind == "null" ? null : empty); }
            finally { Debug.WarningHook = null; }
            Check(!GameplayTagManager.IsInitialized && Resources.LoadCalls == 0 && warnings == 0, kind + " noninitializing copy");
            State(target, kind == "self" ? new[] { "U" } : Array.Empty<string>(), kind + " no-init state");
        }
        // Reuse the established initialization/source-reentrancy and alias callback cases.
        Call("ObservableCallbacks"); Call("MutatingCallbacks"); Call("UnionAliasCallbacks");
    }
    static void MergeCase(string label, string[] destination, string[] incoming)
    {
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (bool spare in new[] { false, true })
        {
            var target = Fixture(destination); var source = Fixture(incoming);
            Tags(target).Capacity = spare ? destination.Length + incoming.Length + 16 : destination.Length;
            // mode 0: Append; mode 1: union-left alias; mode 2: union-right alias.
            var expected = mode == 2 ? Reps(source, target) : Reps(target, source);
            var sourceReps = Reps(source);
            if (mode == 0) target.AppendTags(source);
            else if (mode == 1) target.SetUnion(target, source);
            else target.SetUnion(source, target);
            string why = $"merge {label}/mode={mode}/spare={spare}";
            State(target, expected.Keys, why); Identity(target, expected, why); CleanTail(target, why);
            State(source, sourceReps.Keys, why + " source unchanged"); Identity(source, sourceReps, why + " source identity");
        }
    }
    static void MergeMatrix()
    {
        Init(Range(256));
        foreach (int size in new[] { 4, 8, 32, 64 })
        {
            var destination = Enumerable.Range(0, size).Select(i => Name(2 * i + 2)).ToArray();
            // Missing values remain within destination bounds: exercise failed containment,
            // including a long matched prefix followed by failure rather than a range shortcut.
            foreach (string pattern in new[] { "first", "middle", "last", "multiple", "contained" })
            {
                var incoming = destination.Where((_, i) => i % 2 == 0).ToList();
                if (pattern == "first" || pattern == "multiple") incoming.Add(Name(3));
                if (pattern == "middle" || pattern == "multiple") incoming.Add(Name(size + 1));
                if (pattern == "last" || pattern == "multiple") incoming.Add(Name(2 * size - 1));
                incoming = incoming.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                // First missing source item, with destination[0] smaller than source[0].
                if (pattern == "first") incoming.Remove(destination[0]);
                MergeCase(size + "/" + pattern, destination, incoming.ToArray());
            }
        }
        MergeCase("empty both", Array.Empty<string>(), Array.Empty<string>());
        MergeCase("empty destination", Array.Empty<string>(), Range(8));
        MergeCase("empty source", Range(8), Array.Empty<string>());
        MergeCase("singleton equal", Range(1), Range(1));
        MergeCase("ordered adjacent", Range(8), Range(8, 8));
        MergeCase("reverse adjacent", Range(8, 8), Range(8));
        MergeCase("interleaved", new[] { Name(0), Name(2), Name(4) }, new[] { Name(1), Name(3), Name(5) });
        MergeCase("equal all", Range(32), Range(32));
        MergeCase("source larger", new[] { Name(4), Name(8) }, Range(16));
        MergeCase("lower boundary miss", Range(8, 1), new[] { Name(0), Name(1), Name(7) });
        MergeCase("upper boundary miss", Range(8), new[] { Name(0), Name(6), Name(8) });
        var self = Fixture(Range(8)); var reps = Reps(self);
        self.AppendTags(null); self.AppendTags(self); self.SetUnion(self, self);
        State(self, reps.Keys, "merge null/self/all alias"); Identity(self, reps, "merge null/self/all alias");
    }
    static int Main()
    {
        try
        {
            CopyMatrix(); CopyCallbacks(); MergeMatrix();
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var transcript = (StringBuilder)typeof(Program).GetField("transcript", flags).GetValue(null);
            Console.WriteLine("PASS C12 supplement assertions=" + typeof(Program).GetField("assertions", flags).GetValue(null));
            Console.WriteLine("TRANSCRIPT " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript.ToString()))));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL C12 supplement: " + ex); return 1; }
    }
}
