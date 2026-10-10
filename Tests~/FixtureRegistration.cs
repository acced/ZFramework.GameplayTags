using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GameplayTags;

internal static class FixtureRegistration
{
    // Both the original and rewritten runtime discover this same assembly through
    // their public attribute-registration path. All setup is outside timing loops.
    internal static void Register(IEnumerable<string> names)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("GameplayTags.ValidationFixtures"), AssemblyBuilderAccess.Run);
        ConstructorInfo constructor = typeof(GameplayTagAttribute).GetConstructor(new[]
        {
            typeof(string), typeof(string), typeof(GameplayTagFlags)
        });
        foreach (string name in names)
        {
            assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor,
                new object[] { name, "Validation fixture", GameplayTagFlags.None }));
        }
        GameplayTagManager.InitializeIfNeeded();
    }
}
