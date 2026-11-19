using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// Records what the probe's postprocessors did to the imported assets, in Play mode (see
/// README.md). <see cref="Record"/> (the Editor's -executeMethod) first runs Editor-only steps
/// that change a postprocessor and let the Editor import again, then enters Play mode.
/// </summary>
[InitializeOnLoad]
public static class PostprocessorProbe
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Timeline = new List<string>();
    static int s_Frames;
    const string StepKey = "assetpostprocessors.step";
    const string VersionedFile = "Assets/Editor/PPVersioned.cs";

    static readonly string[] Textures = { "Assets/Tex/Plain.png", "Assets/Tex/Shrink.png", "Assets/Tex/Invert.png", "Assets/Tex/Sprite.png" };
    static readonly string[] Models = { "Assets/Models/Materials.fbx", "Assets/Models/Animated.fbx", "Assets/Models/UserProps.fbx" };
    static readonly string[] Prefabs = { "Assets/Prefabs/Box.prefab", "Assets/Prefabs/Nest.prefab", "Assets/Prefabs/BoxVariant.prefab" };
    static readonly string[] Clips = { "Assets/Audio/Tone.wav", "Assets/Audio/Tone8.wav", "Assets/Audio/Tone24.wav", "Assets/Audio/ToneFloat.wav" };
    /// <summary>The maps of the SpeedTree 9 tree Calm.st9, whose import sets their settings.</summary>
    static readonly string[] TreeTextures =
        { "Assets/Trees/Bark_Color.png", "Assets/Trees/Bark_Normal.png", "Assets/Trees/Leaf_Color.png", "Assets/Trees/Leaf_Extra.png", "Assets/Trees/Billboard_Color.png" };

    static PostprocessorProbe()
    {
        if (string.IsNullOrEmpty(PPLog.Out)) return;
        Timeline("InitializeOnLoad");
        AssemblyReloadEvents.afterAssemblyReload += () => Timeline("afterAssemblyReload");
        EditorApplication.playModeStateChanged += state =>
        {
            Timeline("playModeStateChanged " + state);
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.update += WaitThenRun;
        };
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetInt(StepKey, 0) > 0)
            EditorApplication.delayCall += Next;
    }

    [InitializeOnLoadMethod]
    static void Loaded() { if (!string.IsNullOrEmpty(PPLog.Out)) Timeline("InitializeOnLoadMethod"); }

    [DidReloadScripts]
    static void Reloaded() { if (!string.IsNullOrEmpty(PPLog.Out)) Timeline("DidReloadScripts"); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeScene() { if (!string.IsNullOrEmpty(PPLog.Out)) Timeline("BeforeSceneLoad"); }

    internal static void Timeline(string entry)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) s_Timeline.Add(entry);
    }

    internal static string AllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var ours = imported.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal);
        return "imported=" + imported.Length + " assets=[" + string.Join(",", ours) + "] deleted=" + deleted.Length
            + " moved=" + moved.Length + " movedFrom=" + movedFrom.Length;
    }

    // ------------------------------------------------------------ Editor-only steps (not compared)

    /// <summary>The Editor's entry: opens the probe's scene, makes three changes of the
    /// postprocessors, each followed by the import they cause (events-version.txt,
    /// events-code.txt, events-added.txt), then enters Play mode.</summary>
    public static void Record()
    {
        // Play mode plays the probe's scene (a batch-mode Editor opens none).
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        SessionState.SetInt(StepKey, 1);
        Next();
    }

    static void Next()
    {
        int step = SessionState.GetInt(StepKey, 0);
        SessionState.SetInt(StepKey, step + 1);
        switch (step)
        {
            case 1:
                Change("version", Versioned(2, "v1"));
                break;
            case 2:
                Change("code", Versioned(2, "v2 changed"));
                break;
            case 3:
                File.WriteAllText(Path.Combine(PPLog.Out, "phase.txt"), "added");
                File.WriteAllText("Assets/Editor/PPAdded.cs",
                    "using UnityEditor;\nusing UnityEngine;\nclass PPAdded : AssetPostprocessor\n{\n" +
                    "    void OnPreprocessAudio() => PPLog.Event(this, \"OnPreprocessAudio\", assetPath, \"\");\n" +
                    "    void OnPostprocessTexture(Texture2D t) => PPLog.Event(this, \"OnPostprocessTexture\", assetPath, \"\");\n}\n");
                Refresh();
                break;
            default:
                File.WriteAllText(Path.Combine(PPLog.Out, "phase.txt"), "play");
                SessionState.EraseInt(StepKey);
                EditorApplication.EnterPlaymode();
                break;
        }
    }

    static string Versioned(uint version, string detail) =>
        "using UnityEditor;\nclass PPVersioned : AssetPostprocessor\n{\n" +
        "    public override uint GetVersion() => " + version + ";\n" +
        "    void OnPreprocessTexture() => PPLog.Event(this, \"OnPreprocessTexture\", assetPath, \"" + detail + "\");\n}\n";

    static void Change(string phase, string source)
    {
        File.WriteAllText(Path.Combine(PPLog.Out, "phase.txt"), phase);
        File.WriteAllText(VersionedFile, source);
        Refresh();
    }

    /// <summary>Imports the change; the domain reload then continues with the next step. Without a
    /// reload within 20 s the next step runs anyway.</summary>
    static void Refresh()
    {
        AssetDatabase.Refresh();
        var deadline = EditorApplication.timeSinceStartup + 20;
        int step = SessionState.GetInt(StepKey, 0);
        void Watch()
        {
            if (EditorApplication.timeSinceStartup < deadline || EditorApplication.isCompiling) return;
            EditorApplication.update -= Watch;
            if (SessionState.GetInt(StepKey, 0) == step)
            {
                File.AppendAllText(Path.Combine(PPLog.Out, "events-" + PPLog.Phase + ".txt"), "no reload\n");
                Next();
            }
        }
        EditorApplication.update += Watch;
    }

    // ------------------------------------------------------------ Play mode: the compared values

    static void WaitThenRun()
    {
        if (++s_Frames < 10) return;
        EditorApplication.update -= WaitThenRun;
        try { Run(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message); }
        Put("complete", true);
        File.WriteAllText(Path.Combine(PPLog.Out, "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V3(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";

    static void Put(string key, object value)
    {
        var text = value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            float f => F(f),
            Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        if (value is bool) { s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(text).Append("}\n"); return; }
        s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"")
            .Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\"}\n");
    }

    static readonly Regex s_PlatformEntry = new Regex("(?m)^( *)- serializedVersion: [0-9]+\n\\1  buildTarget: (?!DefaultTexturePlatform\\b|Standalone\\b)(\\S+)\n(?:\\1  .*\n)*");

    /// <summary>A .meta file's digest without the per-platform entries of the installation's
    /// platform modules, which a texture's .meta written by the Editor lists as the installation
    /// has them; whether it lists none or exactly those is recorded apart (`.platforms`; the
    /// committed .meta files list none).</summary>
    static void RecordMeta(string path)
    {
        var text = File.ReadAllText(path + ".meta");
        var listed = new List<string>();
        text = s_PlatformEntry.Replace(text, m => { listed.Add(m.Groups[2].Value); return ""; });
        Put("meta." + path, Hash(Encoding.UTF8.GetBytes(text)));
        if (text.Contains("platformSettings:"))
            Put("meta." + path + ".platforms", listed.Count == 0 ? "none" : listed.SequenceEqual(InstalledPlatforms()) ? "the installed modules" : "differ: " + string.Join(",", listed));
    }

    /// <summary>The platform modules of this Editor installation, in the build settings' order,
    /// by the names texture settings give them.</summary>
    static IEnumerable<string> InstalledPlatforms()
    {
        var platforms = new (BuildTargetGroup group, BuildTarget target, string name)[]
        {
            (BuildTargetGroup.Android, BuildTarget.Android, "Android"), (BuildTargetGroup.iOS, BuildTarget.iOS, "iOS"),
            (BuildTargetGroup.WebGL, BuildTarget.WebGL, "WebGL"), (BuildTargetGroup.WSA, BuildTarget.WSAPlayer, "Windows Store Apps"),
            (BuildTargetGroup.tvOS, BuildTarget.tvOS, "tvOS"), (BuildTargetGroup.VisionOS, BuildTarget.VisionOS, "VisionOS"),
        };
        return platforms.Where(p => BuildPipeline.IsBuildTargetSupported(p.group, p.target)).Select(p => p.name);
    }

    static string Hash(IEnumerable<byte> bytes)
    {
        ulong h = 14695981039346656037UL;
        foreach (var b in bytes) { h ^= b; h *= 1099511628211UL; }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    static void Guard(string key, Action action)
    {
        try { action(); }
        catch (Exception e) { Put(key + ".threw", e.GetType().FullName + ": " + e.Message); }
    }

    static void Run()
    {
        // Load every asset first: Unbound runs an asset's postprocessors when it imports it (the
        // tree too, whose import sets its maps' settings).
        AssetDatabase.LoadMainAssetAtPath("Assets/Trees/Calm.st9");
        foreach (var path in Textures.Concat(Models).Concat(Prefabs).Concat(Clips))
            Guard("assets." + path, () =>
            {
                var objects = AssetDatabase.LoadAllAssetsAtPath(path)
                    .Select(o => o.GetType().Name + ":" + o.name).OrderBy(s => s, StringComparer.Ordinal);
                Put("assets." + path, string.Join(",", objects));
            });
        // The scene's model and prefab instances, before the probe instantiates anything (scenes instantiate the imported prefabs).
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().OrderBy(g => g.name, StringComparer.Ordinal))
            Guard("scene." + root.name, () => RecordHierarchy("scene.", root.transform));
        foreach (var path in Textures) Guard("texture." + path, () => RecordTexture(path));
        foreach (var path in Models) Guard("model." + path, () => RecordModel("model.", path));
        foreach (var path in Prefabs) Guard("prefab." + path, () => RecordModel("prefab.", path));
        foreach (var path in Models.Concat(Prefabs)) Guard("fileid." + path, () => RecordAddedIds(path));
        foreach (var path in Clips) Guard("clip." + path, () => RecordClip(path));
        foreach (var path in TreeTextures)
        {
            Guard("rule." + path, () => Put("rule." + path, TreeRule((TextureImporter)AssetImporter.GetAtPath(path))));
            Guard("texture." + path, () => RecordTexture(path));
        }
        foreach (var path in Textures.Concat(Models).Concat(Clips))
        {
            Guard("importer." + path, () => Put("importer." + path, PPLog.Settings(AssetImporter.GetAtPath(path))));
            Guard("meta." + path, () => RecordMeta(path));
        }
        RecordEvents();
        Put("timeline", string.Join(" | ", s_Timeline));
    }

    static void RecordTexture(string path)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        string key = "texture." + path;
        Put(key, PPLog.Texture(t) + " graphicsFormat=" + (int)t.graphicsFormat);
        if (t.isReadable)
            for (int m = 0; m < t.mipmapCount; m++)
            {
                var pixels = t.GetPixels32(m);
                Put(key + ".mip" + m, Hash(pixels.SelectMany(c => new[] { c.r, c.g, c.b, c.a })) + " first=" + pixels[0]);
            }
        foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal))
            Put(key + ".sprite." + sprite.name, "ppu=" + F(sprite.pixelsPerUnit) + " rect=" + sprite.rect + " bounds=" + V3(sprite.bounds.size));
    }

    /// <summary>The colour-map, normal-map and sRGB settings a tree's import gives its maps.</summary>
    static string TreeRule(TextureImporter t) =>
        "type=" + (int)t.textureType + " srgb=" + (t.sRGBTexture ? 1 : 0) + " alphaIsTransparency=" + (t.alphaIsTransparency ? 1 : 0)
        + " preserveCoverage=" + (t.mipMapsPreserveCoverage ? 1 : 0) + " alphaTestReference=" + F(t.alphaTestReferenceValue)
        + " readable=" + (t.isReadable ? 1 : 0) + " mips=" + (t.mipmapEnabled ? 1 : 0) + " compression=" + (int)t.textureCompression;

    /// <summary>The local file ids of the objects postprocessors added to an import (GameObjects
    /// named Added or BareRenamed and their components, sphere and capsule colliders).</summary>
    static void RecordAddedIds(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            var go = o as GameObject ?? (o as Component)?.gameObject;
            if (go == null) continue;
            if (!(o is SphereCollider || o is CapsuleCollider || go.name == "Added" || go.name == "BareRenamed")) continue;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long id)) id = 0;
            Put("fileid." + path + "." + NodePath(go.transform) + "." + o.GetType().Name, id);
        }
    }

    static void RecordModel(string prefix, string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var instance = UnityEngine.Object.Instantiate(asset);
        instance.name = asset.name;
        RecordHierarchy(prefix + path + ".", instance.transform);
        if (prefix == "model.") RecordModelClips(path);
    }

    static void RecordHierarchy(string prefix, Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string key = prefix + NodePath(t);
            var parts = new List<string>
            {
                "pos=" + V3(t.localPosition) + " scale=" + V3(t.localScale),
                "components=" + string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name)),
            };
            if (t.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                var mesh = filter.sharedMesh;
                parts.Add("mesh=" + mesh.name + " vertices=" + mesh.vertexCount + " bounds=" + V3(mesh.bounds.size) + " readable=" + mesh.isReadable);
            }
            if (t.TryGetComponent(out Renderer renderer))
                parts.Add("materials=" + string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "null" : m.name + ":" + m.color)));
            if (t.TryGetComponent(out MeshCollider meshCollider))
                parts.Add("meshCollider=" + (meshCollider.sharedMesh != null ? meshCollider.sharedMesh.name : "null"));
            if (t.TryGetComponent(out BoxCollider box))
                parts.Add("box=" + V3(box.center) + " " + V3(box.size));
            if (t.TryGetComponent(out SphereCollider sphere))
                parts.Add("sphere=" + F(sphere.radius));
            if (t.TryGetComponent(out CapsuleCollider capsule))
                parts.Add("capsule=" + F(capsule.radius) + "," + F(capsule.height));
            if (t.gameObject.layer != 0)
                parts.Add("layer=" + t.gameObject.layer);
            Put(key, string.Join(" ", parts));
            // The children's order (a model's hierarchy is sorted by name on import).
            if (t.childCount > 0)
                Put(key + ".children", string.Join(",", t.Cast<Transform>().Select(c => c.name)));
        }
    }

    static void RecordModelClips(string path)
    {
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
            Put("model." + path + ".clip." + clip.name, "length=" + F(clip.length) + " events=" + clip.events.Length);
    }

    static string NodePath(Transform t) => t.parent == null ? t.name : NodePath(t.parent) + "/" + t.name;

    static void RecordClip(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        Put("clip." + path, PPLog.Clip(clip));
        if (clip.loadType == AudioClipLoadType.DecompressOnLoad)
        {
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            Put("clip." + path + ".data", Hash(data.SelectMany(BitConverter.GetBytes)) + " first=" + F(data[0]) + "," + F(data[1]));
            // The unconverted clips' first samples, as multiples of 2^-15 (16-bit storage shows whole numbers).
            if (path != "Assets/Audio/Tone.wav")
                Put("clip." + path + ".head", string.Join(",", data.Take(24).Select(v => F(v * 32768f))));
        }
    }

    /// <summary>The asset callbacks, per asset in calling order, from every import of the asset
    /// in the opened project or in Play mode (not the Editor-only steps), and the
    /// OnPostprocessAllAssets calls made in Play mode.</summary>
    static void RecordEvents()
    {
        var lines = new List<string[]>();
        foreach (var phase in new[] { "import", "play" })
        {
            var file = Path.Combine(PPLog.Out, "events-" + phase + ".txt");
            if (File.Exists(file))
                lines.AddRange(File.ReadAllLines(file).Select(l => l.Split('\t')).Where(p => p.Length == 5));
        }
        var perAsset = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var all = new List<string>();
        foreach (var p in lines)
        {
            if (p[1] == "OnPostprocessAllAssets")
            {
                if (p[3] == "play") all.Add(p[0] + " " + p[4]);
                continue;
            }
            if (!p[2].StartsWith("Assets/", StringComparison.Ordinal)) continue;
            if (!perAsset.TryGetValue(p[2], out var list)) perAsset[p[2]] = list = new List<string>();
            list.Add(p[0] + "." + p[1] + (p[4].Length > 0 ? " " + p[4] : ""));
        }
        foreach (var (path, list) in perAsset)
        {
            Put("events." + path + ".count", list.Count);
            for (int i = 0; i < list.Count; i++) Put("events." + path + "." + i.ToString("D2", CultureInfo.InvariantCulture), list[i]);
        }
        Put("allAssets.count", all.Count);
        for (int i = 0; i < all.Count; i++) Put("allAssets." + i, all[i]);
    }
}
