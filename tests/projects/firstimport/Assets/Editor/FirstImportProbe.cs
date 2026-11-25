using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// What OnPostprocessAllAssets is given as the project opens, and in which order its callbacks run
/// (see README.md). Each call appends one line to the file FIRSTIMPORT_OUT names; run.py turns the
/// lines into rows. The Editor's capture runs <c>-executeMethod FirstImportProbe.Record</c>, which
/// enters Play mode and leaves the Editor after a few frames, as `unbound run` plays the project.
/// </summary>
static class FirstImportProbe
{
    internal const string LitV9 = "Assets/M/LitV9.mat";
    static string Out => Environment.GetEnvironmentVariable("FIRSTIMPORT_OUT");

    /// <summary>One line: fields separated by tabs.</summary>
    internal static void Line(params string[] fields)
    {
        var path = Out;
        if (string.IsNullOrEmpty(path)) return;
        File.AppendAllText(path, string.Join("\t", fields.Select(f => (f ?? "").Replace("\t", " ").Replace("\n", " "))) + "\n");
    }

    internal static string State() => EditorApplication.isPlayingOrWillChangePlaymode ? "play" : "edit";

    /// <summary>The material version object's version LitV9.mat holds now.</summary>
    internal static string Version(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o != null && o.GetType().Name == "AssetVersion")
                return new SerializedObject(o).FindProperty("version")?.intValue.ToString() ?? "?";
        return "none";
    }

    /// <summary>A call: the class, its form, didDomainReload, the Editor's state, how many assets
    /// each list holds and LitV9's version as the class sees it.</summary>
    internal static void Call(Type type, string form, string reload, string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (type == typeof(AAAFive)) Line("dispatch");
        Line("call", type.Name, form, reload, State(), imported.Length.ToString(), deleted.Length.ToString(), moved.Length.ToString(),
            movedFrom.Length.ToString(), Version(LitV9));
    }

    static string Sorted(IEnumerable<string> paths) => string.Join(",", paths.OrderBy(p => p, StringComparer.Ordinal));

    /// <summary>The lists, by where the assets are: the project's assets and folders, the settings
    /// files, the packages' (a count) and anything else.</summary>
    internal static void Lists(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        Line("imported.assets", Sorted(imported.Where(p => p == "Assets" || p.StartsWith("Assets/", StringComparison.Ordinal))));
        Line("imported.settings", Sorted(imported.Where(p => p.StartsWith("ProjectSettings/", StringComparison.Ordinal))));
        Line("imported.packages", imported.Count(p => p.StartsWith("Packages/", StringComparison.Ordinal)).ToString());
        Line("imported.other", Sorted(imported.Where(p => !p.StartsWith("Assets", StringComparison.Ordinal) && !p.StartsWith("ProjectSettings/", StringComparison.Ordinal)
            && !p.StartsWith("Packages/", StringComparison.Ordinal))));
        Line("imported.distinct", (imported.Distinct().Count() == imported.Length).ToString());
        Line("counts", imported.Count(p => !p.StartsWith("Library/", StringComparison.Ordinal)).ToString(), deleted.Length.ToString(), moved.Length.ToString());
        Line("deleted", Sorted(deleted));
        Line("moved", string.Join(",", moved.Select((p, i) => p + "<-" + movedFrom[i]).OrderBy(p => p, StringComparer.Ordinal)));
    }

    static readonly string[] TypePaths =
    {
        "Assets/Types", "Assets/Types/Plain.png", "Assets/Types/Tone.wav", "Assets/Types/Plain.fbx", "Assets/Types/Thing.prefab",
        "Assets/Types/Atlas.spriteatlas", "Assets/M/Touched.mat", "Assets/Scenes/Probe.unity", "Assets/Editor/FirstImportProbe.cs",
        "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader", "Assets/Types/Notes.txt", "Assets/Types/Style.uss",
        "Assets/Types/Layout.uxml", "Assets/Types/Clip.anim", "Assets/Types/Control.controller", "Assets/DefaultVolumeProfile.asset",
        "Packages/com.unity.render-pipelines.universal/Shaders/AutodeskInteractive/AutodeskInteractive.shadergraph",
        "Packages/com.unity.render-pipelines.universal/Editor/Converter/converter_editor.uss",
        "Packages/com.unity.searcher/Editor/Resources/FlatSkin/Font/Roboto-Black.ttf",
    };

    /// <summary>The main asset types of assets of most kinds: whether the asset was loaded, its type
    /// and whether asking loaded it.</summary>
    internal static void Types()
    {
        foreach (var path in TypePaths)
        {
            bool before = AssetDatabase.IsMainAssetAtPathLoaded(path);
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            Line("type", path, before.ToString(), type == null ? "null" : type.FullName, AssetDatabase.IsMainAssetAtPathLoaded(path).ToString());
        }
    }

    static bool s_TypesDone;

    internal static void TypesOnce()
    {
        if (s_TypesDone || SessionState.GetBool("FirstImportProbe.types", false)) return;
        s_TypesDone = true;
        SessionState.SetBool("FirstImportProbe.types", true);
        Types();
    }

    /// <summary>The Editor's capture: Play mode, then the Editor's exit after a few frames.</summary>
    public static void Record()
    {
        Line("record", State());
        EditorApplication.isPlaying = true;
    }
}

