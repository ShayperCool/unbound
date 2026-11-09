namespace UnityEngine.Rendering
{
    /// <summary>The "development build" / personal edition watermark: never shown.</summary>
    public partial class Watermark
    {
        public Watermark() { }

        [Unbound.EditorFaithful("the Editor shows no watermark in Play mode")]
        public static bool IsVisible() => false;
    }

    public partial class BatchRendererGroup
    {
        BatchRendererGroup() { }

        // The Editor's answers on the run's device (measured on both: CapsProbe).
        public static BatchBufferTarget BufferTarget => Unbound.Runtime.GraphicsDeviceProfile.Current.BatchBufferTarget;

        [Unbound.EditorFaithful("the Editor's OpenGLCore answer (measured: tests/projects/urp CapsProbe)")]
        public static int GetConstantBufferMaxWindowSize() => 16384;

        [Unbound.EditorFaithful("the device's answer (measured on both: tests/projects/urp CapsProbe)")]
        public static int GetConstantBufferOffsetAlignment() => Unbound.Runtime.GraphicsDeviceProfile.Current.ConstantBufferOffsetAlignment;
    }
}
