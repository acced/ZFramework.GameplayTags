using System;

namespace GameplayTags
{
    /// <summary>
    /// Batch algorithms for GameplayTagContainer.
    ///
    /// Phase 1 of the container refactor: keep the existing ordered-container
    /// semantics, but provide linear-time merge primitives instead of repeatedly
    /// calling AddTag and causing repeated binary searches and insert shifts.
    ///
    /// The caller is responsible for ensuring containers share the same registry
    /// semantics before using these low-level operations.
    /// </summary>
    internal static class GameplayTagContainerAlgorithms
    {
        internal static void UnionSorted(GameplayTagContainer lhs, GameplayTagContainer rhs, GameplayTagContainer result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            result.Clear();
            if (lhs == null && rhs == null)
                return;

            int left = 0;
            int right = 0;

            while (lhs != null && left < lhs.Count || rhs != null && right < (rhs?.Count ?? 0))
            {
                if (lhs == null || left >= lhs.Count)
                {
                    result.AddTag(rhs[right++]);
                    continue;
                }

                if (rhs == null || right >= rhs.Count)
                {
                    result.AddTag(lhs[left++]);
                    continue;
                }

                int compare = lhs[left].CompareTo(rhs[right]);
                if (compare < 0)
                {
                    result.AddTag(lhs[left++]);
                }
                else if (compare > 0)
                {
                    result.AddTag(rhs[right++]);
                }
                else
                {
                    result.AddTag(lhs[left++]);
                    right++;
                }
            }
        }

        internal static void IntersectionExactSorted(GameplayTagContainer lhs, GameplayTagContainer rhs, GameplayTagContainer result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            result.Clear();
            if (lhs == null || rhs == null)
                return;

            int left = 0;
            int right = 0;
            while (left < lhs.Count && right < rhs.Count)
            {
                int compare = lhs[left].CompareTo(rhs[right]);
                if (compare < 0)
                    left++;
                else if (compare > 0)
                    right++;
                else
                {
                    result.AddTag(lhs[left]);
                    left++;
                    right++;
                }
            }
        }

        internal static void ExceptSorted(GameplayTagContainer source, GameplayTagContainer remove, GameplayTagContainer result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            result.Clear();
            if (source == null)
                return;

            int sourceIndex = 0;
            int removeIndex = 0;
            while (sourceIndex < source.Count)
            {
                if (remove == null || removeIndex >= remove.Count)
                {
                    result.AddTag(source[sourceIndex++]);
                    continue;
                }

                int compare = source[sourceIndex].CompareTo(remove[removeIndex]);
                if (compare < 0)
                    result.AddTag(source[sourceIndex++]);
                else if (compare > 0)
                    removeIndex++;
                else
                {
                    sourceIndex++;
                    removeIndex++;
                }
            }
        }
    }
}
