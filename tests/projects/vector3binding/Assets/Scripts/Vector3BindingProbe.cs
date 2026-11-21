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

public enum VecControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum VecControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepVec3Data
{
    public Vector3 position;
}

[Serializable]
public struct NestedVec3Data
{
    public Vector3 offset;
    public DeepVec3Data deep;
}

public class Vector3Target : ScriptableObject
{
    public Vector3 zeroVec = Vector3.zero;
    public Vector3 myStartPos = new Vector3(1.5f, -2.25f, 3.75f);
    public Vector3 otherVec = new Vector3(10f, 20f, 30f);
    public Vector3 multiVec = new Vector3(1f, 10f, 100f);
    public NestedVec3Data nested = new NestedVec3Data
    {
        offset = new Vector3(4f, 5f, 6f),
        deep = new DeepVec3Data { position = new Vector3(-7f, 8.5f, -9.25f) }
    };
    public Vector3[] vecArray = new[]
    {
        new Vector3(0.5f, 1.5f, 2.5f),
        new Vector3(3.5f, 4.5f, 5.5f)
    };
    public int scalarInt = 42;
    public VecControlPlainEnum plainEnum = VecControlPlainEnum.Beta;
    public VecControlFlagsEnum flagsEnum = VecControlFlagsEnum.BitA | VecControlFlagsEnum.BitB;
}

public class Vec3RuntimeSource
{
    public string tip = "rt-vec3-tip-0";
}

