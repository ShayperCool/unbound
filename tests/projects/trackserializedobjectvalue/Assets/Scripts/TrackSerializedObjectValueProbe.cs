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
public struct TrackObjNested
{
    public int score;
    public string tag;
}

public class TrackObjTarget : ScriptableObject
{
    public int count = 10;
    public int unrelated = 1;
    public string label = "init";
    public TrackObjNested nested = new TrackObjNested { score = 3, tag = "n0" };
    public string[] items = new[] { "a", "b" };
}

public class TrackObjRuntimeSource
{
    public string message = "hello";
}

public class TrackSerializedObjectValueProbe : MonoBehaviour
{
    const string UpdaterBindingId = "--unity-serialized-object-bindings-context-updater";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRACK_SERIALIZED_OBJECT_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        document.panelSettings = settings;
        go.AddComponent<TrackSerializedObjectValueProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("TRACK_SERIALIZED_OBJECT_OUT");
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

        // 1. SerializedObjectChangeEvent defaults.
        var t0 = ScriptableObject.CreateInstance<TrackObjTarget>();
        var so0 = new SerializedObject(t0);
        using (var evt = SerializedObjectChangeEvent.GetPooled(so0))
        {
            Put("evt.bubbles", evt.bubbles);
            Put("evt.tricklesDown", evt.tricklesDown);
            Put("evt.sameObject", ReferenceEquals(evt.changedObject, so0));
        }

        // 2. Argument validation.
        var eArg = new VisualElement();
        m_Root.Add(eArg);
        Put("args.nullObject", CatchType(() => eArg.TrackSerializedObjectValue(null, _ => { })));
        Put("args.nullElement", CatchType(() => ((VisualElement)null).TrackSerializedObjectValue(so0, _ => { })));

        // 3. Unchanged, unrelated edit control vs TrackPropertyValue, tracked edit, unapplied, revert-before-update, direct C# edit.
        var t1 = ScriptableObject.CreateInstance<TrackObjTarget>();
        var so1 = new SerializedObject(t1);
        var eObj = new VisualElement();
        var eProp = new VisualElement();
        m_Root.Add(eObj);
        m_Root.Add(eProp);
        var objLog = new List<string>();
        var propLog = new List<string>();
        eObj.TrackSerializedObjectValue(so1, s =>
            objLog.Add("c=" + s.FindProperty("count").intValue + ",u=" + s.FindProperty("unrelated").intValue + ";sameSo=" + ReferenceEquals(s, so1)));
        eProp.TrackPropertyValue(so1.FindProperty("count"), p =>
            propLog.Add("count=" + p.intValue));
        Put("basic.hasBindingBeforeUpdate", eObj.HasBinding(UpdaterBindingId));

        Frames();
        Put("basic.unchanged1", objLog.Count);
        Frames();
        Put("basic.unchanged2", objLog.Count);

        // Unrelated property edit: TrackSerializedObjectValue fires, TrackPropertyValue on "count" does not.
        so1.FindProperty("unrelated").intValue = 99;
        so1.ApplyModifiedProperties();
        Frames();
        Put("basic.afterUnrelatedObj", string.Join("|", objLog));
        Put("basic.afterUnrelatedProp", propLog.Count);

        // Tracked property edit: both fire.
        so1.FindProperty("count").intValue = 11;
        so1.ApplyModifiedProperties();
        Frames();
        Put("basic.afterCountObj", string.Join("|", objLog));
        Put("basic.afterCountProp", string.Join("|", propLog));

        // Stable across subsequent frames.
        Frames();
        Put("basic.afterStable", objLog.Count);

        // Unapplied edit (without ApplyModifiedProperties).
        int countBeforeUnapplied = objLog.Count;
        so1.FindProperty("count").intValue = 44;
        Frames();
        Put("basic.afterUnappliedDelta", objLog.Count - countBeforeUnapplied);
        Put("basic.valueAfterUnapplied", so1.FindProperty("count").intValue);

        // Edit and revert before panel update.
        int countBeforeRevert = objLog.Count;
        so1.FindProperty("count").intValue = 50;
        so1.ApplyModifiedProperties();
        so1.FindProperty("count").intValue = 11;
        so1.ApplyModifiedProperties();
        Frames();
        Put("basic.afterRevertDelta", objLog.Count - countBeforeRevert);

        // Direct C# field mutation detected during panel update.
        int countBeforeDirect = objLog.Count;
        t1.count = 22;
        Frames();
        Put("basic.afterDirectDelta", objLog.Count - countBeforeDirect);
        Put("basic.afterDirectLast", objLog.Count > 0 ? objLog[objLog.Count - 1] : "");

