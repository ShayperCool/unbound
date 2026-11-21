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

public enum SequentialMode
{
    Alpha = 0,
    BetaValue = 1,
    [InspectorName("Gamma Custom")]
    Gamma = 2
}

public enum SparseNonzeroMode
{
    TenStep = 10,
    [InspectorName("Mid Twenty Five")]
    TwentyFive = 25,
    HundredMax = 100
}

public enum NegativeSignedMode
{
    MinusTen = -10,
    ZeroPoint = 0,
    PlusFifty = 50,
    [Obsolete("Legacy option")]
    OldOption = 75
}

public enum ByteBackedMode : byte
{
    Low = 2,
    MediumValue = 128,
    HighMax = 255
}

public enum AliasMode
{
    Primary = 5,
    AliasOfPrimary = 5,
    Distinct = 20
}

[InspectorOrder(InspectorSort.ByName, InspectorSortDirection.Descending)]
public enum OrderedMode
{
    Alpha = 1,
    Charlie = 2,
    Bravo = 3
}

[Serializable]
public struct EnumNestedData
{
    public SequentialMode mode;
    public SparseNonzeroMode nestedSparse;
}

public class EnumBindingTarget : ScriptableObject
{
    public SequentialMode sequential = SequentialMode.BetaValue;
    public SparseNonzeroMode sparse = SparseNonzeroMode.TwentyFive;
    public SparseNonzeroMode sparseUninit = (SparseNonzeroMode)0;
    public NegativeSignedMode negative = NegativeSignedMode.MinusTen;
    public ByteBackedMode byteMode = ByteBackedMode.Low;
    public AliasMode alias = AliasMode.Primary;
    public OrderedMode ordered = OrderedMode.Alpha;
    public EnumNestedData nested = new EnumNestedData
    {
        mode = SequentialMode.Gamma,
        nestedSparse = SparseNonzeroMode.HundredMax
    };
    public int scalarInt = 42;
}

public class EnumRuntimeSource
{
    public string tip = "rt-enum-tip-0";
}

