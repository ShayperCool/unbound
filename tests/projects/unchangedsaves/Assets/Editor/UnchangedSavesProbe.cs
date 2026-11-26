using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Which saves write a file whose text would not change (see README.md). Every open plays a few
/// frames: frame 2 makes the SaveAssets cases and saves, frame 3 the SaveAssetIfDirty case, frame 4
/// leaves the exit cases dirty, frame 6 leaves the Editor (a no-op open, UNCHANGEDSAVES_NOOP=1, only
/// plays and leaves; with UNCHANGEDSAVES_LEAVE=1 it leaves Play mode first and exits in Edit mode). At the first open the start's
/// OnPostprocessAllAssets marks the first-open cases. Lines go to the file UNCHANGEDSAVES_OUT
/// names; run.py turns them and the files' times into rows. The Editor's capture runs
/// <c>-executeMethod UnchangedSavesProbe.Record</c>, which enters Play mode, as `unbound run` plays
/// the project. <c>-executeMethod UnchangedSavesProbe.Create</c> authors the assets (run.py create).
/// </summary>
static class UnchangedSavesProbe
{
    internal const string Folder = "Assets/P";
    const string Gloss = "_Glossiness";
    static string Out => Environment.GetEnvironmentVariable("UNCHANGEDSAVES_OUT");
    internal static bool Recording => !string.IsNullOrEmpty(Out);
    /// <summary>A session that changes nothing: it plays and leaves.</summary>
    internal static bool NoOp => Environment.GetEnvironmentVariable("UNCHANGEDSAVES_NOOP") == "1";
    /// <summary>A session that leaves Play mode first and exits the Editor in Edit mode.</summary>
    internal static bool LeavesPlayMode => Environment.GetEnvironmentVariable("UNCHANGEDSAVES_LEAVE") == "1";

    internal static void Line(params string[] fields)
    {
        if (!Recording) return;
        File.AppendAllText(Out, string.Join("\t", fields.Select(f => (f ?? "").Replace("\t", " ").Replace("\n", " "))) + "\n");
    }

    internal static string State() => EditorApplication.isPlayingOrWillChangePlaymode ? "play" : "edit";

    internal static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/{name}.mat");

    /// <summary>A value the material does not hold yet: the next quarter step, so each open changes it.</summary>
    internal static void Change(string name)
    {
        var m = Mat(name);
        m.SetFloat(Gloss, (m.GetFloat(Gloss) + 0.125f) % 1f);
    }

    internal static void Dirty(string name) => EditorUtility.SetDirty(Mat(name));

    internal static void Report(string step, params string[] names)
    {
        foreach (var name in names)
        {
            var o = name.EndsWith(".asset") ? (UnityEngine.Object)AssetDatabase.LoadAssetAtPath<ProbeData>($"{Folder}/{name}") : Mat(name);
            Line("dirty", step, name, EditorUtility.IsDirty(o) ? "1" : "0");
        }
    }

    /// <summary>Frame 2 of every open: each case, then AssetDatabase.SaveAssets.</summary>
    internal static void SaveAssetsCases()
    {
        Dirty("SetDirty");
        var same = Mat("SameValue");
        same.SetFloat(Gloss, same.GetFloat(Gloss));
        var reverted = Mat("Reverted");
        var was = reverted.GetFloat(Gloss);
        reverted.SetFloat(Gloss, 0.9f);
        reverted.SetFloat(Gloss, was);
        Change("Changed");
        Dirty("Crlf");
        var data = AssetDatabase.LoadAssetAtPath<ProbeData>($"{Folder}/Data.asset");
        data.number += 0;
        EditorUtility.SetDirty(data);
        Report("beforeSave", "SetDirty", "SameValue", "Reverted", "Changed", "Crlf", "Data.asset");
        AssetDatabase.SaveAssets();
        Report("afterSave", "SetDirty", "SameValue", "Reverted", "Changed", "Crlf", "Data.asset");
    }

    /// <summary>Frame 3: one asset saved alone.</summary>
    internal static void SaveIfDirtyCase()
    {
        Dirty("IfDirty");
        AssetDatabase.SaveAssetIfDirty(Mat("IfDirty"));
        Report("afterSaveIfDirty", "IfDirty");
    }

