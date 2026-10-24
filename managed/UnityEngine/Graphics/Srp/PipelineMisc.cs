namespace UnityEngine.Rendering
{
    /// <summary>The "development build" / personal edition watermark: never shown.</summary>
    public partial class Watermark
    {
        public Watermark() { }

        public static bool IsVisible() => false;
    }

    public partial class BatchRendererGroup
    {
        BatchRendererGroup() { }

        // The Editor's answers on its OpenGL Core device (measured: CapsProbe).
        public static BatchBufferTarget BufferTarget => BatchBufferTarget.ConstantBuffer;

        public static int GetConstantBufferMaxWindowSize() => 16384;

        public static int GetConstantBufferOffsetAlignment() => 256;
    }
}
