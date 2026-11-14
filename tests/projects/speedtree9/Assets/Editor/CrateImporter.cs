using System.Globalization;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>A project ScriptedImporter whose main object is a GameObject hierarchy: the file holds
/// the crate's size. A control for scene and prefab instances of scripted imports other than
/// SpeedTree trees.</summary>
[ScriptedImporter(1, "crate")]
public sealed class CrateImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        float size = float.Parse(File.ReadAllText(ctx.assetPath).Trim(), CultureInfo.InvariantCulture);
        var mesh = new Mesh { name = "CrateMesh" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f) * size, new Vector3(0.5f, 0f, -0.5f) * size,
            new Vector3(0.5f, 0f, 0.5f) * size, new Vector3(-0.5f, 0f, 0.5f) * size,
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var material = new Material(Shader.Find("Standard")) { name = "CrateMaterial" };
        var root = new GameObject("Crate");
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(size, size * 0.5f, size);
        box.center = new Vector3(0f, size * 0.25f, 0f);
        var lid = new GameObject("Lid");
        lid.transform.SetParent(root.transform, false);
        lid.transform.localPosition = new Vector3(0f, size * 0.5f, 0f);
        var marker = root.AddComponent<CrateMarker>();
        marker.count = 3;
        marker.label = "crate";
        marker.lid = lid.transform;
        ctx.AddObjectToAsset("crate", root);
        ctx.AddObjectToAsset("mesh", mesh);
        ctx.AddObjectToAsset("material", material);
        ctx.SetMainObject(root);
    }
}
