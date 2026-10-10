using System.Runtime.CompilerServices;

namespace GameplayTags
{
    /// <summary>
    /// In-place integer introsort. No comparer, delegate, boxing, pool or managed
    /// scratch allocation. The internal caller owns valid array/range arguments.
    /// The recursion is bounded by sorting the smaller partition recursively.
    /// </summary>
    internal static class Int32Sort
    {
        private const int InsertionThreshold = 16;

        internal static void Sort(int[] values, int start, int length)
        {
            if (length < 2)
                return;
            int log = 0;
            for (int n = length; n > 1; n >>= 1)
                log++;
            IntroSort(values, start, start + length - 1, log * 2);
        }

        private static void IntroSort(int[] values, int lo, int hi, int depthLimit)
        {
            while (hi - lo + 1 > InsertionThreshold)
            {
                if (depthLimit-- == 0)
                {
                    HeapSort(values, lo, hi - lo + 1);
                    return;
                }
                int middle = lo + ((hi - lo) >> 1);
                SwapIfGreater(values, lo, middle);
                SwapIfGreater(values, lo, hi);
                SwapIfGreater(values, middle, hi);
                int pivot = values[middle];
                Swap(values, middle, hi - 1);
                int left = lo;
                int right = hi - 1;
                // Median-of-three endpoints are sentinels for these scans.
                while (true)
                {
                    while (values[++left] < pivot) { }
                    while (values[--right] > pivot) { }
                    if (left >= right)
                        break;
                    Swap(values, left, right);
                }
                Swap(values, left, hi - 1);
                if (left - lo < hi - left)
                {
                    IntroSort(values, lo, left - 1, depthLimit);
                    lo = left + 1;
                }
                else
                {
                    IntroSort(values, left + 1, hi, depthLimit);
                    hi = left - 1;
                }
            }
            for (int i = lo + 1; i <= hi; i++)
            {
                int value = values[i];
                int j = i - 1;
                while (j >= lo && values[j] > value)
                {
                    values[j + 1] = values[j];
                    j--;
                }
                values[j + 1] = value;
            }
        }

        private static void HeapSort(int[] values, int start, int length)
        {
            for (int root = length / 2 - 1; root >= 0; root--)
                SiftDown(values, start, root, length);
            for (int end = length - 1; end > 0; end--)
            {
                Swap(values, start, start + end);
                SiftDown(values, start, 0, end);
            }
        }

        private static void SiftDown(int[] values, int start, int root, int length)
        {
            int value = values[start + root];
            while (root < length / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < length && values[start + child] < values[start + child + 1])
                    child++;
                if (value >= values[start + child])
                    break;
                values[start + root] = values[start + child];
                root = child;
            }
            values[start + root] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SwapIfGreater(int[] values, int a, int b)
        {
            if (values[a] > values[b])
                Swap(values, a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap(int[] values, int a, int b)
        {
            int temporary = values[a];
            values[a] = values[b];
            values[b] = temporary;
        }
    }
}
