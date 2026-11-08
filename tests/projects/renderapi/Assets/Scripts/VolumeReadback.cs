using System;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class RenderApiProbe
{
    void VolumeReadbackCases()
    {
        var saved = RenderTexture.active;
        foreach (bool volume in new[] { false, true })
        foreach (var format in new[] { RenderTextureFormat.R8, RenderTextureFormat.RG16,
            RenderTextureFormat.R16, RenderTextureFormat.RG32, RenderTextureFormat.RHalf,
            RenderTextureFormat.RGHalf, RenderTextureFormat.RFloat, RenderTextureFormat.RGFloat,
            RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGBFloat })
        {
            string key = "volumeReadback." + volume + "." + format;
            var texture = new RenderTexture(2, 1, 0, format, RenderTextureReadWrite.Linear)
            { dimension = volume ? TextureDimension.Tex3D : TextureDimension.Tex2D, volumeDepth = volume ? 2 : 1 };
            bool created = texture.Create();
            Bool(key + ".create", created);
            if (created)
            {
                for (int slice = 0; slice < (volume ? 2 : 1); slice++)
                {
                    Graphics.SetRenderTarget(texture, 0, CubemapFace.Unknown, slice);
                    GL.Clear(false, true, new Color(.25f + slice * .125f, .5f, .75f, .125f));
                }
                VolumeRead(key, texture);
            }
            RenderTexture.active = saved;
            DestroyImmediate(texture);
        }
    }
}
