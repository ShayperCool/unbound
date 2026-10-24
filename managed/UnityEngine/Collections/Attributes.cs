using System;

// The attributes the job safety system reads from job structs and containers (see
// Unbound.Runtime.Jobs.JobLayout), and Burst's and IL2CPP's markers.

namespace Unity.Collections
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed partial class ReadOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed partial class WriteOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class DeallocateOnJobCompletionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class NativeDisableParallelForRestrictionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class NativeFixedLengthAttribute : Attribute
    {
        public int FixedLength;

        public NativeFixedLengthAttribute(int fixedLength)
        {
            FixedLength = fixedLength;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class NativeMatchesParallelForLengthAttribute : Attribute
    {
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed partial class NativeContainerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed partial class NativeContainerIsAtomicWriteOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed partial class NativeContainerIsReadOnlyAttribute : Attribute
    {
    }

    [Obsolete("Use NativeSetThreadIndexAttribute instead")]
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed partial class NativeContainerNeedsThreadIndexAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed partial class NativeContainerSupportsDeallocateOnJobCompletionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed partial class NativeContainerSupportsDeferredConvertListToArray : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed partial class NativeContainerSupportsMinMaxWriteRestrictionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class NativeDisableContainerSafetyRestrictionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class NativeDisableUnsafePtrRestrictionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class NativeSetClassTypeToNullOnScheduleAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class NativeSetThreadIndexAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public partial class WriteAccessRequiredAttribute : Attribute
    {
    }
}

namespace Unity.Jobs.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed partial class JobProducerTypeAttribute : Attribute
    {
        public Type ProducerType { get; }

        public JobProducerTypeAttribute(Type producerType)
        {
            ProducerType = producerType;
        }
    }
}

namespace Unity.Burst
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public partial class BurstDiscardAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public partial class BurstAuthorizedExternalMethodAttribute : Attribute
    {
    }
}

namespace Unity.Profiling
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = false)]
    public sealed partial class IgnoredByDeepProfilerAttribute : Attribute
    {
    }
}

namespace Unity.IL2CPP.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    internal partial class Il2CppEagerStaticClassConstructionAttribute : Attribute
    {
    }
}

namespace AOT
{
    /// <summary>Marks a static method native code calls back (IL2CPP's reverse P/Invoke); CoreCLR
    /// marshals the delegates of such methods itself.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public partial class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type type)
        {
        }
    }
}
