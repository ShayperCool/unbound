using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>
/// Batched Play-mode probe for Q629 animation remainder items: BlendTree motion stats,
/// Animation.SyncLayer, obsolete Animation.cullingType coercion, AnimationMode.GetCurveBindings,
/// AnimationUtility.PropertyModificationToEditorCurveBinding, AnimationMode.AddPropertyModification,
/// AnimationUtility.EditorCurveBindingsToGenericBindings / GenericBinding, and
/// AnimatorControllerPlayable.IsParameterControlledByCurve.
/// </summary>
[InitializeOnLoad]
public static class AnimRestProbe
{
    const string Variable = "ANIM_REST_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<bool> s_Steps;

    static AnimRestProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Log) s_Logs.Add(type + ": " + message.Split('\n')[0]);
            };
            s_Steps = Steps();
            EditorApplication.update += Tick;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Tick()
    {
        bool more;
        try { more = s_Steps.MoveNext(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message); more = false; }
        if (more) return;
        EditorApplication.update -= Tick;
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        if (value is bool b)
        {
            s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(b ? "true" : "false").Append("}\n");
            return;
        }
        if (value is int i)
        {
            s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(i.ToString(CultureInfo.InvariantCulture)).Append("}\n");
            return;
        }
        if (value is float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f))
                s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(F(f)).Append("\"}\n");
            else
                s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(f.ToString("G9", CultureInfo.InvariantCulture)).Append("}\n");
            return;
        }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(text)).Append("\"}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
    static string F(float v) => Math.Abs(v) < 1e-6f ? "0" : v.ToString("0.####", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";
    static string N(Object o) => o == null ? "null" : o.name;
    static string Binding(EditorCurveBinding b) => $"{b.path}|{b.type?.Name ?? "null"}|{b.propertyName}|{b.isPPtrCurve}|{b.isDiscreteCurve}";

    static void Logs(string key)
    {
        Put(key + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    static void Try(string key, Action action)
    {
        try { action(); }
        catch (Exception e) { Put(key + ".threw", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
    }

    static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Clips/" + name + ".anim");

    static GameObject MakeRig(string name)
    {
        var rig = new GameObject(name);
        rig.AddComponent<ProbeTarget>();
        var child = new GameObject("Child");
        child.transform.SetParent(rig.transform, false);
        child.AddComponent<MeshRenderer>();
        var child2 = new GameObject("Child2");
        child2.transform.SetParent(rig.transform, false);
        return rig;
    }

    static IEnumerator<bool> Steps()
    {
        Time.captureDeltaTime = 0.125f;
        yield return true;

        BlendTreeStats();
        foreach (var step in SyncLayerProbe()) yield return step;
        CullingTypeProbe();
        ModeBindingsProbe();
        PropertyModificationProbe();
        GenericBindingsProbe();
        foreach (var step in PlayableCurveProbe()) yield return step;
        foreach (var step in PlayTimingProbe()) yield return step;
        PropertyModificationExtraProbe();
    }

    // ------------------------------------------------------------------ 1. BlendTree stats

    static AnimationClip MakeRootTurnClip()
    {
        var clip = new AnimationClip { name = "RootTurn", frameRate = 60f };
        // RootT.x: 0 -> 4 over 2s => speed.x = 2
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.x"), AnimationCurve.Linear(0f, 0f, 2f, 4f));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"), AnimationCurve.Constant(0f, 2f, 0f));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.z"), AnimationCurve.Constant(0f, 2f, 0f));
        // RootQ: 0 -> 90 deg around Y over 2s => angularSpeed = pi/4 rad/s
        float s45 = Mathf.Sin(Mathf.PI * 0.25f);
        float c45 = Mathf.Cos(Mathf.PI * 0.25f);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.x"), AnimationCurve.Constant(0f, 2f, 0f));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.y"), AnimationCurve.Linear(0f, 0f, 2f, s45));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.z"), AnimationCurve.Constant(0f, 2f, 0f));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.w"), AnimationCurve.Linear(0f, 1f, 2f, c45));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0f;
        settings.stopTime = 2f;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    static void PutMotion(string key, Motion m)
    {
        Put(key + ".dur", m.averageDuration);
        Put(key + ".sx", m.averageSpeed.x);
        Put(key + ".sy", m.averageSpeed.y);
        Put(key + ".sz", m.averageSpeed.z);
        Put(key + ".ang", m.averageAngularSpeed);
        Put(key + ".app", m.apparentSpeed);
    }

    static void BlendTreeStats()
    {
        var root = Clip("Root");
        var walk = Clip("Walk");
        var emptyClip = Clip("Empty");
        var rootTurn = MakeRootTurnClip();
        PutMotion("blendtree.clip.root", root);
        PutMotion("blendtree.clip.rootTurn", rootTurn);
        PutMotion("blendtree.clip.empty", emptyClip);

        var emptyTree = new BlendTree { name = "EmptyTree" };
        PutMotion("blendtree.empty", emptyTree);

        var nullTree = new BlendTree { name = "NullTree" };
        nullTree.AddChild(null);
        nullTree.AddChild(root);
        PutMotion("blendtree.nullChild", nullTree);

        var withEmpty = new BlendTree { name = "WithEmpty" };
        withEmpty.AddChild(root);
        withEmpty.AddChild(emptyClip);
        PutMotion("blendtree.withEmpty", withEmpty);

        var trulyEmpty = new AnimationClip { name = "TrulyEmpty" };
        var withTrulyEmpty = new BlendTree { name = "WithTrulyEmpty" };
        withTrulyEmpty.AddChild(root);
        withTrulyEmpty.AddChild(trulyEmpty);
        PutMotion("blendtree.withTrulyEmpty", withTrulyEmpty);

        var twoClips = new BlendTree { name = "TwoClips" };
        twoClips.AddChild(root);
        twoClips.AddChild(rootTurn);
        PutMotion("blendtree.twoClips", twoClips);

        var withZero = new BlendTree { name = "WithZero" };
        withZero.AddChild(root);
        withZero.AddChild(walk);
        PutMotion("blendtree.withZero", withZero);

        var timeScale = new BlendTree { name = "TimeScale" };
        timeScale.AddChild(root);
        timeScale.AddChild(rootTurn);
        var children = timeScale.children;
        children[0].timeScale = 2f;
        children[1].timeScale = 0.5f;
        timeScale.children = children;
        PutMotion("blendtree.timeScale", timeScale);

        var inner = new BlendTree { name = "Inner" };
        inner.AddChild(rootTurn);
        inner.AddChild(walk);
        var nested = new BlendTree { name = "Nested" };
        nested.AddChild(root);
        nested.AddChild(inner);
        PutMotion("blendtree.nested", nested);

        Logs("blendtree");
    }

    // ------------------------------------------------------------------ 2. Animation.SyncLayer

    static AnimationClip MakeLegacyClip(string name, float length, string prop, float targetValue)
    {
        var clip = new AnimationClip { name = name, legacy = true, wrapMode = WrapMode.Loop };
        clip.SetCurve("Child", typeof(Transform), prop, AnimationCurve.Linear(0f, 0f, length, targetValue));
        return clip;
    }

    static void PutAnimState(string prefix, AnimationState s)
    {
        Put(prefix + ".time", s.time);
        Put(prefix + ".ntime", s.normalizedTime);
        Put(prefix + ".speed", s.speed);
        Put(prefix + ".nspeed", s.normalizedSpeed);
    }

    static IEnumerable<bool> SyncLayerProbe()
    {
        var shortClip = MakeLegacyClip("Short", 1f, "m_LocalPosition.x", 1f);
        var longClip = MakeLegacyClip("Long", 2f, "m_LocalPosition.y", 2f);
        var otherClip = MakeLegacyClip("Other", 2f, "m_LocalPosition.z", 2f);
        var disClip = MakeLegacyClip("Dis", 1f, "m_LocalScale.x", 2f);

        // Control A: unsynced
        var goA = MakeRig("SyncUnsynced");
        var animA = goA.AddComponent<Animation>();
        animA.playAutomatically = false;
        animA.cullingType = AnimationCullingType.AlwaysAnimate;
        animA.AddClip(shortClip, "Short");
        animA.AddClip(longClip, "Long");
        var aS = animA["Short"]; aS.layer = 0; aS.weight = 0.25f; aS.speed = 1f; aS.time = 0.1f; aS.enabled = true;
        var aL = animA["Long"]; aL.layer = 0; aL.weight = 0.75f; aL.speed = 3f; aL.time = 1.0f; aL.enabled = true;

        // Control B: synced layer 0, unsynced layer 1, disabled state on layer 0
        var goB = MakeRig("SyncSynced");
        var animB = goB.AddComponent<Animation>();
        animB.playAutomatically = false;
        animB.cullingType = AnimationCullingType.AlwaysAnimate;
        animB.AddClip(shortClip, "Short");
        animB.AddClip(longClip, "Long");
        animB.AddClip(otherClip, "Other");
        animB.AddClip(disClip, "Dis");
        var bS = animB["Short"]; bS.layer = 0; bS.weight = 0.25f; bS.speed = 1f; bS.time = 0.1f; bS.enabled = true;
        var bL = animB["Long"]; bL.layer = 0; bL.weight = 0.75f; bL.speed = 3f; bL.time = 1.0f; bL.enabled = true;
        var bO = animB["Other"]; bO.layer = 1; bO.weight = 1f; bO.speed = 2f; bO.time = 0.5f; bO.enabled = true;
        var bD = animB["Dis"]; bD.layer = 0; bD.weight = 1f; bD.speed = 4f; bD.time = 0.8f; bD.enabled = false;
        animB.SyncLayer(0);

        // Immediate readback before first update
        PutAnimState("synclayer.synced.f0.short", bS);
        PutAnimState("synclayer.synced.f0.long", bL);

        // Control C: synced layer 0 with zero weights
        var goC = MakeRig("SyncZeroWeight");
        var animC = goC.AddComponent<Animation>();
        animC.playAutomatically = false;
        animC.cullingType = AnimationCullingType.AlwaysAnimate;
        animC.AddClip(shortClip, "Short");
        animC.AddClip(longClip, "Long");
        var cS = animC["Short"]; cS.layer = 0; cS.weight = 0f; cS.speed = 1f; cS.time = 0.1f; cS.enabled = true;
        var cL = animC["Long"]; cL.layer = 0; cL.weight = 0f; cL.speed = 3f; cL.time = 1.0f; cL.enabled = true;
        animC.SyncLayer(0);

        for (int frame = 1; frame <= 3; frame++)
        {
            yield return true;
            PutAnimState($"synclayer.unsynced.f{frame}.short", aS);
            PutAnimState($"synclayer.unsynced.f{frame}.long", aL);
            PutAnimState($"synclayer.synced.f{frame}.short", bS);
            PutAnimState($"synclayer.synced.f{frame}.long", bL);
            PutAnimState($"synclayer.synced.f{frame}.other", bO);
            PutAnimState($"synclayer.synced.f{frame}.dis", bD);
            PutAnimState($"synclayer.zeroWeight.f{frame}.short", cS);
            PutAnimState($"synclayer.zeroWeight.f{frame}.long", cL);
        }

        Object.Destroy(goA);
        Object.Destroy(goB);
        Object.Destroy(goC);
        Logs("synclayer");
    }

    // ------------------------------------------------------------------ 3. Animation.cullingType

    static void CullingTypeProbe()
    {
        var go = new GameObject("CullTest");
        var anim = go.AddComponent<Animation>();
        Put("culling.default", anim.cullingType.ToString());
        anim.cullingType = AnimationCullingType.AlwaysAnimate;
        Put("culling.always", anim.cullingType.ToString());
#pragma warning disable CS0618
        anim.cullingType = AnimationCullingType.BasedOnClipBounds;
        Put("culling.clipBounds", anim.cullingType.ToString());
        anim.cullingType = AnimationCullingType.AlwaysAnimate;
        anim.cullingType = AnimationCullingType.BasedOnUserBounds;
        Put("culling.userBounds", anim.cullingType.ToString());
#pragma warning restore CS0618
        anim.cullingType = AnimationCullingType.BasedOnRenderers;
        Put("culling.renderers", anim.cullingType.ToString());
        Object.Destroy(go);
        Logs("culling");
    }

    // ------------------------------------------------------------------ 4. GetCurveBindings (Utility & Mode)

    static EditorCurveBinding[] ModeGetCurveBindings(AnimationClip clip)
    {
        var method = typeof(AnimationMode).GetMethod("GetCurveBindings", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return (EditorCurveBinding[])method.Invoke(null, new object[] { clip });
    }

    static string FormatBindings(EditorCurveBinding[] bindings) =>
        bindings == null ? "null" : string.Join(";", bindings.Select(Binding));

    static void ModeBindingsProbe()
    {
        foreach (var name in new[] { "Mixed", "Run", "Pose", "Empty" })
        {
            var clip = Clip(name);
            Put("modebindings.util." + name, FormatBindings(AnimationUtility.GetCurveBindings(clip)));
            Try("modebindings.mode." + name, () => Put("modebindings.mode." + name, FormatBindings(ModeGetCurveBindings(clip))));
        }

        // Runtime clip with SetEditorCurve (single axis of each transform vector + script + GameObject + PPtr)
        var edClip = new AnimationClip { name = "RuntimeEditor" };
        AnimationUtility.SetEditorCurve(edClip, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "localEulerAnglesRaw.y"), AnimationCurve.Linear(0f, 0f, 1f, 90f));
        AnimationUtility.SetEditorCurve(edClip, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0f, 0f, 1f, 2f));
        AnimationUtility.SetEditorCurve(edClip, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalScale.z"), AnimationCurve.Linear(0f, 1f, 1f, 3f));
        AnimationUtility.SetEditorCurve(edClip, EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "amount"), AnimationCurve.Linear(0f, 0f, 1f, 1f));
        AnimationUtility.SetEditorCurve(edClip, EditorCurveBinding.DiscreteCurve("", typeof(ProbeTarget), "counted"), AnimationCurve.Constant(0f, 1f, 1f));
        AnimationUtility.SetEditorCurve(edClip, EditorCurveBinding.DiscreteCurve("", typeof(ProbeTarget), "mode"), AnimationCurve.Constant(0f, 1f, 1f));
        AnimationUtility.SetEditorCurve(edClip, EditorCurveBinding.FloatCurve("Child2", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0f, 1f, 1f));
        AnimationUtility.SetObjectReferenceCurve(edClip, EditorCurveBinding.PPtrCurve("", typeof(ProbeTarget), "reference"),
            new[] { new ObjectReferenceKeyframe { time = 0f, value = Clip("Walk") } });
        Put("modebindings.util.runtimeEd", FormatBindings(AnimationUtility.GetCurveBindings(edClip)));
        Try("modebindings.mode.runtimeEd", () => Put("modebindings.mode.runtimeEd", FormatBindings(ModeGetCurveBindings(edClip))));

        // Runtime clip with SetCurve (runtime API)
        var rtClip = new AnimationClip { name = "RuntimeSetCurve" };
        rtClip.SetCurve("Child", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
        rtClip.SetCurve("", typeof(ProbeTarget), "amount", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        Put("modebindings.util.runtimeSet", FormatBindings(AnimationUtility.GetCurveBindings(rtClip)));
        Try("modebindings.mode.runtimeSet", () => Put("modebindings.mode.runtimeSet", FormatBindings(ModeGetCurveBindings(rtClip))));

        // Edit a copy of Mixed.anim: remove offset.x and add m_LocalScale.y
        var mixedCopy = Object.Instantiate(Clip("Mixed"));
        AnimationUtility.SetEditorCurve(mixedCopy, EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "offset.x"), null);
        AnimationUtility.SetEditorCurve(mixedCopy, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalScale.y"), AnimationCurve.Linear(0f, 1f, 1f, 2f));
        Put("modebindings.util.mixedEdited", FormatBindings(AnimationUtility.GetCurveBindings(mixedCopy)));
        Try("modebindings.mode.mixedEdited", () => Put("modebindings.mode.mixedEdited", FormatBindings(ModeGetCurveBindings(mixedCopy))));

        // ClearCurves on a copy of Mixed.anim
        var mixedCleared = Object.Instantiate(Clip("Mixed"));
        mixedCleared.ClearCurves();
        Put("modebindings.util.mixedCleared", FormatBindings(AnimationUtility.GetCurveBindings(mixedCleared)));

        Try("modebindings.util.null", () => Put("modebindings.util.null", FormatBindings(AnimationUtility.GetCurveBindings(null))));
        Try("modebindings.mode.null", () => Put("modebindings.mode.null", FormatBindings(ModeGetCurveBindings(null))));

        Logs("modebindings");
    }

    // ------------------------------------------------------------------ 5. PropertyModification

    static void PropertyModificationProbe()
    {
        var rig = MakeRig("PropModRig");
        var other = new GameObject("Unrelated");
        var child = rig.transform.Find("Child");
        var child2 = rig.transform.Find("Child2").gameObject;
        var target = rig.GetComponent<ProbeTarget>();
        var renderer = child.GetComponent<MeshRenderer>();
        var matA = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "MatA" };
        var matB = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "MatB" };
        renderer.sharedMaterial = matA;

        var cases = new (string Name, Object Target, string Prop)[]
        {
            ("posX", child, "m_LocalPosition.x"),
            ("rotW", child, "m_LocalRotation.w"),
            ("scaleZ", child, "m_LocalScale.z"),
            ("eulerY", child, "localEulerAnglesRaw.y"),
            ("eulerHintX", child, "m_LocalEulerAnglesHint.x"),
            ("active", child2, "m_IsActive"),
            ("floatField", target, "amount"),
            ("intField", target, "steps"),
            ("boolField", target, "flag"),
            ("vecField", target, "offset.y"),
            ("enumField", target, "mode"),
            ("discreteInt", target, "counted"),
            ("objField", target, "reference"),
            ("unknown", target, "nothing"),
            ("renEnabled", renderer, "m_Enabled"),
            ("renMat0", renderer, "m_Materials.Array.data[0]"),
            ("unrelated", other.transform, "m_LocalPosition.x"),
            ("nullTarget", null, "amount"),
        };

        foreach (var (name, obj, prop) in cases)
        {
            var mod = new PropertyModification { target = obj, propertyPath = prop, value = "1" };
            Try("propmod.conv." + name, () =>
            {
                var t = AnimationUtility.PropertyModificationToEditorCurveBinding(mod, rig, out var b);
                Put("propmod.conv." + name, "type=" + (t == null ? "null" : t.Name) + " " + Binding(b));
            });
        }
        Try("propmod.conv.nullMod", () =>
        {
            var t = AnimationUtility.PropertyModificationToEditorCurveBinding(null, rig, out var b);
            Put("propmod.conv.nullMod", "type=" + (t == null ? "null" : t.Name) + " " + Binding(b));
        });
        Try("propmod.conv.nullRoot", () =>
        {
            var mod = new PropertyModification { target = target, propertyPath = "amount", value = "1" };
            var t = AnimationUtility.PropertyModificationToEditorCurveBinding(mod, null, out var b);
            Put("propmod.conv.nullRoot", "type=" + (t == null ? "null" : t.Name) + " " + Binding(b));
        });

        // AnimationMode.AddPropertyModification outside mode
        Try("propmod.add.outside", () =>
            AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "steps"),
                new PropertyModification { target = target, propertyPath = "steps", value = "2" }, true));

        // Inside AnimationMode: diverging value control & duplicate registration control
        var driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
        AnimationMode.StartAnimationMode(driver);

        target.steps = 5;
        target.amount = 1.5f;
        target.flag = false;
        target.reference = Clip("Walk");
        child.localPosition = new Vector3(1, 2, 3);
        child2.SetActive(true);
        renderer.sharedMaterial = matA;

        // Diverging value: current steps is 5, modification says "2", then mutate to 9 -> Stop reverts to?
        AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "steps"),
            new PropertyModification { target = target, propertyPath = "steps", value = "2" }, true);
        target.steps = 9;

        // Duplicate registration: first says "10", second says "20", mutate to 99 -> Stop reverts to?
        AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "amount"),
            new PropertyModification { target = target, propertyPath = "amount", value = "10" }, true);
        AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "amount"),
            new PropertyModification { target = target, propertyPath = "amount", value = "20" }, true);
        target.amount = 99f;

        // Bool, GameObject active, Transform pos.x, Renderer material, script Object ref
        AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "flag"),
            new PropertyModification { target = target, propertyPath = "flag", value = "1" }, true);
        target.flag = false;

        AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("Child2", typeof(GameObject), "m_IsActive"),
            new PropertyModification { target = child2, propertyPath = "m_IsActive", value = "0" }, true);
        child2.SetActive(true);

        AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x"),
            new PropertyModification { target = child, propertyPath = "m_LocalPosition.x", value = "4" }, true);
        child.localPosition = new Vector3(8, 2, 3);

        AnimationMode.AddPropertyModification(EditorCurveBinding.PPtrCurve("Child", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
            new PropertyModification { target = renderer, propertyPath = "m_Materials.Array.data[0]", objectReference = matA }, true);
        renderer.sharedMaterial = matB;

        AnimationMode.AddPropertyModification(EditorCurveBinding.PPtrCurve("", typeof(ProbeTarget), "reference"),
            new PropertyModification { target = target, propertyPath = "reference", objectReference = Clip("Walk") }, true);
        target.reference = Clip("Run");

        Put("propmod.add.animated",
            $"steps={AnimationMode.IsPropertyAnimated(target, "steps")} amount={AnimationMode.IsPropertyAnimated(target, "amount")} " +
            $"flag={AnimationMode.IsPropertyAnimated(target, "flag")} active={AnimationMode.IsPropertyAnimated(child2, "m_IsActive")} " +
            $"posX={AnimationMode.IsPropertyAnimated(child, "m_LocalPosition.x")} pos={AnimationMode.IsPropertyAnimated(child, "m_LocalPosition")} " +
            $"mat={AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0]")} ref={AnimationMode.IsPropertyAnimated(target, "reference")}");

        Try("propmod.add.nullMod", () => AnimationMode.AddPropertyModification(default, null, true));
        Try("propmod.add.nullTarget", () => AnimationMode.AddPropertyModification(default, new PropertyModification { target = null, propertyPath = "x", value = "1" }, true));

        AnimationMode.StopAnimationMode(driver);

        Put("propmod.add.reverted",
            $"steps={target.steps} amount={F(target.amount)} flag={target.flag} active={child2.activeSelf} " +
            $"pos={V(child.localPosition)} mat={N(renderer.sharedMaterial)} ref={N(target.reference)}");

        Object.Destroy(rig);
        Object.Destroy(other);
        Logs("propmod");
    }

    // ------------------------------------------------------------------ 6. GenericBinding

    static void GenericBindingsProbe()
    {
        var input = new[]
        {
            EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x"),
            EditorCurveBinding.FloatCurve("Child2", typeof(GameObject), "m_IsActive"),
            EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "amount"),
            EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "steps"),
            EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "flag"),
            EditorCurveBinding.DiscreteCurve("", typeof(ProbeTarget), "counted"),
            EditorCurveBinding.DiscreteCurve("", typeof(ProbeTarget), "mode"),
            EditorCurveBinding.PPtrCurve("", typeof(ProbeTarget), "reference"),
            EditorCurveBinding.FloatCurve("Child", typeof(MeshRenderer), "m_Enabled"),
            EditorCurveBinding.PPtrCurve("Child", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
            EditorCurveBinding.FloatCurve("", typeof(Animator), "Speed"),
        };
        var result = AnimationUtility.EditorCurveBindingsToGenericBindings(input);
        Put("generic.count", result.Length);
        for (int i = 0; i < result.Length; i++)
        {
            var g = result[i];
            Put("generic.item." + i,
                $"obj={g.isObjectReference}/disc={g.isDiscrete}/serRef={g.isSerializeReference}/type={g.typeID}/custom={g.customTypeID}/path={g.transformPathHash}/prop={g.propertyNameHash}/hasScript={g.scriptInstanceID != 0}");
        }
        Put("generic.empty", AnimationUtility.EditorCurveBindingsToGenericBindings(Array.Empty<EditorCurveBinding>()).Length);
        Try("generic.null", () => Put("generic.null", AnimationUtility.EditorCurveBindingsToGenericBindings(null)?.Length ?? -1));
        Logs("generic");
    }

    // ------------------------------------------------------------------ 7. AnimatorControllerPlayable.IsParameterControlledByCurve

    static IEnumerable<bool> PlayableCurveProbe()
    {
        var c1 = new AnimatorController { name = "CurveCtrl1" };
        c1.AddLayer("Base Layer");
        c1.AddParameter("CurveParam", AnimatorControllerParameterType.Float);
        c1.AddParameter("PlainParam", AnimatorControllerParameterType.Float);
        c1.AddParameter("IntParam", AnimatorControllerParameterType.Int);
        var clip = new AnimationClip { name = "CurveClip" };
        clip.SetCurve("", typeof(Animator), "CurveParam", AnimationCurve.Linear(0f, 0f, 1f, 5f));
        c1.AddMotion(clip);

        var c2 = new AnimatorController { name = "OtherCtrl2" };
        c2.AddLayer("Base Layer");
        c2.AddParameter("CurveParam", AnimatorControllerParameterType.Float);

        var go = new GameObject("PlayableCurveRig");
        var animator = go.AddComponent<Animator>();

        var graph = PlayableGraph.Create("CurveGraph");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimatorControllerPlayable.Create(graph, c1);
        var output = AnimationPlayableOutput.Create(graph, "AnimOut", animator);
        output.SetSourcePlayable(playable);

        Put("playablecurve.beforePlay",
            $"curve={playable.IsParameterControlledByCurve("CurveParam")} plain={playable.IsParameterControlledByCurve("PlainParam")} int={playable.IsParameterControlledByCurve("IntParam")} unknown={playable.IsParameterControlledByCurve("Missing")}");

        graph.Play();
        graph.Evaluate(0.1f);
        yield return true;

        Put("playablecurve.playing",
            $"curve={playable.IsParameterControlledByCurve("CurveParam")} plain={playable.IsParameterControlledByCurve("PlainParam")} id={playable.IsParameterControlledByCurve(Animator.StringToHash("CurveParam"))} val={F(playable.GetFloat("CurveParam"))}");

        // Replace Animator's runtimeAnimatorController while graph is alive
        animator.runtimeAnimatorController = c2;
        graph.Evaluate(0.1f);
        yield return true;

        Put("playablecurve.afterSwap",
            $"curve={playable.IsParameterControlledByCurve("CurveParam")} plain={playable.IsParameterControlledByCurve("PlainParam")} val={F(playable.GetFloat("CurveParam"))}");

        graph.Destroy();
        Object.Destroy(go);
        Logs("playablecurve");
    }

    // ------------------------------------------------------------------ 8. Legacy Animation Play() timing controls

    static IEnumerable<bool> PlayTimingProbe()
    {
        float prevCapture = Time.captureDeltaTime;
        float prevFixed = Time.fixedDeltaTime;
        Time.captureDeltaTime = 0.03f;
        Time.fixedDeltaTime = 0.02f;

        var clip = MakeLegacyClip("TimingClip", 2f, "m_LocalPosition.x", 2f);
        Animation MakeAnim(string name, bool fixedMode)
        {
            var go = MakeRig(name);
            var a = go.AddComponent<Animation>();
            a.playAutomatically = false;
            a.cullingType = AnimationCullingType.AlwaysAnimate;
            a.updateMode = fixedMode ? AnimationUpdateMode.Fixed : AnimationUpdateMode.Normal;
            a.AddClip(clip, "C");
            return a;
        }

        var animUpd = MakeAnim("TimingUpd", false);
        var animLate = MakeAnim("TimingLate", false);
        var animCoro = MakeAnim("TimingCoro", false);
        var animFixUpd = MakeAnim("TimingFixUpd", true);
        var animFixFix = MakeAnim("TimingFixFix", true);

        var driverGo = new GameObject("PlayTimingDriver");
        var driver = driverGo.AddComponent<PlayTimingDriver>();

        int phase = 0; // 0=waiting for 0.01 offset, 1..4=recording f0..f3
        bool fixStarted = false;
        double t0 = 0, ft0 = 0;

        driver.OnFixed = () =>
        {
            if (phase >= 1 && !fixStarted)
            {
                fixStarted = true;
                animFixFix.Play("C");
            }
        };
        driver.OnUpdate = () =>
        {
            if (phase == 0)
            {
                double diff = Time.timeAsDouble - Time.fixedTimeAsDouble;
                if (diff >= 0.008 && diff <= 0.012)
                {
                    phase = 1;
                    t0 = Time.timeAsDouble;
                    ft0 = Time.fixedTimeAsDouble;
                    Put("playtiming.offset", (float)diff);
                    animUpd.Play("C");
                    animFixUpd.Play("C");
                    driver.ScheduleAfterUpdate();
                }
            }
            else if (phase >= 1 && phase < 4)
            {
                phase++;
            }
        };
        driver.OnLate = () =>
        {
            if (phase == 1)
            {
                animLate.Play("C");
            }
            if (phase >= 1 && phase <= 4)
            {
                int idx = phase - 1;
                Put($"playtiming.f{idx}.dt", (float)(Time.timeAsDouble - t0));
                Put($"playtiming.f{idx}.dft", (float)(Time.fixedTimeAsDouble - ft0));
                Put($"playtiming.f{idx}.upd", animUpd["C"].time);
                Put($"playtiming.f{idx}.late", animLate["C"].time);
                Put($"playtiming.f{idx}.coro", animCoro["C"].time);
                Put($"playtiming.f{idx}.fixUpd", animFixUpd["C"].time);
                Put($"playtiming.f{idx}.fixFix", animFixFix["C"].time);
                if (phase == 4) phase = 5;
            }
        };
        driver.OnEndOfFrame = () =>
        {
            animCoro.Play("C");
        };

        int guard = 0;
        while (phase < 5 && guard++ < 40) yield return true;

        Object.Destroy(driverGo);
        Object.Destroy(animUpd.gameObject);
        Object.Destroy(animLate.gameObject);
        Object.Destroy(animCoro.gameObject);
        Object.Destroy(animFixUpd.gameObject);
        Object.Destroy(animFixFix.gameObject);
        Time.captureDeltaTime = prevCapture;
        Time.fixedDeltaTime = prevFixed;
        Logs("playtiming");
    }

    // ------------------------------------------------------------------ 9. AddPropertyModification prefab & non-float controls

    static void PropertyModificationExtraProbe()
    {
        // A. Non-float (string) property modification
        var strGo = new GameObject("OrigName");
        var driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
        AnimationMode.StartAnimationMode(driver);
        AnimationMode.AddPropertyModification(
            EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_Name"),
            new PropertyModification { target = strGo, propertyPath = "m_Name", value = "RecordedName" },
            true);
        Put("propmod.extra.str.animated", AnimationMode.IsPropertyAnimated(strGo, "m_Name"));
        strGo.name = "ChangedName";
        AnimationMode.StopAnimationMode(driver);
        Put("propmod.extra.str.reverted", strGo.name);
        Object.Destroy(strGo);

        // B. Prefab instance with keepPrefabOverride false vs true
        const string prefabPath = "Assets/TempPropModPrefab.prefab";
        var src = new GameObject("TempPropModPrefab");
        var srcTarget = src.AddComponent<ProbeTarget>();
        srcTarget.steps = 10;
        srcTarget.amount = 2f;
        var prefab = PrefabUtility.SaveAsPrefabAsset(src, prefabPath);
        Object.DestroyImmediate(src);

        foreach (bool keep in new[] { false, true })
        {
            string tag = keep ? "keepTrue" : "keepFalse";
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var t = inst.GetComponent<ProbeTarget>();
            // Give `amount` an existing prefab override; leave `steps` unoverridden.
            t.amount = 7f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);

            AnimationMode.StartAnimationMode(driver);
            AnimationMode.AddPropertyModification(
                EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "amount"),
                new PropertyModification { target = t, propertyPath = "amount", value = "3" },
                keep);
            AnimationMode.AddPropertyModification(
                EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "steps"),
                new PropertyModification { target = t, propertyPath = "steps", value = "4" },
                keep);
            t.amount = 99f;
            t.steps = 99;
            Put($"propmod.extra.{tag}.animated",
                $"amount={AnimationMode.IsPropertyAnimated(t, "amount")} steps={AnimationMode.IsPropertyAnimated(t, "steps")}");
            AnimationMode.StopAnimationMode(driver);

            var mods = PrefabUtility.GetPropertyModifications(inst) ?? Array.Empty<PropertyModification>();
            var modSummary = string.Join(";", mods
                .Where(m => m.propertyPath == "amount" || m.propertyPath == "steps")
                .OrderBy(m => m.propertyPath, StringComparer.Ordinal)
                .Select(m => $"{m.propertyPath}={m.value}"));
            Put($"propmod.extra.{tag}.reverted", $"amount={F(t.amount)} steps={t.steps} mods={modSummary}");
            Object.DestroyImmediate(inst);
        }

        AssetDatabase.DeleteAsset(prefabPath);
        Logs("propmod.extra");
    }
}
