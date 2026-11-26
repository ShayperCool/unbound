using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Which mips of each texture the GPU samples under QualitySettings.globalTextureMipmapLimit;
/// see README.md.</summary>
public class MipmapLimitProbe : MonoBehaviour
{
    const int Cell = 64;
    static readonly List<string> s_Lines = new List<string>();

    static void Add(string key, object value)
    {
        string json = value is string s ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : value is bool b ? (b ? "true" : "false")
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        s_Lines.Add("{\"k\": \"" + key + "\", \"v\": " + json + "}");
    }

    struct Case
    {
        public string Name;
        public Texture Texture;
        public Material Material;
    }

    readonly List<Case> m_Cases = new List<Case>();
    readonly HashSet<int> m_Seen = new HashSet<int>();
    Camera m_Camera;
    RenderTexture m_Target;
    Texture2D m_Readback;

    static Mesh Quad(float x0)
    {
        var m = new Mesh();
        m.vertices = new[] { new Vector3(x0, 0, 0), new Vector3(x0 + Cell, 0, 0), new Vector3(x0 + Cell, Cell, 0), new Vector3(x0, Cell, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        return m;
    }

    /// <summary>A script texture with a colour per mip (as ProbeSetup.MipColor).</summary>
    static Texture2D ScriptTexture(bool ignoreLimit)
    {
        var palette = new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 0, 255),
            new Color32(255, 0, 255, 255), new Color32(0, 255, 255, 255), new Color32(255, 128, 0, 255) };
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        for (int mip = 0; mip < t.mipmapCount; mip++)
        {
            int s = Mathf.Max(1, 64 >> mip);
            t.SetPixels32(Enumerable.Repeat(palette[mip], s * s).ToArray(), mip);
        }
        t.Apply(false);
        t.filterMode = FilterMode.Point;
        t.ignoreMipmapLimit = ignoreLimit;
        return t;
    }

    void AddCase(string name, Texture texture, bool array = false)
    {
        var material = new Material(Shader.Find(array ? "Probe/Array" : "Probe/Tex"));
        material.SetTexture(array ? "_Tex" : "_MainTex", texture);
        m_Cases.Add(new Case { Name = name, Texture = texture, Material = material });
    }

