using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.LowLevel;

public sealed class CrtLoadControl : MonoBehaviour
{
    sealed class BeforePhase { }
    sealed class BetweenPhases { }
    sealed class AfterPhase { }
    static readonly List<string> Values = new List<string>();
    static readonly List<CustomRenderTexture> Textures = new List<CustomRenderTexture>();
    static readonly HashSet<CustomRenderTexture> Loaded = new HashSet<CustomRenderTexture>();
    static readonly HashSet<CustomRenderTexture> Read = new HashSet<CustomRenderTexture>();
    static string output, stage = "setup";
    static int update, visit, events, exceptions;
    static CustomRenderTexture requested;
    static bool madeSkipped, lateOnly;

    static string Q(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static void Put(string key, object value) => Values.Add("{\"k\":" + Q(key) + ",\"v\":"
        + Q(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)) + "}");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Begin()
    {
        output = Environment.GetEnvironmentVariable("CRT_LOAD_OUT");
        if (output == null) return;
        lateOnly = Environment.GetEnvironmentVariable("CRT_LATE_ONLY") == "1";
        CustomRenderTextureManager.textureLoaded += texture =>
        {
            if (!Textures.Contains(texture)) return;
            Loaded.Add(texture);
            Put("event." + (++events), texture.name + ":u" + update + ":" + stage + ":created" + texture.IsCreated());
        };
        CustomRenderTextureManager.textureLoaded += texture =>
        {
            if (texture.name == "spawnParent") Make("spawnChild");
            if (texture.name == "throwing") throw new InvalidOperationException("CRT load callback");
        };
        Application.logMessageReceived += (message, trace, type) =>
        {
            if (type == LogType.Exception && message.Contains("CRT load callback")) exceptions++;
        };
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        Put("loop.installed", Install(ref loop));
        PlayerLoop.SetPlayerLoop(loop);
        if (!lateOnly) Make("beforeScene");
    }

    static bool Install(ref PlayerLoopSystem root)
    {
        if (root.subSystemList == null) return false;
        var list = new List<PlayerLoopSystem>();
        bool installed = false;
        for (int i = 0; i < root.subSystemList.Length; i++)
        {
            var child = root.subSystemList[i];
            if (child.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateCustomRenderTextures))
            {
                list.Add(new PlayerLoopSystem { type = typeof(BeforePhase), updateDelegate = Before });
                list.Add(child);
                list.Add(new PlayerLoopSystem { type = typeof(BetweenPhases), updateDelegate = Between });
                list.Add(child);
                list.Add(new PlayerLoopSystem { type = typeof(AfterPhase), updateDelegate = After });
                installed = true;
            }
            else
            {
                installed |= Install(ref child);
                list.Add(child);
            }
        }
        root.subSystemList = list.ToArray();
        return installed;
    }

    static CustomRenderTexture Make(string name, bool create = false)
    {
        var texture = new CustomRenderTexture(2, 2, RenderTextureFormat.ARGBFloat)
        {
            name = name,
            initializationColor = new Color(0.25f, 0.5f, 0.75f, 1),
            initializationMode = CustomRenderTextureUpdateMode.OnLoad,
            updateMode = CustomRenderTextureUpdateMode.OnDemand
        };
        Textures.Add(texture);
        if (create) texture.Create();
        Put("make." + name, "u" + update + ":" + stage + ":created" + texture.IsCreated());
        return texture;
    }

    static string State()
    {
        var loaded = new List<CustomRenderTexture>();
        CustomRenderTextureManager.GetAllCustomRenderTextures(loaded);
        var parts = new List<string>();
        foreach (var texture in Textures)
            parts.Add(texture.name + ":" + loaded.Contains(texture) + ":" + texture.IsCreated());
        return string.Join(",", parts);
    }

    static string Pixel(CustomRenderTexture target)
    {
        var old = RenderTexture.active;
        var read = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, 2, 2), 0, 0);
            var color = read.GetPixel(0, 0);
            return color.r.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { RenderTexture.active = old; DestroyImmediate(read); }
    }

    static void Before()
    {
        visit++;
        stage = "crt1";
        Put("visit." + visit + ".before", "u" + update + ":" + State());
    }

    static void Between()
    {
        stage = "between";
        Put("visit." + visit + ".between", "u" + update + ":" + State());
        if (update == 3)
        {
            Make("between");
            if (requested != null)
            {
                requested.initializationMode = CustomRenderTextureUpdateMode.OnDemand;
                requested.initializationColor = new Color(0.75f, 0.5f, 0.25f, 1);
                requested.Initialize();
            }
        }
        stage = "crt2";
    }

    static void After()
    {
        stage = "after";
        Put("visit." + visit + ".after", "u" + update + ":" + State());
        foreach (var texture in Textures)
            if (Loaded.Contains(texture) && Read.Add(texture)) Put("pixel.first." + texture.name, Pixel(texture));
        if (Loaded.Contains(requested)) Put("pixel.request.u" + update, Pixel(requested));
        if (update == 6) UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 3;
        if (update == 12) UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
        if (update != 16) return;
        Put("callback.exception.logs", exceptions);
        Values.Sort(StringComparer.Ordinal);
        Put("complete", 1);
        File.WriteAllLines(output, Values);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Add() { if (output != null) new GameObject("CRT load control").AddComponent<CrtLoadControl>(); }
    void Awake() { stage = "awake"; if (!lateOnly) Make("awake"); }
    void Start() { stage = "start"; if (!lateOnly) { requested = Make("start"); Make("startCreated", true); } }
    void Update()
    {
        update++;
        stage = "update";
        Put("update." + update, State());
        if (!lateOnly && update <= 2) Make("update" + update);
        if (update == 13) { Make("throwing"); Make("afterThrow"); }
        if (update == 15) Make("spawnParent");
        if (update > 6)
        {
            bool renders = UnityEngine.Rendering.OnDemandRendering.willCurrentFrameRender;
            Put("render.u" + update, renders);
            if (!renders && !madeSkipped) { Make("screenSkipped"); madeSkipped = true; }
        }
    }
    void LateUpdate()
    {
        stage = "late";
        if (!lateOnly && update <= 2) Make("late" + update);
    }
}
