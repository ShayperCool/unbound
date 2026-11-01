using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// <c>-executeMethod PixelsAssets.Create</c> makes the texture pixel probe's assets in
/// Assets/Pixels/Resources: BGRA32 Texture2DArray and Texture3D <c>.asset</c> files (sRGB and
/// linear) whose bytes are deterministic, and an uncompressed readable imported PNG. Exits 1 on
/// failure.
/// </summary>
public static class PixelsAssets
{
    const string Folder = "Assets/Pixels/Resources";

    // Byte c of texel i of image k: every byte position runs through many values.
    static byte[] Pattern(int texels, int image)
    {
        var data = new byte[texels * 4];
        for (int i = 0; i < texels; i++)
            for (int c = 0; c < 4; c++)
                data[i * 4 + c] = (byte)((i * 37 + c * 64 + image * 101 + 3) & 255);
        return data;
    }

    public static void Create()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (bool linear in new[] { false, true })
            {
                string suffix = linear ? "Linear" : "";
                var array = new Texture2DArray(4, 4, 2, TextureFormat.BGRA32, false, linear);
                for (int e = 0; e < 2; e++) array.SetPixelData(Pattern(16, e), 0, e);
                array.filterMode = FilterMode.Point;
                array.Apply(false, false);
                AssetDatabase.CreateAsset(array, $"{Folder}/PixelsBgraArray{suffix}.asset");
                var volume = new Texture3D(4, 4, 2, linear ? GraphicsFormat.B8G8R8A8_UNorm : GraphicsFormat.B8G8R8A8_SRGB, TextureCreationFlags.None);
                volume.SetPixelData(Pattern(32, 2), 0);
                volume.filterMode = FilterMode.Point;
                volume.Apply(false, false);
                AssetDatabase.CreateAsset(volume, $"{Folder}/PixelsBgraVolume{suffix}.asset");
            }
            var source = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            source.SetPixelData(Pattern(256, 3), 0);
            source.Apply(false);
            string png = $"{Folder}/PixelsImported.png";
            File.WriteAllBytes(png, source.EncodeToPNG());
            AssetDatabase.ImportAsset(png);
            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("PixelsAssets.Create failed: " + e);
            EditorApplication.Exit(1);
        }
    }
}
