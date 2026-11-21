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

public enum ColorControlPlainEnum
{
    Alpha = 1,
    Beta = 2
}

[Flags]
public enum ColorControlFlagsEnum
{
    None = 0,
    BitA = 1 << 0,
    BitB = 1 << 1
}

[Serializable]
public struct DeepColorData
{
    public Color glow;
}

[Serializable]
public struct NestedColorData
{
    public Color tint;
    public DeepColorData deep;
}

public class ColorTarget : ScriptableObject
{
    public Color clearColor = default;
    public Color myTint = new Color(0.25f, 0.5f, 0.75f, 0.5f);
    public Color otherColor = new Color(0.125f, 0.25f, 0.375f, 1f);
    public Color hdrRawColor = new Color(2.5f, 0.5f, 4f, 0.75f);
    public Color multiColor = new Color(1f, 0f, 0f, 1f);
    public Color32 myColor32 = new Color32(16, 128, 240, 200);
    public Color32 multiColor32 = new Color32(10, 20, 30, 40);

    [ColorUsage(false)]
    public Color noAlphaColor = new Color(0.5f, 0.25f, 0.75f, 0.5f);

    [ColorUsage(true, true)]
    public Color hdrAlphaColor = new Color(3f, 1.5f, 0.25f, 0.5f);

    [ColorUsage(false, true)]
    public Color hdrNoAlphaColor = new Color(2f, 4f, 0.5f, 0.25f);

    [ColorUsage(false, true)]
    public Color32 usageColor32 = new Color32(64, 128, 192, 128);

    public NestedColorData nested = new NestedColorData
    {
        tint = new Color(0.5f, 0.5f, 0.25f, 1f),
        deep = new DeepColorData { glow = new Color(1f, 0.5f, 0.25f, 0.75f) }
    };
    public Color[] colorArray = new[]
    {
        new Color(0.25f, 0.25f, 0.25f, 1f),
        new Color(0.75f, 0.5f, 0.25f, 0.5f)
    };
    public Vector3Int siblingVec3Int = new Vector3Int(3, -4, 5);
    public Vector2 siblingVec2 = new Vector2(1.5f, 2.5f);
    public int scalarInt = 42;
    public ColorControlPlainEnum plainEnum = ColorControlPlainEnum.Beta;
    public ColorControlFlagsEnum flagsEnum = ColorControlFlagsEnum.BitA | ColorControlFlagsEnum.BitB;
}

public class ColorRuntimeSource
{
    public string tip = "rt-color-tip-0";
}

