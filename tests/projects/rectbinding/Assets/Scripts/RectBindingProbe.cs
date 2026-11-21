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

public enum RectControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum RectControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepRectData
{
    public Rect bounds2d;
}

[Serializable]
public struct NestedRectData
{
    public Rect frame;
    public DeepRectData deep;
}

public class RectTarget : ScriptableObject
{
    public Rect zeroRect = Rect.zero;
    public Rect myViewport = new Rect(1.5f, -2.25f, 100f, 50.5f);
    public Rect otherRect = new Rect(10f, 20f, 30f, 40f);
    public Rect negativeRect = new Rect(-10f, -20f, -30f, -40f);
    public Rect multiRect = new Rect(1f, 10f, 100f, 1000f);
    public NestedRectData nested = new NestedRectData
    {
        frame = new Rect(4f, 5f, 60f, 70f),
        deep = new DeepRectData { bounds2d = new Rect(-7f, 8.5f, 12f, 24f) }
    };
    public Rect[] rectArray = new[]
    {
        new Rect(1f, 2f, 3f, 4f),
        new Rect(5f, 6f, 70f, 80f)
    };
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3Int siblingVec3Int = new Vector3Int(3, -4, 5);
    public Vector2 siblingVec2 = new Vector2(1.5f, 2.5f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int scalarInt = 42;
    public RectControlPlainEnum plainEnum = RectControlPlainEnum.Beta;
    public RectControlFlagsEnum flagsEnum = RectControlFlagsEnum.BitA | RectControlFlagsEnum.BitB;
}

public class RectRuntimeSource
{
    public string tip = "rt-rect-tip-0";
}

public class RectBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RECT_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<RectBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("RECT_BINDING_OUT");
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
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";
    static string FmtVec2(Vector2 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + ")";
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";

    static FloatField Sub(RectField f, string part) => f.Q<FloatField>("unity-" + part + "-input");

    static string FmtSub(FloatField ff) =>
        ff == null ? "null" : FmtFloat(ff.value) + ":" + (ff.text ?? "null") + ":" + FmtBool(ff.showMixedValue);

    static string FmtRectField(RectField f) =>
        FmtRect(f.value) + "|m=" + FmtBool(f.showMixedValue) +
        "|x=" + FmtSub(Sub(f, "x")) +
        "|y=" + FmtSub(Sub(f, "y")) +
        "|w=" + FmtSub(Sub(f, "width")) +
        "|h=" + FmtSub(Sub(f, "height"));

#if UNITY_EDITOR
    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is RectField rf)
        {
            bool hasLabelCls = rf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = rf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = rf.ClassListContains(BaseField<Rect>.alignedFieldUssClassName);
            bool hasRectCls = rf.ClassListContains(RectField.ussClassName);
            bool hasTwoLines = rf.ClassListContains(BaseCompositeField<Rect, FloatField, float>.twoLinesVariantUssClassName);
            var xf = Sub(rf, "x");
            bool subAligned = xf != null && xf.ClassListContains(BaseField<float>.alignedFieldUssClassName);
            return "RectField|" + rf.name + "|" + (rf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasRectCls) + FmtBool(hasTwoLines) + FmtBool(subAligned) + "|" +
                   FmtRectField(rf);
        }
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

        // 1. Direct RectField BindProperty: metadata, zero/non-zero/negative sync, two-way x/y/width/height, detached.
        var t1 = ScriptableObject.CreateInstance<RectTarget>();
        var so1 = new SerializedObject(t1);

        var pMeta = so1.FindProperty("myViewport");
        Put("prop.meta", FmtBool(pMeta.hasVisibleChildren) + "|" + pMeta.type);

