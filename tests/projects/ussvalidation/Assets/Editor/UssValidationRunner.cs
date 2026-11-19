using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Re-imports the probe style sheet while recording the importer's warnings, then enters
/// Play mode, where UssValidationProbe records the styles and exits the Editor.</summary>
public static class UssValidationRunner
{
    public static void Record()
    {
        var output = Environment.GetEnvironmentVariable("USS_VALIDATION_OUT");
        var logs = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type != LogType.Log) logs.Add(type + ": " + message);
        };
        Application.logMessageReceived += capture;
        AssetDatabase.ImportAsset("Assets/Resources/Invalid.uss", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset("Assets/Resources/Broken.uss", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset("Assets/Resources/Errors.uss", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Application.logMessageReceived -= capture;
        File.WriteAllLines(Path.Combine(output, "import-logs.txt"), logs);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
