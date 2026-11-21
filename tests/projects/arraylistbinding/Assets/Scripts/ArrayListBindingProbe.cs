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

public class ArrayListRuntimeModel
{
    [CreateProperty]
    public string tip { get; set; } = "rt-al-0";
}

public class ArrayListBindingProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("ArrayListBindingProbe");
        var doc = go.AddComponent<UIDocument>();
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        doc.panelSettings = ps;
        go.AddComponent<ArrayListBindingProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("ARRAY_LIST_BINDING_OUT");
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

    static void Frames(int count = 3)
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

#if UNITY_EDITOR
    static TextField SizeField(BaseListView lv) => lv.Q<TextField>(BaseListView.arraySizeFieldUssClassName);

    static Foldout HeaderFoldout(BaseListView lv) => lv.Q<Foldout>(BaseListView.foldoutHeaderUssClassName);

    static string ListInfo(ListView lv)
    {
        if (lv == null) return "null";
        var sf = SizeField(lv);
        var hf = HeaderFoldout(lv);
        int cnt = lv.itemsSource?.Count ?? -1;
        return $"name={lv.name}|bp={lv.bindingPath}|ht={lv.headerTitle}|cnt={cnt}|sf={(sf != null ? sf.value : "null")}|sfm={(sf != null ? B(sf.showMixedValue) : "-")}|hf={(hf != null ? $"{B(hf.value)}:{hf.bindingPath}" : "null")}|brd={B(lv.showBorder)}|ftr={B(lv.showAddRemoveFooter)}|sz={B(lv.showBoundCollectionSize)}|hdr={B(lv.showFoldoutHeader)}";
    }

    static string IntItems(ListView lv)
    {
        var items = new List<string>();
        int cnt = lv.itemsSource?.Count ?? 0;
        for (int i = 0; i < cnt; i++)
        {
            var root = lv.GetRootElementForIndex(i);
            var inf = root?.Q<IntegerField>();
            items.Add(inf != null ? $"{inf.label}={inf.value}(m={B(inf.showMixedValue)})" : (root != null ? root.GetType().Name : "null"));
        }
        return string.Join(",", items);
    }

    static string StringItems(ListView lv)
    {
        var items = new List<string>();
        int cnt = lv.itemsSource?.Count ?? 0;
        for (int i = 0; i < cnt; i++)
        {
            var root = lv.GetRootElementForIndex(i);
            var tf = root?.Q<TextField>();
            items.Add(tf != null ? $"{tf.label}={tf.value}" : (root != null ? root.GetType().Name : "null"));
        }
        return string.Join(",", items);
    }

    void Run()
    {
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 600f;
        m_Root.style.height = 1200f;
        Frames();

        Application.LogCallback onLog = (cond, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{cond}");
        };
        Application.logMessageReceived += onLog;

        var t1 = ScriptableObject.CreateInstance<ArrayListTarget>();
        var t2 = ScriptableObject.CreateInstance<ArrayListTarget>();
        t2.multiSame = new[] { 5, 99 };
        t2.multiDiff = new[] { 100 };

        var so = new SerializedObject(t1);
        var soMulti = new SerializedObject(new UnityEngine.Object[] { t1, t2 });
        try
        {
            // 1. PropertyField on int[]: config, foldout expand, element UI->SO & external->UI edits
            var pIntArr = so.FindProperty("intArray");
            pIntArr.isExpanded = true;
            var pfInt = new PropertyField(pIntArr);
            int pfChangeCount = 0;
            pfInt.RegisterValueChangeCallback(_ => pfChangeCount++);
            pfInt.style.height = 240f;
            m_Root.Add(pfInt);
            pfInt.Bind(so);
            Frames();

            var lvInt = pfInt.hierarchy.childCount > 0 ? pfInt.hierarchy[0] as ListView : null;
            Put("pf.int_array.init", $"{ListInfo(lvInt)}|items={IntItems(lvInt)}");

            var item0Field = lvInt.GetRootElementForIndex(0)?.Q<IntegerField>();
            item0Field.value = 111;
            Frames();
            string afterUi = $"t=[{string.Join(",", t1.intArray)}]|items={IntItems(lvInt)}";

            t1.intArray[2] = 333;
            Frames();
            string afterExt = $"items={IntItems(lvInt)}";
            Put("pf.int_array.edits", $"{afterUi}###{afterExt}");

            // 2. Resize via size field, controller AddItems/RemoveItem/Move/ClearItems, and external resize
            pfChangeCount = 0;
            var sfInt = SizeField(lvInt);
            sfInt.value = "4";
            Frames();
            string afterGrow = $"t=[{string.Join(",", t1.intArray)}]|cnt={lvInt.itemsSource.Count}|sf={sfInt.value}|chg={pfChangeCount}|items={IntItems(lvInt)}";

            lvInt.viewController.AddItems(1);
            Frames();
            string afterAdd = $"t=[{string.Join(",", t1.intArray)}]|cnt={lvInt.itemsSource.Count}|sf={sfInt.value}";

            lvInt.viewController.RemoveItem(1);
            Frames();
            string afterRem = $"t=[{string.Join(",", t1.intArray)}]|cnt={lvInt.itemsSource.Count}|sf={sfInt.value}";

            lvInt.viewController.Move(0, 2);
            Frames();
            string afterMove = $"t=[{string.Join(",", t1.intArray)}]|items={IntItems(lvInt)}";

            sfInt.value = "0";
            Frames();
            string afterZero = $"t=[{string.Join(",", t1.intArray)}]|cnt={lvInt.itemsSource.Count}|sf={sfInt.value}";

            t1.intArray = new[] { 7, 8 };
            Frames();
            string afterExtResize = $"cnt={lvInt.itemsSource.Count}|sf={sfInt.value}|items={IntItems(lvInt)}";
            Put("pf.int_array.resize", $"{afterGrow}###{afterAdd}###{afterRem}###{afterMove}###{afterZero}###{afterExtResize}");

            // 3. List<string> via PropertyField
            var pStrList = so.FindProperty("stringList");
            pStrList.isExpanded = true;
            var pfStr = new PropertyField(pStrList, "Custom Strings");
            pfStr.style.height = 200f;
            m_Root.Add(pfStr);
            pfStr.Bind(so);
            Frames();
            var lvStr = pfStr.hierarchy[0] as ListView;
            string s0 = $"{ListInfo(lvStr)}|items={StringItems(lvStr)}";

            lvStr.GetRootElementForIndex(1).Q<TextField>().value = "TWO";
            Frames();
            string s1 = $"t=[{string.Join(",", t1.stringList)}]";

            t1.stringList.Add("three");
            Frames();
            string s2 = $"cnt={lvStr.itemsSource.Count}|sf={SizeField(lvStr)?.value}|items={StringItems(lvStr)}";

            lvStr.viewController.ClearItems();
            Frames();
            string s3 = $"t=[{string.Join(",", t1.stringList)}]|cnt={lvStr.itemsSource.Count}|sf={SizeField(lvStr)?.value}";
            Put("pf.string_list", $"{s0}###{s1}###{s2}###{s3}");

            // 4. Struct array via PropertyField (nested foldouts + first-string title + edit)
            var pStructArr = so.FindProperty("structArray");
            pStructArr.isExpanded = true;
            var pfStruct = new PropertyField(pStructArr);
            pfStruct.style.height = 260f;
            m_Root.Add(pfStruct);
            pfStruct.Bind(so);
            Frames();
            var lvStruct = pfStruct.hierarchy[0] as ListView;
            var fElem0 = lvStruct.GetRootElementForIndex(0)?.Q<Foldout>();
            var fElem1 = lvStruct.GetRootElementForIndex(1)?.Q<Foldout>();
            string st0 = $"{ListInfo(lvStruct)}|e0={fElem0?.text}|e1={fElem1?.text}";

            fElem0.value = true;
            Frames();
            var titleField = fElem0.Q<TextField>("unity-input-structArray.Array.data[0].title");
            var valField = fElem0.Q<IntegerField>("unity-input-structArray.Array.data[0].value");
            titleField.value = "Renamed";
            valField.value = 88;
            Frames();
            string st1 = $"t0={t1.structArray[0].title}:{t1.structArray[0].value}|e0={fElem0.text}";
            Put("pf.struct_array", $"{st0}###{st1}");

            // 5. Direct ListView.BindProperty (default callbacks, showFoldoutHeader=false and true)
            var lvDirect = new ListView();
            lvDirect.style.height = 180f;
            m_Root.Add(lvDirect);
            lvDirect.BindProperty(so.FindProperty("intArray"));
            Frames();
            string d0 = $"{ListInfo(lvDirect)}|items={IntItems(lvDirect)}";

            var lvDirectHdr = new ListView { showFoldoutHeader = true, showBoundCollectionSize = true, headerTitle = "Direct Header" };
            lvDirectHdr.style.height = 180f;
            m_Root.Add(lvDirectHdr);
            lvDirectHdr.BindProperty(so.FindProperty("intArray"));
            Frames();
            string d1 = $"{ListInfo(lvDirectHdr)}|items={IntItems(lvDirectHdr)}";
            Put("direct.listview_default", $"{d0}###{d1}");

            // 6. Direct ListView with custom makeItem/bindItem/unbindItem
            int customMake = 0, customBind = 0, customUnbind = 0;
            ListView lvCustom = null;
            lvCustom = new ListView
            {
                makeItem = () => { customMake++; return new Label(); },
                bindItem = (ve, i) => { customBind++; ((Label)ve).text = $"row-{i}:{((SerializedProperty)lvCustom.itemsSource[i]).intValue}"; },
                unbindItem = (_, _) => { customUnbind++; }
            };
            lvCustom.style.height = 180f;
            m_Root.Add(lvCustom);
            lvCustom.BindProperty(so.FindProperty("intArray"));
            Frames();
            var row0 = lvCustom.GetRootElementForIndex(0) as Label;
            var row1 = lvCustom.GetRootElementForIndex(1) as Label;
            Put("direct.listview_custom", $"cnt={lvCustom.itemsSource.Count}|r0={row0?.text}|r1={row1?.text}|mk={B(customMake > 0)}|bd={B(customBind > 0)}");

            // 7. Incompatible ListView bindings (non-array Generic struct and scalar int)
            var lvBadStruct = new ListView();
            var lvBadInt = new ListView();
            m_Root.Add(lvBadStruct);
            m_Root.Add(lvBadInt);
            lvBadStruct.BindProperty(so.FindProperty("plainStruct"));
            lvBadInt.BindProperty(so.FindProperty("scalarInt"));
            Frames();
            Put("direct.listview_incompatible", $"structSrc={(lvBadStruct.itemsSource == null ? "null" : "set")}|intSrc={(lvBadInt.itemsSource == null ? "null" : "set")}");

            // 8. Multi-object: same size with mixed element + different sizes with mixed size field
            var pMultiSame = soMulti.FindProperty("multiSame");
            pMultiSame.isExpanded = true;
            var pfMS = new PropertyField(pMultiSame);
            pfMS.style.height = 180f;
            m_Root.Add(pfMS);
            pfMS.Bind(soMulti);
            Frames();
            var lvMS = pfMS.hierarchy[0] as ListView;
            string ms0 = $"{ListInfo(lvMS)}|items={IntItems(lvMS)}";
            lvMS.GetRootElementForIndex(1).Q<IntegerField>().value = 77;
            Frames();
            string ms1 = $"t1=[{string.Join(",", t1.multiSame)}]|t2=[{string.Join(",", t2.multiSame)}]|items={IntItems(lvMS)}";

            var pMultiDiff = soMulti.FindProperty("multiDiff");
            pMultiDiff.isExpanded = true;
            var pfMD = new PropertyField(pMultiDiff);
            pfMD.style.height = 180f;
            m_Root.Add(pfMD);
            pfMD.Bind(soMulti);
            Frames();
            var lvMD = pfMD.hierarchy[0] as ListView;
            string md0 = $"{ListInfo(lvMD)}|items={IntItems(lvMD)}";
            SizeField(lvMD).value = "2";
            Frames();
            string md1 = $"t1=[{string.Join(",", t1.multiDiff)}]|t2=[{string.Join(",", t2.multiDiff)}]|{ListInfo(lvMD)}|items={IntItems(lvMD)}";
            Put("pf.multi", $"{ms0}###{ms1}###{md0}###{md1}");

            // 9. PropertyField rebind: int[] -> List<string> (reuse), -> Generic struct (Foldout), -> int (IntegerField), -> int[]
            t1.stringList = new List<string> { "x", "y" };
            so.Update();
            pStrList.isExpanded = true;
            var pfRebind = new PropertyField(pIntArr);
            pfRebind.style.height = 180f;
            m_Root.Add(pfRebind);
            pfRebind.Bind(so);
            Frames();
            var lvR0 = pfRebind.hierarchy[0] as ListView;
            string r0 = $"{ListInfo(lvR0)}|items={IntItems(lvR0)}";

            pfRebind.BindProperty(pStrList);
            Frames();
            var lvR1 = pfRebind.hierarchy[0] as ListView;
            string r1 = $"sameLv={B(ReferenceEquals(lvR0, lvR1))}|{ListInfo(lvR1)}|items={StringItems(lvR1)}";

            pfRebind.BindProperty(so.FindProperty("plainStruct"));
            Frames();
            var fR2 = pfRebind.hierarchy[0] as Foldout;
            string r2 = $"type={pfRebind.hierarchy[0].GetType().Name}|name={fR2?.name}";

            pfRebind.BindProperty(so.FindProperty("scalarInt"));
            Frames();
            var iR3 = pfRebind.hierarchy[0] as IntegerField;
            string r3 = $"type={pfRebind.hierarchy[0].GetType().Name}|val={iR3?.value}";

            pfRebind.BindProperty(pIntArr);
            Frames();
            var lvR4 = pfRebind.hierarchy[0] as ListView;
            string r4 = $"type={pfRebind.hierarchy[0].GetType().Name}|cnt={lvR4?.itemsSource?.Count}|items={IntItems(lvR4)}";
            Put("pf.rebind", $"{r0}###{r1}###{r2}###{r3}###{r4}");

            // 10. Unbind, rebind, and runtime DataBinding coexistence
            var rtModel = new ArrayListRuntimeModel();
            var pfUb = new PropertyField(pIntArr) { dataSource = rtModel };
            pfUb.SetBinding("tooltip", new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(ArrayListRuntimeModel.tip)),
                bindingMode = BindingMode.ToTarget
            });
            pfUb.style.height = 180f;
            m_Root.Add(pfUb);
            pfUb.Bind(so);
            Frames();
            var lvUb = pfUb.hierarchy[0] as ListView;
            string ub0 = $"tip={pfUb.tooltip}|cnt={lvUb.itemsSource?.Count}|items={IntItems(lvUb)}";

            pfUb.Unbind();
            t1.intArray = new[] { 91, 92, 93 };
            rtModel.tip = "rt-al-1";
            Frames();
            string ub1 = $"tip={pfUb.tooltip}|srcNull={B(lvUb.itemsSource == null)}";

            pfUb.Bind(so);
            Frames();
            var lvUb2 = pfUb.hierarchy[0] as ListView;
            string ub2 = $"tip={pfUb.tooltip}|cnt={lvUb2.itemsSource?.Count}|items={IntItems(lvUb2)}";
            Put("unbind_rebind", $"{ub0}###{ub1}###{ub2}");
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
