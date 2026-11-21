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
using UnityEditor.Rendering;
using UnityEditor.UIElements;
#endif

[Serializable]
public struct DeepMaskData
{
    public LayerMask layerMask;
    public RenderingLayerMask renderingLayerMask;
}

[Serializable]
public struct NestedMaskData
{
    public LayerMask layerMask;
    public RenderingLayerMask renderingLayerMask;
    public DeepMaskData deep;
}

public class MaskProbeRefAsset : ScriptableObject
{
    public int marker;
}

public class MaskTarget : ScriptableObject
{
    public LayerMask zeroLayerMask;
    public LayerMask myLayerMask = 1; // Default
    public LayerMask otherLayerMask = 16; // Water
    public LayerMask multiLayerMask = 1;

    public RenderingLayerMask defaultRlm = RenderingLayerMask.defaultRenderingLayerMask;
    public RenderingLayerMask myRlm = 1u;
    public RenderingLayerMask otherRlm = 2u;
    public RenderingLayerMask multiRlm = 1u;

    public NestedMaskData nested = new NestedMaskData
    {
        layerMask = 1 | 16,
        renderingLayerMask = 1u,
        deep = new DeepMaskData
        {
            layerMask = 32,
            renderingLayerMask = 2u
        }
    };

    public LayerMask[] layerMaskArray = new LayerMask[] { 1, 16, 32 };
    public RenderingLayerMask[] rlmArray = new RenderingLayerMask[] { 1u, 2u, 4u };

    // Sibling controls for tree Bind and PropertyField cross-type rebinding.
    public Hash128 siblingHash = Hash128.Parse("0123456789abcdef1122334455667788");
    public MaskProbeRefAsset siblingRef;
    public int siblingInt = 42;
}

public class MaskRuntimeSource
{
    [CreateProperty]
    public string tip { get; set; } = "rt-mask-tip-0";
}

public class MaskBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("MaskBindingProbe");
        var doc = go.AddComponent<UIDocument>();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        doc.panelSettings = ps;
        go.AddComponent<MaskBindingProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("MASK_BINDING_OUT");
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
    static string FmtUInt(uint v) => v.ToString(Inv);

