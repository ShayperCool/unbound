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

[Flags]
public enum BasicFlags
{
    None = 0,
    ReadAccess = 1 << 0,
    [InspectorName("Write Mode")]
    WriteAccess = 1 << 1,
    Execute = 1 << 2,
    ReadWrite = ReadAccess | WriteAccess,
    All = ~0
}

[Flags]
public enum SparseFlags
{
    AlphaBit = 1 << 0,
    GammaBit = 1 << 2,
    EpsilonBit = 1 << 4,
    [Obsolete("Legacy flag")]
    LegacyBit = 1 << 6
}

[Flags]
public enum ByteFlags : byte
{
    FlagA = 1 << 0,
    FlagB = 1 << 1,
    FlagC = 1 << 2,
    AllByte = 255
}

[Flags]
public enum SignedHighBitFlags
{
    LowBit = 1 << 0,
    MidBit = 1 << 3,
    SignBit = int.MinValue
}

public enum PlainControlEnum
{
    First = 1,
    Second = 2
}

[Serializable]
public struct NestedFlagsData
{
    public BasicFlags flags;
    public SparseFlags sparseFlags;
}

public class EnumFlagsTarget : ScriptableObject
{
    public BasicFlags basic = BasicFlags.None;
    public SparseFlags sparse = 0;
    public ByteFlags byteFlags = ByteFlags.FlagA | ByteFlags.FlagB;
    public SignedHighBitFlags signedHigh = SignedHighBitFlags.SignBit;
    public PlainControlEnum plain = PlainControlEnum.Second;
    public NestedFlagsData nested = new NestedFlagsData
    {
        flags = BasicFlags.ReadWrite,
        sparseFlags = SparseFlags.GammaBit
    };
    public int scalarInt = 77;
}

public class EnumFlagsRuntimeSource
{
    public string tip = "rt-flags-tip-0";
}

public class EnumFlagsBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENUM_FLAGS_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<EnumFlagsBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("ENUM_FLAGS_BINDING_OUT");
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

    static string FmtFlags(Enum e)
    {
        if (e == null) return "null";
        long num = Enum.GetUnderlyingType(e.GetType()) == typeof(byte)
            ? Convert.ToByte(e, Inv)
            : Convert.ToInt64(e, Inv);
        return e.GetType().Name + "(" + num.ToString(Inv) + ")";
    }

