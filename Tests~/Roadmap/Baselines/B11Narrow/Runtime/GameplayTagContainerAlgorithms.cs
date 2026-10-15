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
            bool fast = AllRegistered(source);
            result.Clear();
            if (source == null) return;
            result.EnsureCapacity(source.Count);
            if (fast)
            {
                result.Tags.AddRange(source.Tags);
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
            // count/range bounds are exact; a late missing member can cost an extra
            // scan before the merge below, but no workload-specific cutoff is used.
            if (right.Count <= oldCount &&
                string.CompareOrdinal(output[0].Name, right[0].Name) <= 0 &&
                string.CompareOrdinal(right[right.Count - 1].Name, output[oldCount - 1].Name) <= 0 &&
                TryApplyContainedSource(output, right, preferSourceOnEqual))
                return;

            // Grow the logical Count before indexed writes. Read the distinct source
            // directly; backward writes stay beyond unread destination members.
            // Existing destination members, including unknown names, are retained.
            destination.EnsureCapacity(oldCount + right.Count);
            output.AddRange(right);
            int a = oldCount - 1, b = right.Count - 1, write = output.Count - 1;
            while (a >= 0 && b >= 0)
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
            while (b >= 0) output[write--] = right[b--];
            while (a >= 0) output[write--] = output[a--];
            if (write >= 0) output.RemoveRange(0, write + 1);
        }

        private static bool TryApplyContainedSource(List<GameplayTag> destination, List<GameplayTag> source,
            bool preferSourceOnEqual)
        {
            int a = 0, b = 0;
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
            while (a < left.Count || b < right.Count)
            {
                GameplayTag tag;
                if (a == left.Count) tag = right[b++];
                else if (b == right.Count) tag = left[a++];
                else
                {
                    int order = string.CompareOrdinal(left[a].Name, right[b].Name);
                    if (order < 0) tag = left[a++];
                    else if (order > 0) tag = right[b++];
                    else { tag = left[a++]; b++; }
                }
                result.Tags.Add(tag);
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
            if (ReferenceEquals(lhs, rhs)) { CopyPreservingNames(lhs, result); return; }
            var left = lhs.Tags; var right = rhs.Tags; var output = result.Tags;
            if (left.Count == 0 || right.Count == 0 ||
                string.CompareOrdinal(left[left.Count - 1].Name, right[0].Name) < 0 ||
                string.CompareOrdinal(right[right.Count - 1].Name, left[0].Name) < 0)
            {
                output.Clear();
                return;
            }
            bool alias = ReferenceEquals(result, lhs) || ReferenceEquals(result, rhs);
            var small = left.Count <= right.Count ? left : right;
            var large = left.Count <= right.Count ? right : left;
            if ((small.Count == 1 || PreferBinarySearch(small.Count, large.Count)) &&
                !FitsShortPrefix(small, large))
            {
                IntersectionBySearch(left, right, output, alias);
                return;
            }
            // Grow with actual matches instead of reserving for a potentially empty
            // or sparse intersection. Aliased targets compact in their existing buffer.
            if (!alias) output.Clear();
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

        private static void IntersectionBySearch(List<GameplayTag> left, List<GameplayTag> right,
            List<GameplayTag> output, bool alias)
        {
            bool probeLeft = left.Count <= right.Count;
            var probes = probeLeft ? left : right;
            var searched = probeLeft ? right : left;
            int probeCount = probes.Count, searchedCount = searched.Count;
            if (!alias) output.Clear();
            int lower = 0, write = 0;
            for (int i = 0; i < probeCount && lower < searchedCount; i++)
            {
                GameplayTag probe = probes[i];
                int found = LowerBound(searched, probe.Name, lower, searchedCount);
                if (found == searchedCount) break;
                lower = found;
                if (string.CompareOrdinal(searched[found].Name, probe.Name) != 0) continue;
                // Always retain lhs representatives, even when rhs is the smaller
                // probe set. Capture before writing and exclude the matched prefix
                // from later searches, so aliases cannot corrupt unread search data.
                GameplayTag value = probeLeft ? probe : searched[found];
                lower = found + 1;
                if (alias) output[write++] = value; else output.Add(value);
            }
            if (alias) output.RemoveRange(write, output.Count - write);
        }

        internal static void ExceptSorted(GameplayTagContainer source, GameplayTagContainer remove, GameplayTagContainer result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (source == null || ReferenceEquals(source, remove)) { result.Clear(); return; }
            var left = source.Tags; var right = remove?.Tags;
            if (left.Count == 0) { result.Clear(); return; }
            if (right == null || right.Count == 0 ||
                string.CompareOrdinal(left[left.Count - 1].Name, right[0].Name) < 0 ||
                string.CompareOrdinal(right[right.Count - 1].Name, left[0].Name) < 0)
            {
                CopyPreservingNames(source, result);
                return;
            }

            // Singleton decisions are complete before changing either input, which
            // also avoids snapshots when the target aliases the removal container.
            if (right.Count == 1)
            {
                string name = right[0].Name;
                int found = LowerBound(left, name, 0, left.Count);
                bool matched = found < left.Count && string.CompareOrdinal(left[found].Name, name) == 0;
                CopyPreservingNames(source, result);
                if (matched) result.Tags.RemoveAt(found);
                return;
            }
            if (left.Count == 1)
            {
                // The disjoint check ruled out a source before the first removal.
                // Handle a head match directly instead of searching a large suffix.
                int found = string.CompareOrdinal(left[0].Name, right[0].Name) == 0 ?
                    0 : LowerBound(right, left[0].Name, 1, right.Count);
                if (found < right.Count && string.CompareOrdinal(right[found].Name, left[0].Name) == 0)
                    result.Clear();
                else
                    CopyPreservingNames(source, result);
                return;
            }

            if (ReferenceEquals(result, remove)) right = new List<GameplayTag>(right);
            bool alias = ReferenceEquals(source, result);
            if (PreferBinarySearch(left.Count, right.Count) && !FitsShortPrefix(left, right))
            {
                ExceptBySourceSearch(left, right, result, alias);
                return;
            }
            // Attribution measured four removals (plus the singleton case above).
            // Keep this conservative tiny-removal category: a comparison estimate
            // alone does not account for repeated RemoveAt suffix shifts.
            if (right.Count <= 4 && PreferBinarySearch(right.Count, left.Count))
            {
                CopyPreservingNames(source, result);
                int upper = result.Count;
                for (int i = right.Count - 1; i >= 0 && upper > 0; i--)
                {
                    int found = LowerBound(result.Tags, right[i].Name, 0, upper);
                    if (found < upper && string.CompareOrdinal(result.Tags[found].Name, right[i].Name) == 0)
                        result.Tags.RemoveAt(found);
                    // Remaining removal names are smaller, so the searched prefix
                    // excludes this insertion point whether or not a match existed.
                    upper = found;
                }
                return;
            }

            if (!alias) { result.Clear(); result.EnsureCapacity(left.Count); }
            int a = 0, b = 0, write = 0;
            while (a < left.Count)
            {
                GameplayTag value = left[a++];
                while (b < right.Count && string.CompareOrdinal(right[b].Name, value.Name) < 0) b++;
                if (b < right.Count && string.CompareOrdinal(right[b].Name, value.Name) == 0) continue;
                if (alias) result.Tags[write++] = value; else result.Tags.Add(value);
            }
            if (alias) result.Tags.RemoveRange(write, result.Count - write);
        }

        private static void ExceptBySourceSearch(List<GameplayTag> left, List<GameplayTag> right,
            GameplayTagContainer result, bool alias)
        {
            int count = left.Count, lower = 0, write = 0;
            if (!alias) { result.Clear(); result.EnsureCapacity(count); }
            for (int i = 0; i < count; i++)
            {
                GameplayTag value = left[i];
                int found = LowerBound(right, value.Name, lower, right.Count);
                lower = found;
                if (found < right.Count && string.CompareOrdinal(right[found].Name, value.Name) == 0)
                {
                    lower = found + 1;
                    continue;
                }
                if (alias) result.Tags[write++] = value; else result.Tags.Add(value);
            }
            if (alias) result.Tags.RemoveRange(write, result.Count - write);
        }

        private static void CopyPreservingNames(GameplayTagContainer source, GameplayTagContainer result)
        {
            if (ReferenceEquals(source, result)) return;
            result.Clear();
            result.EnsureCapacity(source.Count);
            result.Tags.AddRange(source.Tags);
        }

        private static bool PreferBinarySearch(int smallCount, int largeCount)
        {
            // Compare estimated name-comparison work, not an empirical size ratio.
            // This is a conservative dispatch policy, not a universal crossover claim;
            // public-call benchmarks still include writes, shifting and allocation.
            int comparisons = 0;
            for (int count = largeCount; count > 0; count >>= 1) comparisons++;
            return (long)smallCount * comparisons < largeCount;
        }

        private static bool FitsShortPrefix(List<GameplayTag> small, List<GameplayTag> large)
        {
            // If the largest probe lies in this prefix, merge finishes after at most
            // small.Count large-side entries regardless of the full large-side size.
            return string.CompareOrdinal(small[small.Count - 1].Name, large[small.Count - 1].Name) <= 0;
        }

        private static int LowerBound(List<GameplayTag> tags, string name, int low, int high)
        {
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                if (string.CompareOrdinal(tags[mid].Name, name) < 0) low = mid + 1;
                else high = mid;
            }
            return low;
        }
    }
}