#if UNITY_EDITOR
    static string FmtList(List<string> items) => items == null ? "null" : "[" + string.Join(",", items) + "]";
    static string FmtIntList(List<int> items) => items == null ? "null" : "[" + string.Join(",", items.Select(FmtInt)) + "]";

    static string FmtMaskField(MaskField mf)
    {
        return FmtInt(mf.value) + "|txt=" + (mf.text ?? "null") + "|m=" + FmtBool(mf.showMixedValue);
    }

    static string FmtRlmField(RenderingLayerMaskField rf)
    {
        var hb = rf.Q<HelpBox>();
        bool hbVisible = hb != null && hb.style.display != DisplayStyle.None;
        return FmtUInt(rf.value) + "|txt=" + (rf.text ?? "null") + "|m=" + FmtBool(rf.showMixedValue) +
               "|hb=" + FmtBool(hb != null) + FmtBool(hbVisible);
    }

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is LayerMaskField lmf)
        {
            bool hasLabelCls = lmf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = lmf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = lmf.ClassListContains(BaseField<int>.alignedFieldUssClassName);
            bool hasLmCls = lmf.ClassListContains(LayerMaskField.ussClassName);
            bool hasMaskCls = lmf.ClassListContains(MaskField.ussClassName);
            return "LayerMaskField|" + lmf.name + "|" + (lmf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasLmCls) + FmtBool(hasMaskCls) + "|" +
                   FmtMaskField(lmf);
        }
        if (child is RenderingLayerMaskField rmf)
        {
            bool hasLabelCls = rmf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = rmf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = rmf.ClassListContains(BaseField<uint>.alignedFieldUssClassName);
            bool hasRlmCls = rmf.ClassListContains(RenderingLayerMaskField.ussClassName);
            return "RenderingLayerMaskField|" + rmf.name + "|" + (rmf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasRlmCls) + "|" +
                   FmtRlmField(rmf);
        }
        if (child is Hash128Field hf)
            return "Hash128Field|" + hf.name + "|" + (hf.label ?? "null") + "|" + hf.value;
        if (child is ObjectField of)
            return "ObjectField|" + of.name + "|" + (of.label ?? "null") + "|" + (of.value != null ? of.value.name : "null");
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

        var refAsset = ScriptableObject.CreateInstance<MaskProbeRefAsset>();
        refAsset.name = "MaskRefA";

        // 0. Standalone MaskField: defaults, choices/masks, formatSelectedValueCallback, mixed value.
        var mfDef = new MaskField("Def Mask");
        m_Root.Add(mfDef);
        Frames(1);
        Put("maskfield.defaults",
            (mfDef.label ?? "null") + "|" +
            FmtBool(mfDef.ClassListContains(MaskField.ussClassName)) +
            FmtBool(mfDef.labelElement.ClassListContains(MaskField.labelUssClassName)) +
            FmtBool(mfDef.Q(className: MaskField.inputUssClassName) != null) + "|" +
            FmtList(mfDef.choices) + "|" + FmtIntList(mfDef.choicesMasks) + "|" + FmtMaskField(mfDef));

        var mfFmt = new MaskField("Fmt Mask", new List<string> { "Alpha", "Beta", "Gamma" }, 2,
            s => "[" + s + "]", s => "<" + s + ">");
        var mfEvts = new List<string>();
        mfFmt.RegisterValueChangedCallback(e => mfEvts.Add(FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(mfFmt);
        Frames(1);
        string mfInit = FmtMaskField(mfFmt);
        mfFmt.value = 0;
        string mfZero = FmtMaskField(mfFmt);
        mfFmt.value = 1;
        string mfOne = FmtMaskField(mfFmt);
        mfFmt.value = 3; // multi-bit (Alpha|Beta) -> not power of 2, not formatted
        string mfMulti = FmtMaskField(mfFmt);
        mfFmt.value = 7; // all 3 bits -> normalized to -1 ("Everything"), not formatted
        string mfAll = FmtMaskField(mfFmt);
        mfFmt.value = 1 | (1 << 5); // undefined bit stripped to 1
        string mfStrip = FmtMaskField(mfFmt);
        mfFmt.showMixedValue = true;
        string mfMixedOn = FmtMaskField(mfFmt);
        mfFmt.showMixedValue = false;
        string mfMixedOff = FmtMaskField(mfFmt);
        Put("maskfield.choices_and_formatting",
            mfInit + "||" + mfZero + "||" + mfOne + "||" + mfMulti + "||" + mfAll + "||" + mfStrip + "||" +
            mfMixedOn + "||" + mfMixedOff + "|ev=" + string.Join("|", mfEvts));

        // 1. Standalone LayerMaskField: defaults, defined layers from TagManager, format callback warnings, normalization, mixed.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && (msg.Contains("not compatible") || msg.Contains("LayerMaskField doesn't support")))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;

        var lmStd = new LayerMaskField("My Layers");
        var lmEvts = new List<string>();
        lmStd.RegisterValueChangedCallback(e => lmEvts.Add(FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(lmStd);
        Frames(1);
        Put("layermask.standalone.defaults",
            (lmStd.label ?? "null") + "|" +
            FmtBool(lmStd.ClassListContains(LayerMaskField.ussClassName)) +
            FmtBool(lmStd.ClassListContains(MaskField.ussClassName)) +
            FmtBool(lmStd.labelElement.ClassListContains(LayerMaskField.labelUssClassName)) +
            FmtBool(lmStd.Q(className: LayerMaskField.inputUssClassName) != null) + "|" +
            FmtList(lmStd.choices) + "|" + FmtIntList(lmStd.choicesMasks) + "|" + FmtMaskField(lmStd));

        lmStd.formatSelectedValueCallback = s => s;
        lmStd.formatListItemCallback = s => s;
        bool fmtNull = lmStd.formatSelectedValueCallback == null && lmStd.formatListItemCallback == null;

        lmStd.value = 1; // Default
        string lmDefault = FmtMaskField(lmStd);
        lmStd.value = 16; // Water
        string lmWater = FmtMaskField(lmStd);
        lmStd.value = 1 | 16; // Default | Water
        string lmTwo = FmtMaskField(lmStd);
        lmStd.value = 1 | 2 | 4 | 16 | 32; // All defined layers -> -1
        string lmAll = FmtMaskField(lmStd);
        lmStd.value = 1 << 8; // Undefined layer -> stripped to 0
        string lmUndef = FmtMaskField(lmStd);
        lmStd.value = 32; // UI
        lmStd.showMixedValue = true;
        string lmMixOn = FmtMaskField(lmStd);
        lmStd.showMixedValue = false;
        string lmMixOff = FmtMaskField(lmStd);
        Put("layermask.standalone.values",
            "fmtNull=" + FmtBool(fmtNull) + "||" + lmDefault + "||" + lmWater + "||" + lmTwo + "||" +
            lmAll + "||" + lmUndef + "||" + lmMixOn + "||" + lmMixOff + "|evc=" + FmtInt(lmEvts.Count));

        // 2. Standalone RenderingLayerMaskField: initial default, added rendering layers, unnamed bit, Everything->deselect one, mixed.
        var rlmInitial = new RenderingLayerMaskField("Init RLM");
        m_Root.Add(rlmInitial);
        Frames(1);
        Put("rlm.standalone.defaults_before_add",
            (rlmInitial.label ?? "null") + "|" +
            FmtBool(rlmInitial.ClassListContains(RenderingLayerMaskField.ussClassName)) +
            FmtBool(rlmInitial.labelElement.ClassListContains(RenderingLayerMaskField.labelUssClassName)) +
            FmtBool(rlmInitial.Q(className: RenderingLayerMaskField.inputUssClassName) != null) + "|" +
            FmtList(rlmInitial.choices) + "|" + FmtIntList(rlmInitial.choicesMasks) + "|" + FmtRlmField(rlmInitial));

        // Add two named rendering layers (bits 1 and 2) so we can test multi-layer, Everything, and deselect-from-Everything.
        RenderPipelineEditorUtility.TryAddRenderingLayerName("LightLayer1");
        RenderPipelineEditorUtility.TryAddRenderingLayerName("DecalLayer2");

        var rlmStd = new RenderingLayerMaskField("My RLM");
        var rlmEvts = new List<string>();
        rlmStd.RegisterValueChangedCallback(e => rlmEvts.Add(FmtUInt(e.previousValue) + "->" + FmtUInt(e.newValue)));
        m_Root.Add(rlmStd);
        Frames(1);
        string rlmAfterAdd = FmtList(rlmStd.choices) + "|" + FmtIntList(rlmStd.choicesMasks) + "|" + FmtRlmField(rlmStd);

        rlmStd.value = 0u;
        string rlmZero = FmtRlmField(rlmStd);
        rlmStd.value = 2u; // LightLayer1
        string rlmTwo = FmtRlmField(rlmStd);
        rlmStd.value = 3u; // Default | LightLayer1
        string rlmMulti = FmtRlmField(rlmStd);
        rlmStd.value = 7u; // All 3 defined layers -> uint.MaxValue ("Everything")
        string rlmAll = FmtRlmField(rlmStd);
        rlmStd.value = uint.MaxValue & ~2u; // Deselect single layer from Everything -> defined bits except bit 1 (1|4 = 5)
        string rlmDeselectOne = FmtRlmField(rlmStd);
        rlmStd.value = 1u << 6; // Unnamed bit 6 -> "Unnamed Layer 6"
        string rlmUnnamed = FmtList(rlmStd.choices) + "|" + FmtRlmField(rlmStd);
        rlmStd.value = 4u; // DecalLayer2
        rlmStd.showMixedValue = true;
        string rlmMixOn = FmtRlmField(rlmStd);
        rlmStd.showMixedValue = false;
        string rlmMixOff = FmtRlmField(rlmStd);
        Put("rlm.standalone.values",
            rlmAfterAdd + "||" + rlmZero + "||" + rlmTwo + "||" + rlmMulti + "||" + rlmAll + "||" +
            rlmDeselectOne + "||" + rlmUnnamed + "||" + rlmMixOn + "||" + rlmMixOff + "|evc=" + FmtInt(rlmEvts.Count));

        // 3. Property metadata for LayerMask and RenderingLayerMask.
        var tMain = ScriptableObject.CreateInstance<MaskTarget>();
        tMain.siblingRef = refAsset;
        var soMain = new SerializedObject(tMain);
        var pLm = soMain.FindProperty("myLayerMask");
        var pRlm = soMain.FindProperty("myRlm");
        var pLmArr1 = soMain.FindProperty("layerMaskArray.Array.data[1]");
        var pRlmArr1 = soMain.FindProperty("rlmArray.Array.data[1]");
        Put("prop.meta",
            "lm=" + pLm.propertyType + "/" + pLm.type + "/" + FmtBool(pLm.hasVisibleChildren) + "/" + FmtInt(pLm.intValue) +
            "|rlm=" + pRlm.propertyType + "/" + pRlm.type + "/" + FmtBool(pRlm.hasVisibleChildren) + "/" + FmtUInt(pRlm.uintValue) +
            "|lmArr1=" + pLmArr1.propertyType + "/" + FmtInt(pLmArr1.intValue) +
            "|rlmArr1=" + pRlmArr1.propertyType + "/" + FmtUInt(pRlmArr1.uintValue));

        // 4. Direct BindProperty for LayerMaskField & RenderingLayerMaskField.
        var lmZero = new LayerMaskField();
        var lmZeroEvts = new List<string>();
        lmZero.RegisterValueChangedCallback(e => lmZeroEvts.Add(FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(lmZero);
        lmZero.BindProperty(soMain.FindProperty("zeroLayerMask"));
        Frames();
        Put("layermask.direct.zero_initial", FmtMaskField(lmZero) + "|ev=" + string.Join("|", lmZeroEvts));

        var lmDirect = new LayerMaskField();
        var lmDirectEvts = new List<string>();
        lmDirect.RegisterValueChangedCallback(e => lmDirectEvts.Add(FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(lmDirect);
        lmDirect.BindProperty(soMain.FindProperty("myLayerMask"));
        Frames();
        Put("layermask.direct.initial", FmtMaskField(lmDirect) + "|ev=" + string.Join("|", lmDirectEvts));

        lmDirect.value = 16;
        Frames();
        string lmUi1 = FmtMaskField(lmDirect) + "|t=" + FmtInt(tMain.myLayerMask.value);
        lmDirect.value = -1;
        Frames();
        string lmUiAll = FmtMaskField(lmDirect) + "|t=" + FmtInt(tMain.myLayerMask.value);
        lmDirect.value = 0;
        Frames();
        string lmUiZero = FmtMaskField(lmDirect) + "|t=" + FmtInt(tMain.myLayerMask.value);
        Put("layermask.direct.ui_to_so", lmUi1 + "||" + lmUiAll + "||" + lmUiZero + "|evc=" + FmtInt(lmDirectEvts.Count));

        soMain.Update();
        soMain.FindProperty("myLayerMask").intValue = 32;
        soMain.ApplyModifiedProperties();
        Frames();
        string lmSoToUi = FmtMaskField(lmDirect) + "|evc=" + FmtInt(lmDirectEvts.Count);
        tMain.myLayerMask = 1 | 4;
        Frames();
        string lmExt = FmtMaskField(lmDirect) + "|evc=" + FmtInt(lmDirectEvts.Count);
        Put("layermask.direct.so_and_external", lmSoToUi + "||" + lmExt);

        var lmDet = new LayerMaskField();
        lmDet.BindProperty(soMain.FindProperty("otherLayerMask"));
        string lmDetBefore = FmtMaskField(lmDet);
        m_Root.Add(lmDet);
        Frames();
        Put("layermask.direct.detached", lmDetBefore + "||" + FmtMaskField(lmDet));

        // Direct BindProperty for RenderingLayerMaskField.
        var rlmDirect = new RenderingLayerMaskField();
        var rlmDirectEvts = new List<string>();
        rlmDirect.RegisterValueChangedCallback(e => rlmDirectEvts.Add(FmtUInt(e.previousValue) + "->" + FmtUInt(e.newValue)));
        m_Root.Add(rlmDirect);
        rlmDirect.BindProperty(soMain.FindProperty("myRlm"));
        Frames();
        Put("rlm.direct.initial", FmtRlmField(rlmDirect) + "|ev=" + string.Join("|", rlmDirectEvts));

        rlmDirect.value = 2u;
        Frames();
        string rlmUi1 = FmtRlmField(rlmDirect) + "|t=" + FmtUInt(tMain.myRlm.value);
        rlmDirect.value = uint.MaxValue;
        Frames();
        string rlmUiAll = FmtRlmField(rlmDirect) + "|t=" + FmtUInt(tMain.myRlm.value);
        rlmDirect.value = 1u << 7;
        Frames();
        string rlmUiUnnamed = FmtRlmField(rlmDirect) + "|t=" + FmtUInt(tMain.myRlm.value);
        rlmDirect.value = 0u;
        Frames();
        string rlmUiZero = FmtRlmField(rlmDirect) + "|t=" + FmtUInt(tMain.myRlm.value);
        Put("rlm.direct.ui_to_so", rlmUi1 + "||" + rlmUiAll + "||" + rlmUiUnnamed + "||" + rlmUiZero + "|evc=" + FmtInt(rlmDirectEvts.Count));

        soMain.Update();
        soMain.FindProperty("myRlm").uintValue = 4u;
        soMain.ApplyModifiedProperties();
        Frames();
        string rlmSoToUi = FmtRlmField(rlmDirect) + "|evc=" + FmtInt(rlmDirectEvts.Count);
        tMain.myRlm = 5u;
        Frames();
        string rlmExt = FmtRlmField(rlmDirect) + "|evc=" + FmtInt(rlmDirectEvts.Count);
        Put("rlm.direct.so_and_external", rlmSoToUi + "||" + rlmExt);

        var rlmDet = new RenderingLayerMaskField();
        rlmDet.BindProperty(soMain.FindProperty("otherRlm"));
        string rlmDetBefore = FmtRlmField(rlmDet);
        m_Root.Add(rlmDet);
        Frames();
        Put("rlm.direct.detached", rlmDetBefore + "||" + FmtRlmField(rlmDet));

        // 5. Multi-object mixed selection for both LayerMaskField and RenderingLayerMaskField.
        var mA = ScriptableObject.CreateInstance<MaskTarget>();
        var mB = ScriptableObject.CreateInstance<MaskTarget>();
        mA.multiLayerMask = 1;
        mB.multiLayerMask = 16;
        mA.multiRlm = 1u;
        mB.multiRlm = 2u;
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var lmMulti = new LayerMaskField();
        var lmMultiEvts = new List<string>();
        lmMulti.RegisterValueChangedCallback(e => lmMultiEvts.Add(FmtInt(e.previousValue) + "->" + FmtInt(e.newValue)));
        m_Root.Add(lmMulti);
        lmMulti.BindProperty(soMulti.FindProperty("multiLayerMask"));

        var rlmMultiField = new RenderingLayerMaskField();
        var rlmMultiEvts = new List<string>();
        rlmMultiField.RegisterValueChangedCallback(e => rlmMultiEvts.Add(FmtUInt(e.previousValue) + "->" + FmtUInt(e.newValue)));
        m_Root.Add(rlmMultiField);
        rlmMultiField.BindProperty(soMulti.FindProperty("multiRlm"));
        Frames();
        Put("multi.initial",
            "lm=" + FmtMaskField(lmMulti) + "|ec=" + FmtInt(lmMultiEvts.Count) +
            "||rlm=" + FmtRlmField(rlmMultiField) + "|ec=" + FmtInt(rlmMultiEvts.Count));

        lmMulti.value = 32;
        rlmMultiField.value = 4u;
        Frames();
        string multiNew =
            "lm=" + FmtMaskField(lmMulti) + "|a=" + FmtInt(mA.multiLayerMask.value) + "|b=" + FmtInt(mB.multiLayerMask.value) +
            "||rlm=" + FmtRlmField(rlmMultiField) + "|a=" + FmtUInt(mA.multiRlm.value) + "|b=" + FmtUInt(mB.multiRlm.value);

        mA.multiLayerMask = 1; mB.multiLayerMask = 16;
        mA.multiRlm = 1u; mB.multiRlm = 2u;
        Frames();
        lmMulti.value = 1; // target[0]'s value
        rlmMultiField.value = 1u; // target[0]'s value
        Frames();
        string multiToFirst =
            "lm=" + FmtMaskField(lmMulti) + "|a=" + FmtInt(mA.multiLayerMask.value) + "|b=" + FmtInt(mB.multiLayerMask.value) +
            "||rlm=" + FmtRlmField(rlmMultiField) + "|a=" + FmtUInt(mA.multiRlm.value) + "|b=" + FmtUInt(mB.multiRlm.value);

        mA.multiLayerMask = 1; mB.multiLayerMask = 16;
        mA.multiRlm = 1u; mB.multiRlm = 2u;
        Frames();
        lmMulti.value = -1;
        rlmMultiField.value = uint.MaxValue;
        Frames();
        string multiToAll =
            "lm=" + FmtMaskField(lmMulti) + "|a=" + FmtInt(mA.multiLayerMask.value) + "|b=" + FmtInt(mB.multiLayerMask.value) +
            "||rlm=" + FmtRlmField(rlmMultiField) + "|a=" + FmtUInt(mA.multiRlm.value) + "|b=" + FmtUInt(mB.multiRlm.value);
        Put("multi.edits", multiNew + "###" + multiToFirst + "###" + multiToAll);

        // 6. Tree Bind(so): LayerMaskField, MaskField, IntegerField on LayerMask; RenderingLayerMaskField, UnsignedIntegerField on RenderingLayerMask; nested/relative/array/siblings.
        var tTree = ScriptableObject.CreateInstance<MaskTarget>();
        tTree.siblingRef = refAsset;
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trLm = new LayerMaskField { bindingPath = "myLayerMask" };
        var trMask = new MaskField(new List<string> { "L0", "L1", "L2", "L3", "L4", "L5" }, 0) { bindingPath = "otherLayerMask" };
        var trLmInt = new IntegerField { bindingPath = "zeroLayerMask" };
        var trRlm = new RenderingLayerMaskField { bindingPath = "myRlm" };
        var trRlmUInt = new UnsignedIntegerField { bindingPath = "otherRlm" };
        var trDeepLm = new LayerMaskField { bindingPath = "nested.deep.layerMask" };
        var trDeepRlm = new RenderingLayerMaskField { bindingPath = "nested.deep.renderingLayerMask" };
        var trParent = new BindableElement { bindingPath = "nested" };
        var trRelLm = new LayerMaskField { bindingPath = "layerMask" };
        var trRelRlm = new RenderingLayerMaskField { bindingPath = "renderingLayerMask" };
        trParent.Add(trRelLm);
        trParent.Add(trRelRlm);
        var trLmArr1 = new LayerMaskField { bindingPath = "layerMaskArray.Array.data[1]" };
        var trRlmArr1 = new RenderingLayerMaskField { bindingPath = "rlmArray.Array.data[1]" };
        var trHash = new Hash128Field { bindingPath = "siblingHash" };
        var trInt = new IntegerField { bindingPath = "siblingInt" };
        treeRoot.Add(trLm);
        treeRoot.Add(trMask);
        treeRoot.Add(trLmInt);
        treeRoot.Add(trRlm);
        treeRoot.Add(trRlmUInt);
        treeRoot.Add(trDeepLm);
        treeRoot.Add(trDeepRlm);
        treeRoot.Add(trParent);
        treeRoot.Add(trLmArr1);
        treeRoot.Add(trRlmArr1);
        treeRoot.Add(trHash);
        treeRoot.Add(trInt);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            "lm=" + FmtInt(trLm.value) + "|mf=" + FmtInt(trMask.value) + "|lmi=" + FmtInt(trLmInt.value) +
            "|rlm=" + FmtUInt(trRlm.value) + "|rlmu=" + FmtUInt(trRlmUInt.value) +
            "|dlm=" + FmtInt(trDeepLm.value) + "|drlm=" + FmtUInt(trDeepRlm.value) +
            "|rl=" + FmtInt(trRelLm.value) + "|rr=" + FmtUInt(trRelRlm.value) +
            "|la1=" + FmtInt(trLmArr1.value) + "|ra1=" + FmtUInt(trRlmArr1.value) +
            "|h=" + trHash.value + "|i=" + FmtInt(trInt.value));

        trLm.value = 4;
        trMask.value = 2;
        trLmInt.value = 16;
        trRlm.value = 4u;
        trRlmUInt.value = 1u;
        trDeepLm.value = 1;
        trDeepRlm.value = 5u;
        trRelLm.value = 32;
        trRelRlm.value = 2u;
        trLmArr1.value = -1;
        trRlmArr1.value = uint.MaxValue;
        Frames();
        Put("tree.ui_to_so",
            "lm=" + FmtInt(tTree.myLayerMask.value) + "|mf=" + FmtInt(tTree.otherLayerMask.value) + "|lmi=" + FmtInt(tTree.zeroLayerMask.value) +
            "|rlm=" + FmtUInt(tTree.myRlm.value) + "|rlmu=" + FmtUInt(tTree.otherRlm.value) +
            "|dlm=" + FmtInt(tTree.nested.deep.layerMask.value) + "|drlm=" + FmtUInt(tTree.nested.deep.renderingLayerMask.value) +
            "|rl=" + FmtInt(tTree.nested.layerMask.value) + "|rr=" + FmtUInt(tTree.nested.renderingLayerMask.value) +
            "|la1=" + FmtInt(tTree.layerMaskArray[1].value) + "|ra1=" + FmtUInt(tTree.rlmArray[1].value));

        tTree.myLayerMask = 32;
        tTree.myRlm = 2u;
        tTree.nested = new NestedMaskData
        {
            layerMask = 2,
            renderingLayerMask = 4u,
            deep = new DeepMaskData { layerMask = 16, renderingLayerMask = 1u }
        };
        tTree.layerMaskArray[1] = 4;
        tTree.rlmArray[1] = 3u;
        Frames();
        Put("tree.so_to_ui",
            "lm=" + FmtInt(trLm.value) + "|rlm=" + FmtUInt(trRlm.value) +
            "|dlm=" + FmtInt(trDeepLm.value) + "|drlm=" + FmtUInt(trDeepRlm.value) +
            "|rl=" + FmtInt(trRelLm.value) + "|rr=" + FmtUInt(trRelRlm.value) +
            "|la1=" + FmtInt(trLmArr1.value) + "|ra1=" + FmtUInt(trRlmArr1.value));

        // 7. PropertyField for LayerMask and RenderingLayerMask: initial, labels, UI<->SO, reuse, cross-type rebind, multi.
        var tPf = ScriptableObject.CreateInstance<MaskTarget>();
        tPf.siblingRef = refAsset;
        var soPf = new SerializedObject(tPf);

        var pfLm = new PropertyField(soPf.FindProperty("myLayerMask"));
        var pfLmEvts = new List<string>();
        pfLm.RegisterValueChangeCallback(e => pfLmEvts.Add(FmtInt(e.changedProperty.intValue)));
        m_Root.Add(pfLm);
        pfLm.Bind(soPf);

        var pfRlm = new PropertyField(soPf.FindProperty("myRlm"));
        var pfRlmEvts = new List<string>();
        pfRlm.RegisterValueChangeCallback(e => pfRlmEvts.Add(FmtUInt(e.changedProperty.uintValue)));
        m_Root.Add(pfRlm);
        pfRlm.Bind(soPf);
        Frames();
        Put("pf.initial",
            DescribePf(pfLm) + "|ec=" + FmtInt(pfLmEvts.Count) + "||" +
            DescribePf(pfRlm) + "|ec=" + FmtInt(pfRlmEvts.Count));

        var pfLmLbl = new PropertyField(soPf.FindProperty("otherLayerMask"), "Custom LM");
        var pfRlmLbl = new PropertyField(soPf.FindProperty("otherRlm"), "Custom RLM");
        m_Root.Add(pfLmLbl);
        m_Root.Add(pfRlmLbl);
        pfLmLbl.Bind(soPf);
        pfRlmLbl.Bind(soPf);
        Frames();
        string lblBefore = DescribePf(pfLmLbl) + "||" + DescribePf(pfRlmLbl);
        pfLmLbl.label = "Dynamic LM";
        pfRlmLbl.label = "Dynamic RLM";
        Frames();
        Put("pf.labels", lblBefore + "###" + DescribePf(pfLmLbl) + "||" + DescribePf(pfRlmLbl));

        pfLm.Q<LayerMaskField>().value = 32;
        pfRlm.Q<RenderingLayerMaskField>().value = 4u;
        Frames();
        Put("pf.ui_to_so",
            DescribePf(pfLm) + "|t=" + FmtInt(tPf.myLayerMask.value) + "|ec=" + FmtInt(pfLmEvts.Count) + "||" +
            DescribePf(pfRlm) + "|t=" + FmtUInt(tPf.myRlm.value) + "|ec=" + FmtInt(pfRlmEvts.Count));

        soPf.Update();
        soPf.FindProperty("myLayerMask").intValue = 4;
        soPf.FindProperty("myRlm").uintValue = 2u;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.so_to_ui",
            DescribePf(pfLm) + "|ec=" + FmtInt(pfLmEvts.Count) + "||" +
            DescribePf(pfRlm) + "|ec=" + FmtInt(pfRlmEvts.Count));

        var pfReuse = new PropertyField(soPf.FindProperty("myLayerMask"));
        m_Root.Add(pfReuse);
        pfReuse.Bind(soPf);
        Frames();
        var origLm = pfReuse.Q<LayerMaskField>();
        pfReuse.BindProperty(soPf.FindProperty("otherLayerMask"));
        Frames();
        string reuseLm = FmtBool(ReferenceEquals(origLm, pfReuse.Q<LayerMaskField>())) + "|" + DescribePf(pfReuse);

        pfReuse.BindProperty(soPf.FindProperty("myRlm"));
        Frames();
        var origRlm = pfReuse.Q<RenderingLayerMaskField>();
        string xToRlm = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("otherRlm"));
        Frames();
        string reuseRlm = FmtBool(ReferenceEquals(origRlm, pfReuse.Q<RenderingLayerMaskField>())) + "|" + DescribePf(pfReuse);

        pfReuse.BindProperty(soPf.FindProperty("siblingHash"));
        Frames();
        string xHash = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingRef"));
        Frames();
        string xRef = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingInt"));
        Frames();
        string xInt = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("myLayerMask"));
        Frames();
        string xBackLm = DescribePf(pfReuse);
        Put("pf.reuse_and_cross_type",
            reuseLm + "||" + xToRlm + "||" + reuseRlm + "||" + xHash + "||" + xRef + "||" + xInt + "||" + xBackLm);

        // PropertyField multi-object mixed for both LayerMask and RenderingLayerMask.
        var pmA = ScriptableObject.CreateInstance<MaskTarget>();
        var pmB = ScriptableObject.CreateInstance<MaskTarget>();
        pmA.multiLayerMask = 1; pmB.multiLayerMask = 16;
        pmA.multiRlm = 1u; pmB.multiRlm = 2u;
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMultiLm = new PropertyField(soPfMulti.FindProperty("multiLayerMask"), "Multi LM");
        var pfMultiRlm = new PropertyField(soPfMulti.FindProperty("multiRlm"), "Multi RLM");
        m_Root.Add(pfMultiLm);
        m_Root.Add(pfMultiRlm);
        pfMultiLm.Bind(soPfMulti);
        pfMultiRlm.Bind(soPfMulti);
        Frames();
        string pfMultiInit = DescribePf(pfMultiLm) + "||" + DescribePf(pfMultiRlm);
        pfMultiLm.Q<LayerMaskField>().value = 32;
        pfMultiRlm.Q<RenderingLayerMaskField>().value = 4u;
        Frames();
        Put("pf.multi",
            pfMultiInit + "###" + DescribePf(pfMultiLm) + "|a=" + FmtInt(pmA.multiLayerMask.value) + "|b=" + FmtInt(pmB.multiLayerMask.value) +
            "||" + DescribePf(pfMultiRlm) + "|a=" + FmtUInt(pmA.multiRlm.value) + "|b=" + FmtUInt(pmB.multiRlm.value));

        // 8. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warnings.
        var tRe1 = ScriptableObject.CreateInstance<MaskTarget>();
        var tRe2 = ScriptableObject.CreateInstance<MaskTarget>();
        tRe1.myLayerMask = 1; tRe1.otherLayerMask = 16; tRe2.myLayerMask = 32;
        tRe1.myRlm = 1u; tRe1.otherRlm = 2u; tRe2.myRlm = 4u;
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtLm = new MaskRuntimeSource();
        var lmRe = new LayerMaskField { dataSource = rtLm };
        lmRe.SetBinding("tooltip", new DataBinding { dataSourcePath = new PropertyPath(nameof(MaskRuntimeSource.tip)), bindingMode = BindingMode.ToTarget });
        var rtRlm = new MaskRuntimeSource();
        var rlmRe = new RenderingLayerMaskField { dataSource = rtRlm };
        rlmRe.SetBinding("tooltip", new DataBinding { dataSourcePath = new PropertyPath(nameof(MaskRuntimeSource.tip)), bindingMode = BindingMode.ToTarget });
        m_Root.Add(lmRe);
        m_Root.Add(rlmRe);
        lmRe.BindProperty(soRe1.FindProperty("myLayerMask"));
        rlmRe.BindProperty(soRe1.FindProperty("myRlm"));
        Frames();
        string reInit = FmtMaskField(lmRe) + "|tip=" + lmRe.tooltip + "||" + FmtRlmField(rlmRe) + "|tip=" + rlmRe.tooltip;

        lmRe.Unbind();
        rlmRe.Unbind();
        Frames();
        lmRe.value = 4;
        rlmRe.value = 5u;
        soRe1.FindProperty("myLayerMask").intValue = 2;
        soRe1.FindProperty("myRlm").uintValue = 2u;
        soRe1.ApplyModifiedProperties();
        rtLm.tip = "rt-lm-1";
        rtRlm.tip = "rt-rlm-1";
        Frames();
        string reUnbound =
            FmtMaskField(lmRe) + "|t1=" + FmtInt(tRe1.myLayerMask.value) + "|tip=" + lmRe.tooltip + "||" +
            FmtRlmField(rlmRe) + "|t1=" + FmtUInt(tRe1.myRlm.value) + "|tip=" + rlmRe.tooltip;

        lmRe.BindProperty(soRe2.FindProperty("myLayerMask"));
        rlmRe.BindProperty(soRe2.FindProperty("myRlm"));
        Frames();
        string reSo2 = FmtMaskField(lmRe) + "||" + FmtRlmField(rlmRe);
        lmRe.value = 2;
        rlmRe.value = 1u;
        Frames();
        string reSo2Edit =
            FmtInt(tRe2.myLayerMask.value) + "|t1=" + FmtInt(tRe1.myLayerMask.value) + "||" +
            FmtUInt(tRe2.myRlm.value) + "|t1=" + FmtUInt(tRe1.myRlm.value);
        Put("unbind_rebind", reInit + "###" + reUnbound + "###" + reSo2 + "###" + reSo2Edit);

        // Incompatible field warnings (FloatField bound to LayerMask and RenderingLayerMask).
        var badLm = new FloatField();
        var badRlm = new FloatField();
        m_Root.Add(badLm);
        m_Root.Add(badRlm);
        badLm.BindProperty(soRe1.FindProperty("myLayerMask"));
        badRlm.BindProperty(soRe1.FindProperty("myRlm"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.fields", FmtInt((int)badLm.value) + "|" + FmtInt((int)badRlm.value));
#endif
    }
}
