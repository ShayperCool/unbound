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

[Serializable]
public struct DeepHashData
{
    public Hash128 digest;
}

[Serializable]
public struct NestedHashData
{
    public Hash128 digest;
    public DeepHashData deep;
}

public class HashProbeRefAsset : ScriptableObject
{
    public int code;
}

public class Hash128Target : ScriptableObject
{
    public Hash128 zeroHash;
    public Hash128 myHash = Hash128.Parse("0123456789abcdef1122334455667788");
    public Hash128 otherHash = Hash128.Parse("fedcba98765432108877665544332211");
    public Hash128 multiHash = Hash128.Parse("aaaaaaaaaaaaaaaa1111111111111111");
    public NestedHashData nested = new NestedHashData
    {
        digest = Hash128.Parse("11112222333344445555666677778888"),
        deep = new DeepHashData
        {
            digest = Hash128.Parse("9999aaaabbbbccccddddeeeeffff0000")
        }
    };
    public Hash128[] hashArray = new[]
    {
        Hash128.Parse("10000000000000000000000000000001"),
        Hash128.Parse("20000000000000000000000000000002"),
        Hash128.Parse("30000000000000000000000000000003")
    };

    // Sibling controls for tree Bind and PropertyField cross-type rebinding.
    public HashProbeRefAsset siblingRef;
    public Gradient siblingGradient = new Gradient();
    public AnimationCurve siblingCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public BoundsInt siblingBoundsInt = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3 siblingVec3 = new Vector3(1f, 2f, 3f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int siblingInt = 42;
}

public class HashRuntimeSource
{
    [CreateProperty]
    public string tip { get; set; } = "rt-hash-tip-0";
}

public class Hash128BindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Hash128BindingProbe");
        var doc = go.AddComponent<UIDocument>();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        doc.panelSettings = ps;
        go.AddComponent<Hash128BindingProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("HASH128_BINDING_OUT");
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
    static string FmtVec3(Vector3 v) => "(" + FmtFloat(v.x) + "," + FmtFloat(v.y) + "," + FmtFloat(v.z) + ")";
    static string FmtVec3Int(Vector3Int v) => "(" + FmtInt(v.x) + "," + FmtInt(v.y) + "," + FmtInt(v.z) + ")";
    static string FmtBoundsInt(BoundsInt b) => "[" + FmtVec3Int(b.position) + ";" + FmtVec3Int(b.size) + "]";
    static string FmtColor(Color c) => "(" + FmtFloat(c.r) + "," + FmtFloat(c.g) + "," + FmtFloat(c.b) + "," + FmtFloat(c.a) + ")";
    static string FmtCurveShort(AnimationCurve c) =>
        c == null ? "null" : ((int)c.preWrapMode).ToString(Inv) + "/" + ((int)c.postWrapMode).ToString(Inv) + "[" + FmtInt(c.length) + "]";
    static string FmtGradShort(Gradient g) =>
        g == null ? "null" : ((int)g.mode).ToString(Inv) + "/" + ((int)g.colorSpace).ToString(Inv) + "[" + FmtInt(g.colorKeys.Length) + "," + FmtInt(g.alphaKeys.Length) + "]";

#if UNITY_EDITOR
    static string FmtHashField(Hash128Field hf)
    {
        bool hasMixedCls = hf.ClassListContains(BaseField<Hash128>.mixedValueLabelUssClassName);
        return hf.value.ToString() + "|txt=" + (hf.text ?? "null") + "|m=" + FmtBool(hf.showMixedValue) + "|mc=" + FmtBool(hasMixedCls);
    }

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is Hash128Field hf)
        {
            bool hasLabelCls = hf.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = hf.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = hf.ClassListContains(BaseField<Hash128>.alignedFieldUssClassName);
            bool hasHashCls = hf.ClassListContains(Hash128Field.ussClassName);
            return "Hash128Field|" + hf.name + "|" + (hf.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasHashCls) + "|" +
                   FmtHashField(hf);
        }
        if (child is ObjectField of)
            return "ObjectField|" + of.name + "|" + (of.label ?? "null") + "|" + (of.value != null ? of.value.name : "null");
        if (child is GradientField gf)
            return "GradientField|" + gf.name + "|" + (gf.label ?? "null") + "|" + FmtGradShort(gf.value);
        if (child is CurveField cf)
            return "CurveField|" + cf.name + "|" + (cf.label ?? "null") + "|" + FmtCurveShort(cf.value);
        if (child is BoundsIntField bif)
            return "BoundsIntField|" + bif.name + "|" + (bif.label ?? "null") + "|" + FmtBoundsInt(bif.value);
        if (child is ColorField col)
            return "ColorField|" + col.name + "|" + (col.label ?? "null") + "|" + FmtColor(col.value);
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

        var hZero = default(Hash128);
        var hA = Hash128.Parse("0123456789abcdef1122334455667788");
        var hB = Hash128.Parse("fedcba98765432108877665544332211");
        var hC = Hash128.Compute("unbound-hash128-control");
        var hLowOnly = new Hash128(0x1122334455667788UL, 0x99aabbccddeeff00UL);
        var hHighDiff = new Hash128(0x1122334455667788UL, 0x0011223344556677UL);

        var refAsset = ScriptableObject.CreateInstance<HashProbeRefAsset>();
        refAsset.name = "HashRefA";

        // 0. Standalone Hash128Field: defaults, value/text round-trip, repeat no-op, showMixedValue + SetValueWithoutNotify.
        var hfStd = new Hash128Field("My Hash");
        var stdEvts = new List<string>();
        hfStd.RegisterValueChangedCallback(e => stdEvts.Add(e.previousValue + "->" + e.newValue));
        m_Root.Add(hfStd);
        Frames(1);
        Put("standalone.defaults",
            (hfStd.label ?? "null") + "|" +
            FmtBool(hfStd.ClassListContains(Hash128Field.ussClassName)) +
            FmtBool(hfStd.labelElement.ClassListContains(Hash128Field.labelUssClassName)) +
            FmtBool(hfStd.Q(className: Hash128Field.inputUssClassName) != null) + "|" +
            FmtHashField(hfStd));

        hfStd.value = hA;
        string afterA = FmtHashField(hfStd);
        int evA = stdEvts.Count;
        hfStd.value = hA;
        int evRep = stdEvts.Count;
        hfStd.value = hZero;
        string afterZero = FmtHashField(hfStd);
        hfStd.value = hC;
        hfStd.showMixedValue = true;
        string mixedOn = FmtHashField(hfStd);
        hfStd.SetValueWithoutNotify(hB);
        string mixedAfterSetNoNotify = FmtHashField(hfStd);
        hfStd.showMixedValue = false;
        string mixedOff = FmtHashField(hfStd);
        Put("standalone.value_and_mixed",
            afterA + "|evA=" + FmtInt(evA) + "|evRep=" + FmtInt(evRep) + "||" +
            afterZero + "||" + mixedOn + "||" + mixedAfterSetNoNotify + "||" + mixedOff + "|evc=" + FmtInt(stdEvts.Count));

        // 1. Property metadata and direct Hash128Field.BindProperty.
        var tMain = ScriptableObject.CreateInstance<Hash128Target>();
        tMain.siblingRef = refAsset;
        var soMain = new SerializedObject(tMain);
        var pZero = soMain.FindProperty("zeroHash");
        var pMain = soMain.FindProperty("myHash");
        var pArr1 = soMain.FindProperty("hashArray.Array.data[1]");
        Put("prop.meta",
            "main=" + pMain.propertyType + "/" + pMain.type + "/" + FmtBool(pMain.hasVisibleChildren) + "/" + pMain.hash128Value +
            "|zero=" + pZero.hash128Value +
            "|arr1=" + pArr1.propertyType + "/" + pArr1.type + "/" + pArr1.hash128Value);

        var hfZero = new Hash128Field();
        var zeroEvts = new List<string>();
        hfZero.RegisterValueChangedCallback(e => zeroEvts.Add(e.previousValue + "->" + e.newValue));
        m_Root.Add(hfZero);
        hfZero.BindProperty(soMain.FindProperty("zeroHash"));
        Frames();
        Put("direct.zero_initial", FmtHashField(hfZero) + "|ev=" + string.Join("|", zeroEvts));
        hfZero.value = hZero;
        Frames();
        Put("direct.zero_noop", FmtHashField(hfZero) + "|evc=" + FmtInt(zeroEvts.Count));

        var hfMain = new Hash128Field();
        var mainEvts = new List<string>();
        hfMain.RegisterValueChangedCallback(e => mainEvts.Add(e.previousValue + "->" + e.newValue));
        m_Root.Add(hfMain);
        hfMain.BindProperty(soMain.FindProperty("myHash"));
        Frames();
        Put("direct.initial", FmtHashField(hfMain) + "|ev=" + string.Join("|", mainEvts));

        hfMain.value = hB;
        Frames();
        Put("direct.ui_to_so", FmtHashField(hfMain) + "|t=" + tMain.myHash + "|evc=" + FmtInt(mainEvts.Count));

        soMain.Update();
        soMain.FindProperty("myHash").hash128Value = hC;
        soMain.ApplyModifiedProperties();
        Frames();
        Put("direct.so_to_ui", FmtHashField(hfMain) + "|t=" + tMain.myHash + "|evc=" + FmtInt(mainEvts.Count));

        // Direct C# external mutation: low64+high64, then high64-only change.
        tMain.myHash = hLowOnly;
        Frames();
        string ext1 = FmtHashField(hfMain) + "|evc=" + FmtInt(mainEvts.Count);
        tMain.myHash = hHighDiff;
        Frames();
        string ext2 = FmtHashField(hfMain) + "|evc=" + FmtInt(mainEvts.Count);
        Put("direct.external_mutate", ext1 + "||" + ext2);

        // Detached-then-attached deferred binding.
        var hfDet = new Hash128Field();
        var detEvts = new List<string>();
        hfDet.RegisterValueChangedCallback(e => detEvts.Add(e.previousValue + "->" + e.newValue));
        hfDet.BindProperty(soMain.FindProperty("otherHash"));
        string detBefore = FmtHashField(hfDet) + "|evc=" + FmtInt(detEvts.Count);
        m_Root.Add(hfDet);
        Frames();
        Put("direct.detached_then_attached", detBefore + "||" + FmtHashField(hfDet) + "|ev=" + string.Join("|", detEvts));

        // 2. Multi-object mixed selection on Hash128Field.
        var mA = ScriptableObject.CreateInstance<Hash128Target>();
        var mB = ScriptableObject.CreateInstance<Hash128Target>();
        mA.multiHash = hA;
        mB.multiHash = hB;
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var hfMulti = new Hash128Field();
        var multiEvts = new List<string>();
        hfMulti.RegisterValueChangedCallback(e => multiEvts.Add(e.previousValue + "->" + e.newValue));
        m_Root.Add(hfMulti);
        hfMulti.BindProperty(soMulti.FindProperty("multiHash"));
        Frames();
        Put("multi.initial_divergent", FmtHashField(hfMulti) + "|evc=" + FmtInt(multiEvts.Count));

        hfMulti.value = hC;
        Frames();
        Put("multi.ui_edit_new", FmtHashField(hfMulti) + "|a=" + mA.multiHash + "|b=" + mB.multiHash + "|evc=" + FmtInt(multiEvts.Count));

        // Diverge with only high-64-bit difference, then edit to target[0]'s value and to zero.
        mA.multiHash = hLowOnly;
        mB.multiHash = hHighDiff;
        Frames();
        string highDiffMixed = FmtHashField(hfMulti);
        hfMulti.value = hLowOnly;
        Frames();
        string editToFirst = FmtHashField(hfMulti) + "|a=" + mA.multiHash + "|b=" + mB.multiHash;
        mA.multiHash = hA;
        mB.multiHash = hB;
        Frames();
        hfMulti.value = hZero;
        Frames();
        string editToZero = FmtHashField(hfMulti) + "|a=" + mA.multiHash + "|b=" + mB.multiHash;
        Put("multi.high_diff_and_edits", highDiffMixed + "||" + editToFirst + "||" + editToZero);

        // 3. Tree Bind(so): top-level, nested dotted, relative parent-child, array element, siblings.
        var tTree = ScriptableObject.CreateInstance<Hash128Target>();
        tTree.siblingRef = refAsset;
        var soTree = new SerializedObject(tTree);
        var treeRoot = new VisualElement();
        var trMain = new Hash128Field { bindingPath = "myHash" };
        var trZero = new Hash128Field { bindingPath = "zeroHash" };
        var trDeep = new Hash128Field { bindingPath = "nested.deep.digest" };
        var trParent = new BindableElement { bindingPath = "nested" };
        var trRel = new Hash128Field { bindingPath = "digest" };
        trParent.Add(trRel);
        var trArr1 = new Hash128Field { bindingPath = "hashArray.Array.data[1]" };
        var trObj = new ObjectField { bindingPath = "siblingRef", objectType = typeof(HashProbeRefAsset) };
        var trGrad = new GradientField { bindingPath = "siblingGradient" };
        var trCurve = new CurveField { bindingPath = "siblingCurve" };
        var trBInt = new BoundsIntField { bindingPath = "siblingBoundsInt" };
        var trInt = new IntegerField { bindingPath = "siblingInt" };
        treeRoot.Add(trMain);
        treeRoot.Add(trZero);
        treeRoot.Add(trDeep);
        treeRoot.Add(trParent);
        treeRoot.Add(trArr1);
        treeRoot.Add(trObj);
        treeRoot.Add(trGrad);
        treeRoot.Add(trCurve);
        treeRoot.Add(trBInt);
        treeRoot.Add(trInt);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);
        Frames();
        Put("tree.initial",
            "m=" + trMain.value + "|z=" + trZero.value + "|deep=" + trDeep.value +
            "|rel=" + trRel.value + "|arr1=" + trArr1.value +
            "|obj=" + (trObj.value != null ? trObj.value.name : "null") +
            "|g=" + FmtGradShort(trGrad.value) + "|c=" + FmtCurveShort(trCurve.value) +
            "|bi=" + FmtBoundsInt(trBInt.value) + "|i=" + FmtInt(trInt.value));

        trMain.value = hC;
        trDeep.value = hA;
        trRel.value = hB;
        trArr1.value = hHighDiff;
        trInt.value = 77;
        Frames();
        Put("tree.ui_to_so",
            "m=" + tTree.myHash + "|deep=" + tTree.nested.deep.digest +
            "|rel=" + tTree.nested.digest + "|arr1=" + tTree.hashArray[1] + "|i=" + FmtInt(tTree.siblingInt));

        tTree.myHash = hLowOnly;
        tTree.nested = new NestedHashData { digest = hC, deep = new DeepHashData { digest = hZero } };
        tTree.hashArray[1] = hA;
        Frames();
        Put("tree.so_to_ui",
            "m=" + trMain.value + "|deep=" + trDeep.value + "|rel=" + trRel.value + "|arr1=" + trArr1.value);

        // 4. PropertyField for Hash128: initial, labels, UI<->SO, same-type reuse, cross-type rebind, multi.
        var tPf = ScriptableObject.CreateInstance<Hash128Target>();
        tPf.siblingRef = refAsset;
        var soPf = new SerializedObject(tPf);

        var pfHash = new PropertyField(soPf.FindProperty("myHash"));
        var pfEvts = new List<string>();
        pfHash.RegisterValueChangeCallback(e => pfEvts.Add(e.changedProperty.hash128Value.ToString()));
        m_Root.Add(pfHash);
        pfHash.Bind(soPf);
        Frames();
        Put("pf.initial", DescribePf(pfHash) + "|ec=" + FmtInt(pfEvts.Count));

        var pfLbl = new PropertyField(soPf.FindProperty("otherHash"), "Custom Hash");
        m_Root.Add(pfLbl);
        pfLbl.Bind(soPf);
        Frames();
        string lblCustom = DescribePf(pfLbl);
        pfLbl.label = "Dynamic Hash";
        Frames();
        Put("pf.labels", lblCustom + "||" + DescribePf(pfLbl));

        pfHash.Q<Hash128Field>().value = hB;
        Frames();
        Put("pf.ui_to_so", DescribePf(pfHash) + "|t=" + tPf.myHash + "|ec=" + FmtInt(pfEvts.Count));

        soPf.Update();
        soPf.FindProperty("myHash").hash128Value = hC;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.so_to_ui", DescribePf(pfHash) + "|t=" + tPf.myHash + "|ec=" + FmtInt(pfEvts.Count));

        var origHf = pfHash.Q<Hash128Field>();
        pfHash.BindProperty(soPf.FindProperty("otherHash"));
        Frames();
        Put("pf.reuse_same_type", FmtBool(ReferenceEquals(origHf, pfHash.Q<Hash128Field>())) + "|" + DescribePf(pfHash));

        pfHash.BindProperty(soPf.FindProperty("siblingRef"));
        Frames();
        string xObj = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("siblingGradient"));
        Frames();
        string xGrad = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("siblingCurve"));
        Frames();
        string xCurve = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("siblingBoundsInt"));
        Frames();
        string xBInt = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("siblingColor"));
        Frames();
        string xCol = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("siblingVec3"));
        Frames();
        string xV3 = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("siblingVec4"));
        Frames();
        string xV4 = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("siblingInt"));
        Frames();
        string xInt = DescribePf(pfHash);
        pfHash.BindProperty(soPf.FindProperty("myHash"));
        Frames();
        string xBack = DescribePf(pfHash);
        Put("pf.rebind_cross_type",
            xObj + "||" + xGrad + "||" + xCurve + "||" + xBInt + "||" + xCol + "||" + xV3 + "||" + xV4 + "||" + xInt + "||" + xBack);

        var pmA = ScriptableObject.CreateInstance<Hash128Target>();
        var pmB = ScriptableObject.CreateInstance<Hash128Target>();
        pmA.multiHash = hA;
        pmB.multiHash = hB;
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiHash"), "Multi Hash");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(e.changedProperty.hash128Value.ToString()));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        pfMulti.Q<Hash128Field>().value = hC;
        Frames();
        Put("pf.multi.ui_edit", DescribePf(pfMulti) + "|a=" + pmA.multiHash + "|b=" + pmB.multiHash + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warning.
        var tRe1 = ScriptableObject.CreateInstance<Hash128Target>();
        var tRe2 = ScriptableObject.CreateInstance<Hash128Target>();
        tRe1.myHash = hA;
        tRe1.otherHash = hB;
        tRe2.myHash = hC;
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new HashRuntimeSource();
        var hfRe = new Hash128Field { dataSource = rtSource };
        hfRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(HashRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(hfRe);
        hfRe.BindProperty(soRe1.FindProperty("myHash"));
        Frames();
        Put("unbind_rebind.initial", FmtHashField(hfRe) + "|tip=" + (hfRe.tooltip ?? "null"));

        hfRe.Unbind();
        Frames();
        hfRe.value = hHighDiff;
        soRe1.FindProperty("myHash").hash128Value = hLowOnly;
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-hash-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtHashField(hfRe) + "|t1=" + tRe1.myHash + "|tip=" + (hfRe.tooltip ?? "null"));

        mA.multiHash = hA;
        mB.multiHash = hB;
        Frames();
        hfMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtHashField(hfMulti));

        hfRe.BindProperty(soRe1.FindProperty("otherHash"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtHashField(hfRe));
        hfRe.value = hC;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", tRe1.otherHash + "|t1_m=" + tRe1.myHash);

        hfRe.BindProperty(soRe2.FindProperty("myHash"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtHashField(hfRe));
        hfRe.value = hA;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", tRe2.myHash + "|t1_o=" + tRe1.otherHash);

        var pfRe = new PropertyField(soRe1.FindProperty("myHash"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("myHash").hash128Value = hB;
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badInt = new IntegerField();
        m_Root.Add(badInt);
        badInt.BindProperty(soRe1.FindProperty("myHash"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.int_field", FmtInt(badInt.value));
#endif
    }
}
