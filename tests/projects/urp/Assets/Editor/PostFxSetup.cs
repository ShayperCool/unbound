using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>`Unity -batchmode -projectPath tests/projects/urp -executeMethod PostFxSetup.Create`:
/// authors what the post-processing scenarios load (<c>Assets/Resources/PostFxAssets.asset</c>):
/// a 32-size Color Lookup texture, imported as a LUT is (linear, no mipmaps, uncompressed,
/// clamped).</summary>
public static class PostFxSetup
{
    const string Dir = "Assets/PostFx";
    const int LutSize = 32;

    public static void Create()
    {
        Directory.CreateDirectory(Dir);
        // A strip of 32 slices (blue across slices, red across a slice, green up): a hue twist
        // that swaps some red into green and lifts the shadows' blue.
        var lut = LightingSetup.WriteTexture(Dir, "Lut", LutSize * LutSize, LutSize, (x, y) =>
        {
            float r = (x % LutSize) / (LutSize - 1f), g = y / (LutSize - 1f), b = (x / LutSize) / (LutSize - 1f);
            return new Color(0.75f * r + 0.25f * g, 0.6f * g + 0.4f * r * r, 0.15f + 0.85f * b, 1f);
        }, importer =>
        {
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        });

        var holder = ScriptableObject.CreateInstance<PostFxAssets>();
        holder.lut = lut;
        AssetDatabase.CreateAsset(holder, "Assets/Resources/PostFxAssets.asset");
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
