using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Which canvas renderers CanvasRenderer.cullTransparentMesh leaves out, over the renderer's alpha,
// its vertex alpha and a CanvasGroup's alpha; and a reverse mask (a child that inverts its Mask's
// stencil, drawn with a renderer alpha just above zero) that depends on that rule. Each cell is
// one Image: Probe/Solid draws it opaque red when it is drawn at all, so the cell's centre pixel
// says whether the canvas drew it. Writes values.jsonl to UI_CULL_OUT.
public class TransparentCullProbe : MonoBehaviour
{
    const int Size = 256, Cell = 8, Columns = Size / Cell;
    readonly StringBuilder m_Records = new StringBuilder();
    readonly List<(string key, int x, int y)> m_Cells = new List<(string, int, int)>();
    RectTransform m_Root;
    Material m_Solid;
    int m_Next;

    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    void Value(string key, string value) => m_Records.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    // Renderer and group alphas around zero, around the vertex byte's half step (0.5/255) and its
    // first step (1/255), and ordinary values.
    static readonly float[] Alphas =
    {
        0f, 1e-7f, 1e-4f, 9e-4f, 1e-3f, 1.1e-3f, 1.9e-3f, 1.96e-3f, 1.97e-3f, 2e-3f, 2.5e-3f, 3e-3f,
        3.9e-3f, 4e-3f, 5e-3f, 1e-2f, 2e-2f, 0.1f, 0.5f, 1f,
    };

    // One-cell renderers fill rows from the bottom; 2x2-cell areas fill rows from the top.
    int m_NextArea;

    (int x, int y) Place(string key)
    {
        int x = m_Next % Columns, y = m_Next / Columns;
        m_Next++;
        m_Cells.Add((key, x * Cell + Cell / 2, y * Cell + Cell / 2));
        return (x, y);
    }

    (int x, int y) PlaceArea(string key)
    {
        int x = m_NextArea % (Columns / 2) * 2, y = Columns - 2 - m_NextArea / (Columns / 2) * 2;
        m_NextArea++;
        m_Cells.Add((key, x * Cell + Cell, y * Cell + Cell));
        return (x, y);
    }

    RectTransform Rect(string name, Transform parent, int px, int py, int size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(px, py);
        rt.sizeDelta = new Vector2(size, size);
        return rt;
    }

    Image SolidImage(string key, Transform parent, Color color)
    {
        var (x, y) = Place(key);
        var image = Rect(key, parent, x * Cell + 1, y * Cell + 1, Cell - 2).gameObject.AddComponent<Image>();
        image.material = m_Solid;
        image.color = color;
        image.canvasRenderer.cullTransparentMesh = true;
        return image;
    }

    readonly List<(CanvasRenderer renderer, float alpha)> m_RendererAlphas = new List<(CanvasRenderer, float)>();

