using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>
    /// Immutable process/domain registry. Initialize once before creating containers,
    /// or let the first name lookup discover assembly declarations. Cache returned tags;
    /// runtime IDs are local to this registry and must never be persisted as asset data.
    /// </summary>
    public class GameplayTagManager
    {
        private static readonly object s_InitializationLock = new object();
        private static volatile bool s_IsInitialized;
        private static Dictionary<string, int> s_IndicesByName;
        private static GameplayTagDefinition[] s_Definitions = { GameplayTagDefinition.None };
        private static GameplayTag[] s_Tags = { default };
        private static int[] s_Parents = { 0 };
        private static int[] s_SubtreeEnds = { 1 };
        private static int[] s_HierarchyIndices = Array.Empty<int>();
        private static GameplayTag[] s_HierarchyTags = Array.Empty<GameplayTag>();

        public static bool IsInitialized => s_IsInitialized;

        /// <summary>The number of registered tags, including implicit parents and excluding None.</summary>
        public static int TagCount
        {
            get
            {
                InitializeIfNeeded();
                return s_Tags.Length - 1;
            }
        }

        /// <summary>
        /// Freeze an explicit declaration set without scanning assemblies. Call before any
        /// tag lookup. Names must be valid; call GameplayTagUtility.ValidateName explicitly
        /// for untrusted input. Replacing a live registry would invalidate every existing ID
        /// and is deliberately rejected. Duplicate flags are ORed; descriptions use ordinal
        /// minimum. Automatically created parents OR their descendants' declared flags.
        /// </summary>
        public static void Initialize(params GameplayTagRegistration[] registrations)
        {
            lock (s_InitializationLock)
            {
                if (s_IsInitialized)
                    throw new InvalidOperationException("The gameplay tag registry is already initialized.");

                var context = new GameplayTagRegistrationContext();
                for (int i = 0; i < registrations.Length; ++i)
                {
                    GameplayTagRegistration registration = registrations[i];
                    context.RegisterTag(registration.Name, registration.Description, registration.Flags);
                }
                Publish(context);
            }
        }

        /// <summary>All tags in ordinal sibling order and depth-first preorder; excludes None.</summary>
        public static ReadOnlySpan<GameplayTag> GetAllTags()
        {
            InitializeIfNeeded();
            return s_Tags.AsSpan(1);
        }

        /// <summary>Resolve a case-sensitive ordinal name. Unknown, null and empty names return None.</summary>
        public static GameplayTag RequestTag(string name)
        {
            if (string.IsNullOrEmpty(name))
                return GameplayTag.None;
            InitializeIfNeeded();
            return s_IndicesByName.TryGetValue(name, out int index) ? s_Tags[index] : GameplayTag.None;
        }

        public static bool RequestTag(string name, out GameplayTag tag)
        {
            if (!string.IsNullOrEmpty(name))
            {
                InitializeIfNeeded();
                if (s_IndicesByName.TryGetValue(name, out int index))
                {
                    tag = s_Tags[index];
                    return true;
                }
            }
            tag = GameplayTag.None;
            return false;
        }

        /// <summary>Discover assembly-level declarations once. Prefer explicit Initialize for AOT/startup control.</summary>
        public static void InitializeIfNeeded()
        {
            if (s_IsInitialized)
                return;
            lock (s_InitializationLock)
            {
                if (s_IsInitialized)
                    return;

                var context = new GameplayTagRegistrationContext();
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Array.Sort(assemblies, (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
                for (int i = 0; i < assemblies.Length; ++i)
                    foreach (GameplayTagAttribute declaration in assemblies[i].GetCustomAttributes<GameplayTagAttribute>())
                        context.RegisterTag(declaration.TagName, declaration.Description, declaration.Flags);
                Publish(context);
            }
        }

        private static void Publish(GameplayTagRegistrationContext context)
        {
            context.Build(out s_Definitions, out s_Tags, out s_Parents, out s_SubtreeEnds,
                out s_HierarchyIndices, out s_HierarchyTags, out s_IndicesByName);
            // Volatile publication follows all array writes. The arrays are never mutated again.
            s_IsInitialized = true;
        }

        // Internal runtime accessors intentionally contain no initialization/validity guards.
        // A live tag comes from the frozen registry, so its ID is already a valid array index.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ref readonly GameplayTagDefinition GetDefinitionFromRuntimeIndex(int index) => ref s_Definitions[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static GameplayTag GetTagFromRuntimeIndex(int index) => s_Tags[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetParentIndex(int index) => s_Parents[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetSubtreeEnd(int index) => s_SubtreeEnds[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<int> GetHierarchyIndices(int index)
        {
            ref readonly GameplayTagDefinition definition = ref s_Definitions[index];
            return s_HierarchyIndices.AsSpan(definition.HierarchyOffset, definition.HierarchyLevel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<GameplayTag> GetTagRange(int start, int length) => s_Tags.AsSpan(start, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<GameplayTag> GetHierarchyTags(int start, int length) => s_HierarchyTags.AsSpan(start, length);
    }
}