public class Vector3BindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VECTOR3_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<Vector3BindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("VECTOR3_BINDING_OUT");
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
    static string FmtVec(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";

    static FloatField Sub(Vector3Field f, string axis) => f.Q<FloatField>("unity-" + axis + "-input");

    static string FmtSub(FloatField ff) =>
        ff == null ? "null" : FmtFloat(ff.value) + ":" + (ff.text ?? "null") + ":" + FmtBool(ff.showMixedValue);

    static string FmtVecField(Vector3Field f) =>
        FmtVec(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|x=" + FmtSub(Sub(f, "x")) +
        "|y=" + FmtSub(Sub(f, "y")) +
        "|z=" + FmtSub(Sub(f, "z"));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is Vector3Field vf)
        {
            bool hasLabelCls = vf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = vf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = vf.ClassListContains(BaseField<Vector3>.alignedFieldUssClassName);
            bool hasVecCls = vf.ClassListContains(Vector3Field.ussClassName);
            var xf = Sub(vf, "x");
            bool subAligned = xf != null && xf.ClassListContains(BaseField<float>.alignedFieldUssClassName);
            return "Vector3Field|" + vf.name + "|" + (vf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasVecCls) + FmtBool(subAligned) + "|" +
                   FmtVecField(vf);
        }
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
        var t1 = ScriptableObject.CreateInstance<Vector3Target>();
        var so1 = new SerializedObject(t1);

        var vfZero = new Vector3Field();
        var zeroEvts = new List<string>();
        var zeroSubEvts = new List<string>();
        vfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtVec(e.previousValue) + "->" + FmtVec(e.newValue)));
        Sub(vfZero, "x").RegisterValueChangedCallback(e => zeroSubEvts.Add("x:" + FmtFloat(e.previousValue) + "->" + FmtFloat(e.newValue)));
        m_Root.Add(vfZero);
        vfZero.BindProperty(so1.FindProperty("zeroVec"));
        Frames();
        Put("direct.zero.initial", FmtVecField(vfZero) + "|e=" + string.Join(";", zeroEvts) + "|se=" + string.Join(";", zeroSubEvts));

        // No-op whole-vector and component writes + no-op SO update.
        vfZero.value = Vector3.zero;
        Sub(vfZero, "x").value = 0f;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtVecField(vfZero) + "|e=" + zeroEvts.Count.ToString(Inv) + "|se=" + zeroSubEvts.Count.ToString(Inv) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        // Non-zero initial sync via BindProperty(SerializedProperty) and whole/component edits both ways.
        var vfMain = new Vector3Field();
        var mainEvts = new List<string>();
        vfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtVec(e.previousValue) + "->" + FmtVec(e.newValue)));
        m_Root.Add(vfMain);
        vfMain.BindProperty(so1.FindProperty("myStartPos"));
        Frames();
        Put("direct.main.initial", FmtVecField(vfMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO whole vector.
        vfMain.value = new Vector3(10f, 20f, 30f);
        Frames();
        Put("direct.main.ui_whole", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos) + "|p=" + FmtVec(so1.FindProperty("myStartPos").vector3Value) + "|ec=" + mainEvts.Count.ToString(Inv));

        // No-op write with same non-zero vector and same sub-field value.
        int countBeforeNoop = mainEvts.Count;
        vfMain.value = new Vector3(10f, 20f, 30f);
        Sub(vfMain, "y").value = 20f;
        Frames();
        Put("direct.main.noop", FmtVec(t1.myStartPos) + "|ec_delta=" + (mainEvts.Count - countBeforeNoop).ToString(Inv));

        // SO -> UI whole vector.
        so1.FindProperty("myStartPos").vector3Value = new Vector3(-4f, 5.5f, -6.25f);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos) + "|ec=" + mainEvts.Count.ToString(Inv));

        // Direct C# target mutation picked up on panel update.
        t1.myStartPos = new Vector3(7f, 8f, 9f);
        Frames();
        Put("direct.main.external_target", FmtVecField(vfMain) + "|ec=" + mainEvts.Count.ToString(Inv));

        // UI -> SO per-component edits via sub FloatFields.
        Sub(vfMain, "x").value = 11.5f;
        Frames();
        Put("direct.main.ui_x", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos) + "|ec=" + mainEvts.Count.ToString(Inv));

        Sub(vfMain, "y").value = -22.25f;
        Frames();
        Put("direct.main.ui_y", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos) + "|ec=" + mainEvts.Count.ToString(Inv));

        Sub(vfMain, "z").value = 33.75f;
        Frames();
        Put("direct.main.ui_z", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos) + "|ec=" + mainEvts.Count.ToString(Inv));

        // SO -> UI per-component edits via FindPropertyRelative.
        so1.FindProperty("myStartPos").FindPropertyRelative("x").floatValue = 101f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_x", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos));

        so1.FindProperty("myStartPos").FindPropertyRelative("y").floatValue = -202.5f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_y", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos));

        so1.FindProperty("myStartPos").FindPropertyRelative("z").floatValue = 303.25f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_z", FmtVecField(vfMain) + "|t=" + FmtVec(t1.myStartPos));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var vfDetached = new Vector3Field { bindingPath = "otherVec" };
        var retProp = vfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtVecField(vfDetached));
        m_Root.Add(vfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtVecField(vfDetached));

        // 2. Mixed multi-object editing (per-component preservation and showMixedValue).
        var mA = ScriptableObject.CreateInstance<Vector3Target>();
        var mB = ScriptableObject.CreateInstance<Vector3Target>();
        mA.multiVec = new Vector3(1f, 10f, 100f);
        mB.multiVec = new Vector3(2f, 10f, 200f);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var vfMulti = new Vector3Field();
        var multiEvts = new List<string>();
        vfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtVec(e.previousValue) + "->" + FmtVec(e.newValue)));
        m_Root.Add(vfMulti);
        vfMulti.BindProperty(soMulti.FindProperty("multiVec"));
        Frames();
        Put("multi.initial", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed X component via sub-field -> must update X on both A & B while preserving A.z=100, B.z=200.
        Sub(vfMulti, "x").value = 5f;
        Frames();
        Put("multi.ui_edit_mixed_x", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit non-mixed Y component while Z is still mixed -> must preserve A.z=100, B.z=200.
        Sub(vfMulti, "y").value = 15f;
        Frames();
        Put("multi.ui_edit_same_y", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed Z component to target[0]'s existing value (100f) -> measure exact Editor contract.
        Sub(vfMulti, "z").value = 100f;
        Frames();
        Put("multi.ui_edit_mixed_z_to_t0", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Edit mixed Z component to a new value (300f) -> resolves mixed state across both targets.
        Sub(vfMulti, "z").value = 300f;
        Frames();
        Put("multi.ui_edit_mixed_z_new", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec) + "|ec=" + multiEvts.Count.ToString(Inv));

        // Transition unified -> single-axis mixed via external target mutation.
        mB.multiVec = new Vector3(5f, 15f, 999f);
        soMulti.Update();
        Frames();
        Put("multi.so_unified_to_mixed_z", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec));

        // Reset to (1,10,100) vs (2,10,200) and test whole-vector assignment on mixed targets.
        mA.multiVec = new Vector3(1f, 10f, 100f);
        mB.multiVec = new Vector3(2f, 10f, 200f);
        soMulti.Update();
        Frames();
        Put("multi.reset_mixed", FmtVecField(vfMulti));

        // Whole-vector write changing only Y (keeping X=1, Z=100 equal to target[0]).
        vfMulti.value = new Vector3(1f, 25f, 100f);
        Frames();
        Put("multi.ui_whole_only_y", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec));

        // Whole-vector write changing all 3 components to new values.
        vfMulti.value = new Vector3(7f, 8f, 9f);
        Frames();
        Put("multi.ui_whole_all", FmtVecField(vfMulti) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec));

        // Direct FloatField bound to "multiVec.x" on mixed targets -> only touches X.
        mA.multiVec = new Vector3(10f, 20f, 30f);
        mB.multiVec = new Vector3(40f, 50f, 60f);
        soMulti.Update();
        Frames();
        var ffCompX = new FloatField();
        m_Root.Add(ffCompX);
        ffCompX.BindProperty(soMulti.FindProperty("multiVec.x"));
        Frames();
        Put("multi.comp_x_field.initial", FmtSub(ffCompX));
        ffCompX.value = 77f;
        Frames();
        Put("multi.comp_x_field.ui_edit", FmtSub(ffCompX) + "|a=" + FmtVec(mA.multiVec) + "|b=" + FmtVec(mB.multiVec));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and scalar/enum siblings.
        var tTree = ScriptableObject.CreateInstance<Vector3Target>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var tvMain = new Vector3Field { bindingPath = "myStartPos" };
        var tvDotted = new Vector3Field { bindingPath = "nested.deep.position" };
        var tvArray = new Vector3Field { bindingPath = "vecArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var tvRelOffset = new Vector3Field { bindingPath = "offset" };
        var tfRelOffsetY = new FloatField { bindingPath = "offset.y" };
        nestedGroup.Add(tvRelOffset);
        nestedGroup.Add(tfRelOffsetY);
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(tvMain);
        treeRoot.Add(tvDotted);
        treeRoot.Add(tvArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtVec(tvMain.value) + "|" + FmtVec(tvDotted.value) + "|" + FmtVec(tvArray.value) + "|" +
            FmtVec(tvRelOffset.value) + "|" + FmtFloat(tfRelOffsetY.value) + "|" +
            tInt.value.ToString(Inv) + "|" + tPlain.value + "|" + tFlags.value);

        // Tree UI -> SO edits across nested/relative/array/siblings.
        Sub(tvDotted, "z").value = 123.5f;
        Sub(tvArray, "x").value = -9.5f;
        tfRelOffsetY.value = 55.25f;
        tInt.value = 99;
        tPlain.value = VecControlPlainEnum.Alpha;
        tFlags.value = VecControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtVec(tTree.nested.deep.position) + "|" + FmtVec(tTree.vecArray[1]) + "|" +
            FmtVec(tTree.nested.offset) + "|" + FmtVec(tvRelOffset.value) + "|" +
            tTree.scalarInt.ToString(Inv) + "|" + tTree.plainEnum + "|" + tTree.flagsEnum);

        // Tree SO -> UI edits across nested/array.
        soTree.FindProperty("nested.offset").vector3Value = new Vector3(1f, 2f, 3f);
        soTree.FindProperty("nested.deep.position.x").floatValue = 88f;
        soTree.FindProperty("vecArray.Array.data[1].y").floatValue = -44f;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtVec(tvRelOffset.value) + "|" + FmtFloat(tfRelOffsetY.value) + "|" +
            FmtVec(tvDotted.value) + "|" + FmtVec(tvArray.value));

        // 4. PropertyField for Vector3 (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<Vector3Target>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myStartPos"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e => pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtVec(e.changedProperty.vector3Value)));
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<Vector3Field>();
        Sub(pfChild, "y").value = 42.5f;
        Frames();
        Put("pf.auto.ui_sub_y", DescribePf(pfAuto) + "|t=" + FmtVec(tPf.myStartPos) + "|evts=" + pfEvents.Count.ToString(Inv));

        pfChild.value = new Vector3(-1f, -2f, -3f);
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtVec(tPf.myStartPos) + "|evts=" + pfEvents.Count.ToString(Inv));

        soPf.FindProperty("myStartPos.z").floatValue = 64f;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_z", DescribePf(pfAuto) + "|evts=" + pfEvents.Count.ToString(Inv));

        // Custom label and dynamic label update.
        pfAuto.label = "Custom Position";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        // Rebind PropertyField to another Vector3 property -> verify child reuse.
        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.offset"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        // Rebind PropertyField to scalar property -> verify child replaced.
        pfAuto.BindProperty(soPf.FindProperty("scalarInt"));
        Frames();
        Put("pf.rebind_scalar", DescribePf(pfAuto));

        // PropertyField with mixed multi-object Vector3.
        var pmA = ScriptableObject.CreateInstance<Vector3Target>();
        var pmB = ScriptableObject.CreateInstance<Vector3Target>();
        pmA.multiVec = new Vector3(3f, 30f, 300f);
        pmB.multiVec = new Vector3(4f, 30f, 400f);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiVec"), "Multi Vec");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtVec(e.changedProperty.vector3Value)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + pfMultiEvts.Count.ToString(Inv));

        var pfMultiVf = pfMulti.Q<Vector3Field>();
        Sub(pfMultiVf, "x").value = 9f;
        Frames();
        Put("pf.multi.ui_edit_x", DescribePf(pfMulti) + "|a=" + FmtVec(pmA.multiVec) + "|b=" + FmtVec(pmB.multiVec) + "|ec=" + pfMultiEvts.Count.ToString(Inv));

        Sub(pfMultiVf, "y").value = 35f;
        Frames();
        Put("pf.multi.ui_edit_y", DescribePf(pfMulti) + "|a=" + FmtVec(pmA.multiVec) + "|b=" + FmtVec(pmB.multiVec) + "|ec=" + pfMultiEvts.Count.ToString(Inv));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<Vector3Target>();
        var tRe2 = ScriptableObject.CreateInstance<Vector3Target>();
        tRe1.myStartPos = new Vector3(1f, 2f, 3f);
        tRe1.otherVec = new Vector3(4f, 5f, 6f);
        tRe2.myStartPos = new Vector3(70f, 80f, 90f);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new Vec3RuntimeSource();
        var vfRe = new Vector3Field();
        vfRe.dataSource = rtSource;
        vfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(Vec3RuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(vfRe);
        vfRe.BindProperty(soRe1.FindProperty("myStartPos"));
        Frames();
        Put("unbind_rebind.initial", FmtVecField(vfRe) + "|tip=" + (vfRe.tooltip ?? "null"));

        // Unbind: check state, verify UI->SO and SO->UI stop, runtime DataBinding continues working.
        vfRe.Unbind();
        Frames();
        vfRe.value = new Vector3(99f, 98f, 97f);
        soRe1.FindProperty("myStartPos").vector3Value = new Vector3(11f, 12f, 13f);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-vec3-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtVecField(vfRe) + "|t1=" + FmtVec(tRe1.myStartPos) + "|tip=" + (vfRe.tooltip ?? "null"));

        // Unbind on mixed multi-object field: check whether showMixedValue persists or clears.
        vfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtVecField(vfMulti));

        // Rebind to another property on same SO, then another SO.
        vfRe.BindProperty(soRe1.FindProperty("otherVec"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtVecField(vfRe));
        Sub(vfRe, "z").value = 66f;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtVec(tRe1.otherVec) + "|t1_start=" + FmtVec(tRe1.myStartPos));

        vfRe.BindProperty(soRe2.FindProperty("myStartPos"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtVecField(vfRe));
        Sub(vfRe, "x").value = 71f;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtVec(tRe2.myStartPos) + "|t1_other=" + FmtVec(tRe1.otherVec));

        // PropertyField Unbind and rebind to soRe2.
        var pfRe = new PropertyField(soRe1.FindProperty("myStartPos"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myStartPos").vector3Value = new Vector3(21f, 22f, 23f);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Vector3 property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new IntegerField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myStartPos"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", badField.value.ToString(Inv));
#endif
    }
}
