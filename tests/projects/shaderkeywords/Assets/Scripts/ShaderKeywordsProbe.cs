using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shader keyword declarations and shader metadata: the capture context, each shader's and
/// compute shader's keyword space (names in order, type, dynamic, overridable), FindKeyword,
/// global keywords, material keywords by LocalKeyword and by name (undeclared names included),
/// SubShader and pass counts and tags, property counts, and material variants loaded from assets
/// (parent, overrides, locks, reverts).
///
/// Writes <c>UNBOUND_PROBE_SHADERKEYWORDS_OUT</c>/values.jsonl, one <c>{"k":key,"v":value}</c>
/// per line. Every case is isolated: a read records its value under its key or, when it throws,
/// the exception under <c>key.exception</c>; a step (a call made for its effect) records only
/// its exception. A block also records the log messages it caused under <c>block.log</c>.
/// The last line is <c>{"k":"complete","v":true}</c>. Then <see cref="finishedCode"/> is set; the
/// Editor runner polls it. This script never ends Play mode or quits.
///
/// Excluded on purpose: the type, isDynamic and isOverridable of an invalid or default
/// LocalKeyword (only valid keywords have them read), Material.SetKeyword with a default
/// LocalKeyword, and CommandBuffer keyword calls.
/// </summary>
public class ShaderKeywordsProbe : MonoBehaviour
{
    public const string OutputVariable = "UNBOUND_PROBE_SHADERKEYWORDS_OUT";

    /// <summary>Null until the record is written; then 0 (complete) or 3 (fatal).</summary>
    public static int? finishedCode;

    static readonly string[] ProbeShaders = { "Probe/KwForms", "Probe/KwPlain", "Probe/KwHlsl", "Probe/KwSurface", "Probe/KwFixed" };

    static readonly string[] BuiltinShaders =
    {
        "Standard", "Standard (Specular setup)", "Autodesk Interactive", "Unlit/Color", "Unlit/Texture",
        "Unlit/Transparent", "Unlit/Transparent Cutout", "UI/Default", "UI/Unlit/Text", "Sprites/Default",
        "Sprites/Mask", "Particles/Standard Unlit", "Particles/Standard Surface", "Legacy Shaders/Diffuse",
        "Legacy Shaders/VertexLit", "Legacy Shaders/Particles/Additive", "Legacy Shaders/Transparent/Cutout/Diffuse",
        "Mobile/Diffuse", "Mobile/Particles/Additive", "Skybox/Procedural", "Skybox/6 Sided", "Skybox/Cubemap",
        "Nature/Terrain/Standard", "Nature/Terrain/Diffuse", "GUI/Text Shader", "Hidden/Internal-Colored",
        "Hidden/InternalErrorShader", "Hidden/BlitCopy", "Hidden/Internal-ScreenSpaceShadows",
        "Hidden/Internal-DeferredShading", "Hidden/Internal-StencilWrite", "Hidden/Internal-GUITexture",
    };

