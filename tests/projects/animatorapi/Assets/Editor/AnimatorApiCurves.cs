using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// More of AnimatorApiProbe's Play mode record (see README.md): the size and curve counts
/// GetAnimationClipStats answers for clips of known content, which sampling paths apply float,
/// enum and object curves to script fields of several types, and what AnimationMode's property
/// modifications revert.
/// </summary>
public static partial class AnimatorApiProbe
{
    const string TypedPrefab = "Assets/Prefabs/Typed.prefab";

    /// <summary>Authors the prefab the property modification cases instantiate (run once in the
    /// Editor; the result is committed).</summary>
    public static void SetupCurves()
    {
        Directory.CreateDirectory("Assets/Prefabs");
        AssetDatabase.Refresh();
        var source = new GameObject("Typed");
        source.AddComponent<TypedTarget>();
        PrefabUtility.SaveAsPrefabAsset(source, TypedPrefab);
        Object.DestroyImmediate(source);
        var old = new AnimationClip { name = "Old", frameRate = 60, legacy = true };
        old.SetCurve("", typeof(TypedTarget), "f", Keys(0, 0, 1, 1));
        AssetDatabase.CreateAsset(old, "Assets/Clips/Old.anim");
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    static AnimationCurve Keys(params float[] timesAndValues)
    {
        var keys = new Keyframe[timesAndValues.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timesAndValues[2 * i], timesAndValues[2 * i + 1]);
        return new AnimationCurve(keys);
    }

    static string StatsOf(AnimationClip clip)
    {
        var method = typeof(AnimationUtility).GetMethod("GetAnimationClipStats", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var stats = method.Invoke(null, new object[] { clip });
        return string.Join(" ", stats.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name + "=" + f.GetValue(stats)));
    }

    static void Float(AnimationClip c, string attribute, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("", typeof(TypedTarget), attribute), curve);

    static void Moved(AnimationClip c, string path, string attribute, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve(path, typeof(Transform), attribute), curve);

    static void Refs(AnimationClip c, string attribute, params float[] times)
    {
        var a = Clip("Walk");
        var b = Clip("Run");
        AnimationUtility.SetObjectReferenceCurve(c, EditorCurveBinding.PPtrCurve("", typeof(TypedTarget), attribute),
            times.Select((t, i) => new ObjectReferenceKeyframe { time = t, value = i % 2 == 0 ? a : b }).ToArray());
    }

    static void StatsCase(string key, Action<AnimationClip> build, Action<AnimationClip> after = null) => Try("stats." + key, () =>
    {
        var c = new AnimationClip { name = key, frameRate = 60 };
        build(c);
        after?.Invoke(c);
        Put("stats." + key, StatsOf(c));
        Object.Destroy(c);
    });

    /// <summary>Stats of clips built in memory: float curves of several key counts and times,
    /// constant curves, object curves, Transform vectors, and clips only SetCurve wrote.</summary>
    static void ClipStatsLadder()
    {
        StatsCase("float.k2", c => Float(c, "f", Keys(0, 0, 1, 1)));
        StatsCase("float.k2late", c => Float(c, "f", Keys(1, 0, 2, 1)));
        StatsCase("float.k3", c => Float(c, "f", Keys(0, 0, 1, 1, 2, 0)));
        StatsCase("float.k10", c => Float(c, "f", Keys(0, 0, 1, 1, 2, 0, 3, 1, 4, 0, 5, 1, 6, 0, 7, 1, 8, 0, 9, 1)));
        StatsCase("float.two", c => { Float(c, "f", Keys(0, 0, 1, 1)); Float(c, "i", Keys(0, 0, 1, 1)); });
        StatsCase("float.twoShared", c => { Float(c, "f", Keys(0, 0, 1, 1)); Float(c, "i", Keys(0, 0, 0.5f, 2, 1, 1)); });
        StatsCase("float.twoDisjoint", c => { Float(c, "f", Keys(0, 0, 1, 1)); Float(c, "i", Keys(0.25f, 0, 0.75f, 1)); });
        StatsCase("float.twoLengths", c => { Float(c, "f", Keys(0, 0, 2, 1)); Float(c, "i", Keys(0, 0, 1, 1)); });
        StatsCase("float.dense", c =>
        {
            var keys = new Keyframe[61];
            for (int i = 0; i <= 60; i++) keys[i] = new Keyframe(i / 60f, Mathf.Sin(i * 0.1f));
            Float(c, "f", new AnimationCurve(keys));
        });
        StatsCase("constant.k1", c => Float(c, "f", Keys(0, 3)));
        StatsCase("constant.k3", c => Float(c, "f", Keys(0, 3, 1, 3, 2, 3)));
        StatsCase("constant.two", c => { Float(c, "f", Keys(0, 3)); Float(c, "i", Keys(0, 4)); });
        StatsCase("constant.withFloat", c => { Float(c, "f", Keys(0, 3, 5, 3)); Float(c, "i", Keys(0, 0, 1, 1)); });
        // The same value at every key, with a slope at one side of one key.
        foreach (var (name, key, incoming) in new[] { ("firstIn", 0, true), ("firstOut", 0, false), ("lastIn", 1, true), ("lastOut", 1, false) })
            StatsCase("constant.slope." + name, c =>
            {
                var keys = Keys(0, 3, 1, 3).keys;
                if (incoming) keys[key].inTangent = 2f;
                else keys[key].outTangent = 2f;
                Float(c, "f", new AnimationCurve(keys));
            });
        StatsCase("object.k1", c => Refs(c, "anyRef", 0));
        StatsCase("object.k2", c => Refs(c, "anyRef", 0, 1));
        StatsCase("object.k3", c => Refs(c, "anyRef", 0, 0.5f, 1));
        StatsCase("object.two", c => { Refs(c, "anyRef", 0, 1); Refs(c, "clipRef", 0, 1); });
        StatsCase("object.withFloat", c => { Refs(c, "anyRef", 0, 0.5f); Float(c, "f", Keys(0, 0, 1, 1)); });
        StatsCase("object.withConstant", c => { Refs(c, "anyRef", 0, 1); Float(c, "f", Keys(0, 3)); });
        StatsCase("euler.y", c => Moved(c, "Child", "localEulerAnglesRaw.y", Keys(0, 0, 1.5f, 180)));
        StatsCase("euler.constant", c => Moved(c, "Child", "localEulerAnglesRaw.y", Keys(0, 20, 1.5f, 20)));
        StatsCase("position.x", c => Moved(c, "Child", "m_LocalPosition.x", Keys(0, 0, 1, 1)));
        StatsCase("position.keysDiffer", c => { Moved(c, "Child", "m_LocalPosition.x", Keys(0, 0, 1, 1)); Moved(c, "Child", "m_LocalPosition.y", Keys(0, 0, 0.5f, 2, 1, 1)); });
        StatsCase("position.timesDiffer", c => { Moved(c, "Child", "m_LocalPosition.x", Keys(0, 0, 1, 1)); Moved(c, "Child", "m_LocalPosition.y", Keys(0.25f, 0, 0.75f, 1)); });
        StatsCase("position.oneConstant", c => { Moved(c, "Child", "m_LocalPosition.x", Keys(0, 2, 1, 2)); Moved(c, "Child", "m_LocalPosition.y", Keys(0, 0, 1, 1)); });
        StatsCase("position.twoPaths", c => { Moved(c, "Child", "m_LocalPosition.x", Keys(0, 0, 1, 1)); Moved(c, "Child2", "m_LocalPosition.x", Keys(0, 0, 1, 1)); });
        StatsCase("rotation", c => { foreach (var axis in "xyzw") Moved(c, "Child", "m_LocalRotation." + axis, Keys(0, 0, 1, 0.5f)); });
        StatsCase("scale.x", c => Moved(c, "Child", "m_LocalScale.x", Keys(0, 1, 1, 2)));
        StatsCase("active", c => AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("Child2", typeof(GameObject), "m_IsActive"), Keys(0, 1, 0.5f, 0, 1, 1)));
        StatsCase("root", c => AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.z"), Keys(0, 0, 1, 2)));
        StatsCase("event", c => { Float(c, "f", Keys(0, 0, 1, 1)); AnimationUtility.SetAnimationEvents(c, new[] { new AnimationEvent { time = 0.5f, functionName = "Hit" } }); });
        StatsCase("loop", c =>
        {
            Float(c, "f", Keys(0, 0, 1, 1));
            var settings = AnimationUtility.GetAnimationClipSettings(c);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(c, settings);
        });
        StatsCase("legacy", c => { c.legacy = true; Float(c, "f", Keys(0, 0, 1, 1)); });
        StatsCase("legacyAfter", c => { Float(c, "f", Keys(0, 0, 1, 1)); c.legacy = true; });

        // A clip only AnimationClip.SetCurve wrote, and what makes its stats count the curve.
        void Written(AnimationClip c) => c.SetCurve("", typeof(TypedTarget), "f", Keys(0, 0, 1, 1));
        StatsCase("setCurve", Written);
        StatsCase("setCurve.length", Written, c => { _ = c.length; });
        StatsCase("setCurve.events", Written, c => AnimationUtility.SetAnimationEvents(c, new AnimationEvent[0]));
        StatsCase("setCurve.settings", Written, c => AnimationUtility.SetAnimationClipSettings(c, AnimationUtility.GetAnimationClipSettings(c)));
        StatsCase("setCurve.editorCurve", Written, c => Float(c, "i", Keys(0, 0, 1, 1)));
        StatsCase("setCurve.objectCurve", Written, c => Refs(c, "anyRef", 0, 1));
        StatsCase("setCurve.bindings", Written, c => AnimationUtility.GetCurveBindings(c));
        StatsCase("setCurve.sampled", Written, c =>
        {
            var go = TypedRig("StatsSampled");
            c.SampleAnimation(go, 0.5f);
            Object.Destroy(go);
        });
        StatsCase("setCurve.played", Written, c =>
        {
            var go = TypedRig("StatsPlayed");
            var controller = new AnimatorController { name = "StatsController" };
            controller.AddLayer("Base Layer");
            controller.AddMotion(c);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.Update(0f);
            Object.Destroy(go);
        });
        StatsCase("editorThenSetCurve", c => { Float(c, "i", Keys(0, 0, 1, 1)); Written(c); });
        StatsCase("cleared", c => { Float(c, "f", Keys(0, 0, 1, 1)); c.ClearCurves(); });
        StatsCase("clearedThenEditor", c => { Float(c, "f", Keys(0, 0, 1, 1)); c.ClearCurves(); Float(c, "i", Keys(0, 0, 1, 1)); });
        // Copies of a clip the Editor's functions wrote and of one only SetCurve wrote; clips read from files.
        Try("stats.copy", () =>
        {
            var c = new AnimationClip { name = "copied", frameRate = 60 };
            Float(c, "f", Keys(0, 0, 1, 1));
            var copy = Object.Instantiate(c);
            Put("stats.copy", StatsOf(copy));
            var w = new AnimationClip { name = "copiedWritten", frameRate = 60 };
            Written(w);
            var copyWritten = Object.Instantiate(w);
            Put("stats.copyOfSetCurve", StatsOf(copyWritten));
            foreach (var o in new[] { c, copy, w, copyWritten }) Object.Destroy(o);
        });
        Put("stats.asset.legacy", StatsOf(Clip("Old")));
        Try("stats.asset.copy", () =>
        {
            var copy = Object.Instantiate(Clip("Walk"));
            Put("stats.asset.copy", StatsOf(copy));
            Object.Destroy(copy);
        });
        Logs("stats");
    }

    static GameObject TypedRig(string name)
    {
        var go = new GameObject(name);
        go.AddComponent<TypedTarget>();
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>().sharedMaterial = null;
        return go;
    }

    static string TypedValues(GameObject go)
    {
        var t = go.GetComponent<TypedTarget>();
        return $"f={F(t.f)} i={t.i} b={t.b} mode={(int)t.mode} mode2={(int)t.mode2} mode3={(int)t.mode3} counted={t.counted} color={F(t.color.r)},{F(t.color.g)} offset={V(t.offset)}"
            + $" any={N(t.anyRef)} clip={N(t.clipRef)} motion={N(t.motion)} material={N(t.material)} texture={N(t.texture)} texture2D={N(t.texture2D)} so={N(t.so)} data={N(t.data)}"
            + $" gameObject={N(t.gameObjectRef)} transform={N(t.transformRef)} component={N(t.component)} behaviour={N(t.behaviour)} script={N(t.script)} probe={N(t.probe)}"
            + $" mesh={N(t.mesh)} shader={N(t.shader)} slot={N(go.GetComponent<MeshRenderer>().sharedMaterial)}";
    }

    /// <summary>One clip with float curves on a float, an int, a bool and an enum, a discrete
    /// curve on an enum and object curves on fields of several types and on a renderer's material
    /// slot, applied by an Animator, AnimationMode, AnimationClip.SampleAnimation and a legacy
    /// Animation.</summary>
    static void TypedCurves()
    {
        var walk = Clip("Walk");
        var run = Clip("Run");
        var m1 = new Material(Shader.Find("Sprites/Default")) { name = "M1" };
        var m2 = new Material(Shader.Find("Sprites/Default")) { name = "M2" };
        var t1 = new Texture2D(2, 2) { name = "T1" };
        var t2 = new Texture2D(2, 2) { name = "T2" };
        var d1 = ScriptableObject.CreateInstance<ProbeData>();
        d1.name = "D1";
        var d2 = ScriptableObject.CreateInstance<ProbeData>();
        d2.name = "D2";
        // Scene objects: A holds a Light and a ProbeTarget, B a ProbeTarget.
        var a = new GameObject("A");
        var light = a.AddComponent<Light>();
        var probeA = a.AddComponent<ProbeTarget>();
        var b = new GameObject("B");
        var probeB = b.AddComponent<ProbeTarget>();
        var mesh1 = new Mesh { name = "Mesh1" };
        var mesh2 = new Mesh { name = "Mesh2" };
        var shader1 = Shader.Find("Sprites/Default");
        var shader2 = Shader.Find("UI/Default");

        AnimationClip Make(bool legacy)
        {
            var c = new AnimationClip { name = legacy ? "TypedLegacy" : "Typed", frameRate = 60, legacy = legacy };
            void Ref(Type type, string attribute, Object a, Object b) => AnimationUtility.SetObjectReferenceCurve(c, EditorCurveBinding.PPtrCurve("", type, attribute),
                new[] { new ObjectReferenceKeyframe { time = 0, value = a }, new ObjectReferenceKeyframe { time = 1, value = b } });
            Ref(typeof(TypedTarget), "anyRef", walk, run);
            Ref(typeof(TypedTarget), "clipRef", walk, run);
            Ref(typeof(TypedTarget), "motion", walk, run);
            Ref(typeof(TypedTarget), "material", m1, m2);
            Ref(typeof(TypedTarget), "texture", t1, t2);
            Ref(typeof(TypedTarget), "texture2D", t1, t2);
            Ref(typeof(TypedTarget), "so", d1, d2);
            Ref(typeof(TypedTarget), "data", d1, d2);
            Ref(typeof(TypedTarget), "gameObjectRef", a, b);
            Ref(typeof(TypedTarget), "transformRef", a.transform, b.transform);
            Ref(typeof(TypedTarget), "component", a.transform, probeB);
            Ref(typeof(TypedTarget), "behaviour", light, probeB);
            Ref(typeof(TypedTarget), "script", probeA, probeB);
            Ref(typeof(TypedTarget), "probe", probeA, probeB);
            Ref(typeof(TypedTarget), "mesh", mesh1, mesh2);
            Ref(typeof(TypedTarget), "shader", shader1, shader2);
            Ref(typeof(MeshRenderer), "m_Materials.Array.data[0]", m1, m2);
            Float(c, "f", Keys(0, 10, 2, 30));
            Float(c, "i", Keys(0, 10, 2, 30));
            Float(c, "b", Keys(0, 1, 2, 1));
            Float(c, "mode", Keys(0, 1, 2, 1));
            Float(c, "color.r", Keys(0, 0, 2, 1));
            Float(c, "offset.x", Keys(0, 0, 2, 4));
            AnimationUtility.SetEditorCurve(c, EditorCurveBinding.DiscreteCurve("", typeof(TypedTarget), "mode2"), Keys(0, 0, 1, 1));
            // Discrete values as integers' bits: an enum from 0 to 2, an int from 3 to 7.
            AnimationUtility.SetEditorCurve(c, EditorCurveBinding.DiscreteCurve("", typeof(TypedTarget), "mode3"), Keys(0, BitConverter.Int32BitsToSingle(0), 1, BitConverter.Int32BitsToSingle(2)));
            AnimationUtility.SetEditorCurve(c, EditorCurveBinding.DiscreteCurve("", typeof(TypedTarget), "counted"), Keys(0, BitConverter.Int32BitsToSingle(3), 1, BitConverter.Int32BitsToSingle(7)));
            return c;
        }
        var clip = Make(false);
        var legacyClip = Make(true);
        Put("typed.stats", StatsOf(clip));
        Put("typed.bindings", string.Join(";", AnimationUtility.GetCurveBindings(clip).Select(Binding)) + " / " + string.Join(";", AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(Binding)));
        var controller = new AnimatorController { name = "TypedController" };
        controller.AddLayer("Base Layer");
        controller.AddMotion(clip);

        foreach (float time in new[] { 0.5f, 1.5f })
        {
            string at = time == 0.5f ? "early" : "late";
            var go = TypedRig("TypedAnimator");
            Try("typed.animator." + at, () =>
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.Update(0f);
                animator.Update(time);
                Put("typed.animator." + at, TypedValues(go));
            });
            Object.Destroy(go);

            go = TypedRig("TypedMode");
            Try("typed.mode." + at, () =>
            {
                var target = go.GetComponent<TypedTarget>();
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(go, clip, time);
                AnimationMode.EndSampling();
                Put("typed.mode." + at, TypedValues(go));
                Put("typed.mode." + at + ".animated", string.Join(" ", new[] { "f", "i", "mode", "mode2", "counted", "color.r", "color.g", "color", "offset.x", "offset.y", "offset", "anyRef", "clipRef", "material", "label" }
                    .Select(p => p + "=" + AnimationMode.IsPropertyAnimated(target, p))) + " slot=" + AnimationMode.IsPropertyAnimated(go.GetComponent<MeshRenderer>(), "m_Materials.Array.data[0]"));
                AnimationMode.StopAnimationMode();
                Put("typed.mode." + at + ".stopped", TypedValues(go));
            });
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            Object.Destroy(go);

            go = TypedRig("TypedSample");
            Try("typed.sample." + at, () =>
            {
                clip.SampleAnimation(go, time);
                Put("typed.sample." + at, TypedValues(go));
            });
            Object.Destroy(go);

            go = TypedRig("TypedLegacySample");
            Try("typed.legacySample." + at, () =>
            {
                legacyClip.SampleAnimation(go, time);
                Put("typed.legacySample." + at, TypedValues(go));
            });
            Object.Destroy(go);

            go = TypedRig("TypedLegacy");
            Try("typed.legacy." + at, () =>
            {
                var animation = go.AddComponent<Animation>();
                animation.AddClip(legacyClip, "TypedLegacy");
                animation.Play("TypedLegacy");
                animation["TypedLegacy"].time = time;
                animation.Sample();
                Put("typed.legacy." + at, TypedValues(go));
            });
            Object.Destroy(go);
        }
        Object.Destroy(a);
        Object.Destroy(b);
        Logs("typed");
    }

