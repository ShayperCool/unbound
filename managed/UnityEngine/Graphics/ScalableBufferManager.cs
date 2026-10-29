namespace UnityEngine
{
    /// <summary>Dynamic resolution scaling. The Editor does not scale buffers: the factors stay 1 and
    /// resizing does nothing, as in the Game view.</summary>
    public static partial class ScalableBufferManager
    {
        [Unbound.EditorFaithful("the Editor has no dynamic resolution: buffers keep their size")]
        public static void ResizeBuffers(float widthScale, float heightScale)
        {
        }

        [Unbound.EditorFaithful("the Editor has no dynamic resolution")]
        public static float widthScaleFactor => 1f;

        [Unbound.EditorFaithful("the Editor has no dynamic resolution")]
        public static float heightScaleFactor => 1f;
    }
}
