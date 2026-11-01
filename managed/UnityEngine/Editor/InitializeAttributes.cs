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

namespace UnityEditor
{
    /// <summary>An editor callback attribute with an order: lower orders are called first.</summary>
    public abstract partial class CallbackOrderAttribute : Attribute
    {
        protected int m_CallbackOrder;

        protected CallbackOrderAttribute() { }

        internal int callbackOrder => m_CallbackOrder;
    }
}

namespace UnityEditor.Callbacks
{
    /// <summary>Calls a static method after the editor domain's scripts loaded, by callback order
    /// (honoured by <c>EditorInitialization</c>).</summary>
    public sealed partial class DidReloadScripts : CallbackOrderAttribute
    {
        public DidReloadScripts() { m_CallbackOrder = DefaultOrder; }

        public DidReloadScripts(int callbackOrder) { m_CallbackOrder = callbackOrder; }

        /// <summary>The order of a method that names none: after order 0, before 3 (measured:
        /// tests/projects/editorcallbacks).</summary>
        internal const int DefaultOrder = 1;
    }

    /// <summary>Calls a static method for each scene loaded in Play mode, by callback order
    /// (honoured by <c>EditorInitialization.ProcessScene</c>).</summary>
    public sealed partial class PostProcessSceneAttribute : CallbackOrderAttribute
    {
        int m_version;

        public PostProcessSceneAttribute() { m_CallbackOrder = DefaultOrder; }

        public PostProcessSceneAttribute(int callbackOrder) { m_CallbackOrder = callbackOrder; }

        public PostProcessSceneAttribute(int callbackOrder, int version)
        {
            m_CallbackOrder = callbackOrder;
            m_version = version;
        }

        internal int GetVersion() => m_version;

        /// <summary>The order of a method that names none: after order 0, before 3 (measured:
        /// tests/projects/editorcallbacks).</summary>
        internal const int DefaultOrder = 1;
    }
}
