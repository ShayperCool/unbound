namespace UnityEngine.U2D
{
    /// <summary>World-space sprite translation grid, applied while collecting draws without
    /// changing the Transform (6000.0.71f1, tests/projects/renderapi).</summary>
    public static partial class PixelPerfectRendering
    {
        public static float pixelSnapSpacing { get; set; }

        internal static Matrix4x4 Snap(Matrix4x4 model)
        {
            float spacing = pixelSnapSpacing;
            if (spacing == 0 || !float.IsFinite(spacing)) return model;
            model.m03 = Mathf.Round(model.m03 / spacing) * spacing;
            model.m13 = Mathf.Round(model.m13 / spacing) * spacing;
            model.m23 = Mathf.Round(model.m23 / spacing) * spacing;
            return model;
        }
    }
}
