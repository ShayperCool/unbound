using System;

namespace UnityEditor
{
    /// <summary>Runs a class's static constructor when scripts load (honoured by
    /// <c>EditorInitialization</c>).</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public partial class InitializeOnLoadAttribute : Attribute
    {
        public InitializeOnLoadAttribute() { }
    }

    /// <summary>Calls a static method when scripts load (honoured by <c>EditorInitialization</c>).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public partial class InitializeOnLoadMethodAttribute : Attribute
    {
        public InitializeOnLoadMethodAttribute() { }
    }

    /// <summary>Calls a static method on entering Play mode (honoured by
    /// <c>EditorInitialization</c>).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public partial class InitializeOnEnterPlayModeAttribute : Attribute
    {
        public InitializeOnEnterPlayModeAttribute() { }
    }
}
