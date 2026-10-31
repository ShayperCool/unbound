using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates the clips, masks and controllers of the additive layer probe and records it.</summary>
public static class ProbeRunner
{
    const string Dir = "Assets/Resources/";

    /// <summary>Piecewise linear keys: (time, value) pairs.</summary>
    static AnimationCurve Linear(params float[] pairs)
    {
        int n = pairs.Length / 2;
        var keys = new Keyframe[n];
        for (int i = 0; i < n; i++)
        {
            float t = pairs[2 * i], v = pairs[2 * i + 1];
            float inSlope = i > 0 ? (v - pairs[2 * i - 1]) / (t - pairs[2 * i - 2]) : 0f;
            float outSlope = i < n - 1 ? (pairs[2 * i + 3] - v) / (pairs[2 * i + 2] - t) : 0f;
            if (i == 0) inSlope = outSlope;
            if (i == n - 1) outSlope = inSlope;
            keys[i] = new Keyframe(t, v, inSlope, outSlope);
        }
        return new AnimationCurve(keys);
    }

    static void Float(AnimationClip clip, string path, Type type, string property, params float[] pairs) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), Linear(pairs));

    /// <summary>Vector keys: times, then three values per time.</summary>
    static void Vector(AnimationClip clip, string path, string property, float[] times, params Vector3[] values)
    {
        for (int c = 0; c < 3; c++)
        {
            var pairs = new float[times.Length * 2];
            for (int i = 0; i < times.Length; i++) { pairs[2 * i] = times[i]; pairs[2 * i + 1] = values[i][c]; }
            Float(clip, path, typeof(Transform), property + "." + "xyz"[c], pairs);
        }
    }

    /// <summary>Quaternion curves keyed from Euler angles.</summary>
    static void Rotation(AnimationClip clip, string path, float[] times, params Vector3[] euler)
    {
        var q = Array.ConvertAll(euler, e => Quaternion.Euler(e));
        for (int c = 0; c < 4; c++)
        {
            var pairs = new float[times.Length * 2];
            for (int i = 0; i < times.Length; i++)
            {
                // Keys in one hemisphere, as the Animation window keys them.
                var k = q[i];
                if (i > 0 && Quaternion.Dot(q[i - 1], k) < 0f) k = new Quaternion(-k.x, -k.y, -k.z, -k.w);
                q[i] = k;
                pairs[2 * i] = times[i]; pairs[2 * i + 1] = k[c];
            }
            Float(clip, path, typeof(Transform), "m_LocalRotation." + "xyzw"[c], pairs);
        }
    }

    static AnimationClip Save(AnimationClip clip, bool loop, AnimationClip reference = null, float referenceTime = 0f, bool hasReference = false,
        float start = -1f)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        if (start >= 0f) settings.startTime = start;
        settings.hasAdditiveReferencePose = hasReference;
        settings.additiveReferencePoseClip = reference;
        settings.additiveReferencePoseTime = referenceTime;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, Dir + clip.name + ".anim");
        return clip;
    }

    static readonly float[] T2 = { 0f, 1f };
    static readonly float[] T3 = { 0f, 0.5f, 1f };

    static AnimationClip AdditiveCurves(string name)
    {
        var clip = new AnimationClip { name = name, frameRate = 60f };
        float[] t2 = T2, t3 = T3;
        Vector(clip, "A", "m_LocalPosition", t2, new Vector3(0.5f, 0.2f, 0f), new Vector3(1.5f, 0.2f, 0.5f));
        Rotation(clip, "A", t3, new Vector3(10, 0, 0), new Vector3(10, 45, 20), new Vector3(10, 90, 45));
        Vector(clip, "A", "m_LocalScale", t2, new Vector3(2f, 1f, 1f), new Vector3(3f, 2f, 1f));
        Vector(clip, "A/B", "localEulerAnglesRaw", t2, new Vector3(0, 0, 10), new Vector3(0, 0, 130));
        Vector(clip, "C", "m_LocalPosition", t2, new Vector3(0, 0, 0), new Vector3(0, 1, 0));
        Float(clip, "", typeof(AdditiveTarget), "f", 0f, 0.25f, 1f, 1.25f);
        Float(clip, "", typeof(AdditiveTarget), "g", 0f, 5f, 1f, 5f);
        return clip;
    }

    public static void Create()
    {
        var baseClip = new AnimationClip { name = "Base", frameRate = 60f };
        Vector(baseClip, "A", "m_LocalPosition", T2, new Vector3(0, 0, 0), new Vector3(2, 1, 0));
        Rotation(baseClip, "A", T3, new Vector3(0, 30, 0), new Vector3(20, 60, 0), new Vector3(40, 30, 10));
        Vector(baseClip, "A", "m_LocalScale", T2, new Vector3(1, 1, 1), new Vector3(2, 1, 1.5f));
        Vector(baseClip, "A/B", "m_LocalPosition", T2, new Vector3(0, 1, 0), new Vector3(0, 2, 0));
        Vector(baseClip, "A/B", "localEulerAnglesRaw", T2, new Vector3(0, 0, 0), new Vector3(30, 0, 0));
        Vector(baseClip, "D", "m_LocalPosition", T2, new Vector3(1, 0, 0), new Vector3(3, 0, 0));
        Float(baseClip, "", typeof(AdditiveTarget), "f", 0f, 0f, 1f, 1f);
        Float(baseClip, "", typeof(AdditiveTarget), "g", 0f, 10f, 1f, 20f);
        Save(baseClip, true);

        var base2 = new AnimationClip { name = "Base2", frameRate = 60f };
        Vector(base2, "A", "m_LocalPosition", T2, new Vector3(0, -1, 0), new Vector3(0, -2, 1));
        Rotation(base2, "A", T2, new Vector3(0, -30, 0), new Vector3(0, -60, 20));
        Float(base2, "", typeof(AdditiveTarget), "f", 0f, -1f, 1f, -2f);
        Save(base2, true);

        var main = Save(AdditiveCurves("AddMain"), true);

        var partial = new AnimationClip { name = "AddPartial", frameRate = 60f };
        Float(partial, "A", typeof(Transform), "m_LocalPosition.x", 0f, 0f, 1f, -1f);
        Rotation(partial, "A", T2, new Vector3(0, 0, 0), new Vector3(0, 0, 60));
        Float(partial, "", typeof(AdditiveTarget), "f", 0f, 1f, 1f, 0f);
        Save(partial, true);

        var big = new AnimationClip { name = "AddBig", frameRate = 60f };
        Vector(big, "A", "m_LocalPosition", T2, new Vector3(0, 0, 0), new Vector3(1, 0, 0));
        Rotation(big, "A", T3, new Vector3(0, 0, 0), new Vector3(0, 85, 0), new Vector3(0, 170, 0));
        Vector(big, "A", "m_LocalScale", T2, new Vector3(1, 1, 1), new Vector3(0.5f, 2f, 1f));
        Save(big, true);

        var pose = new AnimationClip { name = "RefPose", frameRate = 60f };
        Vector(pose, "A", "m_LocalPosition", T2, new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        Rotation(pose, "A", T2, new Vector3(0, 0, 0), new Vector3(0, 90, 0));
        Vector(pose, "A", "m_LocalScale", T2, new Vector3(1, 1, 1), new Vector3(2, 2, 2));
        Vector(pose, "A/B", "localEulerAnglesRaw", T2, new Vector3(0, 0, 0), new Vector3(0, 0, 90));
        Float(pose, "", typeof(AdditiveTarget), "f", 0f, 0f, 1f, 2f);
        Save(pose, false);

        var refClip = Save(AdditiveCurves("AddRef"), true, pose, 0.5f, true);
        var refSelf = Save(AdditiveCurves("AddRefSelf"), true, null, 0.25f, true);
        // The clip's range starts at 0.25 s of its curves.
        var start = Save(AdditiveCurves("AddStart"), true, start: 0.25f);

        // A reference without rotation, scale and float curves.
        var partialPose = new AnimationClip { name = "RefPartialPose", frameRate = 60f };
        Vector(partialPose, "A", "m_LocalPosition", T2, new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        Save(partialPose, false);
        var refPartial = Save(AdditiveCurves("AddRefPartial"), true, partialPose, 0.5f, true);
        // A reference animating only the child's position: no curve of A, B without rotation.
        var childPose = new AnimationClip { name = "RefChildPose", frameRate = 60f };
        Vector(childPose, "A/B", "m_LocalPosition", T2, new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        Save(childPose, false);
        var refChild = Save(AdditiveCurves("AddRefChild"), true, childPose, 0.5f, true);
        var once = Save(AdditiveCurves("AddOnce"), false);
        {
            var probe = new AnimationClip();
            AnimationUtility.SetAdditiveReferencePose(probe, null, 0.3f);
            var settings = AnimationUtility.GetAnimationClipSettings(probe);
            Debug.Log("SetAdditiveReferencePose(null): has " + settings.hasAdditiveReferencePose + " time " + settings.additiveReferencePoseTime);
            foreach (var reference in new[] { partialPose, main, pose })
            {
                probe = AdditiveCurves("Probe");
                AnimationUtility.SetAdditiveReferencePose(probe, reference, 0.3f);
                settings = AnimationUtility.GetAnimationClipSettings(probe);
                Debug.Log("SetAdditiveReferencePose(" + reference.name + "): has " + settings.hasAdditiveReferencePose
                    + " clip " + settings.additiveReferencePoseClip + " time " + settings.additiveReferencePoseTime);
            }
        }

        var over = new AnimationClip { name = "Over", frameRate = 60f };
        Rotation(over, "A", T2, new Vector3(0, -45, 0), new Vector3(0, -45, 0));
        Vector(over, "A/B", "m_LocalPosition", T2, new Vector3(1, 1, 1), new Vector3(1, 1, 1));
        Float(over, "", typeof(AdditiveTarget), "f", 0f, 3f, 1f, 3f);
        Save(over, true);

        var maskA = Mask("MaskA", ("", true), ("A", true), ("A/B", false), ("C", false), ("D", true));
        var maskNoRoot = Mask("MaskNoRoot", ("", false), ("A", true), ("A/B", true), ("C", true), ("D", true));
        var maskSparse = Mask("MaskSparse", ("A", true));

        Controller("Base", (baseClip, false));
        Controller("Basic", (baseClip, false), (main, true));
        Controller("Big", (baseClip, false), (big, true));
        {
            var c = Controller("DefaultWeight", (baseClip, false), (main, true));
            var layers = c.layers; layers[1].defaultWeight = 1.5f; c.layers = layers;
        }
        Controller("NoBase", (null, false), (main, true));
        {
            var c = Controller("NoBaseState", (null, false), (main, true));
            c.layers[0].stateMachine.defaultState = c.layers[0].stateMachine.AddState("Empty");
        }
        Controller("RefClip", (baseClip, false), (refClip, true));
        Controller("RefPartial", (baseClip, false), (refPartial, true));
        Controller("RefChild", (baseClip, false), (refChild, true));
        Controller("Once", (baseClip, false), (once, true));
        Controller("RefSelf", (baseClip, false), (refSelf, true));
        Controller("Start", (baseClip, false), (start, true));
        foreach (var mask in new[] { maskA, maskNoRoot, maskSparse })
        {
            var c = Controller("Mask" + mask.name.Substring(4), (baseClip, false), (main, true));
            var layers = c.layers; layers[1].avatarMask = mask; c.layers = layers;
        }
        foreach (var mask in new[] { maskNoRoot, maskSparse })
        {
            var c = Controller("Over" + mask.name, (baseClip, false), (over, false));
            var layers = c.layers; layers[1].avatarMask = mask; c.layers = layers;
        }
        {
            var c = Controller("Stack", (baseClip, false), (partial, false), (main, true), (over, false), (partial, true), (refClip, true));
            var layers = c.layers;
            layers[1].defaultWeight = 0.5f;
            layers[2].defaultWeight = 0.7f;
            layers[3].defaultWeight = 0.6f; layers[3].avatarMask = maskA;
            layers[5].defaultWeight = 0.5f;
            c.layers = layers;
        }
        Controller("OverAbove", (baseClip, false), (main, true), (over, false));
        {
            var c = Controller("BaseAdditive", (main, true));
            var layers = c.layers; layers[0].blendingMode = AnimatorLayerBlendingMode.Additive; c.layers = layers;
        }
        Sync();
        foreach (bool wd in new[] { true, false })
        {
            Switch("Wd" + (wd ? "On" : "Off"), main, partial, wd);
            Switch("Empty" + (wd ? "On" : "Off"), main, null, wd);
            // The same switches in an override layer at half weight.
            Switch("OverWd" + (wd ? "On" : "Off"), main, partial, wd, additive: false);
            Switch("OverEmpty" + (wd ? "On" : "Off"), main, null, wd, additive: false);
        }
        Tree(main, partial);
        EmptyFirst("EmptyFirst", main, additive: true);
        EmptyFirst("OverEmptyFirst", main, additive: false);
        CreateMixed();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static AvatarMask Mask(string name, params (string Path, bool Active)[] transforms)
    {
        var mask = new AvatarMask { name = name };
        mask.transformCount = transforms.Length;
        for (int i = 0; i < transforms.Length; i++)
        {
            mask.SetTransformPath(i, transforms[i].Path);
            mask.SetTransformActive(i, transforms[i].Active);
        }
        AssetDatabase.CreateAsset(mask, Dir + name + ".mask");
        return mask;
    }

    /// <summary>One layer per motion (null: a layer without states); additive layers at weight 1.</summary>
    static AnimatorController Controller(string name, params (AnimationClip Motion, bool Additive)[] motions)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + name + ".controller");
        for (int i = 1; i < motions.Length; i++) c.AddLayer("L" + i);
        var layers = c.layers;
        for (int i = 0; i < motions.Length; i++)
        {
            layers[i].defaultWeight = 1f;
            if (i > 0 && motions[i].Additive) layers[i].blendingMode = AnimatorLayerBlendingMode.Additive;
            if (motions[i].Motion == null) continue;
            var state = layers[i].stateMachine.AddState(motions[i].Motion.name);
            state.motion = motions[i].Motion;
            layers[i].stateMachine.defaultState = state;
        }
        c.layers = layers;
        return c;
    }

    /// <summary>An additive layer synced to the base layer, which changes state on "go".</summary>
    static void Sync()
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + "Sync.controller");
        c.AddParameter("go", AnimatorControllerParameterType.Bool);
        var machine = c.layers[0].stateMachine;
        var s1 = machine.AddState("S1"); s1.motion = Load("Base");
        var s2 = machine.AddState("S2"); s2.motion = Load("Base2");
        machine.defaultState = s1;
        var t = s1.AddTransition(s2);
        t.hasExitTime = false; t.hasFixedDuration = true; t.duration = 0.25f;
        t.AddCondition(AnimatorConditionMode.If, 0f, "go");
        c.AddLayer("Sync");
        var layers = c.layers;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
        layers[1].syncedLayerIndex = 0;
        layers[1].SetOverrideMotion(s1, Load("AddMain"));
        layers[1].SetOverrideMotion(s2, Load("AddPartial"));
        c.layers = layers;
    }

    /// <summary>Base clip; an additive layer going from P to Q on "q" and back on not "q".</summary>
    static void Switch(string name, AnimationClip p, AnimationClip q, bool writeDefaults, bool additive = true)
    {
        var c = Controller(name, (Load("Base"), false), (p, additive));
        c.AddParameter("q", AnimatorControllerParameterType.Bool);
        var machine = c.layers[1].stateMachine;
        var sp = machine.defaultState;
        var sq = machine.AddState("Q");
        sq.motion = q;
        sp.writeDefaultValues = writeDefaults;
        sq.writeDefaultValues = writeDefaults;
        var to = sp.AddTransition(sq);
        to.hasExitTime = false; to.hasFixedDuration = true; to.duration = 0.25f;
        to.AddCondition(AnimatorConditionMode.If, 0f, "q");
        var back = sq.AddTransition(sp);
        back.hasExitTime = false; back.hasFixedDuration = true; back.duration = 0.25f;
        back.AddCondition(AnimatorConditionMode.IfNot, 0f, "q");
    }

    /// <summary>A layer starting in an empty state without write defaults; P on "q".</summary>
    static void EmptyFirst(string name, AnimationClip p, bool additive)
    {
        var c = Controller(name, (Load("Base"), false), (null, additive));
        c.AddParameter("q", AnimatorControllerParameterType.Bool);
        var machine = c.layers[1].stateMachine;
        var empty = machine.AddState("E");
        empty.writeDefaultValues = false;
        machine.defaultState = empty;
        var sp = machine.AddState("P");
        sp.motion = p;
        sp.writeDefaultValues = false;
        var to = empty.AddTransition(sp);
        to.hasExitTime = false; to.hasFixedDuration = true; to.duration = 0.25f;
        to.AddCondition(AnimatorConditionMode.If, 0f, "q");
        var back = sp.AddTransition(empty);
        back.hasExitTime = false; back.hasFixedDuration = true; back.duration = 0.25f;
        back.AddCondition(AnimatorConditionMode.IfNot, 0f, "q");
    }

    /// <summary>Transitions between a state with write defaults and one without, both ways, in the
    /// base layer (Base to Base2 or to an empty state, an additive AddMain layer above), an override
    /// and an additive layer (AddMain to AddPartial or to an empty state over the Base clip); to and
    /// from empty states also with the same write defaults. Existing controllers are kept.</summary>
    public static void CreateMixed()
    {
        foreach (var (kind, layer, additive) in new[] { ("Base", 0, false), ("Over", 1, false), ("Add", 1, true) })
        foreach (bool empty in new[] { false, true })
        foreach (var (wdP, wdQ) in empty ? new[] { (true, false), (false, true), (true, true), (false, false) } : new[] { (true, false), (false, true) })
        {
            string name = "Mix" + kind + (empty ? "Empty" : "") + (wdP ? "On" : "Off") + (wdQ ? "On" : "Off");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + name + ".controller") != null) continue;
            var c = layer == 0
                ? Controller(name, (Load("Base"), false), (Load("AddMain"), true))
                : Controller(name, (Load("Base"), false), (Load("AddMain"), additive));
            c.AddParameter("q", AnimatorControllerParameterType.Bool);
            var machine = c.layers[layer].stateMachine;
            var sp = machine.defaultState;
            var sq = machine.AddState("Q");
            if (!empty) sq.motion = Load(layer == 0 ? "Base2" : "AddPartial");
            sp.writeDefaultValues = wdP;
            sq.writeDefaultValues = wdQ;
            var to = sp.AddTransition(sq);
            to.hasExitTime = false; to.hasFixedDuration = true; to.duration = 0.375f;
            to.AddCondition(AnimatorConditionMode.If, 0f, "q");
            var back = sq.AddTransition(sp);
            back.hasExitTime = false; back.hasFixedDuration = true; back.duration = 0.375f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0f, "q");
        }
        AssetDatabase.SaveAssets();
    }

    static void Tree(AnimationClip a, AnimationClip b)
    {
        var c = Controller("Tree", (Load("Base"), false), (null, true));
        c.AddParameter("blend", AnimatorControllerParameterType.Float);
        var state = c.CreateBlendTreeInController("Tree", out var tree, 1);
        tree.blendParameter = "blend";
        tree.useAutomaticThresholds = false;
        tree.AddChild(a, 0f);
        tree.AddChild(b, 1f);
        c.layers[1].stateMachine.defaultState = state;
    }

    static AnimationClip Load(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + name + ".anim");

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