    /// <summary>Frame 4: left dirty for the Editor's exit.</summary>
    internal static void ExitCases()
    {
        Dirty("ExitDirty");
        Change("ExitChanged");
        Report("beforeExit", "ExitDirty", "ExitChanged");
    }

    /// <summary>The Editor's capture: Play mode, then the Editor's exit after a few frames.</summary>
    public static void Record()
    {
        Line("record", State());
        EditorApplication.isPlaying = true;
    }

    static readonly string[] Materials =
    {
        "PostDirty", "PostChanged", "SetDirty", "SameValue", "Reverted", "Changed", "Crlf", "IfDirty",
        "ExitDirty", "ExitChanged", "Untouched",
    };

    /// <summary>Authors the probe's assets as the Editor writes them (run.py create copies them back).</summary>
    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "P");
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        foreach (var name in Materials)
        {
            var m = new Material(Shader.Find("Standard"));
            m.SetFloat(Gloss, 0.5f);
            AssetDatabase.CreateAsset(m, $"{Folder}/{name}.mat");
        }
        var data = ScriptableObject.CreateInstance<ProbeData>();
        data.number = 7;
        AssetDatabase.CreateAsset(data, $"{Folder}/Data.asset");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        AssetDatabase.SaveAssets();
    }
}

[InitializeOnLoad]
static class UnchangedSavesFrames
{
    static int s_Frames;

    static UnchangedSavesFrames()
    {
        UnchangedSavesProbe.Line("load", UnchangedSavesProbe.State());
        EditorApplication.update += () =>
        {
            if (!UnchangedSavesProbe.Recording || !EditorApplication.isPlaying) return;
            if (UnchangedSavesProbe.NoOp && ++s_Frames < 6) return;
            switch (UnchangedSavesProbe.NoOp ? s_Frames : ++s_Frames)
            {
                case 2: UnchangedSavesProbe.SaveAssetsCases(); break;
                case 3: UnchangedSavesProbe.SaveIfDirtyCase(); break;
                case 4: UnchangedSavesProbe.ExitCases(); break;
                case 6:
                    UnchangedSavesProbe.Line("exit");
                    if (UnchangedSavesProbe.LeavesPlayMode) EditorApplication.ExitPlaymode();
                    else EditorApplication.Exit(0);
                    break;
            }
        };
        // Where the exit's save stands among the quit callbacks.
        Application.quitting += () => UnchangedSavesProbe.Line("appquitting", UnchangedSavesProbe.State());
        Application.unloading += () => UnchangedSavesProbe.Line("unloading", UnchangedSavesProbe.State());
        EditorApplication.quitting += () => UnchangedSavesProbe.Line("quitting", UnchangedSavesProbe.State());
        EditorApplication.playModeStateChanged += change =>
        {
            UnchangedSavesProbe.Line("playmode", change.ToString());
            // Back in Edit mode, the Editor exits with the exit cases still dirty.
            if (change == PlayModeStateChange.EnteredEditMode && UnchangedSavesProbe.Recording && UnchangedSavesProbe.LeavesPlayMode)
                EditorApplication.Exit(0);
        };
    }
}

/// <summary>The first open's dispatch marks the first-open cases; every dispatch records the
/// probe's assets it lists as imported (the Editor's imports of the files it saved).</summary>
class UnchangedSavesPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
    {
        if (!UnchangedSavesProbe.Recording) return;
        var mine = imported.Where(p => p.StartsWith(UnchangedSavesProbe.Folder + "/", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        UnchangedSavesProbe.Line("dispatch", UnchangedSavesProbe.State(), didDomainReload ? "1" : "0", string.Join("|", mine));
        // Only the start's dispatch of the first open (its imports list every asset); the
        // dispatches of the files the Editor saves list them again.
        if (!didDomainReload || UnchangedSavesProbe.State() != "edit" || !mine.Contains($"{UnchangedSavesProbe.Folder}/PostDirty.mat")) return;
        UnchangedSavesProbe.Dirty("PostDirty");
        UnchangedSavesProbe.Change("PostChanged");
    }
}
