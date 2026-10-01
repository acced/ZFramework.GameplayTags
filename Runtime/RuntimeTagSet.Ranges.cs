using System;

namespace GameplayTags
{
    public sealed partial class RuntimeTagSet
    {
        private static bool ContiguousPackedRanges(RuntimeTagSet left, RuntimeTagSet right, out int aStart, out int bStart)
        {
            aStart = bStart = 0;
            int an = left.m_PackedUsed, bn = right.m_PackedUsed;
            // Amortize the shape checks; tiny ranges keep the ordinary scalar path.
            if (an < 8 || bn < 8) return false;
            aStart = RecordBase(left.m_Ids[0]) >> 4;
            bStart = RecordBase(right.m_Ids[0]) >> 4;
            return (RecordBase(left.m_Ids[an - 1]) >> 4) - aStart + 1 == an &&
                   (RecordBase(right.m_Ids[bn - 1]) >> 4) - bStart + 1 == bn;
        }
        private static bool UnionContiguousPacked(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result, bool backwards)
        {
            if (!ContiguousPackedRanges(left, right, out int aStart, out int bStart)) return false;
            int an = left.m_PackedUsed, bn = right.m_PackedUsed;
            int originalMembers = left.m_Count + right.m_Count;
            int aEnd = aStart + an, bEnd = bStart + bn;
            int overlapStart = Math.Max(aStart, bStart), overlapEnd = Math.Min(aEnd, bEnd);
            int overlap = Math.Max(0, overlapEnd - overlapStart), records = an + bn - overlap;
            result.ReservePacked(records);
            // Reload after growth: the destination may be left's one canonical buffer.
            int[] a = left.m_Ids, b = right.m_Ids, output = result.m_Ids;
            if (overlap == 0)
            {
                int[] first = aStart < bStart ? a : b, second = aStart < bStart ? b : a;
                int firstCount = aStart < bStart ? an : bn, secondCount = records - firstCount;
                // Copy the later interval first, preserving any unread aliased prefix.
                Array.Copy(second, 0, output, firstCount, secondCount);
                Array.Copy(first, 0, output, 0, firstCount);
                result.m_PackedUsed = records; result.m_Count = originalMembers; return true;
            }
            int prefix = overlapStart - Math.Min(aStart, bStart);
            int suffix = Math.Max(aEnd, bEnd) - overlapEnd;
            int[] prefixSource = aStart <= bStart ? a : b;
            int[] suffixSource = aEnd >= bEnd ? a : b;
            int suffixOffset = overlapEnd - (aEnd >= bEnd ? aStart : bStart);
            if (backwards && suffix != 0) Array.Copy(suffixSource, suffixOffset, output, prefix + overlap, suffix);
            if (!backwards && prefix != 0) Array.Copy(prefixSource, 0, output, 0, prefix);
            int common = RuntimeBitOperations.UnionPackedRange(a, overlapStart - aStart, b, overlapStart - bStart,
                output, prefix, overlap, backwards);
            if (backwards && prefix != 0) Array.Copy(prefixSource, 0, output, 0, prefix);
            if (!backwards && suffix != 0) Array.Copy(suffixSource, suffixOffset, output, prefix + overlap, suffix);
            result.m_PackedUsed = records; result.m_Count = originalMembers - common;
            return true;
        }
        private static bool FilterContiguousPacked(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result, bool difference)
        {
            if (!ContiguousPackedRanges(left, right, out int aStart, out int bStart)) return false;
            int an = left.m_PackedUsed, bn = right.m_PackedUsed, originalMembers = left.m_Count;
            int aEnd = aStart + an, bEnd = bStart + bn;
            int start = Math.Max(aStart, bStart), end = Math.Min(aEnd, bEnd), overlap = end - start;
            if (overlap <= 0)
            {
                if (difference) result.CopyFrom(left); else result.Clear();
                return true;
            }
            // Caller-owned actual-result-sized output must not grow just for an upper bound.
            // Such cases use the general compactor; usual reserved/alias paths take this kernel.
            int upper = difference ? an : overlap;
            if (result.m_Ids.Length < upper) return false;
            int[] a = left.m_Ids, b = right.m_Ids, output = result.m_Ids;
            int aOffset = start - aStart, bOffset = start - bStart;
            int prefix = difference ? aOffset : 0, suffix = difference ? aEnd - end : 0;
            if (prefix != 0) Array.Copy(a, 0, output, 0, prefix);
            int common = RuntimeBitOperations.FilterPackedRange(a, aOffset, b, bOffset,
                output, prefix, overlap, difference, out int written);
            if (suffix != 0) Array.Copy(a, aOffset + overlap, output, prefix + written, suffix);
            result.m_PackedUsed = prefix + written + suffix;
            result.m_Count = difference ? originalMembers - common : common;
            return true;
        }
    }
}
