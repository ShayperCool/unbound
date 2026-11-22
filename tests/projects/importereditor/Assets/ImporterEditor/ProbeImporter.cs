using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .probeimp file as a text asset; its two settings are what the probe edits.</summary>
[ScriptedImporter(1, "probeimp")]
public class ProbeImporter : ScriptedImporter
{
    public int number = 3;
    public string label = "start";

    /// <summary>How often each asset path was imported in this session.</summary>
    public static readonly System.Collections.Generic.Dictionary<string, int> Imports = new System.Collections.Generic.Dictionary<string, int>();

    public override void OnImportAsset(AssetImportContext context)
    {
        Imports.TryGetValue(context.assetPath, out var count);
        Imports[context.assetPath] = count + 1;
        var asset = new TextAsset("number " + number + " label " + label);
        context.AddObjectToAsset("main", asset);
        context.SetMainObject(asset);
    }
}
