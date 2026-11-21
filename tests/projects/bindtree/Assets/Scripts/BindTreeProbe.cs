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
public struct BindTreeInner
{
    public int leaf;
    public string note;
}

[Serializable]
public struct BindTreeOuter
{
    public int score;
    public bool enabled;
    public BindTreeInner inner;
}

[Serializable]
public struct BindTreeEntry
{
    public int score;
    public string tag;
}

public class BindTreeTarget : ScriptableObject
{
    public bool flag = true;
    public int count = 42;
    public float ratio = 2.5f;
    public string title = "root-title";
    public int onlyMain = 77;
    public BindTreeOuter outer = new BindTreeOuter
    {
        score = 10,
        enabled = true,
        inner = new BindTreeInner { leaf = 99, note = "deep" }
    };
    public BindTreeEntry[] entries = new[]
    {
        new BindTreeEntry { score = 101, tag = "e0" },
        new BindTreeEntry { score = 202, tag = "e1" }
    };
}

public class BindTreeOtherTarget : ScriptableObject
{
    public int count = 500;
    public int onlyOther = 888;
    public string title = "other-title";
}

public class BindTreeRuntimeSource
{
    public string msg = "rt-0";
    public string tip = "tip-0";
}

public class BindTreeProbe : MonoBehaviour
{
    const string BindingIdStr = "--unity-serialized-object-bindings";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BIND_TREE_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<BindTreeProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("BIND_TREE_OUT");
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

