using System;
using System.Collections.Generic;

namespace GameplayTags
{
    /// <summary>Ordinal merge kernels. Validation belongs only to insertion/copy routes.</summary>
    internal static class GameplayTagContainerAlgorithms
    {
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
            if (source == null || source.Count == 0)
            {
                result.Clear();
                return;
            }
            bool fast = AllRegistered(source);
            result.Clear();
            result.EnsureCapacity(source.Count);
            if (fast)
            {
                if (source.Count == 1) result.Tags.Add(source[0]);
                else result.Tags.AddRange(source.Tags);
                return;
            }
            // Keep live traversal and sorted partial state through warning callbacks.
            for (int i = 0; i < source.Count; i++) result.AddTag(source[i]);
        }

        internal static void AppendSorted(GameplayTagContainer destination, GameplayTagContainer source)
        {
            if (!AllRegistered(source))
            {
                // Keep original callback/warning and sorted partial-state behavior.
                for (int i = 0; i < source.Count; i++) destination.AddTag(source[i]);
                return;
            }
            AppendAlreadyValidated(destination, source);
        }

        // The API boundary excludes source/destination aliases. Validation is already
        // complete, so this path cannot invoke registration or warning callbacks.
        private static void AppendAlreadyValidated(GameplayTagContainer destination, GameplayTagContainer source,
            bool preferSourceOnEqual = false)
        {
            if (source == null || source.Count == 0) return;
            var output = destination.Tags;
            var right = source.Tags;
            int oldCount = destination.Count;
            if (oldCount == 0 || string.CompareOrdinal(output[oldCount - 1].Name, right[0].Name) < 0)
            {
                destination.EnsureCapacity(oldCount + right.Count);
                output.AddRange(right);
                return;
            }
            // A contained input needs neither a larger buffer nor a merge. These
            // count/range bounds are exact; if containment fails, keep the scanned
            // prefix and merge only the remaining suffixes.
            int destinationStart = 0, sourceStart = 0;
            if (right.Count <= oldCount &&
                string.CompareOrdinal(output[0].Name, right[0].Name) <= 0 &&
                string.CompareOrdinal(right[right.Count - 1].Name, output[oldCount - 1].Name) <= 0 &&
                TryApplyContainedSource(output, right, preferSourceOnEqual, out destinationStart, out sourceStart))
                return;

            // Grow the logical Count before indexed writes. Read the distinct source
            // directly; backward writes stay beyond unread destination members.
            // Existing destination members, including unknown names, are retained.
            destination.EnsureCapacity(oldCount + (right.Count - sourceStart));
            if (sourceStart == 0) output.AddRange(right);
            else for (int i = sourceStart; i < right.Count; i++) output.Add(right[i]);
            int a = oldCount - 1, b = right.Count - 1, write = output.Count - 1;
            while (a >= destinationStart && b >= sourceStart)
            {
                int order = string.CompareOrdinal(output[a].Name, right[b].Name);
                if (order > 0) output[write--] = output[a--];
                else if (order < 0) output[write--] = right[b--];
                else
                {
                    // Ordinary append keeps destination identity; a union whose
                    // target is rhs must instead keep the incoming lhs identity.
                    output[write--] = preferSourceOnEqual ? right[b] : output[a];
                    a--; b--;
                }
            }
            while (b >= sourceStart) output[write--] = right[b--];
            while (a >= destinationStart) output[write--] = output[a--];
            // The gap consists only of duplicates in the merged suffix. Never
            // remove or rewrite the already resolved destination prefix.
            if (write >= destinationStart) output.RemoveRange(destinationStart, write - destinationStart + 1);
        }

        private static bool TryApplyContainedSource(List<GameplayTag> destination, List<GameplayTag> source,
            bool preferSourceOnEqual, out int a, out int b)
        {
            a = 0; b = 0;
            while (a < destination.Count && b < source.Count)
            {
                int order = string.CompareOrdinal(destination[a].Name, source[b].Name);
                if (order > 0) return false;
                if (order == 0)
                {
                    // These replacements also agree with the final merge if a later
                    // source member is missing. Names and source contents never change.
                    // If later capacity growth fails, completed representative-only
                    // replacements remain; this path is not transactional on exhaustion.
                    if (preferSourceOnEqual) destination[a] = source[b];
                    b++;
                }
                a++;
            }
            return b == source.Count;
        }

        internal static void UnionSorted(GameplayTagContainer lhs, GameplayTagContainer rhs, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            bool fast = AllRegistered(lhs) && (ReferenceEquals(lhs, rhs) || AllRegistered(rhs));
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
            // Even an all-self union reaches this point only after validation, since
            // union revalidates unknown names unlike the public self-append operation.
            if (ReferenceEquals(lhs, rhs))
            {
                if (!ReferenceEquals(lhs, result))
                {
                    result.Clear();
                    if (lhs != null)
                    {
                        result.EnsureCapacity(lhs.Count);
                        result.Tags.AddRange(lhs.Tags);
                    }
                }
                return;
            }
            if (ReferenceEquals(lhs, result))
            {
                AppendAlreadyValidated(result, rhs);
                return;
            }
            if (ReferenceEquals(rhs, result))
            {
                AppendAlreadyValidated(result, lhs, true);
                return;
            }
            List<GameplayTag> left = lhs?.Tags, right = rhs?.Tags;
            result.Clear();
            result.EnsureCapacity((left?.Count ?? 0) + (right?.Count ?? 0));
            if (left == null) { if (right != null) result.Tags.AddRange(right); return; }
            if (right == null) { result.Tags.AddRange(left); return; }
            int a = 0, b = 0;
            while (a < left.Count && b < right.Count)
            {
                GameplayTag tag;
                int order = string.CompareOrdinal(left[a].Name, right[b].Name);
                if (order < 0) tag = left[a++];
                else if (order > 0) tag = right[b++];
                else { tag = left[a++]; b++; }
                result.Tags.Add(tag);
            }
            while (a < left.Count) result.Tags.Add(left[a++]);
            while (b < right.Count) result.Tags.Add(right[b++]);
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