    void Build()
    {
        foreach (var a in Alphas)
            m_RendererAlphas.Add((SolidImage("renderer." + F(a), m_Root, Color.white).canvasRenderer, a));
        foreach (var b in new[] { 0, 1, 2, 3, 128, 255 })
            SolidImage("vertex." + b, m_Root, new Color32(255, 255, 255, (byte)b));
        foreach (var a in Alphas)
        {
            var group = Rect("group", m_Root, 0, 0, Size).gameObject.AddComponent<CanvasGroup>();
            group.alpha = a;
            SolidImage("group." + F(a), group.transform, Color.white);
        }
        // The vertex byte times the renderer alpha: the product's byte rounds to 1 or to 0.
        foreach (var (b, a) in new[] { (128, 4e-3f), (128, 3e-3f), (1, 0.6f), (1, 0.4f), (2, 0.5f), (255, 1.96e-3f), (255, 1.97e-3f) })
            m_RendererAlphas.Add((SolidImage("vertexRenderer." + b + "." + F(a), m_Root, new Color32(255, 255, 255, (byte)b)).canvasRenderer, a));
        // The flag off: renderer alpha 0 against 1e-3, and transparent vertices.
        foreach (var a in new[] { 0f, 1e-3f })
        {
            var image = SolidImage("noCull." + F(a), m_Root, Color.white);
            image.canvasRenderer.cullTransparentMesh = false;
            m_RendererAlphas.Add((image.canvasRenderer, a));
        }
        {
            var image = SolidImage("noCull.vertex0", m_Root, new Color32(255, 255, 255, 0));
            image.canvasRenderer.cullTransparentMesh = false;
        }
        // A negative renderer alpha, with and without the flag.
        foreach (var cull in new[] { true, false })
        {
            var image = SolidImage((cull ? "renderer" : "noCull") + ".-0.1", m_Root, Color.white);
            image.canvasRenderer.cullTransparentMesh = cull;
            m_RendererAlphas.Add((image.canvasRenderer, -0.1f));
        }
        // A CanvasGroup at zero without the flag.
        foreach (var a in new[] { 0f, 1e-3f })
        {
            var group = Rect("group", m_Root, 0, 0, Size).gameObject.AddComponent<CanvasGroup>();
            group.alpha = a;
            SolidImage("noCullGroup." + F(a), group.transform, Color.white).canvasRenderer.cullTransparentMesh = false;
        }
        // A Mask whose own renderer alpha is zero, with and without the flag: a Mask that is not
        // drawn writes no stencil, so its (opaque, red UI/Default) child is not drawn either.
        foreach (var cull in new[] { true, false })
        foreach (var a in new[] { 0f, 1f })
        {
            string key = "maskAlpha." + (cull ? "cull" : "noCull") + "." + F(a);
            var (x, y) = PlaceArea(key);
            var area = Rect(key, m_Root, x * Cell, y * Cell, Cell * 2);
            var mask = area.gameObject.AddComponent<Image>();
            area.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            mask.canvasRenderer.cullTransparentMesh = cull;
            m_RendererAlphas.Add((mask.canvasRenderer, a));
            Rect("child", area, 0, 0, Cell * 2).gameObject.AddComponent<Image>().color = Color.red;
        }
        // Reverse masks: a Mask over a 2x2-cell area, a child hole that inverts the stencil where
        // it draws (UI/Default with alpha clip, writing no colour, renderer alpha `a`) and a
        // maskable blue screen after it. A hole that is drawn leaves the clear colour.
        var holeShader = Shader.Find("UI/Default");
        foreach (var a in new[] { 0f, 1e-3f, 1.96e-3f, 2e-3f, 4e-3f, 1f })
        {
            string key = "reverseMask." + F(a);
            var (x, y) = PlaceArea(key);
            var area = Rect(key, m_Root, x * Cell, y * Cell, Cell * 2);
            var mask = area.gameObject.AddComponent<Image>();
            mask.color = new Color(1f, 1f, 1f, 0.392f);
            area.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var hole = Rect("hole", area, 0, 0, Cell * 2).gameObject.AddComponent<Image>();
            hole.maskable = false;
            var holeMaterial = new Material(holeShader);
            holeMaterial.SetFloat("_Stencil", 1);
            holeMaterial.SetFloat("_StencilOp", (float)StencilOp.Invert);
            holeMaterial.SetFloat("_StencilComp", (float)CompareFunction.Equal);
            holeMaterial.SetFloat("_StencilReadMask", 1);
            holeMaterial.SetFloat("_StencilWriteMask", 255);
            holeMaterial.SetFloat("_ColorMask", 0);
            holeMaterial.SetFloat("_UseUIAlphaClip", 1);
            holeMaterial.EnableKeyword("UNITY_UI_ALPHACLIP");
            hole.material = holeMaterial;
            hole.canvasRenderer.cullTransparentMesh = true;
            m_RendererAlphas.Add((hole.canvasRenderer, a));
            var screen = Rect("screen", area, 0, 0, Cell * 2).gameObject.AddComponent<Image>();
            screen.color = new Color(0f, 0f, 1f, 1f);
        }
    }

    IEnumerator Start()
    {
        yield return null;
        var directory = Environment.GetEnvironmentVariable("UI_CULL_OUT") ?? "/tmp/ui-cull";
        Directory.CreateDirectory(directory);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = Size / 2;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        camera.targetTexture = target;
        m_Solid = new Material(Shader.Find("Probe/Solid"));
        var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        m_Root = (RectTransform)canvasObject.transform;
        Build();
        // A renderer's alpha is set after its Graphic has built its mesh, as a script does each frame.
        yield return null;
        foreach (var (renderer, alpha) in m_RendererAlphas) renderer.SetAlpha(alpha);
        yield return null;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        foreach (var (key, x, y) in m_Cells)
        {
            Color32 p = image.GetPixel(x, y);
            Value(key, "[" + p.r + "," + p.g + "," + p.b + "," + p.a + "]");
        }
        // The same canvas as Screen Space Overlay, drawn over the screen at the end of a frame.
        camera.targetTexture = null;
        camera.enabled = false;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_Root.localScale = Vector3.one;
        var clear = new GameObject("Clear").AddComponent<Camera>();
        clear.clearFlags = CameraClearFlags.SolidColor;
        clear.backgroundColor = Color.clear;
        clear.cullingMask = 0;
        Value("overlay.screen", "\"" + Screen.width + "x" + Screen.height + "\"");
        for (int frame = 0; frame < 3; frame++) yield return null;
        yield return new WaitForEndOfFrame();
        var screen = ScreenCapture.CaptureScreenshotAsTexture();
        foreach (var (key, x, y) in m_Cells)
        {
            if (x >= screen.width || y >= screen.height) { Value("overlay." + key, "\"outside\""); continue; }
            Color32 p = screen.GetPixel(x, y);
            Value("overlay." + key, "[" + p.r + "," + p.g + "," + p.b + "," + p.a + "]");
        }
        foreach (var (renderer, alpha) in m_RendererAlphas)
            if (renderer.GetAlpha() != alpha) Value("alphaChanged." + renderer.name, F(renderer.GetAlpha()));
        Value("complete", "true");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), m_Records.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
