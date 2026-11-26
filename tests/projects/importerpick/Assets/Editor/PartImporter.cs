using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .part file as a TextAsset whose text names this importer and its label. The
/// class is partial: APartImporterHelpers.cs and PartImporterSettings.cs declare it too, before any
/// other type, so each of them stands for it as well; this file is named like it.</summary>
[ScriptedImporter(1, "part")]
public partial class PartImporter : ScriptedImporter
{
    public int setting;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var text = new TextAsset("PartImporter:" + System.IO.File.ReadAllText(ctx.assetPath).Trim() + ":" + Label());
        ctx.AddObjectToAsset("main", text);
        ctx.SetMainObject(text);
    }
}
