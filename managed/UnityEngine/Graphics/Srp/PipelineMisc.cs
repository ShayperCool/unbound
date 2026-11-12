namespace UnityEngine.Rendering
{
    /// <summary>The "development build" / personal edition watermark: never shown.</summary>
    public partial class Watermark
    {
        public Watermark() { }

        [Unbound.EditorFaithful("the Editor shows no watermark in Play mode")]
        public static bool IsVisible() => false;
    }
}
