using UnityEditor.AssetImporters;
using UnityEngine;
[ScriptedImporter(1,"hierarchyid")]
public sealed class HierarchyIdImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var main=new Texture2D(2,2) { name="main" };
        ctx.AddObjectToAsset("Main",main);
        ctx.AddObjectToAsset("Root",new GameObject("root"));
        ctx.SetMainObject(main);
    }
}
