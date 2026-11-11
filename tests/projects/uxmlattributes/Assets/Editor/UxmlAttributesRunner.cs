using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Re-imports the probe documents (after the controls compiled) while recording the
/// import's warnings and errors into import-logs.txt, then enters Play mode, where the probe
/// script records the built trees and exits the Editor.</summary>
public static class UxmlAttributesRunner
{
    public static void Record()
    {
        var output = Environment.GetEnvironmentVariable("UXML_ATTRIBUTES_OUT");
        var logs = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type != LogType.Log) logs.Add(type + ": " + message);
        };
        Application.logMessageReceived += capture;
        AssetDatabase.ImportAsset("Assets/UI/Child.uxml", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset("Assets/Resources/UxmlAttributesProbe.uxml", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        File.WriteAllLines(Path.Combine(output, "import-logs.txt"), logs);
        logs.Clear();
        AssetDatabase.ImportAsset("Assets/Resources/UxmlIconsProbe.uxml", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Application.logMessageReceived -= capture;
        File.WriteAllLines(Path.Combine(output, "import-logs-icons.txt"), logs);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
