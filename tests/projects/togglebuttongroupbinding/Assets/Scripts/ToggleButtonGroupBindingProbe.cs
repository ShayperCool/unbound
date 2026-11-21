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

public class ToggleButtonGroupBindingProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("ToggleButtonGroupBindingProbe");
        var doc = go.AddComponent<UIDocument>();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        doc.panelSettings = ps;
        go.AddComponent<ToggleButtonGroupBindingProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("TOGGLE_BUTTON_GROUP_BINDING_OUT");
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
        Application.Quit(0);
#endif
    }

    static Type RuntimeUtil => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    static void Frames(int count = 4)
    {
        var update = RuntimeUtil.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var repaint = RuntimeUtil.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < count; i++)
        {
            update.Invoke(null, null);
            repaint.Invoke(null, new object[] { false });
            System.Threading.Thread.Sleep(35);
        }
        update.Invoke(null, null);
        repaint.Invoke(null, new object[] { false });
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

    static string StateStr(ToggleButtonGroupState s)
    {
        var bits = new StringBuilder(s.length);
        for (int i = 0; i < s.length; i++) bits.Append(s[i] ? '1' : '0');
        return $"{s.length}:{bits}";
    }

    static string GroupInfo(ToggleButtonGroup bg)
    {
        if (bg == null) return "null";
        var btns = bg.Query<Button>(null, ToggleButtonGroup.buttonClassName).ToList();
        var texts = string.Join(",", btns.Select(b => b.text));
        bool hasEmpty = bg.Q<Label>(null, ToggleButtonGroup.emptyStateLabelClassName) != null;
        return $"name={bg.name}|lbl={bg.label}|val={StateStr(bg.value)}|m={B(bg.showMixedValue)}|multi={B(bg.isMultipleSelection)}|emptySel={B(bg.allowEmptySelection)}|fixed={B(bg.ClassListContains(ToggleButtonGroup.ussClassName + "--fixed-size"))}|btns=[{texts}]|emptyLbl={B(hasEmpty)}";
    }

