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

public enum RectIntControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum RectIntControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepRectIntData
{
    public RectInt tileBounds;
}

[Serializable]
public struct NestedRectIntData
{
    public RectInt frame;
    public DeepRectIntData deep;
}

public class RectIntTarget : ScriptableObject
{
    public RectInt zeroRectInt = new RectInt(0, 0, 0, 0);
    public RectInt myTileRect = new RectInt(2, -3, 100, 50);
    public RectInt otherRectInt = new RectInt(10, 20, 30, 40);
    public RectInt negativeRectInt = new RectInt(-10, -20, -30, -40);
    public RectInt extremeRectInt = new RectInt(int.MinValue, int.MaxValue, -1, 1000000);
    public RectInt multiRectInt = new RectInt(1, 10, 100, 1000);
    public NestedRectIntData nested = new NestedRectIntData
    {
        frame = new RectInt(4, 5, 60, 70),
        deep = new DeepRectIntData { tileBounds = new RectInt(-7, 8, 12, 24) }
    };
    public RectInt[] rectIntArray = new[]
    {
        new RectInt(1, 2, 3, 4),
        new RectInt(5, 6, 70, 80)
    };
    public Rect siblingRect = new Rect(1.5f, -2.5f, 30f, 40f);
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3Int siblingVec3Int = new Vector3Int(3, -4, 5);
    public Vector2Int siblingVec2Int = new Vector2Int(7, -8);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public RectIntControlPlainEnum plainEnum = RectIntControlPlainEnum.Beta;
    public RectIntControlFlagsEnum flagsEnum = RectIntControlFlagsEnum.BitA | RectIntControlFlagsEnum.BitB;
}

public class RectIntRuntimeSource
{
    public string tip = "rt-rectint-tip-0";
}

