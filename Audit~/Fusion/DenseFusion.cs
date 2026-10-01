// Experimental integration backend. Internal callers establish equal array lengths.
// One pass writes the complete bitmap and returns its exact cardinality.
// .NET Standard 2.1 uses the scalar implementation; .NET 8 may use AVX2/NEON.
using System;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
#endif
namespace GameplayTags.Experiments
{
    internal static class DenseFusion
    {
        public static string Backend
        {
            get
            {
#if NET8_0_OR_GREATER
                if (Avx2.IsSupported) return "AVX2";
                if (AdvSimd.Arm64.IsSupported) return "NEON";
                return "scalar-BitOperations";
#else
                return "portable-SWAR";
#endif
            }
        }
#if NET8_0_OR_GREATER
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
        private static int Pop(ulong x)
        {
#if NET8_0_OR_GREATER
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
        internal static unsafe int Union(ulong[] a, ulong[] b, ulong[] output)
        {
            int i = 0, total = 0, n = a.Length;
#if NET8_0_OR_GREATER
            if (Avx2.IsSupported && n >= 8)
            {
                var table = Lookup; var mask = Vector256.Create((byte)15);
                var sum0 = Vector256<ulong>.Zero; var sum1 = sum0;
                fixed (ulong* pa = a, pb = b, po = output)
                {
                    for (; i <= n - 8; i += 8)
                    {
                        var x = Avx2.Or(Avx.LoadVector256(pa + i), Avx.LoadVector256(pb + i));
                        var y = Avx2.Or(Avx.LoadVector256(pa + i + 4), Avx.LoadVector256(pb + i + 4));
                        Avx.Store(po + i, x); Avx.Store(po + i + 4, y);
                        sum0 = Avx2.Add(sum0, CountBytes(x, table, mask));
                        sum1 = Avx2.Add(sum1, CountBytes(y, table, mask));
                    }
                    if (i <= n - 4)
                    {
                        var x = Avx2.Or(Avx.LoadVector256(pa + i), Avx.LoadVector256(pb + i));
                        Avx.Store(po + i, x); sum0 = Avx2.Add(sum0, CountBytes(x, table, mask)); i += 4;
                    }
                }
                total = Sum(Avx2.Add(sum0, sum1));
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                var sums = Vector128<uint>.Zero;
                fixed (ulong* pa = a, pb = b, po = output)
                    for (; i <= n - 2; i += 2)
                    {
                        var x = AdvSimd.Or(AdvSimd.LoadVector128(pa + i), AdvSimd.LoadVector128(pb + i));
                        AdvSimd.Store(po + i, x);
                        var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(x.AsByte()));
                        sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                    }
                total = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= n - 4; i += 4)
            {
                ulong x0 = a[i] | b[i], x1 = a[i+1] | b[i+1], x2 = a[i+2] | b[i+2], x3 = a[i+3] | b[i+3];
                output[i] = x0; output[i+1] = x1; output[i+2] = x2; output[i+3] = x3;
                c0 += Pop(x0); c1 += Pop(x1); c2 += Pop(x2); c3 += Pop(x3);
            }
            for (; i < n; i++) { ulong x = a[i] | b[i]; output[i] = x; total += Pop(x); }
            return total + c0 + c1 + c2 + c3;
        }
        internal static unsafe int Intersection(ulong[] a, ulong[] b, ulong[] output)
        {
            int i = 0, total = 0, n = a.Length;
#if NET8_0_OR_GREATER
            if (Avx2.IsSupported && n >= 8)
            {
                var table = Lookup; var mask = Vector256.Create((byte)15);
                var sum0 = Vector256<ulong>.Zero; var sum1 = sum0;
                fixed (ulong* pa = a, pb = b, po = output)
                {
                    for (; i <= n - 8; i += 8)
                    {
                        var x = Avx2.And(Avx.LoadVector256(pa + i), Avx.LoadVector256(pb + i));
                        var y = Avx2.And(Avx.LoadVector256(pa + i + 4), Avx.LoadVector256(pb + i + 4));
                        Avx.Store(po + i, x); Avx.Store(po + i + 4, y);
                        sum0 = Avx2.Add(sum0, CountBytes(x, table, mask));
                        sum1 = Avx2.Add(sum1, CountBytes(y, table, mask));
                    }
                    if (i <= n - 4)
                    {
                        var x = Avx2.And(Avx.LoadVector256(pa + i), Avx.LoadVector256(pb + i));
                        Avx.Store(po + i, x); sum0 = Avx2.Add(sum0, CountBytes(x, table, mask)); i += 4;
                    }
                }
                total = Sum(Avx2.Add(sum0, sum1));
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                var sums = Vector128<uint>.Zero;
                fixed (ulong* pa = a, pb = b, po = output)
                    for (; i <= n - 2; i += 2)
                    {
                        var x = AdvSimd.And(AdvSimd.LoadVector128(pa + i), AdvSimd.LoadVector128(pb + i));
                        AdvSimd.Store(po + i, x);
                        var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(x.AsByte()));
                        sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                    }
                total = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= n - 4; i += 4)
            {
                ulong x0 = a[i] & b[i], x1 = a[i+1] & b[i+1], x2 = a[i+2] & b[i+2], x3 = a[i+3] & b[i+3];
                output[i] = x0; output[i+1] = x1; output[i+2] = x2; output[i+3] = x3;
                c0 += Pop(x0); c1 += Pop(x1); c2 += Pop(x2); c3 += Pop(x3);
            }
            for (; i < n; i++) { ulong x = a[i] & b[i]; output[i] = x; total += Pop(x); }
            return total + c0 + c1 + c2 + c3;
        }
        internal static unsafe int Difference(ulong[] a, ulong[] b, ulong[] output)
        {
            int i = 0, total = 0, n = a.Length;
#if NET8_0_OR_GREATER
            if (Avx2.IsSupported && n >= 8)
            {
                var table = Lookup; var mask = Vector256.Create((byte)15);
                var sum0 = Vector256<ulong>.Zero; var sum1 = sum0;
                fixed (ulong* pa = a, pb = b, po = output)
                {
                    for (; i <= n - 8; i += 8)
                    {
                        var x = Avx2.AndNot(Avx.LoadVector256(pb + i), Avx.LoadVector256(pa + i));
                        var y = Avx2.AndNot(Avx.LoadVector256(pb + i + 4), Avx.LoadVector256(pa + i + 4));
                        Avx.Store(po + i, x); Avx.Store(po + i + 4, y);
                        sum0 = Avx2.Add(sum0, CountBytes(x, table, mask));
                        sum1 = Avx2.Add(sum1, CountBytes(y, table, mask));
                    }
                    if (i <= n - 4)
                    {
                        var x = Avx2.AndNot(Avx.LoadVector256(pb + i), Avx.LoadVector256(pa + i));
                        Avx.Store(po + i, x); sum0 = Avx2.Add(sum0, CountBytes(x, table, mask)); i += 4;
                    }
                }
                total = Sum(Avx2.Add(sum0, sum1));
            }
            else if (AdvSimd.Arm64.IsSupported && n >= 4)
            {
                var sums = Vector128<uint>.Zero;
                fixed (ulong* pa = a, pb = b, po = output)
                    for (; i <= n - 2; i += 2)
                    {
                        var x = AdvSimd.BitwiseClear(AdvSimd.LoadVector128(pa + i), AdvSimd.LoadVector128(pb + i));
                        AdvSimd.Store(po + i, x);
                        var c16 = AdvSimd.AddPairwiseWidening(AdvSimd.PopCount(x.AsByte()));
                        sums = AdvSimd.Add(sums, AdvSimd.AddPairwiseWidening(c16));
                    }
                total = (int)AdvSimd.Arm64.AddAcross(sums).ToScalar();
            }
#endif
            int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= n - 4; i += 4)
            {
                ulong x0 = a[i] & ~b[i], x1 = a[i+1] & ~b[i+1], x2 = a[i+2] & ~b[i+2], x3 = a[i+3] & ~b[i+3];
                output[i] = x0; output[i+1] = x1; output[i+2] = x2; output[i+3] = x3;
                c0 += Pop(x0); c1 += Pop(x1); c2 += Pop(x2); c3 += Pop(x3);
            }
            for (; i < n; i++) { ulong x = a[i] & ~b[i]; output[i] = x; total += Pop(x); }
            return total + c0 + c1 + c2 + c3;
        }
    }
}
