using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// What loading a URP renderer asset saved in an older layout does to it, and what the Editor's exit
/// writes (see README.md). In Play mode, frame 2 loads each asset of Resources/R and records whether
/// it is dirty and whether its XR system data reference is set; frame 4 leaves the Editor, whose exit
/// saves what is dirty (a prefab's behaviour that marks itself dirty from OnValidate too). Lines go to the file URPRENDERERWRITES_OUT names. The Editor's capture runs
/// <c>-executeMethod UrpRendererWritesProbe.Record</c>, which enters Play mode, as `unbound run`
/// plays the project. <c>-executeMethod UrpRendererWritesProbe.Create -quit</c> authors
/// Resources/R/Current.asset (run.py create derives the older layouts from it).
/// </summary>
static class UrpRendererWritesProbe
{
    static string Out => Environment.GetEnvironmentVariable("URPRENDERERWRITES_OUT");
    internal static bool Recording => !string.IsNullOrEmpty(Out);
    internal static readonly string[] Names = { "Current", "OldMissingRef", "OldNullShader", "OldLayout" };
    /// <summary>Script assets that mark themselves dirty from a load callback (Resources/S).</summary>
    internal static readonly string[] Scripts = { "AwakeSame", "OnEnableSame", "OnEnableChanged", "OnValidateSame", "OnValidateChanged" };
    static readonly (string Name, string When, bool Change)[] ScriptCases =
    {
        ("AwakeSame", "Awake", false), ("OnEnableSame", "OnEnable", false), ("OnEnableChanged", "OnEnable", true),
        ("OnValidateSame", "OnValidate", false), ("OnValidateChanged", "OnValidate", true),
    };

    internal static void Line(params string[] fields)
    {
        if (!Recording) return;
        File.AppendAllText(Out, string.Join("\t", fields.Select(f => (f ?? "").Replace("\t", " ").Replace("\n", " "))) + "\n");
    }

    /// <summary>The obsolete XR system data reference (a field with the VR and XR modules).</summary>
    static string XrSystemData(UniversalRendererData data)
    {
        var field = typeof(UniversalRendererData).GetField("xrSystemData", BindingFlags.Public | BindingFlags.Instance);
        if (field == null) return "nofield";
        var value = field.GetValue(data) as UnityEngine.Object;
        return value == null ? "null" : value.name;
    }

    internal static void Load()
    {
        foreach (var name in Names)
        {
            var data = Resources.Load<UniversalRendererData>("R/" + name);
            if (data == null) { Line("load", name, "missing"); continue; }
            Line("load", name, EditorUtility.IsDirty(data) ? "dirty" : "clean", XrSystemData(data),
                data.debugShaders != null && data.debugShaders.probeVolumeSamplingDebugComputeShader != null ? "samplingShader" : "noSamplingShader");
        }
        foreach (var name in Scripts)
        {
            var asset = Resources.Load<DirtyOnLoad>("S/" + name);
            if (asset == null) { Line("load", name, "missing"); continue; }
            Line("load", name, EditorUtility.IsDirty(asset) ? "dirty" : "clean", asset.number.ToString());
        }
        // A prefab whose behaviour marks itself dirty from OnValidate (in Play mode only, so the
        // authoring import leaves it as written).
        var prefab = Resources.Load<GameObject>("P/ValidatePrefab");
        var behaviour = prefab != null ? prefab.GetComponent<DirtyOnValidate>() : null;
        Line("load", "ValidatePrefab", behaviour == null ? "missing" : (EditorUtility.IsDirty(behaviour) ? "dirty" : "clean") + "/" + behaviour.number);
    }

    public static void Record()
    {
        Line("record");
        EditorApplication.isPlaying = true;
    }

    public static void Create()
    {
        var data = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(data, "Assets/Resources/R/Current.asset");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/S")) AssetDatabase.CreateFolder("Assets/Resources", "S");
        foreach (var (name, when, change) in ScriptCases)
        {
            var asset = ScriptableObject.CreateInstance<DirtyOnLoad>();
            asset.when = when;
            asset.change = change;
            AssetDatabase.CreateAsset(asset, "Assets/Resources/S/" + name + ".asset");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/P")) AssetDatabase.CreateFolder("Assets/Resources", "P");
        var go = new GameObject("ValidatePrefab");
        go.AddComponent<DirtyOnValidate>();
        PrefabUtility.SaveAsPrefabAsset(go, "Assets/Resources/P/ValidatePrefab.prefab");
        UnityEngine.Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
    }
}

[InitializeOnLoad]
static class UrpRendererWritesFrames
{
    static int s_Frames;

    static UrpRendererWritesFrames()
    {
        EditorApplication.update += () =>
        {
            if (!UrpRendererWritesProbe.Recording || !EditorApplication.isPlaying) return;
            switch (++s_Frames)
            {
                case 2: UrpRendererWritesProbe.Load(); break;
                case 4:
                    UrpRendererWritesProbe.Line("exit");
                    EditorApplication.Exit(0);
                    break;
            }
        };
    }
}
