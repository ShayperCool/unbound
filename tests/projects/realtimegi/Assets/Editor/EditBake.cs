using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Edit-mode script bake (`script_bake.py native --edit`, <c>REALTIME_GI_EDIT_BAKE_OUT</c>
/// set): <see cref="Record"/> opens <c>RoomScriptBake</c> (no lighting data, no bake on load) in
/// Edit mode and bakes it twice with <see cref="Lightmapping.Bake"/>, recording each answer, the
/// Lightmapping events it sends (with isRunning at each; apart, whether the scene's lighting
/// data is on disk and whether the scene names one at each), and then the scene's lighting data, whether the
/// scene is dirty, what the bake wrote into the project (files, the scene file's content) and
/// the renderers' realtime lightmap indices; then it enters Play mode, where
/// <see cref="EditBakeProbe"/> records what is drawn. The Editor runs it with
/// <c>-executeMethod EditBake.Record</c>; with <c>REALTIME_GI_EDIT_BAKE_AUTO</c> set (Unbound's
/// <c>unbound test</c>) it starts itself once Edit mode has run a few updates.
/// </summary>
[InitializeOnLoad]
public static class EditBake
{
    const string Scene = "Assets/Scenes/RoomScriptBake.unity";
    const string Folder = "Assets/Scenes/RoomScriptBake";

    static readonly List<string> s_Lines = new List<string>();

    static EditBake()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REALTIME_GI_EDIT_BAKE_OUT"))) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REALTIME_GI_EDIT_BAKE_AUTO"))) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int updates = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++updates < 30) return;
            EditorApplication.update -= wait;
            Record();
        };
        EditorApplication.update += wait;
    }

    static void Write(string key, object value) =>
        s_Lines.Add("{\"key\":\"" + key + "\",\"value\":\"" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture).Replace("\"", "'") + "\"}");

    static string Root => Path.GetDirectoryName(Application.dataPath);

    /// <summary>The scene's own files beside it and those in its folder.</summary>
    static string Files()
    {
        var listed = new List<string>();
        foreach (var dir in new[] { "Assets/Scenes", Folder })
        {
            var full = Path.Combine(Root, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var f in Directory.GetFiles(full))
            {
                var name = Path.GetFileName(f);
                if (dir == Folder) listed.Add("RoomScriptBake/" + name);
                else if (name.StartsWith("RoomScriptBake", StringComparison.Ordinal)) listed.Add(name);
            }
        }
        listed.Sort(StringComparer.Ordinal);
        return string.Join(" ", listed);
    }

    static string SceneHash()
    {
        using var sha = SHA256.Create();
        var bytes = File.ReadAllBytes(Path.Combine(Root, Scene));
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 16);
    }

    static bool DataOnDisk() => File.Exists(Path.Combine(Root, Folder, "LightingData.asset"));

    static string Indices()
    {
        var indices = new List<string>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if ((GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) != 0)
                indices.Add(r.name + ":" + r.realtimeLightmapIndex);
        indices.Sort(StringComparer.Ordinal);
        return string.Join(" ", indices);
    }

    static void Bake(string prefix)
    {
        var events = new List<string>();
        var data = new List<string>();
        void Note(string name)
        {
            events.Add(name + "(running " + Lightmapping.isRunning + ")");
            data.Add(name + "(on disk " + DataOnDisk() + ", named " + (Lightmapping.lightingDataAsset != null) + ")");
        }
        Action started = () => Note("bakeStarted"), completed = () => Note("bakeCompleted"),
            updated = () => Note("lightingDataUpdated"), cleared = () => Note("lightingDataCleared"),
            assetCleared = () => Note("lightingDataAssetCleared"), cancelled = () => Note("bakeCancelled");
        var logs = new List<string>();
        Application.LogCallback log = (m, s, t) => logs.Add(t + ": " + m);
        Lightmapping.bakeStarted += started;
        Lightmapping.bakeCompleted += completed;
        Lightmapping.lightingDataUpdated += updated;
        Lightmapping.lightingDataCleared += cleared;
        Lightmapping.lightingDataAssetCleared += assetCleared;
        Lightmapping.bakeCancelled += cancelled;
        Application.logMessageReceived += log;
        var result = Lightmapping.Bake();
        Application.logMessageReceived -= log;
        Lightmapping.bakeStarted -= started;
        Lightmapping.bakeCompleted -= completed;
        Lightmapping.lightingDataUpdated -= updated;
        Lightmapping.lightingDataCleared -= cleared;
        Lightmapping.lightingDataAssetCleared -= assetCleared;
        Lightmapping.bakeCancelled -= cancelled;
        Write(prefix + ".result", result);
        Write(prefix + ".events", string.Join(", ", events));
        Write(prefix + ".eventData", string.Join(", ", data));
        Write(prefix + ".isRunning", Lightmapping.isRunning);
        Write(prefix + ".log", string.Join(" || ", logs));
        var asset = Lightmapping.lightingDataAsset;
        Write(prefix + ".lightingData", asset == null ? "null" : asset.name + " at " + AssetDatabase.GetAssetPath(asset));
        Write(prefix + ".dirty", EditorSceneManager.GetActiveScene().isDirty);
        Write(prefix + ".files", Files());
        Write(prefix + ".sceneHash", SceneHash());
        Write(prefix + ".indices", Indices());
        Write(prefix + ".lightmaps", LightmapSettings.lightmaps.Length);
    }

    public static void Record()
    {
        try
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(Scene);
            Write("edit.before.lightingData", Lightmapping.lightingDataAsset != null);
            Write("edit.before.dirty", EditorSceneManager.GetActiveScene().isDirty);
            Write("edit.before.files", Files());
            Write("edit.before.sceneHash", SceneHash());
            Write("edit.before.indices", Indices());
            Bake("edit.first");
            Bake("edit.second");
            File.WriteAllLines(Environment.GetEnvironmentVariable("REALTIME_GI_EDIT_BAKE_OUT") + ".edit", s_Lines);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
