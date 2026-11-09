using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Re-imports the probe documents (after the scripts compiled) while recording the import's
/// warnings and errors into import-logs.txt, then enters Play mode, where BindingsProbe records the
/// bindings and exits the Editor.</summary>
public static class UitkBindingsRunner
{
    public static void Record()
    {
        var output = Environment.GetEnvironmentVariable("UITK_BINDINGS_OUT");
        var logs = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type != LogType.Log) logs.Add(type + ": " + message);
        };
        Application.logMessageReceived += capture;
        foreach (var doc in new[] { "Bindings", "NoProperty", "NotBinding", "UnknownType" })
            AssetDatabase.ImportAsset("Assets/Resources/" + doc + ".uxml", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Application.logMessageReceived -= capture;
        File.WriteAllLines(Path.Combine(output, "import-logs.txt"), logs);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