    static string Overrides(GameObject instance) => string.Join(",", (PrefabUtility.GetPropertyModifications(instance) ?? new PropertyModification[0])
        .Where(m => m.target is TypedTarget).Select(m => m.propertyPath + "=" + m.value + (m.objectReference != null ? "@" + m.objectReference.name : "")).OrderBy(s => s, StringComparer.Ordinal));

    static string Plain(TypedTarget t) => $"f={F(t.f)} i={t.i} b={t.b} label={t.label} mode={(int)t.mode} color={F(t.color.r)},{F(t.color.g)},{F(t.color.b)} material={N(t.material)}";

    /// <summary>AnimationMode.AddPropertyModification of numbers, a string, an enum, a struct's
    /// member and an object, on a plain object and on prefab instances with and without overrides,
    /// keeping the prefab override or not; which properties then count as animated.</summary>
    static void Modifications()
    {
        var material = new Material(Shader.Find("Sprites/Default")) { name = "M3" };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TypedPrefab);
        foreach (var hasOverride in new[] { false, true })
            foreach (var keep in new[] { false, true })
            {
                string key = "modify." + (hasOverride ? "overridden" : "inherited") + (keep ? ".keep" : ".drop");
                GameObject instance = null;
                Try(key, () =>
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    var t = instance.GetComponent<TypedTarget>();
                    if (hasOverride)
                    {
                        t.f = 5;
                        t.i = 6;
                        t.label = "over";
                        t.material = material;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                    }
                    Put(key + ".before", Plain(t) + " overrides=[" + Overrides(instance) + "]");
                    AnimationMode.StartAnimationMode();
                    void Add(string path, string value, Object reference = null, bool pptr = false)
                    {
                        var binding = pptr ? EditorCurveBinding.PPtrCurve("", typeof(TypedTarget), path) : EditorCurveBinding.FloatCurve("", typeof(TypedTarget), path);
                        Try(key + ".add." + path, () => AnimationMode.AddPropertyModification(binding, new PropertyModification { target = t, propertyPath = path, value = value, objectReference = reference }, keep));
                    }
                    Add("f", t.f.ToString(CultureInfo.InvariantCulture));
                    Add("i", t.i.ToString(CultureInfo.InvariantCulture));
                    Add("b", "0");
                    Add("label", t.label);
                    Add("color.r", "1");
                    Add("mode", "2");
                    Add("material", "", t.material, true);
                    Put(key + ".animated", string.Join(" ", new[] { "f", "i", "b", "label", "color.r", "color.g", "color", "mode", "material" }.Select(p => p + "=" + AnimationMode.IsPropertyAnimated(t, p))));
                    t.f = 9;
                    t.i = 9;
                    t.b = true;
                    t.label = "changed";
                    t.color = Color.blue;
                    t.mode = ProbeMode.First;
                    t.material = null;
                    AnimationMode.StopAnimationMode();
                    Put(key + ".after", Plain(t) + " overrides=[" + Overrides(instance) + "]");
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                    Put(key + ".recorded", "overrides=[" + Overrides(instance) + "]");
                });
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                if (instance != null) Object.Destroy(instance);
            }

        // Values that differ from the object's when added, text that is no number, a property
        // the object does not have, and a second modification of one property.
        var go = TypedRig("ModifyPlain");
        Try("modify.plain", () =>
        {
            var t = go.GetComponent<TypedTarget>();
            AnimationMode.StartAnimationMode();
            void Add(Object target, Type type, string path, string value) =>
                AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", type, path), new PropertyModification { target = target, propertyPath = path, value = value }, true);
            Add(t, typeof(TypedTarget), "f", "7");
            Add(t, typeof(TypedTarget), "i", "x");
            Add(t, typeof(TypedTarget), "label", "was");
            Add(t, typeof(TypedTarget), "nothing", "1");
            Add(t, typeof(TypedTarget), "f", "8");
            Add(go.transform, typeof(Transform), "m_LocalPosition.x", "4");
            Add(t, typeof(TypedTarget), "offset.x", "6");
            Put("modify.plain.animated", string.Join(" ", new[] { "f", "i", "label", "nothing", "offset.x", "offset.y", "offset" }.Select(p => p + "=" + AnimationMode.IsPropertyAnimated(t, p)))
                + " " + string.Join(" ", new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition" }.Select(p => p + "=" + AnimationMode.IsPropertyAnimated(go.transform, p))));
            t.f = 1;
            t.i = 1;
            t.label = "changed";
            t.offset = new Vector3(7, 8, 9);
            go.transform.localPosition = new Vector3(1, 2, 3);
            AnimationMode.StopAnimationMode();
            Put("modify.plain.after", Plain(t) + " offset=" + V(t.offset) + " position=" + V(go.transform.localPosition));
        });
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        Object.Destroy(go);

        // An engine component's struct member; values given as empty text, no text and words.
        go = TypedRig("ModifyText");
        Try("modify.text", () =>
        {
            var t = go.GetComponent<TypedTarget>();
            var light = go.AddComponent<Light>();
            light.color = new Color(0.25f, 0.5f, 0.75f, 1f);
            AnimationMode.StartAnimationMode();
            void Add(Object target, Type type, string path, string value) =>
                AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", type, path), new PropertyModification { target = target, propertyPath = path, value = value }, true);
            Add(light, typeof(Light), "m_Color.r", "0.5");
            Add(t, typeof(TypedTarget), "f", "");
            Add(t, typeof(TypedTarget), "i", null);
            Add(t, typeof(TypedTarget), "b", "true");
            Add(t, typeof(TypedTarget), "mode", "First");
            Add(t, typeof(TypedTarget), "label", null);
            Put("modify.text.animated", string.Join(" ", new[] { "m_Color.r", "m_Color.g", "m_Color" }.Select(p => p + "=" + AnimationMode.IsPropertyAnimated(light, p)))
                + " " + string.Join(" ", new[] { "f", "i", "b", "mode", "label" }.Select(p => p + "=" + AnimationMode.IsPropertyAnimated(t, p))));
            t.f = 9;
            t.i = 9;
            t.label = "changed";
            light.color = Color.black;
            AnimationMode.StopAnimationMode();
            Put("modify.text.after", Plain(t) + " light=" + F(light.color.r) + "," + F(light.color.g) + "," + F(light.color.b));
        });
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        Object.Destroy(go);
        Logs("modify");
    }

    /// <summary>Names with a dot given to states, state machines, layers and parameters.</summary>
    static void DottedNames()
    {
        var c = new AnimatorController { name = "NameController" };
        c.AddLayer("Base Layer");
        var machine = c.layers[0].stateMachine;
        void Case(string key, Func<string> name) => Try("names." + key, () =>
        {
            Put("names." + key, name());
            Logs("names." + key);
        });
        Case("state", () => machine.AddState("a.b").name);
        Case("renamed", () =>
        {
            var state = machine.AddState("plain");
            state.name = "c.d";
            return state.name;
        });
        Case("motion", () => c.AddMotion(new AnimationClip { name = "e.f" }).name);
        Case("machine", () => machine.AddStateMachine("g.h").name);
        Case("unique", () => machine.MakeUniqueStateName("a.b") + "|" + machine.MakeUniqueStateName("i.j") + "|" + machine.MakeUniqueStateMachineName("k.l"));
        Case("layer", () =>
        {
            c.AddLayer("m.n");
            return c.layers[1].name + "|" + c.layers[1].stateMachine.name;
        });
        Case("parameter", () =>
        {
            c.AddParameter("o.p", AnimatorControllerParameterType.Float);
            return c.parameters[0].name;
        });
        Case("states", () => string.Join(",", machine.states.Select(s => s.state.name)) + " / " + string.Join(",", machine.stateMachines.Select(m => m.stateMachine.name)));
    }
}
