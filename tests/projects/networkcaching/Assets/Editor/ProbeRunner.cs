using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `-executeMethod ProbeRunner.Create` builds the served bundles once into Content/bundles (they
/// are committed): `alpha` uncompressed, `beta.bundle` chunk-based LZ4, `gamma.unity3d` whole-file
/// LZMA, each holding one text asset, with their CRCs in crc.txt. `-executeMethod
/// ProbeRunner.Record` makes the empty probe scene and enters Play mode, where
/// <see cref="NetworkCachingProbe"/> runs when NETWORK_CACHING_OUT is set.
/// </summary>
public static class ProbeRunner
{
    static readonly (string file, string asset, BuildAssetBundleOptions options)[] Bundles =
    {
        ("alpha", "Assets/Bundled/Alpha.txt", BuildAssetBundleOptions.UncompressedAssetBundle),
        ("beta.bundle", "Assets/Bundled/Beta.txt", BuildAssetBundleOptions.ChunkBasedCompression),
        ("gamma.unity3d", "Assets/Bundled/Gamma.txt", BuildAssetBundleOptions.None),
    };

    public static void Create()
    {
        var content = Path.GetFullPath("Content/bundles");
        Directory.CreateDirectory(content);
        var crcs = new System.Text.StringBuilder();
        foreach (var (file, asset, options) in Bundles)
        {
            var temp = Path.Combine(Path.GetTempPath(), "networkcaching-" + file);
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            var build = new AssetBundleBuild { assetBundleName = file, assetNames = new[] { asset } };
            var manifest = BuildPipeline.BuildAssetBundles(temp, new[] { build },
                options | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneOSX);
            if (manifest == null) throw new System.Exception("building " + file + " failed");
            File.Copy(Path.Combine(temp, file), Path.Combine(content, file), true);
            BuildPipeline.GetCRCForAssetBundle(Path.Combine(temp, file), out uint crc);
            crcs.Append(file).Append(' ').Append(crc).Append('\n');
            Directory.Delete(temp, true);
        }
        File.WriteAllText(Path.Combine(content, "crc.txt"), crcs.ToString());
    }

    public static void Record()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }
}