        // 4. Nested struct and array edits.
        var tNested = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soNested = new SerializedObject(tNested);
        var eNested = new VisualElement();
        m_Root.Add(eNested);
        int nestedCalls = 0;
        eNested.TrackSerializedObjectValue(soNested, _ => nestedCalls++);
        Frames();
        Put("nested.initial", nestedCalls);

        soNested.FindProperty("nested.score").intValue = 7;
        soNested.ApplyModifiedProperties();
        Frames();
        Put("nested.afterScore", nestedCalls);

        soNested.FindProperty("items.Array.data[0]").stringValue = "b";
        soNested.ApplyModifiedProperties();
        Frames();
        Put("nested.afterItem0", nestedCalls);

        soNested.FindProperty("items.Array.size").intValue = 3;
        soNested.ApplyModifiedProperties();
        Frames();
        Put("nested.afterSize", nestedCalls);

        // 5. Immediate mutation before first panel update after TrackSerializedObjectValue.
        var tImm1 = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soImm1 = new SerializedObject(tImm1);
        var eImm1 = new VisualElement();
        m_Root.Add(eImm1);
        var imm1Log = new List<string>();
        eImm1.TrackSerializedObjectValue(soImm1, s => imm1Log.Add("v=" + s.FindProperty("count").intValue));
        soImm1.FindProperty("count").intValue = 77;
        soImm1.ApplyModifiedProperties();
        Frames();
        Put("immediate.applyBeforeFirstUpdate", string.Join("|", imm1Log));

        var tImm2 = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soImm2 = new SerializedObject(tImm2);
        var eImm2 = new VisualElement();
        m_Root.Add(eImm2);
        var imm2Log = new List<string>();
        eImm2.TrackSerializedObjectValue(soImm2, s => imm2Log.Add("v=" + s.FindProperty("count").intValue));
        tImm2.count = 88;
        Frames();
        Put("immediate.directBeforeFirstUpdate", string.Join("|", imm2Log));

        // 6. Null callback dispatches bubbling SerializedObjectChangeEvent (not SerializedPropertyChangeEvent).
        var tEvt = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soEvt = new SerializedObject(tEvt);
        var evtParent = new VisualElement();
        var evtChild = new VisualElement();
        evtParent.Add(evtChild);
        m_Root.Add(evtParent);
        var evtLog = new List<string>();
        int propEvtCount = 0;
        evtChild.RegisterCallback<SerializedObjectChangeEvent>(e =>
            evtLog.Add("child:" + e.changedObject.FindProperty("label").stringValue + ";sameSo=" + ReferenceEquals(e.changedObject, soEvt)));
        evtParent.RegisterCallback<SerializedObjectChangeEvent>(e =>
            evtLog.Add("parent:" + e.changedObject.FindProperty("label").stringValue));
        evtChild.RegisterCallback<SerializedPropertyChangeEvent>(_ => propEvtCount++);
        evtChild.TrackSerializedObjectValue(soEvt);
        Frames();
        Put("nullCb.initial", evtLog.Count);
        soEvt.FindProperty("label").stringValue = "updated";
        soEvt.ApplyModifiedProperties();
        Frames();
        Put("nullCb.afterEdit", string.Join("|", evtLog));
        Put("nullCb.propEventCount", propEvtCount);

        // 7. Multiple callbacks + coexistence with TrackPropertyValue on same element + second SO throws.
        var tMulti = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soMulti = new SerializedObject(tMulti);
        var tOther = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soOther = new SerializedObject(tOther);
        var eMulti = new VisualElement();
        m_Root.Add(eMulti);
        var multiLog = new List<string>();
        eMulti.TrackSerializedObjectValue(soMulti, s => multiLog.Add("obj1:" + s.FindProperty("count").intValue));
        eMulti.TrackSerializedObjectValue(soMulti, s => multiLog.Add("obj2:" + s.FindProperty("count").intValue));
        eMulti.TrackPropertyValue(soMulti.FindProperty("count"), p => multiLog.Add("prop:" + p.intValue));
        Put("multi.secondSoThrows", CatchType(() => eMulti.TrackSerializedObjectValue(soOther, _ => { })));
        Frames();
        soMulti.FindProperty("count").intValue = 33;
        soMulti.ApplyModifiedProperties();
        Frames();
        Put("multi.afterCount", string.Join("|", multiLog));
        multiLog.Clear();
        soMulti.FindProperty("unrelated").intValue = 5;
        soMulti.ApplyModifiedProperties();
        Frames();
        Put("multi.afterUnrelated", string.Join("|", multiLog));

        // 8. Unbind on element (allows rebinding to another SO) and on parent container.
        int beforeUnbind = objLog.Count;
        eObj.Unbind();
        Put("unbind.hasBindingAfter", eObj.HasBinding(UpdaterBindingId));
        so1.FindProperty("count").intValue = 100;
        so1.ApplyModifiedProperties();
        Frames();
        Put("unbind.afterSelfUnbindDelta", objLog.Count - beforeUnbind);

