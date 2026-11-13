using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
[ScriptedImporter(1,"identityprobe")]
public sealed class IdentityImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var main=new Texture2D(2,2) { name="main" };
        var sub=new Texture2D(1,1);
        var light=new LightmapParameters { name="" };
        ctx.AddObjectToAsset("Main",main);
        ctx.AddObjectToAsset("BlankTexture",sub);
        ctx.AddObjectToAsset("Lighting",light);
        ctx.SetMainObject(main);
    }
}
