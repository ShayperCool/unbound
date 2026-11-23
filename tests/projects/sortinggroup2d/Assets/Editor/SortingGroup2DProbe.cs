using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Which of two overlapping sprites the 2D renderer draws on top when one is in a SortingGroup
/// whose sorting layer differs from its own, with the 2D lights splitting the sorting layers into
/// separate layer batches (one global light per layer) or not (one light for both). Two unit
/// squares overlap: A (red) in the group, B (green) outside it; the 2D renderer sorts along the
/// custom axis Y (higher first: Assets/Settings/Probe2DRenderer.asset). Each case's frames draw its
/// camera into a render texture; the colour at the overlap (which sprite is on top) is recorded as
/// "A" or "B". Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class SortingGroup2DProbe
{
    const string Variable = "SORTING_GROUP_2D_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static Sprite s_Square;

    static SortingGroup2DProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) => { if (type != LogType.Log) s_Logs.Add(type + ": " + message.Split('\n')[0]); };
            EditorApplication.delayCall += Run;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static int Objects => SortingLayer.NameToID("Objects");

    struct Spec
    {
        public string Name, GroupLayer, ChildLayer, OtherLayer, InnerLayer;
        public bool Split;
        public float GroupY, OtherY;
    }

    static readonly List<Spec> s_Specs = new List<Spec>();
    static IEnumerator<object> s_Steps;

    static void Add(string name, bool split, string groupLayer, string childLayer, float groupY, string otherLayer, float otherY, string innerLayer = null) =>
        s_Specs.Add(new Spec { Name = name, Split = split, GroupLayer = groupLayer, ChildLayer = childLayer, GroupY = groupY, OtherLayer = otherLayer, OtherY = otherY, InnerLayer = innerLayer });

    static void Run()
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var white = new Color32[16];
        for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(white);
        texture.Apply();
        s_Square = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);

        // The group in front (lower Y) of B; its child A on another sorting layer than the group.
        Add("split.groupFront", true, "Objects", "Default", 0f, "Objects", 0.5f);
        Add("split.groupBack", true, "Objects", "Default", 0.5f, "Objects", 0f);
        Add("single.groupFront", false, "Objects", "Default", 0f, "Objects", 0.5f);
        Add("single.groupBack", false, "Objects", "Default", 0.5f, "Objects", 0f);
        // The group on the lower layer, its child on the higher one.
        Add("split.lowGroupBack", true, "Default", "Objects", 0.5f, "Default", 0f);
        Add("split.lowGroupFront", true, "Default", "Objects", 0f, "Default", 0.5f);
        // Nested groups: an outer group on Objects in front, an inner one on Default; and the reverse.
        Add("split.nestedFront", true, "Objects", "Default", 0f, "Objects", 0.5f, "Default");
        Add("split.nestedInnerObjects", true, "Default", "Default", 0.5f, "Default", 0f, "Objects");
        // Controls without a group: each sprite in its own layer's batch, and one layer.
        Add("split.noGroupDefaultFront", true, null, "Default", 0f, "Objects", 0.5f);
        Add("split.sameLayerFront", true, null, "Objects", 0f, "Objects", 0.5f);
        Add("split.sameLayerBack", true, null, "Objects", 0.5f, "Objects", 0f);
        s_Steps = Steps();
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        bool more;
        try { more = s_Steps.MoveNext(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); more = false; }
        if (more) return;
        EditorApplication.update -= Tick;
        Put("logs", string.Join(" | ", s_Logs));
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    /// <summary>Each case is built, drawn by two frames (sorting groups take their members in
    /// the frame) into its camera's texture, then read.</summary>
    static IEnumerator<object> Steps()
    {
        foreach (var spec in s_Specs)
        {
            var made = new List<GameObject>();
            Camera camera = null;
            try { camera = Build(spec, made); }
            catch (Exception e) { Put(spec.Name + ".threw", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
            yield return null;
            yield return null;
            if (camera != null)
            {
                try { Put(spec.Name + ".top", Top(camera)); }
                catch (Exception e) { Put(spec.Name + ".threw", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
            }
            foreach (var go in made) UnityEngine.Object.DestroyImmediate(go);
            Put(spec.Name + ".logs", string.Join(" | ", s_Logs));
            s_Logs.Clear();
            yield return null;
        }
    }

    static Camera Build(Spec spec, List<GameObject> made)
    {
        GameObject Make(string n, Transform parent = null)
        {
            var go = new GameObject(n);
            if (parent != null) go.transform.SetParent(parent, false);
            else made.Add(go);
            return go;
        }
        // Its layers before it is made global: a new light targets every layer.
        void Light(string n, int[] layers)
        {
            var light = Make(n).AddComponent<Light2D>();
            light.targetSortingLayers = layers;
            light.lightType = Light2D.LightType.Global;
        }
        if (spec.Split)
        {
            Light("LightDefault", new[] { 0 });
            Light("LightObjects", new[] { Objects });
        }
        else Light("LightBoth", new[] { 0, Objects });

        Transform parentOfA = null;
        if (spec.GroupLayer != null)
        {
            var group = Make("Group").AddComponent<SortingGroup>();
            group.sortingLayerName = spec.GroupLayer;
            group.transform.position = new Vector3(0, spec.GroupY, 0);
            parentOfA = group.transform;
            if (spec.InnerLayer != null)
            {
                var inner = Make("Inner", parentOfA).AddComponent<SortingGroup>();
                inner.sortingLayerName = spec.InnerLayer;
                parentOfA = inner.transform;
            }
        }
        var a = (parentOfA != null ? Make("A", parentOfA) : Make("A")).AddComponent<SpriteRenderer>();
        a.sprite = s_Square;
        a.color = Color.red;
        a.sortingLayerName = spec.ChildLayer;
        if (parentOfA == null) a.transform.position = new Vector3(0, spec.GroupY, 0);
        var b = Make("B").AddComponent<SpriteRenderer>();
        b.sprite = s_Square;
        b.color = Color.green;
        b.sortingLayerName = spec.OtherLayer;
        b.transform.position = new Vector3(0, spec.OtherY, 0);

        var camera = Make("Camera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 0.2f;
        camera.transform.position = new Vector3(0, (spec.GroupY + spec.OtherY) / 2, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        // Drawn by the frames the case waits, into a texture read back after them.
        camera.targetTexture = new RenderTexture(16, 16, 24, RenderTextureFormat.ARGB32);
        return camera;
    }

    /// <summary>Which sprite the camera's frames left on top at the overlap's centre.</summary>
    static string Top(Camera camera)
    {
        var target = camera.targetTexture;
        RenderTexture.active = target;
        var read = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
        read.Apply();
        RenderTexture.active = null;
        var c = read.GetPixel(8, 8);
        UnityEngine.Object.DestroyImmediate(read);
        camera.targetTexture = null;
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        return c.r > 0.25f && c.r > c.g ? "A" : c.g > 0.25f && c.g > c.r ? "B" : "none";
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