public class EnumBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENUM_BINDING_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<EnumBindingProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("ENUM_BINDING_OUT");
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
    void Put(string key, int value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value.ToString(Inv)).Append("}\n");
    void Put(string key, bool value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value ? "true" : "false").Append("}\n");

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

    static string FmtEnum(Enum e)
    {
        if (e == null) return "null";
        // For duplicate-value aliases (e.g. AliasMode where two names share value 5), Enum.ToString()
        // tie-breaking is runtime-unspecified; record type + exact numeric value instead.
        if (e is AliasMode)
            return e.GetType().Name + "(" + Convert.ToInt64(e, Inv).ToString(Inv) + ")";
        return e.GetType().Name + "." + e + "(" + Convert.ToInt64(e, Inv).ToString(Inv) + ")";
    }

    static string FmtEf(EnumField f) =>
        FmtEnum(f.value) + "|" + (f.text ?? "null") + "|" + FmtBool(f.showMixedValue);

    static string FmtPopup(PopupField<string> p) =>
        p.index.ToString(Inv) + "|" + (p.value ?? "null") + "|" + (p.text ?? "null") + "|" +
        FmtBool(p.showMixedValue) + "|[" + string.Join(",", p.choices ?? new List<string>()) + "]";

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is PopupField<string> p)
        {
            bool hasLabelCls = p.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = p.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = p.ClassListContains(BaseField<string>.alignedFieldUssClassName);
            return "PopupField|" + p.name + "|" + (p.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + "|" + FmtPopup(p);
        }
        if (child is IntegerField i)
            return "IntegerField|" + i.name + "|" + (i.label ?? "null") + "|" + i.value.ToString(Inv);
        return child.GetType().Name + "|" + child.name;
    }

    void Run()
    {
#if UNITY_EDITOR
        var document = GetComponent<UIDocument>();
        m_Root = document.rootVisualElement;
        Frames(1);

        // 1. Direct EnumField.BindProperty on sparse/nonzero enum (no pre-Init), both directions.
        var t1 = ScriptableObject.CreateInstance<EnumBindingTarget>();
        var so1 = new SerializedObject(t1);
        var efSparse = new EnumField();
        var sparseEvents = new List<string>();
        efSparse.RegisterValueChangedCallback(e => sparseEvents.Add(FmtEnum(e.previousValue) + "->" + FmtEnum(e.newValue)));
        m_Root.Add(efSparse);
        efSparse.BindProperty(so1.FindProperty("sparse"));
        Frames();
        Put("ef.sparse.initial", FmtEf(efSparse) + "|" + sparseEvents.Count.ToString(Inv));

        efSparse.value = SparseNonzeroMode.HundredMax;
        Frames();
        Put("ef.sparse.ui_to_so", FmtEf(efSparse) + "|" + FmtEnum(t1.sparse) + "|" +
            so1.FindProperty("sparse").intValue.ToString(Inv) + "|" +
            so1.FindProperty("sparse").enumValueIndex.ToString(Inv) + "|" +
            sparseEvents.Count.ToString(Inv));

        so1.FindProperty("sparse").intValue = 10;
        so1.ApplyModifiedProperties();
        Frames();
        Put("ef.sparse.so_to_ui", FmtEf(efSparse) + "|" + FmtEnum(t1.sparse) + "|" +
            sparseEvents.Count.ToString(Inv));

        // 2. Uninitialized zero backing value on nonzero-starting enum.
        var efUninit = new EnumField();
        m_Root.Add(efUninit);
        efUninit.BindProperty(so1.FindProperty("sparseUninit"));
        Frames();
        Put("ef.sparse.uninit_zero", FmtEf(efUninit) + "|" + ((int)t1.sparseUninit).ToString(Inv));

        // 3. Negative signed enum (with [Obsolete] member) roundtrip.
        var efNeg = new EnumField();
        m_Root.Add(efNeg);
        efNeg.BindProperty(so1.FindProperty("negative"));
        Frames();
        Put("ef.negative.initial", FmtEf(efNeg));
        efNeg.value = NegativeSignedMode.PlusFifty;
        Frames();
        Put("ef.negative.ui_to_so", FmtEf(efNeg) + "|" + FmtEnum(t1.negative));
        so1.FindProperty("negative").intValue = 0;
        so1.ApplyModifiedProperties();
        Frames();
        Put("ef.negative.so_zero", FmtEf(efNeg));
        so1.FindProperty("negative").intValue = -10;
        so1.ApplyModifiedProperties();
        Frames();
        Put("ef.negative.so_minus", FmtEf(efNeg));

        // 4. Byte-backed enum including 255 (unsigned max) roundtrip.
        var efByte = new EnumField();
        m_Root.Add(efByte);
        efByte.BindProperty(so1.FindProperty("byteMode"));
        Frames();
        Put("ef.byte.initial", FmtEf(efByte));
        efByte.value = ByteBackedMode.HighMax;
        Frames();
        Put("ef.byte.ui_255", FmtEf(efByte) + "|" + ((int)t1.byteMode).ToString(Inv) + "|" +
            so1.FindProperty("byteMode").intValue.ToString(Inv));
        so1.FindProperty("byteMode").intValue = 128;
        so1.ApplyModifiedProperties();
        Frames();
        Put("ef.byte.so_128", FmtEf(efByte));

        // 5. Alias (duplicate value) and InspectorOrder enums on EnumField.
        var efAlias = new EnumField();
        var efOrdered = new EnumField();
        m_Root.Add(efAlias);
        m_Root.Add(efOrdered);
        efAlias.BindProperty(so1.FindProperty("alias"));
        efOrdered.BindProperty(so1.FindProperty("ordered"));
        Frames();
        Put("ef.alias.initial", FmtEf(efAlias));
        efAlias.value = AliasMode.Distinct;
        Frames();
        Put("ef.alias.ui_distinct", FmtEf(efAlias) + "|" + FmtEnum(t1.alias));
        so1.FindProperty("alias").intValue = 5;
        so1.ApplyModifiedProperties();
        Frames();
        Put("ef.alias.so_5", FmtEf(efAlias));

        Put("ef.ordered.initial", FmtEf(efOrdered));
        efOrdered.value = OrderedMode.Charlie;
        Frames();
        Put("ef.ordered.ui_charlie", FmtEf(efOrdered) + "|" + FmtEnum(t1.ordered));

        // 6. Detached EnumField.BindProperty before attaching to panel.
        var tDet = ScriptableObject.CreateInstance<EnumBindingTarget>();
        var soDet = new SerializedObject(tDet);
        var efDet = new EnumField();
        efDet.BindProperty(soDet.FindProperty("sparse"));
        Put("ef.detached.beforeAttach", FmtEf(efDet));
        m_Root.Add(efDet);
        Frames();
        Put("ef.detached.afterAttach", FmtEf(efDet));
        efDet.value = SparseNonzeroMode.TenStep;
        Frames();
        Put("ef.detached.afterUiEdit", FmtEf(efDet) + "|" + FmtEnum(tDet.sparse));

        // 7. Tree Bind(SerializedObject): EnumField, nested struct EnumField, and direct PopupField<string>.
        var tTree = ScriptableObject.CreateInstance<EnumBindingTarget>();
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var treeEf = new EnumField { bindingPath = "sparse" };
        var nestedBox = new BindableElement { bindingPath = "nested" };
        var nestedEf = new EnumField { bindingPath = "mode" };
        nestedBox.Add(nestedEf);
        var origChoices = new List<string> { "OrigA", "OrigB" };
        var directPopup = new PopupField<string>(origChoices, 1) { bindingPath = "negative" };
        treeRoot.Add(treeEf);
        treeRoot.Add(nestedBox);
        treeRoot.Add(directPopup);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial", FmtEf(treeEf) + " || " + FmtEf(nestedEf) + " || " + FmtPopup(directPopup));

        nestedEf.value = SequentialMode.Alpha;
        directPopup.index = 2; // PlusFifty
        Frames();
        Put("tree.ui_to_so", FmtEnum(tTree.nested.mode) + "|" + FmtEnum(tTree.negative) + "|" + FmtPopup(directPopup));

        tTree.nested.mode = SequentialMode.BetaValue;
        tTree.negative = NegativeSignedMode.ZeroPoint;
        Frames();
        Put("tree.so_to_ui", FmtEf(nestedEf) + " || " + FmtPopup(directPopup));

        // Direct PopupField Unbind restores original choices and index.
        treeRoot.Unbind();
        tTree.sparse = SparseNonzeroMode.TenStep;
        tTree.negative = NegativeSignedMode.MinusTen;
        Frames();
        Put("tree.afterUnbind", FmtEf(treeEf) + " || " + FmtPopup(directPopup));

        // 8. Rebind EnumField: same type (targetA -> targetB) and different enum type, then Unbind.
        var tA = ScriptableObject.CreateInstance<EnumBindingTarget>();
        tA.sparse = SparseNonzeroMode.TenStep;
        var tB = ScriptableObject.CreateInstance<EnumBindingTarget>();
        tB.sparse = SparseNonzeroMode.HundredMax;
        tB.negative = NegativeSignedMode.PlusFifty;
        var soA = new SerializedObject(tA);
        var soB = new SerializedObject(tB);
        var efRebind = new EnumField();
        m_Root.Add(efRebind);
        efRebind.BindProperty(soA.FindProperty("sparse"));
        Frames();
        Put("ef.rebind.initialA", FmtEf(efRebind));

        efRebind.BindProperty(soB.FindProperty("sparse"));
        Frames();
        Put("ef.rebind.afterB", FmtEf(efRebind));
        efRebind.value = SparseNonzeroMode.TwentyFive;
        Frames();
        Put("ef.rebind.editB", FmtEnum(tA.sparse) + "|" + FmtEnum(tB.sparse));

        efRebind.BindProperty(soB.FindProperty("negative"));
        Frames();
        Put("ef.rebind.diffType", FmtEf(efRebind));
        efRebind.value = NegativeSignedMode.MinusTen;
        Frames();
        Put("ef.rebind.diffTypeEdit", FmtEnum(tB.negative) + "|" + FmtEf(efRebind));

        efRebind.Unbind();
        tB.negative = NegativeSignedMode.ZeroPoint;
        Frames();
        Put("ef.rebind.afterUnbind", FmtEf(efRebind));

        // 9. Multi-object mixed state on EnumField (converge externally, diverge, UI write new, UI write target[0] value).
        var m1 = ScriptableObject.CreateInstance<EnumBindingTarget>();
        var m2 = ScriptableObject.CreateInstance<EnumBindingTarget>();
        m1.sparse = SparseNonzeroMode.TenStep;
        m2.sparse = SparseNonzeroMode.HundredMax;
        var soMulti = new SerializedObject(new UnityEngine.Object[] { m1, m2 });
        var efMulti = new EnumField();
        var multiEvents = new List<string>();
        efMulti.RegisterValueChangedCallback(e => multiEvents.Add(FmtEnum(e.previousValue) + "->" + FmtEnum(e.newValue)));
        m_Root.Add(efMulti);
        efMulti.BindProperty(soMulti.FindProperty("sparse"));
        Frames();
        Put("ef.mixed.initial", FmtEf(efMulti) + "|" + multiEvents.Count.ToString(Inv));

        m2.sparse = SparseNonzeroMode.TenStep;
        soMulti.Update();
        Frames();
        Put("ef.mixed.converged", FmtEf(efMulti) + "|" + multiEvents.Count.ToString(Inv));

        m2.sparse = SparseNonzeroMode.TwentyFive;
        soMulti.Update();
        Frames();
        Put("ef.mixed.rediverged", FmtEf(efMulti));

        efMulti.value = SparseNonzeroMode.HundredMax;
        Frames();
        Put("ef.mixed.uiNew", FmtEf(efMulti) + "|" + FmtEnum(m1.sparse) + "," + FmtEnum(m2.sparse));

        m2.sparse = SparseNonzeroMode.TenStep;
        soMulti.Update();
        Frames();
        Put("ef.mixed.beforePrimaryWrite", FmtEf(efMulti));
        efMulti.value = SparseNonzeroMode.HundredMax; // matches m1's existing value
        Frames();
        Put("ef.mixed.afterPrimaryWrite", FmtEf(efMulti) + "|" + FmtEnum(m1.sparse) + "," + FmtEnum(m2.sparse));

        // 10. PropertyField generated enum controls (PopupField<string>), choices, labels, sync, callbacks, mixed, rebind.
        var tPf = ScriptableObject.CreateInstance<EnumBindingTarget>();
        var soPf = new SerializedObject(tPf);
        var pfSparse = new PropertyField(soPf.FindProperty("sparse"));
        var pfNeg = new PropertyField(soPf.FindProperty("negative"));
        var pfOrd = new PropertyField(soPf.FindProperty("ordered"));
        var pfAlias = new PropertyField(soPf.FindProperty("alias"));
        var pfLabeled = new PropertyField(soPf.FindProperty("sequential"), "Custom Enum");
        var pfSparseEvts = new List<string>();
        pfSparse.RegisterValueChangeCallback(e => pfSparseEvts.Add(e.changedProperty.intValue.ToString(Inv) + ":" + e.changedProperty.enumValueIndex.ToString(Inv)));
        m_Root.Add(pfSparse);
        m_Root.Add(pfNeg);
        m_Root.Add(pfOrd);
        m_Root.Add(pfAlias);
        m_Root.Add(pfLabeled);
        pfSparse.Bind(soPf);
        pfNeg.Bind(soPf);
        pfOrd.Bind(soPf);
        pfAlias.Bind(soPf);
        pfLabeled.Bind(soPf);
        Frames();
        Put("pf.sparse.initial", DescribePf(pfSparse) + "|evts=" + pfSparseEvts.Count.ToString(Inv));
        Put("pf.negative.initial", DescribePf(pfNeg));
        Put("pf.ordered.initial", DescribePf(pfOrd));
        Put("pf.alias.initial", DescribePf(pfAlias));
        Put("pf.labeled.initial", DescribePf(pfLabeled));

        var pfSparsePopup = (PopupField<string>)pfSparse.hierarchy[0];
        pfSparsePopup.index = 2; // HundredMax
        Frames();
        Put("pf.sparse.ui_to_so", DescribePf(pfSparse) + "|" + FmtEnum(tPf.sparse) + "|evts=" + string.Join(";", pfSparseEvts));

        soPf.FindProperty("sparse").intValue = 10;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.sparse.so_to_ui", DescribePf(pfSparse) + "|" + FmtEnum(tPf.sparse) + "|evts=" + string.Join(";", pfSparseEvts));

        var pfOrdPopup = (PopupField<string>)pfOrd.hierarchy[0];
        pfOrdPopup.index = 0; // Charlie (descending by name: Charlie, Bravo, Alpha)
        Frames();
        Put("pf.ordered.ui_to_so", DescribePf(pfOrd) + "|" + FmtEnum(tPf.ordered));

        var pfAliasPopup = (PopupField<string>)pfAlias.hierarchy[0];
        pfAliasPopup.index = 2; // Distinct
        Frames();
        Put("pf.alias.ui_to_so", DescribePf(pfAlias) + "|" + FmtEnum(tPf.alias));

        // Dynamic label update on PropertyField.
        pfLabeled.label = "Updated Enum";
        Frames();
        Put("pf.labeled.dynamic", DescribePf(pfLabeled));

        // PropertyField mixed multi-object state.
        var pm1 = ScriptableObject.CreateInstance<EnumBindingTarget>();
        var pm2 = ScriptableObject.CreateInstance<EnumBindingTarget>();
        pm1.sparse = SparseNonzeroMode.TenStep;
        pm2.sparse = SparseNonzeroMode.HundredMax;
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pm1, pm2 });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("sparse"));
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(e.changedProperty.intValue.ToString(Inv)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.mixed.initial", DescribePf(pfMulti) + "|evts=" + pfMultiEvts.Count.ToString(Inv));

        var pfMultiPopup = (PopupField<string>)pfMulti.hierarchy[0];
        pfMultiPopup.value = "Mid Twenty Five";
        Frames();
        Put("pf.mixed.afterUiEdit", DescribePf(pfMulti) + "|" + FmtEnum(pm1.sparse) + "," + FmtEnum(pm2.sparse) + "|evts=" + string.Join(";", pfMultiEvts));

        pm2.sparse = SparseNonzeroMode.TenStep;
        soPfMulti.Update();
        Frames();
        Put("pf.mixed.rediverged", DescribePf(pfMulti));
        pm2.sparse = SparseNonzeroMode.TwentyFive;
        soPfMulti.Update();
        Frames();
        Put("pf.mixed.converged", DescribePf(pfMulti));

        // PropertyField rebind: enum -> another enum (reuse PopupField), -> scalar int (replace), -> enum (recreate).
        var pfRebind = new PropertyField(soPf.FindProperty("sparse"));
        m_Root.Add(pfRebind);
        pfRebind.Bind(soPf);
        Frames();
        var child1 = pfRebind.hierarchy[0];
        pfRebind.bindingPath = "negative";
        pfRebind.Bind(soPf);
        Frames();
        var child2 = pfRebind.hierarchy[0];
        Put("pf.rebind.enumToEnum", FmtBool(ReferenceEquals(child1, child2)) + "|" + DescribePf(pfRebind));

        pfRebind.bindingPath = "scalarInt";
        pfRebind.Bind(soPf);
        Frames();
        var child3 = pfRebind.hierarchy[0];
        Put("pf.rebind.enumToScalar", FmtBool(ReferenceEquals(child2, child3)) + "|" + DescribePf(pfRebind));

        pfRebind.bindingPath = "sparse";
        pfRebind.Bind(soPf);
        Frames();
        var child4 = pfRebind.hierarchy[0];
        Put("pf.rebind.scalarToEnum", FmtBool(ReferenceEquals(child3, child4)) + "|" + DescribePf(pfRebind));

        // 11. Coexistence with runtime DataBinding on EnumField across BindProperty and Unbind.
        var tCo = ScriptableObject.CreateInstance<EnumBindingTarget>();
        var soCo = new SerializedObject(tCo);
        var rtSrc = new EnumRuntimeSource();
        var coBox = new VisualElement { dataSource = rtSrc };
        var efCo = new EnumField();
        efCo.SetBinding("tooltip", new DataBinding { dataSourcePath = PropertyPath.FromName("tip"), bindingMode = BindingMode.ToTarget });
        coBox.Add(efCo);
        m_Root.Add(coBox);
        efCo.BindProperty(soCo.FindProperty("sparse"));
        Frames();
        Put("coexist.beforeUnbind", FmtEf(efCo) + "|" + efCo.tooltip);

        tCo.sparse = SparseNonzeroMode.TenStep;
        rtSrc.tip = "rt-enum-tip-1";
        Frames();
        Put("coexist.afterBothUpdate", FmtEf(efCo) + "|" + efCo.tooltip);

        efCo.Unbind();
        tCo.sparse = SparseNonzeroMode.HundredMax;
        rtSrc.tip = "rt-enum-tip-2";
        Frames();
        Put("coexist.afterUnbind", FmtEf(efCo) + "|" + efCo.tooltip);
#endif
    }
}
