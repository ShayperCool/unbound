using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Re-imports the probe style sheet while recording the importer's warnings, then enters
/// Play mode, where CursorsProbe records the cursors and exits the Editor.</summary>
public static class UitkCursorsRunner
{
    public static void Record()
    {
        var output = Environment.GetEnvironmentVariable("UITK_CURSORS_OUT");
        var logs = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type != LogType.Log) logs.Add(type + ": " + message);
        };
        Application.logMessageReceived += capture;
        AssetDatabase.ImportAsset("Assets/Resources/Cursors.uss", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Application.logMessageReceived -= capture;
        File.WriteAllLines(Path.Combine(output, "import-logs.txt"), logs);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