    void Start()
    {
        foreach (var name in new[] { "png", "pngIgnore", "pngNoMips", "pngCompressed", "pngLightmap", "pngSprite", "asset", "assetIgnore", "small16", "small8", "small4", "small2", "chain3", "chain5", "wide", "tall" })
            AddCase(name, Resources.Load<Texture2D>(name));
        AddCase("script", ScriptTexture(false));
        AddCase("scriptDefault", ScriptTexture(true));
        // A script texture limited, then made unreadable by Apply.
        var locked = ScriptTexture(false);
        locked.Apply(false, true);
        AddCase("scriptLocked", locked);
        AddCase("array", Resources.Load<Texture2DArray>("array"), true);

        int width = Cell * m_Cases.Count;
        var camGo = new GameObject("ProbeCamera");
        camGo.transform.position = new Vector3(width / 2f, Cell / 2f, -10);
        m_Camera = camGo.AddComponent<Camera>();
        m_Camera.orthographic = true;
        m_Camera.orthographicSize = Cell / 2f;
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = Color.black;
        m_Camera.nearClipPlane = 1;
        m_Camera.farClipPlane = 20;
        m_Target = new RenderTexture(width, Cell, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        m_Camera.targetTexture = m_Target;
        m_Camera.aspect = (float)width / Cell;
        m_Readback = new Texture2D(width, Cell, TextureFormat.RGBA32, false, true);
        for (int i = 0; i < m_Cases.Count; i++)
        {
            var go = new GameObject("Quad " + m_Cases[i].Name);
            go.AddComponent<MeshFilter>().sharedMesh = Quad(i * Cell);
            go.AddComponent<MeshRenderer>().sharedMaterial = m_Cases[i].Material;
        }

        // The level Play mode starts with (limit 2), a level change each way, the ignore flag
        // toggled under a limit, then script limits; last, levels after a script limit.
        Step("start");
        QualitySettings.SetQualityLevel(0);
        Step("level0");
        QualitySettings.SetQualityLevel(1);
        Step("level1");
        SetIgnore("ignoreFlipped", "asset", true);
        SetIgnore("ignoreFlipped", "png", true);
        SetIgnore("ignoreFlipped", "assetIgnore", false);
        Step("ignoreFlipped");
        SetIgnore("ignoreRestored", "asset", false);
        SetIgnore("ignoreRestored", "assetIgnore", true);
        Step("ignoreRestored");
        foreach (int limit in new[] { 0, 1, 3, 4, 7, -1, 1 })
        {
            QualitySettings.globalTextureMipmapLimit = limit;
            Step("limit" + (limit < 0 ? "Neg" : limit.ToString()) + (m_Seen.Add(limit) ? "" : "again"));
        }
        QualitySettings.SetQualityLevel(0);
        Step("afterSetLevel0");
        QualitySettings.SetQualityLevel(1);
        Step("afterSetLevel1");
        // Texture's own limit properties beside the quality level's.
#pragma warning disable CS0618
        Add("textureLimits.before", QualitySettings.globalTextureMipmapLimit + "," + Texture.masterTextureLimit + "," + Texture.globalMipmapLimit);
        Texture.masterTextureLimit = 3;
        Add("textureLimits.afterMaster3", QualitySettings.globalTextureMipmapLimit + "," + Texture.masterTextureLimit + "," + Texture.globalMipmapLimit);
        Step("afterMaster3");
        QualitySettings.globalTextureMipmapLimit = 0;
        Add("textureLimits.afterQuality0", QualitySettings.globalTextureMipmapLimit + "," + Texture.masterTextureLimit + "," + Texture.globalMipmapLimit);
#pragma warning restore CS0618
        Add("complete", true);

        string dir = Environment.GetEnvironmentVariable("MIPMAP_LIMIT_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), s_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Sets a texture's ignoreMipmapLimit, recording whether the set throws.</summary>
    void SetIgnore(string step, string name, bool value)
    {
        string result = "ok";
        try { Find(name).ignoreMipmapLimit = value; }
        catch (Exception e) { result = e.GetType().Name + ": " + e.Message; }
        Add(step + "." + name + ".set", result);
    }

    Texture2D Find(string name) => (Texture2D)m_Cases.First(c => c.Name == name).Texture;

    static string Hex(Color32 c) => $"{c.r:x2}{c.g:x2}{c.b:x2}";

    void Step(string step)
    {
        Add(step + ".globalTextureMipmapLimit", QualitySettings.globalTextureMipmapLimit);
        Add(step + ".level", QualitySettings.GetQualityLevel());
        m_Camera.Render();
        RenderTexture.active = m_Target;
        m_Readback.ReadPixels(new Rect(0, 0, m_Target.width, m_Target.height), 0, 0);
        m_Readback.Apply();
        RenderTexture.active = null;
        var pixels = m_Readback.GetPixels32();
        for (int i = 0; i < m_Cases.Count; i++)
        {
            var c = m_Cases[i];
            string key = step + "." + c.Name;
            Add(key + ".size", c.Texture.width + "x" + c.Texture.height);
            Add(key + ".mipmapCount", c.Texture.mipmapCount);
            if (c.Texture is Texture2D t2)
            {
                Add(key + ".activeMipmapLimit", t2.activeMipmapLimit);
                Add(key + ".ignoreMipmapLimit", t2.ignoreMipmapLimit);
            }
            else if (c.Texture is Texture2DArray ta)
            {
                Add(key + ".activeMipmapLimit", ta.activeMipmapLimit);
                Add(key + ".ignoreMipmapLimit", ta.ignoreMipmapLimit);
            }
            // Rows 0 and 1, texels 0..7 of the cell: the finest structure the upload kept.
            var sampled = new List<string>();
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 8; x++)
                    sampled.Add(Hex(pixels[y * m_Target.width + i * Cell + x]));
            Add(key + ".pixels", sampled.Distinct().Count() == 1 ? sampled[0] : string.Join(" ", sampled));
        }
    }
}
