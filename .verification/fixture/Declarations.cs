using GameplayTags;
#if PLUGIN
[assembly: GameplayTagAttribute("Plugin.Only")]
#else
[assembly: GameplayTagAttribute("Shared.Base")]
[assembly: GameplayTagAttribute("Ignored.Constructor", "ignored", (GameplayTagFlags)1,
    TagName="Named.Override", Description="quotes\" slash\\ line\n Unicode中文", Flags=(GameplayTagFlags)8)]
#if UNITY_EDITOR
[assembly: GameplayTagAttribute("Editor.Only")]
#endif
#if TARGET_ANDROID
[assembly: GameplayTagAttribute("Platform.Android")]
#else
[assembly: GameplayTagAttribute("Platform.Desktop")]
#endif
#if DLC
[assembly: GameplayTagAttribute("Content.Dlc")]
#endif
#if DERIVED
[assembly: FixtureTag("Derived.Tag")]
public class FixtureTag : GameplayTagAttribute { public FixtureTag(string name):base(name){} }
#endif

#endif
public class DoNotLoadMe { static DoNotLoadMe() { throw new System.Exception("Player code must never execute in Editor metadata discovery"); } }
