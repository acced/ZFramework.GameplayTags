// Portable focused checks: original semantic bodies; TakeLast(1) becomes Skip(Count - 1).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using GameplayTags;

internal static class Probe
{
    static GameplayTag A, B;
    static int checks, sink;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Eq<T>(IEnumerable<T> actual, params T[] expected) => Check(actual.SequenceEqual(expected), "sequence: " + string.Join(",", actual));
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T)); }
    static void Init() { GameplayTagManager.Initialize(new GameplayTagRegistration("A.Child"), new GameplayTagRegistration("B.Child")); A = GameplayTagManager.RequestTag("A.Child"); B = GameplayTagManager.RequestTag("B.Child"); }
    static void Nop(bool value) { if (value) sink++; }
    static object State(GameplayTagContainerBinds binds) => typeof(GameplayTagContainerBinds).GetField("m_State", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binds);
    static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
    static void Empty(GameplayTagContainerBinds binds, GameplayTagCountContainer container)
    {
        object state = State(binds);
        Check((int)Field(state, "Count") == 0, "bind count cleared");
        foreach (object item in (Array)Field(state, "Items")) Check(Field(item, "Callback") == null, "slot callback cleared");
        Check((int)Field(container, "m_CallbackCount") == 0, "container callbacks cleared");
    }
    internal static void Run() { A = GameplayTagManager.RequestTag("A.B.C"); B = GameplayTagManager.RequestTag("A.B.D"); Test(); }
    static void Test()
    {
        var c = new GameplayTagCountContainer(); var binds = new GameplayTagContainerBinds(c); var copy = binds;
        var log = new List<string>(); Action<bool> shared = value => log.Add("S" + value);
        binds.Bind(A, shared); copy.Bind(A, shared); binds.Bind(B, v => log.Add("B" + v));
        c.AddTag(A); c.AddTag(A); c.RemoveTag(A); c.AddTag(B); c.RemoveTag(A); c.RemoveTag(B);
        Eq(log, "SFalse", "SFalse", "BFalse", "STrue", "STrue", "BTrue", "SFalse", "SFalse", "BFalse");
        copy.Dispose(); Empty(binds, c); c.AddTag(A); Check(log.Count == 9, "copy unbinds all"); binds.UnbindAll(); copy.Dispose();
        binds.Bind(A, shared); Eq(log.Skip(log.Count - 1), "STrue"); binds.Dispose(); Empty(binds, c);
        Console.WriteLine("PASS repeated action, multiple tags, edges, order, shared copies, unsubscribe and reuse");

        c = new GameplayTagCountContainer(); binds = new GameplayTagContainerBinds(c); log.Clear(); bool nested = false;
        binds.Bind(A, v => { log.Add("outer" + v); if (!nested) { nested = true; binds.Bind(B, x => log.Add("inner" + x)); } });
        c.AddTag(A); c.AddTag(B); Eq(log, "outerFalse", "innerFalse", "outerTrue", "innerTrue"); binds.UnbindAll(); Empty(binds, c);
        log.Clear(); binds.Bind(A, v => { log.Add("remove" + v); binds.UnbindAll(); }); c.RemoveTag(A); Eq(log, "removeTrue"); Empty(binds, c);
        log.Clear(); binds.Bind(A, v => { log.Add("swap" + v); binds.UnbindAll(); binds.Bind(B, x => log.Add("new" + x)); });
        c.AddTag(A); c.RemoveTag(B); Eq(log, "swapFalse", "newTrue", "newFalse"); binds.Dispose(); Empty(binds, c);
        Console.WriteLine("PASS immediate reentrant Bind, UnbindAll and replacement binding");

        c = new GameplayTagCountContainer(); binds = new GameplayTagContainerBinds(c); int calls = 0; bool shouldThrow = true;
        Throws<InvalidOperationException>(() => binds.Bind(A, v => { calls++; if (shouldThrow) throw new InvalidOperationException("test"); }));
        shouldThrow = false; c.AddTag(A); Check(calls == 2, "throwing immediate callback remains registered"); binds.UnbindAll(); Empty(binds, c);
        Throws<NullReferenceException>(() => binds.Bind(A, null)); Throws<NullReferenceException>(() => c.RemoveTag(A)); binds.UnbindAll(); Empty(binds, c);
        c.AddTag(A); c.RemoveTag(A);
        var noContainer = new GameplayTagContainerBinds(null); Throws<NullReferenceException>(() => noContainer.Bind(A, Nop));
        Check((int)Field(State(noContainer), "Count") == 1, "null container same partial state"); Throws<NullReferenceException>(() => noContainer.UnbindAll());
        GameplayTagContainerBinds defaults = default; Throws<NullReferenceException>(() => defaults.Bind(A, Nop)); defaults.UnbindAll(); defaults.Dispose();
        Console.WriteLine("PASS immediate throw, null callback, null container and default binds exceptions");

        c = new GameplayTagCountContainer(); binds = new GameplayTagContainerBinds(c); log.Clear();
        OnTagCountChangedDelegate external = (tag, count) => log.Add("external" + count); c.RegisterTagEventCallback(A, GameplayTagEventType.NewOrRemoved, external);
        binds.Bind(A, v => log.Add("bound" + v)); c.AddTag(A); binds.UnbindAll(); c.RemoveTag(A);
        Eq(log, "boundFalse", "external1", "boundTrue", "external0"); c.RemoveTagEventCallback(A, GameplayTagEventType.NewOrRemoved, external); Empty(binds, c);
        var released = ReleaseProbe(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Check(!released.Item3.IsAlive, "callback target released"); GC.KeepAlive(released.Item1); GC.KeepAlive(released.Item2);
        Console.WriteLine("PASS external callback retained, supported public event order and target release");
        Console.WriteLine("{\"Checks\":" + checks + ",\"FailedGroups\":0}");
    }
    sealed class Target { public void Accept(bool value) { if (value) sink++; } }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Tuple<GameplayTagCountContainer, GameplayTagContainerBinds, WeakReference> ReleaseProbe()
    {
        var c = new GameplayTagCountContainer(); var b = new GameplayTagContainerBinds(c); var target = new Target(); var weak = new WeakReference(target);
        b.Bind(A, target.Accept); b.UnbindAll(); Empty(b, c); return Tuple.Create(c, b, weak);
    }
}
