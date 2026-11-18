using System.Collections.Generic;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Adds objects of several kinds to its asset and records each one's hideFlags as it adds
/// them (tests/projects/toolapis imported.*): a TextAsset made the main object after the others, a
/// Material, a Mesh, a Texture2D, one with HideInHierarchy set before it is added, a Sprite of it, a
/// ScriptableObject, and a GameObject with a child and components (added as one object).</summary>
[ScriptedImporter(1, "toolapisobjects")]
public class ToolApisObjectsImporter : ScriptedImporter
{
    public static readonly List<string> During = new List<string>();

    public override void OnImportAsset(AssetImportContext ctx)
    {
        During.Clear();
        void Add(string id, Object o)
        {
            ctx.AddObjectToAsset(id, o);
            During.Add($"{id} {(int)o.hideFlags}");
        }
        var material = new Material(Shader.Find("Sprites/Default")) { name = "ImportedMaterial" };
        Add("material", material);
        var mesh = new Mesh { name = "ImportedMesh" };
        mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
        mesh.triangles = new[] { 0, 1, 2 };
        Add("mesh", mesh);
        var texture = new Texture2D(4, 4) { name = "ImportedTexture" };
        Add("texture", texture);
        var hidden = new Texture2D(2, 2) { name = "HiddenTexture", hideFlags = HideFlags.HideInHierarchy };
        Add("hidden", hidden);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        sprite.name = "ImportedSprite";
        Add("sprite", sprite);
        var data = ScriptableObject.CreateInstance<ToolApisData>();
        data.name = "ImportedData";
        Add("data", data);
        var root = new GameObject("ImportedRoot");
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        var child = new GameObject("ImportedChild");
        child.transform.SetParent(root.transform, false);
        child.AddComponent<SpriteRenderer>();
        Add("root", root);
        During.Add($"child {(int)child.hideFlags} transform {(int)root.transform.hideFlags} filter {(int)root.GetComponent<MeshFilter>().hideFlags}");
        var text = new TextAsset("objects") { name = "ImportedText" };
        Add("text", text);
        ctx.SetMainObject(text);
        During.Add($"main set {(int)text.hideFlags}");
    }
}
