using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

public enum GradientControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum GradientControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepGradientData
{
    public Gradient tint;
}

[Serializable]
public struct NestedGradientData
{
    public Gradient ramp;
    public DeepGradientData deep;
}

public class GradientTarget : ScriptableObject
{
    public Gradient defaultGradient = new Gradient();
    public Gradient myGradient = MakeGrad(
        new[]
        {
            new GradientColorKey(new Color(0.25f, 0.5f, 0.75f, 1f), 0f),
            new GradientColorKey(new Color(1f, 0.25f, 0f, 1f), 0.5f),
            new GradientColorKey(new Color(0f, 0.75f, 0.5f, 1f), 1f)
        },
        new[]
        {
            new GradientAlphaKey(0.25f, 0f),
            new GradientAlphaKey(0.75f, 1f)
        },
        GradientMode.Fixed,
        ColorSpace.Linear);
    public Gradient otherGradient = MakeGrad(
        new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
    public Gradient multiGradient = MakeGrad(
        new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

    [GradientUsage(true)]
    public Gradient hdrGradient = MakeGrad(
        new[] { new GradientColorKey(new Color(2f, 0.5f, 0f, 1f), 0f), new GradientColorKey(new Color(0f, 1.5f, 3f, 1f), 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });

    [GradientUsage(true, ColorSpace.Linear)]
    public Gradient linearHdrGradient = MakeGrad(
        new[] { new GradientColorKey(new Color(0.5f, 2f, 0.25f, 1f), 0f), new GradientColorKey(new Color(1f, 0.25f, 4f, 1f), 1f) },
        new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.2f, 1f) },
        GradientMode.PerceptualBlend,
        ColorSpace.Linear);

    [GradientUsage(false, ColorSpace.Linear)]
    public Gradient linearLdrGradient = MakeGrad(
        new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.yellow, 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

    public NestedGradientData nested = new NestedGradientData
    {
        ramp = MakeGrad(
            new[] { new GradientColorKey(Color.cyan, 0f), new GradientColorKey(Color.magenta, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 1f) }),
        deep = new DeepGradientData
        {
            tint = MakeGrad(
                new[] { new GradientColorKey(Color.yellow, 0f), new GradientColorKey(Color.red, 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 1f) },
                GradientMode.Fixed)
        }
    };
    public Gradient[] gradientArray = new[]
    {
        MakeGrad(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                 new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }),
        MakeGrad(new[] { new GradientColorKey(Color.blue, 0f), new GradientColorKey(Color.green, 1f) },
                 new[] { new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(0.75f, 1f) })
    };
    public AnimationCurve siblingCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
    public BoundsInt siblingBoundsInt = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
    public Bounds siblingBounds = new Bounds(new Vector3(1.5f, -2f, 3f), new Vector3(4f, 6f, 8f));
    public RectInt siblingRectInt = new RectInt(1, -2, 30, 40);
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3 siblingVec3 = new Vector3(3f, -4f, 5f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public GradientControlPlainEnum plainEnum = GradientControlPlainEnum.Beta;
    public GradientControlFlagsEnum flagsEnum = GradientControlFlagsEnum.BitA | GradientControlFlagsEnum.BitB;

    public static Gradient MakeGrad(GradientColorKey[] colors, GradientAlphaKey[] alphas,
        GradientMode mode = GradientMode.Blend, ColorSpace cs = ColorSpace.Uninitialized)
    {
        var g = new Gradient { mode = mode, colorSpace = cs };
        g.SetKeys(colors, alphas);
        return g;
    }
}

public class GradientRuntimeSource
{
    public string tip = "rt-grad-tip-0";
}

public class GradientBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GRADIENT_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<GradientBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("GRADIENT_BINDING_OUT");
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Put("error", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
        m_Logs.Sort(StringComparer.Ordinal);
        m_Out.Append("{\"k\":\"logs\",\"v\":[").Append(string.Join(",", m_Logs.Select(Json))).Append("]}\n");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static Type RuntimeUtil => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    static void Frames(int count = 2)
    {
        var update = RuntimeUtil.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var repaint = RuntimeUtil.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < count; i++)
        {
            update.Invoke(null, null);
            repaint.Invoke(null, new object[] { false });
        }
    }

    void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

    static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string FmtBool(bool v) => v ? "1" : "0";
    static string FmtInt(int v) => v.ToString(Inv);
    static string FmtFloat(float v) => v.ToString("G9", Inv);
    static string FmtRectInt(RectInt r) => "(" + FmtInt(r.x) + "," + FmtInt(r.y) + "," + FmtInt(r.width) + "," + FmtInt(r.height) + ")";
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";
    static string FmtBounds(Bounds b) => "[" + FmtVec3(b.center) + ";" + FmtVec3(b.extents) + "]";
    static string FmtBoundsInt(BoundsInt b) => "[" + FmtVec3Int(b.position) + ";" + FmtVec3Int(b.size) + "]";
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";

    static string FmtCurveShort(AnimationCurve c) =>
        c == null ? "null" : ((int)c.preWrapMode).ToString(Inv) + "/" + ((int)c.postWrapMode).ToString(Inv) + "[" + FmtInt(c.length) + ":" +
        (c.length > 0 ? FmtFloat(c[0].value) + "->" + FmtFloat(c[c.length - 1].value) : "") + "]";

    static string FmtGrad(Gradient g)
    {
        if (g == null) return "null";
        var sb = new StringBuilder();
        sb.Append((int)g.mode).Append('/').Append((int)g.colorSpace).Append("[c:");
        var ck = g.colorKeys;
        for (int i = 0; i < ck.Length; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(FmtColor(ck[i].color)).Append('@').Append(FmtFloat(ck[i].time));
        }
        sb.Append("|a:");
        var ak = g.alphaKeys;
        for (int i = 0; i < ak.Length; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(FmtFloat(ak[i].alpha)).Append('@').Append(FmtFloat(ak[i].time));
        }
        sb.Append(']');
        return sb.ToString();
    }

#if UNITY_EDITOR
    static string FmtGradField(GradientField gf)
    {
        bool hasMixedLabel = gf.Q<Label>(className: BaseField<Gradient>.mixedValueLabelUssClassName) != null;
        return FmtGrad(gf.value) + "|hdr=" + FmtBool(gf.hdr) + "|cs=" + gf.colorSpace +
               "|m=" + FmtBool(gf.showMixedValue) + "|ml=" + FmtBool(hasMixedLabel);
    }

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is GradientField gf)
        {
            bool hasLabelCls = gf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = gf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = gf.ClassListContains(BaseField<Gradient>.alignedFieldUssClassName);
            bool hasGradCls = gf.ClassListContains(GradientField.ussClassName);
            bool hasBg = gf.Q(className: GradientField.backgroundUssClassName) != null;
            bool hasContent = gf.Q(className: GradientField.contentUssClassName) != null;
            return "GradientField|" + gf.name + "|" + (gf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasGradCls) + FmtBool(hasBg) + FmtBool(hasContent) + "|" +
                   FmtGradField(gf);
        }
        if (child is CurveField cf)
            return "CurveField|" + cf.name + "|" + (cf.label ?? "null") + "|" + FmtCurveShort(cf.value);
        if (child is BoundsIntField bif)
            return "BoundsIntField|" + bif.name + "|" + (bif.label ?? "null") + "|" + FmtBoundsInt(bif.value);
        if (child is ColorField col)
            return "ColorField|" + col.name + "|" + (col.label ?? "null") + "|" + FmtColor(col.value);
        if (child is Vector3Field v3)
            return "Vector3Field|" + v3.name + "|" + (v3.label ?? "null") + "|" + FmtVec3(v3.value);
        if (child is Foldout fo)
            return "Foldout|" + fo.name + "|" + (fo.text ?? "null") + "|" + FmtBool(fo.value);
        if (child is IntegerField i)
            return "IntegerField|" + i.name + "|" + (i.label ?? "null") + "|" + FmtInt(i.value);
        return child.GetType().Name + "|" + child.name;
    }
