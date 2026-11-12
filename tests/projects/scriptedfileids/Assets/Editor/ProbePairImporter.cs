using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .probepair file as a 2x2 Texture2D (identifier "MainAsset", the main object)
/// and a ProbePair script object (identifier "Data").</summary>
[ScriptedImporter(1, "probepair")]
public class ProbePairImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var texture = new Texture2D(2, 2);
        ctx.AddObjectToAsset("MainAsset", texture);
        ctx.AddObjectToAsset("Data", ScriptableObject.CreateInstance<ProbePair>());
        ctx.SetMainObject(texture);
    }
}
