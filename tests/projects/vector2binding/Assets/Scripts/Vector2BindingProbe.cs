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

public enum Vec2ControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum Vec2ControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepVec2Data
{
    public Vector2 uv;
}

[Serializable]
public struct NestedVec2Data
{
    public Vector2 offset;
    public DeepVec2Data deep;
}

public class Vector2Target : ScriptableObject
{
    public Vector2 zeroVec = Vector2.zero;
    public Vector2 myStartUv = new Vector2(1.5f, -2.25f);
    public Vector2 otherVec = new Vector2(10f, 20f);
    public Vector2 multiVec = new Vector2(1f, 100f);
    public NestedVec2Data nested = new NestedVec2Data
    {
        offset = new Vector2(4f, 5f),
        deep = new DeepVec2Data { uv = new Vector2(-7f, 8.5f) }
    };
    public Vector2[] vecArray = new[]
    {
        new Vector2(0.5f, 1.5f),
        new Vector2(3.5f, 4.5f)
    };
    public Vector3 siblingVec3 = new Vector3(9f, 8f, 7f);
    public int scalarInt = 42;
    public Vec2ControlPlainEnum plainEnum = Vec2ControlPlainEnum.Beta;
    public Vec2ControlFlagsEnum flagsEnum = Vec2ControlFlagsEnum.BitA | Vec2ControlFlagsEnum.BitB;
}

public class Vec2RuntimeSource
{
    public string tip = "rt-vec2-tip-0";
}

public class Vector2BindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VECTOR2_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<Vector2BindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("VECTOR2_BINDING_OUT");
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

    static FloatField Sub(Vector2Field f, string axis) => f.Q<FloatField>("unity-" + axis + "-input");

    static string FmtSub(FloatField ff) =>
        ff == null ? "null" : FmtFloat(ff.value) + ":" + (ff.text ?? "null") + ":" + FmtBool(ff.showMixedValue);

    static string FmtVecField(Vector2Field f) =>
        FmtVec2(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|x=" + FmtSub(Sub(f, "x")) +
        "|y=" + FmtSub(Sub(f, "y"));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is Vector2Field vf)
        {
            bool hasLabelCls = vf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = vf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = vf.ClassListContains(BaseField<Vector2>.alignedFieldUssClassName);
            bool hasVecCls = vf.ClassListContains(Vector2Field.ussClassName);
            var xf = Sub(vf, "x");
            bool subAligned = xf != null && xf.ClassListContains(BaseField<float>.alignedFieldUssClassName);
            return "Vector2Field|" + vf.name + "|" + (vf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasVecCls) + FmtBool(subAligned) + "|" +
                   FmtVecField(vf);
        }
        if (child is Vector3Field v3)
            return "Vector3Field|" + v3.name + "|" + (v3.label ?? "null") + "|" + FmtVec3(v3.value);
        if (child is IntegerField i)
            return "IntegerField|" + i.name + "|" + (i.label ?? "null") + "|" + i.value.ToString(Inv);
        return child.GetType().Name + "|" + child.name;
    }
