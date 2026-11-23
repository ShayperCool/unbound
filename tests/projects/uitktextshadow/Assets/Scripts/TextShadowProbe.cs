using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Labels with text-shadow and -unity-text-outline on the runtime panel of the uitk fixture, one
/// case per cell, rendered at panel scale 1 and 1.25 (shadow-1.png, shadow-1.25.png), with each
/// label's resolved outline and layout (shadow.jsonl). Synchronous, as the fixture: panels are
/// updated and rendered through UIElementsRuntimeUtility.
/// </summary>
public static class TextShadowProbe
{
    static string Output => Environment.GetEnvironmentVariable("UITK_SHADOW_OUT");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const int Columns = 3, CellWidth = 200, CellHeight = 96;

    struct Case
    {
        public string name;
        public Action<IStyle> style;
        public Case(string name, Action<IStyle> style) { this.name = name; this.style = style; }
    }

    static TextShadow Shadow(float x, float y, float blur, Color color) =>
        new TextShadow { offset = new Vector2(x, y), blurRadius = blur, color = color };

    static readonly Case[] Cases =
    {
        // Controls: no shadow, an outline colour without width.
        new Case("plain", s => { }),
        new Case("outline-no-width", s => { s.unityTextOutlineColor = Color.red; s.unityTextOutlineWidth = 0f; }),
        new Case("shadow", s => s.textShadow = Shadow(3f, 4f, 0f, new Color(0.9f, 0.1f, 0.1f, 1f))),
        new Case("shadow-blur", s => s.textShadow = Shadow(2.6f, 3.9f, 5.3f, new Color(43f / 255f, 38f / 255f, 38f / 255f, 0.78f))),
        new Case("shadow-negative", s => s.textShadow = Shadow(-4f, -3f, 2f, new Color(0.1f, 0.8f, 0.2f, 1f))),
        new Case("shadow-far", s => s.textShadow = Shadow(30f, 30f, 0f, new Color(0.1f, 0.2f, 0.9f, 1f))),
        new Case("shadow-wide-blur", s => s.textShadow = Shadow(0f, 0f, 40f, Color.black)),
        new Case("outline", s => { s.unityTextOutlineColor = new Color(0.1f, 0.2f, 0.9f, 1f); s.unityTextOutlineWidth = 2f; }),
        new Case("outline-shadow", s => { s.unityTextOutlineColor = Color.black; s.unityTextOutlineWidth = 1.5f; s.textShadow = Shadow(3f, 3f, 1f, new Color(0.9f, 0.1f, 0.1f, 1f)); }),
        new Case("face-transparent", s => { s.color = new Color(1f, 1f, 1f, 0f); s.textShadow = Shadow(3f, 4f, 2f, Color.black); }),
        new Case("small", s => { s.fontSize = 14f; s.textShadow = Shadow(2f, 2f, 1f, Color.black); }),
        new Case("bold", s => { s.unityFontStyleAndWeight = FontStyle.Bold; s.textShadow = Shadow(3f, 3f, 2f, Color.black); }),
    };

    public static void Run(UIDocument document, RenderTexture target)
    {
        Directory.CreateDirectory(Output);
        var root = document.rootVisualElement;
        root.Clear();
        var labels = new Label[Cases.Length];
        for (int i = 0; i < Cases.Length; i++)
        {
            var cell = new VisualElement { name = Cases[i].name };
            cell.style.position = Position.Absolute;
            cell.style.left = 4 + (i % Columns) * CellWidth;
            cell.style.top = 4 + (i / Columns) * CellHeight;
            cell.style.width = CellWidth - 8;
            cell.style.height = CellHeight - 8;
            cell.style.backgroundColor = new Color(0.55f, 0.65f, 0.45f, 1f);
            var label = new Label("Shadow Ag") { name = Cases[i].name + "-label" };
            label.style.position = Position.Absolute;
            label.style.left = 12;
            label.style.top = 20;
            label.style.fontSize = 32;
            label.style.color = Color.white;
            Cases[i].style(label.style);
            cell.Add(label);
            root.Add(cell);
            labels[i] = label;
        }
        var sb = new StringBuilder();
        foreach (float scale in new[] { 1f, 1.25f })
        {
            document.panelSettings.scale = scale;
            Update(); Update();
            Render(target, Path.Combine(Output, "shadow-" + (scale == 1 ? "1" : "1.25") + ".png"));
        }
        for (int i = 0; i < labels.Length; i++)
        {
            var r = labels[i].resolvedStyle;
            sb.Append("{\"case\":\"").Append(Cases[i].name)
              .Append("\",\"outline\":[").Append(F(r.unityTextOutlineWidth)).Append(',').Append(C(r.unityTextOutlineColor))
              .Append("],\"layout\":[").Append(F(labels[i].layout.x)).Append(',').Append(F(labels[i].layout.y)).Append(',')
              .Append(F(labels[i].layout.width)).Append(',').Append(F(labels[i].layout.height)).Append("]}\n");
        }
        File.WriteAllText(Path.Combine(Output, "shadow.jsonl"), sb.ToString());
    }

    static string F(float v) => v.ToString("R", Inv);
    static string C(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";

    static void Update()
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
    }

    static void Render(RenderTexture target, string path)
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { false });
        t.GetMethod("RenderOffscreenPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, target.width, target.height), 0, 0); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        RenderTexture.active = old;
    }
}
