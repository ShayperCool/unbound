using UnityEngine;

/// <summary>Round-trips a texture through PNG bytes at start, for the fixture test.</summary>
static class ImageCheck
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
    }
}
