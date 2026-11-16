using System.IO;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `-executeMethod ProbeRunner.Create` makes the bundled assets once (a BundleNameData asset, a text
/// asset, a texture imported as a sprite, a text asset named beyond ASCII) and builds their bundles
/// for StandaloneLinux64 into Assets/StreamingAssets: `sbp/mixed` through the Scriptable Build Pipeline with mixed-case
/// addressable names (kept as given, as Addressables builds keep them), and `legacy/names`,
/// `nofile/names`, `noext/names` through BuildPipeline.BuildAssetBundles (names lower-cased) with no
/// option, DisableLoadAssetByFileName and DisableLoadAssetByFileNameWithExtension.
/// `-executeMethod ProbeRunner.Record` plays the probe scene, where <see cref="BundleNameProbe"/>
/// queries them.
/// </summary>
public static class ProbeRunner
{
    const string Output = "Assets/StreamingAssets";
    static readonly string[] Paths = { "Assets/Bundled/Data.asset", "Assets/Bundled/Notes.txt", "Assets/Bundled/Checker.png", "Assets/Bundled/Ünïcode.txt" };
    static readonly string[] Addresses = { "Assets/Mixed/RoomsData.asset", "Notes/MixedNotes.TXT", "Sprites/Checker.png", "Notes/Ünïcode.TXT" };

    public static void Create()
    {
        AssetDatabase.DeleteAsset("Assets/Bundled");
        AssetDatabase.DeleteAsset(Output);
        Directory.CreateDirectory("Assets/Bundled");

        var data = ScriptableObject.CreateInstance<BundleNameData>();
        data.label = "rooms";
        AssetDatabase.CreateAsset(data, Paths[0]);
        File.WriteAllText(Paths[1], "bundled notes\n");
        AssetDatabase.ImportAsset(Paths[1]);
        File.WriteAllText(Paths[3], "a name beyond ASCII\n");
        AssetDatabase.ImportAsset(Paths[3]);
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                texture.SetPixel(x, y, ((x / 2 + y / 2) % 2 == 0) ? Color.red : Color.blue);
        File.WriteAllBytes(Paths[2], texture.EncodeToPNG());
        AssetDatabase.ImportAsset(Paths[2]);
        var importer = (TextureImporter)AssetImporter.GetAtPath(Paths[2]);
        importer.textureType = TextureImporterType.Sprite;
        importer.SaveAndReimport();
        AssetDatabase.SaveAssets();

        var sbp = Output + "/sbp";
        Directory.CreateDirectory(sbp);
        var content = new BundleBuildContent(new[] { new AssetBundleBuild { assetBundleName = "mixed", assetNames = Paths, addressableNames = Addresses } });
        var parameters = new BundleBuildParameters(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, sbp) { BundleCompression = BuildCompression.LZ4 };
        var code = ContentPipeline.BuildAssetBundles(parameters, content, out IBundleBuildResults _);
        if (code < ReturnCode.Success) throw new System.Exception("the Scriptable Build Pipeline failed: " + code);
        // The build's timing log is no input of the probe.
        AssetDatabase.DeleteAsset(sbp + "/buildlogtep.json");

        foreach (var (folder, options) in new[]
        {
            ("legacy", BuildAssetBundleOptions.None),
            ("nofile", BuildAssetBundleOptions.DisableLoadAssetByFileName),
            ("noext", BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension),
        })
        {
            var output = Output + "/" + folder;
            Directory.CreateDirectory(output);
            BuildPipeline.BuildAssetBundles(output, new[] { new AssetBundleBuild { assetBundleName = "names", assetNames = Paths } },
                options | BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneLinux64);
        }
        AssetDatabase.Refresh();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
