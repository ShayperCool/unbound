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

public enum Vec4ControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum Vec4ControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepVec4Data
{
    public Vector4 plane;
}

[Serializable]
public struct NestedVec4Data
{
    public Vector4 offset;
    public DeepVec4Data deep;
}

public class Vector4Target : ScriptableObject
{
    public Vector4 zeroVec = Vector4.zero;
    public Vector4 myStartVec = new Vector4(1.5f, -2.25f, 3.75f, -4.5f);
    public Vector4 otherVec = new Vector4(10f, 20f, 30f, 40f);
    public Vector4 multiVec = new Vector4(1f, 10f, 100f, 100f);
    public NestedVec4Data nested = new NestedVec4Data
    {
        offset = new Vector4(4f, 5f, 6f, 7f),
        deep = new DeepVec4Data { plane = new Vector4(-7f, 8.5f, -9.25f, 10.75f) }
    };
    public Vector4[] vecArray = new[]
    {
        new Vector4(0.5f, 1.5f, 2.5f, 3.5f),
        new Vector4(4.5f, 5.5f, 6.5f, 7.5f)
    };
    public Vector2 siblingVec2 = new Vector2(2f, 3f);
    public Vector3 siblingVec3 = new Vector3(9f, 8f, 7f);
    public int scalarInt = 42;
    public Vec4ControlPlainEnum plainEnum = Vec4ControlPlainEnum.Beta;
    public Vec4ControlFlagsEnum flagsEnum = Vec4ControlFlagsEnum.BitA | Vec4ControlFlagsEnum.BitB;
}

public class Vec4RuntimeSource
{
    public string tip = "rt-vec4-tip-0";
}

