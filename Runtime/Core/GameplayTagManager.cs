using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>
    /// Immutable process/domain registry. Generated declarations initialize on first use.
    /// Cache returned tags;
    /// runtime IDs are local to this registry and must never be persisted as asset data.
    /// </summary>
    public class GameplayTagManager
    {
        private static volatile bool s_IsInitialized;
        private static readonly object s_InitializationLock = new object();
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
                RequireInitialized();
                return s_Tags.Length - 1;
            }
        }

        /// <summary>
        /// Publish declarations once per domain. Subsequent calls are no-ops, preserving
        /// existing handles. Custom declarations must be supplied before first tag use.
        /// Duplicate declarations use the ordinal minimum description.
        /// </summary>
        public static void Initialize(params GameplayTagRegistration[] registrations)
        {
            lock (s_InitializationLock)
            {
                if (s_IsInitialized) return;
                if (registrations == null) throw new ArgumentNullException(nameof(registrations));
                var context = new GameplayTagRegistrationContext();
                for (int i = 0; i < registrations.Length; ++i)
                {
                    GameplayTagRegistration registration = registrations[i];
                    context.RegisterTag(registration.Name, registration.Description);
                }
                Publish(context);
            }
        }

        /// <summary>All tags in ordinal sibling order and depth-first preorder; excludes None.</summary>
        public static ReadOnlySpan<GameplayTag> GetAllTags()
        {
            RequireInitialized();
            return s_Tags.AsSpan(1);
        }

        /// <summary>Resolve a case-sensitive ordinal name. Unknown, null and empty names return None.</summary>
        public static GameplayTag RequestTag(string name)
        {
            int index = ResolveIndex(name);
            return s_Tags[index];
        }

        public static bool RequestTag(string name, out GameplayTag tag)
        {
            int index = ResolveIndex(name);
            tag = s_Tags[index];
            return index != 0;
        }

        private static void RequireInitialized()
        {
            if (s_IsInitialized) return;
            lock (s_InitializationLock)
            {
                if (s_IsInitialized) return;
#if !GAMEPLAY_TAGS_MANUAL_INITIALIZATION
                // The generated assembly references Runtime, so discover it only at the cold
                // name boundary. This also works during editor/worker-thread deserialization
                // before Unity startup callbacks, without calling any Unity APIs.
                var registry = Type.GetType("GameplayTags.Generated.GameplayTagRegistry, ZFramework.GameplayTags.Generated");
                var initialize = registry?.GetMethod("Initialize", Type.EmptyTypes);
                if (initialize != null)
                    ((Action)Delegate.CreateDelegate(typeof(Action), initialize))();
#endif
                if (!s_IsInitialized)
                    throw new InvalidOperationException("Generate the gameplay tag registry, or call " +
                        "GameplayTagRegistry.Initialize() / GameplayTagManager.Initialize(registrations) before querying tags.");
            }
        }

        // Cold name boundary only. Cached handles never call this from RuntimeIndex.
        internal static int ResolveIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            RequireInitialized();
            return s_IndicesByName.TryGetValue(name, out int index) ? index : 0;
        }

        private static void Publish(GameplayTagRegistrationContext context)
        {
            context.Build(out s_Definitions, out s_Tags, out s_Parents, out s_SubtreeEnds,
                out s_HierarchyIndices, out s_HierarchyTags, out s_IndicesByName);
            // Volatile publication makes the completed arrays visible to all readers.
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
