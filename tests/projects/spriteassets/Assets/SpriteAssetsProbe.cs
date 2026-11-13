using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

public sealed class SpriteAssetsProbe : MonoBehaviour
{
    public Sprite sceneSprite;
    public Sprite embeddedSprite;

    [Serializable] public sealed class Capture
    {
        public bool complete;
        public bool sceneResourceIdentity;
        public Entry[] sprites;
        public string image;
        public Layout[] layouts;
        public string layoutImage;
        public Layout[] laterLayouts;
        public string laterLayoutImage;
    }

    [Serializable] public sealed class Bone
    {
        public string name, guid;
        public Vector3 position;
        public Quaternion rotation;
        public float length;
        public int parentId;
        public Color color;
    }

    [Serializable] public sealed class Secondary { public string name, texture; }

    /// <summary>A saved Sprite layout: its Entry plus skin, vertex channels and secondary textures.</summary>
    [Serializable] public sealed class Layout
    {
        public Entry sprite;
        public Bone[] bones;
        public Matrix4x4[] bindPoses;
        public string[] channels;
        public BoneWeight[] weights;
        public Color32[] colors;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Secondary[] secondary;
        public TexCoords[] texCoords;
        public uint scriptableObjectCount;
        public Holder[] scriptableObjects;
    }

    [Serializable] public sealed class TexCoords { public string channel; public Vector2[] values; }
    [Serializable] public sealed class Holder { public string name; public int value; }

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
        var entries = sprites.Select(Describe).ToArray();
        var layoutNames = new[] { "AtlasTight", "AtlasStrip", "Skinned", "Attributes", "Downscaled", "Secondary", "VertexV2" };
        var layouts = layoutNames.Select(n => Resources.Load<Sprite>("Layouts/" + n)).Append(embeddedSprite).Select(DescribeLayout).ToArray();
        var capture = new Capture { complete = true, sprites = entries,
            sceneResourceIdentity = sceneSprite == Resources.Load<Sprite>("Rectangle"), image = Render("Camera", 160, 96),
            layouts = layouts, layoutImage = Render("LayoutCamera", 192, 128),
            laterLayouts = new[] { "Scripted", "TexCoords", "TexCoord0", "Variant", "Split" }.Select(n => Resources.Load<Sprite>("Layouts/" + n)).Select(DescribeLayout).ToArray(),
            laterLayoutImage = Render("LaterLayoutCamera", 224, 64) };
        File.WriteAllText(Environment.GetEnvironmentVariable("SPRITE_ASSETS_OUT"), JsonUtility.ToJson(capture, true));
    }

    static Entry Describe(Sprite s)
    {
        if (s == null) throw new Exception("Missing Sprite fixture");
        var shapes = new Shape[s.GetPhysicsShapeCount()];
        for (int i = 0; i < shapes.Length; ++i)
        {
            var points = new System.Collections.Generic.List<Vector2>();
            s.GetPhysicsShape(i, points);
            shapes[i] = new Shape { points = points.ToArray() };
        }
        var texture = s.texture;
        return new Entry { name = s.name, texture = texture != null ? texture.name : "<null>", textureWidth = texture != null ? texture.width : 0,
            textureHeight = texture != null ? texture.height : 0, rect = s.rect, textureRect = s.textureRect,
            pivot = s.pivot, textureRectOffset = s.textureRectOffset, pixelsPerUnit = s.pixelsPerUnit,
            border = s.border, bounds = s.bounds, vertices = s.vertices, uv = s.uv,
            triangles = s.triangles, physics = shapes, packed = s.packed };
    }

    static readonly VertexAttribute[] Channels = (VertexAttribute[])Enum.GetValues(typeof(VertexAttribute));

    static Layout DescribeLayout(Sprite s)
    {
        var layout = new Layout { sprite = Describe(s),
            bones = s.GetBones().Select(b => new Bone { name = b.name, guid = b.guid, position = b.position, rotation = b.rotation,
                length = b.length, parentId = b.parentId, color = b.color }).ToArray(),
            bindPoses = s.GetBindPoses().ToArray(),
            channels = Channels.Where(s.HasVertexAttribute).Select(c => c.ToString()).ToArray() };
        if (s.HasVertexAttribute(VertexAttribute.BlendWeight)) layout.weights = s.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight).ToArray();
        if (s.HasVertexAttribute(VertexAttribute.Color)) layout.colors = s.GetVertexAttribute<Color32>(VertexAttribute.Color).ToArray();
        if (s.HasVertexAttribute(VertexAttribute.Normal)) layout.normals = s.GetVertexAttribute<Vector3>(VertexAttribute.Normal).ToArray();
        if (s.HasVertexAttribute(VertexAttribute.Tangent)) layout.tangents = s.GetVertexAttribute<Vector4>(VertexAttribute.Tangent).ToArray();
        var secondary = new SecondarySpriteTexture[s.GetSecondaryTextureCount()];
        s.GetSecondaryTextures(secondary);
        layout.secondary = secondary.Select(t => new Secondary { name = t.name, texture = t.texture != null ? t.texture.name : "<null>" }).ToArray();
        layout.texCoords = Enumerable.Range(0, 8).Select(i => VertexAttribute.TexCoord0 + i).Where(s.HasVertexAttribute)
            .Select(c => new TexCoords { channel = c.ToString(), values = s.GetVertexAttribute<Vector2>(c).ToArray() }).ToArray();
        layout.scriptableObjectCount = s.GetScriptableObjectsCount();
        var held = new ScriptableObject[layout.scriptableObjectCount];
        s.GetScriptableObjects(held);
        layout.scriptableObjects = held.Select(h => h is SpriteAssetsHolder holder ? new Holder { name = holder.name, value = holder.value }
            : new Holder { name = h == null ? "<null>" : h.GetType().Name }).ToArray();
        return layout;
    }

    static string Render(string cameraName, int width, int height)
    {
        var camera = GameObject.Find(cameraName).GetComponent<Camera>();
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        var image = new StringBuilder();
        foreach (var c in texture.GetPixels32()) image.Append(c.r.ToString("x2")).Append(c.g.ToString("x2")).Append(c.b.ToString("x2"));
        RenderTexture.active = null;
        camera.targetTexture = null;
        return image.ToString();
    }

    void Start()
    {
        try { Record(); }
        catch (Exception e) { Debug.LogError(e); }
        Application.Quit();
    }
}
