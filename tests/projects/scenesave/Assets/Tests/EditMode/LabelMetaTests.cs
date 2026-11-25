using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit mode: .meta files that hold a GUID alone or an importer section without some of its
/// settings (run.py seeds them under Assets/Labels), as the Editor leaves them when the project
/// opens, after AssetDatabase.SetLabels and after ClearLabels on each kind of asset, and after
/// importer settings are changed and saved by SaveAndReimport: a setting the section has and a
/// setting it lacks, the section lacking a platform too. run.py compares the files on disk after
/// the run.
/// </summary>
public class LabelMetaTests
{
    static readonly string[] Labelled =
    {
        "Tex.png", "Notes.txt", "Data.json", "Mat.mat", "Thing.prefab", "Level.unity", "Beep.wav", "Folder",
        "Unlit.shader", "Style.uss", "Bouncy.physicMaterial", "Move.anim", "Basic.controller", "Data.asset", "Other.xyz",
    };

    const string Root = "Assets/Labels/";

    static void OnLog(string message, string stack, LogType type) => ProbeLog.Row("log", type + ": " + message);

    static void Snap(string key, string path) => ProbeLog.Snapshot(key + "." + Path.GetFileName(path) + ".meta", path + ".meta");

    [Test]
    public void LabelMetas()
    {
        Application.logMessageReceived += OnLog;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            foreach (var name in Labelled) Snap("start", Root + name);
            foreach (var name in new[] { "Partial.png", "PartialKept.png" }) Snap("start", Root + name);
            foreach (var name in Labelled)
            {
                var path = Root + name;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                ProbeLog.Row(name + ".type", asset != null ? asset.GetType().Name : "null");
                ProbeLog.Row(name + ".importer", AssetImporter.GetAtPath(path)?.GetType().Name ?? "null");
                if (asset == null) continue;
                AssetDatabase.SetLabels(asset, new[] { "probe", "Alpha" });
                ProbeLog.Row(name + ".labels", string.Join(",", AssetDatabase.GetLabels(asset)));
                Snap("labelled", path);
                AssetDatabase.ClearLabels(asset);
                Snap("cleared", path);
            }

            // A section without ignorePngGamma, swizzle, streamingMipmaps and the Standalone
            // platform (the Android one listed): one of its own settings changed, then a missing one.
            var kept = (TextureImporter)AssetImporter.GetAtPath(Root + "PartialKept.png");
            kept.mipmapEnabled = false;
            kept.SaveAndReimport();
            Snap("kept", Root + "PartialKept.png");
            var partial = (TextureImporter)AssetImporter.GetAtPath(Root + "Partial.png");
            partial.streamingMipmaps = true;
            partial.SaveAndReimport();
            Snap("missingSetting", Root + "Partial.png");
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            Application.logMessageReceived -= OnLog;
        }
    }
}
