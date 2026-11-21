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
public struct PropFieldNested
{
    public int score;
    public string tag;
}

public class PropFieldTarget : ScriptableObject
{
    public bool flag = true;
    [Tooltip("Count tip")]
    public int count = 42;
    public int otherInt = 77;
    public int unrelated = 1;
    public long bigValue = 10000000000L;
    public uint u32 = 3000000000u;
    public ulong u64 = 12000000000000UL;
    public float ratio = 1.5f;
    public double precise = 3.25;
    public string title = "hello";
    public PropFieldNested nested = new PropFieldNested { score = 19, tag = "n0" };
}

public class PropFieldRuntimeSource
{
    public string tip = "rt-tip-0";
    public string msg = "rt-msg-0";
}

public class PropertyFieldProbe : MonoBehaviour
{
    const string BindingIdStr = "--unity-serialized-object-bindings";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROPERTY_FIELD_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<PropertyFieldProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("PROPERTY_FIELD_OUT");
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
    static string FmtFloat(float v) => v.ToString("R", Inv);
    static string FmtDouble(double v) => v.ToString("R", Inv);

    static string DescribeChild(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        string labelText = child is Label lbl ? lbl.text : "";
        if (child is Toggle t) labelText = t.label ?? "null";
        else if (child is IntegerField i) labelText = i.label ?? "null";
        else if (child is LongField l) labelText = l.label ?? "null";
        else if (child is UnsignedIntegerField ui) labelText = ui.label ?? "null";
        else if (child is UnsignedLongField ul) labelText = ul.label ?? "null";
        else if (child is FloatField f) labelText = f.label ?? "null";
        else if (child is DoubleField d) labelText = d.label ?? "null";
        else if (child is TextField s) labelText = s.label ?? "null";
        return child.GetType().Name + "|" + (child.name ?? "") + "|" + labelText;
    }

