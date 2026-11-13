using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>Records, in Play mode, every sprite of the project as its atlas packs it, the atlases'
/// clones and pages, late binding through SpriteAtlasManager, and a render.</summary>
public sealed class SpriteAtlasProbe : MonoBehaviour
{
    public Sprite[] sprites;
    public SpriteAtlas[] atlases;
    public SpriteAtlas lateAtlas;

    [Serializable] public sealed class Capture
    {
        public bool complete;
        public Entry[] sprites;
        public Entry[] lateBefore;
        public Entry[] lateAfter;
        public AtlasEntry[] atlases;
        public PageEntry[] pages;
        public string[] events;
        public string image;
    }

    [Serializable] public sealed class Entry
    {
        public string name, texture, textureRect;
        public int textureWidth, textureHeight;
        public bool packed;
        public string packingMode, packingRotation;
        public Rect rect;
        public Vector2 pivot, textureRectOffset;
        public float pixelsPerUnit, atlasScale;
        public Bounds bounds;
        public Vector2[] vertices, uv;
        public ushort[] triangles;
        public Vector4 outerUV, innerUV, padding;
    }

    [Serializable] public sealed class AtlasEntry
    {
        public string name, tag;
        public bool isVariant;
        public int spriteCount;
        public Entry[] clones;
        public string byName;
        public bool canBindFirst;
    }

    [Serializable] public sealed class PageEntry
    {
        public string name, format, graphicsFormat, filterMode, wrapMode;
        public int width, height, mipmapCount, anisoLevel;
        public bool readable;
        public string texels;
    }

    static readonly List<string> s_Events = new List<string>();
    static SpriteAtlasProbe s_Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Listen()
    {
        SpriteAtlasManager.atlasRequested += (tag, register) =>
        {
            s_Events.Add($"requested {tag} frame {Time.frameCount}");
            if (s_Instance != null && s_Instance.lateAtlas != null && s_Instance.lateAtlas.tag == tag) register(s_Instance.lateAtlas);
            else s_Events.Add($"not given {tag}");
        };
        SpriteAtlasManager.atlasRegistered += atlas => s_Events.Add($"registered {atlas.name} frame {Time.frameCount}");
    }

    void Awake() => s_Instance = this;

    static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static Entry Describe(Sprite s)
    {
        var t = s.texture;
        string textureRect;
        try { var r = s.textureRect; textureRect = $"{F(r.x)},{F(r.y)},{F(r.width)},{F(r.height)}"; }
        catch (Exception e) { textureRect = "throws " + e.GetType().Name; }
        return new Entry
        {
            name = s.name, texture = t != null ? t.name : "<null>", textureWidth = t != null ? t.width : 0, textureHeight = t != null ? t.height : 0,
            textureRect = textureRect, packed = s.packed, packingMode = s.packed ? s.packingMode.ToString() : "-",
            packingRotation = s.packed ? s.packingRotation.ToString() : "-", rect = s.rect, pivot = s.pivot,
            textureRectOffset = s.textureRectOffset, pixelsPerUnit = s.pixelsPerUnit, atlasScale = s.spriteAtlasTextureScale,
            bounds = s.bounds, vertices = s.vertices, uv = s.uv, triangles = s.triangles,
            outerUV = UnityEngine.Sprites.DataUtility.GetOuterUV(s), innerUV = UnityEngine.Sprites.DataUtility.GetInnerUV(s),
            padding = UnityEngine.Sprites.DataUtility.GetPadding(s),
        };
    }

    static PageEntry Page(Texture2D t)
    {
        var page = new PageEntry
        {
            name = t.name, width = t.width, height = t.height, format = t.format.ToString(), graphicsFormat = t.graphicsFormat.ToString(),
            mipmapCount = t.mipmapCount, filterMode = t.filterMode.ToString(), wrapMode = t.wrapMode.ToString(), anisoLevel = t.anisoLevel,
            readable = t.isReadable,
        };
        if (t.isReadable)
        {
            var texels = t.GetPixels32(0);
            var bytes = new byte[texels.Length * 4];
            for (int i = 0; i < texels.Length; i++)
            {
                bytes[i * 4] = texels[i].r; bytes[i * 4 + 1] = texels[i].g; bytes[i * 4 + 2] = texels[i].b; bytes[i * 4 + 3] = texels[i].a;
            }
            page.texels = Convert.ToBase64String(bytes);
        }
        return page;
    }

    IEnumerator Start()
    {
        var capture = new Capture();
        try
        {
            capture.sprites = sprites.Select(Describe).ToArray();
            capture.lateBefore = sprites.Where(s => s.name.StartsWith("Late") || s.name.StartsWith("Hd")).Select(Describe).ToArray();
            var pages = new Dictionary<string, Texture2D>();
            void Note(Texture2D t) { if (t != null && t.name.StartsWith("sactx")) pages[t.name] = t; }
            foreach (var s in sprites) Note(s.texture);
            var described = new List<AtlasEntry>();
            foreach (var atlas in atlases)
            {
                var clones = new Sprite[atlas.spriteCount];
                atlas.GetSprites(clones);
                foreach (var c in clones) Note(c.texture);
                var first = clones.Length > 0 ? clones[0].name.Replace("(Clone)", "") : null;
                var byName = first != null ? atlas.GetSprite(first) : null;
                described.Add(new AtlasEntry
                {
                    name = atlas.name, tag = atlas.tag, isVariant = atlas.isVariant, spriteCount = atlas.spriteCount,
                    clones = clones.Select(Describe).ToArray(), byName = byName != null ? byName.name + "@" + (byName.texture != null ? byName.texture.name : "<null>") : "<none>",
                    canBindFirst = first != null && atlas.CanBindTo(sprites.FirstOrDefault(s => s.name == first)),
                });
            }
            capture.atlases = described.ToArray();
            capture.pages = pages.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => Page(pages[k])).ToArray();
        }
        catch (Exception e) { Debug.LogException(e); }
        // Late binding: the request comes at a later frame's SpriteAtlasManagerUpdate.
        for (int i = 0; i < 3; i++) yield return null;
        try
        {
            capture.lateAfter = sprites.Where(s => s.name.StartsWith("Late") || s.name.StartsWith("Hd")).Select(Describe).ToArray();
            capture.image = Render(160, 96);
            capture.events = s_Events.ToArray();
            capture.complete = true;
            File.WriteAllText(Environment.GetEnvironmentVariable("SPRITE_ATLAS_OUT"), JsonUtility.ToJson(capture, true));
        }
        catch (Exception e) { Debug.LogException(e); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(capture.complete ? 0 : 1);
#else
        Application.Quit();
#endif
    }

    static string Render(int width, int height)
    {
        var camera = GameObject.Find("Camera").GetComponent<Camera>();
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
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
}
