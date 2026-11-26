using System.Collections.Generic;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .count file as a TextAsset of its number and how many times it was imported.</summary>
[ScriptedImporter(1, "count")]
public class CountImporter : ScriptedImporter
{
    static readonly Dictionary<string, int> s_Imports = new Dictionary<string, int>();

    public override void OnImportAsset(AssetImportContext ctx)
    {
        s_Imports.TryGetValue(ctx.assetPath, out var n);
        s_Imports[ctx.assetPath] = ++n;
        RefreshEvents.Events.Add("importer " + ctx.assetPath);
        var text = new TextAsset("count " + File.ReadAllText(ctx.assetPath).Trim() + " import " + n);
        ctx.AddObjectToAsset("main", text);
        ctx.SetMainObject(text);
    }
}
