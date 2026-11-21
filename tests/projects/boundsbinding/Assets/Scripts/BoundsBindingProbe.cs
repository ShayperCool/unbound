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

public enum BoundsControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum BoundsControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepBoundsData
{
    public Bounds volume;
}

[Serializable]
public struct NestedBoundsData
{
    public Bounds hull;
    public DeepBoundsData deep;
}

public class BoundsTarget : ScriptableObject
{
    public Bounds zeroBounds = new Bounds();
    public Bounds myBox = new Bounds(new Vector3(1.5f, -2.25f, 3f), new Vector3(10f, 20f, 30f));
    public Bounds otherBounds = new Bounds(new Vector3(4f, 5f, 6f), new Vector3(8f, 12f, 16f));
    public Bounds negativeBounds = new Bounds(new Vector3(-1f, -2f, -3f), new Vector3(-4f, -6f, -8f));
    public Bounds multiBounds = new Bounds(new Vector3(1f, 10f, 100f), new Vector3(2f, 20f, 200f));
    public NestedBoundsData nested = new NestedBoundsData
    {
        hull = new Bounds(new Vector3(2f, 4f, 6f), new Vector3(10f, 12f, 14f)),
        deep = new DeepBoundsData { volume = new Bounds(new Vector3(-3f, 5.5f, 7f), new Vector3(4f, 8f, 12f)) }
    };
    public Bounds[] boundsArray = new[]
    {
        new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f)),
        new Bounds(new Vector3(5f, 6f, 7f), new Vector3(10f, 20f, 30f))
    };
    public RectInt siblingRectInt = new RectInt(1, -2, 30, 40);
    public Rect siblingRect = new Rect(1.5f, -2.5f, 30f, 40f);
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3 siblingVec3 = new Vector3(3f, -4f, 5f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public BoundsControlPlainEnum plainEnum = BoundsControlPlainEnum.Beta;
    public BoundsControlFlagsEnum flagsEnum = BoundsControlFlagsEnum.BitA | BoundsControlFlagsEnum.BitB;
}

public class BoundsRuntimeSource
{
    public string tip = "rt-bounds-tip-0";
}

