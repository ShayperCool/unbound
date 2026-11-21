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

public enum ObjectControlEnum
{
    Alpha = 1,
    Beta = 2
}

public class ObjectProbeAsset : ScriptableObject
{
    public int tagValue;
}

public class ObjectProbeSubAsset : ObjectProbeAsset
{
    public string subName;
}

public class ObjectProbeBehaviour : MonoBehaviour
{
    public int marker;
}

[Serializable]
public struct DeepObjectData
{
    public ObjectProbeAsset asset;
}

[Serializable]
public struct NestedObjectData
{
    public ObjectProbeAsset asset;
    public DeepObjectData deep;
}

public class ObjectTarget : ScriptableObject
{
    public UnityEngine.Object anyObjRef;
    public ObjectProbeAsset nullAssetRef;
    public ObjectProbeAsset assetRef;
    public ObjectProbeAsset otherAssetRef;
    public ObjectProbeAsset multiAssetRef;
    public ObjectProbeBehaviour behaviourRef;
    public Transform transformRef;
    public GameObject goRef;
    public Material materialRef;
    public Texture2D textureRef;

    public NestedObjectData nested;
    public ObjectProbeAsset[] assetArray = new ObjectProbeAsset[3];

    // Sibling controls for tree Bind and PropertyField cross-type rebinding.
    public Gradient siblingGradient = new Gradient();
    public AnimationCurve siblingCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public BoundsInt siblingBoundsInt = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
    public Color siblingColor = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Vector3 siblingVec3 = new Vector3(1f, 2f, 3f);
    public Vector4 siblingVec4 = new Vector4(1f, 2f, 3f, 4f);
    public int siblingInt = 42;
    public ObjectControlEnum siblingEnum = ObjectControlEnum.Alpha;
}

public class ObjectRuntimeSource
{
    [CreateProperty]
    public string tip { get; set; } = "rt-obj-tip-0";
}

public class CustomNotifyObjectElement : VisualElement, IBindable, INotifyValueChanged<UnityEngine.Object>
{
    UnityEngine.Object m_Value;
    public IBinding binding { get; set; }
    public string bindingPath { get; set; }
    public int notifyCount;

    public UnityEngine.Object value
    {
        get => m_Value;
        set
        {
            if (EqualityComparer<UnityEngine.Object>.Default.Equals(m_Value, value)) return;
            var prev = m_Value;
            m_Value = value;
            notifyCount++;
            using var evt = ChangeEvent<UnityEngine.Object>.GetPooled(prev, m_Value);
            evt.target = this;
            SendEvent(evt);
        }
    }

    public void SetValueWithoutNotify(UnityEngine.Object newValue) => m_Value = newValue;
}

public class CustomNotifyTypedAssetElement : VisualElement, IBindable, INotifyValueChanged<ObjectProbeAsset>
{
    ObjectProbeAsset m_Value;
    public IBinding binding { get; set; }
    public string bindingPath { get; set; }
    public int notifyCount;

    public ObjectProbeAsset value
    {
        get => m_Value;
        set
        {
            if (EqualityComparer<ObjectProbeAsset>.Default.Equals(m_Value, value)) return;
            var prev = m_Value;
            m_Value = value;
            notifyCount++;
            using var evt = ChangeEvent<ObjectProbeAsset>.GetPooled(prev, m_Value);
            evt.target = this;
            SendEvent(evt);
        }
    }

    public void SetValueWithoutNotify(ObjectProbeAsset newValue) => m_Value = newValue;
}

public class CustomNotifyTextureElement : VisualElement, IBindable, INotifyValueChanged<Texture2D>
{
    Texture2D m_Value;
    public IBinding binding { get; set; }
    public string bindingPath { get; set; }

    public Texture2D value
    {
        get => m_Value;
        set
        {
            if (EqualityComparer<Texture2D>.Default.Equals(m_Value, value)) return;
            var prev = m_Value;
            m_Value = value;
            using var evt = ChangeEvent<Texture2D>.GetPooled(prev, m_Value);
            evt.target = this;
            SendEvent(evt);
        }
    }

    public void SetValueWithoutNotify(Texture2D newValue) => m_Value = newValue;
}

public class ObjectBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("ObjectBindingProbe");
        var doc = go.AddComponent<UIDocument>();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        doc.panelSettings = ps;
        go.AddComponent<ObjectBindingProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("OBJECT_BINDING_OUT");
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

    static string FmtObj(UnityEngine.Object obj)
    {
        if (ReferenceEquals(obj, null)) return "null";
        if (obj == null) return "destroyed(" + obj.GetType().Name + ")";
        return obj.GetType().Name + ":" + obj.name;
    }

    static string FmtType(Type t) => t == null ? "null" : t.Name;

