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

public enum CurveControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum CurveControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepCurveData
{
    public AnimationCurve falloff;
}

[Serializable]
public struct NestedCurveData
{
    public AnimationCurve blend;
    public DeepCurveData deep;
}

public class CurveTarget : ScriptableObject
{
    public AnimationCurve emptyCurve = new AnimationCurve();
    public AnimationCurve myCurve = MakeCurve(
        new[]
        {
            new Keyframe(0f, 0.5f, 0f, 1.5f),
            new Keyframe(1f, 2f, -0.5f, 0f, 0.25f, 0.75f)
        },
        WrapMode.Loop,
        WrapMode.PingPong);
    public AnimationCurve otherCurve = AnimationCurve.Linear(0f, 1f, 2f, 3f);
    public AnimationCurve multiCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public NestedCurveData nested = new NestedCurveData
    {
        blend = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
        deep = new DeepCurveData { falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f) }
    };
    public AnimationCurve[] curveArray = new[]
    {
        AnimationCurve.Constant(0f, 1f, 0.5f),
        AnimationCurve.Linear(0f, 2f, 1f, 4f)
    };
    public BoundsInt siblingBoundsInt = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
    public Bounds siblingBounds = new Bounds(new Vector3(1.5f, -2f, 3f), new Vector3(4f, 6f, 8f));
    public RectInt siblingRectInt = new RectInt(1, -2, 30, 40);
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3 siblingVec3 = new Vector3(3f, -4f, 5f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public CurveControlPlainEnum plainEnum = CurveControlPlainEnum.Beta;
    public CurveControlFlagsEnum flagsEnum = CurveControlFlagsEnum.BitA | CurveControlFlagsEnum.BitB;

    public static AnimationCurve MakeCurve(Keyframe[] keys, WrapMode pre = WrapMode.ClampForever, WrapMode post = WrapMode.ClampForever)
    {
        var c = new AnimationCurve(keys);
        c.preWrapMode = pre;
        c.postWrapMode = post;
        return c;
    }
}

public class CurveRuntimeSource
{
    public string tip = "rt-curve-tip-0";
}

public class CurveBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CURVE_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<CurveBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("CURVE_BINDING_OUT");
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
    static string FmtRect(Rect r) => "(" + FmtFloat(r.x) + "," + FmtFloat(r.y) + "," + FmtFloat(r.width) + "," + FmtFloat(r.height) + ")";
    static string FmtRectInt(RectInt r) => "(" + FmtInt(r.x) + "," + FmtInt(r.y) + "," + FmtInt(r.width) + "," + FmtInt(r.height) + ")";
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";
    static string FmtBounds(Bounds b) => "[" + FmtVec3(b.center) + ";" + FmtVec3(b.extents) + "]";
    static string FmtBoundsInt(BoundsInt b) => "[" + FmtVec3Int(b.position) + ";" + FmtVec3Int(b.size) + "]";
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";

    static string FmtKey(Keyframe k) =>
        FmtFloat(k.time) + "," + FmtFloat(k.value) + "," + FmtFloat(k.inTangent) + "," + FmtFloat(k.outTangent) +
        "," + ((int)k.weightedMode).ToString(Inv) + "," + FmtFloat(k.inWeight) + "," + FmtFloat(k.outWeight);

    static string FmtCurve(AnimationCurve c)
    {
        if (c == null) return "null";
        var keys = c.keys;
        var sb = new StringBuilder();
        sb.Append((int)c.preWrapMode).Append('/').Append((int)c.postWrapMode).Append('[');
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(FmtKey(keys[i]));
        }
        sb.Append(']');
        return sb.ToString();
    }

