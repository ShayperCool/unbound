using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Two screens over the camera's solid background, read at the end of the frame:
/// `imgui`: GUI.DrawTexture with border widths, corner radiuses and alphaBlend off, and
/// GUIStyle borders (the default skin's box and button, a nine-sliced custom style);
/// `canvas`: canvas draws with alpha textures (CanvasRenderer.SetAlphaTexture on bare renderers
/// with UI/DefaultETC1 and UI/Default, Images of sprites with a split alpha texture, a secondary
/// `_AlphaTex` texture or plain alpha) and SpriteRenderers of the same sprites.
/// Writes values.jsonl and PNGs into UI_ALPHA_BORDERS_OUT, then exits. See README.md.</summary>
public class UiAlphaBordersProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("UiAlphaBordersProbe").AddComponent<UiAlphaBordersProbe>();

    const int Cell = 32;
    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;
    bool m_Imgui;
    int m_Repaints;
    Texture2D m_White, m_Quads, m_Steps, m_Half, m_Nine, m_Fade;

    void Value(string key, string json) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");
    static string Str(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static Texture2D Make(int width, int height, bool linear, System.Func<int, int, Color32> texel)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var texels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                texels[y * width + x] = texel(x, y);
        texture.SetPixels32(texels);
        texture.Apply();
        return texture;
    }

    static readonly Color32[] Quadrants = { new Color32(220, 40, 40, 255), new Color32(40, 200, 60, 255), new Color32(50, 80, 230, 255), new Color32(240, 240, 240, 255) };
    static readonly byte[] Steps = { 255, 170, 85, 0 };

    IEnumerator Start()
    {
        m_Dir = System.Environment.GetEnvironmentVariable("UI_ALPHA_BORDERS_OUT");
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(64, 76, 89, 255);
        camera.orthographic = true;
        camera.orthographicSize = Screen.height / 2f / Cell;
        camera.transform.position = new Vector3(0, 0, -10);

        m_White = Texture2D.whiteTexture;
        m_Quads = Make(Cell, Cell, false, (x, y) => Quadrants[(x >= 16 ? 1 : 0) + (y >= 16 ? 2 : 0)]);
        m_Steps = Make(Cell, Cell, true, (x, y) => { byte v = Steps[x / 8]; return new Color32(v, v, v, 255); });
        m_Half = Make(Cell, Cell, true, (x, y) => new Color32(128, 128, 128, 255));
        m_Fade = Make(Cell, Cell, false, (x, y) => { var c = Quadrants[(x >= 16 ? 1 : 0) + (y >= 16 ? 2 : 0)]; c.a = Steps[x / 8]; return c; });
        m_Nine = Make(9, 9, false, (x, y) => {
            bool edgeX = x < 3 || x > 5, edgeY = y < 3 || y > 5;
            return edgeX && edgeY ? new Color32(230, 40, 40, 255) : edgeX || edgeY ? new Color32(40, 210, 60, 255) : new Color32(50, 80, 230, 255);
        });

        m_Imgui = true;
        yield return Capture("imgui");
        Value("imgui.repaints", m_Repaints.ToString());
        m_Imgui = false;

        var first = Canvases();
        yield return Capture("canvas");
        Destroy(first.gameObject);
        yield return null;
        Batches();
        yield return Capture("batches");

        Value("complete", "true");
        int code = 0;
        if (string.IsNullOrEmpty(m_Dir)) { Debug.LogError("UI_ALPHA_BORDERS_OUT is not set"); code = 1; }
        else File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    void Update() => m_Repaints = 0;

    void OnGUI()
    {
        if (!m_Imgui || Event.current.type != EventType.Repaint) return;
        m_Repaints++;
        var white = Color.white;
        var fill = ScaleMode.StretchToFill;
        // Row 1: shapes.
        GUI.DrawTexture(new Rect(5, 5, 40, 40), m_White, fill, true, 0, white, 0, 10);
        GUI.DrawTexture(new Rect(50, 5, 40, 40), m_White, fill, true, 0, new Color(1f, .5f, 0f, 1f), 3, 0);
        GUI.DrawTexture(new Rect(95, 5, 40, 40), m_White, fill, true, 0, white, new Vector4(1, 2, 4, 8), 0);
        GUI.DrawTexture(new Rect(140, 5, 40, 40), m_White, fill, true, 0, white, new Vector4(4, 4, 4, 4), new Vector4(0, 4, 8, 16));
        GUI.DrawTexture(new Rect(185, 5, 40, 20), m_White, fill, true, 0, white, 0, 30);
        GUI.DrawTexture(new Rect(230, 5, 40, 40), m_Quads, fill, true, 0, white, 6, 8);
        GUI.DrawTexture(new Rect(275, 5, 40, 40), m_Quads, ScaleMode.ScaleToFit, true, 2, white, 0, 12);
        // Row 2: blending off, styles.
        GUI.DrawTexture(new Rect(5, 55, 40, 40), m_White, fill, false, 0, new Color(1f, 1f, 1f, .5f), 0, 0);
        GUI.DrawTexture(new Rect(50, 55, 40, 40), m_Fade, fill, false, 0, white, 0, 0);
        GUI.DrawTexture(new Rect(95, 55, 40, 40), m_Fade, fill, true, 0, white, 0, 0);
        GUI.DrawTexture(new Rect(140, 55, 40, 40), m_Fade, fill, false, 0, white, 0, 8);
        GUI.Box(new Rect(185, 55, 40, 40), GUIContent.none);
        GUI.Button(new Rect(230, 55, 40, 40), GUIContent.none);
        var nine = new GUIStyle { border = new RectOffset(3, 3, 3, 3) };
        nine.normal.background = m_Nine;
        nine.Draw(new Rect(275, 55, 40, 30), false, false, false, false);
        var plain = new GUIStyle();
        plain.normal.background = m_Nine;
        plain.Draw(new Rect(275, 88, 40, 10), false, false, false, false);
        // Row 3: over an opaque base.
        for (int i = 0; i < 4; i++)
            GUI.DrawTexture(new Rect(5 + 60 * i, 105, 55, 55), m_White, fill, true, 0, new Color(.2f, .3f, .9f, 1f), 0, 0);
        GUI.DrawTexture(new Rect(10, 110, 45, 45), m_White, fill, true, 0, new Color(1f, 1f, 1f, .5f), 0, 15);
        GUI.DrawTexture(new Rect(70, 110, 45, 45), m_White, fill, false, 0, new Color(1f, 1f, 1f, .5f), 0, 15);
        GUI.DrawTexture(new Rect(130, 110, 45, 45), m_White, fill, true, 0, new Color(1f, 1f, 1f, .5f), 5, 10);
        GUI.DrawTexture(new Rect(190, 110, 45, 45), m_Fade, fill, true, 0, white, new Vector4(2, 6, 2, 6), new Vector4(10, 0, 10, 0));
        // DrawTextureWithTexCoords with alphaBlend off and a translucent tint; negative widths and radiuses.
        var tint = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, .5f);
        GUI.DrawTextureWithTexCoords(new Rect(250, 110, 30, 20), m_Fade, new Rect(0, 0, 1, 1), false);
        GUI.color = tint;
        GUI.DrawTexture(new Rect(285, 110, 30, 20), m_White, fill, true, 0, white, -2, 0);
        GUI.DrawTexture(new Rect(250, 135, 30, 20), m_White, fill, true, 0, white, 0, -5);
        GUI.DrawTexture(new Rect(285, 135, 30, 20), m_White, fill, true, 0, white, new Vector4(-1, 3, 0, 0), new Vector4(-4, 0, 6, 0));
    }

    /// <summary>A RectTransform child of <paramref name="canvas"/> at a pixel position from the
    /// screen's bottom-left corner.</summary>
    static RectTransform Place(Canvas canvas, string name, float x, float y, System.Type component = null)
    {
        var go = component == null ? new GameObject(name, typeof(RectTransform)) : new GameObject(name, typeof(RectTransform), component);
        var rect = (RectTransform)go.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(Cell, Cell);
        return rect;
    }

    static Mesh Quad()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(0, 0), new Vector3(0, Cell), new Vector3(Cell, Cell), new Vector3(Cell, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
        mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
        return mesh;
    }

    /// <summary>Renderers sharing one material and texture with different alpha textures: beside
    /// another draw that does not overlap them (row 140) and one that overlaps both (row 100),
    /// under one RectMask2D (row 60, inside the mask) and the ETC1 canvas material.</summary>
    void Batches()
    {
        var canvas = new GameObject("BatchCanvas", typeof(RectTransform)).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var etc1 = Canvas.GetETC1SupportedCanvasMaterial();
        Value("etc1Material", etc1 == null ? "null" : "[" + Str(etc1.name) + "," + Str(etc1.shader != null ? etc1.shader.name : "null") + "]");
        Value("etc1Material.same", (etc1 == Canvas.GetETC1SupportedCanvasMaterial()).ToString().ToLowerInvariant());
        Value("etc1Material.isDefault", (etc1 == Canvas.GetDefaultCanvasMaterial()).ToString().ToLowerInvariant());
        var shader = Shader.Find("UI/DefaultETC1");
        var plain = new Material(Shader.Find("UI/Default"));
        var apart = new Material(shader);
        Bare(canvas, "apart.steps", 0, 140, apart, m_Steps);
        Bare(canvas, "apart.other", 1, 140, plain, null);
        Bare(canvas, "apart.half", 2, 140, apart, m_Half);
        var over = new Material(shader);
        Bare(canvas, "over.steps", 0, 100, over, m_Steps);
        var cover = Bare(canvas, "over.other", 0, 100, new Material(plain), null);
        ((RectTransform)cover.transform).anchoredPosition = new Vector2(20, 100);
        ((RectTransform)cover.transform).sizeDelta = new Vector2(Cell + 40, Cell);
        var overHalf = Bare(canvas, "over.half", 1, 100, over, m_Half);
        ((RectTransform)overHalf.transform).anchoredPosition = new Vector2(44 + 40, 100);
        var maskRect = Place(canvas, "mask", 0, 60, typeof(RectMask2D));
        maskRect.sizeDelta = new Vector2(320, Cell);
        var masked = new Material(shader);
        foreach (var (name, column, alpha) in new[] { ("mask.steps", 0, m_Steps), ("mask.half", 1, m_Half) })
        {
            var r = Bare(canvas, name, column, 60, masked, alpha);
            r.transform.SetParent(maskRect, true);
        }
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>A bare CanvasRenderer drawing the quadrant texture with <paramref name="material"/>.</summary>
    CanvasRenderer Bare(Canvas canvas, string name, int column, float y, Material material, Texture alpha)
    {
        var renderer = Place(canvas, name, 4 + 40 * column, y).gameObject.AddComponent<CanvasRenderer>();
        renderer.SetMesh(Quad());
        renderer.materialCount = 1;
        renderer.SetMaterial(material, 0);
        renderer.SetTexture(m_Quads);
        if (alpha != null) renderer.SetAlphaTexture(alpha);
        return renderer;
    }

    void RecordSprite(string key, Sprite sprite)
    {
        if (sprite == null) { Value(key, "null"); return; }
        var split = sprite.associatedAlphaSplitTexture;
        Value(key + ".texture", "[" + Str(sprite.texture.name) + "," + Str(sprite.texture.format.ToString()) + "]");
        Value(key + ".associatedAlphaSplitTexture", split == null ? "null"
            : "[" + Str(split.name) + "," + split.width + "," + split.height + "," + Str(split.format.ToString()) + "]");
        var secondary = new SecondarySpriteTexture[sprite.GetSecondaryTextureCount()];
        sprite.GetSecondaryTextures(secondary);
        var names = new StringBuilder("[");
        foreach (var s in secondary) names.Append(names.Length > 1 ? "," : "").Append(Str(s.name + ":" + (s.texture != null ? s.texture.name : "null")));
        Value(key + ".secondary", names.Append("]").ToString());
    }

    Canvas Canvases()
    {
        var canvas = new GameObject("Canvas", typeof(RectTransform)).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var etc1Shader = Shader.Find("UI/DefaultETC1");
        Value("shader.UI/DefaultETC1", etc1Shader == null ? "null" : Str(etc1Shader.name));
        // Each bare renderer has a material of its own, so no two of them can share a batch.
        Material Etc1() => new Material(etc1Shader);
        Material Etc1WithOwn()
        {
            var material = new Material(etc1Shader);
            material.SetTexture("_AlphaTex", m_Half);
            return material;
        }
        var plain = new Material(Shader.Find("UI/Default"));

        // Row at y 140: bare renderers.
        Bare(canvas, "etc1.alpha", 0, 140, Etc1(), m_Steps);
        Bare(canvas, "default.alpha", 1, 140, new Material(plain), m_Steps);
        Bare(canvas, "etc1.none", 2, 140, Etc1(), null);
        Bare(canvas, "etc1.own.alpha", 3, 140, Etc1WithOwn(), m_Steps);
        Bare(canvas, "etc1.own.none", 4, 140, Etc1WithOwn(), null);
        var clearedMaterial = Etc1();
        var cleared = Bare(canvas, "etc1.cleared", 5, 140, clearedMaterial, m_Steps);
        cleared.Clear();
        cleared.SetMesh(Quad());
        cleared.materialCount = 1;
        cleared.SetMaterial(clearedMaterial, 0);
        cleared.SetTexture(m_Quads);
        var replaced = Bare(canvas, "etc1.replaced", 6, 140, Etc1(), m_Half);
        replaced.SetAlphaTexture(m_Steps);
        var raw = Place(canvas, "raw.etc1", 4 + 40 * 7, 140, typeof(RawImage)).GetComponent<RawImage>();
        raw.texture = m_Quads;
        raw.material = Etc1();
        raw.canvasRenderer.SetAlphaTexture(m_Steps);
        // Row at y 20: renderers sharing one material: alpha textures steps then half, and steps
        // then none (whether a shared batch keeps each renderer's alpha texture).
        var shared = Etc1();
        Bare(canvas, "shared.steps", 0, 20, shared, m_Steps);
        Bare(canvas, "shared.half", 1, 20, shared, m_Half);
        var sharedNone = Etc1();
        Bare(canvas, "shared2.steps", 3, 20, sharedNone, m_Steps);
        Bare(canvas, "shared2.none", 4, 20, sharedNone, null);

        // Row at y 100: Images of the sprites with their default material, and one with UI/Default.
        var split = Resources.Load<Sprite>("SplitSprite");
        var secondary = Resources.Load<Sprite>("SecondarySprite");
        var plainSprite = Resources.Load<Sprite>("PlainSprite");
        var splitSrgb = Resources.Load<Sprite>("SplitSrgbSprite");
        RecordSprite("sprite.splitSrgb", splitSrgb);
        RecordSprite("sprite.split", split);
        RecordSprite("sprite.secondary", secondary);
        RecordSprite("sprite.plain", plainSprite);
        var sprites = new[] { ("split", split, (Material)null), ("secondary", secondary, null), ("plain", plainSprite, null), ("split.default", split, plain), ("splitSrgb", splitSrgb, null) };
        for (int i = 0; i < sprites.Length; i++)
        {
            var image = Place(canvas, "image." + sprites[i].Item1, 4 + 40 * i, 100, typeof(Image)).GetComponent<Image>();
            image.sprite = sprites[i].Item2;
            if (sprites[i].Item3 != null) image.material = sprites[i].Item3;
            Value("image." + sprites[i].Item1 + ".materialForRendering", Str(image.materialForRendering != null ? image.materialForRendering.shader.name : null));
        }

        // Row at y 60: SpriteRenderers of the same sprites (centred on their cells).
        var renderers = new[] { ("split", split), ("secondary", secondary), ("plain", plainSprite), ("splitSrgb", splitSrgb) };
        for (int i = 0; i < renderers.Length; i++)
        {
            var go = new GameObject("sprite." + renderers[i].Item1);
            float px = 4 + 40 * i + Cell / 2f, py = 60 + Cell / 2f;
            go.transform.position = new Vector3((px - Screen.width / 2f) / Cell, (py - Screen.height / 2f) / Cell, 0);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = renderers[i].Item2;
            Value("spriterenderer." + renderers[i].Item1 + ".material", Str(renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : null));
        }
        return canvas;
    }

    /// <summary>Renders a few frames, then records the last at its end.</summary>
    IEnumerator Capture(string name)
    {
        for (int i = 0; i < 3; i++) yield return null;
        yield return new WaitForEndOfFrame();
        Value(name + ".screen", "[" + Screen.width + "," + Screen.height + "]");
        if (string.IsNullOrEmpty(m_Dir)) yield break;
        var read = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        read.Apply();
        Directory.CreateDirectory(m_Dir);
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), read.EncodeToPNG());
        Destroy(read);
    }
}
