using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shader and material pass counts against the SubShader actually drawn: four probe shaders whose
/// SubShaders and Fallback declare different pass counts (Assets/Shaders), read under per-shader
/// and global maximum LODs, a Fallback reached under the referring shader's limit and under its
/// own, a first SubShader for another render pipeline, a surface SubShader and the boundary where
/// no SubShader is in reach, with every setting restored afterwards.
///
/// Each case first records what it sets (<c>case.N.set</c>), then reads per shader: its
/// maximumLOD, passCount, isSupported, passCountInSubshader, an existing material's and a new
/// material's passCount, the existing material's SubShader tag (with and without fallbacks) and
/// FindPass for every pass name of the shader's chain, the colour an orthographic camera renders
/// of a quad with the existing material (the drawn SubShader's code and its pass count; see the
/// shaders), and passCount again after that render. The declarations block reads each shader's
/// SubShader count, per-SubShader pass counts, SubShader and pass tags.
///
/// Writes <c>UNBOUND_PROBE_SHADERPASSCOUNT_OUT</c>/values.jsonl, one <c>{"k":key,"v":value}</c> per
/// line. Every case is isolated: a read records its value under its key or, when it throws, the
/// exception under <c>key.exception</c>; a step (a call made for its effect) records only its
/// exception; a block ends with <c>block.log</c>, the log messages its cases caused. Values are
/// counts, flags, names and 8-bit colours only: real times, frame numbers, device names and
/// object ids go to diagnostics.jsonl, which is never compared. The last lines are
/// <c>capture.unscopedLogs</c> and the single marker <c>{"k":"complete","v":true}</c>; the file is
/// written once, through a temporary name. Then <see cref="finishedCode"/> is set; the Editor
/// runner polls it. This script never ends Play mode or quits.
/// </summary>
public class ShaderPassCountProbe : MonoBehaviour
{
    public const string OutputVariable = "UNBOUND_PROBE_SHADERPASSCOUNT_OUT";

    /// <summary>Null until the record is written; then 0 (complete) or 3 (fatal).</summary>
    public static int? finishedCode;

    /// <summary>The probe shaders under their record keys.</summary>
    static readonly string[] Keys = { "lod", "fallback", "tagged", "surface" };
    static readonly string[] Names = { "Probe/PassLod", "Probe/PassFallback", "Probe/PassTagged", "Probe/PassSurface" };

    /// <summary>Every pass name of each shader's chain (own SubShaders, then the Fallback's), in
    /// declaration order; the surface SubShader's are the names Unity's generated passes use.</summary>
    static readonly string[][] PassNames =
    {
        new[] { "L0P0", "L0P1", "L0P2", "L1P0", "L2P0", "L2P1", "F0P0", "F0P1", "F0P2", "F0P3", "F1P0", "F1P1", "F1P2", "F1P3", "F1P4" },
        new[] { "F0P0", "F0P1", "F0P2", "F0P3", "F1P0", "F1P1", "F1P2", "F1P3", "F1P4" },
        new[] { "T0P0", "T0P1", "T0P2", "T1P0", "T1P1" },
        new[] { "FORWARD", "FORWARD_DELTA", "DEFERRED", "META", "S1P0", "S1P1", "S1P2" },
    };

    const string SubTag = "SubId";
    const string PassTag = "PassId";
    const string NoTag = "none";
    const int Size = 16;
    const int SettleFrames = 5;

    /// <summary>Restores the value read before the first case.</summary>
    const int Restore = int.MinValue;

    /// <summary>The cases in order. Each sets the listed maximum LODs (null: unchanged), so the
    /// settings accumulate: global25shader350 keeps global25's global limit.</summary>
    static readonly (string Name, int? Lod, int? Fallback, int? Surface, int? Global)[] Cases =
    {
        ("default", null, null, null, null),
        ("shader350", 350, null, null, null),
        ("shader250", 250, null, null, null),
        ("shader175", 175, null, null, null),
        ("shader100", 100, null, null, null),
        ("shader25", 25, null, null, null),
        ("shaderRestored", Restore, null, null, null),
        ("global250", null, null, null, 250),
        ("global100", null, null, null, 100),
        ("global25", null, null, null, 25),
        ("global25shader350", 350, null, null, null),
        ("global25shader100", 100, null, null, null),
        ("globalRestored", Restore, null, null, Restore),
        ("surface250", null, null, 250, null),
        ("surfaceRestored", null, null, Restore, null),
        ("fallback100shader175", 175, 100, null, null),
        ("fallbackRestored", Restore, Restore, null, null),
    };

