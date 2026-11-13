using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

/// <summary>Records what the SVG importer's sprite, UI image and texture outputs are, and draws
/// the sprite outputs.</summary>
public sealed class SvgSpritesProbe : MonoBehaviour
{
    static readonly string[] Names = { "shapes", "pivot", "gradients", "frame", "strokes", "clip", "physics", "physicsTextured" };

    [Serializable] public sealed class Capture
    {
        public bool complete;
        public Asset[] assets;
        public string image;
        public SpriteId[] spriteIds;
    }

    /// <summary>A texture-imported sprite's ID (the 2D Sprite package reads it through
    /// SpriteEditorExtension.GetSpriteID).</summary>
    [Serializable] public sealed class SpriteId { public string name, id; }

    [Serializable] public sealed class Asset
    {
        public string name, main;
        public string[] objects;
        public SpriteInfo[] sprites;
        public TextureInfo[] textures;
        public ObjectInfo[] gameObjects;
    }

    [Serializable] public sealed class SpriteInfo
    {
        public string name, texture;
        public Rect rect;
        public Vector2 pivot;
        public float pixelsPerUnit;
        public Vector4 border;
        public Bounds bounds;
        public Vector2[] vertices, uv, settingIndices;
        public ushort[] triangles;
        public Color32[] colors;
        public string[] channels;
        public int physicsShapes;
        public Path[] physics;
    }

    [Serializable] public sealed class Path { public Vector2[] points; }

    [Serializable] public sealed class TextureInfo
    {
        public string name, format, filterMode, wrapMode, pixels;
        public int width, height;
    }

    [Serializable] public sealed class ObjectInfo
    {
        public string name, material, shader, sprite;
        public string[] components;
        public bool preserveAspect;
    }

    static readonly VertexAttribute[] Channels = (VertexAttribute[])Enum.GetValues(typeof(VertexAttribute));

    public void Record()
    {
        var assets = Names.Select(Describe).ToArray();
        var capture = new Capture { complete = true, assets = assets, image = Render(), spriteIds = SpriteIds() };
        File.WriteAllText(Environment.GetEnvironmentVariable("SVG_SPRITES_OUT"), JsonUtility.ToJson(capture, true));
    }

    static SpriteId[] SpriteIds()
    {
#if UNITY_EDITOR
        return new[] { "box_a", "sheet" }
            .SelectMany(n => Resources.LoadAll<Sprite>("Textures/" + n).OrderBy(s => s.name, StringComparer.Ordinal))
            .Select(s => new SpriteId { name = s.name, id = UnityEditor.U2D.SpriteEditorExtension.GetSpriteID(s).ToString() })
            .ToArray();
#else
        return Array.Empty<SpriteId>();
#endif
    }

    static Asset Describe(string name)
    {
        var all = Resources.LoadAll("Svg/" + name).OrderBy(o => o.GetType().Name, StringComparer.Ordinal)
            .ThenBy(o => o.name, StringComparer.Ordinal).ToArray();
        var main = Resources.Load("Svg/" + name);
        return new Asset
        {
            name = name,
            main = main == null ? "<null>" : main.GetType().Name + " " + main.name,
            objects = all.Select(o => o.GetType().Name + " " + o.name).ToArray(),
            sprites = all.OfType<Sprite>().Select(DescribeSprite).ToArray(),
            textures = all.OfType<Texture2D>().Select(DescribeTexture).ToArray(),
            gameObjects = all.OfType<GameObject>().Select(DescribeObject).ToArray(),
        };
    }

