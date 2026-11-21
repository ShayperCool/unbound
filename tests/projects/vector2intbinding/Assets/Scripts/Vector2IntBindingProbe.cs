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

public enum Vec2IntControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum Vec2IntControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepVec2IntData
{
    public Vector2Int cell;
}

[Serializable]
public struct NestedVec2IntData
{
    public Vector2Int offset;
    public DeepVec2IntData deep;
}

public class Vector2IntTarget : ScriptableObject
{
    public Vector2Int zeroVec = Vector2Int.zero;
    public Vector2Int myGridPos = new Vector2Int(15, -24);
    public Vector2Int otherVec = new Vector2Int(10, 20);
    public Vector2Int extremeVec = new Vector2Int(int.MinValue, int.MaxValue);
    public Vector2Int multiVec = new Vector2Int(1, 100);
    public NestedVec2IntData nested = new NestedVec2IntData
    {
        offset = new Vector2Int(4, 5),
        deep = new DeepVec2IntData { cell = new Vector2Int(-7, 85) }
    };
    public Vector2Int[] vecArray = new[]
    {
        new Vector2Int(1, 2),
        new Vector2Int(30, -40)
    };
    public Vector2 siblingVec2 = new Vector2(1.5f, 2.5f);
    public Vector3 siblingVec3 = new Vector3(9f, 8f, 7f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public Vec2IntControlPlainEnum plainEnum = Vec2IntControlPlainEnum.Beta;
    public Vec2IntControlFlagsEnum flagsEnum = Vec2IntControlFlagsEnum.BitA | Vec2IntControlFlagsEnum.BitB;
}

public class Vec2IntRuntimeSource
{
    public string tip = "rt-vec2int-tip-0";
}

public class Vector2IntBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VECTOR2INT_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<Vector2IntBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("VECTOR2INT_BINDING_OUT");
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
    static string FmtVec2Int(Vector2Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + ")";
    static string FmtVec2(Vector2 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + ")";
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtVec4(Vector4 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + "," + FmtFloat(v.w) + ")";

    static IntegerField Sub(Vector2IntField f, string axis) => f.Q<IntegerField>("unity-" + axis + "-input");

    static string FmtSub(IntegerField inf) =>
        inf == null ? "null" : FmtInt(inf.value) + ":" + (inf.text ?? "null") + ":" + FmtBool(inf.showMixedValue);

    static string FmtVecField(Vector2IntField f) =>
        FmtVec2Int(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|x=" + FmtSub(Sub(f, "x")) +
        "|y=" + FmtSub(Sub(f, "y"));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is Vector2IntField vf)
        {
            bool hasLabelCls = vf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = vf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = vf.ClassListContains(BaseField<Vector2Int>.alignedFieldUssClassName);
            bool hasVecCls = vf.ClassListContains(Vector2IntField.ussClassName);
            var xf = Sub(vf, "x");
            bool subAligned = xf != null && xf.ClassListContains(BaseField<int>.alignedFieldUssClassName);
            return "Vector2IntField|" + vf.name + "|" + (vf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasVecCls) + FmtBool(subAligned) + "|" +
                   FmtVecField(vf);
        }
        if (child is Vector2Field v2)
            return "Vector2Field|" + v2.name + "|" + (v2.label ?? "null") + "|" + FmtVec2(v2.value);
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

        // 1. Initial sync (zero default, non-zero, extreme ints), no-op writes, detached-then-attached.
        var t1 = ScriptableObject.CreateInstance<Vector2IntTarget>();
        var so1 = new SerializedObject(t1);

        var vfZero = new Vector2IntField();
        var zeroEvts = new List<string>();
        var zeroSubEvts = new List<string>();
        vfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtVec2Int(e.previousValue) + "->" + FmtVec2Int(e.newValue)));
        Sub(vfZero, "x").RegisterValueChangedCallback(e => zeroSubEvts.Add("x:" + FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(vfZero);
        vfZero.BindProperty(so1.FindProperty("zeroVec"));
        Frames();
        Put("direct.zero.initial", FmtVecField(vfZero) + "|e=" + string.Join(";", zeroEvts) + "|se=" + string.Join(";", zeroSubEvts));

        vfZero.value = Vector2Int.zero;
        Sub(vfZero, "x").value = 0;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtVecField(vfZero) + "|e=" + FmtInt(zeroEvts.Count) + "|se=" + FmtInt(zeroSubEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var vfExtreme = new Vector2IntField();
        m_Root.Add(vfExtreme);
        vfExtreme.BindProperty(so1.FindProperty("extremeVec"));
        Frames();
        Put("direct.extreme.initial", FmtVecField(vfExtreme));
        Sub(vfExtreme, "x").value = int.MaxValue;
        Frames();
        Put("direct.extreme.ui_x", FmtVecField(vfExtreme) + "|t=" + FmtVec2Int(t1.extremeVec));

        var vfMain = new Vector2IntField();
        var mainEvts = new List<string>();
        vfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtVec2Int(e.previousValue) + "->" + FmtVec2Int(e.newValue)));
        m_Root.Add(vfMain);
        vfMain.BindProperty(so1.FindProperty("myGridPos"));
        Frames();
        Put("direct.main.initial", FmtVecField(vfMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO whole vector.
        vfMain.value = new Vector2Int(10, 20);
        Frames();
        Put("direct.main.ui_whole", FmtVecField(vfMain) + "|t=" + FmtVec2Int(t1.myGridPos) + "|p=" + FmtVec2Int(so1.FindProperty("myGridPos").vector2IntValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op write with same non-zero vector and same sub-field value.
        int countBeforeNoop = mainEvts.Count;
        vfMain.value = new Vector2Int(10, 20);
        Sub(vfMain, "y").value = 20;
        Frames();
        Put("direct.main.noop", FmtVec2Int(t1.myGridPos) + "|ec_delta=" + FmtInt(mainEvts.Count - countBeforeNoop));

        // SO -> UI whole vector.
        so1.FindProperty("myGridPos").vector2IntValue = new Vector2Int(-4, 55);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtVecField(vfMain) + "|t=" + FmtVec2Int(t1.myGridPos) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation picked up on panel update.
        t1.myGridPos = new Vector2Int(7, -8);
        Frames();
        Put("direct.main.external_target", FmtVecField(vfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO per-component edits via sub IntegerFields.
        Sub(vfMain, "x").value = 115;
        Frames();
        Put("direct.main.ui_x", FmtVecField(vfMain) + "|t=" + FmtVec2Int(t1.myGridPos) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(vfMain, "y").value = -222;
        Frames();
        Put("direct.main.ui_y", FmtVecField(vfMain) + "|t=" + FmtVec2Int(t1.myGridPos) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI per-component edits via FindPropertyRelative.
        so1.FindProperty("myGridPos").FindPropertyRelative("x").intValue = 101;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_x", FmtVecField(vfMain) + "|t=" + FmtVec2Int(t1.myGridPos));

        so1.FindProperty("myGridPos").FindPropertyRelative("y").intValue = -202;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_y", FmtVecField(vfMain) + "|t=" + FmtVec2Int(t1.myGridPos));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var vfDetached = new Vector2IntField { bindingPath = "otherVec" };
        var retProp = vfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtVecField(vfDetached));
        m_Root.Add(vfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtVecField(vfDetached));

        // 2. Mixed multi-object editing (per-component preservation and showMixedValue).
        var mA = ScriptableObject.CreateInstance<Vector2IntTarget>();
        var mB = ScriptableObject.CreateInstance<Vector2IntTarget>();
        mA.multiVec = new Vector2Int(1, 100);
        mB.multiVec = new Vector2Int(2, 200);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var vfMulti = new Vector2IntField();
        var multiEvts = new List<string>();
        vfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtVec2Int(e.previousValue) + "->" + FmtVec2Int(e.newValue)));
        m_Root.Add(vfMulti);
        vfMulti.BindProperty(soMulti.FindProperty("multiVec"));
        Frames();
        Put("multi.initial", FmtVecField(vfMulti) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed X component via sub-field -> updates X on both A & B while preserving A.y=100, B.y=200.
        Sub(vfMulti, "x").value = 5;
        Frames();
        Put("multi.ui_edit_mixed_x", FmtVecField(vfMulti) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit now non-mixed X component while Y is still mixed -> must preserve A.y=100, B.y=200.
        Sub(vfMulti, "x").value = 6;
        Frames();
        Put("multi.ui_edit_same_x", FmtVecField(vfMulti) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed Y component to target[0]'s existing value (100).
        Sub(vfMulti, "y").value = 100;
        Frames();
        Put("multi.ui_edit_mixed_y_to_t0", FmtVecField(vfMulti) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed Y component to a new value (300) -> resolves mixed state across both targets.
        Sub(vfMulti, "y").value = 300;
        Frames();
        Put("multi.ui_edit_mixed_y_new", FmtVecField(vfMulti) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Reset to mixed X (1 vs 2) and non-mixed Y (10 vs 10), test whole-vector edit changing only Y.
        mA.multiVec = new Vector2Int(1, 10);
        mB.multiVec = new Vector2Int(2, 10);
        soMulti.Update();
        Frames();
        Put("multi.reset_partial_mixed", FmtVecField(vfMulti));

        vfMulti.value = new Vector2Int(1, 25);
        Frames();
        Put("multi.ui_whole_only_y", FmtVecField(vfMulti) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec));

        // Whole-vector write changing both axes resolves mixed X too.
        vfMulti.value = new Vector2Int(7, 8);
        Frames();
        Put("multi.ui_whole_all", FmtVecField(vfMulti) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec));

        // Direct IntegerField bound to "multiVec.x" on mixed targets -> only touches X.
        mA.multiVec = new Vector2Int(10, 20);
        mB.multiVec = new Vector2Int(40, 50);
        soMulti.Update();
        Frames();
        var ifCompX = new IntegerField();
        m_Root.Add(ifCompX);
        ifCompX.BindProperty(soMulti.FindProperty("multiVec.x"));
        Frames();
        Put("multi.comp_x_field.initial", FmtSub(ifCompX));
        ifCompX.value = 77;
        Frames();
        Put("multi.comp_x_field.ui_edit", FmtSub(ifCompX) + "|a=" + FmtVec2Int(mA.multiVec) + "|b=" + FmtVec2Int(mB.multiVec));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<Vector2IntTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var tvMain = new Vector2IntField { bindingPath = "myGridPos" };
        var tvDotted = new Vector2IntField { bindingPath = "nested.deep.cell" };
        var tvArray = new Vector2IntField { bindingPath = "vecArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var tvRelOffset = new Vector2IntField { bindingPath = "offset" };
        var tiRelOffsetY = new IntegerField { bindingPath = "offset.y" };
        nestedGroup.Add(tvRelOffset);
        nestedGroup.Add(tiRelOffsetY);
        var tVec2 = new Vector2Field { bindingPath = "siblingVec2" };
        var tVec3 = new Vector3Field { bindingPath = "siblingVec3" };
        var tVec4 = new Vector4Field { bindingPath = "siblingVec4" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(tvMain);
        treeRoot.Add(tvDotted);
        treeRoot.Add(tvArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tVec2);
        treeRoot.Add(tVec3);
        treeRoot.Add(tVec4);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtVec2Int(tvMain.value) + "|" + FmtVec2Int(tvDotted.value) + "|" + FmtVec2Int(tvArray.value) + "|" +
            FmtVec2Int(tvRelOffset.value) + "|" + FmtInt(tiRelOffsetY.value) + "|" +
            FmtVec2(tVec2.value) + "|" + FmtVec3(tVec3.value) + "|" + FmtVec4(tVec4.value) + "|" +
            FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        Sub(tvDotted, "y").value = 123;
        Sub(tvArray, "x").value = -95;
        tiRelOffsetY.value = 55;
        tVec2.Q<FloatField>("unity-x-input").value = 3.25f;
        tVec3.Q<FloatField>("unity-z-input").value = 77f;
        tVec4.Q<FloatField>("unity-w-input").value = 88f;
        tInt.value = 99;
        tPlain.value = Vec2IntControlPlainEnum.Alpha;
        tFlags.value = Vec2IntControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtVec2Int(tTree.nested.deep.cell) + "|" + FmtVec2Int(tTree.vecArray[1]) + "|" +
            FmtVec2Int(tTree.nested.offset) + "|" + FmtVec2Int(tvRelOffset.value) + "|" +
            FmtVec2(tTree.siblingVec2) + "|" + FmtVec3(tTree.siblingVec3) + "|" + FmtVec4(tTree.siblingVec4) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tTree.flagsEnum);

        soTree.FindProperty("nested.offset").vector2IntValue = new Vector2Int(1, 2);
        soTree.FindProperty("nested.deep.cell.x").intValue = 88;
        soTree.FindProperty("vecArray.Array.data[1].y").intValue = -44;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtVec2Int(tvRelOffset.value) + "|" + FmtInt(tiRelOffsetY.value) + "|" +
            FmtVec2Int(tvDotted.value) + "|" + FmtVec2Int(tvArray.value));

        // 4. PropertyField for Vector2Int (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<Vector2IntTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myGridPos"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.Vector2Int)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtVec2Int(e.changedProperty.vector2IntValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<Vector2IntField>();
        Sub(pfChild, "y").value = 42;
        Frames();
        Put("pf.auto.ui_sub_y", DescribePf(pfAuto) + "|t=" + FmtVec2Int(tPf.myGridPos) + "|evts=" + FmtInt(pfEvents.Count));

        pfChild.value = new Vector2Int(-1, -2);
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtVec2Int(tPf.myGridPos) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myGridPos.x").intValue = 64;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_x", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom Grid";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.offset"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec2"));
        Frames();
        Put("pf.rebind_vec2", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec4"));
        Frames();
        Put("pf.rebind_vec4", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("scalarInt"));
        Frames();
        Put("pf.rebind_scalar", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("otherVec"));
        Frames();
        Put("pf.rebind_back_vec2int", DescribePf(pfAuto));

        // PropertyField with mixed multi-object Vector2Int.
        var pmA = ScriptableObject.CreateInstance<Vector2IntTarget>();
        var pmB = ScriptableObject.CreateInstance<Vector2IntTarget>();
        pmA.multiVec = new Vector2Int(3, 300);
        pmB.multiVec = new Vector2Int(4, 400);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiVec"), "Multi Vec2Int");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtVec2Int(e.changedProperty.vector2IntValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiVf = pfMulti.Q<Vector2IntField>();
        Sub(pfMultiVf, "x").value = 9;
        Frames();
        Put("pf.multi.ui_edit_x", DescribePf(pfMulti) + "|a=" + FmtVec2Int(pmA.multiVec) + "|b=" + FmtVec2Int(pmB.multiVec) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<Vector2IntTarget>();
        var tRe2 = ScriptableObject.CreateInstance<Vector2IntTarget>();
        tRe1.myGridPos = new Vector2Int(1, 2);
        tRe1.otherVec = new Vector2Int(4, 5);
        tRe2.myGridPos = new Vector2Int(70, 80);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new Vec2IntRuntimeSource();
        var vfRe = new Vector2IntField();
        vfRe.dataSource = rtSource;
        vfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(Vec2IntRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(vfRe);
        vfRe.BindProperty(soRe1.FindProperty("myGridPos"));
        Frames();
        Put("unbind_rebind.initial", FmtVecField(vfRe) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfRe.Unbind();
        Frames();
        vfRe.value = new Vector2Int(99, 98);
        soRe1.FindProperty("myGridPos").vector2IntValue = new Vector2Int(11, 12);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-vec2int-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtVecField(vfRe) + "|t1=" + FmtVec2Int(tRe1.myGridPos) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtVecField(vfMulti));

        vfRe.BindProperty(soRe1.FindProperty("otherVec"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtVecField(vfRe));
        Sub(vfRe, "y").value = 66;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtVec2Int(tRe1.otherVec) + "|t1_start=" + FmtVec2Int(tRe1.myGridPos));

        vfRe.BindProperty(soRe2.FindProperty("myGridPos"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtVecField(vfRe));
        Sub(vfRe, "x").value = 71;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtVec2Int(tRe2.myGridPos) + "|t1_other=" + FmtVec2Int(tRe1.otherVec));

        var pfRe = new PropertyField(soRe1.FindProperty("myGridPos"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myGridPos").vector2IntValue = new Vector2Int(21, 22);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Vector2Int property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new FloatField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myGridPos"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.float_field", FmtFloat(badField.value));
#endif
    }
}