    static readonly string[] SubshaderTags = { "RenderType", "Queue", "RenderPipeline", "ProbeTag", "IgnoreProjector", "PreviewType", "DisableBatching", "LightMode" };
    static readonly string[] Probes = { "GLOBAL_A", "LOCAL_A", "SHARED_KW", "DYN_GLOBAL", "DYN_LOCAL_A", "LIGHTMAP_ON", "DIRECTIONAL", "INSTANCING_ON", "STEREO_INSTANCING_ON", "PLAIN_INCLUDE_KW", "D3D_ONLY", "NOT_DECLARED", "" };
    static readonly string[] ComputeProbes = { "CS_GLOBAL_A", "CS_LOCAL", "CS_DYN", "LIGHTMAP_ON", "NOT_DECLARED", "" };
    static readonly string[] GlobalNames = { "GLOBAL_A", "LOCAL_A", "SHARED_KW", "DYN_GLOBAL", "DYN_LOCAL_A", "INCLUDED_GLOBAL", "INCLUDED_LOCAL", "PLAIN_INCLUDE_KW", "SUB2_GLOBAL", "CS_GLOBAL_A", "CS_LOCAL", "CS_DYN", "DIRECTIONAL", "LIGHTMAP_ON", "FOG_LINEAR", "STEREO_INSTANCING_ON", "_NORMALMAP", "UNITY_UI_CLIP_RECT", "NEVER_SEEN_KW", "CREATED_KW", "STRING_ENABLED_KW" };
    static readonly string[] VariantProps = { "_Color", "_Value", "_Other", "_MainTex", "_Missing" };

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<string> unscoped = new List<string>();
    bool capturing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(OutputVariable) == null) return;
        finishedCode = null;
        new GameObject("ShaderKeywordsProbe").AddComponent<ShaderKeywordsProbe>();
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
    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? Q(f.ToString("R", CultureInfo.InvariantCulture)) : f.ToString("R", CultureInfo.InvariantCulture);
    static string List(IEnumerable<string> items) => "[" + string.Join(",", items.Select(Q)) + "]";
    static string Color(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string Error(Exception e) => Q(e.GetType().Name + ": " + e.Message);

    /// <summary>A setup value a step failed to produce: the dependent reads record this instead.</summary>
    static T Have<T>(T? value) where T : struct => value ?? throw new InvalidOperationException("the setup step did not produce a value");
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

    // Reading the type or flags of a keyword that is not in its space is excluded (a default
    // LocalKeyword has no space): only valid keywords have them recorded.
    static string Keyword(LocalKeyword k) => !k.isValid ? "{\"name\":" + Q(k.name) + ",\"isValid\":false}" :
        "{\"name\":" + Q(k.name) + ",\"type\":" + Q(k.type.ToString()) + ",\"isDynamic\":" + B(k.isDynamic)
        + ",\"isOverridable\":" + B(k.isOverridable) + ",\"isValid\":" + B(k.isValid) + "}";

    void Space(string key, Func<LocalKeywordSpace> space, IEnumerable<string> extra)
    {
        Read(key + ".keywordCount", () => N(space().keywordCount));
        string[] names = null;
        Read(key + ".keywordNames", () => List(names = space().keywordNames));
        Read(key + ".keywords", () => "[" + string.Join(",", space().keywords.Select(Keyword)) + "]");
        foreach (var name in (names ?? new string[0]).Concat(extra).Distinct())
            Read(key + ".find[" + name + "]", () => Keyword(space().FindKeyword(name)));
    }

    // ------------------------------------------------------------------ run

    System.Collections.IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        int code = 0;
        try { Run(); }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 3;
        }
        Application.logMessageReceived -= OnLog;
        Value("capture.unscopedLogs", List(unscoped));
        if (code == 0) Value("complete", "true");
        string directory = Environment.GetEnvironmentVariable(OutputVariable);
        string temporary = Path.Combine(directory, "values.jsonl.partial");
        File.WriteAllText(temporary, records.ToString());
        File.Move(temporary, Path.Combine(directory, "values.jsonl"));
        // Let Play mode settle a few frames before the runner may leave it.
        for (int i = 0; i < 5; i++) yield return null;
        finishedCode = code;
    }

    void Run()
    {
        Context();
        foreach (var name in ProbeShaders) ShaderMetadata("probe." + name, name, Probes, true);
        foreach (var name in BuiltinShaders) ShaderMetadata("builtin." + name, name, new string[0], false);
        Compute();
        LocalKeywords();
        GlobalKeywords();
        MaterialKeywords();
        SavedKeywords();
        Variants();
    }

    /// <summary>What the capture ran on: enabled global keywords and platform-conditional
    /// declarations hold for this context only.</summary>
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
            Read("context.qualityShadows", () => Q(QualitySettings.shadows.ToString()));
            Read("context.colorSpace", () => Q(QualitySettings.activeColorSpace.ToString()));
            Read("context.renderPipeline", () => Q(GraphicsSettings.currentRenderPipeline == null ? null : GraphicsSettings.currentRenderPipeline.name));
        });
    }

    void ShaderMetadata(string key, string name, string[] extra, bool details)
    {
        Shader shader = null;
        Read(key + ".found", () => B((shader = Shader.Find(name)) != null));
        if (shader == null) return;
        Block(key + ".meta", () =>
        {
            int subs = 0;
            Read(key + ".subshaderCount", () => N(subs = shader.subshaderCount));
            Read(key + ".passCount", () => N(shader.passCount));
            Read(key + ".propertyCount", () => N(shader.GetPropertyCount()));
            Read(key + ".properties", () =>
            {
                var props = new List<string>();
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                    props.Add(shader.GetPropertyName(i) + ":" + shader.GetPropertyType(i) + ":" + (int)shader.GetPropertyFlags(i));
                return List(props);
            });
            Read(key + ".passCountInSubshader", () =>
                List(Enumerable.Range(0, subs).Select(s => N(shader.GetPassCountInSubshader(s)))));
            Read(key + ".subshaderTags", () =>
            {
                var tags = new List<string>();
                for (int s = 0; s < subs; s++)
                    foreach (var t in SubshaderTags)
                    {
                        var v = shader.FindSubshaderTagValue(s, new ShaderTagId(t));
                        if (v != ShaderTagId.none) tags.Add(s + ":" + t + "=" + v.name);
                    }
                return List(tags);
            });
            Read(key + ".passLightModes", () =>
            {
                var passTags = new List<string>();
                for (int s = 0; s < subs; s++)
                    for (int p = 0; p < shader.GetPassCountInSubshader(s); p++)
                    {
                        var v = shader.FindPassTagValue(s, p, new ShaderTagId("LightMode"));
                        passTags.Add(s + "." + p + ":" + (v == ShaderTagId.none ? "-" : v.name));
                    }
                return List(passTags);
            });
        });
        Block(key + ".space", () => Space(key + ".space", () => shader.keywordSpace, extra));
        if (!details) return;
        Block(key + ".outOfRange", () =>
        {
            Read(key + ".passCountInSubshader[-1]", () => N(shader.GetPassCountInSubshader(-1)));
            Read(key + ".passCountInSubshader[count]", () => N(shader.GetPassCountInSubshader(shader.subshaderCount)));
            Read(key + ".subshaderTag[count]", () => Q(shader.FindSubshaderTagValue(shader.subshaderCount, new ShaderTagId("RenderType")).name));
            Read(key + ".subshaderTag[-1]", () => Q(shader.FindSubshaderTagValue(-1, new ShaderTagId("RenderType")).name));
        });
    }

    void Compute()
    {
        ComputeShader cs = null;
        Read("compute.found", () => B((cs = Resources.Load<ComputeShader>("KwCompute")) != null));
        if (cs == null) return;
        Block("compute.space", () => Space("compute.space", () => cs.keywordSpace, ComputeProbes));
        Block("compute.keywords", () =>
        {
            LocalKeywordSpace? space = null;
            Step("compute.step[keywordSpace]", () => space = cs.keywordSpace);
            Step("compute.step[EnableKeyword(kw CS_LOCAL)]", () => cs.EnableKeyword(Have(space).FindKeyword("CS_LOCAL")));
            Step("compute.step[SetKeyword(kw CS_GLOBAL_A, true)]", () => cs.SetKeyword(Have(space).FindKeyword("CS_GLOBAL_A"), true));
            Step("compute.step[EnableKeyword(NOT_DECLARED)]", () => cs.EnableKeyword("NOT_DECLARED"));
            Read("compute.shaderKeywords", () => List(cs.shaderKeywords));
            Read("compute.enabledKeywords", () => List(cs.enabledKeywords.Select(k => k.name)));
            Read("compute.IsKeywordEnabled(NOT_DECLARED)", () => B(cs.IsKeywordEnabled("NOT_DECLARED")));
            Read("compute.IsKeywordEnabled(kw CS_LOCAL)", () => B(cs.IsKeywordEnabled(Have(space).FindKeyword("CS_LOCAL"))));
            Read("compute.space==", () => B(cs.keywordSpace == Have(space)));
        });
    }

    void LocalKeywords()
    {
        var forms = Shader.Find("Probe/KwForms");
        var plain = Shader.Find("Probe/KwPlain");
        Block("local.default", () =>
        {
            // isValid and name of a default keyword only; never its type or flags.
            Read("local.default.keyword", () => Keyword(default(LocalKeyword)));
            Read("local.default.ToString", () => Q(default(LocalKeyword).ToString()));
        });
        Block("local.construct", () =>
        {
            LocalKeyword? a = null, b = null, other = null;
            Step("local.step[ctor(LOCAL_A)]", () => a = new LocalKeyword(forms, "LOCAL_A"));
            Step("local.step[find(LOCAL_A)]", () => b = forms.keywordSpace.FindKeyword("LOCAL_A"));
            Read("local.ctor(LOCAL_A)", () => Keyword(Have(a)));
            Read("local.ctor==find", () => B(Have(a) == Have(b)));
            Read("local.ctor.Equals", () => B(Have(a).Equals(Have(b))));
            Read("local.ToString", () => Q(Have(a).ToString()));
            Read("local.ctor(NOT_DECLARED)", () => Keyword(new LocalKeyword(forms, "NOT_DECLARED")));
            Read("local.ctor(GLOBAL_A)", () => Keyword(new LocalKeyword(forms, "GLOBAL_A")));
            Step("local.step[ctor(plain, LOCAL_A)]", () => other = new LocalKeyword(plain, "LOCAL_A"));
            Read("local.ctor(plain, LOCAL_A)", () => Keyword(Have(other)));
            Read("local.otherSpace==", () => B(Have(other) == Have(a)));
            Read("local.space==", () => B(forms.keywordSpace == forms.keywordSpace));
            Read("local.space!=other", () => B(forms.keywordSpace != plain.keywordSpace));
            Read("local.ctor(null shader)", () => Keyword(new LocalKeyword((Shader)null, "LOCAL_A")));
        });
    }

    // The global keyword registry is process-wide and filled while the Editor loads shaders:
    // its sets are recorded sorted, not in registry order.
    static string SortedGlobals(IEnumerable<GlobalKeyword> keywords) =>
        List(keywords.Select(k => k.name).OrderBy(n => n, StringComparer.Ordinal));

    void GlobalKeywords()
    {
        Block("global.initial", () =>
        {
            Read("global.enabled.initial", () => SortedGlobals(Shader.enabledGlobalKeywords));
            HashSet<string> all = null;
            Step("global.step[globalKeywords]", () => all = new HashSet<string>(Shader.globalKeywords.Select(k => k.name)));
            foreach (var n in GlobalNames)
                Read("global.all.contains[" + n + "]", () => B(Have(all).Contains(n)));
        });
        Block("global.builtin", () =>
        {
            // The global keywords the engine types as built-in (not user-defined), by type.
            Read("global.builtin.types", () =>
            {
                var builtin = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var k in Shader.globalKeywords)
                {
#pragma warning disable 618
                    var type = ShaderKeyword.GetGlobalKeywordType(new ShaderKeyword(k.name));
#pragma warning restore 618
                    if (type == ShaderKeywordType.UserDefined) continue;
                    if (!builtin.TryGetValue(type.ToString(), out var names)) builtin[type.ToString()] = names = new List<string>();
                    names.Add(k.name);
                }
                return "{" + string.Join(",", builtin.Select(pair =>
                    Q(pair.Key) + ":" + List(pair.Value.OrderBy(n => n, StringComparer.Ordinal)))) + "}";
            });
        });
        Block("global.create", () =>
        {
            GlobalKeyword? created = null;
            Step("global.step[Create(CREATED_KW)]", () => created = GlobalKeyword.Create("CREATED_KW"));
            Read("global.create.name", () => Q(Have(created).name));
            Read("global.create.ToString", () => Q(Have(created).ToString()));
            Read("global.create.inAll", () => B(Shader.globalKeywords.Any(k => k.name == "CREATED_KW")));
            Read("global.create.enabled", () => B(Shader.IsKeywordEnabled(Have(created))));
            Read("global.create.again", () => Q(GlobalKeyword.Create("CREATED_KW").name));
            Read("global.create.count", () => N(Shader.globalKeywords.Count(k => k.name == "CREATED_KW")));
        });
        Block("global.ctor", () =>
        {
            Read("global.ctor.unknown.name", () => Q(new GlobalKeyword("NEVER_SEEN_CTOR").name));
            Read("global.ctor.unknown.inAll", () => B(Shader.globalKeywords.Any(k => k.name == "NEVER_SEEN_CTOR")));
            Read("global.ctor.known.name", () => Q(new GlobalKeyword("GLOBAL_A").name));
        });
        Block("global.enable", () =>
        {
            Step("global.step[EnableKeyword(STRING_ENABLED_KW)]", () => Shader.EnableKeyword("STRING_ENABLED_KW"));
            Step("global.step[EnableKeyword(gk GLOBAL_A)]", () => Shader.EnableKeyword(new GlobalKeyword("GLOBAL_A")));
            Step("global.step[SetKeyword(Create(SET_KW), true)]", () => Shader.SetKeyword(GlobalKeyword.Create("SET_KW"), true));
            Read("global.enabled.after", () => SortedGlobals(Shader.enabledGlobalKeywords));
            Read("global.after.contains[STRING_ENABLED_KW]", () => B(Shader.globalKeywords.Any(k => k.name == "STRING_ENABLED_KW")));
            Read("global.IsKeywordEnabled(GLOBAL_A)", () => B(Shader.IsKeywordEnabled("GLOBAL_A")));
            Read("global.IsKeywordEnabled(gk STRING_ENABLED_KW)", () => B(Shader.IsKeywordEnabled(new GlobalKeyword("STRING_ENABLED_KW"))));
            Step("global.step[DisableKeyword(STRING_ENABLED_KW)]", () => Shader.DisableKeyword("STRING_ENABLED_KW"));
            Step("global.step[DisableKeyword(GLOBAL_A)]", () => Shader.DisableKeyword("GLOBAL_A"));
            Step("global.step[SetKeyword(gk SET_KW, false)]", () => Shader.SetKeyword(new GlobalKeyword("SET_KW"), false));
            Read("global.enabled.restored", () => SortedGlobals(Shader.enabledGlobalKeywords));
        });
    }

    void Describe(string key, Material m)
    {
        Read(key + ".shaderKeywords", () => List(m.shaderKeywords));
        Read(key + ".enabledKeywords", () => "[" + string.Join(",", m.enabledKeywords.Select(Keyword)) + "]");
    }

    static LocalKeyword Kw(Shader shader, string name) => shader.keywordSpace.FindKeyword(name);

    void MaterialKeywords()
    {
        var forms = Shader.Find("Probe/KwForms");
        var plain = Shader.Find("Probe/KwPlain");
        Block("mat.new", () =>
        {
            Material m = null;
            Step("mat.new.step[new Material(forms)]", () => m = new Material(forms));
            Describe("mat.new", m);
            Step("mat.new.step[SetKeyword(kw LOCAL_A, true)]", () => m.SetKeyword(Kw(forms, "LOCAL_A"), true));
            Step("mat.new.step[EnableKeyword(kw GLOBAL_A)]", () => m.EnableKeyword(Kw(forms, "GLOBAL_A")));
            Step("mat.new.step[EnableKeyword(kw DYN_LOCAL_A)]", () => m.EnableKeyword(Kw(forms, "DYN_LOCAL_A")));
            Describe("mat.local", m);
            Read("mat.IsKeywordEnabled(kw LOCAL_A)", () => B(m.IsKeywordEnabled(Kw(forms, "LOCAL_A"))));
            Read("mat.IsKeywordEnabled(LOCAL_A)", () => B(m.IsKeywordEnabled("LOCAL_A")));
            Step("mat.new.step[EnableKeyword(NOT_DECLARED)]", () => m.EnableKeyword("NOT_DECLARED"));
            Step("mat.new.step[EnableKeyword(LOCAL_B)]", () => m.EnableKeyword("LOCAL_B"));
            Describe("mat.string", m);
            Read("mat.IsKeywordEnabled(NOT_DECLARED)", () => B(m.IsKeywordEnabled("NOT_DECLARED")));
            Read("mat.IsKeywordEnabled(kw NOT_DECLARED)", () => B(m.IsKeywordEnabled(Kw(forms, "NOT_DECLARED"))));
            Step("mat.new.step[DisableKeyword(kw LOCAL_A)]", () => m.DisableKeyword(Kw(forms, "LOCAL_A")));
            Step("mat.new.step[SetKeyword(kw GLOBAL_A, false)]", () => m.SetKeyword(Kw(forms, "GLOBAL_A"), false));
            Describe("mat.disabled", m);
            Step("mat.new.step[shader = plain]", () => m.shader = plain);
            Describe("mat.toPlain", m);
            Read("mat.toPlain.IsKeywordEnabled(LOCAL_B)", () => B(m.IsKeywordEnabled("LOCAL_B")));
            Step("mat.new.step[shader = forms]", () => m.shader = forms);
            Describe("mat.back", m);
        });
        Block("mat.shaderKeywords", () =>
        {
            Material m = null;
            Step("mat.shaderKeywords.step[new Material(forms)]", () => m = new Material(forms));
            Step("mat.shaderKeywords.step[shaderKeywords =]", () => m.shaderKeywords = new[] { "GLOBAL_A", "BOGUS", "LOCAL_A", "BOGUS", "", "DYN_GLOBAL", "AAA_FIRST" });
            Describe("mat.set", m);
            Step("mat.shaderKeywords.step[enabledKeywords =]", () => m.enabledKeywords = new[] { Kw(forms, "LOCAL_C"), Kw(forms, "FEATURE_A") });
            Describe("mat.setEnabled", m);
        });
        Block("mat.otherSpace", () =>
        {
            Material m = null;
            Step("mat.otherSpace.step[new Material(forms)]", () => m = new Material(forms));
            Step("mat.otherSpace.step[SetKeyword(plain LOCAL_A, true)]", () => m.SetKeyword(new LocalKeyword(plain, "LOCAL_A"), true));
            Describe("mat.otherSpace", m);
        });
        Block("mat.toggle", () =>
        {
            Material m = null;
            Step("mat.toggle.step[new Material(forms)]", () => m = new Material(forms));
            Read("mat.toggle.keyword", () => B(m.IsKeywordEnabled("TOGGLE_KW")));
            Step("mat.toggle.step[SetFloat(_Toggle, 1)]", () => m.SetFloat("_Toggle", 1));
            Read("mat.toggle.afterSetFloat", () => B(m.IsKeywordEnabled("TOGGLE_KW")));
        });
    }

    void SavedKeywords()
    {
        Block("saved", () =>
        {
            Material m = null, copy = null;
            Read("saved.found", () => B((m = Resources.Load<Material>("KwSaved")) != null));
            if (m == null) return;
            Describe("saved", m);
            Read("saved.IsKeywordEnabled(STALE_KW)", () => B(m.IsKeywordEnabled("STALE_KW")));
            Step("saved.step[new Material(saved)]", () => copy = new Material(m));
            Describe("saved.copy", copy);
            Step("saved.step[copy.shader = KwHlsl]", () => copy.shader = Shader.Find("Probe/KwHlsl"));
            Describe("saved.copy.toHlsl", copy);
        });
    }

    void Variant(string key, Material m, Material[] family)
    {
        Read(key + ".isVariant", () => B(m.isVariant));
        Read(key + ".parent", () => Q(m.parent != null ? m.parent.name : null));
        foreach (var f in family) Read(key + ".IsChildOf(" + f.name + ")", () => B(m.IsChildOf(f)));
        foreach (var p in VariantProps)
        {
            Read(key + ".IsPropertyOverriden(" + p + ")", () => B(m.IsPropertyOverriden(p)));
            Read(key + ".IsPropertyLocked(" + p + ")", () => B(m.IsPropertyLocked(p)));
            Read(key + ".IsPropertyLockedByAncestor(" + p + ")", () => B(m.IsPropertyLockedByAncestor(p)));
        }
        Read(key + ".color", () => Color(m.GetColor("_Color")));
        Read(key + ".value", () => F(m.GetFloat("_Value")));
        Read(key + ".other", () => F(m.GetFloat("_Other")));
        Read(key + ".renderQueue", () => N(m.renderQueue));
        Read(key + ".shaderKeywords", () => List(m.shaderKeywords));
    }

    void Variants()
    {
        Material parent = null, child = null, grand = null;
        Read("variant.found", () => B((parent = Resources.Load<Material>("VarParent")) != null
            & (child = Resources.Load<Material>("VarChild")) != null
            & (grand = Resources.Load<Material>("VarGrand")) != null));
        if (parent == null || child == null || grand == null) return;
        var family = new[] { parent, child, grand };
        Block("variant.assets", () =>
        {
            Variant("variant.parent", parent, family);
            Variant("variant.child", child, family);
            Variant("variant.grand", grand, family);
        });
        Block("variant.instance", () =>
        {
            Material copy = null, made = null;
            Step("variant.instance.step[Instantiate(child)]", () => { copy = Instantiate(child); copy.name = "ChildCopy"; });
            Variant("variant.instance", copy, family);
            Step("variant.instance.step[new Material(grand)]", () => { made = new Material(grand); made.name = "GrandMade"; });
            Variant("variant.newFrom", made, family);
        });
        Block("variant.revert", () =>
        {
            Material copy = null;
            Step("variant.revert.step[Instantiate(grand)]", () => { copy = Instantiate(grand); copy.name = "GrandRevert"; });
            Step("variant.revert.step[RevertPropertyOverride(_Value)]", () => copy.RevertPropertyOverride("_Value"));
            Variant("variant.revert.value", copy, family);
            Step("variant.revert.step[RevertPropertyOverride(_Missing)]", () => copy.RevertPropertyOverride("_Missing"));
            Step("variant.revert.step[RevertAllPropertyOverrides]", () => copy.RevertAllPropertyOverrides());
            Variant("variant.revert.all", copy, family);
            Step("variant.revert.step[SetFloat(_Other, 1.75)]", () => copy.SetFloat("_Other", 1.75f));
            Variant("variant.revert.set", copy, family);
            Step("variant.revert.step[SetPropertyLock(_Other, true)]", () => copy.SetPropertyLock("_Other", true));
            Variant("variant.revert.lock", copy, family);
        });
        Block("variant.script", () =>
        {
            Material m = null;
            Step("variant.script.step[new Material(forms)]", () => m = new Material(Shader.Find("Probe/KwForms")) { name = "Scripted" });
            Step("variant.script.step[parent = VarParent]", () => m.parent = parent);
            Variant("variant.script", m, family);
            Step("variant.script.step[SetColor(_Color)]", () => m.SetColor("_Color", new Color(0.25f, 0.5f, 0.75f, 1)));
            Variant("variant.script.set", m, family);
            Step("variant.script.step[parent = null]", () => m.parent = null);
            Variant("variant.script.detached", m, family);
        });
    }
}