#if UNITY_EDITOR
    static string FmtCurveField(CurveField cf)
    {
        bool hasMixedLabel = cf.Q<Label>(className: BaseField<AnimationCurve>.mixedValueLabelUssClassName) != null;
        return FmtCurve(cf.value) + "|m=" + FmtBool(cf.showMixedValue) + "|ml=" + FmtBool(hasMixedLabel);
    }

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is CurveField cf)
        {
            bool hasLabelCls = cf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = cf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = cf.ClassListContains(BaseField<AnimationCurve>.alignedFieldUssClassName);
            bool hasCurveCls = cf.ClassListContains(CurveField.ussClassName);
            bool hasBorder = cf.Q("unity-border") != null;
            return "CurveField|" + cf.name + "|" + (cf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasCurveCls) + FmtBool(hasBorder) + "|" +
                   FmtCurveField(cf);
        }
        if (child is BoundsIntField bif)
            return "BoundsIntField|" + bif.name + "|" + (bif.label ?? "null") + "|" + FmtBoundsInt(bif.value);
        if (child is BoundsField bf)
            return "BoundsField|" + bf.name + "|" + (bf.label ?? "null") + "|" + FmtBounds(bf.value);
        if (child is RectIntField rif)
            return "RectIntField|" + rif.name + "|" + (rif.label ?? "null") + "|" + FmtRectInt(rif.value);
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

        // 0. Standalone CurveField defaults, visual hierarchy, copy semantics, equality, null, renderMode.
        var cfStd = new CurveField("My Label");
        var stdEvts = new List<string>();
        cfStd.RegisterValueChangedCallback(e =>
            stdEvts.Add(FmtBool(ReferenceEquals(e.previousValue, e.newValue)) + ":" + FmtCurve(e.newValue)));
        m_Root.Add(cfStd);
        Frames(1);
        Put("standalone.defaults",
            (cfStd.label ?? "null") + "|" + FmtRect(cfStd.ranges) + "|" + cfStd.renderMode + "|" +
            FmtBool(cfStd.ClassListContains(CurveField.ussClassName)) +
            FmtBool(cfStd.Q("unity-border") != null) +
            FmtBool(cfStd.Q(className: CurveField.contentUssClassName) != null) + "|" +
            FmtCurveField(cfStd));

        // Copy independence on get and set.
        var g1 = cfStd.value;
        var g2 = cfStd.value;
        bool getReturnsCopies = !ReferenceEquals(g1, g2);
        g1.AddKey(0.5f, 10f);
        g1.preWrapMode = WrapMode.Loop;
        bool getMutationIsolated = cfStd.value.length == 0 && cfStd.value.preWrapMode == WrapMode.ClampForever;

        var srcCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) }, WrapMode.Loop, WrapMode.ClampForever);
        cfStd.value = srcCurve;
        srcCurve.AddKey(0.5f, 99f);
        srcCurve.postWrapMode = WrapMode.PingPong;
        bool setMutationIsolated = cfStd.value.length == 2 && cfStd.value.postWrapMode == WrapMode.ClampForever;
        Put("standalone.copy_independence",
            FmtBool(getReturnsCopies) + FmtBool(getMutationIsolated) + FmtBool(setMutationIsolated) + "|" +
            FmtCurve(cfStd.value) + "|ev=" + string.Join("|", stdEvts));

        // No-op equal curve vs wrap-mode-only change vs null.
        int evBefore = stdEvts.Count;
        cfStd.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) }, WrapMode.Loop, WrapMode.ClampForever);
        int evAfterEqual = stdEvts.Count;
        cfStd.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) }, WrapMode.Loop, WrapMode.PingPong);
        int evAfterWrap = stdEvts.Count;
        cfStd.value = null;
        string afterNull = FmtCurve(cfStd.value) + "|ec=" + FmtInt(stdEvts.Count);
        cfStd.value = null;
        int evAfterNullNoop = stdEvts.Count;
        cfStd.value = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        Put("standalone.equality_and_null",
            FmtInt(evAfterEqual - evBefore) + "|" + FmtInt(evAfterWrap - evAfterEqual) + "|" +
            afterNull + "|null_noop=" + FmtInt(evAfterNullNoop) + "|restored=" + FmtCurve(cfStd.value));

        cfStd.renderMode = CurveField.RenderMode.Mesh;
        bool hasMeshContent = cfStd.Q(className: CurveField.contentUssClassName) != null;
        cfStd.renderMode = CurveField.RenderMode.Texture;
        bool hasTexContent = cfStd.Q(className: CurveField.contentUssClassName) != null;
        Put("standalone.render_mode", FmtBool(hasMeshContent) + FmtBool(hasTexContent));

        // 1. Direct CurveField BindProperty: metadata, empty/non-empty sync, two-way, wrap-mode-only, target mutation, null, detached.
        var t1 = ScriptableObject.CreateInstance<CurveTarget>();
        var so1 = new SerializedObject(t1);

        var pMeta = so1.FindProperty("myCurve");
        Put("prop.meta", FmtBool(pMeta.hasVisibleChildren) + "|" + pMeta.type);

        var cfEmpty = new CurveField();
        var emptyEvts = new List<string>();
        cfEmpty.RegisterValueChangedCallback(e => emptyEvts.Add(FmtCurve(e.newValue)));
        m_Root.Add(cfEmpty);
        cfEmpty.BindProperty(so1.FindProperty("emptyCurve"));
        Frames();
        Put("direct.empty.initial", FmtCurveField(cfEmpty) + "|ev=" + string.Join(";", emptyEvts));

        cfEmpty.value = new AnimationCurve();
        so1.Update();
        Frames();
        Put("direct.empty.noop", FmtCurveField(cfEmpty) + "|ec=" + FmtInt(emptyEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var cfMain = new CurveField();
        var mainEvts = new List<string>();
        cfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtCurve(e.newValue)));
        m_Root.Add(cfMain);
        cfMain.BindProperty(so1.FindProperty("myCurve"));
        Frames();
        Put("direct.main.initial", FmtCurveField(cfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO full curve assignment + copy independence from target.
        var assigned = CurveTarget.MakeCurve(
            new[] { new Keyframe(0f, 3f), new Keyframe(0.5f, 4f), new Keyframe(1f, 5f) },
            WrapMode.PingPong,
            WrapMode.Loop);
        cfMain.value = assigned;
        assigned.AddKey(0.25f, 999f);
        Frames();
        bool isolatedFromTarget = !ReferenceEquals(cfMain.value, t1.myCurve) && t1.myCurve.length == 3;
        Put("direct.main.ui_to_so",
            FmtBool(isolatedFromTarget) + "|" + FmtCurveField(cfMain) + "|t=" + FmtCurve(t1.myCurve) +
            "|p=" + FmtCurve(so1.FindProperty("myCurve").animationCurveValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op equal write.
        int mainBeforeNoop = mainEvts.Count;
        cfMain.value = CurveTarget.MakeCurve(
            new[] { new Keyframe(0f, 3f), new Keyframe(0.5f, 4f), new Keyframe(1f, 5f) },
            WrapMode.PingPong,
            WrapMode.Loop);
        Frames();
        Put("direct.main.noop", FmtInt(mainEvts.Count - mainBeforeNoop));

        // SO -> UI full curve assignment.
        so1.FindProperty("myCurve").animationCurveValue = CurveTarget.MakeCurve(
            new[] { new Keyframe(0f, -1f, 0f, 2f), new Keyframe(2f, 4f, -1f, 0f) },
            WrapMode.ClampForever,
            WrapMode.Loop);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_to_ui", FmtCurveField(cfMain) + "|t=" + FmtCurve(t1.myCurve) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI wrap-mode-only change (keys identical!).
        var wrapChanged = so1.FindProperty("myCurve").animationCurveValue;
        wrapChanged.preWrapMode = WrapMode.PingPong;
        so1.FindProperty("myCurve").animationCurveValue = wrapChanged;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_wrap_only", FmtCurveField(cfMain) + "|t=" + FmtCurve(t1.myCurve) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation (in-place key add + replacement) picked up on panel update.
        t1.myCurve.AddKey(1f, 1.5f);
        Frames();
        Put("direct.main.external_inplace", FmtCurveField(cfMain) + "|ec=" + FmtInt(mainEvts.Count));

        t1.myCurve = AnimationCurve.Linear(0f, 10f, 1f, 20f);
        Frames();
        Put("direct.main.external_replace", FmtCurveField(cfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var cfDetached = new CurveField { bindingPath = "otherCurve" };
        var retProp = cfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtCurveField(cfDetached));
        m_Root.Add(cfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtCurveField(cfDetached));

        // 2. Multi-object mixed values on CurveField (divergent keys, wrap-mode-only divergence, edit + instance independence).
        var mA = ScriptableObject.CreateInstance<CurveTarget>();
        var mB = ScriptableObject.CreateInstance<CurveTarget>();
        mA.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) });
        mB.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 3f), new Keyframe(1f, 4f) });
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var cfMulti = new CurveField();
        var multiEvts = new List<string>();
        cfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtCurve(e.newValue)));
        m_Root.Add(cfMulti);
        cfMulti.BindProperty(soMulti.FindProperty("multiCurve"));
        Frames();
        Put("multi.initial", FmtCurveField(cfMulti) + "|a=" + FmtCurve(mA.multiCurve) + "|b=" + FmtCurve(mB.multiCurve) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed field to new curve -> applies to both targets with independent instances.
        cfMulti.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 5f), new Keyframe(1f, 6f) }, WrapMode.Loop, WrapMode.PingPong);
        Frames();
        bool distinctTargets = !ReferenceEquals(mA.multiCurve, mB.multiCurve);
        Put("multi.ui_edit_new",
            FmtBool(distinctTargets) + "|" + FmtCurveField(cfMulti) + "|a=" + FmtCurve(mA.multiCurve) +
            "|b=" + FmtCurve(mB.multiCurve) + "|ec=" + FmtInt(multiEvts.Count));

        // Divergent ONLY in postWrapMode -> still mixed!
        mA.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) }, WrapMode.ClampForever, WrapMode.Loop);
        mB.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) }, WrapMode.ClampForever, WrapMode.PingPong);
        soMulti.Update();
        Frames();
        Put("multi.wrap_only_mixed", FmtCurveField(cfMulti));

        // Assign target[0]'s curve while mixed -> clears mixed and updates target[1] too!
        cfMulti.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) }, WrapMode.ClampForever, WrapMode.Loop);
        Frames();
        Put("multi.ui_edit_to_t0", FmtCurveField(cfMulti) + "|a=" + FmtCurve(mA.multiCurve) + "|b=" + FmtCurve(mB.multiCurve));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<CurveTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trMain = new CurveField { bindingPath = "myCurve" };
        var trDotted = new CurveField { bindingPath = "nested.deep.falloff" };
        var trArray = new CurveField { bindingPath = "curveArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var trRelBlend = new CurveField { bindingPath = "blend" };
        nestedGroup.Add(trRelBlend);
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
            FmtCurve(trMain.value) + "|" + FmtCurve(trDotted.value) + "|" + FmtCurve(trArray.value) + "|" +
            FmtCurve(trRelBlend.value) + "|" + FmtBoundsInt(tBoundsInt.value) + "|" + FmtBounds(tBounds.value) + "|" +
            FmtRectInt(tRectInt.value) + "|" + FmtColor(tColor.value) + "|" + FmtVec3(tVec3.value) + "|" +
            FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        trDotted.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 9f), new Keyframe(1f, 8f) });
        trArray.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, -2f), new Keyframe(1f, -4f) });
        trRelBlend.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 0.25f), new Keyframe(1f, 0.75f) });
        tBoundsInt.Q<Vector3IntField>("unity-m_Position-input").Q<IntegerField>("unity-x-input").value = 11;
        tBounds.Q<Vector3Field>("unity-m_Center-input").Q<FloatField>("unity-x-input").value = 12.5f;
        tRectInt.Q<IntegerField>("unity-x-input").value = 15;
        tColor.value = new Color(1f, 0.5f, 0.25f, 0.5f);
        tVec3.Q<FloatField>("unity-z-input").value = 77f;
        tInt.value = 99;
        tPlain.value = CurveControlPlainEnum.Alpha;
        tFlags.value = CurveControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtCurve(tTree.nested.deep.falloff) + "|" + FmtCurve(tTree.curveArray[1]) + "|" +
            FmtCurve(tTree.nested.blend) + "|" + FmtBoundsInt(tTree.siblingBoundsInt) + "|" +
            FmtBounds(tTree.siblingBounds) + "|" + FmtRectInt(tTree.siblingRectInt) + "|" +
            FmtColor(tTree.siblingColor) + "|" + FmtVec3(tTree.siblingVec3) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tFlags.value);

        soTree.FindProperty("nested.blend").animationCurveValue = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 3f), new Keyframe(1f, 6f) });
        soTree.FindProperty("nested.deep.falloff").animationCurveValue = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 7f), new Keyframe(1f, 0f) });
        soTree.FindProperty("curveArray.Array.data[1]").animationCurveValue = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(2f, 2f) });
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtCurve(trRelBlend.value) + "|" + FmtCurve(trDotted.value) + "|" + FmtCurve(trArray.value));

        // 4. PropertyField for AnimationCurve (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<CurveTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myCurve"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.AnimationCurve)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtCurve(e.changedProperty.animationCurveValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<CurveField>();
        pfChild.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 11f), new Keyframe(1f, 22f) }, WrapMode.Loop, WrapMode.Loop);
        Frames();
        Put("pf.auto.ui_edit", DescribePf(pfAuto) + "|t=" + FmtCurve(tPf.myCurve) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myCurve").animationCurveValue = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 33f), new Keyframe(1f, 44f) });
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_edit", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom Curve";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.blend"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingBoundsInt"));
        Frames();
        Put("pf.rebind_boundsint", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingBounds"));
        Frames();
        Put("pf.rebind_bounds", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingRectInt"));
        Frames();
        Put("pf.rebind_rectint", DescribePf(pfAuto));

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

        pfAuto.BindProperty(soPf.FindProperty("otherCurve"));
        Frames();
        Put("pf.rebind_back_curve", DescribePf(pfAuto));

        // PropertyField with mixed multi-object AnimationCurve.
        var pmA = ScriptableObject.CreateInstance<CurveTarget>();
        var pmB = ScriptableObject.CreateInstance<CurveTarget>();
        pmA.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) });
        pmB.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 3f), new Keyframe(1f, 4f) });
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiCurve"), "Multi Curve");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtCurve(e.changedProperty.animationCurveValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiCf = pfMulti.Q<CurveField>();
        pfMultiCf.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 7f), new Keyframe(1f, 8f) });
        Frames();
        Put("pf.multi.ui_edit", DescribePf(pfMulti) + "|a=" + FmtCurve(pmA.multiCurve) + "|b=" + FmtCurve(pmB.multiCurve) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<CurveTarget>();
        var tRe2 = ScriptableObject.CreateInstance<CurveTarget>();
        tRe1.myCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 1f), new Keyframe(1f, 2f) });
        tRe1.otherCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 5f), new Keyframe(1f, 6f) });
        tRe2.myCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 70f), new Keyframe(1f, 80f) });
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new CurveRuntimeSource();
        var cfRe = new CurveField();
        cfRe.dataSource = rtSource;
        cfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(CurveRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(cfRe);
        cfRe.BindProperty(soRe1.FindProperty("myCurve"));
        Frames();
        Put("unbind_rebind.initial", FmtCurveField(cfRe) + "|tip=" + (cfRe.tooltip ?? "null"));

        cfRe.Unbind();
        Frames();
        cfRe.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 99f), new Keyframe(1f, 98f) });
        soRe1.FindProperty("myCurve").animationCurveValue = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 11f), new Keyframe(1f, 12f) });
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-curve-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtCurveField(cfRe) + "|t1=" + FmtCurve(tRe1.myCurve) + "|tip=" + (cfRe.tooltip ?? "null"));

        // Unbind on a mixed CurveField leaves showMixedValue intact.
        mA.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 10f), new Keyframe(1f, 20f) });
        mB.multiCurve = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 30f), new Keyframe(1f, 40f) });
        soMulti.Update();
        Frames();
        cfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtCurveField(cfMulti));

        cfRe.BindProperty(soRe1.FindProperty("otherCurve"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtCurveField(cfRe));
        cfRe.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 55f), new Keyframe(1f, 66f) });
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtCurve(tRe1.otherCurve) + "|t1_start=" + FmtCurve(tRe1.myCurve));

        cfRe.BindProperty(soRe2.FindProperty("myCurve"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtCurveField(cfRe));
        cfRe.value = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 71f), new Keyframe(1f, 81f) });
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtCurve(tRe2.myCurve) + "|t1_other=" + FmtCurve(tRe1.otherCurve));

        var pfRe = new PropertyField(soRe1.FindProperty("myCurve"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myCurve").animationCurveValue = CurveTarget.MakeCurve(new[] { new Keyframe(0f, 21f), new Keyframe(1f, 22f) });
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to AnimationCurve property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new IntegerField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myCurve"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", FmtInt(badField.value));
#endif
    }
}
