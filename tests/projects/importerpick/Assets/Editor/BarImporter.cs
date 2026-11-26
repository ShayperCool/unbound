using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .bar file as a TextAsset whose text names this importer.</summary>
[ScriptedImporter(1, "bar")]
public class BarImporter : ScriptedImporter
{
    public int setting;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var text = new TextAsset("BarImporter:" + System.IO.File.ReadAllText(ctx.assetPath).Trim());
        ctx.AddObjectToAsset("main", text);
        ctx.SetMainObject(text);
    }
}
