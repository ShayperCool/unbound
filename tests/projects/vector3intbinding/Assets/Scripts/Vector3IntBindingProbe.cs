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

public enum Vec3IntControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum Vec3IntControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepVec3IntData
{
    public Vector3Int voxel;
}

[Serializable]
public struct NestedVec3IntData
{
    public Vector3Int offset;
    public DeepVec3IntData deep;
}

public class Vector3IntTarget : ScriptableObject
{
    public Vector3Int zeroVec = Vector3Int.zero;
    public Vector3Int myVoxelCoord = new Vector3Int(15, -24, 37);
    public Vector3Int otherVec = new Vector3Int(10, 20, 30);
    public Vector3Int extremeVec = new Vector3Int(int.MinValue, 0, int.MaxValue);
    public Vector3Int multiVec = new Vector3Int(1, 100, -10);
    public NestedVec3IntData nested = new NestedVec3IntData
    {
        offset = new Vector3Int(4, 5, 6),
        deep = new DeepVec3IntData { voxel = new Vector3Int(-7, 85, -91) }
    };
    public Vector3Int[] vecArray = new[]
    {
        new Vector3Int(1, 2, 3),
        new Vector3Int(30, -40, 50)
    };
    public Vector2Int siblingVec2Int = new Vector2Int(-3, 9);
    public Vector2 siblingVec2 = new Vector2(1.5f, 2.5f);
    public Vector3 siblingVec3 = new Vector3(9f, 8f, 7f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public Vec3IntControlPlainEnum plainEnum = Vec3IntControlPlainEnum.Beta;
    public Vec3IntControlFlagsEnum flagsEnum = Vec3IntControlFlagsEnum.BitA | Vec3IntControlFlagsEnum.BitB;
}

public class Vec3IntRuntimeSource
{
    public string tip = "rt-vec3int-tip-0";
}

public class Vector3IntBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VECTOR3INT_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<Vector3IntBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("VECTOR3INT_BINDING_OUT");
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
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";
    static string FmtVec2(Vector2 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + ")";
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtVec4(Vector4 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + "," + FmtFloat(v.w) + ")";

    static IntegerField Sub(Vector3IntField f, string axis) => f.Q<IntegerField>("unity-" + axis + "-input");

    static string FmtSub(IntegerField inf) =>
        inf == null ? "null" : FmtInt(inf.value) + ":" + (inf.text ?? "null") + ":" + FmtBool(inf.showMixedValue);

    static string FmtVecField(Vector3IntField f) =>
        FmtVec3Int(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|x=" + FmtSub(Sub(f, "x")) +
        "|y=" + FmtSub(Sub(f, "y")) +
        "|z=" + FmtSub(Sub(f, "z"));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is Vector3IntField vf)
        {
            bool hasLabelCls = vf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = vf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = vf.ClassListContains(BaseField<Vector3Int>.alignedFieldUssClassName);
            bool hasVecCls = vf.ClassListContains(Vector3IntField.ussClassName);
            var xf = Sub(vf, "x");
            bool subAligned = xf != null && xf.ClassListContains(BaseField<int>.alignedFieldUssClassName);
            return "Vector3IntField|" + vf.name + "|" + (vf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasVecCls) + FmtBool(subAligned) + "|" +
                   FmtVecField(vf);
        }
        if (child is Vector2IntField v2i)
            return "Vector2IntField|" + v2i.name + "|" + (v2i.label ?? "null") + "|" + FmtVec2Int(v2i.value);
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

        // 1. Initial sync (zero default, non-zero, extreme ints), no-op writes, detached-then-attached.
        var t1 = ScriptableObject.CreateInstance<Vector3IntTarget>();
        var so1 = new SerializedObject(t1);

        var vfZero = new Vector3IntField();
        var zeroEvts = new List<string>();
        var zeroSubEvts = new List<string>();
        vfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtVec3Int(e.previousValue) + "->" + FmtVec3Int(e.newValue)));
        Sub(vfZero, "x").RegisterValueChangedCallback(e => zeroSubEvts.Add("x:" + FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(vfZero);
        vfZero.BindProperty(so1.FindProperty("zeroVec"));
        Frames();
        Put("direct.zero.initial", FmtVecField(vfZero) + "|e=" + string.Join(";", zeroEvts) + "|se=" + string.Join(";", zeroSubEvts));

        vfZero.value = Vector3Int.zero;
        Sub(vfZero, "x").value = 0;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtVecField(vfZero) + "|e=" + FmtInt(zeroEvts.Count) + "|se=" + FmtInt(zeroSubEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var vfExtreme = new Vector3IntField();
        m_Root.Add(vfExtreme);
        vfExtreme.BindProperty(so1.FindProperty("extremeVec"));
        Frames();
        Put("direct.extreme.initial", FmtVecField(vfExtreme));
        Sub(vfExtreme, "y").value = int.MinValue;
        Frames();
        Put("direct.extreme.ui_y", FmtVecField(vfExtreme) + "|t=" + FmtVec3Int(t1.extremeVec));

        var vfMain = new Vector3IntField();
        var mainEvts = new List<string>();
        vfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtVec3Int(e.previousValue) + "->" + FmtVec3Int(e.newValue)));
        m_Root.Add(vfMain);
        vfMain.BindProperty(so1.FindProperty("myVoxelCoord"));
        Frames();
        Put("direct.main.initial", FmtVecField(vfMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO whole vector.
        vfMain.value = new Vector3Int(10, 20, 30);
        Frames();
        Put("direct.main.ui_whole", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord) + "|p=" + FmtVec3Int(so1.FindProperty("myVoxelCoord").vector3IntValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op write with same non-zero vector and same sub-field value.
        int countBeforeNoop = mainEvts.Count;
        vfMain.value = new Vector3Int(10, 20, 30);
        Sub(vfMain, "z").value = 30;
        Frames();
        Put("direct.main.noop", FmtVec3Int(t1.myVoxelCoord) + "|ec_delta=" + FmtInt(mainEvts.Count - countBeforeNoop));

        // SO -> UI whole vector.
        so1.FindProperty("myVoxelCoord").vector3IntValue = new Vector3Int(-4, 55, -66);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation picked up on panel update.
        t1.myVoxelCoord = new Vector3Int(7, -8, 9);
        Frames();
        Put("direct.main.external_target", FmtVecField(vfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO per-component edits via sub IntegerFields (x, y, z).
        Sub(vfMain, "x").value = 115;
        Frames();
        Put("direct.main.ui_x", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(vfMain, "y").value = -222;
        Frames();
        Put("direct.main.ui_y", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(vfMain, "z").value = 333;
        Frames();
        Put("direct.main.ui_z", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI per-component edits via FindPropertyRelative (x, y, z).
        so1.FindProperty("myVoxelCoord").FindPropertyRelative("x").intValue = 101;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_x", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord));

        so1.FindProperty("myVoxelCoord").FindPropertyRelative("y").intValue = -202;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_y", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord));

        so1.FindProperty("myVoxelCoord").FindPropertyRelative("z").intValue = 303;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_z", FmtVecField(vfMain) + "|t=" + FmtVec3Int(t1.myVoxelCoord));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var vfDetached = new Vector3IntField { bindingPath = "otherVec" };
        var retProp = vfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtVecField(vfDetached));
        m_Root.Add(vfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtVecField(vfDetached));

        // 2. Mixed multi-object editing (per-component preservation and showMixedValue across x/y/z).
        var mA = ScriptableObject.CreateInstance<Vector3IntTarget>();
        var mB = ScriptableObject.CreateInstance<Vector3IntTarget>();
        mA.multiVec = new Vector3Int(1, 100, -10);
        mB.multiVec = new Vector3Int(2, 200, -20);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var vfMulti = new Vector3IntField();
        var multiEvts = new List<string>();
        vfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtVec3Int(e.previousValue) + "->" + FmtVec3Int(e.newValue)));
        m_Root.Add(vfMulti);
        vfMulti.BindProperty(soMulti.FindProperty("multiVec"));
        Frames();
        Put("multi.initial", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed X via sub-field -> updates X on both targets while preserving A.(y,z) and B.(y,z).
        Sub(vfMulti, "x").value = 5;
        Frames();
        Put("multi.ui_edit_mixed_x", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit now non-mixed X while Y and Z are still mixed -> must preserve Y and Z on both targets.
        Sub(vfMulti, "x").value = 6;
        Frames();
        Put("multi.ui_edit_same_x", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed Z via sub-field -> updates Z on both targets while preserving A.y=100, B.y=200.
        Sub(vfMulti, "z").value = -99;
        Frames();
        Put("multi.ui_edit_mixed_z", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed Y component to target[0]'s existing value (100).
        Sub(vfMulti, "y").value = 100;
        Frames();
        Put("multi.ui_edit_mixed_y_to_t0", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed Y component to a new value (300) -> resolves mixed state across all axes.
        Sub(vfMulti, "y").value = 300;
        Frames();
        Put("multi.ui_edit_mixed_y_new", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec) + "|ec=" + FmtInt(multiEvts.Count));

        // Reset to mixed X (1 vs 2), equal Y (10 vs 10), mixed Z (-5 vs -6); whole-vector write changing only Y preserves both X and Z.
        mA.multiVec = new Vector3Int(1, 10, -5);
        mB.multiVec = new Vector3Int(2, 10, -6);
        soMulti.Update();
        Frames();
        Put("multi.reset_partial_mixed", FmtVecField(vfMulti));

        vfMulti.value = new Vector3Int(1, 25, -5);
        Frames();
        Put("multi.ui_whole_only_y", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec));

        // Whole-vector write changing X and Z resolves all remaining mixed axes.
        vfMulti.value = new Vector3Int(7, 25, 9);
        Frames();
        Put("multi.ui_whole_all", FmtVecField(vfMulti) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec));

        // Direct IntegerField bound to "multiVec.y" on mixed targets -> only touches Y, preserves X and Z.
        mA.multiVec = new Vector3Int(10, 20, 30);
        mB.multiVec = new Vector3Int(40, 50, 60);
        soMulti.Update();
        Frames();
        var ifCompY = new IntegerField();
        m_Root.Add(ifCompY);
        ifCompY.BindProperty(soMulti.FindProperty("multiVec.y"));
        Frames();
        Put("multi.comp_y_field.initial", FmtSub(ifCompY));
        ifCompY.value = 77;
        Frames();
        Put("multi.comp_y_field.ui_edit", FmtSub(ifCompY) + "|a=" + FmtVec3Int(mA.multiVec) + "|b=" + FmtVec3Int(mB.multiVec));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<Vector3IntTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var tvMain = new Vector3IntField { bindingPath = "myVoxelCoord" };
        var tvDotted = new Vector3IntField { bindingPath = "nested.deep.voxel" };
        var tvArray = new Vector3IntField { bindingPath = "vecArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var tvRelOffset = new Vector3IntField { bindingPath = "offset" };
        var tiRelOffsetZ = new IntegerField { bindingPath = "offset.z" };
        nestedGroup.Add(tvRelOffset);
        nestedGroup.Add(tiRelOffsetZ);
        var tVec2Int = new Vector2IntField { bindingPath = "siblingVec2Int" };
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
        treeRoot.Add(tVec2Int);
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
            FmtVec3Int(tvMain.value) + "|" + FmtVec3Int(tvDotted.value) + "|" + FmtVec3Int(tvArray.value) + "|" +
            FmtVec3Int(tvRelOffset.value) + "|" + FmtInt(tiRelOffsetZ.value) + "|" +
            FmtVec2Int(tVec2Int.value) + "|" + FmtVec2(tVec2.value) + "|" + FmtVec3(tVec3.value) + "|" + FmtVec4(tVec4.value) + "|" +
            FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        Sub(tvDotted, "z").value = 123;
        Sub(tvArray, "x").value = -95;
        tiRelOffsetZ.value = 55;
        tVec2Int.Q<IntegerField>("unity-y-input").value = -17;
        tVec2.Q<FloatField>("unity-x-input").value = 3.25f;
        tVec3.Q<FloatField>("unity-z-input").value = 77f;
        tVec4.Q<FloatField>("unity-w-input").value = 88f;
        tInt.value = 99;
        tPlain.value = Vec3IntControlPlainEnum.Alpha;
        tFlags.value = Vec3IntControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtVec3Int(tTree.nested.deep.voxel) + "|" + FmtVec3Int(tTree.vecArray[1]) + "|" +
            FmtVec3Int(tTree.nested.offset) + "|" + FmtVec3Int(tvRelOffset.value) + "|" +
            FmtVec2Int(tTree.siblingVec2Int) + "|" + FmtVec2(tTree.siblingVec2) + "|" + FmtVec3(tTree.siblingVec3) + "|" + FmtVec4(tTree.siblingVec4) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tTree.flagsEnum);

        soTree.FindProperty("nested.offset").vector3IntValue = new Vector3Int(1, 2, 3);
        soTree.FindProperty("nested.deep.voxel.x").intValue = 88;
        soTree.FindProperty("vecArray.Array.data[1].y").intValue = -44;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtVec3Int(tvRelOffset.value) + "|" + FmtInt(tiRelOffsetZ.value) + "|" +
            FmtVec3Int(tvDotted.value) + "|" + FmtVec3Int(tvArray.value));

        // 4. PropertyField for Vector3Int (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<Vector3IntTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myVoxelCoord"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.Vector3Int)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtVec3Int(e.changedProperty.vector3IntValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<Vector3IntField>();
        Sub(pfChild, "z").value = 42;
        Frames();
        Put("pf.auto.ui_sub_z", DescribePf(pfAuto) + "|t=" + FmtVec3Int(tPf.myVoxelCoord) + "|evts=" + FmtInt(pfEvents.Count));

        pfChild.value = new Vector3Int(-1, -2, -3);
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtVec3Int(tPf.myVoxelCoord) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myVoxelCoord.y").intValue = 64;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_y", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom Voxel";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.offset"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec2Int"));
        Frames();
        Put("pf.rebind_vec2int", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec3"));
        Frames();
        Put("pf.rebind_vec3", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("siblingVec4"));
        Frames();
        Put("pf.rebind_vec4", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("scalarInt"));
        Frames();
        Put("pf.rebind_scalar", DescribePf(pfAuto));

        pfAuto.BindProperty(soPf.FindProperty("otherVec"));
        Frames();
        Put("pf.rebind_back_vec3int", DescribePf(pfAuto));

        // PropertyField with mixed multi-object Vector3Int -> editing Y preserves distinct X and Z.
        var pmA = ScriptableObject.CreateInstance<Vector3IntTarget>();
        var pmB = ScriptableObject.CreateInstance<Vector3IntTarget>();
        pmA.multiVec = new Vector3Int(3, 300, -30);
        pmB.multiVec = new Vector3Int(4, 400, -40);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiVec"), "Multi Vec3Int");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtVec3Int(e.changedProperty.vector3IntValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiVf = pfMulti.Q<Vector3IntField>();
        Sub(pfMultiVf, "y").value = 999;
        Frames();
        Put("pf.multi.ui_edit_y", DescribePf(pfMulti) + "|a=" + FmtVec3Int(pmA.multiVec) + "|b=" + FmtVec3Int(pmB.multiVec) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<Vector3IntTarget>();
        var tRe2 = ScriptableObject.CreateInstance<Vector3IntTarget>();
        tRe1.myVoxelCoord = new Vector3Int(1, 2, 3);
        tRe1.otherVec = new Vector3Int(4, 5, 6);
        tRe2.myVoxelCoord = new Vector3Int(70, 80, 90);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new Vec3IntRuntimeSource();
        var vfRe = new Vector3IntField();
        vfRe.dataSource = rtSource;
        vfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(Vec3IntRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(vfRe);
        vfRe.BindProperty(soRe1.FindProperty("myVoxelCoord"));
        Frames();
        Put("unbind_rebind.initial", FmtVecField(vfRe) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfRe.Unbind();
        Frames();
        vfRe.value = new Vector3Int(99, 98, 97);
        soRe1.FindProperty("myVoxelCoord").vector3IntValue = new Vector3Int(11, 12, 13);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-vec3int-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtVecField(vfRe) + "|t1=" + FmtVec3Int(tRe1.myVoxelCoord) + "|tip=" + (vfRe.tooltip ?? "null"));

        vfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtVecField(vfMulti));

        vfRe.BindProperty(soRe1.FindProperty("otherVec"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtVecField(vfRe));
        Sub(vfRe, "z").value = 66;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtVec3Int(tRe1.otherVec) + "|t1_start=" + FmtVec3Int(tRe1.myVoxelCoord));

        vfRe.BindProperty(soRe2.FindProperty("myVoxelCoord"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtVecField(vfRe));
        Sub(vfRe, "x").value = 71;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtVec3Int(tRe2.myVoxelCoord) + "|t1_other=" + FmtVec3Int(tRe1.otherVec));

        var pfRe = new PropertyField(soRe1.FindProperty("myVoxelCoord"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myVoxelCoord").vector3IntValue = new Vector3Int(21, 22, 23);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Vector3Int property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new FloatField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myVoxelCoord"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.float_field", FmtFloat(badField.value));
#endif
    }
}
