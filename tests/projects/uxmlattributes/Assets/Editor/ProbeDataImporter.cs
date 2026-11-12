using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .probedata file as two ProbeData objects (identifiers "main" and "second"): an
/// asset made by a project ScriptedImporter, with several objects and no texture.</summary>
[ScriptedImporter(1, "probedata")]
public class ProbeDataImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var data = ScriptableObject.CreateInstance<ProbeData>();
        ctx.AddObjectToAsset("main", data);
        var second = ScriptableObject.CreateInstance<ProbeData>();
        second.name = "Second";
        ctx.AddObjectToAsset("second", second);
        ctx.SetMainObject(data);
    }
}