#if UNITY_EDITOR
    static string FmtEff(EnumFlagsField f)
    {
        var choices = f.choices != null ? string.Join(",", f.choices) : "null";
        var masks = f.choicesMasks != null ? string.Join(",", f.choicesMasks.Select(m => m.ToString(Inv))) : "null";
        return FmtFlags(f.value) + "|" + (f.text ?? "null") + "|" + FmtBool(f.showMixedValue) + "|[" + choices + "]|[" + masks + "]";
    }

    static string FmtEffShort(EnumFlagsField f) =>
        FmtFlags(f.value) + "|" + (f.text ?? "null") + "|" + FmtBool(f.showMixedValue);

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is EnumFlagsField eff)
        {
            bool hasLabelCls = eff.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = eff.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = eff.ClassListContains(BaseField<Enum>.alignedFieldUssClassName);
            bool hasEffCls = eff.ClassListContains(EnumFlagsField.ussClassName);
            return "EnumFlagsField|" + eff.name + "|" + (eff.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasEffCls) + "|" +
                   FmtEff(eff);
        }
        if (child is PopupField<string> p)
            return "PopupField|" + p.name + "|" + (p.label ?? "null") + "|" + p.index.ToString(Inv) + "|" + (p.value ?? "null");
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

        // 1. Direct EnumFlagsField.BindProperty on BasicFlags (zero, single, composite, multi, all-bits, SO->UI).
        var t1 = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        var so1 = new SerializedObject(t1);
        var effBasic = new EnumFlagsField();
        var basicEvts = new List<string>();
        effBasic.RegisterValueChangedCallback(e => basicEvts.Add(FmtFlags(e.previousValue) + "->" + FmtFlags(e.newValue)));
        m_Root.Add(effBasic);
        effBasic.BindProperty(so1.FindProperty("basic"));
        Frames();
        Put("eff.basic.initial_zero", FmtEff(effBasic) + "|" + basicEvts.Count.ToString(Inv));

        effBasic.value = BasicFlags.WriteAccess;
        Frames();
        Put("eff.basic.ui_single", FmtEffShort(effBasic) + "|" + FmtFlags(t1.basic) + "|" +
            so1.FindProperty("basic").intValue.ToString(Inv) + "|" + basicEvts.Count.ToString(Inv));

        effBasic.value = BasicFlags.ReadWrite;
        Frames();
        Put("eff.basic.ui_composite", FmtEffShort(effBasic) + "|" + FmtFlags(t1.basic));

        effBasic.value = BasicFlags.ReadAccess | BasicFlags.Execute;
        Frames();
        Put("eff.basic.ui_multi_unnamed", FmtEffShort(effBasic) + "|" + FmtFlags(t1.basic) + "|" +
            so1.FindProperty("basic").intValue.ToString(Inv));

        effBasic.value = BasicFlags.ReadAccess | BasicFlags.WriteAccess | BasicFlags.Execute;
        Frames();
        Put("eff.basic.ui_all_defined", FmtEffShort(effBasic) + "|" + FmtFlags(t1.basic) + "|" +
            so1.FindProperty("basic").intValue.ToString(Inv));

        so1.FindProperty("basic").intValue = 0;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.basic.so_zero", FmtEffShort(effBasic) + "|" + FmtFlags(t1.basic));

        so1.FindProperty("basic").intValue = -1;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.basic.so_all", FmtEffShort(effBasic) + "|" + FmtFlags(t1.basic));

        so1.FindProperty("basic").intValue = 1;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.basic.so_single", FmtEffShort(effBasic) + "|" + FmtFlags(t1.basic));

        // 2. SparseFlags (no 0 / no -1 / obsolete / undefined bit).
        var effSparse = new EnumFlagsField();
        m_Root.Add(effSparse);
        effSparse.BindProperty(so1.FindProperty("sparse"));
        Frames();
        Put("eff.sparse.initial_zero", FmtEff(effSparse));

        effSparse.value = SparseFlags.AlphaBit | SparseFlags.GammaBit | SparseFlags.EpsilonBit;
        Frames();
        Put("eff.sparse.ui_all_defined", FmtEffShort(effSparse) + "|" + FmtFlags(t1.sparse) + "|" +
            so1.FindProperty("sparse").intValue.ToString(Inv));

        so1.FindProperty("sparse").intValue = (1 << 10) | 1;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.sparse.so_undefined_bit", FmtEffShort(effSparse) + "|" + ((int)t1.sparse).ToString(Inv));

        effSparse.value = (SparseFlags)(1 << 10);
        Frames();
        Put("eff.sparse.ui_undefined_bit", FmtEffShort(effSparse) + "|" + ((int)t1.sparse).ToString(Inv));

        // 3. ByteFlags (unsigned byte including 255) and SignedHighBitFlags (bit 31 / int.MinValue).
        var effByte = new EnumFlagsField();
        var effSigned = new EnumFlagsField();
        m_Root.Add(effByte);
        m_Root.Add(effSigned);
        effByte.BindProperty(so1.FindProperty("byteFlags"));
        effSigned.BindProperty(so1.FindProperty("signedHigh"));
        Frames();
        Put("eff.byte.initial", FmtEff(effByte));

        effByte.value = ByteFlags.FlagA | ByteFlags.FlagB | ByteFlags.FlagC;
        Frames();
        Put("eff.byte.ui_all_defined", FmtEffShort(effByte) + "|" + ((int)t1.byteFlags).ToString(Inv) + "|" +
            so1.FindProperty("byteFlags").intValue.ToString(Inv));

        so1.FindProperty("byteFlags").intValue = 4;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.byte.so_single", FmtEffShort(effByte));

        so1.FindProperty("byteFlags").intValue = 0;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.byte.so_zero", FmtEffShort(effByte));

        so1.FindProperty("byteFlags").intValue = 255;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.byte.so_255", FmtEffShort(effByte));

        Put("eff.signed.initial", FmtEff(effSigned));
        effSigned.value = SignedHighBitFlags.LowBit | SignedHighBitFlags.SignBit;
        Frames();
        Put("eff.signed.ui_multi", FmtEffShort(effSigned) + "|" + ((int)t1.signedHigh).ToString(Inv));

        so1.FindProperty("signedHigh").intValue = -1;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.signed.so_all", FmtEffShort(effSigned));

        so1.FindProperty("signedHigh").intValue = 0;
        so1.ApplyModifiedProperties();
        Frames();
        Put("eff.signed.so_zero", FmtEffShort(effSigned));

        // 4. Detached EnumFlagsField.BindProperty before attaching to panel.
        var tDet = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        tDet.basic = BasicFlags.Execute;
        var soDet = new SerializedObject(tDet);
        var effDet = new EnumFlagsField();
        effDet.BindProperty(soDet.FindProperty("basic"));
        Put("eff.detached.beforeAttach", FmtEffShort(effDet));
        m_Root.Add(effDet);
        Frames();
        Put("eff.detached.afterAttach", FmtEffShort(effDet));
        effDet.value = BasicFlags.ReadWrite;
        Frames();
        Put("eff.detached.afterUiEdit", FmtEffShort(effDet) + "|" + FmtFlags(tDet.basic));

        // 5. Tree Bind(SerializedObject): top-level and nested struct EnumFlagsField + Unbind.
        var tTree = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        tTree.basic = BasicFlags.ReadAccess;
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var treeTop = new EnumFlagsField { bindingPath = "basic" };
        var nestedBox = new BindableElement { bindingPath = "nested" };
        var treeNested = new EnumFlagsField { bindingPath = "flags" };
        nestedBox.Add(treeNested);
        treeRoot.Add(treeTop);
        treeRoot.Add(nestedBox);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial", FmtEffShort(treeTop) + " || " + FmtEffShort(treeNested));

        treeNested.value = BasicFlags.Execute;
        Frames();
        Put("tree.ui_to_so", FmtFlags(tTree.nested.flags) + "|" + FmtEffShort(treeNested));

        tTree.nested.flags = BasicFlags.All;
        Frames();
        Put("tree.so_to_ui", FmtEffShort(treeNested));

        treeRoot.Unbind();
        tTree.basic = BasicFlags.Execute;
        tTree.nested.flags = BasicFlags.None;
        Frames();
        Put("tree.afterUnbind", FmtEffShort(treeTop) + " || " + FmtEffShort(treeNested));

        // 6. Rebind EnumFlagsField: same type (targetA -> targetB), different [Flags] type, then Unbind.
        var tA = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        tA.basic = BasicFlags.ReadAccess;
        var tB = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        tB.basic = BasicFlags.Execute;
        tB.sparse = SparseFlags.EpsilonBit;
        var soA = new SerializedObject(tA);
        var soB = new SerializedObject(tB);
        var effRebind = new EnumFlagsField();
        m_Root.Add(effRebind);
        effRebind.BindProperty(soA.FindProperty("basic"));
        Frames();
        Put("eff.rebind.initialA", FmtEffShort(effRebind));

        effRebind.BindProperty(soB.FindProperty("basic"));
        Frames();
        Put("eff.rebind.afterB", FmtEffShort(effRebind));
        effRebind.value = BasicFlags.ReadWrite;
        Frames();
        Put("eff.rebind.editB", FmtFlags(tA.basic) + "|" + FmtFlags(tB.basic));

        effRebind.BindProperty(soB.FindProperty("sparse"));
        Frames();
        Put("eff.rebind.diffType", FmtEff(effRebind));
        effRebind.value = SparseFlags.GammaBit;
        Frames();
        Put("eff.rebind.diffTypeEdit", FmtFlags(tB.sparse) + "|" + FmtEffShort(effRebind));

        effRebind.Unbind();
        tB.sparse = SparseFlags.AlphaBit;
        Frames();
        Put("eff.rebind.afterUnbind", FmtEffShort(effRebind));

        // 7. Multi-object mixed state on EnumFlagsField (new value + target[0] existing value).
        var m1 = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        var m2 = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        m1.basic = BasicFlags.ReadAccess;
        m2.basic = BasicFlags.Execute;
        var soMulti1 = new SerializedObject(new UnityEngine.Object[] { m1, m2 });
        var effMulti1 = new EnumFlagsField();
        var multiEvts1 = new List<string>();
        effMulti1.RegisterValueChangedCallback(e => multiEvts1.Add(FmtFlags(e.newValue)));
        m_Root.Add(effMulti1);
        effMulti1.BindProperty(soMulti1.FindProperty("basic"));
        Frames();
        Put("eff.mixed.initial", FmtEffShort(effMulti1) + "|" + multiEvts1.Count.ToString(Inv));

        effMulti1.value = BasicFlags.ReadWrite;
        Frames();
        Put("eff.mixed.uiNew", FmtEffShort(effMulti1) + "|" + FmtFlags(m1.basic) + "," + FmtFlags(m2.basic));

        var m3 = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        var m4 = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        m3.basic = BasicFlags.ReadAccess;
        m4.basic = BasicFlags.Execute;
        var soMulti2 = new SerializedObject(new UnityEngine.Object[] { m3, m4 });
        var effMulti2 = new EnumFlagsField();
        m_Root.Add(effMulti2);
        effMulti2.BindProperty(soMulti2.FindProperty("basic"));
        Frames();
        effMulti2.value = BasicFlags.ReadAccess; // matches m3's existing value
        Frames();
        Put("eff.mixed.uiPrimary", FmtEffShort(effMulti2) + "|" + FmtFlags(m3.basic) + "," + FmtFlags(m4.basic));

        // 8. PropertyField generated EnumFlagsField (creation, sync, callback, dynamic label, mixed, rebind).
        var tPf = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        tPf.basic = BasicFlags.WriteAccess;
        tPf.sparse = SparseFlags.GammaBit;
        var soPf = new SerializedObject(tPf);
        var pfBasic = new PropertyField(soPf.FindProperty("basic"));
        var pfSparse = new PropertyField(soPf.FindProperty("sparse"));
        var pfLabeled = new PropertyField(soPf.FindProperty("basic"), "Custom Flags");
        var pfEvts = new List<string>();
        pfBasic.RegisterValueChangeCallback(e => pfEvts.Add(e.changedProperty.intValue.ToString(Inv)));
        m_Root.Add(pfBasic);
        m_Root.Add(pfSparse);
        m_Root.Add(pfLabeled);
        pfBasic.Bind(soPf);
        pfSparse.Bind(soPf);
        pfLabeled.Bind(soPf);
        Frames();
        Put("pf.basic.initial", DescribePf(pfBasic) + "|evts=" + pfEvts.Count.ToString(Inv));
        Put("pf.sparse.initial", DescribePf(pfSparse));
        Put("pf.labeled.initial", DescribePf(pfLabeled));

        var pfBasicChild = (EnumFlagsField)pfBasic.hierarchy[0];
        pfBasicChild.value = BasicFlags.ReadWrite;
        Frames();
        Put("pf.basic.ui_to_so", DescribePf(pfBasic) + "|" + FmtFlags(tPf.basic) + "|evts=" + string.Join(";", pfEvts));

        soPf.FindProperty("basic").intValue = -1;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.basic.so_to_ui", DescribePf(pfBasic) + "|" + FmtFlags(tPf.basic) + "|evts=" + string.Join(";", pfEvts));

        pfLabeled.label = "Updated Flags";
        Frames();
        Put("pf.labeled.dynamic", DescribePf(pfLabeled));

        // PropertyField mixed multi-object state.
        var pm1 = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        var pm2 = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        pm1.basic = BasicFlags.ReadAccess;
        pm2.basic = BasicFlags.Execute;
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pm1, pm2 });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("basic"));
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(e.changedProperty.intValue.ToString(Inv)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.mixed.initial", DescribePf(pfMulti) + "|evts=" + pfMultiEvts.Count.ToString(Inv));

        var pfMultiChild = (EnumFlagsField)pfMulti.hierarchy[0];
        pfMultiChild.value = BasicFlags.ReadWrite;
        Frames();
        Put("pf.mixed.afterUiEdit", DescribePf(pfMulti) + "|" + FmtFlags(pm1.basic) + "," + FmtFlags(pm2.basic) + "|evts=" + string.Join(";", pfMultiEvts));

        // PropertyField rebind: Flags -> Flags (reuse), -> Plain enum (replace with PopupField), -> Flags, -> scalar int.
        var pfRebind = new PropertyField(soPf.FindProperty("basic"));
        m_Root.Add(pfRebind);
        pfRebind.Bind(soPf);
        Frames();
        var c1 = pfRebind.hierarchy[0];
        pfRebind.bindingPath = "sparse";
        pfRebind.Bind(soPf);
        Frames();
        var c2 = pfRebind.hierarchy[0];
        Put("pf.rebind.flagsToFlags", FmtBool(ReferenceEquals(c1, c2)) + "|" + DescribePf(pfRebind));

        pfRebind.bindingPath = "plain";
        pfRebind.Bind(soPf);
        Frames();
        var c3 = pfRebind.hierarchy[0];
        Put("pf.rebind.flagsToPlain", FmtBool(ReferenceEquals(c2, c3)) + "|" + DescribePf(pfRebind));

        pfRebind.bindingPath = "basic";
        pfRebind.Bind(soPf);
        Frames();
        var c4 = pfRebind.hierarchy[0];
        Put("pf.rebind.plainToFlags", FmtBool(ReferenceEquals(c3, c4)) + "|" + DescribePf(pfRebind));

        pfRebind.bindingPath = "scalarInt";
        pfRebind.Bind(soPf);
        Frames();
        var c5 = pfRebind.hierarchy[0];
        Put("pf.rebind.flagsToScalar", FmtBool(ReferenceEquals(c4, c5)) + "|" + DescribePf(pfRebind));

        // 9. Coexistence with runtime DataBinding on EnumFlagsField across BindProperty and Unbind.
        var tCo = ScriptableObject.CreateInstance<EnumFlagsTarget>();
        tCo.basic = BasicFlags.ReadAccess;
        var soCo = new SerializedObject(tCo);
        var rtSrc = new EnumFlagsRuntimeSource();
        var coBox = new VisualElement { dataSource = rtSrc };
        var effCo = new EnumFlagsField();
        effCo.SetBinding("tooltip", new DataBinding { dataSourcePath = PropertyPath.FromName("tip"), bindingMode = BindingMode.ToTarget });
        coBox.Add(effCo);
        m_Root.Add(coBox);
        effCo.BindProperty(soCo.FindProperty("basic"));
        Frames();
        Put("coexist.beforeUnbind", FmtEffShort(effCo) + "|" + effCo.tooltip);

        tCo.basic = BasicFlags.Execute;
        rtSrc.tip = "rt-flags-tip-1";
        Frames();
        Put("coexist.afterBothUpdate", FmtEffShort(effCo) + "|" + effCo.tooltip);

        effCo.Unbind();
        tCo.basic = BasicFlags.ReadWrite;
        rtSrc.tip = "rt-flags-tip-2";
        Frames();
        Put("coexist.afterUnbind", FmtEffShort(effCo) + "|" + effCo.tooltip);
#endif
    }
}