[InitializeOnLoad]
static class FirstImportExit
{
    static int s_Frames;

    static FirstImportExit()
    {
        FirstImportProbe.Line("load", FirstImportProbe.State());
        EditorApplication.update += () =>
        {
            if (!EditorApplication.isPlaying || ++s_Frames != 5) return;
            FirstImportProbe.Line("exit", FirstImportProbe.Version(FirstImportProbe.LitV9));
            EditorApplication.Exit(0);
        };
        EditorApplication.quitting += () => FirstImportProbe.Line("quitting");
    }
}

// The probe's callbacks. AAAFive sorts first among all and starts each line group of a dispatch.
class AAAFive : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
    {
        FirstImportProbe.Call(typeof(AAAFive), "5", didDomainReload ? "1" : "0", imported, deleted, moved, movedFrom);
        if (didDomainReload && !EditorApplication.isPlayingOrWillChangePlaymode) FirstImportProbe.TypesOnce();
        FirstImportProbe.Lists(imported, deleted, moved, movedFrom);
    }
}

class AAAFour : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
        FirstImportProbe.Call(typeof(AAAFour), "4", "-", imported, deleted, moved, movedFrom);
}

// Ordinal order puts BUpper first, a culture's order aLower.
class aLower : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
        FirstImportProbe.Call(typeof(aLower), "5", didDomainReload ? "1" : "0", imported, deleted, moved, movedFrom);
}

class BUpper : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
        FirstImportProbe.Call(typeof(BUpper), "5", didDomainReload ? "1" : "0", imported, deleted, moved, movedFrom);
}

class ZZZFive : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
        FirstImportProbe.Call(typeof(ZZZFive), "5", didDomainReload ? "1" : "0", imported, deleted, moved, movedFrom);
}

// Follows the first callback of all, and the render pipeline's material postprocessor.
class ZDependent : AssetPostprocessor
{
    [RunAfterClass(typeof(AAAFive))]
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
        FirstImportProbe.Call(typeof(ZDependent), "5", didDomainReload ? "1" : "0", imported, deleted, moved, movedFrom);
}

class ZLast : AssetPostprocessor
{
    [RunAfterClass("UnityEditor.Rendering.Universal.MaterialPostprocessor, Unity.RenderPipelines.Universal.Editor")]
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
        FirstImportProbe.Call(typeof(ZLast), "5", didDomainReload ? "1" : "0", imported, deleted, moved, movedFrom);
}
