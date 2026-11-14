namespace UnityEngine
{
    /// <summary>Dynamic resolution scaling. Unbound's renderer does not scale buffers: the factors
    /// stay 1 and resizing does nothing, as the Linux Editor's devices do (tests/projects/builtinfx).
    /// The macOS Editor's Metal device scales them (ResizeBuffers sets the factors: two captures):
    /// there a scale other than 1, and the factors read after one, are warned of.</summary>
    public static partial class ScalableBufferManager
    {
        /// <summary>A scale other than 1 was asked for on a device whose Editor scales.</summary>
        static bool s_Scaled;

        [Unbound.EditorFaithful("the Linux Editor's devices have no dynamic resolution: buffers keep their size (warned on Metal)")]
        public static void ResizeBuffers(float widthScale, float heightScale)
        {
            s_Scaled = Unbound.Runtime.GraphicsDeviceProfile.Current.EditorDynamicResolution
                && (widthScale != 1f || heightScale != 1f);
            WarnIfScaled();
        }

        [Unbound.EditorFaithful("the Linux Editor's devices have no dynamic resolution (warned on Metal)")]
        public static float widthScaleFactor { get { WarnIfScaled(); return 1f; } }

        [Unbound.EditorFaithful("the Linux Editor's devices have no dynamic resolution (warned on Metal)")]
        public static float heightScaleFactor { get { WarnIfScaled(); return 1f; } }

        static void WarnIfScaled()
        {
            if (s_Scaled)
                Unbound.Runtime.Stubs.Ignored("ScalableBufferManager", "does not scale buffers: the factors stay 1 (the Editor's Metal device scales them)");
        }
    }
}
