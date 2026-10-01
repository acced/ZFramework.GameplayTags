using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace GameplayTags
{
    internal static partial class RuntimeBitOperations
    {
        // The caller proved four aligned, consecutive occupied16-ID blocks. Narrowing
        // discards each key and packs their low16 masks into one logical64-bit word.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong PackFourRecordMasks(int[] records, int index)
        {
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (AdvSimd.Arm64.IsSupported)
                return AdvSimd.ExtractNarrowingLower(Vector128.LoadUnsafe(ref records[index]).AsUInt32()).AsUInt64().ToScalar();
            if (Sse41.IsSupported)
            {
                var low = Sse2.And(Vector128.LoadUnsafe(ref records[index]), Vector128.Create(0xFFFF));
                return Sse41.PackUnsignedSaturate(low, Vector128<int>.Zero).AsUInt64().ToScalar();
            }
#endif
            return (ulong)(uint)(records[index] & 0xFFFF) |
                ((ulong)(uint)(records[index + 1] & 0xFFFF) << 16) |
                ((ulong)(uint)(records[index + 2] & 0xFFFF) << 32) |
                ((ulong)(uint)(records[index + 3] & 0xFFFF) << 48);
        }
        /// <summary>
        /// Unions aligned packed-record ranges and returns the shared member count.
        /// Corresponding records must have identical high-16 keys. All ranges must be valid.
        /// For each aliased input, destination starts at or before that input when forwards,
        /// or at or after it when backwards. Both inputs are loaded before each chunk store.
        /// </summary>
        internal static int UnionPackedRange(int[] a, int aOffset, int[] b, int bOffset,
            int[] destination, int destinationOffset, int length, bool backwards)
        {
            int consumed = 0, common = 0;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (Avx2.IsSupported && length >= 16)
            {
                var members = Vector256.Create(0xFFFF);
                var table = Lookup; var nibbleMask = Vector256.Create((byte)15);
                var sums = Vector256<ulong>.Zero;
                ref int pa = ref a[aOffset], pb = ref b[bOffset], pd = ref destination[destinationOffset];
                int index = backwards ? length - 8 : 0, step = backwards ? -8 : 8;
                int end = length & ~7;
                for (; consumed < end; consumed += 8, index += step)
                {
                    var x = Vector256.LoadUnsafe(ref pa, (nuint)index);
                    var y = Vector256.LoadUnsafe(ref pb, (nuint)index);
                    var shared = Avx2.And(Avx2.And(x, y), members);
                    sums = Avx2.Add(sums, CountBytes(shared.AsUInt64(), table, nibbleMask));
                    Avx2.Or(x, y).StoreUnsafe(ref pd, (nuint)index);
                }
                common = Sum(sums);
            }
            else if (AdvSimd.Arm64.IsSupported && length >= 8)
            {
                var members = Vector128.Create(0xFFFF);
                var sums = Vector128<uint>.Zero;
                ref int pa = ref a[aOffset], pb = ref b[bOffset], pd = ref destination[destinationOffset];
                int index = backwards ? length - 4 : 0, step = backwards ? -4 : 4;
                int end = length & ~3;
                for (; consumed < end; consumed += 4, index += step)
                {
                    var x = Vector128.LoadUnsafe(ref pa, (nuint)index);
                    var y = Vector128.LoadUnsafe(ref pb, (nuint)index);
                    var shared = AdvSimd.And(AdvSimd.And(x, y), members);
                    var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(shared.AsByte()));
                    sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                    AdvSimd.Or(x, y).StoreUnsafe(ref pd, (nuint)index);
                }
                common = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            int scalarIndex = backwards ? length - consumed - 1 : consumed;
            int scalarStep = backwards ? -1 : 1;
            for (; consumed < length; consumed++, scalarIndex += scalarStep)
            {
                int x = a[aOffset + scalarIndex], y = b[bOffset + scalarIndex];
                common += PopCount((uint)(x & y & 0xFFFF));
                destination[destinationOffset + scalarIndex] = x | y;
            }
            return common;
        }

        /// <summary>
        /// Intersects aligned ranges, or subtracts b from a, and compacts nonempty records.
        /// Returns the shared member count and writes the output record count to written.
        /// Corresponding high-16 keys must match. Destination must have length slots available,
        /// and for each aliased input it must start no later than that input's start.
        /// Slots after the written prefix are unspecified and are not cleared.
        /// </summary>
        internal static int FilterPackedRange(int[] a, int aOffset, int[] b, int bOffset,
            int[] destination, int destinationOffset, int length, bool difference, out int written)
        {
            int i = 0, common = 0, write = 0;
            int differenceMask = difference ? 0xFFFF : 0;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (Avx2.IsSupported && length >= 16)
            {
                var members = Vector256.Create(0xFFFF);
                var keys = Vector256.Create(unchecked((int)0xFFFF0000));
                var subtract = Vector256.Create(differenceMask);
                var table = Lookup; var nibbleMask = Vector256.Create((byte)15);
                var sums = Vector256<ulong>.Zero;
                ref int pa = ref a[aOffset], pb = ref b[bOffset], pd = ref destination[destinationOffset];
                for (; i <= length - 8; i += 8)
                {
                    var x = Vector256.LoadUnsafe(ref pa, (nuint)i);
                    var y = Vector256.LoadUnsafe(ref pb, (nuint)i);
                    var shared = Avx2.And(Avx2.And(x, y), members);
                    sums = Avx2.Add(sums, CountBytes(shared.AsUInt64(), table, nibbleMask));
                    // a - b is a XOR (a AND b); intersection selects no a bits to XOR.
                    var masks = Avx2.Xor(shared, Avx2.And(x, subtract));
                    var records = Avx2.Or(Avx2.And(x, keys), masks);
                    int zeroBytes = Avx2.MoveMask(Avx2.CompareEqual(masks, Vector256<int>.Zero).AsByte());
                    if (zeroBytes == 0)
                    {
                        records.StoreUnsafe(ref pd, (nuint)write); write += 8;
                    }
                    else if (zeroBytes != -1)
                    {
                        // Compact from the captured vector, never re-read aliased input slots.
                        for (int lane = 0; lane < 8; lane++)
                        {
                            int record = records.GetElement(lane);
                            if ((record & 0xFFFF) != 0) destination[destinationOffset + write++] = record;
                        }
                    }
                }
                common = Sum(sums);
            }
            else if (AdvSimd.Arm64.IsSupported && length >= 8)
            {
                var members = Vector128.Create(0xFFFF);
                var keys = Vector128.Create(unchecked((int)0xFFFF0000));
                var subtract = Vector128.Create(differenceMask);
                var sums = Vector128<uint>.Zero;
                ref int pa = ref a[aOffset], pb = ref b[bOffset], pd = ref destination[destinationOffset];
                for (; i <= length - 4; i += 4)
                {
                    var x = Vector128.LoadUnsafe(ref pa, (nuint)i);
                    var y = Vector128.LoadUnsafe(ref pb, (nuint)i);
                    var shared = AdvSimd.And(AdvSimd.And(x, y), members);
                    var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(shared.AsByte()));
                    sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                    var masks = AdvSimd.Xor(shared, AdvSimd.And(x, subtract));
                    var records = AdvSimd.Or(AdvSimd.And(x, keys), masks);
                    if (!Vector128.EqualsAny(masks, Vector128<int>.Zero))
                    {
                        records.StoreUnsafe(ref pd, (nuint)write); write += 4;
                    }
                    else if (!Vector128.EqualsAll(masks, Vector128<int>.Zero))
                    {
                        for (int lane = 0; lane < 4; lane++)
                        {
                            int record = records.GetElement(lane);
                            if ((record & 0xFFFF) != 0) destination[destinationOffset + write++] = record;
                        }
                    }
                }
                common = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            for (; i < length; i++)
            {
                int x = a[aOffset + i], y = b[bOffset + i], shared = x & y & 0xFFFF;
                common += PopCount((uint)shared);
                int mask = shared ^ (x & differenceMask);
                if (mask != 0) destination[destinationOffset + write++] = (x & ~0xFFFF) | mask;
            }
            written = write;
            return common;
        }
    }
}
