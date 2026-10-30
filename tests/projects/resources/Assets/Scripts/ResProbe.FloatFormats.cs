using System;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ResProbe
{
    static string FloatBits(Color color)
    {
        string bits = "";
        for (int i = 0; i < 4; i++) bits += (i == 0 ? "" : ",") + BitConverter.SingleToInt32Bits(color[i]).ToString("x8");
        return S(bits);
    }

    void FloatFormats()
    {
        var formats = new[] { TextureFormat.RHalf, TextureFormat.RGHalf, TextureFormat.RGBAHalf, TextureFormat.RFloat, TextureFormat.RGFloat, TextureFormat.RGBAFloat };
        var sample = Add(new Material(Shader.Find("Probe/MipLevels")));
        var layered = Add(new Material(Shader.Find("Probe/FloatShapes")));
        var output = Add(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        var color = new Color(1.234567f, -0.1234567f, 1000.1234f, 0.33333334f);
        foreach (var format in formats)
        {
            string key = "floatformats." + format;
            var texture = Add(new Texture2D(1, 1, format, false, true) { filterMode = FilterMode.Point });
            texture.SetPixel(0, 0, color);
            Value(key + ".2d.cpu", FloatBits(texture.GetPixel(0, 0)));
            texture.Apply(false, true);
            Graphics.Blit(texture, output, sample);
            Value(key + ".2d.gpu", FloatBits(Pixel(output)));
            texture.wrapMode = TextureWrapMode.Clamp;
            Graphics.Blit(texture, output, sample);
            Value(key + ".2d.sampler", FloatBits(Pixel(output)));
            var array = Add(new Texture2DArray(1, 1, 1, format, false, true));
            array.SetPixels(new[] { color }, 0);
            Value(key + ".array.cpu", FloatBits(array.GetPixels(0)[0]));
            array.Apply(false);
            layered.SetTexture("_Array", array);
            Graphics.Blit(null, output, layered, 0);
            Value(key + ".array.gpu", FloatBits(Pixel(output)));
            var volume = Add(new Texture3D(1, 1, 1, format, false));
            volume.SetPixels(new[] { color });
            Value(key + ".volume.cpu", FloatBits(volume.GetPixels()[0]));
            volume.Apply(false);
            layered.SetTexture("_Volume", volume);
            Graphics.Blit(null, output, layered, 1);
            Value(key + ".volume.gpu", FloatBits(Pixel(output)));
            var cube = Add(new Cubemap(1, format, false));
            foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                cube.SetPixel(face, 0, 0, color);
            Value(key + ".cube.cpu", FloatBits(cube.GetPixel(CubemapFace.PositiveX, 0, 0)));
            cube.Apply(false);
            layered.SetTexture("_Cube", cube);
            Graphics.Blit(null, output, layered, 2);
            Value(key + ".cube.gpu", FloatBits(Pixel(output)));
            var cubes = Add(new CubemapArray(1, 1, format, false, true));
            for (int face = 0; face < 6; face++) cubes.SetPixels(new[] { color }, (CubemapFace)face, 0);
            Value(key + ".cubearray.cpu", FloatBits(cubes.GetPixels(CubemapFace.PositiveX, 0)[0]));
            cubes.Apply(false);
            layered.SetTexture("_Cubes", cubes);
            Graphics.Blit(null, output, layered, 3);
            Value(key + ".cubearray.gpu", FloatBits(Pixel(output)));
        }
        foreach (var format in new[] { RenderTextureFormat.RHalf, RenderTextureFormat.RGHalf, RenderTextureFormat.ARGBHalf, RenderTextureFormat.RFloat, RenderTextureFormat.RGFloat, RenderTextureFormat.ARGBFloat })
        {
            var rt = Add(new RenderTexture(1, 1, 0, format, RenderTextureReadWrite.Linear));
            RenderTexture.active = rt;
            GL.Clear(false, true, color);
            Value("floatformats.rt." + format + ".read", FloatBits(Pixel(rt)));
            Graphics.Blit(rt, output, sample);
            Value("floatformats.rt." + format + ".sample", FloatBits(Pixel(output)));
        }
        Clear();
    }
}
