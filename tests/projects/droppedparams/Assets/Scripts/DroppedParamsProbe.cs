using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Records what overloads with extra parameters, and a few answers that are easy to get wrong
/// silently, give in the Editor: shader texture property dimensions, LogOption.NoStacktrace,
/// GL.GetGPUProjectionMatrix, Blit depth slices and render target slices, the Clear load action,
/// Resources.UnloadAsset, and a scriptable pipeline's visible lights and shadow caster bounds.
/// Writes <c>DROPPED_PARAMS_PROBE_OUT</c> (one {"k","v"} JSON line per value; floats as the hex
/// of their bits) and exits.
/// </summary>
public sealed class DroppedParamsProbe : MonoBehaviour
{
    public Shader[] dimsShaders;
    public Material solid;

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    bool m_Capturing;

    /// <summary>Keys starting with one of <c>DROPPED_PARAMS_PROBE_SKIP</c>'s comma-separated
    /// prefixes are neither run nor recorded (a control run of an engine that cannot run them).</summary>
    static readonly string[] s_Skip = (Environment.GetEnvironmentVariable("DROPPED_PARAMS_PROBE_SKIP") ?? "")
        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

    static bool Skipped(string key) => s_Skip.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

    void Rec(string key, string value)
    {
        if (!Skipped(key)) Append(key, value);
    }