public class Vector4BindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VECTOR4_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<Vector4BindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("VECTOR4_BINDING_OUT");
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
    static string FmtFloat(float v) => v.ToString("G9", Inv);
    static string FmtVec2(Vector2 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + ")";
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtVec4(Vector4 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + "," + FmtFloat(v.w) + ")";

    static FloatField Sub(Vector4Field f, string axis) => f.Q<FloatField>("unity-" + axis + "-input");

    static string FmtSub(FloatField ff) =>
        ff == null ? "null" : FmtFloat(ff.value) + ":" + (ff.text ?? "null") + ":" + FmtBool(ff.showMixedValue);

    static string FmtVecField(Vector4Field f) =>
        FmtVec4(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|x=" + FmtSub(Sub(f, "x")) +
        "|y=" + FmtSub(Sub(f, "y")) +
        "|z=" + FmtSub(Sub(f, "z")) +
        "|w=" + FmtSub(Sub(f, "w"));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is Foldout f)
        {
            var subPfs = f.Query<PropertyField>().ToList();
            var parts = new List<string>(subPfs.Count);
            foreach (var spf in subPfs)
            {
                var ff = spf.Q<FloatField>();
                parts.Add(spf.bindingPath + "=" + (ff == null ? "none" : FmtSub(ff)));
            }
            return "Foldout|" + f.name + "|" + (f.text ?? "null") + "|exp=" + FmtBool(f.value) +
                   "|n=" + subPfs.Count.ToString(Inv) + "|[" + string.Join(";", parts) + "]";
        }
        if (child is Vector4Field vf)
            return "Vector4Field|" + vf.name + "|" + (vf.label ?? "null") + "|" + FmtVecField(vf);
        if (child is Vector3Field v3)
            return "Vector3Field|" + v3.name + "|" + (v3.label ?? "null") + "|" + FmtVec3(v3.value);
        if (child is IntegerField i)
            return "IntegerField|" + i.name + "|" + (i.label ?? "null") + "|" + i.value.ToString(Inv);
        return child.GetType().Name + "|" + child.name;
    }

    static FloatField PfAxisField(PropertyField pf, string propPath, string axis) =>
        pf.Q<FloatField>("unity-input-" + propPath + "." + axis);
#endif

    void Run()
    {
#if UNITY_EDITOR
        var document = GetComponent<UIDocument>();
        m_Root = document.rootVisualElement;
        Frames(1);

        // 1. Initial sync (zero default and non-zero), no-op writes, detached-then-attached.
        var t1 = ScriptableObject.CreateInstance<Vector4Target>();
        var so1 = new SerializedObject(t1);

        var vfZero = new Vector4Field();
        var zeroEvts = new List<string>();
        var zeroSubEvts = new List<string>();
        vfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtVec4(e.previousValue) + "->" + FmtVec4(e.newValue)));
        Sub(vfZero, "w").RegisterValueChangedCallback(e => zeroSubEvts.Add("w:" + FmtFloat(e.previousValue) + "->" + FmtFloat(e.newValue)));
        m_Root.Add(vfZero);
        vfZero.BindProperty(so1.FindProperty("zeroVec"));
        Frames();
        Put("direct.zero.initial", FmtVecField(vfZero) + "|e=" + string.Join(";", zeroEvts) + "|se=" + string.Join(";", zeroSubEvts));

        vfZero.value = Vector4.zero;
        Sub(vfZero, "w").value = 0f;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtVecField(vfZero) + "|e=" + zeroEvts.Count.ToString(Inv) + "|se=" + zeroSubEvts.Count.ToString(Inv) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var vfMain = new Vector4Field();
        var mainEvts = new List<string>();
        vfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtVec4(e.previousValue) + "->" + FmtVec4(e.newValue)));
        m_Root.Add(vfMain);
        vfMain.BindProperty(so1.FindProperty("myStartVec"));
        Frames();
        Put("direct.main.initial", FmtVecField(vfMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO whole vector.
        vfMain.value = new Vector4(10f, 20f, 30f, 40f);
        Frames();
        Put("direct.main.ui_whole", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec) + "|p=" + FmtVec4(so1.FindProperty("myStartVec").vector4Value) + "|ec=" + mainEvts.Count.ToString(Inv));

        // No-op write with same non-zero vector and same sub-field value.
        int countBeforeNoop = mainEvts.Count;
        vfMain.value = new Vector4(10f, 20f, 30f, 40f);
        Sub(vfMain, "w").value = 40f;
        Frames();
        Put("direct.main.noop", FmtVec4(t1.myStartVec) + "|ec_delta=" + (mainEvts.Count - countBeforeNoop).ToString(Inv));

        // SO -> UI whole vector.
        so1.FindProperty("myStartVec").vector4Value = new Vector4(-4f, 5.5f, -6.25f, 7.75f);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec) + "|ec=" + mainEvts.Count.ToString(Inv));

        // Direct C# target mutation picked up on panel update.
        t1.myStartVec = new Vector4(7f, 8f, 9f, 10f);
        Frames();
        Put("direct.main.external_target", FmtVecField(vfMain) + "|ec=" + mainEvts.Count.ToString(Inv));

        // UI -> SO per-component edits via sub FloatFields (x, y, z, w).
        Sub(vfMain, "x").value = 11.5f;
        Frames();
        Put("direct.main.ui_x", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec) + "|ec=" + mainEvts.Count.ToString(Inv));

        Sub(vfMain, "y").value = -22.25f;
        Frames();
        Put("direct.main.ui_y", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec) + "|ec=" + mainEvts.Count.ToString(Inv));

        Sub(vfMain, "z").value = 33.75f;
        Frames();
        Put("direct.main.ui_z", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec) + "|ec=" + mainEvts.Count.ToString(Inv));

        Sub(vfMain, "w").value = -44.5f;
        Frames();
        Put("direct.main.ui_w", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec) + "|ec=" + mainEvts.Count.ToString(Inv));

        // SO -> UI per-component edits via FindPropertyRelative (x, y, z, w).
        so1.FindProperty("myStartVec").FindPropertyRelative("x").floatValue = 101f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_x", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec));

        so1.FindProperty("myStartVec").FindPropertyRelative("y").floatValue = -202.5f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_y", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec));

        so1.FindProperty("myStartVec").FindPropertyRelative("z").floatValue = 303.25f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_z", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec));

        so1.FindProperty("myStartVec").FindPropertyRelative("w").floatValue = -404.75f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_w", FmtVecField(vfMain) + "|t=" + FmtVec4(t1.myStartVec));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var vfDetached = new Vector4Field { bindingPath = "otherVec" };
        var retProp = vfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtVecField(vfDetached));
        m_Root.Add(vfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtVecField(vfDetached));

        // 2. Mixed multi-object editing (both z == w and z != w controls on target[0]).
        var mA = ScriptableObject.CreateInstance<Vector4Target>();
        var mB = ScriptableObject.CreateInstance<Vector4Target>();
        mA.multiVec = new Vector4(1f, 10f, 100f, 100f);
        mB.multiVec = new Vector4(2f, 10f, 200f, 300f);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var vfMulti = new Vector4Field();
        var multiEvts = new List<string>();
        vfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtVec4(e.previousValue) + "->" + FmtVec4(e.newValue)));
        m_Root.Add(vfMulti);
        vfMulti.BindProperty(soMulti.FindProperty("multiVec"));
        Frames();
        Put("multi.eq_zw.initial", FmtVecField(vfMulti) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed X with z == w on target[0]: preserves mixed Z and mixed W on both targets.
        Sub(vfMulti, "x").value = 5f;
        Frames();
        Put("multi.eq_zw.ui_edit_x", FmtVecField(vfMulti) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit non-mixed Y with z == w on target[0]: preserves mixed Z and mixed W.
        Sub(vfMulti, "y").value = 15f;
        Frames();
        Put("multi.eq_zw.ui_edit_y", FmtVecField(vfMulti) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed W to new value 400f (differs from z): resolves W while preserving mixed Z.
        Sub(vfMulti, "w").value = 400f;
        Frames();
        Put("multi.eq_zw.ui_edit_w", FmtVecField(vfMulti) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Control with z != w on target[0]: mA=(1,10,100,1000), mB=(2,10,200,2000).
        mA.multiVec = new Vector4(1f, 10f, 100f, 1000f);
        mB.multiVec = new Vector4(2f, 10f, 200f, 2000f);
        soMulti.Update();
        Frames();
        Put("multi.neq_zw.initial", FmtVecField(vfMulti));

        // Edit mixed X while z != w on target[0]: measures exact 6000.0 behavior for W.
        Sub(vfMulti, "x").value = 6f;
        Frames();
        Put("multi.neq_zw.ui_edit_x", FmtVecField(vfMulti) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec));

        // Reset to z != w and edit W to target[0].z (100f): measures exact 6000.0 behavior for W == z.
        mA.multiVec = new Vector4(1f, 10f, 100f, 1000f);
        mB.multiVec = new Vector4(2f, 10f, 200f, 2000f);
        soMulti.Update();
        Frames();
        Sub(vfMulti, "w").value = 100f;
        Frames();
        Put("multi.neq_zw.ui_edit_w_to_z", FmtVecField(vfMulti) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec));

        // Whole-vector write changing all 4 components resolves mixed state across all axes.
        vfMulti.value = new Vector4(7f, 8f, 9f, 11f);
        Frames();
        Put("multi.ui_whole_all", FmtVecField(vfMulti) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec));

        // Direct FloatField bound to "multiVec.w" on mixed targets -> only touches W.
        mA.multiVec = new Vector4(10f, 20f, 30f, 40f);
        mB.multiVec = new Vector4(50f, 60f, 70f, 80f);
        soMulti.Update();
        Frames();
        var ffCompW = new FloatField();
        m_Root.Add(ffCompW);
        ffCompW.BindProperty(soMulti.FindProperty("multiVec.w"));
        Frames();
        Put("multi.comp_w_field.initial", FmtSub(ffCompW));
        ffCompW.value = 99f;
        Frames();
        Put("multi.comp_w_field.ui_edit", FmtSub(ffCompW) + "|a=" + FmtVec4(mA.multiVec) + "|b=" + FmtVec4(mB.multiVec));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and Vector2/Vector3/scalar/enum siblings.
        var tTree = ScriptableObject.CreateInstance<Vector4Target>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var tvMain = new Vector4Field { bindingPath = "myStartVec" };
        var tvDotted = new Vector4Field { bindingPath = "nested.deep.plane" };
        var tvArray = new Vector4Field { bindingPath = "vecArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var tvRelOffset = new Vector4Field { bindingPath = "offset" };
        var tfRelOffsetW = new FloatField { bindingPath = "offset.w" };
        nestedGroup.Add(tvRelOffset);
        nestedGroup.Add(tfRelOffsetW);
        var tVec2 = new Vector2Field { bindingPath = "siblingVec2" };
        var tVec3 = new Vector3Field { bindingPath = "siblingVec3" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(tvMain);
        treeRoot.Add(tvDotted);
        treeRoot.Add(tvArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tVec2);
        treeRoot.Add(tVec3);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtVec4(tvMain.value) + "|" + FmtVec4(tvDotted.value) + "|" + FmtVec4(tvArray.value) + "|" +
            FmtVec4(tvRelOffset.value) + "|" + FmtFloat(tfRelOffsetW.value) + "|" +
            FmtVec2(tVec2.value) + "|" + FmtVec3(tVec3.value) + "|" +
            tInt.value.ToString(Inv) + "|" + tPlain.value + "|" + tFlags.value);

        Sub(tvDotted, "w").value = 123.5f;
        Sub(tvArray, "x").value = -9.5f;
        tfRelOffsetW.value = 55.25f;
        tVec2.Q<FloatField>("unity-y-input").value = 13f;
        tVec3.Q<FloatField>("unity-z-input").value = 77f;
        tInt.value = 99;
        tPlain.value = Vec4ControlPlainEnum.Alpha;
        tFlags.value = Vec4ControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtVec4(tTree.nested.deep.plane) + "|" + FmtVec4(tTree.vecArray[1]) + "|" +
            FmtVec4(tTree.nested.offset) + "|" + FmtVec4(tvRelOffset.value) + "|" +
            FmtVec2(tTree.siblingVec2) + "|" + FmtVec3(tTree.siblingVec3) + "|" +
            tTree.scalarInt.ToString(Inv) + "|" + tTree.plainEnum + "|" + tTree.flagsEnum);

        soTree.FindProperty("nested.offset").vector4Value = new Vector4(1f, 2f, 3f, 4f);
        soTree.FindProperty("nested.deep.plane.x").floatValue = 88f;
        soTree.FindProperty("vecArray.Array.data[1].w").floatValue = -44f;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtVec4(tvRelOffset.value) + "|" + FmtFloat(tfRelOffsetW.value) + "|" +
            FmtVec4(tvDotted.value) + "|" + FmtVec4(tvArray.value));

        // 4. PropertyField for Vector4 (Foldout + lazy x/y/z/w child PropertyFields, label, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<Vector4Target>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myStartVec"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e => pfEvents.Add(e.changedProperty.propertyPath));
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial_collapsed", DescribePf(pfAuto) + "|prop_exp=" + FmtBool(soPf.FindProperty("myStartVec").isExpanded) + "|ec=" + pfEvents.Count.ToString(Inv));

        // Expanding the foldout lazily populates and binds x/y/z/w child PropertyFields.
        var pfFoldout = pfAuto.Q<Foldout>();
        pfFoldout.value = true;
        Frames();
        Put("pf.auto.expanded", DescribePf(pfAuto) + "|prop_exp=" + FmtBool(soPf.FindProperty("myStartVec").isExpanded) + "|ec=" + pfEvents.Count.ToString(Inv));

        PfAxisField(pfAuto, "myStartVec", "w").value = 42.5f;
        Frames();
        Put("pf.auto.ui_sub_w", DescribePf(pfAuto) + "|t=" + FmtVec4(tPf.myStartVec) + "|ec=" + pfEvents.Count.ToString(Inv));

        soPf.FindProperty("myStartVec.x").floatValue = 64f;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_x", DescribePf(pfAuto) + "|ec=" + pfEvents.Count.ToString(Inv));

        pfAuto.label = "Custom Plane";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        soPf.FindProperty("nested.offset").isExpanded = true;
        pfAuto.BindProperty(soPf.FindProperty("nested.offset"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec3"));
        Frames();
        Put("pf.rebind_vec3", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("scalarInt"));
        Frames();
        Put("pf.rebind_scalar", DescribePf(pfAuto));

        // PropertyField with mixed multi-object Vector4 (z != w on target[0] too).
        var pmA = ScriptableObject.CreateInstance<Vector4Target>();
        var pmB = ScriptableObject.CreateInstance<Vector4Target>();
        pmA.multiVec = new Vector4(3f, 30f, 300f, 3000f);
        pmB.multiVec = new Vector4(4f, 30f, 400f, 5000f);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        soPfMulti.FindProperty("multiVec").isExpanded = true;
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiVec"), "Multi Vec4");
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti));

        PfAxisField(pfMulti, "multiVec", "x").value = 9f;
        Frames();
        Put("pf.multi.ui_edit_x", DescribePf(pfMulti) + "|a=" + FmtVec4(pmA.multiVec) + "|b=" + FmtVec4(pmB.multiVec));

        PfAxisField(pfMulti, "multiVec", "w").value = 9000f;
        Frames();
        Put("pf.multi.ui_edit_w", DescribePf(pfMulti) + "|a=" + FmtVec4(pmA.multiVec) + "|b=" + FmtVec4(pmB.multiVec));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<Vector4Target>();
        var tRe2 = ScriptableObject.CreateInstance<Vector4Target>();
        tRe1.myStartVec = new Vector4(1f, 2f, 3f, 4f);
        tRe1.otherVec = new Vector4(5f, 6f, 7f, 8f);
        tRe2.myStartVec = new Vector4(70f, 80f, 90f, 100f);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new Vec4RuntimeSource();
        var vfRe = new Vector4Field();
        vfRe.dataSource = rtSource;
        vfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(Vec4RuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(vfRe);
        vfRe.BindProperty(soRe1.FindProperty("myStartVec"));
        Frames();
        Put("unbind_rebind.initial", FmtVecField(vfRe) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfRe.Unbind();
        Frames();
        vfRe.value = new Vector4(99f, 98f, 97f, 96f);
        soRe1.FindProperty("myStartVec").vector4Value = new Vector4(11f, 12f, 13f, 14f);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-vec4-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtVecField(vfRe) + "|t1=" + FmtVec4(tRe1.myStartVec) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtVecField(vfMulti));

        vfRe.BindProperty(soRe1.FindProperty("otherVec"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtVecField(vfRe));
        Sub(vfRe, "w").value = 88f;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtVec4(tRe1.otherVec) + "|t1_start=" + FmtVec4(tRe1.myStartVec));

        vfRe.BindProperty(soRe2.FindProperty("myStartVec"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtVecField(vfRe));
        Sub(vfRe, "x").value = 71f;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtVec4(tRe2.myStartVec) + "|t1_other=" + FmtVec4(tRe1.otherVec));

        soRe1.FindProperty("myStartVec").isExpanded = true;
        soRe2.FindProperty("myStartVec").isExpanded = true;
        var pfRe = new PropertyField(soRe1.FindProperty("myStartVec"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myStartVec").vector4Value = new Vector4(21f, 22f, 23f, 24f);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Vector4 property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new IntegerField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myStartVec"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", badField.value.ToString(Inv));
#endif
    }
}