public class BoundsBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BOUNDS_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<BoundsBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("BOUNDS_BINDING_OUT");
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
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtBounds(Bounds b) => "[" + FmtVec3(b.center) + ";" + FmtVec3(b.extents) + "]";
    static string FmtRectInt(RectInt r) => "(" + FmtInt(r.x) + "," + FmtInt(r.y) + "," + FmtInt(r.width) + "," + FmtInt(r.height) + ")";
    static string FmtRect(Rect r) => "(" + FmtFloat(r.x) + "," + FmtFloat(r.y) + "," + FmtFloat(r.width) + "," + FmtFloat(r.height) + ")";
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";

    static Vector3Field Center(BoundsField f) => f.Q<Vector3Field>("unity-m_Center-input");
    static Vector3Field Extents(BoundsField f) => f.Q<Vector3Field>("unity-m_Extent-input");
    static FloatField Axis(Vector3Field v, string axis) => v.Q<FloatField>("unity-" + axis + "-input");

    static string FmtSub(FloatField ff) =>
        ff == null ? "null" : FmtFloat(ff.value) + ":" + (ff.text ?? "null") + ":" + FmtBool(ff.showMixedValue);

    static string FmtVec3Field(Vector3Field vf) =>
        vf == null
            ? "null"
            : FmtVec3(vf.value) + ":m=" + FmtBool(vf.showMixedValue) +
              ":x=" + FmtSub(Axis(vf, "x")) +
              ":y=" + FmtSub(Axis(vf, "y")) +
              ":z=" + FmtSub(Axis(vf, "z"));

    static string FmtBoundsField(BoundsField f) =>
        FmtBounds(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|c=" + FmtVec3Field(Center(f)) +
        "|e=" + FmtVec3Field(Extents(f));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is BoundsField bf)
        {
            bool hasLabelCls = bf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = bf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = bf.ClassListContains(BaseField<Bounds>.alignedFieldUssClassName);
            bool hasBoundsCls = bf.ClassListContains(BoundsField.ussClassName);
            var cf = Center(bf);
            var ef = Extents(bf);
            bool hasCenterCls = cf != null && cf.ClassListContains(BoundsField.centerFieldUssClassName);
            bool hasExtentsCls = ef != null && ef.ClassListContains(BoundsField.extentsFieldUssClassName);
            string subLabels = (cf?.label ?? "null") + "/" + (ef?.label ?? "null");
            return "BoundsField|" + bf.name + "|" + (bf.label ?? "null") + "|" + subLabels + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasBoundsCls) + FmtBool(hasCenterCls) + FmtBool(hasExtentsCls) + "|" +
                   FmtBoundsField(bf);
        }
        if (child is RectIntField rif)
            return "RectIntField|" + rif.name + "|" + (rif.label ?? "null") + "|" + FmtRectInt(rif.value);
        if (child is RectField rf)
            return "RectField|" + rf.name + "|" + (rf.label ?? "null") + "|" + FmtRect(rf.value);
        if (child is ColorField cf2)
            return "ColorField|" + cf2.name + "|" + (cf2.label ?? "null") + "|" + FmtColor(cf2.value);
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

        // 1. Direct BoundsField BindProperty: metadata, zero/negative/non-zero sync, two-way center/extents & axes, detached.
        var t1 = ScriptableObject.CreateInstance<BoundsTarget>();
        var so1 = new SerializedObject(t1);

        var pMeta = so1.FindProperty("myBox");
        Put("prop.meta", FmtBool(pMeta.hasVisibleChildren) + "|" + pMeta.type);

        var bfZero = new BoundsField();
        var zeroEvts = new List<string>();
        var zeroCenterEvts = new List<string>();
        bfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtBounds(e.previousValue) + "->" + FmtBounds(e.newValue)));
        Center(bfZero).RegisterValueChangedCallback(e => zeroCenterEvts.Add(FmtVec3(e.previousValue) + "->" + FmtVec3(e.newValue)));
        m_Root.Add(bfZero);
        bfZero.BindProperty(so1.FindProperty("zeroBounds"));
        Frames();
        Put("direct.zero.initial", FmtBoundsField(bfZero) + "|ev=" + string.Join(";", zeroEvts) + "|cev=" + string.Join(";", zeroCenterEvts));

        bfZero.value = new Bounds();
        Axis(Center(bfZero), "x").value = 0f;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtBoundsField(bfZero) + "|ev=" + FmtInt(zeroEvts.Count) + "|cev=" + FmtInt(zeroCenterEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var bfNeg = new BoundsField();
        m_Root.Add(bfNeg);
        bfNeg.BindProperty(so1.FindProperty("negativeBounds"));
        Frames();
        Put("direct.negative.initial", FmtBoundsField(bfNeg));
        Axis(Extents(bfNeg), "y").value = -12.5f;
        Frames();
        Put("direct.negative.ui_ey", FmtBoundsField(bfNeg) + "|t=" + FmtBounds(t1.negativeBounds));

        var bfMain = new BoundsField();
        var mainEvts = new List<string>();
        bfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtBounds(e.previousValue) + "->" + FmtBounds(e.newValue)));
        m_Root.Add(bfMain);
        bfMain.BindProperty(so1.FindProperty("myBox"));
        Frames();
        Put("direct.main.initial", FmtBoundsField(bfMain) + "|ev=" + string.Join(";", mainEvts));

        // UI -> SO whole Bounds.
        bfMain.value = new Bounds(new Vector3(10f, 20f, 30f), new Vector3(4f, 6f, 8f));
        Frames();
        Put("direct.main.ui_whole", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox) + "|p=" + FmtBounds(so1.FindProperty("myBox").boundsValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op write.
        int countBeforeNoop = mainEvts.Count;
        bfMain.value = new Bounds(new Vector3(10f, 20f, 30f), new Vector3(4f, 6f, 8f));
        Axis(Extents(bfMain), "z").value = 4f;
        Frames();
        Put("direct.main.noop", FmtBounds(t1.myBox) + "|ec_delta=" + FmtInt(mainEvts.Count - countBeforeNoop));

        // SO -> UI whole Bounds.
        so1.FindProperty("myBox").boundsValue = new Bounds(new Vector3(-4f, 5.5f, 6f), new Vector3(12f, 16f, 20f));
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation picked up on panel update.
        t1.myBox = new Bounds(new Vector3(7f, -8f, 9f), new Vector3(2f, 4f, 6f));
        Frames();
        Put("direct.main.external_target", FmtBoundsField(bfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO via child Vector3Fields and axis FloatFields.
        Center(bfMain).value = new Vector3(11.5f, -12.25f, 13f);
        Frames();
        Put("direct.main.ui_center", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox) + "|ec=" + FmtInt(mainEvts.Count));

        Extents(bfMain).value = new Vector3(21f, 22.5f, 23f);
        Frames();
        Put("direct.main.ui_extents", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox) + "|ec=" + FmtInt(mainEvts.Count));

        Axis(Center(bfMain), "y").value = -44.5f;
        Frames();
        Put("direct.main.ui_cy", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox) + "|ec=" + FmtInt(mainEvts.Count));

        Axis(Extents(bfMain), "z").value = 55.25f;
        Frames();
        Put("direct.main.ui_ez", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI via FindPropertyRelative for m_Center, m_Extent, and leaf axes.
        so1.FindProperty("myBox").FindPropertyRelative("m_Center").vector3Value = new Vector3(101f, 102f, 103f);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_center", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox));

        so1.FindProperty("myBox").FindPropertyRelative("m_Extent").vector3Value = new Vector3(201f, 202f, 203f);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_extents", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox));

        so1.FindProperty("myBox.m_Center.x").floatValue = -301.5f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_cx", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox));

        so1.FindProperty("myBox.m_Extent.z").floatValue = 404.5f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_ez", FmtBoundsField(bfMain) + "|t=" + FmtBounds(t1.myBox));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var bfDetached = new BoundsField { bindingPath = "otherBounds" };
        var retProp = bfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtBoundsField(bfDetached));
        m_Root.Add(bfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtBoundsField(bfDetached));

        // 2. Multi-object mixed values on BoundsField (per-axis showMixedValue, whole-Bounds vs direct sub-property edits).
        var mA = ScriptableObject.CreateInstance<BoundsTarget>();
        var mB = ScriptableObject.CreateInstance<BoundsTarget>();
        mA.multiBounds = new Bounds(new Vector3(1f, 10f, 100f), new Vector3(2f, 20f, 200f));
        mB.multiBounds = new Bounds(new Vector3(2f, 20f, 200f), new Vector3(4f, 40f, 400f));
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var bfMulti = new BoundsField();
        var multiEvts = new List<string>();
        bfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtBounds(e.previousValue) + "->" + FmtBounds(e.newValue)));
        m_Root.Add(bfMulti);
        bfMulti.BindProperty(soMulti.FindProperty("multiBounds"));
        Frames();
        Put("multi.initial", FmtBoundsField(bfMulti) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed center.x via axis sub-field on divergent targets.
        Axis(Center(bfMulti), "x").value = 5f;
        Frames();
        Put("multi.ui_edit_mixed_cx", FmtBoundsField(bfMulti) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds) + "|ec=" + FmtInt(multiEvts.Count));

        // Reset to partial mixed: center.x and extents.z differ; center.y/z and extents.x/y equal.
        mA.multiBounds = new Bounds(new Vector3(1f, 10f, 100f), new Vector3(6f, 8f, 20f));
        mB.multiBounds = new Bounds(new Vector3(2f, 10f, 100f), new Vector3(6f, 8f, 40f));
        soMulti.Update();
        Frames();
        Put("multi.reset_partial_mixed", FmtBoundsField(bfMulti));

        // Edit equal center.y via axis sub-field while center.x and extents.z are mixed.
        Axis(Center(bfMulti), "y").value = 25f;
        Frames();
        Put("multi.ui_edit_same_cy", FmtBoundsField(bfMulti) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds));

        // Reset to all-mixed and write target[0]'s existing extents.y via sub-field, then a new extents.y.
        mA.multiBounds = new Bounds(new Vector3(1f, 10f, 100f), new Vector3(2f, 20f, 200f));
        mB.multiBounds = new Bounds(new Vector3(2f, 20f, 200f), new Vector3(4f, 40f, 400f));
        soMulti.Update();
        Frames();
        Axis(Extents(bfMulti), "y").value = 10f;
        Frames();
        Put("multi.ui_edit_mixed_ey_to_t0", FmtBoundsField(bfMulti) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds));

        Axis(Extents(bfMulti), "y").value = 30f;
        Frames();
        Put("multi.ui_edit_mixed_ey_new", FmtBoundsField(bfMulti) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds));

        // Reset to partial mixed and test whole-Bounds write changing only extents.z.
        mA.multiBounds = new Bounds(new Vector3(1f, 10f, 100f), new Vector3(6f, 8f, 20f));
        mB.multiBounds = new Bounds(new Vector3(2f, 10f, 100f), new Vector3(6f, 8f, 40f));
        soMulti.Update();
        Frames();
        bfMulti.value = new Bounds(new Vector3(1f, 10f, 100f), new Vector3(6f, 8f, 70f));
        Frames();
        Put("multi.ui_whole_only_ez", FmtBoundsField(bfMulti) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds));

        // Direct Vector3Field bound to "multiBounds.m_Center" on mixed targets -> per-axis preservation!
        mA.multiBounds = new Bounds(new Vector3(10f, 20f, 30f), new Vector3(2f, 4f, 6f));
        mB.multiBounds = new Bounds(new Vector3(50f, 60f, 70f), new Vector3(8f, 10f, 12f));
        soMulti.Update();
        Frames();
        var vfCompCenter = new Vector3Field();
        m_Root.Add(vfCompCenter);
        vfCompCenter.BindProperty(soMulti.FindProperty("multiBounds.m_Center"));
        Frames();
        Put("multi.comp_center.initial", FmtVec3Field(vfCompCenter));
        Axis(vfCompCenter, "x").value = 99f;
        Frames();
        Put("multi.comp_center.ui_edit_x", FmtVec3Field(vfCompCenter) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds));

        // Direct FloatField bound to "multiBounds.m_Extent.z" on mixed targets -> only touches extents.z!
        var ffCompEz = new FloatField();
        m_Root.Add(ffCompEz);
        ffCompEz.BindProperty(soMulti.FindProperty("multiBounds.m_Extent.z"));
        Frames();
        Put("multi.comp_ez.initial", FmtSub(ffCompEz));
        ffCompEz.value = 77f;
        Frames();
        Put("multi.comp_ez.ui_edit", FmtSub(ffCompEz) + "|a=" + FmtBounds(mA.multiBounds) + "|b=" + FmtBounds(mB.multiBounds));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<BoundsTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trMain = new BoundsField { bindingPath = "myBox" };
        var trDotted = new BoundsField { bindingPath = "nested.deep.volume" };
        var trArray = new BoundsField { bindingPath = "boundsArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var trRelHull = new BoundsField { bindingPath = "hull" };
        var vfRelHullCenter = new Vector3Field { bindingPath = "hull.m_Center" };
        var ffRelHullEy = new FloatField { bindingPath = "hull.m_Extent.y" };
        nestedGroup.Add(trRelHull);
        nestedGroup.Add(vfRelHullCenter);
        nestedGroup.Add(ffRelHullEy);
        var tRectInt = new RectIntField { bindingPath = "siblingRectInt" };
        var tRect = new RectField { bindingPath = "siblingRect" };
        var tColor = new ColorField { bindingPath = "siblingColor" };
        var tVec3 = new Vector3Field { bindingPath = "siblingVec3" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(trMain);
        treeRoot.Add(trDotted);
        treeRoot.Add(trArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tRectInt);
        treeRoot.Add(tRect);
        treeRoot.Add(tColor);
        treeRoot.Add(tVec3);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtBounds(trMain.value) + "|" + FmtBounds(trDotted.value) + "|" + FmtBounds(trArray.value) + "|" +
            FmtBounds(trRelHull.value) + "|" + FmtVec3(vfRelHullCenter.value) + "|" + FmtFloat(ffRelHullEy.value) + "|" +
            FmtRectInt(tRectInt.value) + "|" + FmtRect(tRect.value) + "|" + FmtColor(tColor.value) + "|" +
            FmtVec3(tVec3.value) + "|" + FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        Axis(Extents(trDotted), "z").value = 33.5f;
        Axis(Center(trArray), "x").value = -9.5f;
        ffRelHullEy.value = 18.25f;
        tRectInt.Q<IntegerField>("unity-x-input").value = 15;
        tRect.Q<FloatField>("unity-x-input").value = 9.5f;
        tColor.value = new Color(1f, 0.5f, 0.25f, 0.5f);
        tVec3.Q<FloatField>("unity-z-input").value = 77f;
        tInt.value = 99;
        tPlain.value = BoundsControlPlainEnum.Alpha;
        tFlags.value = BoundsControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtBounds(tTree.nested.deep.volume) + "|" + FmtBounds(tTree.boundsArray[1]) + "|" +
            FmtBounds(tTree.nested.hull) + "|" + FmtBounds(trRelHull.value) + "|" +
            FmtRectInt(tTree.siblingRectInt) + "|" + FmtRect(tTree.siblingRect) + "|" +
            FmtColor(tTree.siblingColor) + "|" + FmtVec3(tTree.siblingVec3) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tFlags.value);

        soTree.FindProperty("nested.hull").boundsValue = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(8f, 10f, 12f));
        soTree.FindProperty("nested.deep.volume.m_Center.x").floatValue = 88f;
        soTree.FindProperty("boundsArray.Array.data[1].m_Extent.y").floatValue = 44f;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtBounds(trRelHull.value) + "|" + FmtVec3(vfRelHullCenter.value) + "|" + FmtFloat(ffRelHullEy.value) + "|" +
            FmtBounds(trDotted.value) + "|" + FmtBounds(trArray.value));

        // 4. PropertyField for Bounds (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<BoundsTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myBox"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.Bounds)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtBounds(e.changedProperty.boundsValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<BoundsField>();
        Axis(Extents(pfChild), "y").value = 42.5f;
        Frames();
        Put("pf.auto.ui_sub_ey", DescribePf(pfAuto) + "|t=" + FmtBounds(tPf.myBox) + "|evts=" + FmtInt(pfEvents.Count));

        pfChild.value = new Bounds(new Vector3(-1f, -2f, -3f), new Vector3(6f, 8f, 10f));
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtBounds(tPf.myBox) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myBox.m_Center.z").floatValue = 64f;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_cz", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom Bounds";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.hull"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingRectInt"));
        Frames();
        Put("pf.rebind_rectint", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingRect"));
        Frames();
        Put("pf.rebind_rect", DescribePf(pfAuto));

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

        pfAuto.BindProperty(soPf.FindProperty("otherBounds"));
        Frames();
        Put("pf.rebind_back_bounds", DescribePf(pfAuto));

        // PropertyField with mixed multi-object Bounds.
        var pmA = ScriptableObject.CreateInstance<BoundsTarget>();
        var pmB = ScriptableObject.CreateInstance<BoundsTarget>();
        pmA.multiBounds = new Bounds(new Vector3(3f, 30f, 300f), new Vector3(6f, 60f, 600f));
        pmB.multiBounds = new Bounds(new Vector3(4f, 30f, 400f), new Vector3(6f, 80f, 600f));
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiBounds"), "Multi Bounds");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtBounds(e.changedProperty.boundsValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiBf = pfMulti.Q<BoundsField>();
        Axis(Center(pfMultiBf), "x").value = 9f;
        Frames();
        Put("pf.multi.ui_edit_cx", DescribePf(pfMulti) + "|a=" + FmtBounds(pmA.multiBounds) + "|b=" + FmtBounds(pmB.multiBounds) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<BoundsTarget>();
        var tRe2 = ScriptableObject.CreateInstance<BoundsTarget>();
        tRe1.myBox = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 8f));
        tRe1.otherBounds = new Bounds(new Vector3(5f, 6f, 7f), new Vector3(10f, 12f, 14f));
        tRe2.myBox = new Bounds(new Vector3(70f, 80f, 90f), new Vector3(20f, 40f, 60f));
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new BoundsRuntimeSource();
        var bfRe = new BoundsField();
        bfRe.dataSource = rtSource;
        bfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(BoundsRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(bfRe);
        bfRe.BindProperty(soRe1.FindProperty("myBox"));
        Frames();
        Put("unbind_rebind.initial", FmtBoundsField(bfRe) + "|tip=" + (bfRe.tooltip ?? "null"));

        bfRe.Unbind();
        Frames();
        bfRe.value = new Bounds(new Vector3(99f, 98f, 97f), new Vector3(2f, 4f, 6f));
        soRe1.FindProperty("myBox").boundsValue = new Bounds(new Vector3(11f, 12f, 13f), new Vector3(22f, 24f, 26f));
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-bounds-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtBoundsField(bfRe) + "|t1=" + FmtBounds(tRe1.myBox) + "|tip=" + (bfRe.tooltip ?? "null"));

        bfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtBoundsField(bfMulti));

        bfRe.BindProperty(soRe1.FindProperty("otherBounds"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtBoundsField(bfRe));
        Axis(Extents(bfRe), "z").value = 66f;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtBounds(tRe1.otherBounds) + "|t1_start=" + FmtBounds(tRe1.myBox));

        bfRe.BindProperty(soRe2.FindProperty("myBox"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtBoundsField(bfRe));
        Axis(Center(bfRe), "x").value = 71f;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtBounds(tRe2.myBox) + "|t1_other=" + FmtBounds(tRe1.otherBounds));

        var pfRe = new PropertyField(soRe1.FindProperty("myBox"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myBox").boundsValue = new Bounds(new Vector3(21f, 22f, 23f), new Vector3(42f, 44f, 46f));
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Bounds property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new IntegerField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myBox"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", FmtInt(badField.value));
#endif
    }
}
