using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

// Original control images with glyph color suppressed, followed by independent shape controls.
// Suppression happens after the recorded render and is restored before the next interaction.
public static class PixelProbe
{
    static string Output => Environment.GetEnvironmentVariable("UITK_PIXELS_OUT");
    static readonly List<string> Rows = new List<string>();
    static void Visit(VisualElement e, Action<VisualElement> action)
    {
        action(e);
        for (int i = 0; i < e.hierarchy.childCount; i++) Visit(e.hierarchy[i], action);
    }

    public static void Observe(string phase, UIDocument document, Action render)
    {
        var saved = new List<KeyValuePair<VisualElement, StyleColor>>();
        Visit(document.rootVisualElement, e => {
            saved.Add(new KeyValuePair<VisualElement, StyleColor>(e, e.style.color));
            e.style.color = Color.clear;
        });
        Update(); render();
        foreach (var pair in saved) pair.Key.style.color = pair.Value;
        Update();
    }

    static void Update()
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
    }

    public static void Run(UIDocument document)
    {
        Directory.CreateDirectory(Output);
        Rows.Add("{\"gpu\":\"" + SystemInfo.graphicsDeviceName + "\",\"width\":800,\"height\":600}");
        var root = document.rootVisualElement;
        root.Clear();
        root.style.backgroundColor = (Color)new Color32(43, 45, 49, 255);
        document.panelSettings.scale = 1;
        int[] radii = { 0, 2, 4, 8, 12, 18 };
        for (int row = 0; row < 6; row++)
        for (int col = 0; col < 10; col++)
        {
            var e = new VisualElement { name = "shape-" + row + "-" + col };
            e.style.position = Position.Absolute;
            e.style.left = 12 + col * 61 + (col % 3) * 0.2f;
            e.style.top = 12 + row * 63 + (row % 3) * 0.2f;
            e.style.width = 48; e.style.height = 36;
            e.style.backgroundColor = (Color)new Color32(48, 48, 48, 255);
            int radius = radii[row];
            e.style.borderTopLeftRadius = radius; e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius; e.style.borderBottomRightRadius = radius;
            if (col > 0)
            {
                e.style.borderLeftWidth = col == 1 ? 2 : col;
                e.style.borderTopWidth = 2;
                e.style.borderRightWidth = col == 1 ? 2 : 4;
                e.style.borderBottomWidth = col == 1 ? 2 : 6;
                e.style.borderTopColor = (Color)new Color32(224, 192, 64, 255);
                e.style.borderRightColor = (Color)new Color32(64, 192, 96, 255);
                e.style.borderBottomColor = (Color)new Color32(64, 128, 224, 255);
                e.style.borderLeftColor = (Color)new Color32(192, 64, 192, 255);
            }
            root.Add(e);
        }
        foreach (float scale in new[] { 1f, 1.25f })
        {
            document.panelSettings.scale = scale; Update(); Update();
            Render(document, "shapes-" + (scale == 1 ? "1" : "1.25"));
        }
        root.Clear(); document.panelSettings.scale = 1;
        int[][] borders = {
            new[] { 1, 1, 19, 19 }, new[] { 19, 1, 1, 19 },
            new[] { 19, 19, 1, 1 }, new[] { 1, 19, 19, 1 },
            new[] { 1, 1, 20, 20 }, new[] { 1, 1, 19, 2 },
            new[] { 1, 1, 2, 19 }, new[] { 1, 1, 1, 1 },
        };
        for (int i = 0; i < borders.Length; i++)
        {
            var e = new VisualElement { name = "border-interior-" + i };
            e.style.position = Position.Absolute;
            e.style.left = 30 + i * 70; e.style.top = 30;
            e.style.width = 20; e.style.height = 20;
            e.style.backgroundColor = (Color)new Color32(48, 48, 48, 255);
            e.style.borderTopLeftRadius = 10; e.style.borderTopRightRadius = 10;
            e.style.borderBottomLeftRadius = 10; e.style.borderBottomRightRadius = 10;
            e.style.borderLeftWidth = borders[i][0]; e.style.borderTopWidth = borders[i][1];
            e.style.borderRightWidth = borders[i][2]; e.style.borderBottomWidth = borders[i][3];
            e.style.borderTopColor = e.style.borderRightColor = e.style.borderBottomColor = e.style.borderLeftColor = (Color)new Color32(224, 144, 48, 255);
            root.Add(e);
        }
        Update(); Update(); Render(document, "collapsed-borders");
        File.WriteAllLines(Path.Combine(Output, "pixels.jsonl"), Rows);
    }

    static void Render(UIDocument document, string name)
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { false });
        t.GetMethod("RenderOffscreenPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        var old = RenderTexture.active;
        RenderTexture.active = document.panelSettings.targetTexture;
        var tex = new Texture2D(800, 600, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 800, 600), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(Output, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        RenderTexture.active = old;
    }
}