public class ColorBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COLOR_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<ColorBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("COLOR_BINDING_OUT");
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
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";
    static string FmtColor32(Color32 c) => "(" + c.r.ToString(Inv) + "," + c.g.ToString(Inv) + "," + c.b.ToString(Inv) + "," + c.a.ToString(Inv) + ")";
    static string FmtVec2(Vector2 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + ")";
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";

    static string FmtDisp(VisualElement el) =>
        el == null ? "null" : (el.style.display.keyword == StyleKeyword.Undefined ? "undef" : el.style.display.value.ToString());

#if UNITY_EDITOR
    static string FmtColorField(ColorField f)
    {
        var inner = f.Q("unity-internal-color-field");
        var alpha = f.Q<ProgressBar>("unity-alpha-progress");
        var hdrLbl = f.Q<Label>("unity-hdr-label");
        var eye = f.Q("unity-eyedropper");
        var mixedLbl = f.Q("unity-mixed-value-label");
        bool innerMixedCls = inner != null && inner.ClassListContains(ColorField.mixedValueColorUssClassName);
        return FmtColor(f.value) + "|m=" + FmtBool(f.showMixedValue) +
               "|a=" + FmtBool(f.showAlpha) + "|h=" + FmtBool(f.hdr) + "|e=" + FmtBool(f.showEyeDropper) +
               "|im=" + FmtBool(innerMixedCls) +
               "|d=" + FmtDisp(alpha) + "/" + FmtDisp(hdrLbl) + "/" + FmtDisp(eye) + "/" + FmtDisp(mixedLbl);
    }

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is ColorField cf)
        {
            bool hasLabelCls = cf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = cf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = cf.ClassListContains(BaseField<Color>.alignedFieldUssClassName);
            bool hasColorCls = cf.ClassListContains(ColorField.ussClassName);
            return "ColorField|" + (cf.name ?? "null") + "|" + (cf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasColorCls) + "|" +
                   FmtColorField(cf);
        }
        if (child is Vector3IntField v3i)
            return "Vector3IntField|" + v3i.name + "|" + (v3i.label ?? "null") + "|" + FmtVec3Int(v3i.value);
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

        // 0. Standalone ColorField defaults & property toggles.
        var cfStandalone = new ColorField();
        Put("standalone.default", FmtColorField(cfStandalone) + "|cls=" + FmtBool(cfStandalone.ClassListContains(ColorField.ussClassName)));
        cfStandalone.showAlpha = false;
        cfStandalone.hdr = true;
        cfStandalone.showEyeDropper = true;
        Put("standalone.flags_on", FmtColorField(cfStandalone));
        cfStandalone.showMixedValue = true;
        Put("standalone.mixed_on", FmtColorField(cfStandalone));
        cfStandalone.showMixedValue = false;
        Put("standalone.mixed_off", FmtColorField(cfStandalone));

        // 1. Direct ColorField BindProperty: initial sync, RGBA both ways, HDR, showAlpha=false, Color32, detached.
        var t1 = ScriptableObject.CreateInstance<ColorTarget>();
        var so1 = new SerializedObject(t1);

        Put("prop.meta",
            "color:" + FmtBool(so1.FindProperty("myTint").hasVisibleChildren) + ":" + so1.FindProperty("myTint").type +
            "|c32:" + FmtBool(so1.FindProperty("myColor32").hasVisibleChildren) + ":" + so1.FindProperty("myColor32").type);

        var cfZero = new ColorField();
        var zeroEvts = new List<string>();
        cfZero.RegisterValueChangedCallback(e => zeroEvts.Add(FmtColor(e.previousValue) + "->" + FmtColor(e.newValue)));
        m_Root.Add(cfZero);
        cfZero.BindProperty(so1.FindProperty("clearColor"));
        Frames();
        Put("direct.zero.initial", FmtColorField(cfZero) + "|e=" + string.Join(";", zeroEvts));

        cfZero.value = default;
        so1.Update();
        Frames();
        Put("direct.zero.noop", FmtColorField(cfZero) + "|ec=" + FmtInt(zeroEvts.Count) + "|mod=" + FmtBool(so1.hasModifiedProperties));

        var cfMain = new ColorField();
        var mainEvts = new List<string>();
        cfMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtColor(e.previousValue) + "->" + FmtColor(e.newValue)));
        m_Root.Add(cfMain);
        cfMain.BindProperty(so1.FindProperty("myTint"));
        Frames();
        Put("direct.main.initial", FmtColorField(cfMain) + "|e=" + string.Join(";", mainEvts));

        // UI -> SO RGBA write.
        cfMain.value = new Color(0.125f, 0.375f, 0.625f, 0.875f);
        Frames();
        Put("direct.main.ui_to_so", FmtColorField(cfMain) + "|t=" + FmtColor(t1.myTint) + "|p=" + FmtColor(so1.FindProperty("myTint").colorValue) + "|ec=" + FmtInt(mainEvts.Count));

        // No-op write.
        int countBeforeNoop = mainEvts.Count;
        cfMain.value = new Color(0.125f, 0.375f, 0.625f, 0.875f);
        Frames();
        Put("direct.main.noop", FmtColor(t1.myTint) + "|ec_delta=" + FmtInt(mainEvts.Count - countBeforeNoop));

        // SO -> UI whole color.
        so1.FindProperty("myTint").colorValue = new Color(0.75f, 0.25f, 0.125f, 0.5f);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_whole", FmtColorField(cfMain) + "|t=" + FmtColor(t1.myTint) + "|ec=" + FmtInt(mainEvts.Count));

        // Direct C# target mutation picked up on panel update.
        t1.myTint = new Color(0.5f, 1f, 0.25f, 0.75f);
        Frames();
        Put("direct.main.external_target", FmtColorField(cfMain) + "|ec=" + FmtInt(mainEvts.Count));

        // SO -> UI per-component edits via FindPropertyRelative (r, g, b, a).
        so1.FindProperty("myTint").FindPropertyRelative("r").floatValue = 0.25f;
        so1.FindProperty("myTint").FindPropertyRelative("a").floatValue = 0.125f;
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.main.so_comp_ra", FmtColorField(cfMain) + "|t=" + FmtColor(t1.myTint));

        // HDR values on ColorField with hdr=true and hdr=false, plus showAlpha=false alpha preservation.
        var cfHdr = new ColorField { hdr = true, showAlpha = false };
        m_Root.Add(cfHdr);
        cfHdr.BindProperty(so1.FindProperty("hdrRawColor"));
        Frames();
        Put("direct.hdr.initial", FmtColorField(cfHdr));
        cfHdr.value = new Color(5f, 2.5f, 0.25f, 0.25f);
        Frames();
        Put("direct.hdr.ui_to_so", FmtColorField(cfHdr) + "|t=" + FmtColor(t1.hdrRawColor));

        var cfLdr = new ColorField { hdr = false };
        m_Root.Add(cfLdr);
        cfLdr.BindProperty(so1.FindProperty("hdrRawColor"));
        Frames();
        Put("direct.ldr_reads_hdr", FmtColorField(cfLdr));

        // Color32 binding to ColorField.
        var cf32 = new ColorField();
        m_Root.Add(cf32);
        cf32.BindProperty(so1.FindProperty("myColor32"));
        Frames();
        Put("direct.color32.initial", FmtColorField(cf32) + "|t=" + FmtColor32(t1.myColor32));
        cf32.value = new Color(1f, 0f, 0.5f, 0.25f);
        Frames();
        Put("direct.color32.ui_to_so", FmtColor32(t1.myColor32) + "|ui=" + FmtColor(cf32.value));
        so1.FindProperty("myColor32").colorValue = new Color32(50, 100, 150, 255);
        so1.ApplyModifiedProperties();
        Frames();
        Put("direct.color32.so_to_ui", FmtColor32(t1.myColor32) + "|ui=" + FmtColor(cf32.value));

        // Detached BindProperty(SerializedObject) via bindingPath -> deferred until attach.
        var cfDetached = new ColorField { bindingPath = "otherColor" };
        var retProp = cfDetached.BindProperty(so1);
        Frames();
        Put("direct.detached.before_attach", (retProp != null ? retProp.propertyPath : "null") + "|" + FmtColorField(cfDetached));
        m_Root.Add(cfDetached);
        Frames();
        Put("direct.detached.after_attach", FmtColorField(cfDetached));

        // 2. Multi-object mixed values on ColorField (RGB diff, alpha-only diff, Color32 diff, direct FloatField on .r).
        var mA = ScriptableObject.CreateInstance<ColorTarget>();
        var mB = ScriptableObject.CreateInstance<ColorTarget>();
        mA.multiColor = new Color(1f, 0f, 0f, 1f);
        mB.multiColor = new Color(0f, 1f, 0f, 1f);
        mA.multiColor32 = new Color32(10, 20, 30, 255);
        mB.multiColor32 = new Color32(40, 50, 60, 255);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var cfMulti = new ColorField();
        var multiEvts = new List<string>();
        cfMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtColor(e.previousValue) + "->" + FmtColor(e.newValue)));
        m_Root.Add(cfMulti);
        cfMulti.BindProperty(soMulti.FindProperty("multiColor"));
        Frames();
        Put("multi.initial_rgb", FmtColorField(cfMulti) + "|a=" + FmtColor(mA.multiColor) + "|b=" + FmtColor(mB.multiColor) + "|ec=" + FmtInt(multiEvts.Count));

        // Write target[0]'s existing value while mixed.
        cfMulti.value = new Color(1f, 0f, 0f, 1f);
        Frames();
        Put("multi.ui_write_t0", FmtColorField(cfMulti) + "|a=" + FmtColor(mA.multiColor) + "|b=" + FmtColor(mB.multiColor) + "|ec=" + FmtInt(multiEvts.Count));

        // Write a new color -> applies to both targets and clears showMixedValue.
        cfMulti.value = new Color(0.25f, 0.5f, 0.75f, 0.5f);
        Frames();
        Put("multi.ui_write_new", FmtColorField(cfMulti) + "|a=" + FmtColor(mA.multiColor) + "|b=" + FmtColor(mB.multiColor) + "|ec=" + FmtInt(multiEvts.Count));

        // Diverge ONLY in alpha -> showMixedValue becomes true again.
        mA.multiColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);
        mB.multiColor = new Color(0.5f, 0.5f, 0.5f, 0.75f);
        soMulti.Update();
        Frames();
        Put("multi.alpha_only_mixed", FmtColorField(cfMulti));

        // Direct FloatField bound to multiColor.r on mixed targets -> updates r on both while preserving g/b/a.
        mA.multiColor = new Color(0.25f, 0.5f, 0.75f, 0.25f);
        mB.multiColor = new Color(0.75f, 0.25f, 0.125f, 0.75f);
        soMulti.Update();
        Frames();
        var ffCompR = new FloatField();
        m_Root.Add(ffCompR);
        ffCompR.BindProperty(soMulti.FindProperty("multiColor.r"));
        Frames();
        Put("multi.comp_r.initial", FmtFloat(ffCompR.value) + ":" + FmtBool(ffCompR.showMixedValue));
        ffCompR.value = 0.5f;
        Frames();
        Put("multi.comp_r.ui_edit", FmtFloat(ffCompR.value) + ":" + FmtBool(ffCompR.showMixedValue) + "|a=" + FmtColor(mA.multiColor) + "|b=" + FmtColor(mB.multiColor));

        // Multi-object Color32.
        var cfMulti32 = new ColorField();
        m_Root.Add(cfMulti32);
        cfMulti32.BindProperty(soMulti.FindProperty("multiColor32"));
        Frames();
        Put("multi.color32.initial", FmtColorField(cfMulti32));
        cfMulti32.value = new Color(0f, 1f, 0f, 1f);
        Frames();
        Put("multi.color32.ui_edit", FmtColorField(cfMulti32) + "|a=" + FmtColor32(mA.multiColor32) + "|b=" + FmtColor32(mB.multiColor32));

        // 3. Tree Bind(so) with top-level, nested dotted, relative parent-child, array element, and sibling fields.
        var tTree = ScriptableObject.CreateInstance<ColorTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var tcMain = new ColorField { bindingPath = "myTint" };
        var tcDotted = new ColorField { bindingPath = "nested.deep.glow" };
        var tcArray = new ColorField { bindingPath = "colorArray.Array.data[1]" };
        var nestedGroup = new BindableElement { bindingPath = "nested" };
        var tcRelTint = new ColorField { bindingPath = "tint" };
        var tfRelTintA = new FloatField { bindingPath = "tint.a" };
        nestedGroup.Add(tcRelTint);
        nestedGroup.Add(tfRelTintA);
        var tVec3Int = new Vector3IntField { bindingPath = "siblingVec3Int" };
        var tVec2 = new Vector2Field { bindingPath = "siblingVec2" };
        var tInt = new IntegerField { bindingPath = "scalarInt" };
        var tPlain = new EnumField { bindingPath = "plainEnum" };
        var tFlags = new EnumFlagsField { bindingPath = "flagsEnum" };
        treeRoot.Add(tcMain);
        treeRoot.Add(tcDotted);
        treeRoot.Add(tcArray);
        treeRoot.Add(nestedGroup);
        treeRoot.Add(tVec3Int);
        treeRoot.Add(tVec2);
        treeRoot.Add(tInt);
        treeRoot.Add(tPlain);
        treeRoot.Add(tFlags);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            FmtColor(tcMain.value) + "|" + FmtColor(tcDotted.value) + "|" + FmtColor(tcArray.value) + "|" +
            FmtColor(tcRelTint.value) + "|" + FmtFloat(tfRelTintA.value) + "|" +
            FmtVec3Int(tVec3Int.value) + "|" + FmtVec2(tVec2.value) + "|" +
            FmtInt(tInt.value) + "|" + tPlain.value + "|" + tFlags.value);

        tcDotted.value = new Color(0.5f, 0.25f, 1f, 0.5f);
        tcArray.value = new Color(0.125f, 0.25f, 0.5f, 0.75f);
        tfRelTintA.value = 0.25f;
        tVec3Int.Q<IntegerField>("unity-z-input").value = 99;
        tVec2.Q<FloatField>("unity-x-input").value = 3.25f;
        tInt.value = 77;
        tPlain.value = ColorControlPlainEnum.Alpha;
        tFlags.value = ColorControlFlagsEnum.BitB;
        Frames();
        Put("tree.ui_to_so",
            FmtColor(tTree.nested.deep.glow) + "|" + FmtColor(tTree.colorArray[1]) + "|" +
            FmtColor(tTree.nested.tint) + "|" + FmtColor(tcRelTint.value) + "|" +
            FmtVec3Int(tTree.siblingVec3Int) + "|" + FmtVec2(tTree.siblingVec2) + "|" +
            FmtInt(tTree.scalarInt) + "|" + tTree.plainEnum + "|" + tTree.flagsEnum);

        soTree.FindProperty("nested.tint").colorValue = new Color(0.25f, 0.75f, 0.5f, 0.5f);
        soTree.FindProperty("nested.deep.glow.r").floatValue = 0.125f;
        soTree.FindProperty("colorArray.Array.data[1].b").floatValue = 1f;
        soTree.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            FmtColor(tcRelTint.value) + "|" + FmtFloat(tfRelTintA.value) + "|" +
            FmtColor(tcDotted.value) + "|" + FmtColor(tcArray.value));

        // 4. PropertyField for plain Color, Color32, and [ColorUsageAttribute] combinations.
        var tPf = ScriptableObject.CreateInstance<ColorTarget>();
        var soPf = new SerializedObject(tPf);

        var pfPlain = new PropertyField(soPf.FindProperty("myTint"));
        var pfPlainEvts = new List<string>();
        pfPlain.RegisterValueChangeCallback(e =>
        {
            if (e.changedProperty.propertyType == SerializedPropertyType.Color)
                pfPlainEvts.Add(e.changedProperty.propertyPath + ":" + FmtColor(e.changedProperty.colorValue));
        });
        m_Root.Add(pfPlain);
        pfPlain.Bind(soPf);
        Frames();
        Put("pf.plain.initial", DescribePf(pfPlain) + "|evts=" + string.Join(";", pfPlainEvts));

        var pfPlainChild = pfPlain.Q<ColorField>();
        pfPlainChild.value = new Color(0.5f, 0.25f, 0.125f, 0.75f);
        Frames();
        Put("pf.plain.ui_edit", DescribePf(pfPlain) + "|t=" + FmtColor(tPf.myTint) + "|ec=" + FmtInt(pfPlainEvts.Count));

        soPf.FindProperty("myTint.g").floatValue = 0.75f;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.plain.so_comp_g", DescribePf(pfPlain) + "|ec=" + FmtInt(pfPlainEvts.Count));

        pfPlain.label = "Custom Tint";
        Frames();
        Put("pf.plain.label_update", DescribePf(pfPlain));

        var childBeforeReuse = pfPlain.hierarchy[0];
        pfPlain.BindProperty(soPf.FindProperty("nested.tint"));
        Frames();
        var childAfterReuse = pfPlain.hierarchy[0];
        Put("pf.rebind_same_type", FmtBool(ReferenceEquals(childBeforeReuse, childAfterReuse)) + "|" + DescribePf(pfPlain));

        // Rebind plain Color PropertyField -> [ColorUsage(false, true)] Color -> plain Color.
        pfPlain.BindProperty(soPf.FindProperty("hdrNoAlphaColor"));
        Frames();
        Put("pf.rebind_plain_to_usage", FmtBool(ReferenceEquals(childAfterReuse, pfPlain.hierarchy[0])) + "|" + DescribePf(pfPlain));

        pfPlain.BindProperty(soPf.FindProperty("siblingVec3Int"));
        Frames();
        Put("pf.rebind_vec3int", DescribePf(pfPlain));

        pfPlain.BindProperty(soPf.FindProperty("scalarInt"));
        Frames();
        Put("pf.rebind_scalar", DescribePf(pfPlain));

        pfPlain.BindProperty(soPf.FindProperty("otherColor"));
        Frames();
        Put("pf.rebind_back_color", DescribePf(pfPlain));

        // PropertyField with [ColorUsage(false)] (showAlpha=false, hdr=false).
        var pfNoAlpha = new PropertyField(soPf.FindProperty("noAlphaColor"));
        var pfNoAlphaEvts = new List<string>();
        pfNoAlpha.RegisterValueChangeCallback(e => pfNoAlphaEvts.Add(e.changedProperty.propertyPath + ":" + FmtColor(e.changedProperty.colorValue)));
        m_Root.Add(pfNoAlpha);
        pfNoAlpha.Bind(soPf);
        Frames(4);
        Put("pf.usage_no_alpha.initial", DescribePf(pfNoAlpha) + "|evts=" + string.Join(";", pfNoAlphaEvts));
        var pfNoAlphaChild = pfNoAlpha.Q<ColorField>();
        pfNoAlphaChild.value = new Color(0.25f, 0.5f, 0.75f, 0.25f);
        Frames(4);
        Put("pf.usage_no_alpha.ui_edit", DescribePf(pfNoAlpha) + "|t=" + FmtColor(tPf.noAlphaColor) + "|ec=" + FmtInt(pfNoAlphaEvts.Count));
        pfNoAlpha.label = "Opaque Label";
        Frames(4);
        Put("pf.usage_no_alpha.label_update", DescribePf(pfNoAlpha));

        // PropertyField with [ColorUsage(true, true)] (showAlpha=true, hdr=true) and custom ctor label.
        var pfHdrAlpha = new PropertyField(soPf.FindProperty("hdrAlphaColor"), "HDR Glow");
        m_Root.Add(pfHdrAlpha);
        pfHdrAlpha.Bind(soPf);
        Frames(4);
        Put("pf.usage_hdr_alpha.initial", DescribePf(pfHdrAlpha));
        pfHdrAlpha.Q<ColorField>().value = new Color(6f, 2f, 0.5f, 0.25f);
        Frames(4);
        Put("pf.usage_hdr_alpha.ui_edit", DescribePf(pfHdrAlpha) + "|t=" + FmtColor(tPf.hdrAlphaColor));

        // PropertyField with [ColorUsage(false, true)] on Color and Color32.
        var pfHdrNoAlpha = new PropertyField(soPf.FindProperty("hdrNoAlphaColor"));
        m_Root.Add(pfHdrNoAlpha);
        pfHdrNoAlpha.Bind(soPf);
        Frames(4);
        Put("pf.usage_hdr_no_alpha.initial", DescribePf(pfHdrNoAlpha));

        var pfUsage32 = new PropertyField(soPf.FindProperty("usageColor32"));
        m_Root.Add(pfUsage32);
        pfUsage32.Bind(soPf);
        Frames(4);
        Put("pf.usage_color32.initial", DescribePf(pfUsage32));

        // Rebind a [ColorUsage] PropertyField to a plain Color property.
        var usageChildBefore = pfHdrNoAlpha.hierarchy[0];
        pfHdrNoAlpha.BindProperty(soPf.FindProperty("otherColor"));
        Frames(4);
        Put("pf.rebind_usage_to_plain", FmtBool(ReferenceEquals(usageChildBefore, pfHdrNoAlpha.hierarchy[0])) + "|" + DescribePf(pfHdrNoAlpha));

        // PropertyField with mixed multi-object plain Color and [ColorUsage(false, true)] Color.
        var pmA = ScriptableObject.CreateInstance<ColorTarget>();
        var pmB = ScriptableObject.CreateInstance<ColorTarget>();
        pmA.multiColor = new Color(1f, 0f, 0f, 1f);
        pmB.multiColor = new Color(0f, 0f, 1f, 0.5f);
        pmA.hdrNoAlphaColor = new Color(2f, 1f, 0.5f, 1f);
        pmB.hdrNoAlphaColor = new Color(4f, 1f, 0.5f, 1f);
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });

        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiColor"), "Multi Color");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtColor(e.changedProperty.colorValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));
        pfMulti.Q<ColorField>().value = new Color(0.5f, 0.75f, 0.25f, 0.5f);
        Frames();
        Put("pf.multi.ui_edit", DescribePf(pfMulti) + "|a=" + FmtColor(pmA.multiColor) + "|b=" + FmtColor(pmB.multiColor) + "|ec=" + FmtInt(pfMultiEvts.Count));

        var pfMultiUsage = new PropertyField(soPfMulti.FindProperty("hdrNoAlphaColor"));
        m_Root.Add(pfMultiUsage);
        pfMultiUsage.Bind(soPfMulti);
        Frames(4);
        Put("pf.multi_usage.initial", DescribePf(pfMultiUsage));
        pfMultiUsage.Q<ColorField>().value = new Color(3f, 2f, 1f, 0.5f);
        Frames(4);
        Put("pf.multi_usage.ui_edit", DescribePf(pfMultiUsage) + "|a=" + FmtColor(pmA.hdrNoAlphaColor) + "|b=" + FmtColor(pmB.hdrNoAlphaColor));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<ColorTarget>();
        var tRe2 = ScriptableObject.CreateInstance<ColorTarget>();
        tRe1.myTint = new Color(1f, 0f, 0f, 1f);
        tRe1.otherColor = new Color(0f, 1f, 0f, 0.5f);
        tRe2.myTint = new Color(0f, 0f, 1f, 0.25f);
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new ColorRuntimeSource();
        var cfRe = new ColorField();
        cfRe.dataSource = rtSource;
        cfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(ColorRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(cfRe);
        cfRe.BindProperty(soRe1.FindProperty("myTint"));
        Frames();
        Put("unbind_rebind.initial", FmtColorField(cfRe) + "|tip=" + (cfRe.tooltip ?? "null"));

        cfRe.Unbind();
        Frames();
        cfRe.value = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        soRe1.FindProperty("myTint").colorValue = new Color(0.25f, 0.25f, 0.25f, 1f);
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-color-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtColorField(cfRe) + "|t1=" + FmtColor(tRe1.myTint) + "|tip=" + (cfRe.tooltip ?? "null"));

        cfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtColorField(cfMulti));

        cfRe.BindProperty(soRe1.FindProperty("otherColor"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtColorField(cfRe));
        cfRe.value = new Color(0.75f, 0.25f, 0.5f, 1f);
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtColor(tRe1.otherColor) + "|t1_start=" + FmtColor(tRe1.myTint));

        cfRe.BindProperty(soRe2.FindProperty("myTint"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtColorField(cfRe));
        cfRe.value = new Color(0.25f, 0.5f, 1f, 0.75f);
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtColor(tRe2.myTint) + "|t1_other=" + FmtColor(tRe1.otherColor));

        var pfRe = new PropertyField(soRe1.FindProperty("myTint"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myTint").colorValue = new Color(0.125f, 0.5f, 0.75f, 1f);
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field type bound to Color property -> captures warning log.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badField = new FloatField();
        m_Root.Add(badField);
        badField.BindProperty(soRe1.FindProperty("myTint"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.float_field", FmtFloat(badField.value));
#endif
    }
}
