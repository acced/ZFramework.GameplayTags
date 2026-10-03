using System;

namespace GameplayTags.Experiments
{
    public sealed partial class DirectArraySet
    {
        /// <summary>True when this set explicitly owns the tag or one of its descendants.</summary>
        public bool HasTag(RuntimeTag tag) => Accept(tag) && AnyInRange(tag.Id, Registry.Ends[tag.Id]);

        /// <summary>Evaluate an immutable query against this set without copying membership.</summary>
        public bool Matches(FrozenGameplayTagQuery query) => query != null && query.MatchesDirect(this);

        // Callers admit registry ownership and supply a valid half-open DFS range.
        // Reads the active representation directly; no temporary set or delegate.
        internal bool AnyInRange(int start, int end)
        {
            if (count == 0 || start == end) return false;
            if (dense != null)
            {
                int first = start >> 6, last = (end - 1) >> 6;
                ulong lowMask = ulong.MaxValue << (start & 63);
                ulong highMask = (end & 63) == 0 ? ulong.MaxValue : (1UL << (end & 63)) - 1;
                if (first == last) return (dense[first] & lowMask & highMask) != 0;
                if ((dense[first] & lowMask) != 0) return true;
                for (int i = first + 1; i < last; i++) if (dense[i] != 0) return true;
                return (dense[last] & highMask) != 0;
            }

            int at = Find(Key(start));
            if (at < 0) at = ~at;
            while (at < used)
            {
                uint record = Read(at++);
                int firstId = (int)(record >> MaskBits) << Shift;
                if (firstId >= end) return false;
                int low = Math.Max(0, start - firstId);
                int high = Math.Min(1 << Shift, end - firstId);
                uint rangeMask = (Mask << low) & (Mask >> (MaskBits - high));
                if ((record & rangeMask) != 0) return true;
                // Only an edge record can miss: every interior record is nonempty.
            }
            return false;
        }
    }
}
