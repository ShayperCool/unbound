using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `-executeMethod ProbeRunner.Create` makes the bundled assets once (a texture, a material, a mesh,
/// a prefab with a ProbeComponent and a child collider, a ProbeData asset, a text asset, a scene)
/// and builds their bundles for StandaloneLinux64 into Reference/bundles/{lz4,lzma,none} (chunk
/// based LZ4, whole-file LZMA, uncompressed). `-executeMethod ProbeRunner.Record` with
/// <c>BUNDLE_PROBE_OUT</c> set plays the probe scene, where <see cref="BundleProbe"/> loads them.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var path in new[] { "Assets/Bundled", "Assets/BundledScenes", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        foreach (var dir in new[] { "Assets/Bundled", "Assets/BundledScenes", "Assets/Scenes" })
            Directory.CreateDirectory(dir);

        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                texture.SetPixel(x, y, ((x / 4 + y / 4) % 2 == 0) ? new Color(0.9f, 0.2f, 0.1f) : new Color(0.1f, 0.3f, 0.8f));
        File.WriteAllBytes("Assets/Bundled/Checker.png", texture.EncodeToPNG());
        AssetDatabase.ImportAsset("Assets/Bundled/Checker.png");
        var checker = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Bundled/Checker.png");

        var material = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.8f, 0.6f), mainTexture = checker };
        material.SetFloat("_Glossiness", 0.3f);
        AssetDatabase.CreateAsset(material, "Assets/Bundled/Thing.mat");

        var mesh = new Mesh { name = "Wedge" };
        mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1), new Vector3(1, 1, 1) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 4, 1, 4, 3, 2, 3, 4 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, "Assets/Bundled/Wedge.asset");

        var data = ScriptableObject.CreateInstance<ProbeData>();
        AssetDatabase.CreateAsset(data, "Assets/Bundled/Data.asset");
        File.WriteAllText("Assets/Bundled/Notes.txt", "bundled text\nsecond line\n");
        AssetDatabase.ImportAsset("Assets/Bundled/Notes.txt");

        var root = new GameObject("Thing");
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        var child = new GameObject("Child");
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = new Vector3(0.5f, 1.5f, -0.25f);
        child.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
        child.AddComponent<BoxCollider>().size = new Vector3(0.5f, 2f, 1f);
        var probe = root.AddComponent<ProbeComponent>();
        probe.data = data;
        probe.material = material;
        probe.child = child;
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Bundled/Thing.prefab");
        Object.DestroyImmediate(root);

        foreach (var path in new[] { "Assets/Bundled/Checker.png", "Assets/Bundled/Thing.mat", "Assets/Bundled/Wedge.asset", "Assets/Bundled/Data.asset", "Assets/Bundled/Notes.txt", "Assets/Bundled/Thing.prefab" })
            AssetImporter.GetAtPath(path).assetBundleName = "things";

        var bundled = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "SceneCube";
        cube.transform.position = new Vector3(2f, 0f, 0f);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bundled/Thing.prefab"));
        instance.transform.position = new Vector3(-2f, 0.5f, 0f);
        instance.GetComponent<ProbeComponent>().count = 42;
        EditorSceneManager.SaveScene(bundled, "Assets/BundledScenes/BundleScene.unity");
        AssetImporter.GetAtPath("Assets/BundledScenes/BundleScene.unity").assetBundleName = "scene";

        var probeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(probeScene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();

        foreach (var (folder, options) in new[]
        {
            ("lz4", BuildAssetBundleOptions.ChunkBasedCompression),
            ("lzma", BuildAssetBundleOptions.None),
            ("none", BuildAssetBundleOptions.UncompressedAssetBundle),
        })
        {
            var output = "Reference/bundles/" + folder;
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            BuildPipeline.BuildAssetBundles(output, options | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneLinux64);
        }
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
