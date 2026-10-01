using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace GameplayTags
{
    /// <summary>Allocation-free bit operations shared by runtime set kernels.</summary>
    /// <remarks>
    /// Internal callers guarantee non-null, equal-length buffers and a cardinality that
    /// fits in an int. A destination may be either input or both inputs. Mutating kernels
    /// write every destination word and return the complete result count in one pass.
    /// No per-set state, pinning, unsafe compilation, or platform package is needed.
    /// Unity/.NET Standard builds use the portable scalar path; .NET 8+ additionally
    /// uses hardware intrinsics when supported by the executing CPU. Define
    /// GAMEPLAYTAGS_FORCE_PORTABLE to validate or benchmark the portable path on .NET 8+.
    /// </remarks>
    internal static class RuntimeBitOperations
    {
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
        // Constant, loaded before the loop. No per-container state or per-word cctor check.
        private static readonly Vector256<byte> Lookup = Vector256.Create(
            (byte)0,1,1,2,1,2,2,3,1,2,2,3,2,3,3,4,
            0,1,1,2,1,2,2,3,1,2,2,3,2,3,3,4);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<ulong> CountBytes(Vector256<ulong> value, Vector256<byte> table, Vector256<byte> mask)
        {
            var bytes = value.AsByte();
            var lo = Avx2.Shuffle(table, Avx2.And(bytes, mask));
            var hi = Avx2.Shuffle(table, Avx2.And(Avx2.ShiftRightLogical(bytes.AsUInt16(), 4).AsByte(), mask));
            return Avx2.SumAbsoluteDifferences(Avx2.Add(lo, hi), Vector256<byte>.Zero).AsUInt64();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Sum(Vector256<ulong> x) => (int)(x.GetElement(0) + x.GetElement(1) + x.GetElement(2) + x.GetElement(3));
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int PopCount(ulong x)
        {
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            return BitOperations.PopCount(x);
#else
            unchecked
            {
                x -= (x >> 1) & 0x5555555555555555UL;
                x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
                x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
                return (int)((x * 0x0101010101010101UL) >> 56);
            }
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int TrailingZeroCount(ulong value)
        {
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            return BitOperations.TrailingZeroCount(value);
#else
            if (value == 0) return 64;
            int shift = 0;
            if ((value & 0xFFFFFFFFUL) == 0) { value >>= 32; shift += 32; }
            if ((value & 0xFFFFUL) == 0) { value >>= 16; shift += 16; }
            if ((value & 0xFFUL) == 0) { value >>= 8; shift += 8; }
            if ((value & 0xFUL) == 0) { value >>= 4; shift += 4; }
            if ((value & 3UL) == 0) { value >>= 2; shift += 2; }
            return shift + ((value & 1UL) == 0 ? 1 : 0);
#endif
        }

        // Keep small sets scalar; vector setup is amortized over at least two vectors.
        // The specialized loops deliberately avoid delegates and per-word operation dispatch.
        internal static int Union(ulong[] a, ulong[] b, ulong[] output)
        {
            int i = 0, total = 0, n = a.Length;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (Avx2.IsSupported && n >= 8)
            {
                var table = Lookup; var mask = Vector256.Create((byte)15);
                var sum0 = Vector256<ulong>.Zero; var sum1 = sum0;
                ref ulong pa = ref a[0], pb = ref b[0], po = ref output[0];
                for (; i <= n - 8; i += 8)
                {
                    var x = Avx2.Or(Vector256.LoadUnsafe(ref pa, (nuint)i), Vector256.LoadUnsafe(ref pb, (nuint)i));
                    var y = Avx2.Or(Vector256.LoadUnsafe(ref pa, (nuint)i + 4), Vector256.LoadUnsafe(ref pb, (nuint)i + 4));
                    x.StoreUnsafe(ref po, (nuint)i); y.StoreUnsafe(ref po, (nuint)i + 4);
                    sum0 = Avx2.Add(sum0, CountBytes(x, table, mask));
                    sum1 = Avx2.Add(sum1, CountBytes(y, table, mask));
                }
                if (i <= n - 4)
                {
                    var x = Avx2.Or(Vector256.LoadUnsafe(ref pa, (nuint)i), Vector256.LoadUnsafe(ref pb, (nuint)i));
                    x.StoreUnsafe(ref po, (nuint)i); sum0 = Avx2.Add(sum0, CountBytes(x, table, mask)); i += 4;
                }
                total = Sum(Avx2.Add(sum0, sum1));
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                var sums = Vector128<uint>.Zero;
                ref ulong pa = ref a[0], pb = ref b[0], po = ref output[0];
                for (; i <= n - 2; i += 2)
                {
                    var x = AdvSimd.Or(Vector128.LoadUnsafe(ref pa, (nuint)i), Vector128.LoadUnsafe(ref pb, (nuint)i));
                    x.StoreUnsafe(ref po, (nuint)i);
                    var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(x.AsByte()));
                    sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                }
                total = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            // Independent accumulators avoid a serial popcount dependency chain.
            int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= n - 4; i += 4)
            {
                ulong x0 = a[i] | b[i], x1 = a[i + 1] | b[i + 1], x2 = a[i + 2] | b[i + 2], x3 = a[i + 3] | b[i + 3];
                output[i] = x0; output[i + 1] = x1; output[i + 2] = x2; output[i + 3] = x3;
                c0 += PopCount(x0); c1 += PopCount(x1); c2 += PopCount(x2); c3 += PopCount(x3);
            }
            for (; i < n; i++) { ulong x = a[i] | b[i]; output[i] = x; total += PopCount(x); }
            return total + c0 + c1 + c2 + c3;
        }
        internal static int Intersect(ulong[] a, ulong[] b, ulong[] output)
        {
            int i = 0, total = 0, n = a.Length;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (Avx2.IsSupported && n >= 8)
            {
                var table = Lookup; var mask = Vector256.Create((byte)15);
                var sum0 = Vector256<ulong>.Zero; var sum1 = sum0;
                ref ulong pa = ref a[0], pb = ref b[0], po = ref output[0];
                for (; i <= n - 8; i += 8)
                {
                    var x = Avx2.And(Vector256.LoadUnsafe(ref pa, (nuint)i), Vector256.LoadUnsafe(ref pb, (nuint)i));
                    var y = Avx2.And(Vector256.LoadUnsafe(ref pa, (nuint)i + 4), Vector256.LoadUnsafe(ref pb, (nuint)i + 4));
                    x.StoreUnsafe(ref po, (nuint)i); y.StoreUnsafe(ref po, (nuint)i + 4);
                    sum0 = Avx2.Add(sum0, CountBytes(x, table, mask));
                    sum1 = Avx2.Add(sum1, CountBytes(y, table, mask));
                }
                if (i <= n - 4)
                {
                    var x = Avx2.And(Vector256.LoadUnsafe(ref pa, (nuint)i), Vector256.LoadUnsafe(ref pb, (nuint)i));
                    x.StoreUnsafe(ref po, (nuint)i); sum0 = Avx2.Add(sum0, CountBytes(x, table, mask)); i += 4;
                }
                total = Sum(Avx2.Add(sum0, sum1));
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                var sums = Vector128<uint>.Zero;
                ref ulong pa = ref a[0], pb = ref b[0], po = ref output[0];
                for (; i <= n - 2; i += 2)
                {
                    var x = AdvSimd.And(Vector128.LoadUnsafe(ref pa, (nuint)i), Vector128.LoadUnsafe(ref pb, (nuint)i));
                    x.StoreUnsafe(ref po, (nuint)i);
                    var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(x.AsByte()));
                    sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                }
                total = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            // Independent accumulators avoid a serial popcount dependency chain.
            int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= n - 4; i += 4)
            {
                ulong x0 = a[i] & b[i], x1 = a[i + 1] & b[i + 1], x2 = a[i + 2] & b[i + 2], x3 = a[i + 3] & b[i + 3];
                output[i] = x0; output[i + 1] = x1; output[i + 2] = x2; output[i + 3] = x3;
                c0 += PopCount(x0); c1 += PopCount(x1); c2 += PopCount(x2); c3 += PopCount(x3);
            }
            for (; i < n; i++) { ulong x = a[i] & b[i]; output[i] = x; total += PopCount(x); }
            return total + c0 + c1 + c2 + c3;
        }
        internal static int Except(ulong[] a, ulong[] b, ulong[] output)
        {
            int i = 0, total = 0, n = a.Length;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (Avx2.IsSupported && n >= 8)
            {
                var table = Lookup; var mask = Vector256.Create((byte)15);
                var sum0 = Vector256<ulong>.Zero; var sum1 = sum0;
                ref ulong pa = ref a[0], pb = ref b[0], po = ref output[0];
                for (; i <= n - 8; i += 8)
                {
                    var x = Avx2.AndNot(Vector256.LoadUnsafe(ref pb, (nuint)i), Vector256.LoadUnsafe(ref pa, (nuint)i));
                    var y = Avx2.AndNot(Vector256.LoadUnsafe(ref pb, (nuint)i + 4), Vector256.LoadUnsafe(ref pa, (nuint)i + 4));
                    x.StoreUnsafe(ref po, (nuint)i); y.StoreUnsafe(ref po, (nuint)i + 4);
                    sum0 = Avx2.Add(sum0, CountBytes(x, table, mask));
                    sum1 = Avx2.Add(sum1, CountBytes(y, table, mask));
                }
                if (i <= n - 4)
                {
                    var x = Avx2.AndNot(Vector256.LoadUnsafe(ref pb, (nuint)i), Vector256.LoadUnsafe(ref pa, (nuint)i));
                    x.StoreUnsafe(ref po, (nuint)i); sum0 = Avx2.Add(sum0, CountBytes(x, table, mask)); i += 4;
                }
                total = Sum(Avx2.Add(sum0, sum1));
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                var sums = Vector128<uint>.Zero;
                ref ulong pa = ref a[0], pb = ref b[0], po = ref output[0];
                for (; i <= n - 2; i += 2)
                {
                    var x = AdvSimd.BitwiseClear(Vector128.LoadUnsafe(ref pa, (nuint)i), Vector128.LoadUnsafe(ref pb, (nuint)i));
                    x.StoreUnsafe(ref po, (nuint)i);
                    var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(x.AsByte()));
                    sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                }
                total = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            // Independent accumulators avoid a serial popcount dependency chain.
            int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= n - 4; i += 4)
            {
                ulong x0 = a[i] & ~b[i], x1 = a[i + 1] & ~b[i + 1], x2 = a[i + 2] & ~b[i + 2], x3 = a[i + 3] & ~b[i + 3];
                output[i] = x0; output[i + 1] = x1; output[i + 2] = x2; output[i + 3] = x3;
                c0 += PopCount(x0); c1 += PopCount(x1); c2 += PopCount(x2); c3 += PopCount(x3);
            }
            for (; i < n; i++) { ulong x = a[i] & ~b[i]; output[i] = x; total += PopCount(x); }
            return total + c0 + c1 + c2 + c3;
        }
        /// <summary>Returns whether the two bitmaps have any member in common.</summary>
        internal static bool IsAny(ulong[] a, ulong[] b)
        {
            int i = 0, n = a.Length;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (Avx2.IsSupported && n >= 8)
            {
                ref ulong pa = ref a[0], pb = ref b[0];
                for (; i <= n - 4; i += 4)
                {
                    var x = Vector256.LoadUnsafe(ref pa, (nuint)i);
                    var y = Vector256.LoadUnsafe(ref pb, (nuint)i);
                    if (!Avx2.TestZ(x, y)) return true;
                }
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                ref ulong pa = ref a[0], pb = ref b[0];
                for (; i <= n - 2; i += 2)
                {
                    var x = AdvSimd.And(Vector128.LoadUnsafe(ref pa, (nuint)i),
                        Vector128.LoadUnsafe(ref pb, (nuint)i));
                    if (AdvSimd.Arm64.MaxAcross(x.AsUInt32()).ToScalar() != 0) return true;
                }
            }
#endif
            for (; i < n; i++) if ((a[i] & b[i]) != 0) return true;
            return false;
        }

        /// <summary>Returns whether every member of subset is present in superset.</summary>
        internal static bool IsSubset(ulong[] subset, ulong[] superset)
        {
            int i = 0, n = subset.Length;
#if NET8_0_OR_GREATER && !GAMEPLAYTAGS_FORCE_PORTABLE
            if (Avx2.IsSupported && n >= 8)
            {
                ref ulong pa = ref subset[0], pb = ref superset[0];
                for (; i <= n - 4; i += 4)
                {
                    var x = Vector256.LoadUnsafe(ref pa, (nuint)i);
                    var y = Vector256.LoadUnsafe(ref pb, (nuint)i);
                    var missing = Avx2.AndNot(y, x);
                    if (!Avx2.TestZ(missing, missing)) return false;
                }
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                ref ulong pa = ref subset[0], pb = ref superset[0];
                for (; i <= n - 2; i += 2)
                {
                    var missing = AdvSimd.BitwiseClear(Vector128.LoadUnsafe(ref pa, (nuint)i),
                        Vector128.LoadUnsafe(ref pb, (nuint)i));
                    if (AdvSimd.Arm64.MaxAcross(missing.AsUInt32()).ToScalar() != 0) return false;
                }
            }
#endif
            for (; i < n; i++) if ((subset[i] & ~superset[i]) != 0) return false;
            return true;
        }
    }
}
