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

public class CharBox : BaseField<char>
{
    public CharBox() : base(null, new VisualElement()) { }

    protected override void UpdateMixedValueContent() { }
}

public class QuatBox : BaseField<Quaternion>
{
    public QuatBox() : base(null, new VisualElement()) { }

    protected override void UpdateMixedValueContent() { }
}

public class QuatCharBindingProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("QuatCharBindingProbe");
        var doc = go.AddComponent<UIDocument>();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        doc.panelSettings = ps;
        go.AddComponent<QuatCharBindingProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("QUAT_CHAR_BINDING_OUT");
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
                default:
                    if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string B(bool v) => v ? "1" : "0";
    static string F(float v) => v.ToString("G9", CultureInfo.InvariantCulture);
    static string Q(Quaternion q) => $"{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}";
    static string Ch(char c) => c >= 32 && c < 127 ? c.ToString() : $"\\u{(int)c:x4}";
    static string Str(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(Ch(c));
        return sb.ToString();
    }

#if UNITY_EDITOR
    static string FoldoutFloats(Foldout f)
    {
        if (f == null) return "null";
        var fields = f.Query<FloatField>().ToList();
        var parts = new string[fields.Count];
        for (int i = 0; i < fields.Count; i++)
            parts[i] = $"{fields[i].label}={F(fields[i].value)}(m={B(fields[i].showMixedValue)})";
        return string.Join(",", parts);
    }

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

        var t1 = ScriptableObject.CreateInstance<QuatCharTarget>();
        var t2 = ScriptableObject.CreateInstance<QuatCharTarget>();
        t2.multiDiffChar = 'B';
        t2.multiQuat = new Quaternion(0.5f, 0.2f, -0.3f, 0.9f);
        var go = new GameObject("QuatHost");
        go.transform.localRotation = new Quaternion(0.25f, 0.5f, -0.25f, 0.75f);

        var so = new SerializedObject(t1);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { t1, t2 });
        var soTr = new SerializedObject(go.transform);
        try
        {
            // 1. Metadata
            var pChar = so.FindProperty("charVal");
            var pQuat = so.FindProperty("quatVal");
            var pTrRot = soTr.FindProperty("m_LocalRotation");
            Put("meta", $"char={pChar.propertyType}/{pChar.type}/{pChar.numericType}/vis={B(pChar.hasVisibleChildren)}/int={pChar.intValue}|quat={pQuat.propertyType}/{pQuat.type}/vis={B(pQuat.hasVisibleChildren)}/v={Q(pQuat.quaternionValue)}|tr={pTrRot.propertyType}/{pTrRot.type}/vis={B(pTrRot.hasVisibleChildren)}/v={Q(pTrRot.quaternionValue)}");

            // 2. Character PropertyField: init, UI edit, empty string, multi-char, external edit, null char
            int charChg = 0;
            var pfChar = new PropertyField(pChar);
            pfChar.RegisterValueChangeCallback(_ => charChg++);
            m_Root.Add(pfChar);
            pfChar.Bind(so);
            Frames();
            var tfChar = pfChar.hierarchy.childCount > 0 ? pfChar.hierarchy[0] as TextField : null;
            string c0 = $"type={pfChar.hierarchy[0]?.GetType().Name}|name={tfChar?.name}|lbl={tfChar?.label}|max={tfChar?.maxLength}|val={Str(tfChar?.value)}";

            tfChar.value = "Z";
            Frames();
            string c1 = $"t={Ch(t1.charVal)}|val={Str(tfChar.value)}|chg={charChg}";

            tfChar.value = "";
            Frames();
            string c2 = $"t={Ch(t1.charVal)}|val={Str(tfChar.value)}";

            tfChar.value = "xy";
            Frames();
            string c3 = $"t={Ch(t1.charVal)}|val={Str(tfChar.value)}";

            t1.charVal = 'q';
            Frames();
            string c4 = $"val={Str(tfChar.value)}";

            t1.charVal = '\0';
            Frames();
            string c5 = $"val={Str(tfChar.value)}|len={tfChar.value?.Length ?? -1}";
            Put("char.pf", $"{c0}###{c1}###{c2}###{c3}###{c4}###{c5}");

            // 3. Character direct BindProperty: TextField, CharBox (INotifyValueChanged<char>), incompatible IntegerField
            t1.charVal = 'D';
            so.Update();
            var tfDir = new TextField();
            var cbDir = new CharBox();
            var badChar = new IntegerField();
            m_Root.Add(tfDir);
            m_Root.Add(cbDir);
            m_Root.Add(badChar);
            tfDir.BindProperty(pChar);
            cbDir.BindProperty(pChar);
            badChar.BindProperty(pChar);
            Frames();
            string cd0 = $"tf={Str(tfDir.value)}|cb={Ch(cbDir.value)}|bad={badChar.value}";

            cbDir.value = '!';
            Frames();
            string cd1 = $"t={Ch(t1.charVal)}|tf={Str(tfDir.value)}|cb={Ch(cbDir.value)}";

            t1.charVal = '#';
            Frames();
            string cd2 = $"tf={Str(tfDir.value)}|cb={Ch(cbDir.value)}";
            Put("char.direct", $"{cd0}###{cd1}###{cd2}");

            // 4. Character multi-object & PropertyField Character <-> String reuse
            var pfCharMS = new PropertyField(soMulti.FindProperty("multiSameChar"));
            var pfCharMD = new PropertyField(soMulti.FindProperty("multiDiffChar"));
            var cbMD = new CharBox();
            m_Root.Add(pfCharMS);
            m_Root.Add(pfCharMD);
            m_Root.Add(cbMD);
            pfCharMS.Bind(soMulti);
            pfCharMD.Bind(soMulti);
            cbMD.BindProperty(soMulti.FindProperty("multiDiffChar"));
            Frames();
            var tfMS = pfCharMS.hierarchy[0] as TextField;
            var tfMD = pfCharMD.hierarchy[0] as TextField;
            string cm0 = $"same={Str(tfMS.value)}(m={B(tfMS.showMixedValue)})|diff={Str(tfMD.value)}(m={B(tfMD.showMixedValue)})|cb={Ch(cbMD.value)}(m={B(cbMD.showMixedValue)})";

            tfMD.value = "K";
            Frames();
            string cm1 = $"t1={Ch(t1.multiDiffChar)}|t2={Ch(t2.multiDiffChar)}|diff={Str(tfMD.value)}(m={B(tfMD.showMixedValue)})|cb={Ch(cbMD.value)}(m={B(cbMD.showMixedValue)})";

            var pfReuseCS = new PropertyField(pChar);
            m_Root.Add(pfReuseCS);
            pfReuseCS.Bind(so);
            Frames();
            var tfR0 = pfReuseCS.hierarchy[0] as TextField;
            pfReuseCS.BindProperty(so.FindProperty("strVal"));
            Frames();
            var tfR1 = pfReuseCS.hierarchy[0] as TextField;
            string cr1 = $"same={B(ReferenceEquals(tfR0, tfR1))}|max={tfR1.maxLength}|val={Str(tfR1.value)}";
            pfReuseCS.BindProperty(pChar);
            Frames();
            var tfR2 = pfReuseCS.hierarchy[0] as TextField;
            string cr2 = $"same={B(ReferenceEquals(tfR1, tfR2))}|max={tfR2.maxLength}|val={Str(tfR2.value)}";
            Put("char.multi_reuse", $"{cm0}###{cm1}###{cr1}###{cr2}");

            // 5. Quaternion PropertyField: collapsed lazy -> expand -> component edit -> external edit + Transform.m_LocalRotation
            pQuat.isExpanded = false;
            var pfQuat = new PropertyField(pQuat);
            m_Root.Add(pfQuat);
            pfQuat.Bind(so);
            Frames();
            var fQuat = pfQuat.hierarchy.childCount > 0 ? pfQuat.hierarchy[0] as Foldout : null;
            string q0 = $"type={pfQuat.hierarchy[0]?.GetType().Name}|name={fQuat?.name}|txt={fQuat?.text}|exp={B(fQuat?.value ?? false)}|cc={fQuat?.contentContainer.childCount ?? -1}";

            fQuat.value = true;
            Frames();
            string q1 = $"exp={B(pQuat.isExpanded)}|cc={fQuat.contentContainer.childCount}|f={FoldoutFloats(fQuat)}";

            fQuat.Q<FloatField>("unity-input-quatVal.x").value = 0.75f;
            Frames();
            string q2 = $"t={Q(t1.quatVal)}|f={FoldoutFloats(fQuat)}";

            t1.quatVal = new Quaternion(0.5f, -0.5f, 0.25f, 0.75f);
            Frames();
            string q3 = $"f={FoldoutFloats(fQuat)}";

            pTrRot.isExpanded = true;
            var pfTr = new PropertyField(pTrRot);
            m_Root.Add(pfTr);
            pfTr.Bind(soTr);
            Frames();
            var fTr = pfTr.hierarchy.childCount > 0 ? pfTr.hierarchy[0] as Foldout : null;
            fTr.Q<FloatField>("unity-input-m_LocalRotation.y").value = -0.25f;
            Frames();
            string qTr = $"type={pfTr.hierarchy[0]?.GetType().Name}|name={fTr?.name}|t={Q(go.transform.localRotation)}|f={FoldoutFloats(fTr)}";
            Put("quat.pf", $"{q0}###{q1}###{q2}###{q3}###{qTr}");

            // 6. Quaternion direct BindProperty: QuatBox (INotifyValueChanged<Quaternion>) + incompatible Vector4Field
            var qbDir = new QuatBox();
            var badQuat = new Vector4Field();
            m_Root.Add(qbDir);
            m_Root.Add(badQuat);
            qbDir.BindProperty(pQuat);
            badQuat.BindProperty(pQuat);
            Frames();
            string qd0 = $"qb={Q(qbDir.value)}|bad={badQuat.value}";

            qbDir.value = new Quaternion(0.1f, 0.2f, 0.4f, 0.8f);
            Frames();
            string qd1 = $"t={Q(t1.quatVal)}|qb={Q(qbDir.value)}|f={FoldoutFloats(fQuat)}";

            t1.quatVal = new Quaternion(-0.2f, 0.3f, -0.4f, 0.6f);
            Frames();
            string qd2 = $"qb={Q(qbDir.value)}";
            Put("quat.direct", $"{qd0}###{qd1}###{qd2}");

            // 7. Quaternion multi-object (PropertyField Foldout + QuatBox) & cross-type rebind
            var pMQ = soMulti.FindProperty("multiQuat");
            pMQ.isExpanded = true;
            var pfMQ = new PropertyField(pMQ);
            var qbMQ = new QuatBox();
            m_Root.Add(pfMQ);
            m_Root.Add(qbMQ);
            pfMQ.Bind(soMulti);
            qbMQ.BindProperty(pMQ);
            Frames();
            var fMQ = pfMQ.hierarchy[0] as Foldout;
            string qm0 = $"f={FoldoutFloats(fMQ)}|qb={Q(qbMQ.value)}(m={B(qbMQ.showMixedValue)})";

            fMQ.Q<FloatField>("unity-input-multiQuat.x").value = 0.9f;
            Frames();
            string qm1 = $"t1={Q(t1.multiQuat)}|t2={Q(t2.multiQuat)}|f={FoldoutFloats(fMQ)}|qbm={B(qbMQ.showMixedValue)}";

            qbMQ.value = new Quaternion(0f, 0f, 0f, 1f);
            Frames();
            string qm2 = $"t1={Q(t1.multiQuat)}|t2={Q(t2.multiQuat)}|f={FoldoutFloats(fMQ)}|qbm={B(qbMQ.showMixedValue)}";

            var pVec4 = so.FindProperty("vec4Val");
            pVec4.isExpanded = true;
            var pfRebind = new PropertyField(pQuat);
            m_Root.Add(pfRebind);
            pfRebind.Bind(so);
            Frames();
            var rb0 = pfRebind.hierarchy[0] as Foldout;
            pfRebind.BindProperty(pVec4);
            Frames();
            var rb1 = pfRebind.hierarchy[0] as Foldout;
            string qr1 = $"type={pfRebind.hierarchy[0]?.GetType().Name}|sameF={B(ReferenceEquals(rb0, rb1))}|f={FoldoutFloats(rb1)}";
            pfRebind.BindProperty(pChar);
            Frames();
            var rb2 = pfRebind.hierarchy[0] as TextField;
            string qr2 = $"type={pfRebind.hierarchy[0]?.GetType().Name}|max={rb2?.maxLength}|val={Str(rb2?.value)}";
            pfRebind.BindProperty(pQuat);
            Frames();
            var rb3 = pfRebind.hierarchy[0] as Foldout;
            string qr3 = $"type={pfRebind.hierarchy[0]?.GetType().Name}|f={FoldoutFloats(rb3)}";
            Put("quat.multi_rebind", $"{qm0}###{qm1}###{qm2}###{qr1}###{qr2}###{qr3}");

            // 8. Tree Bind, Unbind, rebind, and runtime DataBinding coexistence
            var rt = new QuatCharRuntimeModel();
            var tree = new VisualElement { dataSource = rt };
            tree.SetBinding("tooltip", new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(QuatCharRuntimeModel.tip)),
                bindingMode = BindingMode.ToTarget
            });
            var treeChar = new CharBox { bindingPath = "charVal" };
            var treeQuat = new QuatBox { bindingPath = "quatVal" };
            tree.Add(treeChar);
            tree.Add(treeQuat);
            m_Root.Add(tree);
            tree.Bind(so);
            Frames();
            string u0 = $"tip={tree.tooltip}|c={Ch(treeChar.value)}|q={Q(treeQuat.value)}";

            tree.Unbind();
            t1.charVal = 'U';
            t1.quatVal = new Quaternion(1f, 0f, 0f, 0f);
            rt.tip = "rt-qc-1";
            Frames();
            string u1 = $"tip={tree.tooltip}|c={Ch(treeChar.value)}|q={Q(treeQuat.value)}";

            tree.Bind(so);
            Frames();
            string u2 = $"tip={tree.tooltip}|c={Ch(treeChar.value)}|q={Q(treeQuat.value)}";
            Put("tree_unbind_rebind", $"{u0}###{u1}###{u2}");

            // 9. Character binding via UXML (TextField + PropertyField with binding-path="charVal")
            var tUxml = ScriptableObject.CreateInstance<QuatCharTarget>();
            var soUxml = new SerializedObject(tUxml);
            try
            {
                var vta = Resources.Load<VisualTreeAsset>("CharBind");
                var uxmlRoot = vta.Instantiate();
                m_Root.Add(uxmlRoot);
                uxmlRoot.Bind(soUxml);
                Frames();
                var uxmlTf = uxmlRoot.Q<TextField>("uxml-char");
                var uxmlPf = uxmlRoot.Q<PropertyField>("uxml-pf-char");
                var uxmlPfTf = uxmlPf.hierarchy.childCount > 0 ? uxmlPf.hierarchy[0] as TextField : null;
                string ux0 = $"bp={uxmlTf?.bindingPath}|tf={Str(uxmlTf?.value)}|pfMax={uxmlPfTf?.maxLength}|pf={Str(uxmlPfTf?.value)}";

                uxmlTf.value = "W";
                Frames();
                string ux1 = $"t={Ch(tUxml.charVal)}|tf={Str(uxmlTf.value)}|pf={Str(uxmlPfTf?.value)}";

                uxmlPfTf.value = "M";
                Frames();
                string ux2 = $"t={Ch(tUxml.charVal)}|tf={Str(uxmlTf.value)}|pf={Str(uxmlPfTf.value)}";

                tUxml.charVal = 'Q';
                Frames();
                string ux3 = $"tf={Str(uxmlTf.value)}|pf={Str(uxmlPfTf.value)}";
                Put("char.uxml", $"{ux0}###{ux1}###{ux2}###{ux3}");
            }
            finally
            {
                soUxml.Dispose();
                UnityEngine.Object.DestroyImmediate(tUxml);
            }
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            soTr.Dispose();
            soMulti.Dispose();
            so.Dispose();
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(t1);
            UnityEngine.Object.DestroyImmediate(t2);
        }
    }
#else
    void Run() { }
#endif
}
