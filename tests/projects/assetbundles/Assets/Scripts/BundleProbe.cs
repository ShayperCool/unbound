using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads the bundles ProbeRunner.Create built (Reference/bundles/{lz4,lzma,none}) and records what
/// they hold: asset names, the manifest, the prefab instantiated (hierarchy, components, fields,
/// mesh, material, texture), the ScriptableObject, the text, everything LoadAllAssets gives, the
/// scene bundle's scene, the asynchronous and in-memory loads, Unload; and an image of the prefab.
/// Writes <c>bundles.jsonl</c> and <c>thing.png</c> into <c>BUNDLE_PROBE_OUT</c>, then quits.
/// </summary>
public class BundleProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("BundleProbe").AddComponent<BundleProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";
    static string C(Color c) => $"[{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}]";
    static string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("BUNDLE_PROBE_OUT") ?? "/tmp/bundles";
        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Reference", "bundles"));

        foreach (var variant in new[] { "lz4", "lzma", "none" })
        {
            var things = AssetBundle.LoadFromFile(Path.Combine(root, variant, "things"));
            Record("bundle", $"\"variant\":{S(variant)},\"loaded\":{(things != null ? "true" : "false")},\"name\":{S(things?.name)},\"streamedScenes\":{(things != null && things.isStreamedSceneAssetBundle ? "true" : "false")},\"assets\":[{string.Join(",", (things?.GetAllAssetNames() ?? new string[0]).OrderBy(n => n).Select(S))}]");
            if (things == null) continue;
            var data = things.LoadAsset<ProbeData>("assets/bundled/data.asset");
            Record("data", $"\"variant\":{S(variant)},\"name\":{S(data?.name)},\"level\":{data?.level},\"title\":{S(data?.title)},\"range\":[{F(data.range.x)},{F(data.range.y)}],\"tags\":[{string.Join(",", data.tags.Select(S))}]");
            if (variant == "lz4") RecordPrefab(things, dir);
            things.Unload(true);
        }

        // The manifest the build wrote: bundles and dependencies.
        var manifestBundle = AssetBundle.LoadFromFile(Path.Combine(root, "lz4", "lz4"));
        var manifest = manifestBundle != null ? manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest") : null;
        if (manifest != null)
            Record("manifest", $"\"bundles\":[{string.Join(",", manifest.GetAllAssetBundles().OrderBy(n => n).Select(S))}],\"sceneDependencies\":[{string.Join(",", manifest.GetAllDependencies("scene").Select(S))}],\"direct\":[{string.Join(",", manifest.GetDirectDependencies("scene").Select(S))}]");
        manifestBundle?.Unload(true);

        // A scene from its bundle, with the things bundle its prefab instance needs.
        var thingsForScene = AssetBundle.LoadFromFile(Path.Combine(root, "lz4", "things"));
        var sceneBundle = AssetBundle.LoadFromFile(Path.Combine(root, "lz4", "scene"));
        Record("scene_bundle", $"\"streamedScenes\":{(sceneBundle.isStreamedSceneAssetBundle ? "true" : "false")},\"scenes\":[{string.Join(",", sceneBundle.GetAllScenePaths().Select(S))}]");
        SceneManager.LoadScene("BundleScene", LoadSceneMode.Additive);
        yield return null;
        var scene = SceneManager.GetSceneByName("BundleScene");
        foreach (var go in scene.GetRootGameObjects().OrderBy(g => g.name))
        {
            var probe = go.GetComponent<ProbeComponent>();
            Record("scene_object", $"\"name\":{S(go.name)},\"position\":{V(go.transform.position)},\"components\":[{string.Join(",", go.GetComponents<Component>().Select(c => S(c.GetType().Name)))}]" + (probe != null ? $",\"count\":{probe.count},\"label\":{S(probe.label)}" : ""));
        }
        yield return SceneManager.UnloadSceneAsync(scene);
        sceneBundle.Unload(true);
        thingsForScene.Unload(true);

        // Asynchronous loads.
        var request = AssetBundle.LoadFromFileAsync(Path.Combine(root, "lzma", "things"));
        yield return request;
        var asyncBundle = request.assetBundle;
        var assetRequest = asyncBundle.LoadAssetAsync<TextAsset>("assets/bundled/notes.txt");
        yield return assetRequest;
        Record("async", $"\"text\":{S((assetRequest.asset as TextAsset)?.text)}");
        var all = asyncBundle.LoadAllAssets();
        Record("all", $"\"assets\":[{string.Join(",", all.Select(a => S(a.GetType().Name + ":" + a.name)).OrderBy(n => n))}]");
        asyncBundle.Unload(true);

        // From memory, then unloaded with its objects.
        var bytes = File.ReadAllBytes(Path.Combine(root, "none", "things"));
        var memory = AssetBundle.LoadFromMemory(bytes);
        var material = memory.LoadAsset<Material>("assets/bundled/thing.mat");
        Record("memory", $"\"material\":{S(material?.name)},\"loadedBundles\":{AssetBundle.GetAllLoadedAssetBundles().Count()}");
        memory.Unload(true);
        Record("unloaded", $"\"materialAlive\":{(material != null ? "true" : "false")},\"loadedBundles\":{AssetBundle.GetAllLoadedAssetBundles().Count()}");

        File.WriteAllText(Path.Combine(dir, "bundles.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void RecordPrefab(AssetBundle things, string dir)
    {
        var prefab = things.LoadAsset<GameObject>("assets/bundled/thing.prefab");
        var instance = Instantiate(prefab);
        foreach (var t in instance.GetComponentsInChildren<Transform>())
            Record("node", $"\"name\":{S(t.name)},\"parent\":{S(t.parent != null ? t.parent.name : "")},\"position\":{V(t.localPosition)},\"rotation\":{Q(t.localRotation)},\"scale\":{V(t.localScale)},\"components\":[{string.Join(",", t.GetComponents<Component>().Select(c => S(c.GetType().Name)))}]");
        var probe = instance.GetComponent<ProbeComponent>();
        Record("component", $"\"count\":{probe.count},\"label\":{S(probe.label)},\"weights\":[{string.Join(",", probe.weights.Select(F))}],\"points\":[{string.Join(",", probe.points.Select(V))}],\"data\":{S(probe.data?.title)},\"nested\":{S(probe.nested.name)},\"tint\":{C(probe.nested.tint)},\"curve\":{F(probe.nested.curve.Evaluate(0.5f))},\"material\":{S(probe.material?.name)},\"childIsInstance\":{(probe.child != null && probe.child.transform.parent == instance.transform ? "true" : "false")}");
        var mesh = instance.GetComponent<MeshFilter>().sharedMesh;
        Record("mesh", $"\"name\":{S(mesh.name)},\"vertices\":{mesh.vertexCount},\"triangles\":[{string.Join(",", mesh.triangles)}],\"bounds\":[{V(mesh.bounds.center)},{V(mesh.bounds.size)}],\"first\":{V(mesh.vertices[1])},\"normal\":{V(mesh.normals[4])},\"uv\":[{F(mesh.uv[4].x)},{F(mesh.uv[4].y)}],\"readable\":{(mesh.isReadable ? "true" : "false")}");
        var renderer = instance.GetComponent<MeshRenderer>();
        var material = renderer.sharedMaterial;
        var texture = material.mainTexture as Texture2D;
        Record("material", $"\"name\":{S(material.name)},\"shader\":{S(material.shader.name)},\"color\":{C(material.color)},\"glossiness\":{F(material.GetFloat("_Glossiness"))},\"texture\":{S(texture?.name)},\"size\":[{texture?.width},{texture?.height}],\"format\":{S(texture?.format.ToString())},\"readable\":{(texture != null && texture.isReadable ? "true" : "false")},\"mips\":{texture?.mipmapCount}");
        var box = instance.GetComponentInChildren<BoxCollider>();
        Record("collider", $"\"size\":{V(box.size)},\"center\":{V(box.center)}");

        var cameraObject = new GameObject("Camera");
        cameraObject.transform.position = new Vector3(1.5f, 1.8f, -2.5f);
        cameraObject.transform.LookAt(new Vector3(0.4f, 0.4f, 0.4f));
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
        var light = new GameObject("Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        var target = new RenderTexture(200, 150, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(200, 150, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 200, 150), 0, 0);
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, "thing.png"), image.EncodeToPNG());
        Destroy(cameraObject);
        Destroy(light.gameObject);
        Destroy(instance);
    }
}
