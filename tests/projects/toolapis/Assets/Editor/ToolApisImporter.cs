using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Records which importer AssetImporter.GetAtPath gives for the asset being imported,
/// during its import (tests/projects/toolapis importer.*).</summary>
[ScriptedImporter(1, "toolapis")]
public class ToolApisImporter : ScriptedImporter
{
    [NonSerialized] public int imports;
    public static string During = "not imported";

    public override void OnImportAsset(AssetImportContext ctx)
    {
        imports++;
        var found = AssetImporter.GetAtPath(ctx.assetPath);
        During = found == null ? "null" : ReferenceEquals(found, this) ? "this" : "another " + found.GetType().Name;
        var text = new TextAsset("toolapis");
        ctx.AddObjectToAsset("main", text);
        ctx.SetMainObject(text);
    }
}
