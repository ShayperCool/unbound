using UnityEditor.AssetImporters;
using UnityEngine;
[ScriptedImporter(1,"hierarchyid")]
public sealed class HierarchyIdImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        if (ctx.assetPath.EndsWith("Deep.hierarchyid")) { ImportDeep(ctx); return; }
        var main=new Texture2D(2,2) { name="main" };
        ctx.AddObjectToAsset("Main",main);
        ctx.AddObjectToAsset("Root",new GameObject("root"));
        ctx.SetMainObject(main);
    }

    // A main GameObject with components, children, same-named siblings, a grandchild, a child that
    // is also added under its own identifier, a second added root, and a leaf asset.
    static void ImportDeep(AssetImportContext ctx)
    {
        var tree=new GameObject("Tree");
        tree.AddComponent<Rigidbody>();
        tree.AddComponent<LODGroup>();
        ctx.AddObjectToAsset("Top",tree);
        ctx.SetMainObject(tree);
        var lod0=Child(tree,"LOD0");
        lod0.AddComponent<MeshFilter>();
        lod0.AddComponent<MeshRenderer>();
        Child(lod0,"Leaf").AddComponent<BoxCollider>();
        Child(tree,"Twin");
        Child(tree,"Twin").AddComponent<SphereCollider>();
        var extra=Child(tree,"Extra");
        extra.AddComponent<CapsuleCollider>();
        ctx.AddObjectToAsset("Extra",extra);
        ctx.AddObjectToAsset("Second",new GameObject("Second"));
        ctx.AddObjectToAsset("Mesh",new Mesh { name="Mesh" });
    }

    static GameObject Child(GameObject parent,string name)
    {
        var go=new GameObject(name);
        go.transform.SetParent(parent.transform,false);
        return go;
    }
}
