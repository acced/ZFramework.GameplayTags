using System;

namespace GameplayTags
{
    public sealed partial class RuntimeTagSet
    {
        // Public union boundaries route aliases through reverse append. This forward
        // kernel has an independent output and reserves only when its capacity needs it.
        private static void UnionPackedOperation(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            if (UnionContiguousPacked(left, right, result, false)) return;
            int a = left.m_PackedUsed, b = right.m_PackedUsed;
            int upper = Math.Min(result.PackedMaximum, a + b);
            if (result.m_Ids.Length < upper) result.ReservePacked(CountPackedUnionDirect(left, right));
            int[] x = left.m_Ids, y = right.m_Ids, output = result.m_Ids;
            int i = 0, j = 0, write = 0, common = 0;
            while (i < a && j < b)
            {
                int first = x[i], second = y[j], ak = RecordKey(first), bk = RecordKey(second);
                if (ak < bk) { output[write++] = first; i++; }
                else if (ak > bk) { output[write++] = second; j++; }
                else
                {
                    output[write++] = first | second;
                    common += RecordPop(first & second);
                    i++; j++;
                }
            }
            if (i < a) { Array.Copy(x, i, output, write, a - i); write += a - i; }
            if (j < b) { Array.Copy(y, j, output, write, b - j); write += b - j; }
            result.m_PackedUsed = write;
            result.m_Count = left.m_Count + right.m_Count - common;
        }

        // The dispatch proves capacity >= min(input record counts). Compaction
        // cannot overtake either consumed prefix, including either output alias.
        private static void IntersectPackedOperation(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            if (FilterContiguousPacked(left, right, result, false)) return;
            int a = left.m_PackedUsed, b = right.m_PackedUsed;
            int[] x = left.m_Ids, y = right.m_Ids, output = result.m_Ids;
            int i = 0, j = 0, write = 0, members = 0;
            while (i < a && j < b)
            {
                int first = x[i], second = y[j], ak = RecordKey(first), bk = RecordKey(second);
                if (ak < bk) i++;
                else if (ak > bk) j++;
                else
                {
                    int bits = first & second & PackedMask;
                    if (bits != 0) { output[write++] = ak | bits; members += RecordPop(bits); }
                    i++; j++;
                }
            }
            result.m_PackedUsed = write;
            result.m_Count = members;
        }

        // Capacity is at least the left record count. Right-output aliases retain
        // their existing protected path; a left alias only writes consumed records.
        private static void DifferencePackedOperation(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result)
        {
            if (FilterContiguousPacked(left, right, result, true)) return;
            int a = left.m_PackedUsed, b = right.m_PackedUsed, originalMembers = left.m_Count;
            int[] x = left.m_Ids, y = right.m_Ids, output = result.m_Ids;
            int i = 0, j = 0, write = 0, removed = 0;
            while (i < a && j < b)
            {
                int first = x[i], second = y[j], ak = RecordKey(first), bk = RecordKey(second);
                if (ak < bk) { output[write++] = first; i++; }
                else if (ak > bk) j++;
                else
                {
                    removed += RecordPop(first & second);
                    int bits = first & ~second & PackedMask;
                    if (bits != 0) output[write++] = ak | bits;
                    i++; j++;
                }
            }
            if (i < a) { Array.Copy(x, i, output, write, a - i); write += a - i; }
            result.m_PackedUsed = write;
            result.m_Count = originalMembers - removed;
        }
    }
}
