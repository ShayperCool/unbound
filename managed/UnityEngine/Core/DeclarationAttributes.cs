using System;

namespace UnityEngine
{
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)4)]
    public partial class ExcludeFromObjectFactoryAttribute : Attribute
    {
        public ExcludeFromObjectFactoryAttribute() { }
    }

    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2048, AllowMultiple = false)]
    internal partial class WritableAttribute : Attribute
    {
        public WritableAttribute() { }
    }

    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    internal partial class ThreadAndSerializationSafeAttribute : Attribute
    {
        public ThreadAndSerializationSafeAttribute() { }
    }

    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)4)]
    internal partial class RejectDragAndDropMaterial : Attribute
    {
        public RejectDragAndDropMaterial() { }
    }

}