#if UNITY_EDITOR
    static Label DisplayLabel(ObjectField of) => of.Q<Label>(className: "unity-object-field-display__label");
    static Image DisplayIcon(ObjectField of) => of.Q<Image>(className: "unity-object-field-display__icon");

    static string FmtObjField(ObjectField of)
    {
        var lbl = DisplayLabel(of);
        var icon = DisplayIcon(of);
        bool hasNullCls = lbl != null && lbl.ClassListContains("unity-object-field-display__label--value-null");
        bool hasMixedCls = lbl != null && lbl.ClassListContains(BaseField<UnityEngine.Object>.mixedValueLabelUssClassName);
        return FmtObj(of.value) +
               "|t=" + FmtType(of.objectType) +
               "|sc=" + FmtBool(of.allowSceneObjects) +
               "|m=" + FmtBool(of.showMixedValue) +
               "|ml=" + FmtBool(hasMixedCls) +
               "|nl=" + FmtBool(hasNullCls) +
               "|ic=" + FmtBool(icon != null && icon.image != null) +
               "|txt=" + (lbl != null ? (lbl.text ?? "null") : "nolbl");
    }

    static string DescribePf(PropertyField pf)
    {
        if (pf.hierarchy.childCount == 0) return "none";
        var child = pf.hierarchy[0];
        if (child is ObjectField of)
        {
            bool hasLabelCls = of.labelElement.ClassListContains(PropertyField.labelUssClassName);
            bool hasInputCls = of.Q(className: PropertyField.inputUssClassName) != null;
            bool hasAligned = of.ClassListContains(BaseField<UnityEngine.Object>.alignedFieldUssClassName);
            bool hasObjCls = of.ClassListContains(ObjectField.ussClassName);
            bool hasDisp = of.Q(className: ObjectField.objectUssClassName) != null;
            bool hasSel = of.Q(className: ObjectField.selectorUssClassName) != null;
            return "ObjectField|" + of.name + "|" + (of.label ?? "null") + "|" +
                   FmtBool(hasLabelCls) + FmtBool(hasInputCls) + FmtBool(hasAligned) + FmtBool(hasObjCls) + FmtBool(hasDisp) + FmtBool(hasSel) + "|" +
                   FmtObjField(of);
        }
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

    static ObjectProbeAsset MakeAsset(string name, int tag)
    {
        var a = ScriptableObject.CreateInstance<ObjectProbeAsset>();
        a.name = name;
        a.tagValue = tag;
        return a;
    }

    static ObjectProbeSubAsset MakeSubAsset(string name, int tag, string sub)
    {
        var a = ScriptableObject.CreateInstance<ObjectProbeSubAsset>();
        a.name = name;
        a.tagValue = tag;
        a.subName = sub;
        return a;
    }
#endif

    void Run()
    {
#if UNITY_EDITOR
        var document = GetComponent<UIDocument>();
        m_Root = document.rootVisualElement;
        Frames(1);

        var assetA = MakeAsset("AssetA", 1);
        var assetB = MakeAsset("AssetB", 2);
        var assetC = MakeAsset("AssetC", 3);
        var subAsset = MakeSubAsset("SubAssetD", 4, "sub");
        var texA = new Texture2D(2, 2) { name = "TexA" };
        var texB = new Texture2D(2, 2) { name = "TexB" };
        var matA = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "MatA" };

        var goWithComp = new GameObject("ActorWithComp");
        var compA = goWithComp.AddComponent<ObjectProbeBehaviour>();
        compA.marker = 11;
        var goOtherComp = new GameObject("ActorOtherComp");
        var compB = goOtherComp.AddComponent<ObjectProbeBehaviour>();
        compB.marker = 22;
        var goPlain = new GameObject("PlainActor");
        var goIcon = new GameObject("IconActor");
        var soIcon = new SerializedObject(goIcon);
        soIcon.FindProperty("m_Icon").objectReferenceValue = texA;
        soIcon.ApplyModifiedProperties();

        // 0. Standalone ObjectField: defaults, hierarchy, objectType, allowSceneObjects, value/coercion, destroyed, mixed.
        var ofStd = new ObjectField("My Ref");
        var stdEvts = new List<string>();
        ofStd.RegisterValueChangedCallback(e =>
            stdEvts.Add(FmtObj(e.previousValue) + "->" + FmtObj(e.newValue)));
        m_Root.Add(ofStd);
        Frames(1);
        var disp = ofStd.Q(className: ObjectField.objectUssClassName);
        var sel = ofStd.Q(className: ObjectField.selectorUssClassName);
        var inp = ofStd.Q(className: ObjectField.inputUssClassName);
        Put("standalone.defaults",
            (ofStd.label ?? "null") + "|" +
            FmtBool(ofStd.ClassListContains(ObjectField.ussClassName)) +
            FmtBool(ofStd.labelElement.ClassListContains(ObjectField.labelUssClassName)) +
            FmtBool(inp != null) +
            FmtBool(disp != null && disp.ClassListContains("unity-object-field-display")) +
            FmtBool(sel != null) +
            FmtBool(inp != null && inp.focusable) +
            FmtBool(ofStd.labelElement.focusable) +
            FmtBool(disp != null && disp.focusable) + "|" +
            FmtObjField(ofStd));

        // objectType changes and display labels when value == null.
        ofStd.objectType = typeof(ObjectProbeAsset);
        string dispAssetNull = FmtObjField(ofStd);
        ofStd.objectType = typeof(Texture2D);
        string dispTexNull = FmtObjField(ofStd);
        ofStd.objectType = typeof(Transform);
        string dispTransNull = FmtObjField(ofStd);
        ofStd.objectType = null;
        string dispTypeNull = FmtObjField(ofStd);
        ofStd.objectType = typeof(ObjectProbeAsset);
        ofStd.allowSceneObjects = false;
        Put("standalone.object_type_display",
            dispAssetNull + "||" + dispTexNull + "||" + dispTransNull + "||" + dispTypeNull + "||sc=" + FmtBool(ofStd.allowSceneObjects));

        // Value assignment, no-op repeat, subtype, wrong type, allowSceneObjects=false, null.
        ofStd.value = assetA;
        string afterAssetA = FmtObjField(ofStd);
        int evAfterA = stdEvts.Count;
        ofStd.value = assetA;
        int evAfterRepeat = stdEvts.Count;
        ofStd.value = subAsset;
        string afterSub = FmtObjField(ofStd);
        ofStd.value = texA; // mismatched with objectType=ObjectProbeAsset on unbound field
        string afterWrongType = FmtObjField(ofStd);
        ofStd.value = goPlain; // scene object with allowSceneObjects=false on unbound field
        string afterSceneWhenFalse = FmtObjField(ofStd);
        ofStd.value = null;
        string afterNull = FmtObjField(ofStd);
        Put("standalone.value_assign",
            afterAssetA + "|evA=" + FmtInt(evAfterA) + "|evRep=" + FmtInt(evAfterRepeat) + "||" +
            afterSub + "||" + afterWrongType + "||" + afterSceneWhenFalse + "||" + afterNull + "|ev=" + FmtInt(stdEvts.Count));

        // GameObject -> Component coercion in SetValueWithoutNotify / value.
        var ofComp = new ObjectField();
        m_Root.Add(ofComp);
        ofComp.objectType = typeof(Transform);
        ofComp.value = goPlain;
        string coerceTransform = FmtObjField(ofComp);

        ofComp.objectType = typeof(ObjectProbeBehaviour);
        ofComp.value = goWithComp;
        string coercePresent = FmtObjField(ofComp);
        ofComp.value = goPlain; // does not have ObjectProbeBehaviour
        string coerceAbsent = FmtObjField(ofComp);

        ofComp.objectType = typeof(Component); // exact Component, not subclass
        ofComp.value = goWithComp;
        string coerceBaseComp = FmtObjField(ofComp);
        Put("standalone.component_coercion",
            coerceTransform + "||" + coercePresent + "||" + coerceAbsent + "||" + coerceBaseComp);

        // Standalone destroyed object and mixed value display.
        var ofMix = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        m_Root.Add(ofMix);
        var tempStandalone = MakeAsset("TempStandalone", 99);
        ofMix.value = tempStandalone;
        string beforeDestroy = FmtObjField(ofMix);
        UnityEngine.Object.DestroyImmediate(tempStandalone);
        ofMix.showMixedValue = true;
        string mixedOn = FmtObjField(ofMix);
        ofMix.showMixedValue = false;
        string mixedOffAfterDestroy = FmtObjField(ofMix);
        ofMix.value = assetA;
        ofMix.showMixedValue = true;
        string mixedOnLive = FmtObjField(ofMix);
        ofMix.showMixedValue = false;
        string mixedOffLive = FmtObjField(ofMix);
        Put("standalone.destroyed_and_mixed",
            beforeDestroy + "||" + mixedOn + "||" + mixedOffAfterDestroy + "||" + mixedOnLive + "||" + mixedOffLive);

        // 1. Direct ObjectField.BindProperty.
        var tMain = ScriptableObject.CreateInstance<ObjectTarget>();
        tMain.anyObjRef = assetA;
        tMain.assetRef = assetA;
        tMain.otherAssetRef = assetB;
        tMain.behaviourRef = compA;
        tMain.transformRef = goWithComp.transform;
        tMain.goRef = goPlain;
        tMain.materialRef = matA;
        tMain.textureRef = texA;
        tMain.nested = new NestedObjectData { asset = assetB, deep = new DeepObjectData { asset = assetC } };
        tMain.assetArray = new[] { assetA, assetB, assetC };
        var soMain = new SerializedObject(tMain);
        var soComp = new SerializedObject(compA);

        var pAny = soMain.FindProperty("anyObjRef");
        var pAsset = soMain.FindProperty("assetRef");
        var pBehav = soMain.FindProperty("behaviourRef");
        var pTrans = soMain.FindProperty("transformRef");
        var pGo = soMain.FindProperty("goRef");
        var pMat = soMain.FindProperty("materialRef");
        var pScript = soComp.FindProperty("m_Script");
        var pIcon = soIcon.FindProperty("m_Icon");
        var pArr1 = soMain.FindProperty("assetArray.Array.data[1]");
        Put("prop.meta",
            "any=" + pAny.propertyType + "/" + pAny.type + "/" + FmtBool(pAny.hasVisibleChildren) +
            "|asset=" + pAsset.propertyType + "/" + pAsset.type + "/" + FmtBool(pAsset.hasVisibleChildren) +
            "|behav=" + pBehav.type +
            "|trans=" + pTrans.type +
            "|go=" + pGo.type +
            "|mat=" + pMat.type +
            "|script=" + pScript.type +
            "|icon=" + pIcon.type +
            "|arr1=" + pArr1.type);

        // Direct bind to initially null property.
        var ofNull = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        var nullEvts = new List<string>();
        ofNull.RegisterValueChangedCallback(e => nullEvts.Add(FmtObj(e.previousValue) + "->" + FmtObj(e.newValue)));
        m_Root.Add(ofNull);
        ofNull.BindProperty(soMain.FindProperty("nullAssetRef"));
        Frames();
        Put("direct.null_initial", FmtObjField(ofNull) + "|ev=" + string.Join("|", nullEvts));

        ofNull.value = null;
        Frames();
        Put("direct.null_noop", FmtObjField(ofNull) + "|evc=" + FmtInt(nullEvts.Count));

        // Direct bind to populated asset property.
        var ofMain = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        var mainEvts = new List<string>();
        ofMain.RegisterValueChangedCallback(e => mainEvts.Add(FmtObj(e.previousValue) + "->" + FmtObj(e.newValue)));
        m_Root.Add(ofMain);
        ofMain.BindProperty(soMain.FindProperty("assetRef"));
        Frames();
        Put("direct.initial", FmtObjField(ofMain) + "|ev=" + string.Join("|", mainEvts));

        // UI -> SO: assign assetB, assign subAsset, assign null.
        ofMain.value = assetB;
        Frames();
        Put("direct.ui_to_so", FmtObjField(ofMain) + "|t=" + FmtObj(tMain.assetRef) + "|evc=" + FmtInt(mainEvts.Count));

        ofMain.value = subAsset;
        Frames();
        Put("direct.ui_to_so_sub", FmtObjField(ofMain) + "|t=" + FmtObj(tMain.assetRef) + "|evc=" + FmtInt(mainEvts.Count));

        ofMain.value = null;
        Frames();
        Put("direct.ui_to_so_null", FmtObjField(ofMain) + "|t=" + FmtObj(tMain.assetRef) + "|evc=" + FmtInt(mainEvts.Count));

        // SO -> UI: assign assetA via SerializedProperty.
        soMain.Update();
        soMain.FindProperty("assetRef").objectReferenceValue = assetA;
        soMain.ApplyModifiedProperties();
        Frames();
        Put("direct.so_to_ui", FmtObjField(ofMain) + "|t=" + FmtObj(tMain.assetRef) + "|evc=" + FmtInt(mainEvts.Count));

        // Direct C# external mutation -> UI.
        tMain.assetRef = assetC;
        Frames();
        Put("direct.external_mutate", FmtObjField(ofMain) + "|evc=" + FmtInt(mainEvts.Count));

        // UI -> SO wrong type assignment on bound ObjectField.
        ofMain.value = texA;
        Frames();
        Put("direct.ui_to_so_wrong_type", FmtObjField(ofMain) + "|t=" + FmtObj(tMain.assetRef) + "|evc=" + FmtInt(mainEvts.Count));

        // Reset to assetA for clean state, then test GameObject -> Component coercion on bound field.
        ofMain.value = assetA;
        Frames();
        var ofBehav = new ObjectField { objectType = typeof(ObjectProbeBehaviour) };
        m_Root.Add(ofBehav);
        ofBehav.BindProperty(soMain.FindProperty("behaviourRef"));
        Frames();
        ofBehav.value = goOtherComp;
        Frames();
        Put("direct.ui_to_so_coercion", FmtObjField(ofBehav) + "|t=" + FmtObj(tMain.behaviourRef));

        // Destroyed / missing reference on bound ObjectField.
        var tempBound = MakeAsset("TempBound", 77);
        tMain.otherAssetRef = tempBound;
        soMain.Update();
        var ofMiss = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        var missEvts = new List<string>();
        ofMiss.RegisterValueChangedCallback(e => missEvts.Add(FmtObj(e.previousValue) + "->" + FmtObj(e.newValue)));
        m_Root.Add(ofMiss);
        ofMiss.BindProperty(soMain.FindProperty("otherAssetRef"));
        Frames();
        string missBefore = FmtObjField(ofMiss);

        UnityEngine.Object.DestroyImmediate(tempBound);
        Frames();
        soMain.Update();
        var pMiss = soMain.FindProperty("otherAssetRef");
        bool missPropIsNull = pMiss.objectReferenceValue == null;
        bool missPropHasId = pMiss.objectReferenceInstanceIDValue != 0;
        string missAfterDestroy = FmtObjField(ofMiss) + "|pnull=" + FmtBool(missPropIsNull) + "|pid=" + FmtBool(missPropHasId) + "|evc=" + FmtInt(missEvts.Count);

        // Clearing missing reference by setting value = null.
        ofMiss.value = null;
        Frames();
        soMain.Update();
        pMiss = soMain.FindProperty("otherAssetRef");
        string missAfterClear = FmtObjField(ofMiss) +
            "|pnull=" + FmtBool(pMiss.objectReferenceValue == null) +
            "|pid=" + FmtBool(pMiss.objectReferenceInstanceIDValue != 0) +
            "|tref=" + FmtObj(tMain.otherAssetRef) +
            "|evc=" + FmtInt(missEvts.Count);
        Put("direct.destroyed_missing", missBefore + "||" + missAfterDestroy + "||" + missAfterClear);

        // Detached-then-attached deferred binding.
        var ofDet = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        var detEvts = new List<string>();
        ofDet.RegisterValueChangedCallback(e => detEvts.Add(FmtObj(e.previousValue) + "->" + FmtObj(e.newValue)));
        ofDet.BindProperty(soMain.FindProperty("assetRef"));
        string detBefore = FmtObjField(ofDet) + "|evc=" + FmtInt(detEvts.Count);
        m_Root.Add(ofDet);
        Frames();
        Put("direct.detached_then_attached", detBefore + "||" + FmtObjField(ofDet) + "|ev=" + string.Join("|", detEvts));

        // Custom INotifyValueChanged<UnityEngine.Object> and typed INotifyValueChanged<ObjectProbeAsset> (DefaultBindUnityObject).
        var custObj = new CustomNotifyObjectElement();
        m_Root.Add(custObj);
        custObj.BindProperty(soMain.FindProperty("assetRef"));
        Frames();
        string custObjInit = FmtObj(custObj.value) + "|n=" + FmtInt(custObj.notifyCount);
        custObj.value = assetB;
        Frames();
        string custObjUiToSo = FmtObj(custObj.value) + "|t=" + FmtObj(tMain.assetRef);
        tMain.assetRef = assetA;
        Frames();
        string custObjSoToUi = FmtObj(custObj.value);
        Put("direct.custom_notify_object", custObjInit + "||" + custObjUiToSo + "||" + custObjSoToUi);

        var custTyped = new CustomNotifyTypedAssetElement();
        m_Root.Add(custTyped);
        custTyped.BindProperty(soMain.FindProperty("assetRef"));
        Frames();
        string custTypedInit = FmtObj(custTyped.value) + "|n=" + FmtInt(custTyped.notifyCount);
        custTyped.value = assetC;
        Frames();
        string custTypedUiToSo = FmtObj(custTyped.value) + "|t=" + FmtObj(tMain.assetRef);
        tMain.assetRef = assetB;
        Frames();
        string custTypedSoToUi = FmtObj(custTyped.value);
        Put("direct.custom_notify_typed", custTypedInit + "||" + custTypedUiToSo + "||" + custTypedSoToUi);

        // 2. Multi-object mixed selection on ObjectField.
        var mA = ScriptableObject.CreateInstance<ObjectTarget>();
        var mB = ScriptableObject.CreateInstance<ObjectTarget>();
        mA.multiAssetRef = assetA;
        mB.multiAssetRef = assetB;
        mA.nullAssetRef = assetA;
        mB.nullAssetRef = null;
        var soMulti = new SerializedObject(new UnityEngine.Object[] { mA, mB });

        var ofMulti = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        var multiEvts = new List<string>();
        ofMulti.RegisterValueChangedCallback(e => multiEvts.Add(FmtObj(e.previousValue) + "->" + FmtObj(e.newValue)));
        m_Root.Add(ofMulti);
        ofMulti.BindProperty(soMulti.FindProperty("multiAssetRef"));
        Frames();
        Put("multi.initial_divergent", FmtObjField(ofMulti) + "|evc=" + FmtInt(multiEvts.Count));

        var ofMultiNull = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        m_Root.Add(ofMultiNull);
        ofMultiNull.BindProperty(soMulti.FindProperty("nullAssetRef"));
        Frames();
        Put("multi.initial_with_null", FmtObjField(ofMultiNull));

        // Edit mixed to new object C.
        ofMulti.value = assetC;
        Frames();
        Put("multi.ui_edit_new", FmtObjField(ofMulti) + "|a=" + FmtObj(mA.multiAssetRef) + "|b=" + FmtObj(mB.multiAssetRef) + "|evc=" + FmtInt(multiEvts.Count));

        // Diverge again (A vs B), then edit mixed to target[0]'s value (A).
        mA.multiAssetRef = assetA;
        mB.multiAssetRef = assetB;
        Frames();
        string multiReDiv = FmtObjField(ofMulti);
        ofMulti.value = assetA;
        Frames();
        Put("multi.ui_edit_to_first", multiReDiv + "||" + FmtObjField(ofMulti) + "|a=" + FmtObj(mA.multiAssetRef) + "|b=" + FmtObj(mB.multiAssetRef));

        // Diverge again (A vs B), then edit mixed to null.
        mA.multiAssetRef = assetA;
        mB.multiAssetRef = assetB;
        Frames();
        ofMulti.value = null;
        Frames();
        Put("multi.ui_edit_to_null", FmtObjField(ofMulti) + "|a=" + FmtObj(mA.multiAssetRef) + "|b=" + FmtObj(mB.multiAssetRef));

        // External converge and diverge.
        mA.multiAssetRef = assetA;
        mB.multiAssetRef = assetB;
        Frames();
        mB.multiAssetRef = assetA;
        Frames();
        string afterConverge = FmtObjField(ofMulti);
        mB.multiAssetRef = assetC;
        Frames();
        string afterDiverge = FmtObjField(ofMulti);
        Put("multi.external_converge_diverge", afterConverge + "||" + afterDiverge);

        // Missing reference vs null in multi-object.
        var tempMulti = MakeAsset("TempMulti", 88);
        var mMissA = ScriptableObject.CreateInstance<ObjectTarget>();
        var mMissB = ScriptableObject.CreateInstance<ObjectTarget>();
        mMissA.multiAssetRef = tempMulti;
        mMissB.multiAssetRef = null;
        UnityEngine.Object.DestroyImmediate(tempMulti);
        var soMultiMiss = new SerializedObject(new UnityEngine.Object[] { mMissA, mMissB });
        var pMultiMiss = soMultiMiss.FindProperty("multiAssetRef");
        var ofMultiMiss = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        m_Root.Add(ofMultiMiss);
        ofMultiMiss.BindProperty(pMultiMiss);
        Frames();
        Put("multi.missing_vs_null", "diff=" + FmtBool(pMultiMiss.hasMultipleDifferentValues) + "|" + FmtObjField(ofMultiMiss));

        // 3. Tree Bind(so): top-level, nested dotted, relative parent-child, array element, native collider material, siblings.
        var tTree = ScriptableObject.CreateInstance<ObjectTarget>();
        tTree.assetRef = assetA;
        tTree.anyObjRef = texA;
        tTree.transformRef = goWithComp.transform;
        tTree.goRef = goPlain;
        tTree.nested = new NestedObjectData { asset = assetB, deep = new DeepObjectData { asset = assetC } };
        tTree.assetArray = new[] { assetA, assetB, assetC };
        var soTree = new SerializedObject(tTree);

        var treeRoot = new VisualElement();
        var trAsset = new ObjectField { bindingPath = "assetRef", objectType = typeof(ObjectProbeAsset) };
        var trAny = new ObjectField { bindingPath = "anyObjRef", objectType = typeof(UnityEngine.Object) };
        var trTrans = new ObjectField { bindingPath = "transformRef", objectType = typeof(Transform) };
        var trGo = new ObjectField { bindingPath = "goRef", objectType = typeof(GameObject) };
        var trDeep = new ObjectField { bindingPath = "nested.deep.asset", objectType = typeof(ObjectProbeAsset) };
        var trParent = new BindableElement { bindingPath = "nested" };
        var trRelAsset = new ObjectField { bindingPath = "asset", objectType = typeof(ObjectProbeAsset) };
        trParent.Add(trRelAsset);
        var trArr1 = new ObjectField { bindingPath = "assetArray.Array.data[1]", objectType = typeof(ObjectProbeAsset) };
        var trGrad = new GradientField { bindingPath = "siblingGradient" };
        var trCurve = new CurveField { bindingPath = "siblingCurve" };
        var trBInt = new BoundsIntField { bindingPath = "siblingBoundsInt" };
        var trCol = new ColorField { bindingPath = "siblingColor" };
        var trV3 = new Vector3Field { bindingPath = "siblingVec3" };
        var trInt = new IntegerField { bindingPath = "siblingInt" };
        var trEnum = new PopupField<string> { bindingPath = "siblingEnum" };
        treeRoot.Add(trAsset);
        treeRoot.Add(trAny);
        treeRoot.Add(trTrans);
        treeRoot.Add(trGo);
        treeRoot.Add(trDeep);
        treeRoot.Add(trParent);
        treeRoot.Add(trArr1);
        treeRoot.Add(trGrad);
        treeRoot.Add(trCurve);
        treeRoot.Add(trBInt);
        treeRoot.Add(trCol);
        treeRoot.Add(trV3);
        treeRoot.Add(trInt);
        treeRoot.Add(trEnum);
        m_Root.Add(treeRoot);
        treeRoot.Bind(soTree);

        var trIcon = new ObjectField { bindingPath = "m_Icon", objectType = typeof(Texture2D) };
        m_Root.Add(trIcon);
        trIcon.Bind(soIcon);
        Frames();
        Put("tree.initial",
            "a=" + FmtObj(trAsset.value) + "|any=" + FmtObj(trAny.value) + "|tr=" + FmtObj(trTrans.value) +
            "|go=" + FmtObj(trGo.value) + "|deep=" + FmtObj(trDeep.value) + "|rel=" + FmtObj(trRelAsset.value) +
            "|arr1=" + FmtObj(trArr1.value) + "|ic=" + FmtObj(trIcon.value) +
            "|g=" + FmtGradShort(trGrad.value) + "|c=" + FmtCurveShort(trCurve.value) +
            "|bi=" + FmtBoundsInt(trBInt.value) + "|col=" + FmtColor(trCol.value) +
            "|v3=" + FmtVec3(trV3.value) + "|i=" + FmtInt(trInt.value) + "|e=" + trEnum.value);

        trAsset.value = assetC;
        trDeep.value = assetA;
        trRelAsset.value = subAsset;
        trArr1.value = null;
        trIcon.value = texB;
        trInt.value = 99;
        Frames();
        soIcon.Update();
        Put("tree.ui_to_so",
            "a=" + FmtObj(tTree.assetRef) + "|deep=" + FmtObj(tTree.nested.deep.asset) +
            "|rel=" + FmtObj(tTree.nested.asset) + "|arr1=" + FmtObj(tTree.assetArray[1]) +
            "|ic=" + FmtObj(soIcon.FindProperty("m_Icon").objectReferenceValue) + "|i=" + FmtInt(tTree.siblingInt));

        tTree.assetRef = assetB;
        tTree.nested = new NestedObjectData { asset = assetA, deep = new DeepObjectData { asset = subAsset } };
        tTree.assetArray[1] = assetC;
        soIcon.FindProperty("m_Icon").objectReferenceValue = texA;
        soIcon.ApplyModifiedProperties();
        Frames();
        Put("tree.so_to_ui",
            "a=" + FmtObj(trAsset.value) + "|deep=" + FmtObj(trDeep.value) +
            "|rel=" + FmtObj(trRelAsset.value) + "|arr1=" + FmtObj(trArr1.value) +
            "|ic=" + FmtObj(trIcon.value));

        // 4. PropertyField for ObjectReference: type inference, labels, events, missing ref, reuse, cross-type rebind, multi.
        var tPf = ScriptableObject.CreateInstance<ObjectTarget>();
        tPf.anyObjRef = assetA;
        tPf.assetRef = assetA;
        tPf.otherAssetRef = assetB;
        tPf.behaviourRef = compA;
        tPf.transformRef = goWithComp.transform;
        tPf.goRef = goPlain;
        tPf.materialRef = matA;
        tPf.textureRef = texA;
        var soPf = new SerializedObject(tPf);

        var pfAsset = new PropertyField(soPf.FindProperty("assetRef"));
        var pfEvts = new List<string>();
        pfAsset.RegisterValueChangeCallback(e => pfEvts.Add(FmtObj(e.changedProperty.objectReferenceValue)));
        m_Root.Add(pfAsset);
        pfAsset.Bind(soPf);
        Frames();
        Put("pf.asset_initial", DescribePf(pfAsset) + "|ec=" + FmtInt(pfEvts.Count));

        // Type inference across diverse ObjectReference property types (script + native PPtr).
        var pfAny = new PropertyField(soPf.FindProperty("anyObjRef"));
        var pfBehav = new PropertyField(soPf.FindProperty("behaviourRef"));
        var pfTrans = new PropertyField(soPf.FindProperty("transformRef"));
        var pfGo = new PropertyField(soPf.FindProperty("goRef"));
        var pfMat = new PropertyField(soPf.FindProperty("materialRef"));
        var pfTex = new PropertyField(soPf.FindProperty("textureRef"));
        var pfScript = new PropertyField(soComp.FindProperty("m_Script"));
        var pfIcon = new PropertyField(soIcon.FindProperty("m_Icon"));
        m_Root.Add(pfAny);
        m_Root.Add(pfBehav);
        m_Root.Add(pfTrans);
        m_Root.Add(pfGo);
        m_Root.Add(pfMat);
        m_Root.Add(pfTex);
        m_Root.Add(pfScript);
        m_Root.Add(pfIcon);
        pfAny.Bind(soPf);
        pfBehav.Bind(soPf);
        pfTrans.Bind(soPf);
        pfGo.Bind(soPf);
        pfMat.Bind(soPf);
        pfTex.Bind(soPf);
        pfScript.Bind(soComp);
        pfIcon.Bind(soIcon);
        Frames();
        Put("pf.types",
            "any=" + DescribePf(pfAny) +
            "||behav=" + DescribePf(pfBehav) +
            "||trans=" + DescribePf(pfTrans) +
            "||go=" + DescribePf(pfGo) +
            "||mat=" + DescribePf(pfMat) +
            "||tex=" + DescribePf(pfTex) +
            "||script=" + FmtType(pfScript.Q<ObjectField>()?.objectType) + ":" + FmtBool(pfScript.Q<ObjectField>()?.value != null) +
            "||icon=" + DescribePf(pfIcon));

        // Custom and dynamic labels.
        var pfLbl = new PropertyField(soPf.FindProperty("otherAssetRef"), "Custom Ref Label");
        m_Root.Add(pfLbl);
        pfLbl.Bind(soPf);
        Frames();
        string pfLblCustom = DescribePf(pfLbl);
        pfLbl.label = "Dynamic Ref Label";
        Frames();
        Put("pf.labels", pfLblCustom + "||" + DescribePf(pfLbl));

        // UI -> SO and SO -> UI through PropertyField.
        var pfChildOf = pfAsset.Q<ObjectField>();
        pfChildOf.value = assetB;
        Frames();
        Put("pf.ui_to_so", DescribePf(pfAsset) + "|t=" + FmtObj(tPf.assetRef) + "|ec=" + FmtInt(pfEvts.Count));

        soPf.Update();
        soPf.FindProperty("assetRef").objectReferenceValue = assetC;
        soPf.ApplyModifiedProperties();
        Frames();
        Put("pf.so_to_ui", DescribePf(pfAsset) + "|t=" + FmtObj(tPf.assetRef) + "|ec=" + FmtInt(pfEvts.Count));

        // Missing reference inside PropertyField.
        var tempPf = MakeAsset("TempPf", 55);
        tPf.otherAssetRef = tempPf;
        soPf.Update();
        var pfMiss = new PropertyField(soPf.FindProperty("otherAssetRef"));
        m_Root.Add(pfMiss);
        pfMiss.Bind(soPf);
        Frames();
        string pfMissBefore = DescribePf(pfMiss);
        UnityEngine.Object.DestroyImmediate(tempPf);
        Frames();
        string pfMissAfter = DescribePf(pfMiss);
        pfMiss.Q<ObjectField>().value = null;
        Frames();
        string pfMissCleared = DescribePf(pfMiss);
        Put("pf.destroyed_missing", pfMissBefore + "||" + pfMissAfter + "||" + pfMissCleared);

        // Same-type reuse (ObjectProbeAsset -> Texture2D) and cross-type rebind.
        var pfReuse = new PropertyField(soPf.FindProperty("assetRef"));
        m_Root.Add(pfReuse);
        pfReuse.Bind(soPf);
        Frames();
        var origOf = pfReuse.Q<ObjectField>();
        pfReuse.BindProperty(soPf.FindProperty("textureRef"));
        Frames();
        var reusedOf = pfReuse.Q<ObjectField>();
        Put("pf.reuse_same_type", FmtBool(ReferenceEquals(origOf, reusedOf)) + "|" + DescribePf(pfReuse));

        pfReuse.BindProperty(soPf.FindProperty("siblingGradient"));
        Frames();
        string xGrad = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingCurve"));
        Frames();
        string xCurve = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingBoundsInt"));
        Frames();
        string xBInt = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingColor"));
        Frames();
        string xCol = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingVec3"));
        Frames();
        string xV3 = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingVec4"));
        Frames();
        string xV4 = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("siblingInt"));
        Frames();
        string xInt = DescribePf(pfReuse);
        pfReuse.BindProperty(soPf.FindProperty("assetRef"));
        Frames();
        string xBack = DescribePf(pfReuse);
        Put("pf.rebind_cross_type",
            xGrad + "||" + xCurve + "||" + xBInt + "||" + xCol + "||" + xV3 + "||" + xV4 + "||" + xInt + "||" + xBack);

        // PropertyField multi-object mixed selection.
        var pmA = ScriptableObject.CreateInstance<ObjectTarget>();
        var pmB = ScriptableObject.CreateInstance<ObjectTarget>();
        pmA.multiAssetRef = assetA;
        pmB.multiAssetRef = assetB;
        var soPfMulti = new SerializedObject(new UnityEngine.Object[] { pmA, pmB });
        var pfMulti = new PropertyField(soPfMulti.FindProperty("multiAssetRef"), "Multi Ref");
        var pfMultiEvts = new List<string>();
        pfMulti.RegisterValueChangeCallback(e => pfMultiEvts.Add(FmtObj(e.changedProperty.objectReferenceValue)));
        m_Root.Add(pfMulti);
        pfMulti.Bind(soPfMulti);
        Frames();
        Put("pf.multi.initial", DescribePf(pfMulti) + "|ec=" + FmtInt(pfMultiEvts.Count));

        pfMulti.Q<ObjectField>().value = assetC;
        Frames();
        Put("pf.multi.ui_edit", DescribePf(pfMulti) + "|a=" + FmtObj(pmA.multiAssetRef) + "|b=" + FmtObj(pmB.multiAssetRef) + "|ec=" + FmtInt(pfMultiEvts.Count));

        // 5. Unbind, rebind across properties/SOs, runtime DataBinding coexistence, incompatible field warnings.
        var tRe1 = ScriptableObject.CreateInstance<ObjectTarget>();
        var tRe2 = ScriptableObject.CreateInstance<ObjectTarget>();
        tRe1.assetRef = assetA;
        tRe1.otherAssetRef = assetB;
        tRe2.assetRef = assetC;
        var soRe1 = new SerializedObject(tRe1);
        var soRe2 = new SerializedObject(tRe2);

        var rtSource = new ObjectRuntimeSource();
        var ofRe = new ObjectField { objectType = typeof(ObjectProbeAsset) };
        ofRe.dataSource = rtSource;
        ofRe.SetBinding("tooltip", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(ObjectRuntimeSource.tip)),
            bindingMode = BindingMode.ToTarget
        });
        m_Root.Add(ofRe);
        ofRe.BindProperty(soRe1.FindProperty("assetRef"));
        Frames();
        Put("unbind_rebind.initial", FmtObjField(ofRe) + "|tip=" + (ofRe.tooltip ?? "null"));

        ofRe.Unbind();
        Frames();
        ofRe.value = subAsset;
        soRe1.FindProperty("assetRef").objectReferenceValue = assetB;
        soRe1.ApplyModifiedProperties();
        rtSource.tip = "rt-obj-tip-1";
        Frames();
        Put("unbind_rebind.after_unbind", FmtObjField(ofRe) + "|t1=" + FmtObj(tRe1.assetRef) + "|tip=" + (ofRe.tooltip ?? "null"));

        ofMulti.Unbind();
        Frames();
        Put("unbind_rebind.after_mixed_unbind", FmtObjField(ofMulti));

        ofRe.BindProperty(soRe1.FindProperty("otherAssetRef"));
        Frames();
        Put("unbind_rebind.rebind_prop", FmtObjField(ofRe));
        ofRe.value = assetC;
        Frames();
        Put("unbind_rebind.rebind_prop_edit", FmtObj(tRe1.otherAssetRef) + "|t1_a=" + FmtObj(tRe1.assetRef));

        ofRe.BindProperty(soRe2.FindProperty("assetRef"));
        Frames();
        Put("unbind_rebind.rebind_so2", FmtObjField(ofRe));
        ofRe.value = assetA;
        Frames();
        Put("unbind_rebind.rebind_so2_edit", FmtObj(tRe2.assetRef) + "|t1_other=" + FmtObj(tRe1.otherAssetRef));

        var pfRe = new PropertyField(soRe1.FindProperty("assetRef"));
        m_Root.Add(pfRe);
        pfRe.Bind(soRe1);
        Frames();
        pfRe.Unbind();
        soRe1.FindProperty("assetRef").objectReferenceValue = assetC;
        soRe1.ApplyModifiedProperties();
        Frames();
        Put("pf.after_unbind", DescribePf(pfRe));
        pfRe.Bind(soRe2);
        Frames();
        Put("pf.after_rebind_so2", DescribePf(pfRe));

        // Incompatible field warnings: IntegerField and mismatched typed INotifyValueChanged<Texture2D>.
        Application.LogCallback capture = (msg, stack, type) =>
        {
            if (type == LogType.Warning && msg.Contains("not compatible"))
                m_Logs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += capture;
        var badInt = new IntegerField();
        m_Root.Add(badInt);
        badInt.BindProperty(soRe1.FindProperty("assetRef"));
        var badTex = new CustomNotifyTextureElement();
        m_Root.Add(badTex);
        badTex.BindProperty(soRe1.FindProperty("assetRef"));
        Frames();
        Application.logMessageReceived -= capture;
        Put("incompatible.fields", "int=" + FmtInt(badInt.value) + "|tex=" + FmtObj(badTex.value));
#endif
    }
}
