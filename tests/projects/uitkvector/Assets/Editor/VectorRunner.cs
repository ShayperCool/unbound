using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Re-imports the probe's SVG files while recording the import's warnings and errors into
/// import-logs.txt, then enters Play mode, where VectorProbe records the images and exits the Editor.</summary>
public static class VectorRunner
{
    public static void Record()
    {
        var output = Environment.GetEnvironmentVariable("UITK_VECTOR_OUT");
        EditorSettings.asyncShaderCompilation = false;
        var logs = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type != LogType.Log) logs.Add(type + ": " + message);
        };
        Application.logMessageReceived += capture;
        foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets/Resources/Vector" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Application.logMessageReceived -= capture;
        File.WriteAllLines(Path.Combine(output, "import-logs.txt"), logs);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
