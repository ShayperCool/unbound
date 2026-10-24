using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>com.unity.burst with compilation disabled: code runs as managed code.</summary>
[BurstCompile]
public static class BurstProbe
{
    public delegate int BinaryOp(int a, int b);

    public delegate void PointerOp(ref float3 value);

    [BurstCompile]
    public static int Add(int a, int b) => a + b;

    [BurstCompile]
    public static void Scale(ref float3 value) => value *= 2f;

    [BurstCompile]
    public static int DirectCall(int a) => a * 7;

    [BurstCompile]
    public struct BurstJob : IJob
    {
        public NativeArray<int> Out;

        public void Execute()
        {
            Out[0] = DirectCall(6);
            Out[1] = IsManaged() ? 1 : 0;
            Out[2] = X86.Sse2.IsSse2Supported ? 1 : 0;
        }
    }

    [BurstDiscard]
    static void MarkManaged(ref bool managed) => managed = true;

    static bool IsManaged()
    {
        bool managed = false;
        MarkManaged(ref managed);
        return managed;
    }

    struct SharedContext { }
    struct SubContext { }
    struct OtherContext { }

    static readonly SharedStatic<int> s_Shared = SharedStatic<int>.GetOrCreate<SharedContext>();

    public static void RunAll()
    {
        ProbeLog.Begin("burst.compiler");
        ProbeLog.Value("isEnabled", BurstCompiler.IsEnabled);
        ProbeLog.Value("options.enableBurstCompilation", BurstCompiler.Options.EnableBurstCompilation);
        ProbeLog.Value("options.enableBurstSafetyChecks", BurstCompiler.Options.EnableBurstSafetyChecks);
        ProbeLog.Value("options.isEnabled", BurstCompiler.Options.IsEnabled);
        ProbeLog.Value("isManaged", IsManaged());
        ProbeLog.Value("directCall", DirectCall(3));
        ProbeLog.Value("executionMode", BurstCompiler.GetExecutionMode().ToString());
        ProbeLog.Value("loadAdditionalLibrarySupported", BurstCompiler.IsLoadAdditionalLibrarySupported());

        ProbeLog.Begin("burst.functionPointer");
        try
        {
            var add = BurstCompiler.CompileFunctionPointer<BinaryOp>(Add);
            ProbeLog.Value("isCreated", add.IsCreated);
            ProbeLog.Value("valueNonZero", add.Value != IntPtr.Zero);
            ProbeLog.Value("invoke", add.Invoke(20, 22));
            var scale = BurstCompiler.CompileFunctionPointer<PointerOp>(Scale);
            var v = new float3(1, 2, 3);
            scale.Invoke(ref v);
            ProbeLog.Value("refInvoke.z", v.z);
            var again = BurstCompiler.CompileFunctionPointer<BinaryOp>(Add);
            ProbeLog.Value("sameValue", again.Value == add.Value);
            var fromPointer = new FunctionPointer<BinaryOp>(add.Value);
            ProbeLog.Value("fromPointer.invoke", fromPointer.Invoke(1, 2));
        }
        catch (Exception e)
        {
            ProbeLog.Error(e);
        }
        ProbeLog.Try("nonStatic", () => BurstCompiler.CompileFunctionPointer<BinaryOp>(new Instance().Add));

        ProbeLog.Begin("burst.sharedStatic");
        s_Shared.Data = 41;
        var same = SharedStatic<int>.GetOrCreate<SharedContext>();
        same.Data++;
        ProbeLog.Value("shared", s_Shared.Data);
        var sub = SharedStatic<int>.GetOrCreate<SharedContext, SubContext>();
        ProbeLog.Value("subInitial", sub.Data);
        var other = SharedStatic<long>.GetOrCreate<OtherContext>(16);
        unsafe
        {
            ProbeLog.Value("aligned", ((long)other.UnsafeDataPointer & 15) == 0);
        }
        ProbeLog.Try("sizeMismatch", () => SharedStatic<long>.GetOrCreate<SharedContext>());

        ProbeLog.Begin("burst.job");
        var output = new NativeArray<int>(3, Allocator.TempJob);
        new BurstJob { Out = output }.Schedule().Complete();
        ProbeLog.Value("directCall", output[0]);
        ProbeLog.Value("isManaged", output[1]);
        ProbeLog.Value("sse2", output[2]);
        output.Dispose();

        ProbeLog.Begin("burst.runtime");
        ProbeLog.Value("hash64", BurstRuntime.GetHashCode64<float3>());
        ProbeLog.Value("hash32", BurstRuntime.GetHashCode32<float3>());
        ProbeLog.Value("hash32Type", BurstRuntime.GetHashCode32(typeof(int)));
        ProbeLog.Value("hash64Type", BurstRuntime.GetHashCode64(typeof(int)));
        ProbeLog.Value("avx2", X86.Avx2.IsAvx2Supported);
        ProbeLog.Value("neon", Arm.Neon.IsNeonSupported);
    }

    sealed class Instance
    {
        public int Add(int a, int b) => a + b;
    }
}