    static SpriteInfo DescribeSprite(Sprite s)
    {
        var info = new SpriteInfo
        {
            name = s.name, texture = s.texture != null ? s.texture.name : "<null>", rect = s.rect, pivot = s.pivot,
            pixelsPerUnit = s.pixelsPerUnit, border = s.border, bounds = s.bounds, vertices = s.vertices, uv = s.uv,
            triangles = s.triangles, physicsShapes = s.GetPhysicsShapeCount(),
            channels = Channels.Where(s.HasVertexAttribute).Select(c => c.ToString()).ToArray(),
        };
        var points = new List<Vector2>();
        info.physics = new Path[info.physicsShapes];
        for (int i = 0; i < info.physics.Length; i++) { s.GetPhysicsShape(i, points); info.physics[i] = new Path { points = points.ToArray() }; }
        if (s.HasVertexAttribute(VertexAttribute.Color)) info.colors = s.GetVertexAttribute<Color32>(VertexAttribute.Color).ToArray();
        if (s.HasVertexAttribute(VertexAttribute.TexCoord2)) info.settingIndices = s.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord2).ToArray();
        return info;
    }

    static TextureInfo DescribeTexture(Texture2D t)
    {
        var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(t, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var hex = new StringBuilder();
        foreach (var c in copy.GetPixels32()) hex.Append(c.r.ToString("x2")).Append(c.g.ToString("x2")).Append(c.b.ToString("x2")).Append(c.a.ToString("x2"));
        DestroyImmediate(copy);
        return new TextureInfo { name = t.name, width = t.width, height = t.height, format = t.format.ToString(),
            filterMode = t.filterMode.ToString(), wrapMode = t.wrapMode.ToString(), pixels = hex.ToString() };
    }

    static ObjectInfo DescribeObject(GameObject go)
    {
        var info = new ObjectInfo { name = go.name, components = go.GetComponents<Component>().Select(c => c.GetType().Name).ToArray() };
        if (go.TryGetComponent<SpriteRenderer>(out var renderer))
        {
            info.sprite = renderer.sprite != null ? renderer.sprite.name : "<null>";
            info.material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "<null>";
            info.shader = renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "<null>";
        }
        if (go.TryGetComponent<Unity.VectorGraphics.SVGImage>(out var image))
        {
            info.sprite = image.sprite != null ? image.sprite.name : "<null>";
            info.preserveAspect = image.preserveAspect;
            info.material = image.material != null ? image.material.name : "<null>";
            info.shader = image.material != null ? image.material.shader.name : "<null>";
        }
        return info;
    }

    /// <summary>The sprite outputs side by side: the vector sprites' own GameObjects (copies)
    /// and the textured sprite on a plain SpriteRenderer.</summary>
    static string Render()
    {
        var cameraObject = new GameObject("SvgCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(30, 30, 40, 255);
        camera.allowHDR = false;
        camera.allowMSAA = false;
        var made = new System.Collections.Generic.List<GameObject> { cameraObject };
        // Side by side, not overlapping (the draw order of overlapping sprites is unspecified).
        var positions = new[] { new Vector3(-2f, 0, 0), new Vector3(-1.5f, -0.3f, 0), new Vector3(0.4f, 0, 0) };
        var names = new[] { "shapes", "pivot", "gradients" };
        for (int i = 0; i < names.Length; i++)
        {
            var copy = Instantiate((GameObject)Resources.Load("Svg/" + names[i]));
            copy.transform.position = positions[i];
            made.Add(copy);
        }
        var textured = new GameObject("Textured").AddComponent<SpriteRenderer>();
        textured.sprite = Resources.LoadAll<Sprite>("Svg/strokes")[0];
        textured.transform.position = new Vector3(1.6f, 0, 0);
        made.Add(textured.gameObject);
        var rt = new RenderTexture(240, 96, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(240, 96, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 240, 96), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        var image = new StringBuilder();
        foreach (var c in texture.GetPixels32()) image.Append(c.r.ToString("x2")).Append(c.g.ToString("x2")).Append(c.b.ToString("x2"));
        foreach (var go in made) DestroyImmediate(go);
        DestroyImmediate(rt);
        return image.ToString();
    }

    void Start()
    {
        try { Record(); }
        catch (Exception e) { Debug.LogError(e); }
        Application.Quit();
    }
}
