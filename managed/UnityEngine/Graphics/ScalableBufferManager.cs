namespace UnityEngine
{
    /// <summary>Dynamic resolution scaling. Unbound's renderer does not scale buffers: the factors
    /// stay 1 and resizing does nothing, as the Linux Editor's devices do (tests/projects/builtinfx).
    /// The macOS Editor's Metal device scales them (ResizeBuffers sets the factors: two captures):
    /// warned there.</summary>
    public static partial class ScalableBufferManager
    {
        [Unbound.EditorFaithful("the Linux Editor's devices have no dynamic resolution: buffers keep their size (warned on Metal)")]
        public static void ResizeBuffers(float widthScale, float heightScale)
        {
            WarnWhereTheEditorScales();
        }

        [Unbound.EditorFaithful("the Linux Editor's devices have no dynamic resolution (warned on Metal)")]
        public static float widthScaleFactor { get { WarnWhereTheEditorScales(); return 1f; } }

        [Unbound.EditorFaithful("the Linux Editor's devices have no dynamic resolution (warned on Metal)")]
        public static float heightScaleFactor { get { WarnWhereTheEditorScales(); return 1f; } }

        static void WarnWhereTheEditorScales()
        {
            if (Unbound.Runtime.GraphicsDeviceProfile.Current.EditorDynamicResolution)
                Unbound.Runtime.Stubs.Ignored("ScalableBufferManager", "does not scale buffers: the factors stay 1 (the Editor's Metal device scales them)");
        }
    }
}