    void Run()
    {
#if UNITY_EDITOR
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        Frames();

        // 1. Constructors, default properties, USS classes.
        var tCtor = ScriptableObject.CreateInstance<PropFieldTarget>();
        var soCtor = new SerializedObject(tCtor);
        var pfEmpty = new PropertyField();
        var pfProp = new PropertyField(soCtor.FindProperty("count"));
        var pfCustom = new PropertyField(soCtor.FindProperty("count"), "Custom");
        var pfNullProp = new PropertyField(null, "NoProp");
        var pfEmptyLabel = new PropertyField(soCtor.FindProperty("count"), "");
        Put("ctor.empty", (pfEmpty.bindingPath ?? "null") + "|" + (pfEmpty.label ?? "null") + "|" + FmtBool(pfEmpty.ClassListContains(PropertyField.ussClassName)) + "|" + pfEmpty.hierarchy.childCount);
        Put("ctor.prop", (pfProp.bindingPath ?? "null") + "|" + (pfProp.label ?? "null") + "|" + pfProp.hierarchy.childCount);
        Put("ctor.custom", (pfCustom.bindingPath ?? "null") + "|" + (pfCustom.label ?? "null") + "|" + pfCustom.hierarchy.childCount);
        Put("ctor.nullProp", (pfNullProp.bindingPath ?? "null") + "|" + (pfNullProp.label ?? "null"));
        Put("ctor.emptyLabelIsEmpty", pfEmptyLabel.label == "");

        // 2. Scalar field creation, names, labels, USS classes, tooltip, initial values.
        var tSc = ScriptableObject.CreateInstance<PropFieldTarget>();
        var soSc = new SerializedObject(tSc);
        var scRoot = new VisualElement();
        var pfBool = new PropertyField(soSc.FindProperty("flag"));
        var pfInt = new PropertyField(soSc.FindProperty("count"));
        var pfLong = new PropertyField(soSc.FindProperty("bigValue"));
        var pfUInt = new PropertyField(soSc.FindProperty("u32"));
        var pfULong = new PropertyField(soSc.FindProperty("u64"));
        var pfFloat = new PropertyField(soSc.FindProperty("ratio"));
        var pfDouble = new PropertyField(soSc.FindProperty("precise"));
        var pfString = new PropertyField(soSc.FindProperty("title"));
        scRoot.Add(pfBool);
        scRoot.Add(pfInt);
        scRoot.Add(pfLong);
        scRoot.Add(pfUInt);
        scRoot.Add(pfULong);
        scRoot.Add(pfFloat);
        scRoot.Add(pfDouble);
        scRoot.Add(pfString);
        m_Root.Add(scRoot);

        scRoot.Bind(soSc);
        Frames();

        Put("creation.bool", DescribeChild(pfBool) + "|" + FmtBool(((Toggle)pfBool.hierarchy[0]).value));
        var intChild = (IntegerField)pfInt.hierarchy[0];
        Put("creation.int", DescribeChild(pfInt) + "|" + intChild.value.ToString(Inv) + "|" + FmtBool(intChild.isDelayed) + "|" + (intChild.tooltip ?? ""));
        Put("creation.intClasses",
            FmtBool(intChild.ClassListContains(BaseField<int>.alignedFieldUssClassName)) + "|" +
            FmtBool(intChild.labelElement.ClassListContains(PropertyField.labelUssClassName)) + "|" +
            FmtBool(intChild.Q(null, BaseField<int>.inputUssClassName).ClassListContains(PropertyField.inputUssClassName)));
        Put("creation.long", DescribeChild(pfLong) + "|" + ((LongField)pfLong.hierarchy[0]).value.ToString(Inv));
        Put("creation.uint", DescribeChild(pfUInt) + "|" + ((UnsignedIntegerField)pfUInt.hierarchy[0]).value.ToString(Inv));
        Put("creation.ulong", DescribeChild(pfULong) + "|" + ((UnsignedLongField)pfULong.hierarchy[0]).value.ToString(Inv));
        Put("creation.float", DescribeChild(pfFloat) + "|" + FmtFloat(((FloatField)pfFloat.hierarchy[0]).value));
        Put("creation.double", DescribeChild(pfDouble) + "|" + FmtDouble(((DoubleField)pfDouble.hierarchy[0]).value));
        var strChild = (TextField)pfString.hierarchy[0];
        Put("creation.string", DescribeChild(pfString) + "|" + strChild.value + "|" + strChild.maxLength.ToString(Inv));

        // 3. Label customization: custom, empty, dynamic change after bind, reset to null.
        var tLbl = ScriptableObject.CreateInstance<PropFieldTarget>();
        var soLbl = new SerializedObject(tLbl);
        var pfLblCustom = new PropertyField(soLbl.FindProperty("count"), "My Count");
        var pfLblEmpty = new PropertyField(soLbl.FindProperty("count"), "");
        m_Root.Add(pfLblCustom);
        m_Root.Add(pfLblEmpty);
        pfLblCustom.Bind(soLbl);
        pfLblEmpty.Bind(soLbl);
        Frames();
        var origLblChild = pfLblCustom.hierarchy[0];
        Put("label.custom", ((IntegerField)origLblChild).label ?? "null");
        Put("label.empty", ((IntegerField)pfLblEmpty.hierarchy[0]).label ?? "null");
        Put("label.emptyNoLabelClass", ((IntegerField)pfLblEmpty.hierarchy[0]).ClassListContains(BaseField<int>.noLabelVariantUssClassName));

        pfLblCustom.label = "Updated Label";
        Frames();
        Put("label.afterDynamicChange",
            (((IntegerField)pfLblCustom.hierarchy[0]).label ?? "null") + "|" +
            FmtBool(ReferenceEquals(origLblChild, pfLblCustom.hierarchy[0])) + "|" +
            ((IntegerField)pfLblCustom.hierarchy[0]).value.ToString(Inv));

        pfLblCustom.label = null;
        Frames();
        Put("label.afterResetNull", ((IntegerField)pfLblCustom.hierarchy[0]).label ?? "null");

        // 4. Bind overloads, detached-before-attach, nested BindableElement, missing path.
        var tBind = ScriptableObject.CreateInstance<PropFieldTarget>();
        var soBind = new SerializedObject(tBind);

        var pfByProp = new PropertyField();
        m_Root.Add(pfByProp);
        pfByProp.BindProperty(soBind.FindProperty("count"));
        Frames();
        Put("bind.bySerializedProperty", DescribeChild(pfByProp) + "|" + ((IntegerField)pfByProp.hierarchy[0]).value.ToString(Inv));

        var pfBySo = new PropertyField { bindingPath = "count" };
        m_Root.Add(pfBySo);
        SerializedProperty retSp = pfBySo.BindProperty(soBind);
        Frames();
        Put("bind.bySerializedObject", (retSp != null ? retSp.propertyPath : "null") + "|" + ((IntegerField)pfBySo.hierarchy[0]).value.ToString(Inv));

        var pfDet = new PropertyField(soBind.FindProperty("count"));
        pfDet.Bind(soBind);
        Put("bind.detachedBeforeAttachChildren", pfDet.hierarchy.childCount);
        m_Root.Add(pfDet);
        Frames();
        Put("bind.detachedAfterAttach", pfDet.hierarchy.childCount.ToString(Inv) + "|" + ((IntegerField)pfDet.hierarchy[0]).value.ToString(Inv));

        var nestBox = new BindableElement { bindingPath = "nested" };
        var pfNested = new PropertyField { bindingPath = "score" };
        nestBox.Add(pfNested);
        m_Root.Add(nestBox);
        nestBox.Bind(soBind);
        Frames();
        Put("bind.nested", DescribeChild(pfNested) + "|" + ((IntegerField)pfNested.hierarchy[0]).value.ToString(Inv));

        var pfMissing = new PropertyField { bindingPath = "doesNotExist" };
        m_Root.Add(pfMissing);
        pfMissing.Bind(soBind);
        Frames();
        Put("bind.missingPathChildren", pfMissing.hierarchy.childCount);

        // 5. Two-way sync & RegisterValueChangeCallback / SerializedPropertyChangeEvent.
        var tSync = ScriptableObject.CreateInstance<PropFieldTarget>();
        var soSync = new SerializedObject(tSync);
        var syncParent = new VisualElement();
        var pfSync = new PropertyField(soSync.FindProperty("count"));
        syncParent.Add(pfSync);
        m_Root.Add(syncParent);

        var pfEvts = new List<string>();
        var parentEvts = new List<string>();
        pfSync.RegisterValueChangeCallback(null); // must be ignored safely
        pfSync.RegisterValueChangeCallback(e => pfEvts.Add(e.changedProperty.propertyPath + "=" + e.changedProperty.intValue.ToString(Inv)));
        syncParent.RegisterCallback<SerializedPropertyChangeEvent>(e => parentEvts.Add(e.changedProperty.propertyPath + "=" + e.changedProperty.intValue.ToString(Inv)));

        pfSync.Bind(soSync);
        Put("sync.initialImmediate", string.Join("|", pfEvts) + ";" + string.Join("|", parentEvts));
        Frames();
        Put("sync.initialAfterFrames", string.Join("|", pfEvts) + ";" + string.Join("|", parentEvts));

        pfEvts.Clear();
        parentEvts.Clear();
        tSync.count = 99;
        Frames();
        var syncChild = (IntegerField)pfSync.hierarchy[0];
        Put("sync.afterTargetEdit", syncChild.value.ToString(Inv) + ";" + string.Join("|", pfEvts) + ";" + string.Join("|", parentEvts));

        pfEvts.Clear();
        parentEvts.Clear();
        syncChild.value = 123;
        Frames();
        Put("sync.afterFieldEdit", tSync.count.ToString(Inv) + ";" + string.Join("|", pfEvts) + ";" + string.Join("|", parentEvts));

        pfEvts.Clear();
        parentEvts.Clear();
        tSync.unrelated = 777;
        Frames();
        Put("sync.afterUnrelated", pfEvts.Count.ToString(Inv) + "," + parentEvts.Count.ToString(Inv));

        // Verify RegisterValueChangeCallback ignores bubbling SerializedPropertyChangeEvent from a descendant.
        pfEvts.Clear();
        parentEvts.Clear();
        using (var fakeChildEvt = SerializedPropertyChangeEvent.GetPooled(soSync.FindProperty("otherInt")))
        {
            fakeChildEvt.target = syncChild;
            syncChild.SendEvent(fakeChildEvt);
        }
        Put("sync.descendantEventFiltered", pfEvts.Count.ToString(Inv) + "," + parentEvts.Count.ToString(Inv));

        // 6. Rebinding: same field type reuse, cross-width int replacement, cross-type replacement, second SO.
        var tRe1 = ScriptableObject.CreateInstance<PropFieldTarget>();
        var tRe2 = ScriptableObject.CreateInstance<PropFieldTarget>();
        tRe1.count = 11; tRe1.otherInt = 22; tRe1.bigValue = 3333333333L; tRe1.flag = false;
        tRe2.count = 44;
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var pfRe = new PropertyField(soRe1.FindProperty("count"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        var firstChild = pfRe.hierarchy[0];
        Put("rebind.first", DescribeChild(pfRe) + "|" + ((IntegerField)firstChild).value.ToString(Inv));

        // Rebind to another int property on same SO -> reuses IntegerField instance.
        pfRe.BindProperty(soRe1.FindProperty("otherInt"));
        Frames();
        var secondChild = pfRe.hierarchy[0];
        Put("rebind.sameTypeReuse",
            FmtBool(ReferenceEquals(firstChild, secondChild)) + "|" + pfRe.hierarchy.childCount.ToString(Inv) + "|" +
            DescribeChild(pfRe) + "|" + ((IntegerField)secondChild).value.ToString(Inv));

        // Rebind to second SO int property -> reuses IntegerField, stops tracking soRe1.
        pfRe.BindProperty(soRe2.FindProperty("count"));
        Frames();
        var thirdChild = pfRe.hierarchy[0];
        tRe1.otherInt = 999;
        Frames();
        Put("rebind.secondSo",
            FmtBool(ReferenceEquals(secondChild, thirdChild)) + "|" + ((IntegerField)thirdChild).value.ToString(Inv));

        // Rebind to long (same SerializedPropertyType.Integer, different field type LongField) -> replaces child.
        pfRe.BindProperty(soRe1.FindProperty("bigValue"));
        Frames();
        var fourthChild = pfRe.hierarchy[0];
        Put("rebind.intToLong",
            FmtBool(ReferenceEquals(thirdChild, fourthChild)) + "|" + pfRe.hierarchy.childCount.ToString(Inv) + "|" +
            DescribeChild(pfRe) + "|" + ((LongField)fourthChild).value.ToString(Inv));

        // Rebind to bool (different SerializedPropertyType) -> clears and creates Toggle.
        pfRe.BindProperty(soRe1.FindProperty("flag"));
        Frames();
        var fifthChild = pfRe.hierarchy[0];
        Put("rebind.longToBool",
            pfRe.hierarchy.childCount.ToString(Inv) + "|" + DescribeChild(pfRe) + "|" + FmtBool(((Toggle)fifthChild).value));

        // 7. Unbind (element, parent container, detached cancel-before-attach).
        var tUnb = ScriptableObject.CreateInstance<PropFieldTarget>();
        var soUnb = new SerializedObject(tUnb);
        var pfUnb = new PropertyField(soUnb.FindProperty("count"));
        m_Root.Add(pfUnb);
        var unbEvts = new List<string>();
        pfUnb.RegisterValueChangeCallback(e => unbEvts.Add(e.changedProperty.intValue.ToString(Inv)));
        pfUnb.Bind(soUnb);
        Frames();
        unbEvts.Clear();

        pfUnb.Unbind();
        var unbChild = (IntegerField)pfUnb.hierarchy[0];
        Put("unbind.afterUnbindState", pfUnb.hierarchy.childCount.ToString(Inv) + "|" + FmtBool(unbChild.HasBinding(BindingIdStr)));
        tUnb.count = 500;
        Frames();
        Put("unbind.afterTargetEdit", unbChild.value.ToString(Inv) + "|" + unbEvts.Count.ToString(Inv));
        unbChild.value = 501;
        Frames();
        Put("unbind.afterChildEdit", tUnb.count.ToString(Inv) + "|" + unbEvts.Count.ToString(Inv));

        var pfDetCancel = new PropertyField(soUnb.FindProperty("count"));
        pfDetCancel.Bind(soUnb);
        pfDetCancel.Unbind();
        m_Root.Add(pfDetCancel);
        Frames();
        Put("unbind.detachedCancelChildren", pfDetCancel.hierarchy.childCount);

        // 8. Multi-object mixed state through PropertyField.
        var m1 = ScriptableObject.CreateInstance<PropFieldTarget>();
        var m2 = ScriptableObject.CreateInstance<PropFieldTarget>();
        m1.flag = true; m1.count = 10;
        m2.flag = false; m2.count = 20;
        var soMulti = new SerializedObject(new UnityEngine.Object[] { m1, m2 });

        var pfMultiToggle = new PropertyField(soMulti.FindProperty("flag"));
        var pfMultiInt = new PropertyField(soMulti.FindProperty("count"));
        m_Root.Add(pfMultiToggle);
        m_Root.Add(pfMultiInt);
        var multiIntEvts = new List<string>();
        pfMultiInt.RegisterValueChangeCallback(e => multiIntEvts.Add(e.changedProperty.intValue.ToString(Inv)));
        pfMultiToggle.Bind(soMulti);
        pfMultiInt.Bind(soMulti);
        Frames();
        var mtChild = (Toggle)pfMultiToggle.hierarchy[0];
        var miChild = (IntegerField)pfMultiInt.hierarchy[0];
        Put("mixed.initial",
            FmtBool(mtChild.showMixedValue) + ":" + FmtBool(mtChild.value) + "|" +
            FmtBool(miChild.showMixedValue) + ":" + miChild.value.ToString(Inv) + "|" +
            multiIntEvts.Count.ToString(Inv));

        miChild.value = 10; // primary value resolves mixed state across both targets
        Frames();
        Put("mixed.afterPrimaryEdit",
            m1.count.ToString(Inv) + "," + m2.count.ToString(Inv) + "|" +
            FmtBool(miChild.showMixedValue) + "|" + string.Join("|", multiIntEvts));

        // 9. Coexistence with runtime DataBinding across Bind and Unbind.
        var tCo = ScriptableObject.CreateInstance<PropFieldTarget>();
        var soCo = new SerializedObject(tCo);
        var rtSrc = new PropFieldRuntimeSource();
        var coContainer = new VisualElement { dataSource = rtSrc };
        var pfCo = new PropertyField(soCo.FindProperty("count"));
        pfCo.SetBinding("tooltip", new DataBinding { dataSourcePath = PropertyPath.FromName("tip"), bindingMode = BindingMode.ToTarget });
        var rtField = new TextField();
        rtField.SetBinding("value", new DataBinding { dataSourcePath = PropertyPath.FromName("msg"), bindingMode = BindingMode.ToTarget });
        coContainer.Add(pfCo);
        coContainer.Add(rtField);
        m_Root.Add(coContainer);

        coContainer.Bind(soCo);
        Frames();
        Put("coexist.beforeUnbind", ((IntegerField)pfCo.hierarchy[0]).value.ToString(Inv) + "|" + pfCo.tooltip + "|" + rtField.value);

        tCo.count = 61;
        rtSrc.tip = "rt-tip-1";
        rtSrc.msg = "rt-msg-1";
        Frames();
        Put("coexist.afterBothUpdate", ((IntegerField)pfCo.hierarchy[0]).value.ToString(Inv) + "|" + pfCo.tooltip + "|" + rtField.value);

        coContainer.Unbind();
        tCo.count = 62;
        rtSrc.tip = "rt-tip-2";
        rtSrc.msg = "rt-msg-2";
        Frames();
        Put("coexist.afterUnbind", ((IntegerField)pfCo.hierarchy[0]).value.ToString(Inv) + "|" + pfCo.tooltip + "|" + rtField.value);
#endif
    }
}
