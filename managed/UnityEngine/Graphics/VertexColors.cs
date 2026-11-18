namespace UnityEngine
{
    /// <summary>Where a renderer's own color (SpriteRenderer.color, a tilemap's cell colors,
    /// SpriteShapeRenderer.color) reaches the shader of a draw (see <see cref="VertexColors"/>).</summary>
    internal enum DrawTint
    {
        /// <summary>The draw's color is no renderer color: it multiplies the vertex colors as it is.</summary>
        None,
        /// <summary>A renderer color of the vertices: `unity_SpriteColor` for a program that declares it
        /// (vertex colors left alone), else the vertex colors' bytes (SpriteRenderer, a tilemap's cells drawn one
        /// by one).</summary>
        Vertices,
        /// <summary>A renderer color of constants only: `unity_SpriteColor` for a program that declares it;
        /// the vertex colors are left alone (SpriteShapeRenderer, which also sets `_RendererColor`).</summary>
        Constant,
    }

    /// <summary>
    /// How renderers put colors into vertex data and per-draw constants (matches the Unity 6000.0.71f1
    /// Editor, tests/projects/vertexcolors): a renderer's color is in the project's color space
    /// (<see cref="InColorSpace"/>); in vertex colors and in the built-in pipeline's
    /// `_RendererColor` it is stored as bytes (<see cref="Bytes"/>), in `unity_SpriteColor`
    /// as it is (unclamped), for every program that declares it (URP's shader library does, for all its
    /// programs). A mesh's own vertex colors are never converted.
    /// </summary>
    internal static class VertexColors
    {
        /// <summary>A channel as a vertex color byte: clamped to 0..1, times 255, rounded half up.</summary>
        internal static byte Byte(float value) => (byte)(Mathf.Clamp01(value) * 255f + 0.5f);

        /// <summary>A color as vertex color bytes (<see cref="Byte"/> of each channel).</summary>
        internal static Color32 Bytes(Color color) => new Color32(Byte(color.r), Byte(color.g), Byte(color.b), Byte(color.a));

        /// <summary>A renderer's color in the project's color space: its color channels taken to
        /// linear (<see cref="Color.linear"/>, outside 0..1 too) in a linear project, the alpha as it is.</summary>
        internal static Color InColorSpace(Color color) => QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
    }
}