    readonly StringBuilder records = new StringBuilder();
    readonly StringBuilder diagnostics = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<string> unscoped = new List<string>();
    bool capturing;

    readonly Shader[] shaders = new Shader[4];
    readonly Material[] materials = new Material[4];
    readonly int[] initialLod = new int[4];
    int initialGlobal;
    bool initialRead;
    GameObject cameraObject, quad;
    Camera view;
    Renderer quadRenderer;
    RenderTexture target;
    Texture2D readback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(OutputVariable) == null) return;
        finishedCode = null;
        new GameObject("ShaderPassCountProbe").AddComponent<ShaderPassCountProbe>();
    }

    // ------------------------------------------------------------------ record

    void Value(string k, string json) { records.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(json).Append("}\n"); }

    static string Q(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string B(bool b) => b ? "true" : "false";
    static string N(long n) => n.ToString(CultureInfo.InvariantCulture);
    static string List(IEnumerable<string> items) => "[" + string.Join(",", items.Select(Q)) + "]";
    static string Ints(IEnumerable<int> items) => "[" + string.Join(",", items.Select(i => N(i))) + "]";
    static string Error(Exception e) => Q(e.GetType().Name + ": " + e.Message);

    /// <summary>A setup value a step failed to produce: the dependent reads record this instead.</summary>
    static T Have<T>(T value) where T : class => value ?? throw new InvalidOperationException("the setup step did not produce a value");

    void OnLog(string condition, string stack, LogType type)
    {
        (capturing ? logs : unscoped).Add(type + ": " + condition);
    }

    /// <summary>One read: its value under <paramref name="k"/>, or its exception under k.exception.</summary>
    void Read(string k, Func<string> json)
    {
        string value;
        try { value = json(); }
        catch (Exception e) { Value(k + ".exception", Error(e)); return; }
        Value(k, value);
    }

    /// <summary>One call made for its effect: only an exception is recorded (k.exception).</summary>
    void Step(string k, Action action)
    {
        try { action(); }
        catch (Exception e) { Value(k + ".exception", Error(e)); }
    }

    /// <summary>A group of cases and the log messages they cause (key.log). The cases catch their
    /// own exceptions; key.exception only records a fault of the probe itself.</summary>
    void Block(string key, Action body)
    {
        logs.Clear();
        capturing = true;
        try { body(); }
        catch (Exception e) { Value(key + ".exception", Error(e)); }
        capturing = false;
        Value(key + ".log", List(logs));
        logs.Clear();
    }

    /// <summary>A raw diagnostic (real time, frame, device): never part of the compared record.</summary>
    void Diagnostic(string what)
    {
        diagnostics.Append("{\"what\":").Append(Q(what))
            .Append(",\"frame\":").Append(N(Time.frameCount))
            .Append(",\"realtime\":").Append(Time.realtimeSinceStartupAsDouble.ToString("R", CultureInfo.InvariantCulture))
            .Append(",\"device\":").Append(Q(SystemInfo.graphicsDeviceName))
            .Append(",\"deviceVersion\":").Append(Q(SystemInfo.graphicsDeviceVersion))
            .Append("}\n");
    }

    // ------------------------------------------------------------------ run

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        Diagnostic("start");
        int code = 0;
        try { Run(); }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 3;
        }
        // Before the record is written and before any yield, also after a fatal error.
        Block("cleanup", Cleanup);
        Application.logMessageReceived -= OnLog;
        Value("capture.unscopedLogs", List(unscoped));
        if (code == 0) Value("complete", "true");
        string directory = Environment.GetEnvironmentVariable(OutputVariable);
        string temporary = Path.Combine(directory, "values.jsonl.partial");
        File.WriteAllText(temporary, records.ToString());
        File.Move(temporary, Path.Combine(directory, "values.jsonl"));
        Diagnostic("written");
        File.WriteAllText(Path.Combine(directory, "diagnostics.jsonl"), diagnostics.ToString());
        // Let Play mode settle a few frames before the runner may leave it.
        for (int i = 0; i < SettleFrames; i++) yield return null;
        finishedCode = code;
    }

    void Run()
    {
        Context();
        Setup();
        for (int i = 0; i < Keys.Length; i++) Declarations(i);
        foreach (var c in Cases) Case(c);
    }

    /// <summary>What the capture ran on (pipeline and settings that decide SubShader selection).</summary>
    void Context()
    {
        Block("context", () =>
        {
            Read("context.unityVersion", () => Q(Application.unityVersion));
            Read("context.isBatchMode", () => B(Application.isBatchMode));
            Read("context.platform", () => Q(Application.platform.ToString()));
            Read("context.graphicsDeviceType", () => Q(SystemInfo.graphicsDeviceType.ToString()));
            Read("context.graphicsShaderLevel", () => N(SystemInfo.graphicsShaderLevel));
            Read("context.qualityLevel", () => N(QualitySettings.GetQualityLevel()));
            Read("context.qualityName", () => Q(QualitySettings.names[QualitySettings.GetQualityLevel()]));
            Read("context.colorSpace", () => Q(QualitySettings.activeColorSpace.ToString()));
            Read("context.renderPipeline", () => Q(GraphicsSettings.currentRenderPipeline == null ? null : GraphicsSettings.currentRenderPipeline.name));
            Read("context.defaultRenderPipeline", () => Q(GraphicsSettings.defaultRenderPipeline == null ? null : GraphicsSettings.defaultRenderPipeline.name));
            Read("context.qualityRenderPipeline", () => Q(QualitySettings.renderPipeline == null ? null : QualitySettings.renderPipeline.name));
            Read("context.globalRenderPipeline", () => Q(Shader.globalRenderPipeline));
        });
    }

    /// <summary>The shaders, their materials, the camera and the quad it renders.</summary>
    void Setup()
    {
        Block("setup", () =>
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                int s = i;
                Read("setup." + Keys[s] + ".found", () => B((shaders[s] = Shader.Find(Names[s])) != null));
                Step("setup." + Keys[s] + ".step[new Material]", () => materials[s] = new Material(Have(shaders[s])) { name = "Probe" + s });
                Read("setup." + Keys[s] + ".maximumLOD", () => N(initialLod[s] = Have(shaders[s]).maximumLOD));
            }
            Read("setup.globalMaximumLOD", () => N(initialGlobal = Shader.globalMaximumLOD));
            initialRead = shaders.All(s => s != null);
            Step("setup.step[ambient]", () =>
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.fog = false;
            });
            Step("setup.step[camera]", () =>
            {
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
                cameraObject = new GameObject("ProbeCamera");
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                view = cameraObject.AddComponent<Camera>();
                view.enabled = false;
                view.orthographic = true;
                view.orthographicSize = 1f;
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = Color.black;
                view.renderingPath = RenderingPath.Forward;
                view.allowHDR = false;
                view.allowMSAA = false;
                view.targetTexture = target;
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.position = Vector3.zero;
                quad.transform.localScale = new Vector3(4f, 4f, 1f);
                quadRenderer = quad.GetComponent<Renderer>();
                quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
            });
            Read("setup.actualRenderingPath", () => Q(Have(view).actualRenderingPath.ToString()));
            Read("setup.background", () => Render(null));
        });
    }

    /// <summary>The colour the camera renders at the quad's centre with <paramref name="material"/>
    /// (null: the quad hidden), as 8-bit RGBA, and whether every pixel has it.</summary>
    string Render(Material material)
    {
        Have(view);
        quadRenderer.enabled = material != null;
        quadRenderer.sharedMaterial = material;
        view.Render();
        var active = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        readback.Apply(false);
        RenderTexture.active = active;
        var pixels = readback.GetPixels32();
        var c = pixels[(Size / 2) * Size + Size / 2];
        bool uniform = pixels.All(p => p.r == c.r && p.g == c.g && p.b == c.b && p.a == c.a);
        return "{\"rgba\":[" + N(c.r) + "," + N(c.g) + "," + N(c.b) + "," + N(c.a) + "],\"uniform\":" + B(uniform) + "}";
    }

    void Declarations(int i)
    {
        string key = "decl." + Keys[i];
        Block(key, () =>
        {
            var shader = shaders[i];
            int subs = 0;
            Read(key + ".subshaderCount", () => N(subs = Have(shader).subshaderCount));
            Read(key + ".passCountInSubshader", () => Ints(Enumerable.Range(0, subs).Select(s => Have(shader).GetPassCountInSubshader(s))));
            Read(key + ".subIds", () => List(Enumerable.Range(0, subs).Select(s => TagName(Have(shader).FindSubshaderTagValue(s, new ShaderTagId(SubTag))))));
            Read(key + ".renderPipelineTags", () => List(Enumerable.Range(0, subs).Select(s => TagName(Have(shader).FindSubshaderTagValue(s, new ShaderTagId("RenderPipeline"))))));
            Read(key + ".passIds", () => List(PassTags(Have(shader), subs, PassTag)));
            Read(key + ".passLightModes", () => List(PassTags(Have(shader), subs, "LightMode")));
        });
    }

    static string TagName(ShaderTagId id) => id == ShaderTagId.none ? "-" : id.name;

    static IEnumerable<string> PassTags(Shader shader, int subs, string tag)
    {
        var all = new List<string>();
        for (int s = 0; s < subs; s++)
            for (int p = 0; p < shader.GetPassCountInSubshader(s); p++)
                all.Add(s + "." + p + ":" + TagName(shader.FindPassTagValue(s, p, new ShaderTagId(tag))));
        return all;
    }

    static string SetJson(int? value) => value == null ? null : value.Value == Restore ? "\"initial\"" : N(value.Value);

    void Case((string Name, int? Lod, int? Fallback, int? Surface, int? Global) c)
    {
        string key = "case." + c.Name;
        Block(key, () =>
        {
            var set = new List<string>();
            foreach (var (field, value) in new[] { ("lod", c.Lod), ("fallback", c.Fallback), ("surface", c.Surface), ("global", c.Global) })
                if (value != null) set.Add(Q(field) + ":" + SetJson(value));
            Value(key + ".set", "{" + string.Join(",", set) + "}");
            if (c.Lod != null) Step(key + ".step[lod.maximumLOD]", () => SetLod(0, c.Lod.Value));
            if (c.Fallback != null) Step(key + ".step[fallback.maximumLOD]", () => SetLod(1, c.Fallback.Value));
            if (c.Surface != null) Step(key + ".step[surface.maximumLOD]", () => SetLod(3, c.Surface.Value));
            if (c.Global != null) Step(key + ".step[globalMaximumLOD]", () =>
            {
                if (c.Global.Value == Restore && !initialRead) throw new InvalidOperationException("the initial LODs were not read");
                Shader.globalMaximumLOD = c.Global.Value == Restore ? initialGlobal : c.Global.Value;
            });
            Read(key + ".globalMaximumLOD", () => N(Shader.globalMaximumLOD));
            for (int i = 0; i < Keys.Length; i++) Readback(key + "." + Keys[i], i);
        });
    }

    void SetLod(int i, int value)
    {
        if (value == Restore && !initialRead) throw new InvalidOperationException("the initial LODs were not read");
        Have(shaders[i]).maximumLOD = value == Restore ? initialLod[i] : value;
    }

    void Readback(string key, int i)
    {
        var shader = shaders[i];
        var material = materials[i];
        Read(key + ".maximumLOD", () => N(Have(shader).maximumLOD));
        Read(key + ".passCount", () => N(Have(shader).passCount));
        Read(key + ".isSupported", () => B(Have(shader).isSupported));
        Read(key + ".passCountInSubshader", () => Ints(Enumerable.Range(0, Have(shader).subshaderCount).Select(s => shader.GetPassCountInSubshader(s))));
        Read(key + ".mat.passCount", () => N(Have(material).passCount));
        Material fresh = null;
        try
        {
            Step(key + ".step[new Material]", () => fresh = new Material(Have(shader)) { name = "Fresh" + i });
            Read(key + ".newMat.passCount", () => N(Have(fresh).passCount));
        }
        finally
        {
            if (fresh != null) DestroyImmediate(fresh);
        }
        Read(key + ".mat.subTag", () => Q(Have(material).GetTag(SubTag, false, NoTag)));
        Read(key + ".mat.subTagFallbacks", () => Q(Have(material).GetTag(SubTag, true, NoTag)));
        Read(key + ".mat.findPass", () => Ints(PassNames[i].Select(n => Have(material).FindPass(n))));
        Read(key + ".color", () => Render(Have(material)));
        Read(key + ".passCount.after", () => N(Have(shader).passCount));
        Read(key + ".mat.passCount.after", () => N(Have(material).passCount));
    }

    /// <summary>Restores every maximum LOD read before the cases and destroys what the probe made.</summary>
    void Cleanup()
    {
        if (initialRead)
        {
            for (int i = 0; i < shaders.Length; i++) shaders[i].maximumLOD = initialLod[i];
            Shader.globalMaximumLOD = initialGlobal;
        }
        if (view != null) view.targetTexture = null;
        if (quad != null) DestroyImmediate(quad);
        if (cameraObject != null) DestroyImmediate(cameraObject);
        if (target != null) { target.Release(); DestroyImmediate(target); }
        if (readback != null) DestroyImmediate(readback);
        foreach (var m in materials) if (m != null) DestroyImmediate(m);
    }
}
