using System;
using UnityEngine;
using UnityEngine.Rendering;

// Physical storage controls for the selected release, captured on NVIDIA only.
static class DepthShapeControls
{
    public static void Run(Action<string,string> row, Action<RenderTexture,string> observe)
    {
        var previous = RenderTexture.active;
        foreach (var dimension in new[] { TextureDimension.Tex2D, TextureDimension.Tex2DArray, TextureDimension.Cube })
        foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.Shadowmap })
        foreach (bool mips in new[] { false, true })
        {
            string key = "depthShape." + dimension + "." + format + "." + mips;
            var rt = new RenderTexture(8, 8, 24, format, RenderTextureReadWrite.Linear)
            {
                dimension = dimension,
                volumeDepth = dimension == TextureDimension.Tex2DArray ? 3 : 1,
                useMipMap = mips,
                autoGenerateMips = false
            };
            row(key + ".created", rt.Create() ? "true" : "false");
            row(key + ".publicMips", rt.mipmapCount.ToString());
            if (rt.IsCreated())
            {
                observe(rt, key + ".before");
                // A nonzero color level and a nonzero array layer exercise attachment selection.
                int mip = mips ? 1 : 0;
                int layer = dimension == TextureDimension.Tex2DArray ? 2 : 0;
                Graphics.SetRenderTarget(rt, mip, dimension == TextureDimension.Cube ? CubemapFace.NegativeZ : CubemapFace.Unknown, layer);
                GL.Clear(true, true, new Color(.125f, .5f, .75f, 1), .625f);
                RenderTexture.active = previous;
                observe(rt, key + ".after");
            }
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
