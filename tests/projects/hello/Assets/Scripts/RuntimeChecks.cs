using UnityEngine;

/// <summary>Checks native-backed features at start, for the fixture test: a texture through PNG
/// bytes and back, and a model asset loaded as its prefab.</summary>
static class RuntimeChecks
{
    [RuntimeInitializeOnLoadMethod]
    static void Run()
    {
        var texture = new Texture2D(3, 2, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.red);
        texture.SetPixel(2, 1, Color.blue);
        texture.Apply();
        var png = texture.EncodeToPNG();
        var loaded = new Texture2D(1, 1);
        bool ok = loaded.LoadImage(png);
        Debug.Log($"image {ok} {loaded.width}x{loaded.height} {loaded.GetPixel(0, 0)} {loaded.GetPixel(2, 1)} {loaded.format}");

        // A model asset loads as its prefab, with its mesh.
        var model = Resources.Load<GameObject>("Quad");
        var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
        var mesh = filter != null ? filter.sharedMesh : null;
        Debug.Log(mesh != null
            ? $"model {model.name} {mesh.vertexCount} vertices {mesh.triangles.Length / 3} triangles {mesh.bounds.size}"
            : "model missing");
    }
}