    void Run()
    {
#if UNITY_EDITOR
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        Frames();

        // 1. Null arguments & leaf IBindable root.
        var tArgs = ScriptableObject.CreateInstance<BindTreeTarget>();
        var soArgs = new SerializedObject(tArgs);
        Put("args.nullElement", CatchType(() => ((VisualElement)null).Bind(soArgs)));
        var elNullSo = new VisualElement();
        m_Root.Add(elNullSo);
        Put("args.nullSoAttached", CatchType(() => elNullSo.Bind(null)));

        var leafRoot = new IntegerField { bindingPath = "count" };
        m_Root.Add(leafRoot);
        leafRoot.Bind(soArgs);
        Put("args.leafRootVal", leafRoot.value);
        Put("args.leafRootHasBinding", leafRoot.HasBinding(BindingIdStr));

        // 2. Traversal order, valid/empty/missing bindingPath, two-way updates in one tree.
        var tBasic = ScriptableObject.CreateInstance<BindTreeTarget>();
        var soBasic = new SerializedObject(tBasic);
        var tree = new VisualElement();
        var fToggle = new Toggle { bindingPath = "flag" };
        var group = new VisualElement();
        var fInt = new IntegerField { bindingPath = "count" };
        var fFloat = new FloatField { bindingPath = "ratio" };
        var fText = new TextField { bindingPath = "title" };
        var fMissing = new IntegerField { bindingPath = "noSuchProp" };
        var fEmpty = new IntegerField { bindingPath = "" };
        fMissing.SetValueWithoutNotify(7);
        fEmpty.SetValueWithoutNotify(8);

        var order = new List<string>();
        fToggle.RegisterValueChangedCallback(_ => order.Add("flag"));
        fInt.RegisterValueChangedCallback(_ => order.Add("count"));
        fFloat.RegisterValueChangedCallback(_ => order.Add("ratio"));
        fText.RegisterValueChangedCallback(_ => order.Add("title"));
        fMissing.RegisterValueChangedCallback(_ => order.Add("missing"));
        fEmpty.RegisterValueChangedCallback(_ => order.Add("empty"));

        group.Add(fInt);
        group.Add(fFloat);
        tree.Add(fToggle);
        tree.Add(group);
        tree.Add(fText);
        tree.Add(fMissing);
        tree.Add(fEmpty);
        m_Root.Add(tree);

        tree.Bind(soBasic);
        Put("traversal.order", string.Join(",", order));
        Put("traversal.initialVals",
            FmtBool(fToggle.value) + "|" + fInt.value.ToString(Inv) + "|" + FmtFloat(fFloat.value) + "|" +
            fText.value + "|" + fMissing.value.ToString(Inv) + "|" + fEmpty.value.ToString(Inv));
        Put("traversal.missingEmptyBound", FmtBool(fMissing.HasBinding(BindingIdStr)) + "," + FmtBool(fEmpty.HasBinding(BindingIdStr)));

        Frames();
        tBasic.flag = false;
        tBasic.count = 123;
        tBasic.ratio = 4.25f;
        tBasic.title = "updated";
        Frames();
        Put("traversal.afterTargetEdit",
            FmtBool(fToggle.value) + "|" + fInt.value.ToString(Inv) + "|" + FmtFloat(fFloat.value) + "|" + fText.value);

        fToggle.value = true;
        fInt.value = 321;
        fFloat.value = 6.5f;
        fText.value = "from-ui";
        Frames();
        Put("traversal.afterFieldEdit",
            FmtBool(tBasic.flag) + "|" + tBasic.count.ToString(Inv) + "|" + FmtFloat(tBasic.ratio) + "|" + tBasic.title);

        // 3. Nested path resolution (1-level, 2-level, root fallback, missing container, array element).
        var tNest = ScriptableObject.CreateInstance<BindTreeTarget>();
        var soNest = new SerializedObject(tNest);
        var nestRoot = new VisualElement();

        var outerBox = new BindableElement { bindingPath = "outer" };
        var fOuterScore = new IntegerField { bindingPath = "score" };
        var fOuterEnabled = new Toggle { bindingPath = "enabled" };
        var fRootFallback = new IntegerField { bindingPath = "count" }; // not under outer -> resolves root count
        var innerBox = new BindableElement { bindingPath = "inner" };
        var fInnerLeaf = new IntegerField { bindingPath = "leaf" };
        var fInnerNote = new TextField { bindingPath = "note" };
        innerBox.Add(fInnerLeaf);
        innerBox.Add(fInnerNote);
        outerBox.Add(fOuterScore);
        outerBox.Add(fOuterEnabled);
        outerBox.Add(fRootFallback);
        outerBox.Add(innerBox);

        var missingBox = new BindableElement { bindingPath = "doesNotExist" };
        var fInsideMissing = new IntegerField { bindingPath = "score" }; // inside outerBox? no, directly under nestRoot
        var fRootInsideMissing = new IntegerField { bindingPath = "count" };
        missingBox.Add(fInsideMissing);
        missingBox.Add(fRootInsideMissing);

        var missingUnderOuter = new BindableElement { bindingPath = "doesNotExist" };
        var fKeepOuterScope = new IntegerField { bindingPath = "score" }; // missing container keeps outer parentProperty
        missingUnderOuter.Add(fKeepOuterScope);
        outerBox.Add(missingUnderOuter);

        var arrBox = new BindableElement { bindingPath = "entries.Array.data[1]" };
        var fArr1Score = new IntegerField { bindingPath = "score" };
        var fArr1Tag = new TextField { bindingPath = "tag" };
        var fArr0Tag = new TextField { bindingPath = "entries.Array.data[0].tag" };
        arrBox.Add(fArr1Score);
        arrBox.Add(fArr1Tag);

        nestRoot.Add(outerBox);
        nestRoot.Add(missingBox);
        nestRoot.Add(arrBox);
        nestRoot.Add(fArr0Tag);
        m_Root.Add(nestRoot);

        nestRoot.Bind(soNest);
        Frames();
        Put("nested.initial",
            fOuterScore.value.ToString(Inv) + "|" + FmtBool(fOuterEnabled.value) + "|" +
            fRootFallback.value.ToString(Inv) + "|" + fInnerLeaf.value.ToString(Inv) + "|" + fInnerNote.value);
        Put("nested.missingContainer",
            FmtBool(fInsideMissing.HasBinding(BindingIdStr)) + "|" + fRootInsideMissing.value.ToString(Inv) + "|" +
            fKeepOuterScope.value.ToString(Inv));
        Put("nested.arrayInitial", fArr1Score.value.ToString(Inv) + "|" + fArr1Tag.value + "|" + fArr0Tag.value);
        Put("nested.containerHasBinding", FmtBool(outerBox.HasBinding(BindingIdStr)) + "," + FmtBool(innerBox.HasBinding(BindingIdStr)));

        fInnerLeaf.value = 199;
        fArr1Tag.value = "e1-edit";
        Frames();
        Put("nested.afterFieldEdit", tNest.outer.inner.leaf.ToString(Inv) + "|" + tNest.entries[1].tag);

        tNest.outer.score = 55;
        tNest.outer.inner.note = "deep-2";
        tNest.entries[1].score = 250;
        Frames();
        Put("nested.afterTargetEdit", fOuterScore.value.ToString(Inv) + "|" + fInnerNote.value + "|" + fArr1Score.value.ToString(Inv));

        // 4. Bind-before-attach, Unbind cancelling pending Bind, and late-added children.
        var tDet = ScriptableObject.CreateInstance<BindTreeTarget>();
        var soDet = new SerializedObject(tDet);
        var detTree = new VisualElement();
        var detInt = new IntegerField { bindingPath = "count" };
        var detToggle = new Toggle { bindingPath = "flag" };
        detTree.Add(detInt);
        detTree.Add(detToggle);
        detTree.Bind(soDet);
        Put("detached.beforeAttachVals", detInt.value.ToString(Inv) + "|" + FmtBool(detToggle.value));

        // Add another child AFTER detached Bind() but BEFORE attaching to panel.
        var detLateBeforeAttach = new TextField { bindingPath = "title" };
        detTree.Add(detLateBeforeAttach);
        m_Root.Add(detTree);
        Frames();
        Put("detached.afterAttachVals", detInt.value.ToString(Inv) + "|" + FmtBool(detToggle.value) + "|" + detLateBeforeAttach.value);

        // Add child AFTER attached Bind() -> measure whether it auto-binds, then after re-calling Bind().
        var lateAfterAttached = new IntegerField { bindingPath = "count" };
        detTree.Add(lateAfterAttached);
        Frames();
        Put("late.afterAttachedBind", lateAfterAttached.value.ToString(Inv) + "|" + FmtBool(lateAfterAttached.HasBinding(BindingIdStr)));
        detTree.Bind(soDet);
        Frames();
        Put("late.afterRebindCall", lateAfterAttached.value.ToString(Inv) + "|" + FmtBool(lateAfterAttached.HasBinding(BindingIdStr)));

        // Pending detached Bind cancelled by Unbind before attaching.
        var cancelTree = new VisualElement();
        var cancelField = new IntegerField { bindingPath = "count" };
        cancelTree.Add(cancelField);
        cancelTree.Bind(soDet);
        cancelTree.Unbind();
        m_Root.Add(cancelTree);
        Frames();
        Put("detached.cancelBeforeAttach", cancelField.value.ToString(Inv) + "|" + FmtBool(cancelField.HasBinding(BindingIdStr)));

        // 5. Unbind and rebind tree to a second SerializedObject (including overlapping vs unique paths).
        var tMain = ScriptableObject.CreateInstance<BindTreeTarget>();
        var tOther = ScriptableObject.CreateInstance<BindTreeOtherTarget>();
        var soMain = new SerializedObject(tMain);
        var soOther = new SerializedObject(tOther);

        var rebindTree = new VisualElement();
        var rbShared = new IntegerField { bindingPath = "count" };
        var rbOnlyMain = new IntegerField { bindingPath = "onlyMain" };
        var rbOnlyOther = new IntegerField { bindingPath = "onlyOther" };
        rebindTree.Add(rbShared);
        rebindTree.Add(rbOnlyMain);
        rebindTree.Add(rbOnlyOther);
        m_Root.Add(rebindTree);

        rebindTree.Bind(soMain);
        Frames();
        Put("rebind.onMain", rbShared.value.ToString(Inv) + "|" + rbOnlyMain.value.ToString(Inv) + "|" + rbOnlyOther.value.ToString(Inv));

        // Rebind tree directly to soOther without Unbind first.
        rebindTree.Bind(soOther);
        Frames();
        Put("rebind.onOther", rbShared.value.ToString(Inv) + "|" + rbOnlyMain.value.ToString(Inv) + "|" + rbOnlyOther.value.ToString(Inv));
        tMain.onlyMain = 177;
        tMain.count = 142;
        tOther.count = 505;
        Frames();
        Put("rebind.afterBothTargetsEdit", rbShared.value.ToString(Inv) + "|" + rbOnlyMain.value.ToString(Inv) + "|" + rbOnlyOther.value.ToString(Inv));

        // Unbind rebindTree (now owned by soOther): check what stops updating!
        rebindTree.Unbind();
        tMain.onlyMain = 277;
        tOther.count = 606;
        tOther.onlyOther = 999;
        Frames();
        Put("rebind.afterUnbind", rbShared.value.ToString(Inv) + "|" + rbOnlyMain.value.ToString(Inv) + "|" + rbOnlyOther.value.ToString(Inv));

        // 6. Independence between explicit BindProperty and tree Bind / Unbind.
        var tIndMain = ScriptableObject.CreateInstance<BindTreeTarget>();
        var tIndOther = ScriptableObject.CreateInstance<BindTreeOtherTarget>();
        var soIndMain = new SerializedObject(tIndMain);
        var soIndOther = new SerializedObject(tIndOther);

        // 6a: Explicit BindProperty(soOther.onlyOther) BEFORE tree.Bind(soMain) (path does NOT exist on soMain).
        var indTree = new VisualElement();
        var indTreeField = new IntegerField { bindingPath = "count" };
        var indUnique = new IntegerField();
        var indOverlap = new IntegerField();
        var indAfterBind = new IntegerField();
        var indSameSo = new IntegerField();
        indTree.Add(indTreeField);
        indTree.Add(indUnique);
        indTree.Add(indOverlap);
        indTree.Add(indAfterBind);
        indTree.Add(indSameSo);
        m_Root.Add(indTree);

        indUnique.BindProperty(soIndOther.FindProperty("onlyOther"));
        indOverlap.BindProperty(soIndOther.FindProperty("count"));
        Frames();
        Put("indep.beforeTreeBind", indUnique.value.ToString(Inv) + "|" + indOverlap.value.ToString(Inv));

        indTree.Bind(soIndMain);
        Frames();
        Put("indep.afterTreeBind",
            indTreeField.value.ToString(Inv) + "|" + indUnique.value.ToString(Inv) + "|" + indOverlap.value.ToString(Inv));

        // BindProperty AFTER tree.Bind: one to soOther, one to soMain.
        indAfterBind.BindProperty(soIndOther.FindProperty("onlyOther"));
        indSameSo.BindProperty(soIndMain.FindProperty("onlyMain"));
        Frames();
        Put("indep.afterPostBindProps", indAfterBind.value.ToString(Inv) + "|" + indSameSo.value.ToString(Inv));

        // Now call indTree.Unbind() and mutate both soIndMain and soIndOther!
        indTree.Unbind();
        tIndMain.count = 401;
        tIndMain.onlyMain = 402;
        tIndOther.onlyOther = 901;
        tIndOther.count = 902;
        Frames();
        Put("indep.afterTreeUnbindVals",
            indTreeField.value.ToString(Inv) + "|" + indUnique.value.ToString(Inv) + "|" +
            indOverlap.value.ToString(Inv) + "|" + indAfterBind.value.ToString(Inv) + "|" + indSameSo.value.ToString(Inv));
        Put("indep.afterTreeUnbindBound",
            FmtBool(indTreeField.HasBinding(BindingIdStr)) + "|" + FmtBool(indUnique.HasBinding(BindingIdStr)) + "|" +
            FmtBool(indOverlap.HasBinding(BindingIdStr)) + "|" + FmtBool(indAfterBind.HasBinding(BindingIdStr)) + "|" +
            FmtBool(indSameSo.HasBinding(BindingIdStr)));

        // 6b: Nested subtree bound to soOther inside parent bound to soMain; parent.Unbind() stops at child subtree.
        var parentTree = new VisualElement();
        var parentField = new IntegerField { bindingPath = "count" };
        var subTree = new VisualElement();
        var subField = new IntegerField { bindingPath = "onlyOther" };
        subTree.Add(subField);
        parentTree.Add(parentField);
        parentTree.Add(subTree);
        m_Root.Add(parentTree);
        parentTree.Bind(soIndMain);
        subTree.Bind(soIndOther);
        Frames();
        parentTree.Unbind();
        tIndMain.count = 450;
        tIndOther.onlyOther = 950;
        Frames();
        Put("indep.nestedSubtreeAfterParentUnbind",
            parentField.value.ToString(Inv) + "|" + FmtBool(parentField.HasBinding(BindingIdStr)) + "|" +
            subField.value.ToString(Inv) + "|" + FmtBool(subField.HasBinding(BindingIdStr)));

        // 7. Multi-object mixed state through tree Bind.
        var m1 = ScriptableObject.CreateInstance<BindTreeTarget>();
        var m2 = ScriptableObject.CreateInstance<BindTreeTarget>();
        m1.flag = true; m1.count = 10; m1.outer.score = 1;
        m2.flag = false; m2.count = 20; m2.outer.score = 1; // outer.score agrees, flag & count differ
        var soMulti = new SerializedObject(new UnityEngine.Object[] { m1, m2 });

        var mTree = new VisualElement();
        var mToggle = new Toggle { bindingPath = "flag" };
        var mInt = new IntegerField { bindingPath = "count" };
        var mOuter = new BindableElement { bindingPath = "outer" };
        var mScore = new IntegerField { bindingPath = "score" };
        mOuter.Add(mScore);
        mTree.Add(mToggle);
        mTree.Add(mInt);
        mTree.Add(mOuter);
        m_Root.Add(mTree);

        mTree.Bind(soMulti);
        Frames();
        Put("mixed.treeInitial",
            FmtBool(mToggle.showMixedValue) + ":" + FmtBool(mToggle.value) + "|" +
            FmtBool(mInt.showMixedValue) + ":" + mInt.value.ToString(Inv) + "|" +
            FmtBool(mScore.showMixedValue) + ":" + mScore.value.ToString(Inv));

        mInt.value = 33;
        Frames();
        Put("mixed.treeAfterEdit", m1.count.ToString(Inv) + "," + m2.count.ToString(Inv) + "|" + FmtBool(mInt.showMixedValue));

        // 8. Coexistence with runtime DataBinding across tree Bind and Unbind.
        var tCo = ScriptableObject.CreateInstance<BindTreeTarget>();
        var soCo = new SerializedObject(tCo);
        var rtSrc = new BindTreeRuntimeSource();
        var coTree = new VisualElement { dataSource = rtSrc };
        var coInt = new IntegerField { bindingPath = "count" };
        coInt.SetBinding("tooltip", new DataBinding { dataSourcePath = PropertyPath.FromName("tip"), bindingMode = BindingMode.ToTarget });
        var coRtOnly = new TextField();
        coRtOnly.SetBinding("value", new DataBinding { dataSourcePath = PropertyPath.FromName("msg"), bindingMode = BindingMode.ToTarget });
        coTree.Add(coInt);
        coTree.Add(coRtOnly);
        m_Root.Add(coTree);

        coTree.Bind(soCo);
        Frames();
        Put("coexist.beforeUnbind", coInt.value.ToString(Inv) + "|" + coInt.tooltip + "|" + coRtOnly.value);

        tCo.count = 61;
        rtSrc.tip = "tip-1";
        rtSrc.msg = "rt-1";
        Frames();
        Put("coexist.afterBothUpdate", coInt.value.ToString(Inv) + "|" + coInt.tooltip + "|" + coRtOnly.value);

        coTree.Unbind();
        tCo.count = 62;
        rtSrc.tip = "tip-2";
        rtSrc.msg = "rt-2";
        Frames();
        Put("coexist.afterUnbind", coInt.value.ToString(Inv) + "|" + coInt.tooltip + "|" + coRtOnly.value);
#endif
    }
}
