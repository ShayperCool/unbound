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

public enum BoundsIntControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum BoundsIntControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepBoundsIntData
{
    public BoundsInt voxelVolume;
}

[Serializable]
public struct NestedBoundsIntData
{
    public BoundsInt region;
    public DeepBoundsIntData deep;
}

public class BoundsIntTarget : ScriptableObject
{
    public BoundsInt zeroBoundsInt = new BoundsInt();
    public BoundsInt myCellBox = new BoundsInt(new Vector3Int(2, -3, 4), new Vector3Int(10, 20, 30));
    public BoundsInt otherBoundsInt = new BoundsInt(new Vector3Int(4, 5, 6), new Vector3Int(8, 12, 16));
    public BoundsInt negativeBoundsInt = new BoundsInt(new Vector3Int(-1, -2, -3), new Vector3Int(-4, -6, -8));
    public BoundsInt extremeBoundsInt = new BoundsInt(new Vector3Int(int.MinValue, int.MaxValue, -1), new Vector3Int(1000000, int.MinValue, int.MaxValue));
    public BoundsInt multiBoundsInt = new BoundsInt(new Vector3Int(1, 10, 100), new Vector3Int(2, 20, 200));
    public NestedBoundsIntData nested = new NestedBoundsIntData
    {
        region = new BoundsInt(new Vector3Int(2, 4, 6), new Vector3Int(10, 12, 14)),
        deep = new DeepBoundsIntData { voxelVolume = new BoundsInt(new Vector3Int(-3, 5, 7), new Vector3Int(4, 8, 12)) }
    };
    public BoundsInt[] boundsIntArray = new[]
    {
        new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(2, 4, 6)),
        new BoundsInt(new Vector3Int(5, 6, 7), new Vector3Int(10, 20, 30))
    };
    public Bounds siblingBounds = new Bounds(new Vector3(1.5f, -2f, 3f), new Vector3(4f, 6f, 8f));
    public RectInt siblingRectInt = new RectInt(1, -2, 30, 40);
    public Rect siblingRect = new Rect(1.5f, -2.5f, 30f, 40f);
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3Int siblingVec3Int = new Vector3Int(3, -4, 5);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public BoundsIntControlPlainEnum plainEnum = BoundsIntControlPlainEnum.Beta;
    public BoundsIntControlFlagsEnum flagsEnum = BoundsIntControlFlagsEnum.BitA | BoundsIntControlFlagsEnum.BitB;
}

public class BoundsIntRuntimeSource
{
    public string tip = "rt-boundsint-tip-0";
}