    void Append(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value)).Append("\"}\n");

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
    static string V(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);
    static string C(Color c) => F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a);
    static string M(Matrix4x4 m)
    {
        var parts = new string[16];
        for (int i = 0; i < 16; i++) parts[i] = F(m[i % 4, i / 4]);
        return string.Join(" ", parts);
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        if (m_Capturing) m_Logs.Add(type + "|" + condition + "|" + (string.IsNullOrEmpty(stackTrace) ? "nostack" : "stack"));
    }

    /// <summary>Runs a case, recording what it returns, throws and logs.</summary>
    void Case(string key, Func<string> run)
    {
        if (Skipped(key)) return;
        m_Logs.Clear();
        m_Capturing = true;
        string result;
        try { result = run(); }
        catch (Exception e) { result = "throws " + e.GetType().Name + ": " + e.Message; }
        m_Capturing = false;
        Rec(key, result);
        for (int i = 0; i < m_Logs.Count; i++) Rec(key + ".log" + i, m_Logs[i]);
    }

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        yield return null;
        yield return null;
        int code = 0;
        try
        {
            Dims();
            Logs();
            Projections();
            Slices();
            LoadActions();
            Unload();
            Shadows();
            var path = Environment.GetEnvironmentVariable("DROPPED_PARAMS_PROBE_OUT");
            if (!string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, m_Out.ToString());
            }
            else
            {
                Debug.Log(m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("dropped params probe failed: " + e);
            code = 1;
        }
        Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    // ------------------------------------------------------------------ shader properties

    void Dims()
    {
        foreach (var shader in dimsShaders)
        {
            int count = shader.GetPropertyCount();
            Rec("dims." + shader.name + ".count", count.ToString());
            for (int i = 0; i <= count; i++)
            {
                int index = i;
                string key = "dims." + shader.name + "." + i;
                Case(key + ".name", () => shader.GetPropertyName(index));
                Case(key + ".type", () => shader.GetPropertyType(index).ToString());
                Case(key + ".dimension", () => shader.GetPropertyTextureDimension(index).ToString());
            }
        }
    }

    // ------------------------------------------------------------------ logs

    void Logs()
    {
        var types = new[] { LogType.Log, LogType.Warning, LogType.Error };
        var options = new[] { LogOption.None, LogOption.NoStacktrace };
        foreach (var type in types)
            foreach (var option in options)
                Case("log." + type + "." + option, () =>
                {
                    Debug.LogFormat(type, option, null, "probe {0} {1}", type, option);
                    return "logged";
                });
        Case("log.logger.Warning", () =>
        {
            Debug.unityLogger.LogFormat(LogType.Warning, "probe logger {0}", 1);
            return "logged";
        });
    }

    // ------------------------------------------------------------------ projections

    void Projections()
    {
        var perspective = Matrix4x4.Perspective(60f, 1.5f, 0.3f, 100f);
        var ortho = Matrix4x4.Ortho(-2f, 2f, -1f, 1f, 0.1f, 10f);
        Rec("gpuproj.perspective.false", M(GL.GetGPUProjectionMatrix(perspective, false)));
        Rec("gpuproj.perspective.true", M(GL.GetGPUProjectionMatrix(perspective, true)));
        Rec("gpuproj.ortho.false", M(GL.GetGPUProjectionMatrix(ortho, false)));
        Rec("gpuproj.ortho.true", M(GL.GetGPUProjectionMatrix(ortho, true)));
    }

    // ------------------------------------------------------------------ slices

    static readonly Color32[] Base = { new Color32(10, 20, 30, 255), new Color32(40, 50, 60, 255), new Color32(70, 80, 90, 255) };
    static readonly Color32[] Source = { new Color32(200, 0, 0, 255), new Color32(0, 200, 0, 255), new Color32(0, 0, 200, 255) };

    static RenderTexture Target(int slices)
    {
        var rt = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        if (slices > 0)
        {
            rt.dimension = TextureDimension.Tex2DArray;
            rt.volumeDepth = slices;
        }
        rt.filterMode = FilterMode.Point;
        rt.Create();
        return rt;
    }

    static void Fill(RenderTexture rt, Color32[] colors)
    {
        var previous = RenderTexture.active;
        for (int s = 0; s < colors.Length; s++)
        {
            Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, s);
            GL.Clear(true, true, colors[s]);
        }
        RenderTexture.active = previous;
    }

    static Material s_SampleSlice;
    static RenderTexture s_Flat;

    /// <summary>The 4x4 texels of a render texture's slice: an array's slice is sampled into a 2D
    /// target first (point sampling at texel centres), which is then read back.</summary>
    static string Read(RenderTexture rt, int slice)
    {
        var previous = RenderTexture.active;
        if (rt.dimension == TextureDimension.Tex2DArray)
        {
            if (s_SampleSlice == null) s_SampleSlice = new Material(Shader.Find("Probe/SampleSlice"));
            if (s_Flat == null) s_Flat = Target(0);
            s_SampleSlice.SetTexture("_Array", rt);
            s_SampleSlice.SetFloat("_Layer", slice);
            Graphics.Blit(null, s_Flat, s_SampleSlice, 0);
            rt = s_Flat;
            slice = 0;
        }
        Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, slice);
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
        texture.ReadPixels(new Rect(0, 0, 4, 4), 0, 0, false);
        var pixels = texture.GetPixels32();
        RenderTexture.active = previous;
        Destroy(texture);
        var distinct = pixels.Select(p => p.r.ToString("x2") + p.g.ToString("x2") + p.b.ToString("x2") + p.a.ToString("x2")).ToArray();
        return distinct.All(p => p == distinct[0]) ? distinct[0] : string.Join(" ", distinct);
    }

    void Slices()
    {
        var array = Target(3);
        var sourceArray = Target(3);
        var source = Target(0);
        Fill(sourceArray, Source);
        Fill(source, new[] { new Color32(250, 10, 10, 255) });
        solid.color = new Color(0f, 1f, 1f, 1f);

        void Slice(string name, Action blit)
        {
            Fill(array, Base);
            Case("slice." + name, () =>
            {
                blit();
                return Read(array, 0) + " | " + Read(array, 1) + " | " + Read(array, 2);
            });
        }

        void Command(Action<CommandBuffer> record)
        {
            using (var cmd = new CommandBuffer { name = "probe" })
            {
                record(cmd);
                Graphics.ExecuteCommandBuffer(cmd);
            }
        }

        var dst = new RenderTargetIdentifier(array);
        var src = new RenderTargetIdentifier(source);
        var srcArray = new RenderTargetIdentifier(sourceArray);
        Slice("none", () => { });
        Slice("graphics.copy", () => Graphics.Blit(source, array));
        Slice("graphics.copy.slices.0.1", () => Graphics.Blit(source, array, 0, 1));
        Slice("graphics.copy.array.2.0", () => Graphics.Blit(sourceArray, array, 2, 0));
        Slice("graphics.copy.scale.0.2", () => Graphics.Blit(source, array, Vector2.one, Vector2.zero, 0, 2));
        Slice("graphics.copy.array", () => Graphics.Blit(sourceArray, array));
        Slice("graphics.material", () => Graphics.Blit(source, array, solid, 0));
        Slice("graphics.material.slice.2", () => Graphics.Blit(source, array, solid, 0, 2));
        Slice("graphics.active.material.slice.1", () =>
        {
            var previous = RenderTexture.active;
            RenderTexture.active = array;
            Graphics.Blit(source, solid, 0, 1);
            RenderTexture.active = previous;
        });
        Slice("cmd.copy", () => Command(c => c.Blit(src, dst)));
        Slice("cmd.copy.array", () => Command(c => c.Blit(srcArray, dst)));
        Slice("cmd.copy.slices.0.1", () => Command(c => c.Blit(src, dst, 0, 1)));
        Slice("cmd.copy.array.2.0", () => Command(c => c.Blit(srcArray, dst, 2, 0)));
        Slice("cmd.copy.scale.0.2", () => Command(c => c.Blit(src, dst, Vector2.one, Vector2.zero, 0, 2)));
        Slice("cmd.material.slice.2", () => Command(c => c.Blit(src, dst, solid, 0, 2)));
        Slice("cmd.copy.identifier.slice.1", () => Command(c => c.Blit(src, new RenderTargetIdentifier(array, 0, CubemapFace.Unknown, 1))));
        Slice("cmd.clear.identifier.slice.2", () => Command(c =>
        {
            c.SetRenderTarget(new RenderTargetIdentifier(array, 0, CubemapFace.Unknown, 2));
            c.ClearRenderTarget(true, true, new Color(1f, 1f, 0f, 1f));
        }));
        Slice("cmd.clear.identifier.all", () => Command(c =>
        {
            c.SetRenderTarget(new RenderTargetIdentifier(array, 0, CubemapFace.Unknown, -1));
            c.ClearRenderTarget(true, true, new Color(1f, 1f, 0f, 1f));
        }));
        Slice("cmd.clear.target.all", () => Command(c =>
        {
            c.SetRenderTarget(array, 0, CubemapFace.Unknown, -1);
            c.ClearRenderTarget(true, true, new Color(1f, 1f, 0f, 1f));
        }));
        var flat = Target(0);
        void Flat(string name, Action blit)
        {
            Fill(flat, Base.Take(1).ToArray());
            Case("slice." + name, () =>
            {
                blit();
                return Read(flat, 0);
            });
        }
        Flat("graphics.copy.array.1.flat", () => Graphics.Blit(sourceArray, flat, 1, 0));
        Flat("cmd.copy.array.1.flat", () => Command(c => c.Blit(srcArray, new RenderTargetIdentifier(flat), 1, 0)));
        flat.Release();
        array.Release();
        sourceArray.Release();
        source.Release();
    }

    // ------------------------------------------------------------------ load actions

    void LoadActions()
    {
        var target = Target(0);
        void Load(string name, Action<CommandBuffer> record)
        {
            Fill(target, Base.Take(1).ToArray());
            Case("load." + name, () =>
            {
                using (var cmd = new CommandBuffer { name = "probe" })
                {
                    record(cmd);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                return Read(target, 0);
            });
        }
        var id = new RenderTargetIdentifier(target);
        Load("load", c => c.SetRenderTarget(id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store));
        Load("clear", c => c.SetRenderTarget(id, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store));
        Load("clear.both", c => c.SetRenderTarget(id, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store,
            RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store));
        Load("clear.draw", c =>
        {
            c.SetRenderTarget(id, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
            c.DrawProcedural(Matrix4x4.identity, solid, 0, MeshTopology.Triangles, 0);
        });
        target.Release();
    }

    // ------------------------------------------------------------------ Resources.UnloadAsset

    void Unload()
    {
        Material material = null;
        int materialId = 0;
        Case("unload.material", () =>
        {
            material = Resources.Load<Material>("UnloadMat");
            materialId = material.GetInstanceID();
            material.color = new Color(1f, 0f, 0f, 1f);
            Resources.UnloadAsset(material);
            return "ok";
        });
        Case("unload.material.alive", () => (material != null).ToString());
        Case("unload.material.color", () => C(material.color));
        Case("unload.material.reload", () =>
        {
            var again = Resources.Load<Material>("UnloadMat");
            return ReferenceEquals(again, material) + " " + (again.GetInstanceID() == materialId) + " " + C(again.color);
        });

        ProbeData data = null;
        Case("unload.data", () =>
        {
            data = Resources.Load<ProbeData>("UnloadData");
            data.value = 99;
            Resources.UnloadAsset(data);
            return "ok";
        });
        Case("unload.data.alive", () => (data != null).ToString());
        Case("unload.data.value", () => data.value.ToString());
        Case("unload.data.reload", () =>
        {
            var again = Resources.Load<ProbeData>("UnloadData");
            return ReferenceEquals(again, data) + " " + again.value;
        });

        Case("unload.prefab", () =>
        {
            Resources.UnloadAsset(Resources.Load<GameObject>("UnloadPrefab"));
            return "ok";
        });
        Case("unload.prefab.transform", () =>
        {
            Resources.UnloadAsset(Resources.Load<GameObject>("UnloadPrefab").transform);
            return "ok";
        });
        Case("unload.runtime", () =>
        {
            var runtime = new Material(solid);
            runtime.color = new Color(0f, 1f, 0f, 1f);
            Resources.UnloadAsset(runtime);
            return (runtime != null) + " " + C(runtime.color);
        });
        Case("unload.null", () =>
        {
            Resources.UnloadAsset(null);
            return "ok";
        });
    }

    // ------------------------------------------------------------------ shadow casters

    static GameObject Caster(string name, Vector3 position, float scale = 1f)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        go.AddComponent<MeshRenderer>();
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    static Light MakeLight(string name, LightType type, Vector3 position, Vector3 euler, float intensity, float range = 10f,
        float angle = 30f, LightShadows shadows = LightShadows.Hard)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = type;
        light.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
        light.intensity = intensity;
        light.range = range;
        light.spotAngle = angle;
        light.shadows = shadows;
        return light;
    }

    void Shadows()
    {
        Rec("shadow.quality", QualitySettings.shadows + " " + F(QualitySettings.shadowDistance));
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var camera = new GameObject("ProbeCamera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(0f, 1f, -10f);
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 200f;
        camera.targetTexture = target;
        camera.cullingMask = ~(1 << 9);

        var casters = new Dictionary<string, GameObject>
        {
            ["A"] = Caster("A", new Vector3(0f, 0f, 0f)),
            ["B"] = Caster("B", new Vector3(4f, 0f, 10f), 2f),
            ["C"] = Caster("C", new Vector3(0f, 0f, -25f)),
            ["D"] = Caster("D", new Vector3(0f, 0f, 100f)),
            ["E"] = Caster("E", new Vector3(-6f, 0f, 5f)),
            ["F"] = Caster("F", new Vector3(6f, 0f, 0f)),
            ["G"] = Caster("G", new Vector3(0f, 0f, 5f)),
            ["H"] = Caster("H", new Vector3(30f, 0f, 5f)),
            ["I"] = Caster("I", new Vector3(0f, 40f, 10f)),
            ["J"] = Caster("J", new Vector3(0f, 0f, 8f)),
            ["K"] = Caster("K", new Vector3(3f, 3f, 0f)),
        };
        casters["E"].GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        casters["F"].GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        casters["G"].layer = 9;
        casters["J"].GetComponent<Renderer>().enabled = false;

        MakeLight("Ldir", LightType.Directional, Vector3.zero, new Vector3(50f, -30f, 0f), 1.0f);
        MakeLight("LdirNone", LightType.Directional, Vector3.zero, new Vector3(30f, 60f, 0f), 0.9f, shadows: LightShadows.None);
        MakeLight("Lpoint", LightType.Point, new Vector3(0f, 2f, 0f), Vector3.zero, 0.8f, range: 4f);
        MakeLight("LpointEmpty", LightType.Point, new Vector3(0f, 1f, 20f), Vector3.zero, 0.7f, range: 3f);
        MakeLight("LpointMask", LightType.Point, new Vector3(0f, 2f, 0f), Vector3.zero, 0.6f, range: 4f).cullingMask = ~1;
        MakeLight("Lspot", LightType.Spot, new Vector3(0f, 6f, 0f), new Vector3(90f, 0f, 0f), 0.5f, range: 10f, angle: 40f);
        MakeLight("LspotAway", LightType.Spot, new Vector3(0f, 1f, -3f), new Vector3(0f, 180f, 0f), 0.4f, range: 30f, angle: 30f);
        MakeLight("LpointBehind", LightType.Point, new Vector3(0f, 0f, -30f), Vector3.zero, 0.3f, range: 6f);
        MakeLight("LspotCone", LightType.Spot, new Vector3(0f, 1f, -12f), new Vector3(0f, 180f, 0f), 0.15f, range: 6f, angle: 30f);
        MakeLight("LpointFar", LightType.Point, new Vector3(0f, 0f, 100f), Vector3.zero, 0.2f, range: 5f);

        var configs = new List<string> { "none" };
        configs.AddRange(casters.Keys);
        configs.Add("all");
        foreach (var config in configs)
        {
            foreach (var pair in casters)
                pair.Value.SetActive(config == "all" || config == pair.Key);
            int culls = 0;
            ProbePipelineAsset.OnCull = (cam, results) =>
            {
                if (cam != camera) return;
                culls++;
                // The Editor's order of the visible lights changes from run to run (directional
                // lights come first): they are recorded by name.
                var lights = results.visibleLights;
                var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < lights.Length; i++)
                {
                    var light = lights[i].light;
                    string name = light != null ? light.name : "null";
                    bool any = results.GetShadowCasterBounds(i, out var bounds);
                    rows[name] = any ? "true " + V(bounds.center) + " " + V(bounds.extents) : "false";
                }
                foreach (var row in rows) Rec("shadow." + config + "." + row.Key, row.Value);
                Rec("shadow." + config + ".lights", string.Join(" ", rows.Keys));
                int directional = 0;
                while (directional < lights.Length && lights[directional].lightType == LightType.Directional) directional++;
                int after = 0;
                for (int i = directional; i < lights.Length; i++) if (lights[i].lightType == LightType.Directional) after++;
                Rec("shadow." + config + ".directionalFirst", directional + " " + after);
            };
            camera.Render();
            ProbePipelineAsset.OnCull = null;
            Rec("shadow." + config + ".culls", culls.ToString());
        }
        camera.targetTexture = null;
        target.Release();
    }
}
