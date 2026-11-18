using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace UnityEngine.Animations
{
    internal enum JobMethodIndex
    {
        ProcessRootMotionMethodIndex = 0,
        ProcessAnimationMethodIndex = 1,
        MethodIndexCount = 2,
    }

    // The job producer IAnimationJob names. Declarations follow the selected release's assemblies;
    // Execute runs a job's method by index on a stream it copies from the pointer, as in the
    // Unity 6000.0.71f1 Editor (tests/projects/animjobproducer). Script playables run their jobs
    // through it.
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal partial struct ProcessAnimationJobStruct<T> where T : struct, IAnimationJob
    {
        static IntPtr jobReflectionData;

        public delegate void ExecuteJobFunction(ref T data, IntPtr animationStreamPtr, IntPtr unusedPtr, ref JobRanges ranges, int jobIndex);

        public static IntPtr GetJobReflectionData()
        {
            if (jobReflectionData == IntPtr.Zero)
                jobReflectionData = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute), null, null);
            return jobReflectionData;
        }

        public static unsafe void Execute(ref T data, IntPtr animationStreamPtr, IntPtr methodIndex, ref JobRanges ranges, int jobIndex)
        {
            UnsafeUtility.CopyPtrToStructure((void*)animationStreamPtr, out AnimationStream stream);
            switch ((JobMethodIndex)methodIndex.ToInt32())
            {
                case JobMethodIndex.ProcessRootMotionMethodIndex: data.ProcessRootMotion(stream); break;
                case JobMethodIndex.ProcessAnimationMethodIndex: data.ProcessAnimation(stream); break;
                default: throw new NotImplementedException("Invalid Animation jobs method index.");
            }
        }
    }
}

namespace Unbound.Runtime.Playables
{
    using UnityEngine.Animations;

    /// <summary>How a script playable runs its boxed job of type <typeparamref name="T"/>: through
    /// the producer's Execute, in place, so the job keeps what its methods change.</summary>
    internal static class AnimationJobDispatch<T> where T : struct, IAnimationJob
    {
        internal static readonly Action<object, IntPtr, IntPtr> Run = (job, stream, method) =>
        {
            var ranges = default(JobRanges);
            ProcessAnimationJobStruct<T>.Execute(ref System.Runtime.CompilerServices.Unsafe.Unbox<T>(job), stream, method, ref ranges, 0);
        };
    }
}
