using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>`Unity -batchmode -projectPath tests/projects/urp -executeMethod PostFxSetup.Create`:
/// authors what the post-processing scenarios load (<c>Assets/Resources/PostFxAssets.asset</c>):
/// a 32-size Color Lookup texture, imported as a LUT is (linear, no mipmaps, uncompressed,
/// clamped), and the Single Channel controls of texture-samples.jsonl
/// (<see cref="CreateSingleChannelControls"/>).</summary>
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
        AddSingleChannelControls(holder);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    /// <summary>`-executeMethod PostFxSetup.CreateSingleChannelControls`: adds the Single Channel
    /// controls to the existing PostFxAssets.asset (Create adds them too).</summary>
    public static void CreateSingleChannelControls()
    {
        var holder = AssetDatabase.LoadAssetAtPath<PostFxAssets>("Assets/Resources/PostFxAssets.asset");
        AddSingleChannelControls(holder);
        EditorUtility.SetDirty(holder);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    /// <summary>Single Channel textures under the import's default compression (Alpha and Red),
    /// and a Red one marked sRGB. The compressed ones hold only 0 and 1 in their channel and
    /// pure colors elsewhere, which every block format keeps exactly: their samples show which
    /// channels the GPU format carries, not compression error.</summary>
    static void AddSingleChannelControls(PostFxAssets holder)
    {
        System.Func<int, int, float> bit = (x, y) => (x * 3 + y) % 4 < 2 ? 1f : 0f;
        holder.singleAlphaCompressed = LightingSetup.WriteTexture(Dir, "SingleAlphaCompressed", 32, 32,
            (x, y) => new Color(1f, 0f, 1f, bit(x, y)), importer =>
            {
                importer.textureType = TextureImporterType.SingleChannel;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.textureCompression = TextureImporterCompression.Compressed;
                FeatureSetup.SetSingleChannel(importer, 0);
            });
        holder.singleRedCompressed = LightingSetup.WriteTexture(Dir, "SingleRedCompressed", 32, 32,
            (x, y) => new Color(bit(x, y), 0f, 1f, 1f - bit(x, y)), importer =>
            {
                importer.textureType = TextureImporterType.SingleChannel;
                importer.textureCompression = TextureImporterCompression.Compressed;
                FeatureSetup.SetSingleChannel(importer, 1);
            });
        holder.singleRedSrgb = LightingSetup.WriteTexture(Dir, "SingleRedSrgb", 32, 32,
            (x, y) => new Color(x / 31f, y / 31f, 0.5f, ((x + y) % 7) / 6f), importer =>
            {
                importer.textureType = TextureImporterType.SingleChannel;
                importer.sRGBTexture = true;
                FeatureSetup.SetSingleChannel(importer, 1);
            });
    }
}
