using UnityEngine;
using UnityEngine.Rendering;

public partial class ResProbe
{
    void RenderMipSamples(string name, RenderTexture texture, Material sample, RenderTexture pixel)
    {
        Value(name + ".count", I(texture.mipmapCount));
        for (int mip = 0; mip < texture.mipmapCount; mip++)
        {
            sample.SetFloat("_Level", mip);
            Graphics.Blit(texture, pixel, sample);
            Value(name + ".mip" + mip, C(Pixel(pixel)));
        }
    }

    void LayeredRenderMips(RenderTexture pixel)
    {
        var sample = Add(new Material(Shader.Find("Probe/MipLayers")));
        foreach (bool cube in new[] { false, true })
            foreach (bool automatic in new[] { false, true })
            {
                int layers = cube ? 6 : 3;
                var rt = Add(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    dimension = cube ? TextureDimension.Cube : TextureDimension.Tex2DArray,
                    volumeDepth = cube ? 1 : layers,
                    useMipMap = true,
                    autoGenerateMips = automatic,
                });
                string name = "rtmips.layers." + B(cube) + "." + B(automatic);
                for (int layer = 0; layer < layers; layer++)
                {
                    Graphics.SetRenderTarget(rt, 0, cube ? (CubemapFace)layer : CubemapFace.Unknown, cube ? 0 : layer);
                    GL.Clear(false, true, new Color((layer + 1) * 0.1f, 0.2f, 0.4f, 1f));
                }
                RenderTexture.active = null;
                if (!automatic) rt.GenerateMips();
                sample.SetTexture(cube ? "_Cube" : "_Array", rt);
                var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                Value(name + ".count", I(rt.mipmapCount));
                for (int layer = 0; layer < layers; layer++)
                    for (int mip = 0; mip < rt.mipmapCount; mip++)
                    {
                        sample.SetFloat("_Level", mip);
                        sample.SetFloat("_Layer", layer);
                        if (cube) sample.SetVector("_Direction", directions[layer]);
                        Graphics.Blit(null, pixel, sample, cube ? 0 : 1);
                        Value(name + ".layer" + layer + ".mip" + mip, C(Pixel(pixel)));
                    }
            }
    }

    void RenderMipmaps()
    {
        var sample = Add(new Material(Shader.Find("Probe/MipLevels")));
        var pixel = Add(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGBFloat, RenderTextureFormat.RFloat })
        {
            var rt = Add(new RenderTexture(8, 4, 0, format, RenderTextureReadWrite.Linear) { useMipMap = true, autoGenerateMips = false });
            string name = "rtmips." + format;
            var active = RenderTexture.active;
            for (int mip = 0; mip < rt.mipmapCount; mip++)
            {
                Graphics.SetRenderTarget(rt, mip);
                GL.Clear(false, true, new Color(0.2f + mip * 0.1f, 0.4f, 0.6f - mip * 0.1f, 1f));
            }
            RenderTexture.active = active;
            RenderMipSamples(name + ".manual", rt, sample, pixel);
            rt.GenerateMips();
            RenderMipSamples(name + ".generated", rt, sample, pixel);
            var source = Resources.Load<Texture2D>("MipImport");
            var automatic = Add(new RenderTexture(8, 4, 0, format, RenderTextureReadWrite.Linear) { useMipMap = true, autoGenerateMips = true });
            Graphics.Blit(source, automatic);
            RenderMipSamples(name + ".automatic", automatic, sample, pixel);
            var commands = new CommandBuffer { name = "generate mips at execution" };
            commands.SetRenderTarget(rt, 0);
            commands.ClearRenderTarget(false, true, new Color(0.7f, 0.2f, 0.4f, 1f));
            commands.GenerateMips(rt);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();
            RenderTexture.active = active;
            RenderMipSamples(name + ".command", rt, sample, pixel);
        }
        var msaa = Add(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 4 });
        var resolved = Add(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        Graphics.Blit(Resources.Load<Texture2D>("MipImport"), msaa);
        msaa.ResolveAntiAliasedSurface(resolved);
        Value("rtmips.resolve.creates_target", B(resolved.IsCreated()));
        resolved.Create();
        msaa.ResolveAntiAliasedSurface(resolved);
        RenderMipSamples("rtmips.resolve", resolved, sample, pixel);
        var beforeBlit = Add(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { useMipMap = true, autoGenerateMips = false });
        var blitTarget = Add(new RenderTexture(2, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        Graphics.SetRenderTarget(beforeBlit, 2);
        GL.Clear(false, true, Color.red);
        Graphics.Blit(Resources.Load<Texture2D>("MipImport"), blitTarget);
        Value("rtmips.blit.active_is_destination", B(RenderTexture.active == blitTarget));
        GL.Clear(false, true, Color.blue);
        sample.SetFloat("_Level", 2);
        Graphics.Blit(beforeBlit, pixel, sample);
        Value("rtmips.blit.previous_mip", C(Pixel(pixel)));
        Value("rtmips.blit.destination", C(Pixel(blitTarget)));
        LayeredRenderMips(pixel);
        Clear();
    }
}