        var eObjRebound = new List<string>();
        eObj.TrackSerializedObjectValue(soOther, s => eObjRebound.Add("soOther:" + s.FindProperty("count").intValue));
        Frames();
        soOther.FindProperty("count").intValue = 55;
        soOther.ApplyModifiedProperties();
        Frames();
        Put("unbind.afterRebindOtherSo", string.Join("|", eObjRebound));

        var container = new VisualElement();
        var childInContainer = new VisualElement();
        container.Add(childInContainer);
        m_Root.Add(container);
        int childCalls = 0;
        childInContainer.TrackSerializedObjectValue(so1, _ => childCalls++);
        Frames();
        container.Unbind();
        so1.FindProperty("count").intValue = 101;
        so1.ApplyModifiedProperties();
        Frames();
        Put("unbind.afterParentUnbind", childCalls);

        // 9. Panel detach suppresses callbacks while detached; measure re-attach behavior.
        var tDet = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soDet = new SerializedObject(tDet);
        var eDet = new VisualElement();
        var eSib = new VisualElement();
        m_Root.Add(eDet);
        m_Root.Add(eSib);
        var detLog = new List<string>();
        var sibLog = new List<string>();
        eDet.TrackSerializedObjectValue(soDet, s => detLog.Add("det:" + s.FindProperty("count").intValue));
        eSib.TrackSerializedObjectValue(soDet, s => sibLog.Add("sib:" + s.FindProperty("count").intValue));
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

        // 10. Deferred registration on detached element + cancellation via Unbind before attach.
        var tDef = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soDef = new SerializedObject(tDef);
        var eDef = new VisualElement();
        var eDefCancel = new VisualElement();
        var defLog = new List<string>();
        var defCancelLog = new List<string>();
        eDef.TrackSerializedObjectValue(soDef, s => defLog.Add("def:" + s.FindProperty("count").intValue));
        eDefCancel.TrackSerializedObjectValue(soDef, s => defCancelLog.Add("cancel:" + s.FindProperty("count").intValue));
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

        // 11. Disposed SerializedObject cleans up cleanly on update.
        var tDisp = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soDisp = new SerializedObject(tDisp);
        var eDisp = new VisualElement();
        m_Root.Add(eDisp);
        int dispCount = 0;
        eDisp.TrackSerializedObjectValue(soDisp, _ => dispCount++);
        Frames();
        soDisp.Dispose();
        Frames();
        Put("disposed.count", dispCount);
        Put("disposed.hasBindingAfterUpdate", eDisp.HasBinding(UpdaterBindingId));

        // 12. Two-object SerializedObject: secondary edit without/with SetIsDifferentCacheDirty and primary edit.
        var tA = ScriptableObject.CreateInstance<TrackObjTarget>();
        var tB = ScriptableObject.CreateInstance<TrackObjTarget>();
        tA.count = 5;
        tB.count = 5;
        var soTwo = new SerializedObject(new UnityEngine.Object[] { tA, tB });
        var eTwo = new VisualElement();
        m_Root.Add(eTwo);
        var twoLog = new List<string>();
        eTwo.TrackSerializedObjectValue(soTwo, s =>
            twoLog.Add("diff=" + s.FindProperty("count").hasMultipleDifferentValues + ";v=" + s.FindProperty("count").intValue));
        Frames();
        Put("twoObj.initial", twoLog.Count);

        tB.count = 9;
        Frames();
        Put("twoObj.afterSecondaryWithoutDirty", string.Join("|", twoLog));

        soTwo.SetIsDifferentCacheDirty();
        Frames();
        Put("twoObj.afterDirtyCache", string.Join("|", twoLog));

        tA.count = 9;
        Frames();
        Put("twoObj.afterPrimaryConverge", string.Join("|", twoLog));

        // 13. Runtime DataBinding coexistence control.
        var rtSource = new TrackObjRuntimeSource();
        var tRt = ScriptableObject.CreateInstance<TrackObjTarget>();
        var soRt = new SerializedObject(tRt);
        var lbl = new Label("initial");
        lbl.dataSource = rtSource;
        lbl.SetBinding("text", new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(TrackObjRuntimeSource.message)),
            bindingMode = BindingMode.ToTarget,
        });
        m_Root.Add(lbl);
        var rtTrackLog = new List<string>();
        lbl.TrackSerializedObjectValue(soRt, s => rtTrackLog.Add("track:" + s.FindProperty("count").intValue));
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
        soTwo.Dispose();
        soRt.Dispose();
#endif
    }
}
