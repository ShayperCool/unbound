using System;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class RenderApiProbe
{
    void VolumeRead(string key, Texture texture, int mip = 0)
    {
        Try(key, () =>
        {
            var request = AsyncGPUReadback.Request(texture, mip, TextureFormat.RGBAFloat);
            request.WaitForCompletion();
            Bool(key + ".error", request.hasError);
            if (request.hasError) return;
            Value(key + ".shape", "[" + request.width + "," + request.height + "," + request.depth + "," + request.layerCount + "]");
            for (int layer = 0; layer < request.layerCount; layer++)
            {
                var data = request.GetData<float>(layer);
                var values = new string[data.Length];
                for (int i = 0; i < data.Length; i++) values[i] = F(data[i]);
                Value(key + ".layer" + layer, "[" + string.Join(",", values) + "]");
            }
        });
    }

    void VolumeCases()
    {
        Bool("volume.supported", SystemInfo.supports3DRenderTextures);
        var saved = RenderTexture.active;
        var material = new Material(Resources.Load<Shader>("VolumeTargets"));
        var output = new RenderTexture(4, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        output.Create();
        foreach (int depth in new[] { 1, 4, 8 })
        foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBFloat })
        {
            string key = "volume." + format + "." + depth;
            var rt = new RenderTexture(4, 2, 0, format, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = depth,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                useMipMap = true, autoGenerateMips = false
            };
            Bool(key + ".create", rt.Create());
            Value(key + ".descriptor", "[" + (int)rt.dimension + "," + rt.volumeDepth + "," + rt.mipmapCount + "," + rt.antiAliasing + "]");
            for (int slice = 0; slice < depth; slice++)
            {
                Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, slice);
                GL.Clear(false, true, new Color(slice * .25f, .25f, .5f, 1));
            }
            VolumeRead(key + ".cleared", rt);
            material.SetVector("_Color", new Vector4(.75f, .5f, .25f, 1));
            Graphics.Blit(null, rt, material, 0, depth - 1);
            VolumeRead(key + ".blit", rt);
            material.SetTexture("_Volume", rt);
            foreach (float z in new[] { 0f, .125f, .5f, .875f, 1f })
            {
                material.SetFloat("_Slice", z);
                Graphics.Blit(null, output, material, 1);
                VolumeRead(key + ".point" + F(z), output);
            }
            rt.filterMode = FilterMode.Bilinear;
            material.SetFloat("_Slice", .5f);
            Graphics.Blit(null, output, material, 1);
            VolumeRead(key + ".linear", output);
            material.SetTexture("_Volume", null);
            var source = new RenderTexture(4, 2, 0, format, RenderTextureReadWrite.Linear);
            source.Create();
            Graphics.SetRenderTarget(source); GL.Clear(false, true, Color.green);
            Try(key + ".copyInto", () => Graphics.CopyTexture(source, 0, 0, rt, depth - 1, 0));
            VolumeRead(key + ".copied", rt);
            Try(key + ".copyOut", () => Graphics.CopyTexture(rt, depth - 1, 0, source, 0, 0));
            VolumeRead(key + ".copyOutResult", source);
            for (int slice = 0; slice < Math.Max(1, depth >> 1); slice++)
            {
                Graphics.SetRenderTarget(rt, 1, CubemapFace.Unknown, slice);
                GL.Clear(false, true, slice == 0 ? Color.red : Color.blue);
            }
            VolumeRead(key + ".mip1", rt, 1);
            RenderTexture.active = saved;
            DestroyImmediate(source); DestroyImmediate(rt);
        }
        foreach (int depthBits in new[] { 0, 24 })
        foreach (int samples in new[] { 1, 4 })
        {
            string key = "volume.configuration." + depthBits + "." + samples;
            var rt = new RenderTexture(4, 2, depthBits, RenderTextureFormat.ARGB32)
            { dimension = TextureDimension.Tex3D, volumeDepth = 4, antiAliasing = samples };
            Try(key, () =>
            {
                Bool(key + ".create", rt.Create());
                Value(key + ".descriptor", "[" + rt.depth + "," + rt.antiAliasing + "," + rt.volumeDepth + "]");
            });
            DestroyImmediate(rt);
        }
        // A normal 2D target uses the same shader and copy operations.
        Graphics.Blit(null, output, material, 0);
        VolumeRead("volume.control2D", output);
        RenderTexture.active = saved;
        DestroyImmediate(output); DestroyImmediate(material);
        VolumeMipCases();
    }

    void VolumeMipCases()
    {
        var saved = RenderTexture.active;
        var material = new Material(Resources.Load<Shader>("VolumeTargets"));
        foreach (bool automatic in new[] { false, true })
        {
            string key = "volumeMore.mips." + automatic;
            var rt = new RenderTexture(4, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { dimension = TextureDimension.Tex3D, volumeDepth = 8, useMipMap = true, autoGenerateMips = automatic };
            rt.Create();
            for (int slice = 0; slice < 8; slice++)
            {
                material.SetFloat("_Slice", slice / 8f);
                Graphics.Blit(null, rt, material, 2, slice);
            }
            VolumeRead(key + ".base", rt);
            if (!automatic) rt.GenerateMips();
            for (int mip = 1; mip < rt.mipmapCount; mip++) VolumeRead(key + ".level" + mip, rt, mip);
            // Reuse the same mip resources after changing one source slice.
            material.SetFloat("_Slice", .875f);
            Graphics.Blit(null, rt, material, 2, 0);
            if (!automatic) rt.GenerateMips();
            VolumeRead(key + ".updated", rt, 1);
            RenderTexture.active = saved;
            DestroyImmediate(rt);
        }
        DestroyImmediate(material);
        VolumeCopyCases();
    }

    void VolumeCopyCases()
    {
        Value("volumeCopy.max3DSize", SystemInfo.maxTexture3DSize.ToString());
        var saved = RenderTexture.active;
        var descriptor = new RenderTextureDescriptor(4, 2, RenderTextureFormat.ARGBFloat, 0)
        { dimension = TextureDimension.Tex3D, volumeDepth = 4, useMipMap = true, autoGenerateMips = false };
        var source = new RenderTexture(descriptor);
        var destination = new RenderTexture(descriptor);
        source.Create(); destination.Create();
        for (int mip = 0; mip < source.mipmapCount; mip++)
            for (int slice = 0; slice < Math.Max(1, 4 >> mip); slice++)
            {
                Graphics.SetRenderTarget(source, mip, CubemapFace.Unknown, slice);
                GL.Clear(false, true, new Color(mip * .25f, slice * .25f, .125f, 1));
                Graphics.SetRenderTarget(destination, mip, CubemapFace.Unknown, slice);
                GL.Clear(false, true, Color.clear);
            }
        Try("volumeCopy.all", () => Graphics.CopyTexture(source, destination));
        for (int mip = 0; mip < source.mipmapCount; mip++) VolumeRead("volumeCopy.all.level" + mip, destination, mip);
        Graphics.SetRenderTarget(source, 0, CubemapFace.Unknown, 2);
        GL.Clear(false, true, Color.red);
        Try("volumeCopy.region", () => Graphics.CopyTexture(source, 2, 0, 1, 0, 2, 1, destination, 1, 0, 0, 1));
        VolumeRead("volumeCopy.region.result", destination);
        RenderTexture.active = saved;
        DestroyImmediate(source); DestroyImmediate(destination);
        var material = new Material(Resources.Load<Shader>("VolumeTargets"));
        var odd = new RenderTexture(3, 5, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        { dimension = TextureDimension.Tex3D, volumeDepth = 3, useMipMap = true, autoGenerateMips = false };
        odd.Create();
        for (int slice = 0; slice < 3; slice++)
        {
            material.SetFloat("_Slice", slice * .25f);
            Graphics.Blit(null, odd, material, 2, slice);
        }
        odd.GenerateMips();
        for (int mip = 0; mip < odd.mipmapCount; mip++) VolumeRead("volumeCopy.odd.level" + mip, odd, mip);
        RenderTexture.active = saved;
        DestroyImmediate(odd); DestroyImmediate(material);
        VolumeAutoCases();
    }

    void VolumeAutoCases()
    {
        var saved = RenderTexture.active;
        var material = new Material(Resources.Load<Shader>("VolumeTargets"));
        foreach (bool volume in new[] { false, true })
        {
            string key = "volumeAuto." + volume;
            var rt = new RenderTexture(4, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                dimension = volume ? TextureDimension.Tex3D : TextureDimension.Tex2D,
                volumeDepth = volume ? 4 : 1, useMipMap = true, autoGenerateMips = true
            };
            rt.Create();
            material.SetVector("_Color", new Vector4(.25f, .25f, .25f, 1));
            for (int slice = 0; slice < rt.volumeDepth; slice++) Graphics.Blit(null, rt, material, 0, slice);
            RenderTexture.active = saved;
            VolumeRead(key + ".initial", rt, 1);
            int last = rt.volumeDepth - 1;
            material.SetVector("_Color", new Vector4(.5f, .5f, .5f, 1));
            Graphics.Blit(null, rt, material, 0, last);
            VolumeRead(key + ".bound", rt, 1);
            material.SetVector("_Color", new Vector4(.75f, .75f, .75f, 1));
            Graphics.Blit(null, rt, material, 0, last);
            VolumeRead(key + ".sameSlice", rt, 1);
            Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, last);
            VolumeRead(key + ".sameBinding", rt, 1);
            Try(key + ".manualOnAuto", () => rt.GenerateMips());
            VolumeRead(key + ".afterManual", rt, 1);
            RenderTexture.active = saved;
            VolumeRead(key + ".unbound", rt, 1);
            DestroyImmediate(rt);
        }
        DestroyImmediate(material);
    }
}