#endif

    void Run()
    {
#if UNITY_EDITOR
        var document = GetComponent<UIDocument>();
        m_Root = document.rootVisualElement;
        Frames(1);

        // 1. Initial sync (zero default and non-zero), no-op writes, detached-then-attached.
        var t1 = ScriptableObject.CreateInstance<Vector2Target>();
        var so1 = new SerializedObject(t1);

        var vfZero = new Vector2Field();
        var zeroEvts = new List<string>();
        var zeroSubEvts = new List<string>();
        vfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtVec2(e.previousValue) + "->" + FmtVec2(e.newValue)));
        Sub(vfZero, "x").RegisterValueChangedCallback(e => zeroSubEvts.Add("x:" + FmtFloat(e.previousValue) + "->" + FmtFloat(e.newValue)));
        m_Root.Add(vfZero);
        vfZero.BindProperty(so1.FindProperty("zeroVec"));
        Frames();
        Put("direct.zero.initial", FmtVecField(vfZero) + "|e=" + string.Join(";", zeroEvts) + "|se=" + string.Join(";", zeroSubEvts));

        vfZero.value = Vector2.zero;
        Sub(vfZero, "x").value = 0f;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtVecField(vfZero) + "|e=" + zeroEvts.Count.ToString(Inv) + "|se=" + zeroSubEvts.Count.ToString(Inv) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var vfMain = new Vector2Field();
        var mainEvts = new List<string>();
        vfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtVec2(e.previousValue) + "->" + FmtVec2(e.newValue)));
        m_Root.Add(vfMain);
        vfMain.BindProperty(so1.FindProperty("myStartUv"));
        Frames();
        Put("direct.main.initial", FmtVecField(vfMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO whole vector.
        vfMain.value = new Vector2(10f, 20f);
        Frames();
        Put("direct.main.ui_whole", FmtVecField(vfMain) + "|t=" + FmtVec2(t1.myStartUv) + "|p=" + FmtVec2(so1.FindProperty("myStartUv").vector2Value) + "|ec=" + mainEvts.Count.ToString(Inv));

        // No-op write with same non-zero vector and same sub-field value.
        int countBeforeNoop = mainEvts.Count;
        vfMain.value = new Vector2(10f, 20f);
        Sub(vfMain, "y").value = 20f;
        Frames();
        Put("direct.main.noop", FmtVec2(t1.myStartUv) + "|ec_delta=" + (mainEvts.Count - countBeforeNoop).ToString(Inv));

        // SO -> UI whole vector.
        so1.FindProperty("myStartUv").vector2Value = new Vector2(-4f, 5.5f);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtVecField(vfMain) + "|t=" + FmtVec2(t1.myStartUv) + "|ec=" + mainEvts.Count.ToString(Inv));

        // Direct C# target mutation picked up on panel update.
        t1.myStartUv = new Vector2(7f, 8f);
        Frames();
        Put("direct.main.external_target", FmtVecField(vfMain) + "|ec=" + mainEvts.Count.ToString(Inv));

        // UI -> SO per-component edits via sub FloatFields.
        Sub(vfMain, "x").value = 11.5f;
        Frames();
        Put("direct.main.ui_x", FmtVecField(vfMain) + "|t=" + FmtVec2(t1.myStartUv) + "|ec=" + mainEvts.Count.ToString(Inv));

        Sub(vfMain, "y").value = -22.25f;
        Frames();
        Put("direct.main.ui_y", FmtVecField(vfMain) + "|t=" + FmtVec2(t1.myStartUv) + "|ec=" + mainEvts.Count.ToString(Inv));

        // SO -> UI per-component edits via FindPropertyRelative.
        so1.FindProperty("myStartUv").FindPropertyRelative("x").floatValue = 101f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_x", FmtVecField(vfMain) + "|t=" + FmtVec2(t1.myStartUv));

        so1.FindProperty("myStartUv").FindPropertyRelative("y").floatValue = -202.5f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_y", FmtVecField(vfMain) + "|t=" + FmtVec2(t1.myStartUv));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var vfDetached = new Vector2Field { bindingPath = "otherVec" };
        var retProp = vfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtVecField(vfDetached));
        m_Root.Add(vfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtVecField(vfDetached));

        // 2. Mixed multi-object editing (per-component preservation and showMixedValue).
        var mA = ScriptableObject.CreateInstance<Vector2Target>();
        var mB = ScriptableObject.CreateInstance<Vector2Target>();
        mA.multiVec = new Vector2(1f, 100f);
        mB.multiVec = new Vector2(2f, 200f);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var vfMulti = new Vector2Field();
        var multiEvts = new List<string>();
        vfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtVec2(e.previousValue) + "->" + FmtVec2(e.newValue)));
        m_Root.Add(vfMulti);
        vfMulti.BindProperty(soMulti.FindProperty("multiVec"));
        Frames();
        Put("multi.initial", FmtVecField(vfMulti) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed X component via sub-field -> updates X on both A & B while preserving A.y=100, B.y=200.
        Sub(vfMulti, "x").value = 5f;
        Frames();
        Put("multi.ui_edit_mixed_x", FmtVecField(vfMulti) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit now non-mixed X component while Y is still mixed -> must preserve A.y=100, B.y=200.
        Sub(vfMulti, "x").value = 6f;
        Frames();
        Put("multi.ui_edit_same_x", FmtVecField(vfMulti) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed Y component to target[0]'s existing value (100f).
        Sub(vfMulti, "y").value = 100f;
        Frames();
        Put("multi.ui_edit_mixed_y_to_t0", FmtVecField(vfMulti) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed Y component to a new value (300f) -> resolves mixed state across both targets.
        Sub(vfMulti, "y").value = 300f;
        Frames();
        Put("multi.ui_edit_mixed_y_new", FmtVecField(vfMulti) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Reset to mixed X (1 vs 2) and non-mixed Y (10 vs 10), test whole-vector edit changing only Y.
        mA.multiVec = new Vector2(1f, 10f);
        mB.multiVec = new Vector2(2f, 10f);
        soMulti.Update();
        Frames();
        Put("multi.reset_partial_mixed", FmtVecField(vfMulti));

        vfMulti.value = new Vector2(1f, 25f);
        Frames();
        Put("multi.ui_whole_only_y", FmtVecField(vfMulti) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec));

        // Whole-vector write changing both axes resolves mixed X too.
        vfMulti.value = new Vector2(7f, 8f);
        Frames();
        Put("multi.ui_whole_all", FmtVecField(vfMulti) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec));

        // Direct FloatField bound to "multiVec.x" on mixed targets -> only touches X.
        mA.multiVec = new Vector2(10f, 20f);
        mB.multiVec = new Vector2(40f, 50f);
        soMulti.Update();
        Frames();
        var ffCompX = new FloatField();
        m_Root.Add(ffCompX);
        ffCompX.BindProperty(soMulti.FindProperty("multiVec.x"));
        Frames();
        Put("multi.comp_x_field.initial", FmtSub(ffCompX));
        ffCompX.value = 77f;
        Frames();
        Put("multi.comp_x_field.ui_edit", FmtSub(ffCompX) + "|a=" + FmtVec2(mA.multiVec) + "|b=" + FmtVec2(mB.multiVec));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and Vector3/scalar/enum siblings.
        var tTree = ScriptableObject.CreateInstance<Vector2Target>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var tvMain = new Vector2Field { bindingPath = "myStartUv" };
        var tvDotted = new Vector2Field { bindingPath = "nested.deep.uv" };
        var tvArray = new Vector2Field { bindingPath = "vecArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var tvRelOffset = new Vector2Field { bindingPath = "offset" };
        var tfRelOffsetY = new FloatField { bindingPath = "offset.y" };
        nestedGroup.Add(tvRelOffset);
        nestedGroup.Add(tfRelOffsetY);
        var tVec3 = new Vector3Field { bindingPath = "siblingVec3" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(tvMain);
        treeRoot.Add(tvDotted);
        treeRoot.Add(tvArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tVec3);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtVec2(tvMain.value) + "|" + FmtVec2(tvDotted.value) + "|" + FmtVec2(tvArray.value) + "|" +
            FmtVec2(tvRelOffset.value) + "|" + FmtFloat(tfRelOffsetY.value) + "|" +
            FmtVec3(tVec3.value) + "|" + tInt.value.ToString(Inv) + "|" + tPlain.value + "|" + tFlags.value);

        Sub(tvDotted, "y").value = 123.5f;
        Sub(tvArray, "x").value = -9.5f;
        tfRelOffsetY.value = 55.25f;
        tVec3.Q<FloatField>("unity-z-input").value = 77f;
        tInt.value = 99;
        tPlain.value = Vec2ControlPlainEnum.Alpha;
        tFlags.value = Vec2ControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtVec2(tTree.nested.deep.uv) + "|" + FmtVec2(tTree.vecArray[1]) + "|" +
            FmtVec2(tTree.nested.offset) + "|" + FmtVec2(tvRelOffset.value) + "|" +
            FmtVec3(tTree.siblingVec3) + "|" + tTree.scalarInt.ToString(Inv) + "|" + tTree.plainEnum + "|" + tTree.flagsEnum);

        soTree.FindProperty("nested.offset").vector2Value = new Vector2(1f, 2f);
        soTree.FindProperty("nested.deep.uv.x").floatValue = 88f;
        soTree.FindProperty("vecArray.Array.data[1].y").floatValue = -44f;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtVec2(tvRelOffset.value) + "|" + FmtFloat(tfRelOffsetY.value) + "|" +
            FmtVec2(tvDotted.value) + "|" + FmtVec2(tvArray.value));

        // 4. PropertyField for Vector2 (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<Vector2Target>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myStartUv"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.Vector2)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtVec2(e.changedProperty.vector2Value));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<Vector2Field>();
        Sub(pfChild, "y").value = 42.5f;
        Frames();
        Put("pf.auto.ui_sub_y", DescribePf(pfAuto) + "|t=" + FmtVec2(tPf.myStartUv) + "|evts=" + pfEvents.Count.ToString(Inv));

        pfChild.value = new Vector2(-1f, -2f);
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtVec2(tPf.myStartUv) + "|evts=" + pfEvents.Count.ToString(Inv));

        soPf.FindProperty("myStartUv.x").floatValue = 64f;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_x", DescribePf(pfAuto) + "|evts=" + pfEvents.Count.ToString(Inv));

        pfAuto.label = "Custom UV";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
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

        // PropertyField with mixed multi-object Vector2.
        var pmA = ScriptableObject.CreateInstance<Vector2Target>();
        var pmB = ScriptableObject.CreateInstance<Vector2Target>();
        pmA.multiVec = new Vector2(3f, 300f);
        pmB.multiVec = new Vector2(4f, 400f);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiVec"), "Multi Vec2");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtVec2(e.changedProperty.vector2Value)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + pfMultiEvts.Count.ToString(Inv));

        var pfMultiVf = pfMulti.Q<Vector2Field>();
        Sub(pfMultiVf, "x").value = 9f;
        Frames();
        Put("pf.multi.ui_edit_x", DescribePf(pfMulti) + "|a=" + FmtVec2(pmA.multiVec) + "|b=" + FmtVec2(pmB.multiVec) + "|ec=" + pfMultiEvts.Count.ToString(Inv));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<Vector2Target>();
        var tRe2 = ScriptableObject.CreateInstance<Vector2Target>();
        tRe1.myStartUv = new Vector2(1f, 2f);
        tRe1.otherVec = new Vector2(4f, 5f);
        tRe2.myStartUv = new Vector2(70f, 80f);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new Vec2RuntimeSource();
        var vfRe = new Vector2Field();
        vfRe.dataSource = rtSource;
        vfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(Vec2RuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(vfRe);
        vfRe.BindProperty(soRe1.FindProperty("myStartUv"));
        Frames();
        Put("unbind_rebind.initial", FmtVecField(vfRe) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfRe.Unbind();
        Frames();
        vfRe.value = new Vector2(99f, 98f);
        soRe1.FindProperty("myStartUv").vector2Value = new Vector2(11f, 12f);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-vec2-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtVecField(vfRe) + "|t1=" + FmtVec2(tRe1.myStartUv) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtVecField(vfMulti));

        vfRe.BindProperty(soRe1.FindProperty("otherVec"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtVecField(vfRe));
        Sub(vfRe, "y").value = 66f;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtVec2(tRe1.otherVec) + "|t1_start=" + FmtVec2(tRe1.myStartUv));

        vfRe.BindProperty(soRe2.FindProperty("myStartUv"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtVecField(vfRe));
        Sub(vfRe, "x").value = 71f;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtVec2(tRe2.myStartUv) + "|t1_other=" + FmtVec2(tRe1.otherVec));

        var pfRe = new PropertyField(soRe1.FindProperty("myStartUv"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myStartUv").vector2Value = new Vector2(21f, 22f);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Vector2 property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new IntegerField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myStartUv"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", badField.value.ToString(Inv));
#endif
    }
}
