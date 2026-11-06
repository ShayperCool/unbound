using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed class SpriteAssetsProbe : MonoBehaviour
{
    public Sprite sceneSprite;

    [Serializable] public sealed class Capture
    {
        public bool complete;
        public bool sceneResourceIdentity;
        public Entry[] sprites;
        public string image;
    }

    [Serializable] public sealed class Shape { public Vector2[] points; }

    [Serializable] public sealed class Entry
    {
        public string name, texture;
        public int textureWidth, textureHeight;
        public Rect rect, textureRect;
        public Vector2 pivot, textureRectOffset;
        public float pixelsPerUnit;
        public Vector4 border;
        public Bounds bounds;
        public Vector2[] vertices, uv;
        public ushort[] triangles;
        public Shape[] physics;
        public bool packed;
    }

    public void Record()
    {
        var sprites = new[] { sceneSprite, Resources.Load<Sprite>("Polygon"), Resources.Load<Sprite>("Imported") };
        var entries = sprites.Select(s =>
        {
            if (s == null) throw new Exception("Missing Sprite fixture");
            var shapes = new Shape[s.GetPhysicsShapeCount()];
            for (int i = 0; i < shapes.Length; ++i)
            {
                var points = new System.Collections.Generic.List<Vector2>();
                s.GetPhysicsShape(i, points);
                shapes[i] = new Shape { points = points.ToArray() };
            }
            return new Entry { name = s.name, texture = s.texture.name, textureWidth = s.texture.width,
                textureHeight = s.texture.height, rect = s.rect, textureRect = s.textureRect,
                pivot = s.pivot, textureRectOffset = s.textureRectOffset, pixelsPerUnit = s.pixelsPerUnit,
                border = s.border, bounds = s.bounds, vertices = s.vertices, uv = s.uv,
                triangles = s.triangles, physics = shapes, packed = s.packed };
        }).ToArray();
        var camera = GameObject.Find("Camera").GetComponent<Camera>();
        var rt = new RenderTexture(160, 96, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(160, 96, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 160, 96), 0, 0);
        texture.Apply();
        var image = new StringBuilder();
        foreach (var c in texture.GetPixels32()) image.Append(c.r.ToString("x2")).Append(c.g.ToString("x2")).Append(c.b.ToString("x2"));
        RenderTexture.active = null;
        camera.targetTexture = null;
        var capture = new Capture { complete = true, sprites = entries,
            sceneResourceIdentity = sceneSprite == Resources.Load<Sprite>("Rectangle"), image = image.ToString() };
        File.WriteAllText(Environment.GetEnvironmentVariable("SPRITE_ASSETS_OUT"), JsonUtility.ToJson(capture, true));
    }

    void Start()
    {
        try { Record(); }
        catch (Exception e) { Debug.LogError(e); }
        Application.Quit();
    }
}
