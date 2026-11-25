using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Appends each SpeedTree callback of the import of a tree of Assets/Trees, in order, to
/// SPEEDTREE8_PROBE_OUT/events.txt (one line: callback, asset path, what it sees), and the material
/// and model callbacks the Editor does not make for trees, so that they would show.
/// SpeedTree8Probe turns the lines into values.
/// </summary>
public sealed class SpeedTree8Events : AssetPostprocessor
{
    internal const string File = "events.txt";

    static void Write(string kind, string path, string detail)
    {
        string dir = Environment.GetEnvironmentVariable("SPEEDTREE8_PROBE_OUT");
        if (string.IsNullOrEmpty(dir) || !path.StartsWith("Assets/Trees/", StringComparison.Ordinal)) return;
        Directory.CreateDirectory(dir);
        System.IO.File.AppendAllText(Path.Combine(dir, File), kind + "\t" + path + "\t" + detail.Replace("\n", " ").Replace("\t", " ") + "\n");
    }

    string Importer => assetImporter == null ? "null" : assetImporter.GetType().Name;

    void OnPreprocessAsset()
    {
        if (assetPath.EndsWith(".st", StringComparison.Ordinal)) Write("OnPreprocessAsset", assetPath, Importer);
    }

    void OnPreprocessSpeedTree()
    {
        var importer = assetImporter as SpeedTreeImporter;
        Write("OnPreprocessSpeedTree", assetPath, Importer + (importer == null ? "" :
            " materialLocation " + importer.materialLocation + " hasImported " + importer.hasImported + " isV8 " + importer.isV8));
    }

    void OnPostprocessSpeedTree(GameObject root)
    {
        var detail = new StringBuilder(Importer + " hasImported " + ((assetImporter as SpeedTreeImporter)?.hasImported.ToString() ?? "null"));
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            detail.Append(" ").Append(t.name).Append("[").Append(string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().Name))).Append("]");
        Write("OnPostprocessSpeedTree", assetPath, detail.ToString());
    }

    void OnPostprocessMaterial(Material material) => Write("OnPostprocessMaterial", assetPath, material.name);

    Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        Write("OnAssignMaterialModel", assetPath, material.name);
        return null;
    }

    void OnPostprocessMeshHierarchy(GameObject root) => Write("OnPostprocessMeshHierarchy", assetPath, root.name);
}
