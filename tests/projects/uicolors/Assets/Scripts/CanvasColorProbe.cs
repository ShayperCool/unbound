using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class CanvasColorProbe : MonoBehaviour
{
    readonly StringBuilder records = new StringBuilder();
    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    void Value(string key, string value) => records.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");
    static string ColorJson(Color color) => "[" + F(color.r) + "," + F(color.g) + "," + F(color.b) + "," + F(color.a) + "]";

    void Capture(string key, Camera camera, RenderTexture target)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); image.Apply(); RenderTexture.active = null;
        var pixels = image.GetPixels();
        for (int i = 0; i < 256; i++) Value(key + "." + i, ColorJson(pixels[(i / 16 * 8 + 4) * 128 + i % 16 * 8 + 4]));
        DestroyImmediate(image);
    }

    static Mesh Palette(bool alpha)
    {
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var uv = new List<Vector2>();
        var indices = new List<int>();
        for (int i = 0; i < 256; i++)
        {
            int first = vertices.Count;
            float x = (i % 16) * 8 - 64, y = (i / 16) * 8 - 64;
            vertices.AddRange(new[] { new Vector3(x, y), new Vector3(x + 8, y), new Vector3(x + 8, y + 8), new Vector3(x, y + 8) });
            var color = new Color32((byte)i, (byte)(255 - i), (byte)((i * 73) % 256), alpha ? (byte)i : (byte)255);
            for (int j = 0; j < 4; j++) { colors.Add(color); uv.Add(new Vector2(.5f, .5f)); }
            indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
        return mesh;
    }

    IEnumerator Start()
    {
        yield return null;
        Value("colorSpace", "\"" + QualitySettings.activeColorSpace + "\"");
        var directory = Environment.GetEnvironmentVariable("UI_COLORS_OUT") ?? "/tmp/ui-colors";
        Directory.CreateDirectory(directory);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true; camera.orthographicSize = 64;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        camera.targetTexture = target;
        var raw = new Material(Shader.Find("Probe/CanvasVertexColors"));
        var standard = new Material(Shader.Find("UI/Default"));
        for (int nested = 0; nested < 4; nested++)
        for (int flag = 0; flag < 2; flag++)
        for (int tinted = 0; tinted < 2; tinted++)
        {
            var rootObject = new GameObject("Root", typeof(RectTransform), typeof(Canvas));
            var root = rootObject.GetComponent<Canvas>();
            root.renderMode = nested == 3 ? RenderMode.ScreenSpaceCamera : RenderMode.WorldSpace;
            if (nested == 3) { root.worldCamera = camera; root.planeDistance = 1; }
            else root.transform.position = Vector3.zero;
            root.vertexColorAlwaysGammaSpace = flag != 0;
            rootObject.GetComponent<RectTransform>().sizeDelta = new Vector2(128, 128);
            Transform parent = root.transform;
            if (nested == 1 || nested == 2)
            {
                var childObject = new GameObject("Nested", typeof(RectTransform), typeof(Canvas));
                childObject.transform.SetParent(root.transform, false);
                var child = childObject.GetComponent<Canvas>();
                child.overrideSorting = nested == 2;
                child.vertexColorAlwaysGammaSpace = flag == 0;
                var childRect = childObject.GetComponent<RectTransform>();
                childRect.anchorMin = childRect.anchorMax = new Vector2(.5f, .5f);
                childRect.sizeDelta = new Vector2(128, 128);
                childRect.localPosition = Vector3.zero;
                parent = child.transform;
            }
            var objectWithRenderer = new GameObject("Palette", typeof(RectTransform), typeof(CanvasRenderer));
            objectWithRenderer.transform.SetParent(parent, false);
            objectWithRenderer.GetComponent<RectTransform>().sizeDelta = new Vector2(128, 128);
            var renderer = objectWithRenderer.GetComponent<CanvasRenderer>();
            renderer.SetTexture(Texture2D.whiteTexture);
            renderer.SetColor(tinted == 0 ? Color.white : new Color(.5f, .25f, .75f, .6f));
            renderer.materialCount = 1;
            var mesh = Palette(tinted != 0);
            renderer.SetMesh(mesh);
            for (int shader = 0; shader < 3; shader++)
            {
                string key = "n" + nested + ".f" + flag + ".t" + tinted + ".s" + shader;
                renderer.SetMaterial(shader == 1 ? standard : raw, 0);
                raw.SetFloat("_ProbeUniform", shader == 2 ? 1 : 0);
                Capture(key, camera, target);
            }
            string live = "n" + nested + ".f" + flag + ".t" + tinted + ".live";
            var effective = parent.GetComponent<Canvas>();
            renderer.SetMaterial(raw, 0); raw.SetFloat("_ProbeUniform", 0);
            Capture(live + "0", camera, target);
            effective.vertexColorAlwaysGammaSpace = !effective.vertexColorAlwaysGammaSpace;
            Capture(live + "1", camera, target);
            effective.vertexColorAlwaysGammaSpace = !effective.vertexColorAlwaysGammaSpace;
            Capture(live + "2", camera, target);
            renderer.SetColor(new Color(.7f, .2f, .9f, .42f));
            Value(live + ".alphaAfterColor", F(renderer.GetAlpha()));
            Capture(live + "3", camera, target);
            var group = rootObject.AddComponent<CanvasGroup>(); group.alpha = .37f;
            Capture(live + "4", camera, target);
            renderer.SetAlpha(.51f);
            Value(live + ".colorAfterAlpha", ColorJson(renderer.GetColor()));
            Capture(live + "5", camera, target);
            var changed = mesh.colors32;
            for (int i = 0; i < changed.Length; i++) changed[i] = new Color32(255, 32, 128, 175);
            mesh.colors32 = changed;
            Capture(live + "6", camera, target);
            renderer.SetMesh(mesh);
            Capture(live + "7", camera, target);
            renderer.SetMaterial(standard, 0); renderer.popMaterialCount = 1;
            renderer.SetPopMaterial(raw, 0); renderer.hasPopInstruction = true;
            Capture(live + "8", camera, target);
            var clone = Instantiate(rootObject);
            Value("clone.n" + nested + ".f" + flag + ".t" + tinted, clone.GetComponent<Canvas>().vertexColorAlwaysGammaSpace ? "true" : "false");
            DestroyImmediate(clone); DestroyImmediate(rootObject); DestroyImmediate(mesh);
        }
        DestroyImmediate(raw); DestroyImmediate(standard); DestroyImmediate(camera.gameObject); DestroyImmediate(target);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
