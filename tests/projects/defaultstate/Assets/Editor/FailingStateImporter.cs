using System;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, "defaultstatefail")]
public sealed class FailingStateImporter : ScriptedImporter
{
    [NonSerialized] public bool entered;
    public FailingStateImporter() { entered = true; throw new InvalidOperationException("default-state constructor control"); }
    public override void OnImportAsset(AssetImportContext context)
    {
        var asset = new TextAsset("constructor control");
        context.AddObjectToAsset("main", asset); context.SetMainObject(asset);
    }
}
