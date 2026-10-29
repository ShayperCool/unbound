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

        // The Editor's answers on its OpenGL Core device (measured: CapsProbe).
        public static BatchBufferTarget BufferTarget => BatchBufferTarget.ConstantBuffer;

        [Unbound.EditorFaithful("the Editor's OpenGLCore answer (measured: tests/projects/urp CapsProbe)")]
        public static int GetConstantBufferMaxWindowSize() => 16384;

        [Unbound.EditorFaithful("the Editor's OpenGLCore answer (measured: tests/projects/urp CapsProbe)")]
        public static int GetConstantBufferOffsetAlignment() => 256;
    }
}
