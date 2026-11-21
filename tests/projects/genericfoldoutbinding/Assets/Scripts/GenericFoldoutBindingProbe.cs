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

public class GenericFoldoutRuntimeModel
{
    [CreateProperty]
    public string tip { get; set; } = "rt-gf-0";
}

public class GenericFoldoutBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("GenericFoldoutBindingProbe");
        var doc = go.AddComponent<UIDocument>();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        doc.panelSettings = ps;
        go.AddComponent<GenericFoldoutBindingProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("GENERIC_FOLDOUT_BINDING_OUT");
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
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string B(bool v) => v ? "1" : "0";
    static string F(float v) => v.ToString("R", Inv);

#if UNITY_EDITOR
    static string PropMeta(SerializedProperty p) =>
        $"{p.propertyType}|{p.type}|ch={B(p.hasChildren)}|vch={B(p.hasVisibleChildren)}|arr={B(p.isArray)}|dn={p.displayName}";

    static string FoldoutInfo(Foldout f)
    {
        if (f == null) return "null";
        var toggle = f.Q<Toggle>(null, Foldout.toggleUssClassName);
        var lbl = toggle?.Q<Label>(null, Toggle.textUssClassName);
        return $"name={f.name}|bp={f.bindingPath}|txt={f.text}|val={B(f.value)}|cc={f.childCount}|ltxt={lbl?.text}|lbp={lbl?.bindingPath}";
    }

    static string ChildSummary(Foldout f)
    {
        var parts = new List<string>();
        for (int i = 0; i < f.childCount; i++)
        {
            if (f[i] is PropertyField pf)
            {
                var inner = pf.hierarchy.childCount > 0 ? pf.hierarchy[0] : null;
                string innerDesc = inner switch
                {
                    IntegerField inf => $"Int({inf.label}={inf.value},m={B(inf.showMixedValue)})",
                    FloatField flf => $"Float({flf.label}={F(flf.value)},m={B(flf.showMixedValue)})",
                    TextField tf => $"Text({tf.label}={tf.value},m={B(tf.showMixedValue)})",
                    Toggle tg => $"Toggle({tg.label}={B(tg.value)},m={B(tg.showMixedValue)})",
                    Vector3Field v3 => $"Vec3({v3.label}={F(v3.value.x)},{F(v3.value.y)},{F(v3.value.z)})",
                    ColorField cf => $"Color({cf.label}={F(cf.value.r)},{F(cf.value.g)},{F(cf.value.b)},{F(cf.value.a)})",
                    Foldout cf => $"Foldout({cf.name},{cf.text},val={B(cf.value)},cc={cf.childCount})",
                    null => "null",
                    _ => inner.GetType().Name
                };
                parts.Add($"{pf.name}[{pf.bindingPath}]->{innerDesc}");
            }
            else
            {
                parts.Add(f[i].GetType().Name);
            }
        }
        return string.Join(";", parts);
    }

    void Run()
    {
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 600f;
        m_Root.style.height = 800f;
        Frames();

        Application.LogCallback onLog = (cond, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{cond}");
        };
        Application.logMessageReceived += onLog;

        var t1 = ScriptableObject.CreateInstance<GenericFoldoutTarget>();
        var t2 = ScriptableObject.CreateInstance<GenericFoldoutTarget>();
        t2.basic = new BasicInlineData { count = 77, label = "omega", flag = true };

        var so = new SerializedObject(t1);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { t1, t2 });
        try
        {
            // 1. Metadata
            Put("prop.meta.basic", PropMeta(so.FindProperty("basic")));
            Put("prop.meta.outer", PropMeta(so.FindProperty("outer")));
            Put("prop.meta.outer_inner", PropMeta(so.FindProperty("outer.inner")));
            Put("prop.meta.empty", PropMeta(so.FindProperty("emptyStruct")));
            Put("prop.meta.hidden_only", PropMeta(so.FindProperty("hiddenOnly")));
            Put("prop.meta.mixed_vis", PropMeta(so.FindProperty("mixedVis")));
            Put("prop.meta.string_first", PropMeta(so.FindProperty("stringFirst")));
            Put("prop.meta.string_first_elem0", PropMeta(so.FindProperty("stringFirstArray").GetArrayElementAtIndex(0)));
            Put("prop.meta.string_first_elem1", PropMeta(so.FindProperty("stringFirstArray").GetArrayElementAtIndex(1)));

            // 2. Basic collapsed (lazy children), expand, UI->SO and external->UI edits, collapse
            var pBasic = so.FindProperty("basic");
            pBasic.isExpanded = false;
            var pfBasic = new PropertyField(pBasic);
            var parentEvents = new List<string>();
            pfBasic.RegisterValueChangeCallback(e => parentEvents.Add(e.changedProperty.propertyPath));
            var bubbledEvents = new List<string>();
            pfBasic.RegisterCallback<SerializedPropertyChangeEvent>(e => bubbledEvents.Add(e.changedProperty.propertyPath));
            m_Root.Add(pfBasic);
            pfBasic.Bind(so);
            Frames();

            var fBasic = pfBasic.hierarchy.childCount > 0 ? pfBasic.hierarchy[0] as Foldout : null;
            Put("pf.basic.collapsed", $"{pfBasic.hierarchy.childCount}|{FoldoutInfo(fBasic)}|exp={B(pBasic.isExpanded)}");

            fBasic.value = true;
            Frames();
            Put("pf.basic.expanded", $"{FoldoutInfo(fBasic)}|exp={B(pBasic.isExpanded)}|children={ChildSummary(fBasic)}");

            var countField = fBasic.Q<IntegerField>("unity-input-basic.count");
            var labelField = fBasic.Q<TextField>("unity-input-basic.label");
            var flagField = fBasic.Q<Toggle>("unity-input-basic.flag");
            parentEvents.Clear();
            bubbledEvents.Clear();
            countField.value = 123;
            labelField.value = "edited";
            flagField.value = false;
            Frames();
            Put("pf.basic.ui_to_so", $"t={t1.basic.count},{t1.basic.label},{B(t1.basic.flag)}|pe={string.Join(",", parentEvents)}|be={string.Join(",", bubbledEvents)}");

            t1.basic.count = 456;
            t1.basic.label = "external";
            t1.basic.flag = true;
            Frames();
            Put("pf.basic.external_to_ui", $"ui={countField.value},{labelField.value},{B(flagField.value)}");

            fBasic.value = false;
            Frames();
            Put("pf.basic.collapsed_after", $"{FoldoutInfo(fBasic)}|exp={B(pBasic.isExpanded)}");

            // 3. Pre-expanded before Bind
            var pPre = so.FindProperty("preExpanded");
            pPre.isExpanded = true;
            var pfPre = new PropertyField(pPre);
            m_Root.Add(pfPre);
            pfPre.Bind(so);
            Frames();
            var fPre = pfPre.hierarchy[0] as Foldout;
            Put("pf.pre_expanded", $"{FoldoutInfo(fPre)}|children={ChildSummary(fPre)}");

            // 4. Nested generic foldouts (outer -> outer.inner)
            var pOuter = so.FindProperty("outer");
            var pInner = so.FindProperty("outer.inner");
            pOuter.isExpanded = false;
            pInner.isExpanded = false;
            var pfOuter = new PropertyField(pOuter);
            m_Root.Add(pfOuter);
            pfOuter.Bind(so);
            Frames();
            var fOuter = pfOuter.hierarchy[0] as Foldout;
            string outerCollapsed = FoldoutInfo(fOuter);

            fOuter.value = true;
            Frames();
            string outerExpanded = $"{FoldoutInfo(fOuter)}|children={ChildSummary(fOuter)}";

            var fInner = fOuter.Q<Foldout>("unity-foldout-outer.inner");
            fInner.value = true;
            Frames();
            string innerExpanded = $"{FoldoutInfo(fInner)}|exp={B(pInner.isExpanded)}|children={ChildSummary(fInner)}";

            var codeField = fInner.Q<IntegerField>("unity-input-outer.inner.code");
            codeField.value = 202;
            Frames();
            int afterUiCode = t1.outer.inner.code;
            t1.outer.inner.code = 303;
            t1.outer.inner.tag = "ext-tag";
            Frames();
            var tagField = fInner.Q<TextField>("unity-input-outer.inner.tag");
            Put("pf.nested", $"{outerCollapsed}###{outerExpanded}###{innerExpanded}###ui={afterUiCode}|ext={codeField.value},{tagField.value}");

            // 5. Explicit custom label, empty string "", and dynamic label updates
            var pfCustom = new PropertyField(so.FindProperty("small"), "Custom Small");
            var pfEmptyLbl = new PropertyField(so.FindProperty("small"), "");
            m_Root.Add(pfCustom);
            m_Root.Add(pfEmptyLbl);
            pfCustom.Bind(so);
            pfEmptyLbl.Bind(so);
            Frames();
            var fCustom = pfCustom.hierarchy[0] as Foldout;
            var fEmptyLbl = pfEmptyLbl.hierarchy[0] as Foldout;
            string sCustom0 = FoldoutInfo(fCustom);
            string sEmptyLbl0 = FoldoutInfo(fEmptyLbl);

            pfCustom.label = "Renamed Small";
            Frames();
            var fCustom1 = pfCustom.hierarchy[0] as Foldout;
            string sCustom1 = $"same={B(ReferenceEquals(fCustom, fCustom1))}|{FoldoutInfo(fCustom1)}";

            pfCustom.label = null;
            Frames();
            var fCustom2 = pfCustom.hierarchy[0] as Foldout;
            string sCustom2 = $"same={B(ReferenceEquals(fCustom, fCustom2))}|{FoldoutInfo(fCustom2)}";

            pfCustom.label = "Back Again";
            Frames();
            var fCustom3 = pfCustom.hierarchy[0] as Foldout;
            string sCustom3 = $"same={B(ReferenceEquals(fCustom, fCustom3))}|{FoldoutInfo(fCustom3)}";
            Put("pf.labels", $"{sCustom0}###{sEmptyLbl0}###{sCustom1}###{sCustom2}###{sCustom3}");

            // 6. Empty struct, hidden-only struct, mixed-visibility struct
            var pfEmpty = new PropertyField(so.FindProperty("emptyStruct"));
            var pfHidden = new PropertyField(so.FindProperty("hiddenOnly"));
            var pMixVis = so.FindProperty("mixedVis");
            pMixVis.isExpanded = true;
            var pfMixVis = new PropertyField(pMixVis);
            m_Root.Add(pfEmpty);
            m_Root.Add(pfHidden);
            m_Root.Add(pfMixVis);
            pfEmpty.Bind(so);
            pfHidden.Bind(so);
            pfMixVis.Bind(so);
            Frames();
            var fMixVis = pfMixVis.hierarchy.childCount > 0 ? pfMixVis.hierarchy[0] as Foldout : null;
            Put("pf.empty_and_hidden", $"empty={pfEmpty.hierarchy.childCount}|hidden={pfHidden.hierarchy.childCount}|mix={FoldoutInfo(fMixVis)}|children={ChildSummary(fMixVis)}");

            // 7. Direct Foldout.BindProperty & tree Bind with nested BindableElement + PropertyField + relative scalar
            var directFoldout = new Foldout();
            m_Root.Add(directFoldout);
            var pSmall = so.FindProperty("small");
            pSmall.isExpanded = false;
            directFoldout.BindProperty(pSmall);
            Frames();
            bool d0 = directFoldout.value;
            directFoldout.value = true;
            Frames();
            bool d1 = pSmall.isExpanded;
            pSmall.isExpanded = false;
            Frames();
            bool d2 = directFoldout.value;

            var treeRoot = new VisualElement();
            var outerScope = new BindableElement { bindingPath = "outer" };
            var relInnerPf = new PropertyField { bindingPath = "inner" };
            var relCodeField = new IntegerField { bindingPath = "inner.code" };
            outerScope.Add(relInnerPf);
            outerScope.Add(relCodeField);
            treeRoot.Add(outerScope);
            m_Root.Add(treeRoot);
            pInner.isExpanded = true;
            treeRoot.Bind(so);
            Frames();
            var relInnerFoldout = relInnerPf.hierarchy.childCount > 0 ? relInnerPf.hierarchy[0] as Foldout : null;
            Put("direct.foldout_and_tree", $"d={B(d0)},{B(d1)},{B(d2)}|relCode={relCodeField.value}|relInner={FoldoutInfo(relInnerFoldout)}|children={ChildSummary(relInnerFoldout)}");

            // 8. Multi-object mixed & aligned child fields
            var pMultiBasic = soMulti.FindProperty("basic");
            pMultiBasic.isExpanded = true;
            var pfMulti = new PropertyField(pMultiBasic);
            m_Root.Add(pfMulti);
            pfMulti.Bind(soMulti);
            Frames();
            var fMulti = pfMulti.hierarchy[0] as Foldout;
            string multiBefore = ChildSummary(fMulti);
            var multiCount = fMulti.Q<IntegerField>("unity-input-basic.count");
            multiCount.value = 900;
            Frames();
            string multiAfter = $"t1={t1.basic.count},t2={t2.basic.count},l1={t1.basic.label},l2={t2.basic.label}|{ChildSummary(fMulti)}";
            Put("pf.multi", $"{multiBefore}###{multiAfter}");

            // 9. Rebinding: Generic->Generic (trim), expanded->collapsed, Generic->empty Generic, Generic<->scalar
            pBasic.isExpanded = true;
            pSmall.isExpanded = true;
            var pfRebind = new PropertyField(pBasic);
            m_Root.Add(pfRebind);
            pfRebind.Bind(so);
            Frames();
            var fR0 = pfRebind.hierarchy[0] as Foldout;
            var child0Orig = fR0[0];
            string r0 = $"{FoldoutInfo(fR0)}|children={ChildSummary(fR0)}";

            pfRebind.BindProperty(pSmall);
            Frames();
            var fR1 = pfRebind.hierarchy[0] as Foldout;
            string r1 = $"sameF={B(ReferenceEquals(fR0, fR1))}|sameC0={B(ReferenceEquals(child0Orig, fR1[0]))}|{FoldoutInfo(fR1)}|children={ChildSummary(fR1)}";

            pBasic.isExpanded = false;
            pfRebind.BindProperty(pBasic);
            Frames();
            var fR2 = pfRebind.hierarchy[0] as Foldout;
            string r2Collapsed = $"sameF={B(ReferenceEquals(fR0, fR2))}|{FoldoutInfo(fR2)}|children={ChildSummary(fR2)}";
            fR2.value = true;
            Frames();
            string r2Expanded = $"{FoldoutInfo(fR2)}|children={ChildSummary(fR2)}";

            string r3Empty;
            try
            {
                pfRebind.BindProperty(so.FindProperty("emptyStruct"));
                Frames();
                r3Empty = $"ok:hc={pfRebind.hierarchy.childCount}";
            }
            catch (Exception ex)
            {
                r3Empty = $"ex={ex.GetType().Name}:{ex.Message}|hc={pfRebind.hierarchy.childCount}";
            }

            pfRebind.BindProperty(so.FindProperty("topInt"));
            Frames();
            var r4Int = pfRebind.hierarchy.childCount > 0 ? pfRebind.hierarchy[0] as IntegerField : null;
            string r4 = $"hc={pfRebind.hierarchy.childCount}|type={r4Int?.GetType().Name}|val={r4Int?.value}";

            pSmall.isExpanded = true;
            pfRebind.BindProperty(pSmall);
            Frames();
            var fR5 = pfRebind.hierarchy.childCount > 0 ? pfRebind.hierarchy[0] as Foldout : null;
            string r5 = $"hc={pfRebind.hierarchy.childCount}|{FoldoutInfo(fR5)}|children={ChildSummary(fR5)}";
            Put("pf.rebind", $"{r0}###{r1}###{r2Collapsed}###{r2Expanded}###{r3Empty}###{r4}###{r5}");

            // 10. Unbind, rebind, and runtime DataBinding coexistence
            var rtModel = new GenericFoldoutRuntimeModel();
            pSmall.isExpanded = true;
            var pfUb = new PropertyField(pSmall) { dataSource = rtModel };
            pfUb.SetBinding("tooltip", new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(GenericFoldoutRuntimeModel.tip)),
                bindingMode = BindingMode.ToTarget
            });
            m_Root.Add(pfUb);
            pfUb.Bind(so);
            Frames();
            var fUb = pfUb.hierarchy[0] as Foldout;
            var wField = fUb.Q<FloatField>("unity-input-small.weight");
            string ub0 = $"tip={pfUb.tooltip}|w={F(wField.value)}|exp={B(fUb.value)}";

            pfUb.Unbind();
            t1.small.weight = 9.25f;
            pSmall.isExpanded = false;
            rtModel.tip = "rt-gf-1";
            Frames();
            string ub1 = $"tip={pfUb.tooltip}|w={F(wField.value)}|exp={B(fUb.value)}";

            pfUb.Bind(so);
            Frames();
            var fUb2 = pfUb.hierarchy[0] as Foldout;
            fUb2.value = true;
            Frames();
            var wField2 = fUb2.Q<FloatField>("unity-input-small.weight");
            string ub2 = $"tip={pfUb.tooltip}|w={F(wField2.value)}|exp={B(fUb2.value)}";
            Put("unbind_rebind", $"{ub0}###{ub1}###{ub2}");

            // 11. String-first struct displayName (standalone and array element) & titleLabel update
            var pArr0 = so.FindProperty("stringFirstArray").GetArrayElementAtIndex(0);
            var pfArr0 = new PropertyField(pArr0);
            m_Root.Add(pfArr0);
            pfArr0.Bind(so);
            Frames();
            var fArr0 = pfArr0.hierarchy[0] as Foldout;
            string arr0Before = FoldoutInfo(fArr0);
            t1.stringFirstArray[0].name = "Paladin";
            Frames();
            string arr0After = FoldoutInfo(fArr0);
            t1.stringFirstArray[0].name = "";
            Frames();
            string arr0Blank = FoldoutInfo(fArr0);
            Put("pf.string_first_elem_title", $"{arr0Before}###{arr0After}###{arr0Blank}");
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            soMulti.Dispose();
            so.Dispose();
            UnityEngine.Object.DestroyImmediate(t1);
            UnityEngine.Object.DestroyImmediate(t2);
        }
    }
#else
    void Run() { }
#endif
}
