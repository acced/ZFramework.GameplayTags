// .NET 8 execution experiment; NOT a Unity/Burst/IL2CPP implementation claim.
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace GameplayTags.Experiments
{
    public static class DenseVectorKernels
    {
        // op: 0 = union, 1 = intersection, 2 = difference. Exact output + eager count.
        public static int Run(ulong[] a, ulong[] b, ulong[] output, int op, int kernel)
        {
            if (a == null || b == null || output == null) throw new ArgumentNullException();
            if (a.Length != b.Length || a.Length != output.Length || (uint)op > 2 || (uint)kernel > 3)
                throw new ArgumentException("Invalid buffers/operation/kernel");
            if (kernel == 0) return Swar(a, b, output, op);
            if (kernel == 1) return Hardware(a, b, output, op);
            if (kernel == 2) return VectorFused(a, b, output, op);
            return VectorTwoPass(a, b, output, op);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Apply(ulong a, ulong b, int op) => op == 0 ? a | b : op == 1 ? a & b : a & ~b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<ulong> ApplyVector(Vector256<ulong> a, Vector256<ulong> b, int op)
            => op == 0 ? Avx2.Or(a, b) : op == 1 ? Avx2.And(a, b) : Avx2.AndNot(b, a);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopSwar(ulong v)
        {
            unchecked { v -= (v >> 1) & 0x5555555555555555UL;
                v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
                v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
                return (int)((v * 0x0101010101010101UL) >> 56); }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Swar(ulong[] a, ulong[] b, ulong[] output, int op)
        {
            int i = 0, c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= a.Length - 4; i += 4)
            {
                ulong x0 = Apply(a[i], b[i], op), x1 = Apply(a[i+1], b[i+1], op);
                ulong x2 = Apply(a[i+2], b[i+2], op), x3 = Apply(a[i+3], b[i+3], op);
                output[i] = x0; output[i+1] = x1; output[i+2] = x2; output[i+3] = x3;
                c0 += PopSwar(x0); c1 += PopSwar(x1); c2 += PopSwar(x2); c3 += PopSwar(x3);
            }
            for (; i < a.Length; i++) { ulong x = Apply(a[i], b[i], op); output[i] = x; c0 += PopSwar(x); }
            return c0+c1+c2+c3;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Hardware(ulong[] a, ulong[] b, ulong[] output, int op)
        {
            int i = 0, c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            for (; i <= a.Length - 4; i += 4)
            {
                ulong x0 = Apply(a[i], b[i], op), x1 = Apply(a[i+1], b[i+1], op);
                ulong x2 = Apply(a[i+2], b[i+2], op), x3 = Apply(a[i+3], b[i+3], op);
                output[i] = x0; output[i+1] = x1; output[i+2] = x2; output[i+3] = x3;
                c0 += BitOperations.PopCount(x0); c1 += BitOperations.PopCount(x1);
                c2 += BitOperations.PopCount(x2); c3 += BitOperations.PopCount(x3);
            }
            for (; i < a.Length; i++) { ulong x = Apply(a[i], b[i], op); output[i] = x; c0 += BitOperations.PopCount(x); }
            return c0+c1+c2+c3;
        }
        private static readonly Vector256<byte> Lookup = Vector256.Create(
            Vector128.Create((byte)0, (byte)1, (byte)1, (byte)2, (byte)1, (byte)2, (byte)2, (byte)3,
                (byte)1, (byte)2, (byte)2, (byte)3, (byte)2, (byte)3, (byte)3, (byte)4),
            Vector128.Create((byte)0, (byte)1, (byte)1, (byte)2, (byte)1, (byte)2, (byte)2, (byte)3,
                (byte)1, (byte)2, (byte)2, (byte)3, (byte)2, (byte)3, (byte)3, (byte)4));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<ulong> PopVector(Vector256<ulong> words)
        {
            Vector256<byte> mask = Vector256.Create((byte)15);
            var low = Avx2.And(words.AsByte(), mask);
            var high = Avx2.And(Avx2.ShiftRightLogical(words.AsUInt16(), 4).AsByte(), mask);
            var counts = Avx2.Add(Avx2.Shuffle(Lookup, low), Avx2.Shuffle(Lookup, high));
            return Avx2.SumAbsoluteDifferences(counts, Vector256<byte>.Zero).AsUInt64();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe int VectorFused(ulong[] a, ulong[] b, ulong[] output, int op)
        {
            if (!Avx2.IsSupported) return Hardware(a, b, output, op);
            int i = 0, count = 0; var sums = Vector256<ulong>.Zero;
            fixed (ulong* pa = a, pb = b, po = output)
            {
                for (; i <= a.Length - 4; i += 4)
                {
                    var x = ApplyVector(Avx.LoadVector256(pa+i), Avx.LoadVector256(pb+i), op);
                    Avx.Store(po+i, x); sums = Avx2.Add(sums, PopVector(x));
                }
            }
            count = (int)(sums.GetElement(0)+sums.GetElement(1)+sums.GetElement(2)+sums.GetElement(3));
            for (; i < a.Length; i++) { ulong x = Apply(a[i], b[i], op); output[i] = x; count += BitOperations.PopCount(x); }
            return count;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe int VectorTwoPass(ulong[] a, ulong[] b, ulong[] output, int op)
        {
            if (!Avx2.IsSupported) return Hardware(a, b, output, op);
            int i = 0;
            fixed (ulong* pa = a, pb = b, po = output)
            {
                for (; i <= a.Length - 4; i += 4)
                    Avx.Store(po+i, ApplyVector(Avx.LoadVector256(pa+i), Avx.LoadVector256(pb+i), op));
            }
            for (; i < a.Length; i++) output[i] = Apply(a[i], b[i], op);
            int c0 = 0, c1 = 0, c2 = 0, c3 = 0; i = 0;
            for (; i <= output.Length - 4; i += 4)
            { c0 += BitOperations.PopCount(output[i]); c1 += BitOperations.PopCount(output[i+1]);
              c2 += BitOperations.PopCount(output[i+2]); c3 += BitOperations.PopCount(output[i+3]); }
            for (; i < output.Length; i++) c0 += BitOperations.PopCount(output[i]);
            return c0+c1+c2+c3;
        }
    }
}
