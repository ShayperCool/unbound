using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Records every OnPostprocessAllAssets call: what it was told was imported, deleted and
/// moved (each list sorted: their order is not documented) and whether a domain reload made it.</summary>
public class SessionImportRecorder : AssetPostprocessor
{
    public static readonly List<string> Calls = new List<string>();

    static string List(string[] paths) => "[" + string.Join(",", paths.OrderBy(p => p, System.StringComparer.Ordinal)) + "]";

    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
    {
        Calls.Add($"reload={didDomainReload} imported={List(importedAssets)} deleted={List(deletedAssets)} moved={List(movedAssets)} from={List(movedFromAssetPaths)}");
    }
}

/// <summary>
/// Edit mode: the OnPostprocessAllAssets calls the AssetDatabase's writes make in a session (the
/// Editor imports what it writes): a folder made, assets created, a dirty material saved and a
/// clean save, an object added to an asset, a copy, a move, a rename, a new file found by Refresh
/// and one imported by ImportAsset, a changed file found by Refresh, a forced import, writes
/// between StartAssetEditing and StopAssetEditing, an import of an unchanged asset, the open scene saved
/// as a copy,
/// a prefab saved, a new image imported, its importer's SaveAndReimport, a folder copied and moved, a folder moved to the
/// trash, deletes of an asset and of a folder. Each step
/// logs the calls made by the time its writes returned (`<step>`) and those made by the next
/// editor update (`<step>.later`).
/// </summary>
public class SessionImportTests
{
    const string Root = "Assets/Session";

    static IEnumerator Step(string key, System.Action action)
    {
        SessionImportRecorder.Calls.Clear();
        try { action(); }
        catch (System.Exception e) { ProbeLog.Row(key + ".threw", e.GetType().Name); }
        ProbeLog.Row(key, string.Join(" | ", SessionImportRecorder.Calls));
        SessionImportRecorder.Calls.Clear();
        yield return null;
        ProbeLog.Row(key + ".later", string.Join(" | ", SessionImportRecorder.Calls));
    }

    static ProbeData Data(string name)
    {
        var data = ScriptableObject.CreateInstance<ProbeData>();
        data.name = name;
        return data;
    }

    [UnityTest]
    public IEnumerator SessionImports()
    {
        yield return Step("folder", () => AssetDatabase.CreateFolder("Assets", "Session"));
        yield return Step("create", () => AssetDatabase.CreateAsset(Data("Data"), Root + "/Data.asset"));
        yield return Step("material", () => AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Color")), Root + "/Mat.mat"));
        yield return Step("dirty-save", () =>
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Mat.mat");
            material.color = Color.red;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        });
        yield return Step("clean-save", AssetDatabase.SaveAssets);
        yield return Step("add-object", () =>
        {
            AssetDatabase.AddObjectToAsset(Data("Sub"), AssetDatabase.LoadMainAssetAtPath(Root + "/Data.asset"));
            AssetDatabase.SaveAssets();
        });
        yield return Step("copy", () => AssetDatabase.CopyAsset(Root + "/Data.asset", Root + "/Copy.asset"));
        yield return Step("move", () => AssetDatabase.MoveAsset(Root + "/Copy.asset", Root + "/Moved.asset"));
        yield return Step("rename", () => AssetDatabase.RenameAsset(Root + "/Moved.asset", "Renamed"));
        yield return Step("write-refresh", () =>
        {
            File.WriteAllText(Root + "/Notes.txt", "notes");
            AssetDatabase.Refresh();
        });
        yield return Step("write-import", () =>
        {
            File.WriteAllText(Root + "/Other.txt", "other");
            AssetDatabase.ImportAsset(Root + "/Other.txt");
        });
        yield return Step("change-refresh", () =>
        {
            File.WriteAllText(Root + "/Notes.txt", "changed notes");
            AssetDatabase.Refresh();
        });
        yield return Step("force-import", () => AssetDatabase.ImportAsset(Root + "/Data.asset", ImportAssetOptions.ForceUpdate));
        yield return Step("batch", () =>
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                AssetDatabase.CreateAsset(Data("First"), Root + "/First.asset");
                AssetDatabase.CopyAsset(Root + "/Data.asset", Root + "/Second.asset");
                ProbeLog.Row("batch.inside", string.Join(" | ", SessionImportRecorder.Calls));
            }
            finally { AssetDatabase.StopAssetEditing(); }
        });
        yield return Step("plain-import", () => AssetDatabase.ImportAsset(Root + "/Data.asset"));
        yield return Step("scene-save", () =>
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), Root + "/Saved.unity", true));
        yield return Step("prefab", () =>
        {
            var go = new GameObject("Made");
            PrefabUtility.SaveAsPrefabAsset(go, Root + "/Made.prefab");
            Object.DestroyImmediate(go);
        });
        yield return Step("png-import", () =>
        {
            File.WriteAllBytes(Root + "/Pic.png", new Texture2D(4, 4).EncodeToPNG());
            AssetDatabase.ImportAsset(Root + "/Pic.png");
        });
        yield return Step("importer", () =>
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Pic.png");
            importer.maxTextureSize = 64;
            importer.SaveAndReimport();
        });
        yield return Step("folder-copy", () =>
        {
            AssetDatabase.CreateFolder(Root, "Inner");
            AssetDatabase.CreateAsset(Data("Held"), Root + "/Inner/Held.asset");
            SessionImportRecorder.Calls.Clear();
            AssetDatabase.CopyAsset(Root + "/Inner", Root + "/InnerCopy");
        });
        yield return Step("folder-move", () => AssetDatabase.MoveAsset(Root + "/InnerCopy", Root + "/InnerMoved"));
        yield return Step("trash", () => AssetDatabase.MoveAssetToTrash(Root + "/InnerMoved"));
        yield return Step("delete", () => AssetDatabase.DeleteAsset(Root + "/Renamed.asset"));
        yield return Step("delete-folder", () => AssetDatabase.DeleteAsset(Root));
    }
}