#endif

    void Run()
    {
#if UNITY_EDITOR
        var document = GetComponent<UIDocument>();
        m_Root = document.rootVisualElement;
        Frames(1);

        // 0. Standalone GradientField defaults, visual hierarchy, copy semantics, equality, time-only quirk, null, hdr/colorSpace.
        var gfStd = new GradientField("My Gradient");
        var stdEvts = new List<string>();
        gfStd.RegisterValueChangedCallback(e =>
            stdEvts.Add(FmtBool(ReferenceEquals(e.previousValue, e.newValue)) + ":" + FmtGrad(e.newValue)));
        m_Root.Add(gfStd);
        Frames(1);
        Put("standalone.defaults",
            (gfStd.label ?? "null") + "|" +
            FmtBool(gfStd.ClassListContains(GradientField.ussClassName)) +
            FmtBool(gfStd.Q(className: GradientField.backgroundUssClassName) != null) +
            FmtBool(gfStd.Q(className: GradientField.contentUssClassName) != null) + "|" +
            FmtGradField(gfStd));

        // Copy independence on get and set + colorSpace handling in GradientCopy/SetValueWithoutNotify.
        var g1 = gfStd.value;
        var g2 = gfStd.value;
        bool getReturnsCopies = !ReferenceEquals(g1, g2);
        g1.mode = GradientMode.Fixed;
        g1.colorKeys = new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) };
        bool getMutationIsolated = gfStd.value.mode == GradientMode.Blend && gfStd.value.colorKeys[0].color == Color.white;

        var srcGrad = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) },
            GradientMode.Fixed,
            ColorSpace.Linear);
        gfStd.value = srcGrad;
        srcGrad.mode = GradientMode.Blend;
        srcGrad.colorKeys = new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) };
        bool setMutationIsolated = gfStd.value.mode == GradientMode.Fixed && gfStd.value.colorKeys.Length == 3;
        Put("standalone.copy_independence",
            FmtBool(getReturnsCopies) + FmtBool(getMutationIsolated) + FmtBool(setMutationIsolated) + "|" +
            FmtGrad(gfStd.value) + "|ev=" + string.Join("|", stdEvts));

        // No-op equal, color-key time-only change, alpha-key time-only change, mode-only change, alpha-only change, null.
        int evBefore = stdEvts.Count;
        gfStd.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) },
            GradientMode.Fixed);
        int evAfterEqual = stdEvts.Count;

        // Time-only change on middle color key (0.5 -> 0.75) and middle alpha key (0.5 -> 0.25).
        gfStd.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.75f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) },
            GradientMode.Fixed);
        int evAfterColorTimeOnly = stdEvts.Count;
        string afterColorTimeVal = FmtGrad(gfStd.value);

        gfStd.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.25f), new GradientAlphaKey(0f, 1f) },
            GradientMode.Fixed);
        int evAfterAlphaTimeOnly = stdEvts.Count;
        string afterAlphaTimeVal = FmtGrad(gfStd.value);

        // Mode-only change.
        gfStd.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) },
            GradientMode.PerceptualBlend);
        int evAfterMode = stdEvts.Count;

        // Alpha-value-only change.
        gfStd.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 0.5f), new GradientAlphaKey(0f, 1f) },
            GradientMode.PerceptualBlend);
        int evAfterAlpha = stdEvts.Count;

        // Null + repeat null + restore.
        gfStd.value = null;
        string afterNull = FmtGrad(gfStd.value) + "|ec=" + FmtInt(stdEvts.Count);
        gfStd.value = null;
        int evAfterNullNoop = stdEvts.Count;
        gfStd.value = new Gradient();
        Put("standalone.equality_and_null",
            FmtInt(evAfterEqual - evBefore) + "|" +
            FmtInt(evAfterColorTimeOnly - evAfterEqual) + ":" + afterColorTimeVal + "|" +
            FmtInt(evAfterAlphaTimeOnly - evAfterColorTimeOnly) + ":" + afterAlphaTimeVal + "|" +
            FmtInt(evAfterMode - evAfterAlphaTimeOnly) + "|" +
            FmtInt(evAfterAlpha - evAfterMode) + "|" +
            afterNull + "|null_noop=" + FmtInt(evAfterNullNoop) + "|restored=" + FmtGrad(gfStd.value));

        gfStd.hdr = true;
        gfStd.colorSpace = ColorSpace.Linear;
        Put("standalone.hdr_cs", FmtBool(gfStd.hdr) + "|" + gfStd.colorSpace);

        // 1. Direct GradientField BindProperty: metadata, default/non-default sync, two-way, mode/alpha/same-time/HDR, external mutation, detached.
        var t1 = ScriptableObject.CreateInstance<GradientTarget>();
        var so1 = new SerializedObject(t1);

        var pMeta = so1.FindProperty("myGradient");
        Put("prop.meta", FmtBool(pMeta.hasVisibleChildren) + "|" + pMeta.type);

        var gfDef = new GradientField();
        var defEvts = new List<string>();
        gfDef.RegisterValueChangedCallback(e => defEvts.Add(FmtGrad(e.newValue)));
        m_Root.Add(gfDef);
        gfDef.BindProperty(so1.FindProperty("defaultGradient"));
        Frames();
        Put("direct.default.initial", FmtGradField(gfDef) + "|ev=" + string.Join(";", defEvts));

        gfDef.value = new Gradient();
        so1.Update();
        Frames();
        Put("direct.default.noop", FmtGradField(gfDef) + "|ec=" + FmtInt(defEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var gfMain = new GradientField();
        var mainEvts = new List<string>();
        gfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtGrad(e.newValue)));
        m_Root.Add(gfMain);
        gfMain.BindProperty(so1.FindProperty("myGradient"));
        Frames();
        Put("direct.main.initial", FmtGradField(gfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO full assignment + copy independence from target.
        var assigned = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(new Color(0.5f, 0.25f, 1f, 1f), 0f), new GradientColorKey(new Color(2f, 0.5f, 0f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.25f, 1f) },
            GradientMode.PerceptualBlend,
            ColorSpace.Linear);
        gfMain.value = assigned;
        assigned.mode = GradientMode.Blend;
        assigned.colorKeys = new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) };
        Frames();
        bool isolatedFromTarget = !ReferenceEquals(gfMain.value, t1.myGradient) && t1.myGradient.mode == GradientMode.PerceptualBlend;
        Put("direct.main.ui_to_so",
            FmtBool(isolatedFromTarget) + "|" + FmtGradField(gfMain) + "|t=" + FmtGrad(t1.myGradient) +
            "|p=" + FmtGrad(so1.FindProperty("myGradient").gradientValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op equal write.
        int mainBeforeNoop = mainEvts.Count;
        gfMain.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(new Color(0.5f, 0.25f, 1f, 1f), 0f), new GradientColorKey(new Color(2f, 0.5f, 0f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.25f, 1f) },
            GradientMode.PerceptualBlend);
        Frames();
        Put("direct.main.noop", FmtInt(mainEvts.Count - mainBeforeNoop));

        // SO -> UI full gradient assignment.
        so1.FindProperty("myGradient").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.yellow, 0.5f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) },
            GradientMode.Blend,
            ColorSpace.Gamma);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_to_ui", FmtGradField(gfMain) + "|t=" + FmtGrad(t1.myGradient) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI mode-only change.
        var modeChanged = so1.FindProperty("myGradient").gradientValue;
        modeChanged.mode = GradientMode.Fixed;
        so1.FindProperty("myGradient").gradientValue = modeChanged;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_mode_only", FmtGradField(gfMain) + "|t=" + FmtGrad(t1.myGradient) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI same-key-times color-only change (exercises Gradient contentHash!).
        so1.FindProperty("myGradient").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.cyan, 0.5f), new GradientColorKey(Color.magenta, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) },
            GradientMode.Fixed,
            ColorSpace.Gamma);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_color_only", FmtGradField(gfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI alpha-only change.
        so1.FindProperty("myGradient").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.cyan, 0.5f), new GradientColorKey(Color.magenta, 1f) },
            new[] { new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(0.75f, 1f) },
            GradientMode.Fixed,
            ColorSpace.Gamma);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_alpha_only", FmtGradField(gfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation (in-place SetKeys + mode + full replacement) picked up on panel update.
        t1.myGradient.mode = GradientMode.Blend;
        t1.myGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.red, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 1f) });
        Frames();
        Put("direct.main.external_inplace", FmtGradField(gfMain) + "|ec=" + FmtInt(mainEvts.Count));

        t1.myGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.blue, 0f), new GradientColorKey(Color.yellow, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
            GradientMode.PerceptualBlend);
        Frames();
        Put("direct.main.external_replace", FmtGradField(gfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var gfDetached = new GradientField { bindingPath = "otherGradient" };
        var retProp = gfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtGradField(gfDetached));
        m_Root.Add(gfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtGradField(gfDetached));

        // 2. Multi-object mixed values on GradientField (divergent colors, mode-only, alpha-only, edit to new and to target[0]).
        var mA = ScriptableObject.CreateInstance<GradientTarget>();
        var mB = ScriptableObject.CreateInstance<GradientTarget>();
        mA.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        mB.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.blue, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var gfMulti = new GradientField();
        var multiEvts = new List<string>();
        gfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtGrad(e.newValue)));
        m_Root.Add(gfMulti);
        gfMulti.BindProperty(soMulti.FindProperty("multiGradient"));
        Frames();
        Put("multi.initial", FmtGradField(gfMulti) + "|a=" + FmtGrad(mA.multiGradient) + "|b=" + FmtGrad(mB.multiGradient) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed field to new gradient -> applies to both targets with independent instances.
        gfMulti.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.yellow, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 1f) },
            GradientMode.Fixed);
        Frames();
        bool distinctTargets = !ReferenceEquals(mA.multiGradient, mB.multiGradient);
        Put("multi.ui_edit_new",
            FmtBool(distinctTargets) + "|" + FmtGradField(gfMulti) + "|a=" + FmtGrad(mA.multiGradient) +
            "|b=" + FmtGrad(mB.multiGradient) + "|ec=" + FmtInt(multiEvts.Count));

        // Divergent ONLY in mode -> still mixed!
        mA.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
            GradientMode.Blend);
        mB.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
            GradientMode.Fixed);
        soMulti.Update();
        Frames();
        Put("multi.mode_only_mixed", FmtGradField(gfMulti));

        // Assign target[0]'s gradient while mixed -> clears mixed and updates target[1] too!
        gfMulti.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
            GradientMode.Blend);
        Frames();
        Put("multi.ui_edit_to_t0", FmtGradField(gfMulti) + "|a=" + FmtGrad(mA.multiGradient) + "|b=" + FmtGrad(mB.multiGradient));

        // Divergent ONLY in alpha -> still mixed!
        mA.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(1f, 1f) });
        mB.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(1f, 1f) });
        soMulti.Update();
        Frames();
        Put("multi.alpha_only_mixed", FmtGradField(gfMulti));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<GradientTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trMain = new GradientField { bindingPath = "myGradient" };
        var trDotted = new GradientField { bindingPath = "nested.deep.tint" };
        var trArray = new GradientField { bindingPath = "gradientArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var trRelRamp = new GradientField { bindingPath = "ramp" };
        nestedGroup.Add(trRelRamp);
        var tCurve = new CurveField { bindingPath = "siblingCurve" };
        var tBoundsInt = new BoundsIntField { bindingPath = "siblingBoundsInt" };
        var tBounds = new BoundsField { bindingPath = "siblingBounds" };
        var tRectInt = new RectIntField { bindingPath = "siblingRectInt" };
        var tColor = new ColorField { bindingPath = "siblingColor" };
        var tVec3 = new Vector3Field { bindingPath = "siblingVec3" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(trMain);
        treeRoot.Add(trDotted);
        treeRoot.Add(trArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tCurve);
        treeRoot.Add(tBoundsInt);
        treeRoot.Add(tBounds);
        treeRoot.Add(tRectInt);
        treeRoot.Add(tColor);
        treeRoot.Add(tVec3);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtGrad(trMain.value) + "|" + FmtGrad(trDotted.value) + "|" + FmtGrad(trArray.value) + "|" +
            FmtGrad(trRelRamp.value) + "|" + FmtCurveShort(tCurve.value) + "|" + FmtBoundsInt(tBoundsInt.value) + "|" +
            FmtBounds(tBounds.value) + "|" + FmtRectInt(tRectInt.value) + "|" + FmtColor(tColor.value) + "|" +
            FmtVec3(tVec3.value) + "|" + FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        trDotted.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.magenta, 0f), new GradientColorKey(Color.cyan, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0.25f, 1f) });
        trArray.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.yellow, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
        trRelRamp.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.green, 1f) },
            new[] { new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(1f, 1f) });
        tCurve.value = AnimationCurve.Linear(0f, 5f, 1f, 6f);
        tBoundsInt.Q<Vector3IntField>("unity-m_Position-input").Q<IntegerField>("unity-x-input").value = 11;
        tBounds.Q<Vector3Field>("unity-m_Center-input").Q<FloatField>("unity-x-input").value = 12.5f;
        tRectInt.Q<IntegerField>("unity-x-input").value = 15;
        tColor.value = new Color(1f, 0.5f, 0.25f, 0.5f);
        tVec3.Q<FloatField>("unity-z-input").value = 77f;
        tInt.value = 99;
        tPlain.value = GradientControlPlainEnum.Alpha;
        tFlags.value = GradientControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtGrad(tTree.nested.deep.tint) + "|" + FmtGrad(tTree.gradientArray[1]) + "|" +
            FmtGrad(tTree.nested.ramp) + "|" + FmtCurveShort(tTree.siblingCurve) + "|" +
            FmtBoundsInt(tTree.siblingBoundsInt) + "|" + FmtBounds(tTree.siblingBounds) + "|" +
            FmtRectInt(tTree.siblingRectInt) + "|" + FmtColor(tTree.siblingColor) + "|" +
            FmtVec3(tTree.siblingVec3) + "|" + FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tFlags.value);

        // Same key times (0 and 1), only colors/alphas differ -> verifies nested struct SO -> UI sync!
        soTree.FindProperty("nested.ramp").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.blue, 0f), new GradientColorKey(Color.red, 1f) },
            new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.25f, 1f) });
        soTree.FindProperty("nested.deep.tint").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
        soTree.FindProperty("gradientArray.Array.data[1]").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.yellow, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 1f) });
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtGrad(trRelRamp.value) + "|" + FmtGrad(trDotted.value) + "|" + FmtGrad(trArray.value));

        // 4. PropertyField for plain Gradient + [GradientUsageAttribute] (hdr, colorSpace, rebind, mixed).
        var tPf = ScriptableObject.CreateInstance<GradientTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myGradient"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.Gradient)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtGrad(e.changedProperty.gradientValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<GradientField>();
        pfChild.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 1f) },
            GradientMode.PerceptualBlend);
        Frames();
        Put("pf.auto.ui_edit", DescribePf(pfAuto) + "|t=" + FmtGrad(tPf.myGradient) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myGradient").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.blue, 0f), new GradientColorKey(Color.cyan, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 1f) });
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_edit", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom Gradient";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        // Plain -> plain reuse check.
        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.ramp"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        // Plain -> [GradientUsage(true, ColorSpace.Linear)] rebind check.
        var childBeforeUsage = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("linearHdrGradient"));
        Frames();
        var childAfterUsage = pfAuto.hierarchy[0];
        Put("pf.rebind_plain_to_usage", FmtBool(ReferenceEquals(childBeforeUsage, childAfterUsage)) + "|" + DescribePf(pfAuto));

        // [GradientUsage] -> plain rebind check.
        pfAuto.BindProperty(soPf.FindProperty("otherGradient"));
        Frames();
        Put("pf.rebind_usage_to_plain", DescribePf(pfAuto));

        // Standalone [GradientUsageAttribute] PropertyFields (hdr only, hdr+Linear, ldr+Linear).
        var pfHdr = new PropertyField(soPf.FindProperty("hdrGradient"));
        var pfHdrEvts = new List<string>();
        pfHdr.RegisterValueChangeCallback(e => pfHdrEvts.Add(e.changedProperty.propertyPath + ":" + FmtGrad(e.changedProperty.gradientValue)));
        m_Root.Add(pfHdr);
        pfHdr.Bind(soPf);
        Frames();
        Put("pf.usage.hdr_initial", DescribePf(pfHdr) + "|evts=" + FmtInt(pfHdrEvts.Count));

        var pfHdrChild = pfHdr.Q<GradientField>();
        pfHdrChild.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(new Color(4f, 0.25f, 0.5f, 1f), 0f), new GradientColorKey(new Color(0.5f, 3f, 1f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.5f, 1f) });
        Frames();
        Put("pf.usage.hdr_ui_edit", DescribePf(pfHdr) + "|t=" + FmtGrad(tPf.hdrGradient) + "|evts=" + FmtInt(pfHdrEvts.Count));

        var pfLinHdr = new PropertyField(soPf.FindProperty("linearHdrGradient"));
        m_Root.Add(pfLinHdr);
        pfLinHdr.Bind(soPf);
        Frames();
        Put("pf.usage.linear_hdr", DescribePf(pfLinHdr));

        var pfLinLdr = new PropertyField(soPf.FindProperty("linearLdrGradient"), "Linear LDR");
        m_Root.Add(pfLinLdr);
        pfLinLdr.Bind(soPf);
        Frames();
        Put("pf.usage.linear_ldr", DescribePf(pfLinLdr));

        // Rebind from usage PropertyField to plain Gradient property.
        pfLinHdr.BindProperty(soPf.FindProperty("otherGradient"));
        Frames();
        Put("pf.usage.rebind_to_plain", DescribePf(pfLinHdr));

        // Cross-type rebind across sibling types and back to Gradient.
        pfAuto.BindProperty(soPf.FindProperty("siblingCurve"));
        Frames();
        Put("pf.rebind_curve", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingBoundsInt"));
        Frames();
        Put("pf.rebind_boundsint", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingColor"));
        Frames();
        Put("pf.rebind_color", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec3"));
        Frames();
        Put("pf.rebind_vec3", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec4"));
        Frames();
        Put("pf.rebind_vec4", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("scalarInt"));
        Frames();
        Put("pf.rebind_scalar", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("otherGradient"));
        Frames();
        Put("pf.rebind_back_gradient", DescribePf(pfAuto));

        // PropertyField with mixed multi-object Gradient.
        var pmA = ScriptableObject.CreateInstance<GradientTarget>();
        var pmB = ScriptableObject.CreateInstance<GradientTarget>();
        pmA.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        pmB.multiGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.blue, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiGradient"), "Multi Gradient");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtGrad(e.changedProperty.gradientValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiGf = pfMulti.Q<GradientField>();
        pfMultiGf.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.yellow, 0f), new GradientColorKey(Color.green, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 1f) });
        Frames();
        Put("pf.multi.ui_edit", DescribePf(pfMulti) + "|a=" + FmtGrad(pmA.multiGradient) + "|b=" + FmtGrad(pmB.multiGradient) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<GradientTarget>();
        var tRe2 = ScriptableObject.CreateInstance<GradientTarget>();
        tRe1.myGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        tRe1.otherGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        tRe2.myGradient = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.blue, 0f), new GradientColorKey(Color.yellow, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new GradientRuntimeSource();
        var gfRe = new GradientField();
        gfRe.dataSource = rtSource;
        gfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(GradientRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(gfRe);
        gfRe.BindProperty(soRe1.FindProperty("myGradient"));
        Frames();
        Put("unbind_rebind.initial", FmtGradField(gfRe) + "|tip=" + (gfRe.tooltip ?? "null"));

        gfRe.Unbind();
        Frames();
        gfRe.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.magenta, 0f), new GradientColorKey(Color.magenta, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        soRe1.FindProperty("myGradient").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.cyan, 0f), new GradientColorKey(Color.cyan, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-grad-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtGradField(gfRe) + "|t1=" + FmtGrad(tRe1.myGradient) + "|tip=" + (gfRe.tooltip ?? "null"));

        // Unbind on a mixed GradientField leaves showMixedValue intact.
        gfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtGradField(gfMulti));

        gfRe.BindProperty(soRe1.FindProperty("otherGradient"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtGradField(gfRe));
        gfRe.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.yellow, 0f), new GradientColorKey(Color.red, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtGrad(tRe1.otherGradient) + "|t1_start=" + FmtGrad(tRe1.myGradient));

        gfRe.BindProperty(soRe2.FindProperty("myGradient"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtGradField(gfRe));
        gfRe.value = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtGrad(tRe2.myGradient) + "|t1_other=" + FmtGrad(tRe1.otherGradient));

        var pfRe = new PropertyField(soRe1.FindProperty("myGradient"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myGradient").gradientValue = GradientTarget.MakeGrad(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Gradient property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new IntegerField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myGradient"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", FmtInt(badField.value));
#endif
    }
}