public class RectIntBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RECTINT_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<RectIntBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("RECTINT_BINDING_OUT");
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
    static string FmtRect(Rect r) => "(" + FmtFloat(r.x) + "," + FmtFloat(r.y) + "," + FmtFloat(r.width) + "," + FmtFloat(r.height) + ")";
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";
    static string FmtVec2Int(Vector2Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + ")";
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";

    static IntegerField Sub(RectIntField f, string part) => f.Q<IntegerField>("unity-" + part + "-input");

    static string FmtSub(IntegerField f) =>
        f == null ? "null" : FmtInt(f.value) + ":" + (f.text ?? "null") + ":" + FmtBool(f.showMixedValue);

    static string FmtRectIntField(RectIntField f) =>
        FmtRectInt(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|x=" + FmtSub(Sub(f, "x")) +
        "|y=" + FmtSub(Sub(f, "y")) +
        "|w=" + FmtSub(Sub(f, "width")) +
        "|h=" + FmtSub(Sub(f, "height"));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is RectIntField rif)
        {
            bool hasLabelCls = rif.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = rif.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = rif.ClassListContains(BaseField<RectInt>.alignedFieldUssClassName);
            bool hasRectIntCls = rif.ClassListContains(RectIntField.ussClassName);
            bool hasTwoLines = rif.ClassListContains(BaseCompositeField<RectInt, IntegerField, int>.twoLinesVariantUssClassName);
            var xf = Sub(rif, "x");
            bool subAligned = xf != null && xf.ClassListContains(BaseField<int>.alignedFieldUssClassName);
            return "RectIntField|" + rif.name + "|" + (rif.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasRectIntCls) + FmtBool(hasTwoLines) + FmtBool(subAligned) + "|" +
                   FmtRectIntField(rif);
        }
        if (child is RectField rf)
            return "RectField|" + rf.name + "|" + (rf.label ?? "null") + "|" + FmtRect(rf.value);
        if (child is ColorField cf)
            return "ColorField|" + cf.name + "|" + (cf.label ?? "null") + "|" + FmtColor(cf.value);
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

        // 1. Direct RectIntField BindProperty: metadata, zero/negative/extreme/non-zero sync, two-way x/y/width/height, detached.
        var t1 = ScriptableObject.CreateInstance<RectIntTarget>();
        var so1 = new SerializedObject(t1);

        var pMeta = so1.FindProperty("myTileRect");
        Put("prop.meta", FmtBool(pMeta.hasVisibleChildren) + "|" + pMeta.type);

        var rifZero = new RectIntField();
        var zeroEvts = new List<string>();
        var zeroSubEvts = new List<string>();
        rifZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtRectInt(e.previousValue) + "->" + FmtRectInt(e.newValue)));
        Sub(rifZero, "x").RegisterValueChangedCallback(e => zeroSubEvts.Add("x:" + FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(rifZero);
        rifZero.BindProperty(so1.FindProperty("zeroRectInt"));
        Frames();
        Put("direct.zero.initial", FmtRectIntField(rifZero) + "|e=" + string.Join(";", zeroEvts) + "|se=" + string.Join(";", zeroSubEvts));

        rifZero.value = new RectInt(0, 0, 0, 0);
        Sub(rifZero, "x").value = 0;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtRectIntField(rifZero) + "|e=" + FmtInt(zeroEvts.Count) + "|se=" + FmtInt(zeroSubEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var rifNeg = new RectIntField();
        m_Root.Add(rifNeg);
        rifNeg.BindProperty(so1.FindProperty("negativeRectInt"));
        Frames();
        Put("direct.negative.initial", FmtRectIntField(rifNeg));
        Sub(rifNeg, "width").value = -55;
        Frames();
        Put("direct.negative.ui_w", FmtRectIntField(rifNeg) + "|t=" + FmtRectInt(t1.negativeRectInt));

        var rifExtreme = new RectIntField();
        m_Root.Add(rifExtreme);
        rifExtreme.BindProperty(so1.FindProperty("extremeRectInt"));
        Frames();
        Put("direct.extreme.initial", FmtRectIntField(rifExtreme));
        Sub(rifExtreme, "height").value = int.MinValue;
        Frames();
        Put("direct.extreme.ui_h", FmtRectIntField(rifExtreme) + "|t=" + FmtRectInt(t1.extremeRectInt));

        var rifMain = new RectIntField();
        var mainEvts = new List<string>();
        rifMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtRectInt(e.previousValue) + "->" + FmtRectInt(e.newValue)));
        m_Root.Add(rifMain);
        rifMain.BindProperty(so1.FindProperty("myTileRect"));
        Frames();
        Put("direct.main.initial", FmtRectIntField(rifMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO whole RectInt.
        rifMain.value = new RectInt(10, 20, 30, 40);
        Frames();
        Put("direct.main.ui_whole", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect) + "|p=" + FmtRectInt(so1.FindProperty("myTileRect").rectIntValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op write.
        int countBeforeNoop = mainEvts.Count;
        rifMain.value = new RectInt(10, 20, 30, 40);
        Sub(rifMain, "height").value = 40;
        Frames();
        Put("direct.main.noop", FmtRectInt(t1.myTileRect) + "|ec_delta=" + FmtInt(mainEvts.Count - countBeforeNoop));

        // SO -> UI whole RectInt.
        so1.FindProperty("myTileRect").rectIntValue = new RectInt(-4, 5, 64, 32);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation picked up on panel update.
        t1.myTileRect = new RectInt(7, -8, 90, 120);
        Frames();
        Put("direct.main.external_target", FmtRectIntField(rifMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO per-component edits via sub IntegerFields (x, y, width, height).
        Sub(rifMain, "x").value = 11;
        Frames();
        Put("direct.main.ui_x", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(rifMain, "y").value = -22;
        Frames();
        Put("direct.main.ui_y", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(rifMain, "width").value = 333;
        Frames();
        Put("direct.main.ui_w", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(rifMain, "height").value = 444;
        Frames();
        Put("direct.main.ui_h", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI per-component edits via FindPropertyRelative (x, y, width, height).
        so1.FindProperty("myTileRect").FindPropertyRelative("x").intValue = 101;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_x", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect));

        so1.FindProperty("myTileRect").FindPropertyRelative("y").intValue = -202;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_y", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect));

        so1.FindProperty("myTileRect").FindPropertyRelative("width").intValue = 303;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_w", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect));

        so1.FindProperty("myTileRect").FindPropertyRelative("height").intValue = 404;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_h", FmtRectIntField(rifMain) + "|t=" + FmtRectInt(t1.myTileRect));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var rifDetached = new RectIntField { bindingPath = "otherRectInt" };
        var retProp = rifDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtRectIntField(rifDetached));
        m_Root.Add(rifDetached);
        Frames();
        Put("direct.detached.after_attach", FmtRectIntField(rifDetached));

        // 2. Multi-object mixed values on RectIntField (per-component showMixedValue and edit behavior).
        var mA = ScriptableObject.CreateInstance<RectIntTarget>();
        var mB = ScriptableObject.CreateInstance<RectIntTarget>();
        mA.multiRectInt = new RectInt(1, 10, 100, 1000);
        mB.multiRectInt = new RectInt(2, 20, 200, 2000);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var rifMulti = new RectIntField();
        var multiEvts = new List<string>();
        rifMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtRectInt(e.previousValue) + "->" + FmtRectInt(e.newValue)));
        m_Root.Add(rifMulti);
        rifMulti.BindProperty(soMulti.FindProperty("multiRectInt"));
        Frames();
        Put("multi.initial", FmtRectIntField(rifMulti) + "|a=" + FmtRectInt(mA.multiRectInt) + "|b=" + FmtRectInt(mB.multiRectInt) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed X via sub-field on divergent targets.
        Sub(rifMulti, "x").value = 5;
        Frames();
        Put("multi.ui_edit_mixed_x", FmtRectIntField(rifMulti) + "|a=" + FmtRectInt(mA.multiRectInt) + "|b=" + FmtRectInt(mB.multiRectInt) + "|ec=" + FmtInt(multiEvts.Count));

        // Reset to partial mixed: X and W mixed, Y and H equal.
        mA.multiRectInt = new RectInt(1, 10, 100, 50);
        mB.multiRectInt = new RectInt(2, 10, 200, 50);
        soMulti.Update();
        Frames();
        Put("multi.reset_partial_mixed", FmtRectIntField(rifMulti));

        // Edit equal Y via sub-field while X and W are mixed.
        Sub(rifMulti, "y").value = 25;
        Frames();
        Put("multi.ui_edit_same_y", FmtRectIntField(rifMulti) + "|a=" + FmtRectInt(mA.multiRectInt) + "|b=" + FmtRectInt(mB.multiRectInt));

        // Reset to all-mixed and write target[0]'s existing width via sub-field, then a new width.
        mA.multiRectInt = new RectInt(1, 10, 100, 1000);
        mB.multiRectInt = new RectInt(2, 20, 200, 2000);
        soMulti.Update();
        Frames();
        Sub(rifMulti, "width").value = 100;
        Frames();
        Put("multi.ui_edit_mixed_w_to_t0", FmtRectIntField(rifMulti) + "|a=" + FmtRectInt(mA.multiRectInt) + "|b=" + FmtRectInt(mB.multiRectInt));

        Sub(rifMulti, "width").value = 300;
        Frames();
        Put("multi.ui_edit_mixed_w_new", FmtRectIntField(rifMulti) + "|a=" + FmtRectInt(mA.multiRectInt) + "|b=" + FmtRectInt(mB.multiRectInt));

        // Reset to partial mixed and test whole-RectInt write changing only height.
        mA.multiRectInt = new RectInt(1, 10, 100, 50);
        mB.multiRectInt = new RectInt(2, 10, 200, 50);
        soMulti.Update();
        Frames();
        rifMulti.value = new RectInt(1, 10, 100, 75);
        Frames();
        Put("multi.ui_whole_only_h", FmtRectIntField(rifMulti) + "|a=" + FmtRectInt(mA.multiRectInt) + "|b=" + FmtRectInt(mB.multiRectInt));

        // Direct IntegerField bound to "multiRectInt.width" on mixed targets -> only touches width.
        mA.multiRectInt = new RectInt(10, 20, 30, 40);
        mB.multiRectInt = new RectInt(50, 60, 70, 80);
        soMulti.Update();
        Frames();
        var ifCompW = new IntegerField();
        m_Root.Add(ifCompW);
        ifCompW.BindProperty(soMulti.FindProperty("multiRectInt.width"));
        Frames();
        Put("multi.comp_w_field.initial", FmtSub(ifCompW));
        ifCompW.value = 99;
        Frames();
        Put("multi.comp_w_field.ui_edit", FmtSub(ifCompW) + "|a=" + FmtRectInt(mA.multiRectInt) + "|b=" + FmtRectInt(mB.multiRectInt));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<RectIntTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trMain = new RectIntField { bindingPath = "myTileRect" };
        var trDotted = new RectIntField { bindingPath = "nested.deep.tileBounds" };
        var trArray = new RectIntField { bindingPath = "rectIntArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var trRelFrame = new RectIntField { bindingPath = "frame" };
        var tfRelFrameH = new IntegerField { bindingPath = "frame.height" };
        nestedGroup.Add(trRelFrame);
        nestedGroup.Add(tfRelFrameH);
        var tRect = new RectField { bindingPath = "siblingRect" };
        var tColor = new ColorField { bindingPath = "siblingColor" };
        var tVec3Int = new Vector3IntField { bindingPath = "siblingVec3Int" };
        var tVec2Int = new Vector2IntField { bindingPath = "siblingVec2Int" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(trMain);
        treeRoot.Add(trDotted);
        treeRoot.Add(trArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tRect);
        treeRoot.Add(tColor);
        treeRoot.Add(tVec3Int);
        treeRoot.Add(tVec2Int);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtRectInt(trMain.value) + "|" + FmtRectInt(trDotted.value) + "|" + FmtRectInt(trArray.value) + "|" +
            FmtRectInt(trRelFrame.value) + "|" + FmtInt(tfRelFrameH.value) + "|" +
            FmtRect(tRect.value) + "|" + FmtColor(tColor.value) + "|" + FmtVec3Int(tVec3Int.value) + "|" +
            FmtVec2Int(tVec2Int.value) + "|" + FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        Sub(trDotted, "height").value = 123;
        Sub(trArray, "x").value = -9;
        tfRelFrameH.value = 55;
        tRect.Q<FloatField>("unity-x-input").value = 9.5f;
        tColor.value = new Color(1f, 0.5f, 0.25f, 0.5f);
        tVec3Int.Q<IntegerField>("unity-z-input").value = 77;
        tVec2Int.Q<IntegerField>("unity-x-input").value = 33;
        tInt.value = 99;
        tPlain.value = RectIntControlPlainEnum.Alpha;
        tFlags.value = RectIntControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtRectInt(tTree.nested.deep.tileBounds) + "|" + FmtRectInt(tTree.rectIntArray[1]) + "|" +
            FmtRectInt(tTree.nested.frame) + "|" + FmtRectInt(trRelFrame.value) + "|" +
            FmtRect(tTree.siblingRect) + "|" + FmtColor(tTree.siblingColor) + "|" +
            FmtVec3Int(tTree.siblingVec3Int) + "|" + FmtVec2Int(tTree.siblingVec2Int) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tFlags.value);

        soTree.FindProperty("nested.frame").rectIntValue = new RectInt(1, 2, 3, 4);
        soTree.FindProperty("nested.deep.tileBounds.x").intValue = 88;
        soTree.FindProperty("rectIntArray.Array.data[1].width").intValue = 44;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtRectInt(trRelFrame.value) + "|" + FmtInt(tfRelFrameH.value) + "|" +
            FmtRectInt(trDotted.value) + "|" + FmtRectInt(trArray.value));

        // 4. PropertyField for RectInt (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<RectIntTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myTileRect"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.RectInt)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtRectInt(e.changedProperty.rectIntValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<RectIntField>();
        Sub(pfChild, "height").value = 42;
        Frames();
        Put("pf.auto.ui_sub_h", DescribePf(pfAuto) + "|t=" + FmtRectInt(tPf.myTileRect) + "|evts=" + FmtInt(pfEvents.Count));

        pfChild.value = new RectInt(-1, -2, 30, 40);
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtRectInt(tPf.myTileRect) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myTileRect.width").intValue = 64;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_w", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom RectInt";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.frame"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

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

        pfAuto.BindProperty(soPf.FindProperty("otherRectInt"));
        Frames();
        Put("pf.rebind_back_rectint", DescribePf(pfAuto));

        // PropertyField with mixed multi-object RectInt.
        var pmA = ScriptableObject.CreateInstance<RectIntTarget>();
        var pmB = ScriptableObject.CreateInstance<RectIntTarget>();
        pmA.multiRectInt = new RectInt(3, 30, 300, 3000);
        pmB.multiRectInt = new RectInt(4, 40, 400, 4000);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiRectInt"), "Multi RectInt");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtRectInt(e.changedProperty.rectIntValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiRif = pfMulti.Q<RectIntField>();
        Sub(pfMultiRif, "x").value = 9;
        Frames();
        Put("pf.multi.ui_edit_x", DescribePf(pfMulti) + "|a=" + FmtRectInt(pmA.multiRectInt) + "|b=" + FmtRectInt(pmB.multiRectInt) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<RectIntTarget>();
        var tRe2 = ScriptableObject.CreateInstance<RectIntTarget>();
        tRe1.myTileRect = new RectInt(1, 2, 3, 4);
        tRe1.otherRectInt = new RectInt(5, 6, 7, 8);
        tRe2.myTileRect = new RectInt(70, 80, 90, 100);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new RectIntRuntimeSource();
        var rifRe = new RectIntField();
        rifRe.dataSource = rtSource;
        rifRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(RectIntRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(rifRe);
        rifRe.BindProperty(soRe1.FindProperty("myTileRect"));
        Frames();
        Put("unbind_rebind.initial", FmtRectIntField(rifRe) + "|tip=" + (rifRe.tooltip ?? "null"));

        rifRe.Unbind();
        Frames();
        rifRe.value = new RectInt(99, 98, 97, 96);
        soRe1.FindProperty("myTileRect").rectIntValue = new RectInt(11, 12, 13, 14);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-rectint-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtRectIntField(rifRe) + "|t1=" + FmtRectInt(tRe1.myTileRect) + "|tip=" + (rifRe.tooltip ?? "null"));

        rifMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtRectIntField(rifMulti));

        rifRe.BindProperty(soRe1.FindProperty("otherRectInt"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtRectIntField(rifRe));
        Sub(rifRe, "height").value = 66;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtRectInt(tRe1.otherRectInt) + "|t1_start=" + FmtRectInt(tRe1.myTileRect));

        rifRe.BindProperty(soRe2.FindProperty("myTileRect"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtRectIntField(rifRe));
        Sub(rifRe, "x").value = 71;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtRectInt(tRe2.myTileRect) + "|t1_other=" + FmtRectInt(tRe1.otherRectInt));

        var pfRe = new PropertyField(soRe1.FindProperty("myTileRect"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myTileRect").rectIntValue = new RectInt(21, 22, 23, 24);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to RectInt property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new FloatField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myTileRect"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.float_field", FmtFloat(badField.value));
#endif
    }
}