        var rfZero = new RectField();
        var zeroEvts = new List<string>();
        var zeroSubEvts = new List<string>();
        rfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtRect(e.previousValue) + "->" + FmtRect(e.newValue)));
        Sub(rfZero, "x").RegisterValueChangedCallback(e => zeroSubEvts.Add("x:" + FmtFloat(e.previousValue) + "->" + FmtFloat(e.newValue)));
        m_Root.Add(rfZero);
        rfZero.BindProperty(so1.FindProperty("zeroRect"));
        Frames();
        Put("direct.zero.initial", FmtRectField(rfZero) + "|e=" + string.Join(";", zeroEvts) + "|se=" + string.Join(";", zeroSubEvts));

        rfZero.value = Rect.zero;
        Sub(rfZero, "x").value = 0f;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtRectField(rfZero) + "|e=" + FmtInt(zeroEvts.Count) + "|se=" + FmtInt(zeroSubEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var rfNeg = new RectField();
        m_Root.Add(rfNeg);
        rfNeg.BindProperty(so1.FindProperty("negativeRect"));
        Frames();
        Put("direct.negative.initial", FmtRectField(rfNeg));
        Sub(rfNeg, "width").value = -55.5f;
        Frames();
        Put("direct.negative.ui_w", FmtRectField(rfNeg) + "|t=" + FmtRect(t1.negativeRect));

        var rfMain = new RectField();
        var mainEvts = new List<string>();
        rfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtRect(e.previousValue) + "->" + FmtRect(e.newValue)));
        m_Root.Add(rfMain);
        rfMain.BindProperty(so1.FindProperty("myViewport"));
        Frames();
        Put("direct.main.initial", FmtRectField(rfMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO whole rect.
        rfMain.value = new Rect(10f, 20f, 30f, 40f);
        Frames();
        Put("direct.main.ui_whole", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport) + "|p=" + FmtRect(so1.FindProperty("myViewport").rectValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op write.
        int countBeforeNoop = mainEvts.Count;
        rfMain.value = new Rect(10f, 20f, 30f, 40f);
        Sub(rfMain, "height").value = 40f;
        Frames();
        Put("direct.main.noop", FmtRect(t1.myViewport) + "|ec_delta=" + FmtInt(mainEvts.Count - countBeforeNoop));

        // SO -> UI whole rect.
        so1.FindProperty("myViewport").rectValue = new Rect(-4f, 5.5f, 64f, 32f);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation picked up on panel update.
        t1.myViewport = new Rect(7f, -8f, 90f, 120f);
        Frames();
        Put("direct.main.external_target", FmtRectField(rfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // UI -> SO per-component edits via sub FloatFields (x, y, width, height).
        Sub(rfMain, "x").value = 11.5f;
        Frames();
        Put("direct.main.ui_x", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(rfMain, "y").value = -22.25f;
        Frames();
        Put("direct.main.ui_y", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(rfMain, "width").value = 333.5f;
        Frames();
        Put("direct.main.ui_w", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport) + "|ec=" + FmtInt(mainEvts.Count));

        Sub(rfMain, "height").value = 444.25f;
        Frames();
        Put("direct.main.ui_h", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI per-component edits via FindPropertyRelative (x, y, width, height).
        so1.FindProperty("myViewport").FindPropertyRelative("x").floatValue = 101f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_x", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport));

        so1.FindProperty("myViewport").FindPropertyRelative("y").floatValue = -202.5f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_y", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport));

        so1.FindProperty("myViewport").FindPropertyRelative("width").floatValue = 303f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_w", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport));

        so1.FindProperty("myViewport").FindPropertyRelative("height").floatValue = 404.5f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_h", FmtRectField(rfMain) + "|t=" + FmtRect(t1.myViewport));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var rfDetached = new RectField { bindingPath = "otherRect" };
        var retProp = rfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtRectField(rfDetached));
        m_Root.Add(rfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtRectField(rfDetached));

        // 2. Multi-object mixed values on RectField (per-component showMixedValue and edit behavior).
        var mA = ScriptableObject.CreateInstance<RectTarget>();
        var mB = ScriptableObject.CreateInstance<RectTarget>();
        mA.multiRect = new Rect(1f, 10f, 100f, 1000f);
        mB.multiRect = new Rect(2f, 20f, 200f, 2000f);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var rfMulti = new RectField();
        var multiEvts = new List<string>();
        rfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtRect(e.previousValue) + "->" + FmtRect(e.newValue)));
        m_Root.Add(rfMulti);
        rfMulti.BindProperty(soMulti.FindProperty("multiRect"));
        Frames();
        Put("multi.initial", FmtRectField(rfMulti) + "|a=" + FmtRect(mA.multiRect) + "|b=" + FmtRect(mB.multiRect) + "|ec=" + FmtInt(multiEvts.Count));

        // Edit mixed X via sub-field on divergent targets.
        Sub(rfMulti, "x").value = 5f;
        Frames();
        Put("multi.ui_edit_mixed_x", FmtRectField(rfMulti) + "|a=" + FmtRect(mA.multiRect) + "|b=" + FmtRect(mB.multiRect) + "|ec=" + FmtInt(multiEvts.Count));

        // Reset to partial mixed: X and W mixed, Y and H equal.
        mA.multiRect = new Rect(1f, 10f, 100f, 50f);
        mB.multiRect = new Rect(2f, 10f, 200f, 50f);
        soMulti.Update();
        Frames();
        Put("multi.reset_partial_mixed", FmtRectField(rfMulti));

        // Edit equal Y via sub-field while X and W are mixed.
        Sub(rfMulti, "y").value = 25f;
        Frames();
        Put("multi.ui_edit_same_y", FmtRectField(rfMulti) + "|a=" + FmtRect(mA.multiRect) + "|b=" + FmtRect(mB.multiRect));

        // Reset to all-mixed and write target[0]'s existing width via sub-field, then a new width.
        mA.multiRect = new Rect(1f, 10f, 100f, 1000f);
        mB.multiRect = new Rect(2f, 20f, 200f, 2000f);
        soMulti.Update();
        Frames();
        Sub(rfMulti, "width").value = 100f;
        Frames();
        Put("multi.ui_edit_mixed_w_to_t0", FmtRectField(rfMulti) + "|a=" + FmtRect(mA.multiRect) + "|b=" + FmtRect(mB.multiRect));

        Sub(rfMulti, "width").value = 300f;
        Frames();
        Put("multi.ui_edit_mixed_w_new", FmtRectField(rfMulti) + "|a=" + FmtRect(mA.multiRect) + "|b=" + FmtRect(mB.multiRect));

        // Reset to partial mixed and test whole-rect write changing only height.
        mA.multiRect = new Rect(1f, 10f, 100f, 50f);
        mB.multiRect = new Rect(2f, 10f, 200f, 50f);
        soMulti.Update();
        Frames();
        rfMulti.value = new Rect(1f, 10f, 100f, 75f);
        Frames();
        Put("multi.ui_whole_only_h", FmtRectField(rfMulti) + "|a=" + FmtRect(mA.multiRect) + "|b=" + FmtRect(mB.multiRect));

        // Direct FloatField bound to "multiRect.width" on mixed targets -> only touches width.
        mA.multiRect = new Rect(10f, 20f, 30f, 40f);
        mB.multiRect = new Rect(50f, 60f, 70f, 80f);
        soMulti.Update();
        Frames();
        var ffCompW = new FloatField();
        m_Root.Add(ffCompW);
        ffCompW.BindProperty(soMulti.FindProperty("multiRect.width"));
        Frames();
        Put("multi.comp_w_field.initial", FmtSub(ffCompW));
        ffCompW.value = 99f;
        Frames();
        Put("multi.comp_w_field.ui_edit", FmtSub(ffCompW) + "|a=" + FmtRect(mA.multiRect) + "|b=" + FmtRect(mB.multiRect));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<RectTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trMain = new RectField { bindingPath = "myViewport" };
        var trDotted = new RectField { bindingPath = "nested.deep.bounds2d" };
        var trArray = new RectField { bindingPath = "rectArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var trRelFrame = new RectField { bindingPath = "frame" };
        var tfRelFrameH = new FloatField { bindingPath = "frame.height" };
        nestedGroup.Add(trRelFrame);
        nestedGroup.Add(tfRelFrameH);
        var tColor = new ColorField { bindingPath = "siblingColor" };
        var tVec3Int = new Vector3IntField { bindingPath = "siblingVec3Int" };
        var tVec2 = new Vector2Field { bindingPath = "siblingVec2" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(trMain);
        treeRoot.Add(trDotted);
        treeRoot.Add(trArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tColor);
        treeRoot.Add(tVec3Int);
        treeRoot.Add(tVec2);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtRect(trMain.value) + "|" + FmtRect(trDotted.value) + "|" + FmtRect(trArray.value) + "|" +
            FmtRect(trRelFrame.value) + "|" + FmtFloat(tfRelFrameH.value) + "|" +
            FmtColor(tColor.value) + "|" + FmtVec3Int(tVec3Int.value) + "|" + FmtVec2(tVec2.value) + "|" +
            FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        Sub(trDotted, "height").value = 123.5f;
        Sub(trArray, "x").value = -9.5f;
        tfRelFrameH.value = 55.25f;
        tColor.value = new Color(1f, 0.5f, 0.25f, 0.5f);
        tVec3Int.Q<IntegerField>("unity-z-input").value = 77;
        tVec2.Q<FloatField>("unity-x-input").value = 3.25f;
        tInt.value = 99;
        tPlain.value = RectControlPlainEnum.Alpha;
        tFlags.value = RectControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtRect(tTree.nested.deep.bounds2d) + "|" + FmtRect(tTree.rectArray[1]) + "|" +
            FmtRect(tTree.nested.frame) + "|" + FmtRect(trRelFrame.value) + "|" +
            FmtColor(tTree.siblingColor) + "|" + FmtVec3Int(tTree.siblingVec3Int) + "|" + FmtVec2(tTree.siblingVec2) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tTree.flagsEnum);

        soTree.FindProperty("nested.frame").rectValue = new Rect(1f, 2f, 3f, 4f);
        soTree.FindProperty("nested.deep.bounds2d.x").floatValue = 88f;
        soTree.FindProperty("rectArray.Array.data[1].width").floatValue = 44f;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtRect(trRelFrame.value) + "|" + FmtFloat(tfRelFrameH.value) + "|" +
            FmtRect(trDotted.value) + "|" + FmtRect(trArray.value));

        // 4. PropertyField for Rect (Bind, BindProperty, label, callbacks, mixed multi-object, reuse, type change).
        var tPf = ScriptableObject.CreateInstance<RectTarget>();
        var soPf = new SerializedObject(tPf);
        var pfAuto = new PropertyField(soPf.FindProperty("myViewport"));
        var pfEvents = new List<string>();
        pfAuto.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.Rect)
                pfEvents.Add(e.changedProperty.propertyPath + ":" + FmtRect(e.changedProperty.rectValue));
        });
        m_Root.Add(pfAuto);
        pfAuto.Bind(soPf);
        Frames();
        Put("pf.auto.initial", DescribePf(pfAuto) + "|evts=" + string.Join(";", pfEvents));

        var pfChild = pfAuto.Q<RectField>();
        Sub(pfChild, "height").value = 42.5f;
        Frames();
        Put("pf.auto.ui_sub_h", DescribePf(pfAuto) + "|t=" + FmtRect(tPf.myViewport) + "|evts=" + FmtInt(pfEvents.Count));

        pfChild.value = new Rect(-1f, -2f, 30f, 40f);
        Frames();
        Put("pf.auto.ui_whole", DescribePf(pfAuto) + "|t=" + FmtRect(tPf.myViewport) + "|evts=" + FmtInt(pfEvents.Count));

        soPf.FindProperty("myViewport.width").floatValue = 64f;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.auto.so_sub_w", DescribePf(pfAuto) + "|evts=" + FmtInt(pfEvents.Count));

        pfAuto.label = "Custom Rect";
        Frames();
        Put("pf.auto.label_update", DescribePf(pfAuto));

        var childBeforeReuse = pfAuto.hierarchy[0];
        pfAuto.BindProperty(soPf.FindProperty("nested.frame"));
        Frames();
        var childAfterReuse = pfAuto.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfAuto));

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

        pfAuto.BindProperty(soPf.FindProperty("otherRect"));
        Frames();
        Put("pf.rebind_back_rect", DescribePf(pfAuto));

        // PropertyField with mixed multi-object Rect.
        var pmA = ScriptableObject.CreateInstance<RectTarget>();
        var pmB = ScriptableObject.CreateInstance<RectTarget>();
        pmA.multiRect = new Rect(3f, 30f, 300f, 3000f);
        pmB.multiRect = new Rect(4f, 40f, 400f, 4000f);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiRect"), "Multi Rect");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtRect(e.changedProperty.rectValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiRf = pfMulti.Q<RectField>();
        Sub(pfMultiRf, "x").value = 9f;
        Frames();
        Put("pf.multi.ui_edit_x", DescribePf(pfMulti) + "|a=" + FmtRect(pmA.multiRect) + "|b=" + FmtRect(pmB.multiRect) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<RectTarget>();
        var tRe2 = ScriptableObject.CreateInstance<RectTarget>();
        tRe1.myViewport = new Rect(1f, 2f, 3f, 4f);
        tRe1.otherRect = new Rect(5f, 6f, 7f, 8f);
        tRe2.myViewport = new Rect(70f, 80f, 90f, 100f);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new RectRuntimeSource();
        var rfRe = new RectField();
        rfRe.dataSource = rtSource;
        rfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(RectRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(rfRe);
        rfRe.BindProperty(soRe1.FindProperty("myViewport"));
        Frames();
        Put("unbind_rebind.initial", FmtRectField(rfRe) + "|tip=" + (rfRe.tooltip ?? "null"));

        rfRe.Unbind();
        Frames();
        rfRe.value = new Rect(99f, 98f, 97f, 96f);
        soRe1.FindProperty("myViewport").rectValue = new Rect(11f, 12f, 13f, 14f);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-rect-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtRectField(rfRe) + "|t1=" + FmtRect(tRe1.myViewport) + "|tip=" + (rfRe.tooltip ?? "null"));

        rfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtRectField(rfMulti));

        rfRe.BindProperty(soRe1.FindProperty("otherRect"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtRectField(rfRe));
        Sub(rfRe, "height").value = 66f;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtRect(tRe1.otherRect) + "|t1_start=" + FmtRect(tRe1.myViewport));

        rfRe.BindProperty(soRe2.FindProperty("myViewport"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtRectField(rfRe));
        Sub(rfRe, "x").value = 71f;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtRect(tRe2.myViewport) + "|t1_other=" + FmtRect(tRe1.otherRect));

        var pfRe = new PropertyField(soRe1.FindProperty("myViewport"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myViewport").rectValue = new Rect(21f, 22f, 23f, 24f);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Rect property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new IntegerField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myViewport"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", FmtInt(badField.value));
#endif
    }
}
