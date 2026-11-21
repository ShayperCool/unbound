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

public class BindPropTarget : ScriptableObject
{
    public bool flag = true;
    [Tooltip("Count tip")]
    public int count = 42;
    public int unrelated = 1;
    public long big = 10000000000L;
    public uint u32 = 3000000000u;
    public ulong u64 = 12000000000000UL;
    public float ratio = 1.5f;
    public double precise = 3.25;
    public string text = "hello";
    public float sliderF = 25f;
    public int sliderI = 15;
}

public class BindPropRuntimeSource
{
    public string tip = "rt-tip-0";
    public string msg = "rt-msg-0";
}

public class BindPropertyProbe : MonoBehaviour
{
    const string BindingIdStr = "--unity-serialized-object-bindings";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BIND_PROPERTY_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<BindPropertyProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("BIND_PROPERTY_OUT");
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

    static string CatchType(Action action)
    {
        try
        {
            action();
            return "none";
        }
        catch (ArgumentNullException e)
        {
            return "ArgumentNullException:" + e.ParamName;
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    static string FmtBool(bool v) => v ? "1" : "0";
    static string FmtFloat(float v) => v.ToString("R", Inv);
    static string FmtDouble(double v) => v.ToString("R", Inv);

    void Run()
    {
#if UNITY_EDITOR
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        Frames();

        // 1. Arguments, return values, bindingPath side effects, incompatible field warning.
        var tArgs = ScriptableObject.CreateInstance<BindPropTarget>();
        var soArgs = new SerializedObject(tArgs);
        var fNullProp = new IntegerField { bindingPath = "keep" };
        m_Root.Add(fNullProp);
        Put("args.nullProp", CatchType(() => fNullProp.BindProperty((SerializedProperty)null)));
        Put("args.nullPropBindingPath", fNullProp.bindingPath);

        var fNullSo = new IntegerField { bindingPath = "count" };
        m_Root.Add(fNullSo);
        SerializedProperty retNullSo = fNullSo.BindProperty((SerializedObject)null);
        Put("args.nullSoIsNull", retNullSo == null);
        Put("args.nullSoHasBinding", fNullSo.HasBinding(BindingIdStr));

        var fMissing = new IntegerField { bindingPath = "doesNotExist" };
        m_Root.Add(fMissing);
        int missingEvents = 0;
        fMissing.RegisterValueChangedCallback(_ => missingEvents++);
        SerializedProperty retMissing = fMissing.BindProperty(soArgs);
        Frames();
        Put("args.missingPathIsNull", retMissing == null);
        Put("args.missingPathHasBinding", fMissing.HasBinding(BindingIdStr));
        Put("args.missingPathEvents", missingEvents);

        var fEmpty = new IntegerField { bindingPath = "" };
        m_Root.Add(fEmpty);
        Put("args.emptyPathIsNull", fEmpty.BindProperty(soArgs) == null);

        var fRet = new IntegerField { bindingPath = "count" };
        m_Root.Add(fRet);
        SerializedProperty retValid = fRet.BindProperty(soArgs);
        Put("args.validSoReturnPath", retValid != null ? retValid.propertyPath : "null");
        Put("args.validSoHasBinding", fRet.HasBinding(BindingIdStr));

        var fDetRet = new IntegerField { bindingPath = "count" };
        SerializedProperty retDet = fDetRet.BindProperty(soArgs);
        Put("args.detachedSoReturnPath", retDet != null ? retDet.propertyPath : "null");
        Put("args.detachedSoValueBeforeAttach", fDetRet.value);

        var fSpPath = new Toggle();
        fSpPath.BindProperty(soArgs.FindProperty("flag"));
        Put("args.spSetsBindingPathDetached", fSpPath.bindingPath);

        var fIncompat = new Toggle();
        m_Root.Add(fIncompat);
        var compatLogs = new List<string>();
        Application.LogCallback logCb = (condition, stackTrace, type) =>
        {
            if (type == LogType.Warning && condition.Contains("not compatible"))
                compatLogs.Add(condition);
        };
        Application.logMessageReceived += logCb;
        try
        {
            fIncompat.BindProperty(soArgs.FindProperty("count"));
        }
        finally
        {
            Application.logMessageReceived -= logCb;
        }
        Put("args.incompatWarnCount", compatLogs.Count);
        Put("args.incompatWarnText", compatLogs.Count > 0 ? compatLogs[0] : "");
        Put("args.incompatHasBinding", fIncompat.HasBinding(BindingIdStr));

        // 2. Initial sync & ChangeEvent behavior: different initial value vs same initial value vs detached.
        var tInit = ScriptableObject.CreateInstance<BindPropTarget>();
        var soInit = new SerializedObject(tInit);

        var diffToggle = new Toggle(); // default false, prop is true
        var diffInt = new IntegerField { bindingPath = "count" }; // default 0, prop is 42
        m_Root.Add(diffToggle);
        m_Root.Add(diffInt);
        var diffToggleEvts = new List<string>();
        var diffIntEvts = new List<string>();
        diffToggle.RegisterValueChangedCallback(e => diffToggleEvts.Add(FmtBool(e.previousValue) + "->" + FmtBool(e.newValue)));
        diffInt.RegisterValueChangedCallback(e => diffIntEvts.Add(e.previousValue.ToString(Inv) + "->" + e.newValue.ToString(Inv)));

        diffToggle.BindProperty(soInit.FindProperty("flag"));
        diffInt.BindProperty(soInit);
        Put("initial.diffToggleImmediateVal", diffToggle.value);
        Put("initial.diffToggleImmediateEvts", string.Join("|", diffToggleEvts));
        Put("initial.diffIntImmediateVal", diffInt.value);
        Put("initial.diffIntImmediateEvts", string.Join("|", diffIntEvts));
        Frames();
        Put("initial.diffToggleAfterFramesEvts", string.Join("|", diffToggleEvts));
        Put("initial.diffIntAfterFramesEvts", string.Join("|", diffIntEvts));

        var sameToggle = new Toggle();
        sameToggle.SetValueWithoutNotify(true); // already matches prop true
        var sameInt = new IntegerField { bindingPath = "count" };
        sameInt.SetValueWithoutNotify(42); // already matches prop 42
        m_Root.Add(sameToggle);
        m_Root.Add(sameInt);
        var sameToggleEvts = new List<string>();
        var sameIntEvts = new List<string>();
        sameToggle.RegisterValueChangedCallback(e => sameToggleEvts.Add(FmtBool(e.previousValue) + "->" + FmtBool(e.newValue)));
        sameInt.RegisterValueChangedCallback(e => sameIntEvts.Add(e.previousValue.ToString(Inv) + "->" + e.newValue.ToString(Inv)));

        sameToggle.BindProperty(soInit.FindProperty("flag"));
        sameInt.BindProperty(soInit);
        Put("initial.sameToggleImmediateVal", sameToggle.value);
        Put("initial.sameToggleImmediateEvts", string.Join("|", sameToggleEvts));
        Put("initial.sameIntImmediateVal", sameInt.value);
        Put("initial.sameIntImmediateEvts", string.Join("|", sameIntEvts));
        Frames();
        Put("initial.sameToggleAfterFramesEvts", string.Join("|", sameToggleEvts));
        Put("initial.sameIntAfterFramesEvts", string.Join("|", sameIntEvts));

        var detToggle = new Toggle();
        var detInt = new IntegerField { bindingPath = "count" };
        var detToggleEvts = new List<string>();
        var detIntEvts = new List<string>();
        detToggle.RegisterValueChangedCallback(e => detToggleEvts.Add(FmtBool(e.previousValue) + "->" + FmtBool(e.newValue)));
        detInt.RegisterValueChangedCallback(e => detIntEvts.Add(e.previousValue.ToString(Inv) + "->" + e.newValue.ToString(Inv)));
        detToggle.BindProperty(soInit.FindProperty("flag"));
        detInt.BindProperty(soInit);
        Put("initial.detBeforeAttachVals", FmtBool(detToggle.value) + "," + detInt.value.ToString(Inv));
        Put("initial.detBeforeAttachEvts", detToggleEvts.Count + "," + detIntEvts.Count);
        m_Root.Add(detToggle);
        m_Root.Add(detInt);
        Frames();
        Put("initial.detAfterAttachVals", FmtBool(detToggle.value) + "," + detInt.value.ToString(Inv));
        Put("initial.detAfterAttachEvts", string.Join("|", detToggleEvts) + ";" + string.Join("|", detIntEvts));

        // Tooltip and NotEditable behavior.
        var tipAuto = new IntegerField();
        var tipCustom = new IntegerField { tooltip = "custom" };
        m_Root.Add(tipAuto);
        m_Root.Add(tipCustom);
        tipAuto.BindProperty(soInit.FindProperty("count"));
        tipCustom.BindProperty(soInit.FindProperty("count"));
        Put("initial.tooltipAutoBound", tipAuto.tooltip ?? "");
        Put("initial.tooltipCustomBound", tipCustom.tooltip ?? "");
        tipAuto.Unbind();
        tipCustom.Unbind();
        Put("initial.tooltipAutoUnbound", tipAuto.tooltip ?? "");
        Put("initial.tooltipCustomUnbound", tipCustom.tooltip ?? "");

        var tRO = ScriptableObject.CreateInstance<BindPropTarget>();
        tRO.hideFlags = HideFlags.NotEditable;
        var soRO = new SerializedObject(tRO);
        var fRO = new IntegerField();
        m_Root.Add(fRO);
        fRO.BindProperty(soRO.FindProperty("count"));
        Put("initial.notEditableEnabledSelf", fRO.enabledSelf);

        // 3. Property-to-field edits.
        var tMain = ScriptableObject.CreateInstance<BindPropTarget>();
        var soMain = new SerializedObject(tMain);
        var mainToggle = new Toggle();
        var mainInt = new IntegerField { bindingPath = "count" };
        m_Root.Add(mainToggle);
        m_Root.Add(mainInt);
        var mainToggleEvts = new List<string>();
        var mainIntEvts = new List<string>();
        mainToggle.RegisterValueChangedCallback(e => mainToggleEvts.Add(FmtBool(e.previousValue) + "->" + FmtBool(e.newValue)));
        mainInt.RegisterValueChangedCallback(e => mainIntEvts.Add(e.previousValue.ToString(Inv) + "->" + e.newValue.ToString(Inv)));
        mainToggle.BindProperty(soMain.FindProperty("flag"));
        mainInt.BindProperty(soMain);
        Frames();
        mainToggleEvts.Clear();
        mainIntEvts.Clear();

        soMain.FindProperty("flag").boolValue = false;
        soMain.FindProperty("count").intValue = 99;
        soMain.ApplyModifiedProperties();
        Put("propToField.beforeFramesVals", FmtBool(mainToggle.value) + "," + mainInt.value.ToString(Inv));
        Frames();
        Put("propToField.afterApplyVals", FmtBool(mainToggle.value) + "," + mainInt.value.ToString(Inv));
        Put("propToField.afterApplyEvts", string.Join("|", mainToggleEvts) + ";" + string.Join("|", mainIntEvts));

        mainToggleEvts.Clear();
        mainIntEvts.Clear();
        tMain.flag = true;
        tMain.count = 123;
        Frames();
        Put("propToField.afterDirectVals", FmtBool(mainToggle.value) + "," + mainInt.value.ToString(Inv));
        Put("propToField.afterDirectEvts", string.Join("|", mainToggleEvts) + ";" + string.Join("|", mainIntEvts));

        mainIntEvts.Clear();
        soMain.FindProperty("count").intValue = 777;
        soMain.Update();
        Frames();
        Put("propToField.afterUnappliedRevertVal", mainInt.value);
        Put("propToField.afterUnappliedRevertEvts", mainIntEvts.Count);

        // 4. Field-to-property edits + TrackPropertyValue coexistence.
        var trackEl = new VisualElement();
        m_Root.Add(trackEl);
        var trackedCounts = new List<int>();
        trackEl.TrackPropertyValue(soMain.FindProperty("count"), p => trackedCounts.Add(p.intValue));
        Frames();
        trackedCounts.Clear();

        mainToggleEvts.Clear();
        mainIntEvts.Clear();
        mainToggle.value = false;
        Put("fieldToProp.toggleImmediate", FmtBool(tMain.flag) + "," + FmtBool(soMain.FindProperty("flag").boolValue) + "," + FmtBool(soMain.hasModifiedProperties));
        Put("fieldToProp.toggleImmediateEvts", string.Join("|", mainToggleEvts));

        mainInt.value = 555;
        Put("fieldToProp.intImmediate", tMain.count.ToString(Inv) + "," + soMain.FindProperty("count").intValue.ToString(Inv) + "," + FmtBool(soMain.hasModifiedProperties));
        Put("fieldToProp.intImmediateEvts", string.Join("|", mainIntEvts));
        Frames();
        Put("fieldToProp.afterFramesEvts", string.Join("|", mainToggleEvts) + ";" + string.Join("|", mainIntEvts));
        Put("fieldToProp.trackedAfterFieldEdit", string.Join(",", trackedCounts));

        mainIntEvts.Clear();
        mainInt.value = 555; // same value
        Frames();
        Put("fieldToProp.sameValEvts", mainIntEvts.Count);

        trackedCounts.Clear();
        mainInt.value = 666;
        Frames();
        Put("fieldToProp.secondEditTargetAndTrack", tMain.count.ToString(Inv) + ";" + string.Join(",", trackedCounts));

        // 5. Unrelated property changes do not alter bound fields or fire ChangeEvents.
        mainToggleEvts.Clear();
        mainIntEvts.Clear();
        tMain.unrelated = 77;
        Frames();
        soMain.FindProperty("unrelated").intValue = 88;
        soMain.ApplyModifiedProperties();
        Frames();
        Put("unrelated.vals", FmtBool(mainToggle.value) + "," + mainInt.value.ToString(Inv));
        Put("unrelated.evtCounts", mainToggleEvts.Count + "," + mainIntEvts.Count);

        // 6. Detach/reattach, Unbind, detached cancel before attach, and rebind to second SO.
        mainIntEvts.Clear();
        mainInt.RemoveFromHierarchy();
        tMain.count = 801;
        Frames();
        Put("unbindReattach.detachedValAndEvts", mainInt.value.ToString(Inv) + ";" + mainIntEvts.Count);
        m_Root.Add(mainInt);
        Put("unbindReattach.reattachImmediateVal", mainInt.value);
        Put("unbindReattach.reattachImmediateEvts", string.Join("|", mainIntEvts));
        Frames();
        Put("unbindReattach.reattachAfterFramesVal", mainInt.value);
        Put("unbindReattach.reattachAfterFramesEvts", string.Join("|", mainIntEvts));

        mainIntEvts.Clear();
        mainInt.RemoveFromHierarchy();
        mainInt.value = 802; // detached edit: BaseField.value does not send ChangeEvent when panel == null
        Put("unbindReattach.detachedFieldEditTarget", tMain.count);
        m_Root.Add(mainInt);
        Frames();
        Put("unbindReattach.reattachAfterDetachedFieldEdit", mainInt.value.ToString(Inv) + ";" + tMain.count.ToString(Inv) + ";" + string.Join("|", mainIntEvts));

        // Unbind element & Unbind parent container.
        mainIntEvts.Clear();
        mainInt.Unbind();
        Put("unbindReattach.hasBindingAfterUnbind", mainInt.HasBinding(BindingIdStr));
        tMain.count = 900;
        Frames();
        Put("unbindReattach.afterSelfUnbindPropEdit", mainInt.value.ToString(Inv) + ";" + mainIntEvts.Count);
        mainInt.value = 901;
        Frames();
        Put("unbindReattach.afterSelfUnbindFieldEdit", tMain.count);

        var parentBox = new VisualElement();
        var childToggle = new Toggle();
        parentBox.Add(childToggle);
        m_Root.Add(parentBox);
        childToggle.BindProperty(soMain.FindProperty("flag"));
        Frames();
        parentBox.Unbind();
        tMain.flag = true;
        Frames();
        Put("unbindReattach.afterParentUnbindPropEdit", childToggle.value);
        childToggle.value = false;
        Frames();
        Put("unbindReattach.afterParentUnbindFieldEdit", tMain.flag);

        // Detached BindProperty cancelled by Unbind before attaching.
        var detCancel = new IntegerField { bindingPath = "count" };
        detCancel.BindProperty(soMain);
        detCancel.Unbind();
        m_Root.Add(detCancel);
        Frames();
        Put("unbindReattach.detachedCancelVal", detCancel.value);
        Put("unbindReattach.detachedCancelHasBinding", detCancel.HasBinding(BindingIdStr));

        // Rebind already-bound field to a second SerializedObject.
        var tRebind1 = ScriptableObject.CreateInstance<BindPropTarget>();
        var tRebind2 = ScriptableObject.CreateInstance<BindPropTarget>();
        tRebind1.count = 11;
        tRebind2.count = 22;
        var soRebind1 = new SerializedObject(tRebind1);
        var soRebind2 = new SerializedObject(tRebind2);
        var fRebind = new IntegerField();
        m_Root.Add(fRebind);
        fRebind.BindProperty(soRebind1.FindProperty("count"));
        Frames();
        fRebind.BindProperty(soRebind2.FindProperty("count"));
        Frames();
        Put("unbindReattach.rebindVal", fRebind.value);
        tRebind1.count = 111;
        Frames();
        Put("unbindReattach.rebindIgnoresOldSo", fRebind.value);
        fRebind.value = 222;
        Frames();
        Put("unbindReattach.rebindWritesNewSo", tRebind1.count.ToString(Inv) + "," + tRebind2.count.ToString(Inv));

        // 7. Multi-object mixed state.
        var m1 = ScriptableObject.CreateInstance<BindPropTarget>();
        var m2 = ScriptableObject.CreateInstance<BindPropTarget>();
        m1.flag = true; m1.count = 10; m1.text = "a";
        m2.flag = false; m2.count = 20; m2.text = "b";
        var soMulti = new SerializedObject(new UnityEngine.Object[] { m1, m2 });

        var mToggle = new Toggle();
        var mInt = new IntegerField { bindingPath = "count" };
        var mText = new TextField();
        m_Root.Add(mToggle);
        m_Root.Add(mInt);
        m_Root.Add(mText);
        var mToggleEvts = new List<string>();
        var mIntEvts = new List<string>();
        var mTextEvts = new List<string>();
        mToggle.RegisterValueChangedCallback(e => mToggleEvts.Add(FmtBool(e.previousValue) + "->" + FmtBool(e.newValue)));
        mInt.RegisterValueChangedCallback(e => mIntEvts.Add(e.previousValue.ToString(Inv) + "->" + e.newValue.ToString(Inv)));
        mText.RegisterValueChangedCallback(e => mTextEvts.Add((e.previousValue ?? "null") + "->" + (e.newValue ?? "null")));

        mToggle.BindProperty(soMulti.FindProperty("flag"));
        mInt.BindProperty(soMulti);
        mText.BindProperty(soMulti.FindProperty("text"));
        Put("mixed.immediateState",
            FmtBool(mToggle.showMixedValue) + ":" + FmtBool(mToggle.value) + "," +
            FmtBool(mInt.showMixedValue) + ":" + mInt.value.ToString(Inv) + "," +
            FmtBool(mText.showMixedValue) + ":" + (mText.value ?? "null"));
        Put("mixed.immediateEvtCounts", mToggleEvts.Count + "," + mIntEvts.Count + "," + mTextEvts.Count);
        Frames();
        Put("mixed.afterFramesState",
            FmtBool(mToggle.showMixedValue) + ":" + FmtBool(mToggle.value) + "," +
            FmtBool(mInt.showMixedValue) + ":" + mInt.value.ToString(Inv) + "," +
            FmtBool(mText.showMixedValue) + ":" + (mText.value ?? "null"));
        Put("mixed.afterFramesEvtCounts", mToggleEvts.Count + "," + mIntEvts.Count + "," + mTextEvts.Count);

        // Edit mixed toggle -> false (matches m2, differs from m1).
        mToggle.value = false;
        Frames();
        Put("mixed.afterToggleEdit", FmtBool(m1.flag) + "," + FmtBool(m2.flag) + "," + FmtBool(mToggle.showMixedValue) + ";" + string.Join("|", mToggleEvts));

        // Edit mixed int -> 10 (SAME as primary m1, differs from m2!).
        mInt.value = 10;
        Frames();
        Put("mixed.afterIntPrimaryEdit", m1.count.ToString(Inv) + "," + m2.count.ToString(Inv) + "," + FmtBool(mInt.showMixedValue) + ";" + string.Join("|", mIntEvts));

        // Diverge m2 externally + SetIsDifferentCacheDirty.
        mIntEvts.Clear();
        m2.count = 30;
        soMulti.SetIsDifferentCacheDirty();
        Frames();
        Put("mixed.afterDiverge", FmtBool(mInt.showMixedValue) + ":" + mInt.value.ToString(Inv) + ";" + string.Join("|", mIntEvts));

        // Converge m2 externally + SetIsDifferentCacheDirty.
        mIntEvts.Clear();
        m2.count = 10;
        soMulti.SetIsDifferentCacheDirty();
        Frames();
        Put("mixed.afterConverge", FmtBool(mInt.showMixedValue) + ":" + mInt.value.ToString(Inv) + ";" + string.Join("|", mIntEvts));

        // Diverge again and write a fresh value 44 across both targets.
        m2.count = 35;
        soMulti.SetIsDifferentCacheDirty();
        Frames();
        mIntEvts.Clear();
        mInt.value = 44;
        Frames();
        Put("mixed.afterNewValueEdit", m1.count.ToString(Inv) + "," + m2.count.ToString(Inv) + "," + FmtBool(mInt.showMixedValue) + ";" + string.Join("|", mIntEvts));

        // 8. Additional confirmed scalar fields.
        var tSc = ScriptableObject.CreateInstance<BindPropTarget>();
        var soSc = new SerializedObject(tSc);
        var fFloat = new FloatField { bindingPath = "ratio" };
        var fDouble = new DoubleField();
        var fLong = new LongField { bindingPath = "big" };
        var fUInt = new UnsignedIntegerField();
        var fULong = new UnsignedLongField { bindingPath = "u64" };
        var fText = new TextField();
        var fSlider = new Slider(0f, 100f) { bindingPath = "sliderF" };
        var fSliderInt = new SliderInt(0, 100);
        m_Root.Add(fFloat);
        m_Root.Add(fDouble);
        m_Root.Add(fLong);
        m_Root.Add(fUInt);
        m_Root.Add(fULong);
        m_Root.Add(fText);
        m_Root.Add(fSlider);
        m_Root.Add(fSliderInt);

        fFloat.BindProperty(soSc);
        fDouble.BindProperty(soSc.FindProperty("precise"));
        fLong.BindProperty(soSc);
        fUInt.BindProperty(soSc.FindProperty("u32"));
        fULong.BindProperty(soSc);
        fText.BindProperty(soSc.FindProperty("text"));
        fSlider.BindProperty(soSc);
        fSliderInt.BindProperty(soSc.FindProperty("sliderI"));
        Frames();
        Put("scalars.initial",
            FmtFloat(fFloat.value) + "|" + FmtDouble(fDouble.value) + "|" + fLong.value.ToString(Inv) + "|" +
            fUInt.value.ToString(Inv) + "|" + fULong.value.ToString(Inv) + "|" + fText.value + "|" +
            FmtFloat(fSlider.value) + "|" + fSliderInt.value.ToString(Inv));

        tSc.ratio = 2.25f;
        tSc.precise = 7.5;
        tSc.big = -9000000000L;
        tSc.u32 = 4000000000u;
        tSc.u64 = 15000000000000UL;
        tSc.text = "world";
        tSc.sliderF = 60f;
        tSc.sliderI = 70;
        Frames();
        Put("scalars.propToField",
            FmtFloat(fFloat.value) + "|" + FmtDouble(fDouble.value) + "|" + fLong.value.ToString(Inv) + "|" +
            fUInt.value.ToString(Inv) + "|" + fULong.value.ToString(Inv) + "|" + fText.value + "|" +
            FmtFloat(fSlider.value) + "|" + fSliderInt.value.ToString(Inv));

        fFloat.value = 4.5f;
        fDouble.value = 9.125;
        fLong.value = 123456789012L;
        fUInt.value = 2500000000u;
        fULong.value = 18000000000000UL;
        fText.value = "edited";
        fSlider.value = 82.5f;
        fSliderInt.value = 88;
        Frames();
        Put("scalars.fieldToProp",
            FmtFloat(tSc.ratio) + "|" + FmtDouble(tSc.precise) + "|" + tSc.big.ToString(Inv) + "|" +
            tSc.u32.ToString(Inv) + "|" + tSc.u64.ToString(Inv) + "|" + tSc.text + "|" +
            FmtFloat(tSc.sliderF) + "|" + tSc.sliderI.ToString(Inv));

        // 9. Coexistence with runtime DataBinding across BindProperty and Unbind.
        var tCo = ScriptableObject.CreateInstance<BindPropTarget>();
        var soCo = new SerializedObject(tCo);
        var rtSrc = new BindPropRuntimeSource();
        var coField = new IntegerField { dataSource = rtSrc };
        coField.SetBinding("tooltip", new DataBinding { dataSourcePath = PropertyPath.FromName("tip"), bindingMode = BindingMode.ToTarget });
        var coSib = new TextField { dataSource = rtSrc };
        coSib.SetBinding("value", new DataBinding { dataSourcePath = PropertyPath.FromName("msg"), bindingMode = BindingMode.ToTarget });
        m_Root.Add(coField);
        m_Root.Add(coSib);
        coField.BindProperty(soCo.FindProperty("count"));
        Frames();
        Put("coexist.beforeUnbind", coField.value.ToString(Inv) + "|" + coField.tooltip + "|" + coSib.value);

        rtSrc.tip = "rt-tip-1";
        rtSrc.msg = "rt-msg-1";
        tCo.count = 51;
        Frames();
        Put("coexist.afterBothUpdate", coField.value.ToString(Inv) + "|" + coField.tooltip + "|" + coSib.value);

        coField.Unbind();
        rtSrc.tip = "rt-tip-2";
        rtSrc.msg = "rt-msg-2";
        tCo.count = 52;
        Frames();
        Put("coexist.afterUnbind", coField.value.ToString(Inv) + "|" + coField.tooltip + "|" + coSib.value);
#endif
    }
}