public class BoundsIntBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BOUNDSINT_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<BoundsIntBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("BOUNDSINT_BINDING_OUT");
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
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtBoundsInt(BoundsInt b) => "[" + FmtVec3Int(b.position) + ";" + FmtVec3Int(b.size) + "]";
    static string FmtBounds(Bounds b) => "[" + FmtVec3(b.center) + ";" + FmtVec3(b.extents) + "]";
    static string FmtRectInt(RectInt r) => "(" + FmtInt(r.x) + "," + FmtInt(r.y) + "," + FmtInt(r.width) + "," + FmtInt(r.height) + ")";
    static string FmtRect(Rect r) => "(" + FmtFloat(r.x) + "," + FmtFloat(r.y) + "," + FmtFloat(r.width) + "," + FmtFloat(r.height) + ")";
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";

    static Vector3IntField Position(BoundsIntField f) => f.Q<Vector3IntField>("unity-m_Position-input");
    static Vector3IntField Size(BoundsIntField f) => f.Q<Vector3IntField>("unity-m_Size-input");
    static IntegerField Axis(Vector3IntField v, string axis) => v.Q<IntegerField>("unity-" + axis + "-input");

    static string FmtSub(IntegerField f) =>
        f == null ? "null" : FmtInt(f.value) + ":" + (f.text ?? "null") + ":" + FmtBool(f.showMixedValue);

    static string FmtVec3IntField(Vector3IntField vf) =>
        vf == null
            ? "null"
            : FmtVec3Int(vf.value) + ":m=" + FmtBool(vf.showMixedValue) +
              ":x=" + FmtSub(Axis(vf, "x")) +
              ":y=" + FmtSub(Axis(vf, "y")) +
              ":z=" + FmtSub(Axis(vf, "z"));

    static string FmtBoundsIntField(BoundsIntField f) =>
        FmtBoundsInt(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|p=" + FmtVec3IntField(Position(f)) +
        "|s=" + FmtVec3IntField(Size(f));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is BoundsIntField bif)
        {
            bool hasLabelCls = bif.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = bif.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = bif.ClassListContains(BaseField<BoundsInt>.alignedFieldUssClassName);
            bool hasBoundsIntCls = bif.ClassListContains(BoundsIntField.ussClassName);
            var posf = Position(bif);
            var sizef = Size(bif);
            bool hasPosCls = posf != null && posf.ClassListContains(BoundsIntField.positionUssClassName);
            bool hasSizeCls = sizef != null && sizef.ClassListContains(BoundsIntField.sizeUssClassName);
            string subLabels = (posf?.label ?? "null") + "/" + (sizef?.label ?? "null");
            return "BoundsIntField|" + bif.name + "|" + (bif.label ?? "null") + "|" + subLabels + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasBoundsIntCls) + FmtBool(hasPosCls) + FmtBool(hasSizeCls) + "|" +
                   FmtBoundsIntField(bif);
        }
        if (child is BoundsField bf)
            return "BoundsField|" + bf.name + "|" + (bf.label ?? "null") + "|" + FmtBounds(bf.value);
        if (child is RectIntField rif)
            return "RectIntField|" + rif.name + "|" + (rif.label ?? "null") + "|" + FmtRectInt(rif.value);
        if (child is RectField rf)
            return "RectField|" + rf.name + "|" + (rf.label ?? "null") + "|" + FmtRect(rf.value);
        if (child is ColorField cf2)
            return "ColorField|" + cf2.name + "|" + (cf2.label ?? "null") + "|" + FmtColor(cf2.value);
        if (child is Vector3IntField v3i)
            return "Vector3IntField|" + v3i.name + "|" + (v3i.label ?? "null") + "|" + FmtVec3Int(v3i.value);
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

        // 1. Direct BoundsIntField BindProperty: metadata, zero/negative/extreme/non-zero sync, two-way position/size & axes, detached.
        var t1 = ScriptableObject.CreateInstance<BoundsIntTarget>();
        var so1 = new SerializedObject(t1);

        var pMeta = so1.FindProperty("myCellBox");
        Put("prop.meta", FmtBool(pMeta.hasVisibleChildren) + "|" + pMeta.type);

        var bifZero = new BoundsIntField();
        var zeroEvts = new List<string>();
        var zeroPosEvts = new List<string>();
        bifZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtBoundsInt(e.previousValue) + "->" + FmtBoundsInt(e.newValue)));
        Position(bifZero).RegisterValueChangedCallback(e => zeroPosEvts.Add(FmtVec3Int(e.previousValue) + "->" + FmtVec3Int(e.newValue)));
        m_Root.Add(bifZero);
        bifZero.BindProperty(so1.FindProperty("zeroBoundsInt"));
        Frames();
        Put("direct.zero.initial", FmtBoundsIntField(bifZero) + "|ev=" + string.Join(";", zeroEvts) + "|pev=" + string.Join(";", zeroPosEvts));

        bifZero.value = new BoundsInt();
        Axis(Position(bifZero), "x").value = 0;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtBoundsIntField(bifZero) + "|ev=" + FmtInt(zeroEvts.Count) + "|pev=" + FmtInt(zeroPosEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var bifNeg = new BoundsIntField();
        m_Root.Add(bifNeg);
        bifNeg.BindProperty(so1.FindProperty("negativeBoundsInt"));
        Frames();
        Put("direct.negative.initial", FmtBoundsIntField(bifNeg));
        Axis(Size(bifNeg), "y").value = -12;
        Frames();
        Put("direct.negative.ui_sy", FmtBoundsIntField(bifNeg) + "|t=" + FmtBoundsInt(t1.negativeBoundsInt));

        var bifExtreme = new BoundsIntField();
        m_Root.Add(bifExtreme);
        bifExtreme.BindProperty(so1.FindProperty("extremeBoundsInt"));
        Frames();
        Put("direct.extreme.initial", FmtBoundsIntField(bifExtreme));
        Axis(Position(bifExtreme), "z").value = int.MaxValue;
        Frames();
        Put("direct.extreme.ui_pz", FmtBoundsIntField(bifExtreme) + "|t=" + FmtBoundsInt(t1.extremeBoundsInt));

        var bifMain = new BoundsIntField();
        var mainEvts = new List<string>();
        bifMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtBoundsInt(e.previousValue) + "->" + FmtBoundsInt(e.newValue)));
        m_Root.Add(bifMain);
        bifMain.BindProperty(so1.FindProperty("myCellBox"));
        Frames();
        Put("direct.main.initial", FmtBoundsIntField(bifMain) + "|ev=" + string.Join(";", mainEvts));

        // UI -> SO whole BoundsInt.
        bifMain.value = new BoundsInt(new Vector3Int(10, 20, 30), new Vector3Int(4, 6, 8));
        Frames();
        Put("direct.main.ui_whole", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox) + "|p=" + FmtBoundsInt(so1.FindProperty("myCellBox").boundsIntValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op write.
        int countBeforeNoop = mainEvts.Count;
        bifMain.value = new BoundsInt(new Vector3Int(10, 20, 30), new Vector3Int(4, 6, 8));
        Axis(Size(bifMain), "z").value = 8;
        Frames();
        Put("direct.main.noop", FmtBoundsInt(t1.myCellBox) + "|ec_delta=" + FmtInt(mainEvts.Count - countBeforeNoop));

        // SO -> UI whole BoundsInt.
        so1.FindProperty("myCellBox").boundsIntValue = new BoundsInt(new Vector3Int(-4, 5, 6), new Vector3Int(12, 16, 20));
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation picked up on panel update.
        t1.myCellBox = new BoundsInt(new Vector3Int(7, -8, 9), new Vector3Int(2, 4, 6));
        Frames();
        Put("direct.main.external_target", FmtBoundsIntField(bifMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO via child Vector3IntFields and axis IntegerFields.
        Position(bifMain).value = new Vector3Int(11, -12, 13);
        Frames();
        Put("direct.main.ui_pos", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox) + "|ec=" + FmtInt(mainEvts.Count));

        Size(bifMain).value = new Vector3Int(21, 22, 23);
        Frames();
        Put("direct.main.ui_size", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox) + "|ec=" + FmtInt(mainEvts.Count));

        Axis(Position(bifMain), "y").value = -44;
        Frames();
        Put("direct.main.ui_py", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox) + "|ec=" + FmtInt(mainEvts.Count));

        Axis(Size(bifMain), "z").value = 55;
        Frames();
        Put("direct.main.ui_sz", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI via FindPropertyRelative for m_Position, m_Size, and leaf axes.
        so1.FindProperty("myCellBox").FindPropertyRelative("m_Position").vector3IntValue = new Vector3Int(101, 102, 103);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_pos", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox));

        so1.FindProperty("myCellBox").FindPropertyRelative("m_Size").vector3IntValue = new Vector3Int(201, 202, 203);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_size", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox));

        so1.FindProperty("myCellBox.m_Position.x").intValue = -301;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_px", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox));

        so1.FindProperty("myCellBox.m_Size.z").intValue = 404;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_sz", FmtBoundsIntField(bifMain) + "|t=" + FmtBoundsInt(t1.myCellBox));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var bifDetached = new BoundsIntField { bindingPath = "otherBoundsInt" };
        var retProp = bifDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtBoundsIntField(bifDetached));
        m_Root.Add(bifDetached);
        Frames();
        Put("direct.detached.after_attach", FmtBoundsIntField(bifDetached));

        // 2. Multi-object mixed values on BoundsIntField (per-axis showMixedValue, whole-BoundsInt vs direct sub-property edits).
        var mA = ScriptableObject.CreateInstance<BoundsIntTarget>();
        var mB = ScriptableObject.CreateInstance<BoundsIntTarget>();
        mA.multiBoundsInt = new BoundsInt(new Vector3Int(1, 10, 100), new Vector3Int(2, 20, 200));
        mB.multiBoundsInt = new BoundsInt(new Vector3Int(2, 20, 200), new Vector3Int(4, 40, 400));
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var bifMulti = new BoundsIntField();
        var multiEvts = new List<string>();
        bifMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtBoundsInt(e.previousValue) + "->" + FmtBoundsInt(e.newValue)));
        m_Root.Add(bifMulti);
        bifMulti.BindProperty(soMulti.FindProperty("multiBoundsInt"));
        Frames();
        Put("multi.initial", FmtBoundsIntField(bifMulti) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed position.x via axis sub-field on divergent targets.
        Axis(Position(bifMulti), "x").value = 5;
        Frames();
        Put("multi.ui_edit_mixed_px", FmtBoundsIntField(bifMulti) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt) + "|ec=" + FmtInt(multiEvts.Count));

        // Reset to partial mixed: position.x and size.z differ; position.y/z and size.x/y equal.
        mA.multiBoundsInt = new BoundsInt(new Vector3Int(1, 10, 100), new Vector3Int(6, 8, 20));
        mB.multiBoundsInt = new BoundsInt(new Vector3Int(2, 10, 100), new Vector3Int(6, 8, 40));
        soMulti.Update();
        Frames();
        Put("multi.reset_partial_mixed", FmtBoundsIntField(bifMulti));

        // Edit equal position.y via axis sub-field while position.x and size.z are mixed.
        Axis(Position(bifMulti), "y").value = 25;
        Frames();
        Put("multi.ui_edit_same_py", FmtBoundsIntField(bifMulti) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt));

        // Reset to all-mixed and write target[0]'s existing size.y via sub-field, then a new size.y.
        mA.multiBoundsInt = new BoundsInt(new Vector3Int(1, 10, 100), new Vector3Int(2, 20, 200));
        mB.multiBoundsInt = new BoundsInt(new Vector3Int(2, 20, 200), new Vector3Int(4, 40, 400));
        soMulti.Update();
        Frames();
        Axis(Size(bifMulti), "y").value = 20;
        Frames();
        Put("multi.ui_edit_mixed_sy_to_t0", FmtBoundsIntField(bifMulti) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt));

        Axis(Size(bifMulti), "y").value = 30;
        Frames();
        Put("multi.ui_edit_mixed_sy_new", FmtBoundsIntField(bifMulti) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt));

        // Reset to partial mixed and test whole-BoundsInt write changing only size.z.
        mA.multiBoundsInt = new BoundsInt(new Vector3Int(1, 10, 100), new Vector3Int(6, 8, 20));
        mB.multiBoundsInt = new BoundsInt(new Vector3Int(2, 10, 100), new Vector3Int(6, 8, 40));
        soMulti.Update();
        Frames();
        bifMulti.value = new BoundsInt(new Vector3Int(1, 10, 100), new Vector3Int(6, 8, 70));
        Frames();
        Put("multi.ui_whole_only_sz", FmtBoundsIntField(bifMulti) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt));

        // Direct Vector3IntField bound to "multiBoundsInt.m_Position" on mixed targets -> per-axis preservation!
        mA.multiBoundsInt = new BoundsInt(new Vector3Int(10, 20, 30), new Vector3Int(2, 4, 6));
        mB.multiBoundsInt = new BoundsInt(new Vector3Int(50, 60, 70), new Vector3Int(8, 10, 12));
        soMulti.Update();
        Frames();
        var vfCompPos = new Vector3IntField();
        m_Root.Add(vfCompPos);
        vfCompPos.BindProperty(soMulti.FindProperty("multiBoundsInt.m_Position"));
        Frames();
        Put("multi.comp_pos.initial", FmtVec3IntField(vfCompPos));
        Axis(vfCompPos, "x").value = 99;
        Frames();
        Put("multi.comp_pos.ui_edit_x", FmtVec3IntField(vfCompPos) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt));

        // Direct IntegerField bound to "multiBoundsInt.m_Size.z" on mixed targets -> only touches size.z!
        var ifCompSz = new IntegerField();
        m_Root.Add(ifCompSz);
        ifCompSz.BindProperty(soMulti.FindProperty("multiBoundsInt.m_Size.z"));
        Frames();
        Put("multi.comp_sz.initial", FmtSub(ifCompSz));
        ifCompSz.value = 77;
        Frames();
        Put("multi.comp_sz.ui_edit", FmtSub(ifCompSz) + "|a=" + FmtBoundsInt(mA.multiBoundsInt) + "|b=" + FmtBoundsInt(mB.multiBoundsInt));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<BoundsIntTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trMain = new BoundsIntField { bindingPath = "myCellBox" };
        var trDotted = new BoundsIntField { bindingPath = "nested.deep.voxelVolume" };
        var trArray = new BoundsIntField { bindingPath = "boundsIntArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var trRelRegion = new BoundsIntField { bindingPath = "region" };
        var vfRelRegionPos = new Vector3IntField { bindingPath = "region.m_Position" };
        var ifRelRegionSy = new IntegerField { bindingPath = "region.m_Size.y" };
        nestedGroup.Add(trRelRegion);
        nestedGroup.Add(vfRelRegionPos);
        nestedGroup.Add(ifRelRegionSy);
        var tBounds = new BoundsField { bindingPath = "siblingBounds" };
        var tRectInt = new RectIntField { bindingPath = "siblingRectInt" };
        var tRect = new RectField { bindingPath = "siblingRect" };
        var tColor = new ColorField { bindingPath = "siblingColor" };
        var tVec3Int = new Vector3IntField { bindingPath = "siblingVec3Int" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(trMain);
        treeRoot.Add(trDotted);
        treeRoot.Add(trArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tBounds);
        treeRoot.Add(tRectInt);
        treeRoot.Add(tRect);
        treeRoot.Add(tColor);
        treeRoot.Add(tVec3Int);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtBoundsInt(trMain.value) + "|" + FmtBoundsInt(trDotted.value) + "|" + FmtBoundsInt(trArray.value) + "|" +
            FmtBoundsInt(trRelRegion.value) + "|" + FmtVec3Int(vfRelRegionPos.value) + "|" + FmtInt(ifRelRegionSy.value) + "|" +
            FmtBounds(tBounds.value) + "|" + FmtRectInt(tRectInt.value) + "|" + FmtRect(tRect.value) + "|" +
            FmtColor(tColor.value) + "|" + FmtVec3Int(tVec3Int.value) + "|" + FmtInt(tInt.value) + "|" +
            tPlain.value + "|" + tFlags.value);

        Axis(Size(trDotted), "z").value = 33;
        Axis(Position(trArray), "x").value = -9;
        ifRelRegionSy.value = 18;
        tBounds.Q<Vector3Field>("unity-m_Center-input").Q<FloatField>("unity-x-input").value = 12.5f;
        tRectInt.Q<IntegerField>("unity-x-input").value = 15;
        tRect.Q<FloatField>("unity-x-input").value = 9.5f;
        tColor.value = new Color(1f, 0.5f, 0.25f, 0.5f);
        tVec3Int.Q<IntegerField>("unity-z-input").value = 77;
        tInt.value = 99;
        tPlain.value = BoundsIntControlPlainEnum.Alpha;
        tFlags.value = BoundsIntControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtBoundsInt(tTree.nested.deep.voxelVolume) + "|" + FmtBoundsInt(tTree.boundsIntArray[1]) + "|" +
            FmtBoundsInt(tTree.nested.region) + "|" + FmtBoundsInt(trRelRegion.value) + "|" +
            FmtBounds(tTree.siblingBounds) + "|" + FmtRectInt(tTree.siblingRectInt) + "|" + FmtRect(tTree.siblingRect) + "|" +
            FmtColor(tTree.siblingColor) + "|" + FmtVec3Int(tTree.siblingVec3Int) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tFlags.value);

        soTree.FindProperty("nested.region").boundsIntValue = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(8, 10, 12));
        soTree.FindProperty("nested.deep.voxelVolume.m_Position.x").intValue = 88;
        soTree.FindProperty("boundsIntArray.Array.data[1].m_Size.y").intValue = 44;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtBoundsInt(trRelRegion.value) + "|" + FmtVec3Int(vfRelRegionPos.value) + "|" + FmtInt(ifRelRegionSy.value) + "|" +
            FmtBoundsInt(trDotted.value) + "|" + FmtBoundsInt(trArray.value));

        // 4. PropertyField for BoundsInt (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<BoundsIntTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myCellBox"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.BoundsInt)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtBoundsInt(e.changedProperty.boundsIntValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<BoundsIntField>();
        Axis(Size(pfChild), "y").value = 42;
        Frames();
        Put("pf.auto.ui_sub_sy", DescribePf(pfAuto) + "|t=" + FmtBoundsInt(tPf.myCellBox) + "|evts=" + FmtInt(pfEvents.Count));

        pfChild.value = new BoundsInt(new Vector3Int(-1, -2, -3), new Vector3Int(6, 8, 10));
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtBoundsInt(tPf.myCellBox) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myCellBox.m_Position.z").intValue = 64;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_pz", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom BoundsInt";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.region"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingBounds"));
        Frames();
        Put("pf.rebind_bounds", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingRectInt"));
        Frames();
        Put("pf.rebind_rectint", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingRect"));
        Frames();
        Put("pf.rebind_rect", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingColor"));
        Frames();
        Put("pf.rebind_color", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec3Int"));
        Frames();
        Put("pf.rebind_vec3int", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec4"));
        Frames();
        Put("pf.rebind_vec4", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("scalarInt"));
        Frames();
        Put("pf.rebind_scalar", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("otherBoundsInt"));
        Frames();
        Put("pf.rebind_back_boundsint", DescribePf(pfAuto));

        // PropertyField with mixed multi-object BoundsInt.
        var pmA = ScriptableObject.CreateInstance<BoundsIntTarget>();
        var pmB = ScriptableObject.CreateInstance<BoundsIntTarget>();
        pmA.multiBoundsInt = new BoundsInt(new Vector3Int(3, 30, 300), new Vector3Int(6, 60, 600));
        pmB.multiBoundsInt = new BoundsInt(new Vector3Int(4, 30, 400), new Vector3Int(6, 80, 600));
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiBoundsInt"), "Multi BoundsInt");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtBoundsInt(e.changedProperty.boundsIntValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiBif = pfMulti.Q<BoundsIntField>();
        Axis(Position(pfMultiBif), "x").value = 9;
        Frames();
        Put("pf.multi.ui_edit_px", DescribePf(pfMulti) + "|a=" + FmtBoundsInt(pmA.multiBoundsInt) + "|b=" + FmtBoundsInt(pmB.multiBoundsInt) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<BoundsIntTarget>();
        var tRe2 = ScriptableObject.CreateInstance<BoundsIntTarget>();
        tRe1.myCellBox = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 6, 8));
        tRe1.otherBoundsInt = new BoundsInt(new Vector3Int(5, 6, 7), new Vector3Int(10, 12, 14));
        tRe2.myCellBox = new BoundsInt(new Vector3Int(70, 80, 90), new Vector3Int(20, 40, 60));
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new BoundsIntRuntimeSource();
        var bifRe = new BoundsIntField();
        bifRe.dataSource = rtSource;
        bifRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(BoundsIntRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(bifRe);
        bifRe.BindProperty(soRe1.FindProperty("myCellBox"));
        Frames();
        Put("unbind_rebind.initial", FmtBoundsIntField(bifRe) + "|tip=" + (bifRe.tooltip ?? "null"));

        bifRe.Unbind();
        Frames();
        bifRe.value = new BoundsInt(new Vector3Int(99, 98, 97), new Vector3Int(2, 4, 6));
        soRe1.FindProperty("myCellBox").boundsIntValue = new BoundsInt(new Vector3Int(11, 12, 13), new Vector3Int(22, 24, 26));
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-boundsint-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtBoundsIntField(bifRe) + "|t1=" + FmtBoundsInt(tRe1.myCellBox) + "|tip=" + (bifRe.tooltip ?? "null"));

        bifMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtBoundsIntField(bifMulti));

        bifRe.BindProperty(soRe1.FindProperty("otherBoundsInt"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtBoundsIntField(bifRe));
        Axis(Size(bifRe), "z").value = 66;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtBoundsInt(tRe1.otherBoundsInt) + "|t1_start=" + FmtBoundsInt(tRe1.myCellBox));

        bifRe.BindProperty(soRe2.FindProperty("myCellBox"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtBoundsIntField(bifRe));
        Axis(Position(bifRe), "x").value = 71;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtBoundsInt(tRe2.myCellBox) + "|t1_other=" + FmtBoundsInt(tRe1.otherBoundsInt));

        var pfRe = new PropertyField(soRe1.FindProperty("myCellBox"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myCellBox").boundsIntValue = new BoundsInt(new Vector3Int(21, 22, 23), new Vector3Int(42, 44, 46));
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to BoundsInt property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new FloatField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myCellBox"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.float_field", FmtFloat(badField.value));
#endif
    }
}
