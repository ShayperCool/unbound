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
public struct TrackProbeNested
{
    public int score;
    public string tag;
}

public class TrackProbeTarget : ScriptableObject
{
    public int count = 10;
    public int unrelated = 1;
    public string label = "init";
    public bool flag = false;
    public TrackProbeNested nested = new TrackProbeNested { score = 3, tag = "n0" };
    public string[] items = new[] { "a", "b" };
}

public class TrackRuntimeSource
{
    public string message = "hello";
}

public class TrackPropertyValueProbe : MonoBehaviour
{
    const string UpdaterBindingId = "--unity-serialized-object-bindings-context-updater";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRACK_PROPERTY_VALUE_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<TrackPropertyValueProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("TRACK_PROPERTY_VALUE_OUT");
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
            return e.GetType().Name + ":" + e.ParamName;
        }
        catch (Exception e)
        {
            return e.GetType().Name + ":" + e.Message;
        }
    }

    void Run()
    {
#if UNITY_EDITOR
        var document = GetComponent<UIDocument>();
        var targetTex = new RenderTexture(400, 300, 24, RenderTextureFormat.ARGB32);
        targetTex.Create();
        document.panelSettings.targetTexture = targetTex;
        m_Root = document.rootVisualElement;
        Frames();

        // 1. Constants & SerializedPropertyChangeEvent defaults.
        Put("const.prefabOverrideUssClassName", BindingExtensions.prefabOverrideUssClassName);
        var t0 = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var so0 = new SerializedObject(t0);
        using (var evt = SerializedPropertyChangeEvent.GetPooled(so0.FindProperty("count")))
        {
            Put("evt.bubbles", evt.bubbles);
            Put("evt.tricklesDown", evt.tricklesDown);
            Put("evt.path", evt.changedProperty.propertyPath);
            Put("evt.value", evt.changedProperty.intValue);
        }

        // 2. Argument validation.
        var eArg = new VisualElement();
        m_Root.Add(eArg);
        Put("args.nullProperty", CatchType(() => eArg.TrackPropertyValue(null, _ => { })));
        Put("args.nullElement", CatchType(() => ((VisualElement)null).TrackPropertyValue(so0.FindProperty("count"), _ => { })));
        Put("args.unbindNull", CatchType(() => ((VisualElement)null).Unbind()));

        // 3 & 4. Unchanged property, unrelated edit, tracked edit, unapplied edit, revert-before-update, direct C# edit.
        var t1 = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var so1 = new SerializedObject(t1);
        var e1 = new VisualElement();
        m_Root.Add(e1);
        var e1Events = new List<string>();
        e1.TrackPropertyValue(so1.FindProperty("count"), p =>
            e1Events.Add(p.propertyPath + "=" + p.intValue + ";sameSo=" + ReferenceEquals(p.serializedObject, so1)));
        Put("basic.hasBindingBeforeUpdate", e1.HasBinding(UpdaterBindingId));

        Frames();
        Put("basic.unchanged1", e1Events.Count);
        Frames();
        Put("basic.unchanged2", e1Events.Count);

        // Unrelated property edit.
        so1.FindProperty("unrelated").intValue = 99;
        so1.ApplyModifiedProperties();
        Frames();
        Put("basic.afterUnrelated", e1Events.Count);

        // Tracked property edit via SerializedProperty + ApplyModifiedProperties.
        so1.FindProperty("count").intValue = 11;
        so1.ApplyModifiedProperties();
        Frames();
        Put("basic.afterTracked", string.Join("|", e1Events));

        // Stable after reporting.
        Frames();
        Put("basic.afterStable", e1Events.Count);

        // Unapplied edit does not trigger callback.
        so1.FindProperty("count").intValue = 44;
        Frames();
        Put("basic.afterUnapplied", e1Events.Count);
        Put("basic.valueAfterUnapplied", so1.FindProperty("count").intValue);

        // Edit and revert before panel update does not trigger callback.
        so1.FindProperty("count").intValue = 50;
        so1.ApplyModifiedProperties();
        so1.FindProperty("count").intValue = 11;
        so1.ApplyModifiedProperties();
        Frames();
        Put("basic.afterRevertBeforeUpdate", e1Events.Count);

        // Direct C# field edit picked up during panel update.
        t1.count = 22;
        Frames();
        Put("basic.afterDirectEdit", string.Join("|", e1Events));

        // 5. Nested struct (leaf + parent) and array (element + size).
        var tNested = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soNested = new SerializedObject(tNested);
        var eNested = new VisualElement();
        m_Root.Add(eNested);
        var nestedLog = new List<string>();
        eNested.TrackPropertyValue(soNested.FindProperty("nested.score"), p => nestedLog.Add("score=" + p.intValue));
        eNested.TrackPropertyValue(soNested.FindProperty("nested"), p => nestedLog.Add("nested.score=" + p.FindPropertyRelative("score").intValue));
        eNested.TrackPropertyValue(soNested.FindProperty("items.Array.size"), p => nestedLog.Add("size=" + p.intValue));
        eNested.TrackPropertyValue(soNested.FindProperty("items.Array.data[0]"), p => nestedLog.Add("item0=" + p.stringValue));
        Frames();
        Put("nested.initial", nestedLog.Count);

        soNested.FindProperty("nested.score").intValue = 7;
        soNested.ApplyModifiedProperties();
        Frames();
        nestedLog.Sort(StringComparer.Ordinal);
        Put("nested.afterScore", string.Join("|", nestedLog));
        nestedLog.Clear();

        soNested.FindProperty("items.Array.data[0]").stringValue = "b";
        soNested.ApplyModifiedProperties();
        Frames();
        nestedLog.Sort(StringComparer.Ordinal);
        Put("nested.afterItem0", string.Join("|", nestedLog));
        nestedLog.Clear();

        soNested.FindProperty("items.Array.size").intValue = 3;
        soNested.ApplyModifiedProperties();
        Frames();
        nestedLog.Sort(StringComparer.Ordinal);
        Put("nested.afterSize", string.Join("|", nestedLog));
        nestedLog.Clear();

        // 6. Immediate mutation before first panel update after TrackPropertyValue.
        var tImm1 = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soImm1 = new SerializedObject(tImm1);
        var eImm1 = new VisualElement();
        m_Root.Add(eImm1);
        var imm1Log = new List<string>();
        eImm1.TrackPropertyValue(soImm1.FindProperty("count"), p => imm1Log.Add("v=" + p.intValue));
        soImm1.FindProperty("count").intValue = 77;
        soImm1.ApplyModifiedProperties();
        Frames();
        Put("immediate.applyBeforeFirstUpdate", string.Join("|", imm1Log));

        var tImm2 = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soImm2 = new SerializedObject(tImm2);
        var eImm2 = new VisualElement();
        m_Root.Add(eImm2);
        var imm2Log = new List<string>();
        eImm2.TrackPropertyValue(soImm2.FindProperty("count"), p => imm2Log.Add("v=" + p.intValue));
        tImm2.count = 88;
        Frames();
        Put("immediate.directBeforeFirstUpdate", string.Join("|", imm2Log));

        // 7. Null callback dispatches bubbling SerializedPropertyChangeEvent.
        var tEvt = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soEvt = new SerializedObject(tEvt);
        var evtParent = new VisualElement();
        var evtChild = new VisualElement();
        evtParent.Add(evtChild);
        m_Root.Add(evtParent);
        var evtLog = new List<string>();
        evtChild.RegisterCallback<SerializedPropertyChangeEvent>(e =>
            evtLog.Add("child:" + e.changedProperty.propertyPath + "=" + e.changedProperty.stringValue));
        evtParent.RegisterCallback<SerializedPropertyChangeEvent>(e =>
            evtLog.Add("parent:" + e.changedProperty.propertyPath + "=" + e.changedProperty.stringValue));
        evtChild.TrackPropertyValue(soEvt.FindProperty("label"));
        Frames();
        Put("nullCb.initial", evtLog.Count);
        soEvt.FindProperty("label").stringValue = "updated";
        soEvt.ApplyModifiedProperties();
        Frames();
        Put("nullCb.afterEdit", string.Join("|", evtLog));

        // 8. Multiple callbacks on same element + NotSupportedException for second SerializedObject.
        var tMulti = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soMulti = new SerializedObject(tMulti);
        var tOther = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soOther = new SerializedObject(tOther);
        var eMulti = new VisualElement();
        m_Root.Add(eMulti);
        var multiLog = new List<string>();
        eMulti.TrackPropertyValue(soMulti.FindProperty("count"), p => multiLog.Add("cb1:" + p.intValue));
        eMulti.TrackPropertyValue(soMulti.FindProperty("count"), p => multiLog.Add("cb2:" + p.intValue));
        eMulti.TrackPropertyValue(soMulti.FindProperty("flag"), p => multiLog.Add("flag:" + p.boolValue));
        Put("multi.secondSoThrows", CatchType(() => eMulti.TrackPropertyValue(soOther.FindProperty("count"), _ => { })));
        Frames();
        soMulti.FindProperty("count").intValue = 33;
        soMulti.ApplyModifiedProperties();
        Frames();
        Put("multi.afterCount", string.Join("|", multiLog));

        // 9. Unbind on element (allows rebinding to another SerializedObject) and on parent container.
        e1Events.Clear();
        e1.Unbind();
        Put("unbind.hasBindingAfter", e1.HasBinding(UpdaterBindingId));
        so1.FindProperty("count").intValue = 100;
        so1.ApplyModifiedProperties();
        Frames();
        Put("unbind.afterSelfUnbind", e1Events.Count);

        var e1Rebound = new List<string>();
        e1.TrackPropertyValue(soOther.FindProperty("count"), p => e1Rebound.Add("soOther:" + p.intValue));
        Frames();
        soOther.FindProperty("count").intValue = 55;
        soOther.ApplyModifiedProperties();
        Frames();
        Put("unbind.afterRebindOtherSo", string.Join("|", e1Rebound));

        var container = new VisualElement();
        var childInContainer = new VisualElement();
        container.Add(childInContainer);
        m_Root.Add(container);
        var childLog = new List<string>();
        childInContainer.TrackPropertyValue(so1.FindProperty("count"), p => childLog.Add("c:" + p.intValue));
        Frames();
        container.Unbind();
        so1.FindProperty("count").intValue = 101;
        so1.ApplyModifiedProperties();
        Frames();
        Put("unbind.afterParentUnbind", childLog.Count);

        // 10. Panel detach suppresses callbacks even while sibling updates the same SO; re-attach resumes.
        var tDet = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soDet = new SerializedObject(tDet);
        var eDet = new VisualElement();
        var eSib = new VisualElement();
        m_Root.Add(eDet);
        m_Root.Add(eSib);
        var detLog = new List<string>();
        var sibLog = new List<string>();
        eDet.TrackPropertyValue(soDet.FindProperty("count"), p => detLog.Add("det:" + p.intValue));
        eSib.TrackPropertyValue(soDet.FindProperty("count"), p => sibLog.Add("sib:" + p.intValue));
        Frames();

        soDet.FindProperty("count").intValue = 12;
        soDet.ApplyModifiedProperties();
        Frames();
        Put("detach.beforeDetachDet", string.Join("|", detLog));
        Put("detach.beforeDetachSib", string.Join("|", sibLog));

        eDet.RemoveFromHierarchy();
        Put("detach.hasBindingWhileDetached", eDet.HasBinding(UpdaterBindingId));
        soDet.FindProperty("count").intValue = 13;
        soDet.ApplyModifiedProperties();
        Frames();
        Put("detach.whileDetachedDet", string.Join("|", detLog));
        Put("detach.whileDetachedSib", string.Join("|", sibLog));

        m_Root.Add(eDet);
        Frames();
        Put("detach.immediatelyAfterReattachDet", string.Join("|", detLog));
        soDet.FindProperty("count").intValue = 14;
        soDet.ApplyModifiedProperties();
        Frames();
        Put("detach.afterReattachEditDet", string.Join("|", detLog));
        Put("detach.afterReattachEditSib", string.Join("|", sibLog));

        // 11. Deferred registration on a detached element + cancellation via Unbind before attach.
        var tDef = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soDef = new SerializedObject(tDef);
        var eDef = new VisualElement();
        var eDefCancel = new VisualElement();
        var defLog = new List<string>();
        var defCancelLog = new List<string>();
        eDef.TrackPropertyValue(soDef.FindProperty("count"), p => defLog.Add("def:" + p.intValue));
        eDefCancel.TrackPropertyValue(soDef.FindProperty("count"), p => defCancelLog.Add("cancel:" + p.intValue));
        eDefCancel.Unbind();

        soDef.FindProperty("count").intValue = 20;
        soDef.ApplyModifiedProperties();
        Frames();
        Put("deferred.whileDetached", defLog.Count);

        m_Root.Add(eDef);
        m_Root.Add(eDefCancel);
        Frames();
        Put("deferred.onAttach", string.Join("|", defLog));

        soDef.FindProperty("count").intValue = 21;
        soDef.ApplyModifiedProperties();
        Frames();
        Put("deferred.afterAttachEdit", string.Join("|", defLog));
        Put("deferred.cancelledCount", defCancelLog.Count);

        // 12. Disposed SerializedObject cleans up cleanly on update.
        var tDisp = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soDisp = new SerializedObject(tDisp);
        var eDisp = new VisualElement();
        m_Root.Add(eDisp);
        int dispCount = 0;
        eDisp.TrackPropertyValue(soDisp.FindProperty("count"), _ => dispCount++);
        Frames();
        soDisp.Dispose();
        Frames();
        Put("disposed.count", dispCount);
        Put("disposed.hasBindingAfterUpdate", eDisp.HasBinding(UpdaterBindingId));

        // 13. Multi-object SerializedObject hasMultipleDifferentValues transition.
        var tA = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var tB = ScriptableObject.CreateInstance<TrackProbeTarget>();
        tA.count = 5;
        tB.count = 5;
        var soMultiObj = new SerializedObject(new UnityEngine.Object[] { tA, tB });
        var eMultiObj = new VisualElement();
        m_Root.Add(eMultiObj);
        var multiObjLog = new List<string>();
        eMultiObj.TrackPropertyValue(soMultiObj.FindProperty("count"), p =>
            multiObjLog.Add("diff=" + p.hasMultipleDifferentValues + ";v=" + p.intValue));
        Frames();
        Put("multiObj.initial", multiObjLog.Count);
        tB.count = 9;
        Frames();
        Put("multiObj.afterDivergeWithoutDirty", string.Join("|", multiObjLog));
        soMultiObj.SetIsDifferentCacheDirty();
        Frames();
        Put("multiObj.afterDirtyCache", string.Join("|", multiObjLog));

        // 14. Runtime DataBinding coexistence control.
        var rtSource = new TrackRuntimeSource();
        var tRt = ScriptableObject.CreateInstance<TrackProbeTarget>();
        var soRt = new SerializedObject(tRt);
        var lbl = new Label("initial");
        lbl.dataSource = rtSource;
        lbl.SetBinding("text", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(TrackRuntimeSource.message)),
            bindingMode = BindingMode.ToTarget,
        });
        m_Root.Add(lbl);
        var rtTrackLog = new List<string>();
        lbl.TrackPropertyValue(soRt.FindProperty("count"), p => rtTrackLog.Add("track:" + p.intValue));
        Frames();
        Put("coexist.initialText", lbl.text);
        Put("coexist.initialTrack", rtTrackLog.Count);

        rtSource.message = "world";
        soRt.FindProperty("count").intValue = 42;
        soRt.ApplyModifiedProperties();
        Frames();
        Put("coexist.afterBothText", lbl.text);
        Put("coexist.afterBothTrack", string.Join("|", rtTrackLog));

        lbl.Unbind();
        rtSource.message = "still-bound";
        soRt.FindProperty("count").intValue = 99;
        soRt.ApplyModifiedProperties();
        Frames();
        Put("coexist.afterUnbindText", lbl.text);
        Put("coexist.afterUnbindTrack", string.Join("|", rtTrackLog));
        Put("coexist.hasTextBinding", lbl.HasBinding("text"));
        Put("coexist.hasUpdaterBinding", lbl.HasBinding(UpdaterBindingId));

        so0.Dispose();
        so1.Dispose();
        soNested.Dispose();
        soImm1.Dispose();
        soImm2.Dispose();
        soEvt.Dispose();
        soMulti.Dispose();
        soOther.Dispose();
        soDet.Dispose();
        soDef.Dispose();
        soMultiObj.Dispose();
        soRt.Dispose();
#endif
    }
}
