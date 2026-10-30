using System;
using System.IO;
using UnityEngine;

public partial class ResProbe
{
    void BundleMipmaps()
    {
        var bundle = AssetBundle.LoadFromFile(Path.Combine(Application.dataPath, "..", "Reference", "mip-bundles", "mips"));
        var sample = Add(new Material(Shader.Find("Probe/MipLevels")));
        var pixel = Add(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        foreach (string name in new[] { "MipDXT1", "MipDXT5" })
        {
            var texture = bundle.LoadAsset<Texture2D>(name);
            string key = "bundlemips." + name;
            Value(key + ".format", S(texture.format.ToString()));
            Value(key + ".graphics_format", S(texture.graphicsFormat.ToString()));
            Value(key + ".count", I(texture.mipmapCount));
            Value(key + ".readable", B(texture.isReadable));
            Try(key + ".raw", () => S(Convert.ToBase64String(texture.GetRawTextureData())));
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int level = mip;
                Try(key + ".cpu" + mip, () => C(texture.GetPixels(level)[0]));
                sample.SetFloat("_Level", mip);
                Graphics.Blit(texture, pixel, sample);
                Value(key + ".gpu" + mip, C(Pixel(pixel)));
            }
        }
        bundle.Unload(true);
        Clear();
    }
}
