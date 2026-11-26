using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .foo file as a TextAsset whose text names this importer.</summary>
[ScriptedImporter(1, "foo")]
public class FooImporter : ScriptedImporter
{
    public int setting;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var text = new TextAsset("FooImporter:" + System.IO.File.ReadAllText(ctx.assetPath).Trim());
        ctx.AddObjectToAsset("main", text);
        ctx.SetMainObject(text);
    }
}
