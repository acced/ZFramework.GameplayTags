using System;
using System.Collections.Generic;

namespace GameplayTags
{
    /// <summary>Ordinal merge kernels. Validation belongs only to insertion/copy routes.</summary>
    internal static class GameplayTagContainerAlgorithms
    {
        private static bool Valid(GameplayTag tag)
        {
            if (GameplayTagManager.IsRegistered(tag)) return true;
            GameplayTagManager.WarnUnregistered(tag.Name);
            return false;
        }

        private static bool AllRegistered(GameplayTagContainer source)
        {
            if (source == null || source.Count == 0) return true;
            if (!GameplayTagManager.IsInitialized) return false;
            for (int i = 0; i < source.Count; i++)
                if (!GameplayTagManager.IsRegistered(source[i])) return false;
            return true;
        }

        internal static void CopyValidated(GameplayTagContainer source, GameplayTagContainer result)
        {
            bool fast = AllRegistered(source);
            result.Clear();
            if (source == null) return;
            result.EnsureCapacity(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (fast) result.Tags.Add(source[i]);
                else result.AddTag(source[i]);
            }
        }

        internal static void AppendSorted(GameplayTagContainer destination, GameplayTagContainer source)
        {
            if (!AllRegistered(source))
            {
                // Keep original callback/warning and sorted partial-state behavior.
                for (int i = 0; i < source.Count; i++) destination.AddTag(source[i]);
                return;
            }
            // Reuse the target's spare capacity. Existing unknown members are retained.
            int oldCount = destination.Count;
            destination.EnsureCapacity(oldCount + source.Count);
            for (int i = 0; i < source.Count; i++)
                if (Valid(source[i])) destination.Tags.Add(source[i]);
            int incoming = destination.Count - oldCount;
            if (incoming == 0) return;
            // The appended input is sorted. Backward merging cannot overwrite unread input
            // after moving the incoming run into a temporary buffer (alias-safe API boundary).
            var right = new GameplayTag[incoming];
            destination.Tags.CopyTo(oldCount, right, 0, incoming);
            int a = oldCount - 1, b = incoming - 1, write = destination.Count - 1;
            while (a >= 0 && b >= 0)
            {
                int order = string.CompareOrdinal(destination.Tags[a].Name, right[b].Name);
                if (order > 0) destination.Tags[write--] = destination.Tags[a--];
                else if (order < 0) destination.Tags[write--] = right[b--];
                else { destination.Tags[write--] = destination.Tags[a--]; b--; }
            }
            while (b >= 0) destination.Tags[write--] = right[b--];
            while (a >= 0) destination.Tags[write--] = destination.Tags[a--];
            if (write >= 0) destination.Tags.RemoveRange(0, write + 1);
        }

        internal static void UnionSorted(GameplayTagContainer lhs, GameplayTagContainer rhs, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            bool fast = AllRegistered(lhs) && AllRegistered(rhs);
            if (!fast)
            {
                // Legacy factory calls retain live source traversal through warning callbacks.
                // New target-writing aliases snapshot only the aliased input before clearing it.
                GameplayTagContainer l = ReferenceEquals(lhs, result) ? Snapshot(lhs) : lhs;
                GameplayTagContainer r = ReferenceEquals(rhs, result) ?
                    (ReferenceEquals(lhs, rhs) ? l : Snapshot(rhs)) : rhs;
                result.Clear();
                if (l != null) for (int i = 0; i < l.Count; i++) result.AddTag(l[i]);
                if (r != null) for (int i = 0; i < r.Count; i++) result.AddTag(r[i]);
                return;
            }
            List<GameplayTag> left = lhs?.Tags, right = rhs?.Tags;
            if (ReferenceEquals(lhs, result)) left = new List<GameplayTag>(left);
            if (ReferenceEquals(rhs, result)) right = ReferenceEquals(lhs, rhs) ? left : new List<GameplayTag>(right);
            result.Clear();
            result.EnsureCapacity((left?.Count ?? 0) + (right?.Count ?? 0));
            int a = 0, b = 0;
            while (a < (left?.Count ?? 0) || b < (right?.Count ?? 0))
            {
                GameplayTag tag;
                if (a == (left?.Count ?? 0)) tag = right[b++];
                else if (b == (right?.Count ?? 0)) tag = left[a++];
                else
                {
                    int order = string.CompareOrdinal(left[a].Name, right[b].Name);
                    if (order < 0) tag = left[a++];
                    else if (order > 0) tag = right[b++];
                    else { tag = left[a++]; b++; }
                }
                if (GameplayTagManager.IsRegistered(tag)) result.Tags.Add(tag);
            }
        }

        private static GameplayTagContainer Snapshot(GameplayTagContainer source)
        {
            var result = new GameplayTagContainer(source.Count);
            for (int i = 0; i < source.Count; i++) result.Tags.Add(source[i]);
            return result;
        }

        internal static void IntersectionExactSorted(GameplayTagContainer lhs, GameplayTagContainer rhs, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (lhs == null || rhs == null) { result.Clear(); return; }
            var left = lhs.Tags; var right = rhs.Tags; var output = result.Tags;
            bool alias = ReferenceEquals(result, lhs) || ReferenceEquals(result, rhs);
            if (!alias) { output.Clear(); result.EnsureCapacity(Math.Min(left.Count, right.Count)); }
            int a = 0, b = 0, write = 0;
            while (a < left.Count && b < right.Count)
            {
                int order = string.CompareOrdinal(left[a].Name, right[b].Name);
                if (order < 0) a++;
                else if (order > 0) b++;
                else
                {
                    GameplayTag value = left[a++]; b++;
                    if (alias) output[write++] = value; else output.Add(value);
                }
            }
            if (alias) output.RemoveRange(write, output.Count - write);
        }

        internal static void ExceptSorted(GameplayTagContainer source, GameplayTagContainer remove, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (source == null || ReferenceEquals(source, remove)) { result.Clear(); return; }
            var left = source.Tags; var right = remove?.Tags;
            if (ReferenceEquals(result, remove)) right = new List<GameplayTag>(right);
            bool alias = ReferenceEquals(source, result);
            if (!alias) { result.Clear(); result.EnsureCapacity(left.Count); }
            int a = 0, b = 0, write = 0;
            while (a < left.Count)
            {
                GameplayTag value = left[a++];
                while (b < (right?.Count ?? 0) && string.CompareOrdinal(right[b].Name, value.Name) < 0) b++;
                if (b < (right?.Count ?? 0) && string.CompareOrdinal(right[b].Name, value.Name) == 0) continue;
                if (alias) result.Tags[write++] = value; else result.Tags.Add(value);
            }
            if (alias) result.Tags.RemoveRange(write, result.Count - write);
        }
    }
}