#if UNITY_EDITOR
    void Run()
    {
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 640f;
        m_Root.style.height = 900f;

        Application.LogCallback onLog = (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };
        Application.logMessageReceived += onLog;

        var t1 = ScriptableObject.CreateInstance<ToggleButtonGroupTarget>();
        var t2 = ScriptableObject.CreateInstance<ToggleButtonGroupTarget>();
        t2.multiDiff = new ToggleButtonGroupState(0b011UL, 4);

        var so = new SerializedObject(t1);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { t1, t2 });
        try
        {
            // 1. Metadata
            var pPlain = so.FindProperty("plainState");
            var pData = pPlain.FindPropertyRelative("m_Data");
            var pLen = pPlain.FindPropertyRelative("m_Length");
            var boxed = (ToggleButtonGroupState)pPlain.boxedValue;
            Put("meta", $"pt={pPlain.propertyType}|type={pPlain.type}|vis={B(pPlain.hasVisibleChildren)}|data={pData.ulongValue}|len={pLen.intValue}|boxed={StateStr(boxed)}");

            // 2. PropertyField plain ToggleButtonGroupState: init, UI edit, external data edit, length grow/shrink/zero
            int pfChg = 0;
            var pfPlain = new PropertyField(pPlain);
            pfPlain.RegisterValueChangeCallback(_ => pfChg++);
            m_Root.Add(pfPlain);
            pfPlain.Bind(so);
            Frames();
            var bgPlain = pfPlain.hierarchy.childCount > 0 ? pfPlain.hierarchy[0] as ToggleButtonGroup : null;
            string s0 = $"{GroupInfo(bgPlain)}";

            bgPlain.value = new ToggleButtonGroupState(0b0110UL, 4);
            Frames();
            string s1 = $"t={StateStr(t1.plainState)}|chg={B(pfChg > 0)}|{GroupInfo(bgPlain)}";

            t1.plainState = new ToggleButtonGroupState(0b1000UL, 4);
            Frames();
            string s2 = GroupInfo(bgPlain);

            t1.plainState = new ToggleButtonGroupState(0b10001UL, 5);
            Frames();
            string s3 = GroupInfo(bgPlain);

            t1.plainState = new ToggleButtonGroupState(0b10UL, 2);
            Frames();
            string s4 = GroupInfo(bgPlain);

            t1.plainState = new ToggleButtonGroupState(0UL, 0);
            Frames();
            string s5 = GroupInfo(bgPlain);
            Put("pf.plain", $"{s0}###{s1}###{s2}###{s3}###{s4}###{s5}");

            // 3. PropertyField with [ToggleButtonGroupStatePropertiesAttribute] & default empty state
            var pfSingle = new PropertyField(so.FindProperty("singleChoice"));
            var pfKeep = new PropertyField(so.FindProperty("keepLengthAttr"));
            var pfEmpty = new PropertyField(so.FindProperty("emptyState"));
            m_Root.Add(pfSingle);
            m_Root.Add(pfKeep);
            m_Root.Add(pfEmpty);
            pfSingle.Bind(so);
            pfKeep.Bind(so);
            pfEmpty.Bind(so);
            Frames();
            var bgSingle = pfSingle.hierarchy[0] as ToggleButtonGroup;
            var bgKeep = pfKeep.hierarchy[0] as ToggleButtonGroup;
            var bgEmpty = pfEmpty.hierarchy[0] as ToggleButtonGroup;
            string a0 = $"tSingle={StateStr(t1.singleChoice)}|{GroupInfo(bgSingle)}";
            string a1 = GroupInfo(bgKeep);
            string a2 = GroupInfo(bgEmpty);

            bgSingle.value = new ToggleButtonGroupState(0b010UL, 3);
            Frames();
            string a3 = $"tSingle={StateStr(t1.singleChoice)}|{GroupInfo(bgSingle)}";
            Put("pf.attrs_empty", $"{a0}###{a1}###{a2}###{a3}");

            // 4. Direct ToggleButtonGroup.BindProperty + incompatible IntegerField + UXML
            var pDir = so.FindProperty("directState");
            var bgDir = new ToggleButtonGroup("Custom");
            bgDir.Add(new Button { text = "Alpha" });
            bgDir.Add(new Button { text = "Beta" });
            bgDir.Add(new Button { text = "Gamma" });
            var badField = new IntegerField();
            m_Root.Add(bgDir);
            m_Root.Add(badField);
            bgDir.BindProperty(pDir);
            badField.BindProperty(pDir);
            Frames();
            string d0 = GroupInfo(bgDir);

            bgDir.value = new ToggleButtonGroupState(0b011UL, 3);
            Frames();
            string d1 = $"t={StateStr(t1.directState)}|{GroupInfo(bgDir)}";

            t1.directState = new ToggleButtonGroupState(0b100UL, 3);
            Frames();
            string d2 = GroupInfo(bgDir);

            var vta = Resources.Load<VisualTreeAsset>("ToggleGroupBind");
            var uxmlRoot = vta.Instantiate();
            m_Root.Add(uxmlRoot);
            uxmlRoot.Bind(so);
            Frames();
            var uxBg = uxmlRoot.Q<ToggleButtonGroup>("uxml-tbg");
            var uxPf = uxmlRoot.Q<PropertyField>("uxml-pf-tbg");
            var uxPfBg = uxPf.hierarchy.childCount > 0 ? uxPf.hierarchy[0] as ToggleButtonGroup : null;
            string ux0 = $"bg={GroupInfo(uxBg)}|pf={GroupInfo(uxPfBg)}";
            uxBg.value = new ToggleButtonGroupState(0b010UL, 3);
            Frames();
            string ux1 = $"t={StateStr(t1.directState)}|bg={StateStr(uxBg.value)}|pf={StateStr(uxPfBg.value)}";
            Put("direct_uxml", $"{d0}###{d1}###{d2}###{ux0}###{ux1}");

            // 5. Multi-object same & different
            soMulti.Update();
            var pfMS = new PropertyField(soMulti.FindProperty("multiSame"));
            var pfMD = new PropertyField(soMulti.FindProperty("multiDiff"));
            var bgMD = new ToggleButtonGroup();
            for (int i = 0; i < 4; i++) bgMD.Add(new Button { text = $"B{i}" });
            m_Root.Add(pfMS);
            m_Root.Add(pfMD);
            m_Root.Add(bgMD);
            pfMS.Bind(soMulti);
            pfMD.Bind(soMulti);
            bgMD.BindProperty(soMulti.FindProperty("multiDiff"));
            Frames();
            var bgPfMS = pfMS.hierarchy[0] as ToggleButtonGroup;
            var bgPfMD = pfMD.hierarchy[0] as ToggleButtonGroup;
            string m0 = $"ms={StateStr(bgPfMS.value)}(m={B(bgPfMS.showMixedValue)})|mdPf={StateStr(bgPfMD.value)}(m={B(bgPfMD.showMixedValue)})|mdDir={StateStr(bgMD.value)}(m={B(bgMD.showMixedValue)})";

            bgMD.value = new ToggleButtonGroupState(0b0110UL, 4);
            Frames();
            string m1 = $"t1={StateStr(t1.multiDiff)}|t2={StateStr(t2.multiDiff)}|mdPf={StateStr(bgPfMD.value)}(m={B(bgPfMD.showMixedValue)})|mdDir={StateStr(bgMD.value)}(m={B(bgMD.showMixedValue)})";
            Put("multi", $"{m0}###{m1}");

            // 6. PropertyField rebind: ToggleButtonGroupState -> other ToggleButtonGroupState -> Generic struct -> int -> ToggleButtonGroupState
            t1.directState = new ToggleButtonGroupState(0b101UL, 3);
            so.Update();
            var pfRb = new PropertyField(so.FindProperty("otherState"));
            m_Root.Add(pfRb);
            pfRb.Bind(so);
            Frames();
            var rb0 = pfRb.hierarchy[0] as ToggleButtonGroup;
            string r0 = GroupInfo(rb0);

            pfRb.BindProperty(so.FindProperty("directState"));
            Frames();
            var rb1 = pfRb.hierarchy[0] as ToggleButtonGroup;
            string r1 = $"same={B(ReferenceEquals(rb0, rb1))}|{GroupInfo(rb1)}";

            pfRb.BindProperty(so.FindProperty("plainStruct"));
            Frames();
            string r2 = $"type={pfRb.hierarchy[0]?.GetType().Name}|name={pfRb.hierarchy[0]?.name}";

            pfRb.BindProperty(so.FindProperty("scalarInt"));
            Frames();
            var rb3 = pfRb.hierarchy[0] as IntegerField;
            string r3 = $"type={pfRb.hierarchy[0]?.GetType().Name}|val={rb3?.value}";

            pfRb.BindProperty(so.FindProperty("otherState"));
            Frames();
            var rb4 = pfRb.hierarchy[0] as ToggleButtonGroup;
            string r4 = $"type={pfRb.hierarchy[0]?.GetType().Name}|{GroupInfo(rb4)}";
            Put("pf.rebind", $"{r0}###{r1}###{r2}###{r3}###{r4}");

            // 7. Unbind, rebind, and runtime DataBinding coexistence (PropertyField and direct ToggleButtonGroup)
            var rt = new ToggleButtonGroupRuntimeModel();
            var pfUb = new PropertyField(so.FindProperty("otherState")) { dataSource = rt };
            pfUb.SetBinding("tooltip", new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(ToggleButtonGroupRuntimeModel.tip)),
                bindingMode = BindingMode.ToTarget
            });
            var bgUbDir = new ToggleButtonGroup();
            for (int i = 0; i < 3; i++) bgUbDir.Add(new Button { text = $"U{i}" });
            m_Root.Add(pfUb);
            m_Root.Add(bgUbDir);
            pfUb.Bind(so);
            bgUbDir.BindProperty(so.FindProperty("otherState"));
            Frames();
            var bgUb = pfUb.hierarchy[0] as ToggleButtonGroup;
            string u0 = $"tip={pfUb.tooltip}|pf={StateStr(bgUb.value)}|dir={StateStr(bgUbDir.value)}";

            pfUb.Unbind();
            bgUbDir.Unbind();
            t1.otherState = new ToggleButtonGroupState(0b111UL, 3);
            rt.tip = "rt-tbg-1";
            Frames();
            string u1 = $"tip={pfUb.tooltip}|pf={StateStr(bgUb.value)}|dir={StateStr(bgUbDir.value)}";

            pfUb.Bind(so);
            bgUbDir.BindProperty(so.FindProperty("otherState"));
            Frames();
            var bgUb2 = pfUb.hierarchy[0] as ToggleButtonGroup;
            string u2 = $"tip={pfUb.tooltip}|pf={StateStr(bgUb2.value)}|dir={StateStr(bgUbDir.value)}";
            Put("unbind_rebind", $"{u0}###{u1}###{u2}");
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
