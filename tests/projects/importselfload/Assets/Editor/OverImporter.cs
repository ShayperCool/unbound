using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports .over files, and .ovr2 files whose .meta names it (an override extension), as a
/// TextAsset named "over".</summary>
[ScriptedImporter(1, new[] { "over" }, new[] { "ovr2" })]
public class OverImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var text = new TextAsset("over") { name = "over" };
        ctx.AddObjectToAsset("text", text);
        ctx.SetMainObject(text);
    }
}
