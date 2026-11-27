using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, "tune")]
public class TuneImporter : ScriptedImporter
{
    public int scale = 2;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var asset = new TextAsset("tune:" + scale);
        ctx.AddObjectToAsset("main", asset);
        ctx.SetMainObject(asset);
    }
}
