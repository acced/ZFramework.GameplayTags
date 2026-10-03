using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
Console.WriteLine(JsonSerializer.Serialize(new {
    runtime = RuntimeInformation.FrameworkDescription,
    process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    os_architecture = RuntimeInformation.OSArchitecture.ToString(),
    vector_accelerated = Vector.IsHardwareAccelerated,
    vector_uint_lanes = Vector<uint>.Count,
    avx2 = Avx2.IsSupported,
    popcnt = Popcnt.IsSupported,
    popcnt_x64 = Popcnt.X64.IsSupported,
    advsimd = AdvSimd.IsSupported,
    advsimd_arm64 = AdvSimd.Arm64.IsSupported,
    dotnet_enable_hw_intrinsic = Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic"),
    complus_enable_hw_intrinsic = Environment.GetEnvironmentVariable("COMPlus_EnableHWIntrinsic")
}));
