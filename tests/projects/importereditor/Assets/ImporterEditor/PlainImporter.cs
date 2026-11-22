using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .plainimp file; no editor class is written for it.</summary>
[ScriptedImporter(1, "plainimp")]
public class PlainImporter : ScriptedImporter
{
    public int amount = 1;

    public override void OnImportAsset(AssetImportContext context)
    {
        var asset = new TextAsset("amount " + amount);
        context.AddObjectToAsset("main", asset);
        context.SetMainObject(asset);
    }
}
