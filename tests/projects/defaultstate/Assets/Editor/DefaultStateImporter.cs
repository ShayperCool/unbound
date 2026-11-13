using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, "defaultstate")]
public sealed class DefaultStateImporter : ScriptedImporter
{
    static bool makingNested;
    [NonSerialized] public string fieldInitializerState = NestedInitializer();
    static string NestedInitializer()
    {
        if (makingNested) return "recursion-stop";
        makingNested = true;
        try
        {
            var nested = new DefaultStateImporter();
            try { return "value:" + nested.userData; }
            catch (Exception error) { return error.GetType().FullName; }
        }
        finally { makingNested = false; }
    }
    [NonSerialized] public string constructorState;
    [NonSerialized] public string constructorMessage;
    [NonSerialized] public string nestedConstructorState;
    public DefaultStateImporter()
    {
        try { constructorState = "value:" + userData; }
        catch (Exception error) { constructorState = error.GetType().FullName; constructorMessage = error.Message; }
        var nested = new TextureImporter();
        try { nestedConstructorState = "value:" + nested.userData; }
        catch (Exception error) { nestedConstructorState = error.GetType().FullName; }
    }
    public override void OnImportAsset(AssetImportContext context)
    {
        var asset = new TextAsset("default-state import control");
        context.AddObjectToAsset("main", asset); context.SetMainObject(asset);
    }
}
