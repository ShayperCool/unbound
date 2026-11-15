using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> (batchmode) authors the probe's clips and controllers into Assets/Resources. With
/// <c>ANIM_PARTIAL_OUT</c> set, a windowed Editor (or a batchmode one through <see cref="Record"/>)
/// opens the scene, enters Play mode and quits when <see cref="PartialProbe"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("ANIM_PARTIAL_OUT") == null) return;
        if (Application.isBatchMode)
        {
            EditorApplication.update += () =>
            {
                if (EditorApplication.isPlaying && PartialProbe.Finished) EditorApplication.Exit(0);
            };
            return;
        }
        if (SessionState.GetBool("PartialProbe.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("PartialProbe.finished", false))
                EditorApplication.Exit(0);
        };
        EditorApplication.update += Tick;
    }

    static void Begin()
    {
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
            foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
            var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            game.Focus();
            PlayModeWindow.SetPlayModeFocused(true);
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }

    /// <summary>Batchmode capture (<c>-executeMethod ProbeRunner.Record</c> without <c>-quit</c>):
    /// enters Play mode in the probe scene; the Editor exits when the record is written.</summary>
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !PartialProbe.Finished) return;
        SessionState.SetBool("PartialProbe.finished", true);
        EditorApplication.ExitPlaymode();
    }

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

    static void Vector(AnimationClip clip, string path, string property, float[] times, params Vector3[] values)
    {
        for (int c = 0; c < 3; c++)
        {
            var pairs = new float[times.Length * 2];
            for (int i = 0; i < times.Length; i++) { pairs[2 * i] = times[i]; pairs[2 * i + 1] = values[i][c]; }
            Float(clip, path, typeof(Transform), property + "." + "xyz"[c], pairs);
        }
    }

    /// <summary>Quaternion curves keyed from Euler angles, keys in one hemisphere.</summary>
    static void Rotation(AnimationClip clip, string path, float[] times, params Vector3[] euler)
    {
        var q = Array.ConvertAll(euler, e => Quaternion.Euler(e));
        for (int i = 1; i < q.Length; i++)
            if (Quaternion.Dot(q[i - 1], q[i]) < 0f) q[i] = new Quaternion(-q[i].x, -q[i].y, -q[i].z, -q[i].w);
        for (int c = 0; c < 4; c++)
        {
            var pairs = new float[times.Length * 2];
            for (int i = 0; i < times.Length; i++) { pairs[2 * i] = times[i]; pairs[2 * i + 1] = q[i][c]; }
            Float(clip, path, typeof(Transform), "m_LocalRotation." + "xyzw"[c], pairs);
        }
    }

    static readonly float[] T2 = { 0f, 1f };
    static readonly float[] T3 = { 0f, 0.5f, 1f };

    /// <summary>P animates A's position and rotation, B's position, C's rotation, D's activity (off)
    /// and f; Q animates A's x position and scale, D's position and g; R (2 s) A's position and f.
    /// Each is saved as a legacy clip (L prefix) and as a generic one.</summary>
    static AnimationClip P(bool legacy)
    {
        var clip = new AnimationClip { name = (legacy ? "L" : "") + "P", frameRate = 60f, legacy = legacy };
        Vector(clip, "A", "m_LocalPosition", T2, new Vector3(0, 0, 0), new Vector3(2, 1, 0));
        Rotation(clip, "A", T3, new Vector3(0, 30, 0), new Vector3(20, 60, 0), new Vector3(40, 30, 10));
        Vector(clip, "A/B", "m_LocalPosition", T2, new Vector3(0, 1, 0), new Vector3(0, 2, 0));
        Rotation(clip, "C", T2, new Vector3(0, 0, 0), new Vector3(0, 90, 0));
        Float(clip, "D", typeof(GameObject), "m_IsActive", 0f, 0f, 1f, 0f);
        Float(clip, "", typeof(PartialTarget), "f", 0f, 0f, 1f, 1f);
        return Save(clip);
    }

    static AnimationClip Q(bool legacy)
    {
        var clip = new AnimationClip { name = (legacy ? "L" : "") + "Q", frameRate = 60f, legacy = legacy };
        Float(clip, "A", typeof(Transform), "m_LocalPosition.x", 0f, 0f, 1f, -1f);
        Vector(clip, "A", "m_LocalScale", T2, new Vector3(1, 1, 1), new Vector3(2, 1, 1));
        Vector(clip, "D", "m_LocalPosition", T2, new Vector3(1, 0, 0), new Vector3(3, 0, 0));
        Float(clip, "", typeof(PartialTarget), "g", 0f, 0f, 1f, 5f);
        return Save(clip);
    }

    static AnimationClip R(bool legacy)
    {
        var clip = new AnimationClip { name = (legacy ? "L" : "") + "R", frameRate = 60f, legacy = legacy };
        Vector(clip, "A", "m_LocalPosition", new[] { 0f, 2f }, new Vector3(0, -1, 0), new Vector3(0, -2, 1));
        Float(clip, "", typeof(PartialTarget), "f", 0f, -1f, 2f, -2f);
        return Save(clip);
    }

    /// <summary>S (0.5 s, played once): A's x position from 0 to 1 and g from 0 to 10.</summary>
    static AnimationClip S(bool legacy)
    {
        var clip = new AnimationClip { name = (legacy ? "L" : "") + "S", frameRate = 60f, legacy = legacy };
        Float(clip, "A", typeof(Transform), "m_LocalPosition.x", 0f, 0f, 0.5f, 1f);
        Float(clip, "", typeof(PartialTarget), "g", 0f, 0f, 0.5f, 10f);
        return Save(clip, loop: false);
    }

    /// <summary>Bools: values around the conversion thresholds of bool and int properties, one per
    /// 0.125 s key (D's activity, the script's and C's renderer's enabled, the script's b and i).</summary>
    static AnimationClip Bools(bool legacy)
    {
        var clip = new AnimationClip { name = (legacy ? "L" : "") + "Bools", frameRate = 60f, legacy = legacy };
        float[] flags = { -0.002f, -0.0011f, -0.001f, -0.0009f, 0f, 0.0009f, 0.001f, 0.0011f, 0.25f, 0.5f, 1f, 0f };
        float[] ints = { -1.6f, -1.4f, -0.6f, -0.4f, 0.4f, 0.6f, 1.4f, 1.6f, 2.6f, -2.6f, 3.7f, 0f };
        var flagPairs = new float[flags.Length * 2];
        var intPairs = new float[ints.Length * 2];
        for (int k = 0; k < flags.Length; k++)
        {
            flagPairs[2 * k] = intPairs[2 * k] = k * 0.125f;
            flagPairs[2 * k + 1] = flags[k];
            intPairs[2 * k + 1] = ints[k];
        }
        Float(clip, "D", typeof(GameObject), "m_IsActive", flagPairs);
        Float(clip, "", typeof(PartialTarget), "m_Enabled", flagPairs);
        Float(clip, "C", typeof(MeshRenderer), "m_Enabled", flagPairs);
        Float(clip, "", typeof(PartialTarget), "b", flagPairs);
        Float(clip, "", typeof(PartialTarget), "i", intPairs);
        return Save(clip, loop: false);
    }

    /// <summary>LE (legacy, 1 s, loops): A's x position from 0 to 1, and events calling
    /// PartialTarget.Hit at 0.3125, 0.4375, 0.5625 and 0.6875 s (between the 0.125 s frames).</summary>
    static AnimationClip LE()
    {
        var clip = new AnimationClip { name = "LE", frameRate = 60f, legacy = true };
        Float(clip, "A", typeof(Transform), "m_LocalPosition.x", 0f, 0f, 1f, 1f);
        AnimationUtility.SetAnimationEvents(clip, Array.ConvertAll(new[] { 0.3125f, 0.4375f, 0.5625f, 0.6875f },
            t => new AnimationEvent { time = t, functionName = "Hit", stringParameter = "e" + t.ToString("R", System.Globalization.CultureInfo.InvariantCulture) }));
        return Save(clip);
    }

    /// <summary>One layer: P (default) and Q, an Any State transition to Q on "a" (0.25 s fixed,
    /// not to itself).</summary>
    static void Any(string name, AnimationClip p, AnimationClip q)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + name + ".controller");
        c.AddParameter("a", AnimatorControllerParameterType.Bool);
        var machine = c.layers[0].stateMachine;
        var sp = machine.AddState("P");
        sp.motion = p;
        machine.AddState("Q").motion = q;
        machine.defaultState = sp;
        var any = machine.AddAnyStateTransition(machine.states[1].state);
        any.hasExitTime = false; any.hasFixedDuration = true; any.duration = 0.25f; any.canTransitionToSelf = false;
        any.AddCondition(AnimatorConditionMode.If, 0f, "a");
    }

    static AnimationClip Save(AnimationClip clip, bool loop = true)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (clip.legacy) clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
        AssetDatabase.CreateAsset(clip, Dir + clip.name + ".anim");
        return clip;
    }

    public static void Create()
    {
        P(true); Q(true); R(true); S(true); Bools(true); LE();
        var p = P(false);
        var q = Q(false);
        Any("Any", p, q);
        R(false);
        var bools = Bools(false);
        {
            var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + "Bools.controller");
            var state = c.layers[0].stateMachine.AddState("Bools");
            state.motion = bools;
        }
        foreach (bool wd in new[] { true, false })
        {
            Switch("Switch" + (wd ? "On" : "Off"), p, q, wd);
            Tree("Tree" + (wd ? "On" : "Off"), p, q, wd);
        }
        Humans();
        AssetDatabase.SaveAssets();
    }

    /// <summary>Humanoid .anim clips of 1 s that animate only some muscles of HumanA (the humanoid
    /// probe's rig, copied to Assets/Rig/Resources): HM (Spine Front-Back, Left Arm Down-Up, Left
    /// Forearm Stretch, Right Upper Leg Front-Back) and HN (Spine Front-Back, Left Forearm Stretch,
    /// Head Nod Down-Up, Right Arm Down-Up), no body or goal curves; and controllers, each with write
    /// defaults on and off (On/Off suffix): HumanSwitch (HM, to HN on "q" and back on not "q", 0.25 s
    /// fixed), HumanTree (a 1D tree of HM at 0 and HN at 1 on "blend"), HumanFromA (HumanA's own clip
    /// A, to HM on "q" and back); HumanOver (A, HM on an override layer of weight 1), HumanOverHalf
    /// (weight 0.6), HumanMask (HM on an override layer of weight 1 through MaskLeftArm, a humanoid
    /// mask with only LeftArm on), HumanMaskHalf (the same at 0.6), HumanAdd (HM on an additive
    /// layer of weight 0.6), HumanAddMask (the same through MaskLeftArm) and HumanOverN (HN on the
    /// base layer, HM on an override layer of weight 0.5).</summary>
    static void Humans()
    {
        var a = AssetDatabase.LoadAllAssetsAtPath("Assets/Rig/Resources/HumanA.fbx").OfType<AnimationClip>().First(c => c.name == "Scene");
        AnimationClip Muscles(string name, params (string Muscle, float From, float To)[] curves)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };
            foreach (var (muscle, from, to) in curves)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), Linear(0f, from, 1f, to));
            return Save(clip);
        }
        var hm = Muscles("HM", ("Spine Front-Back", 0.1f, 0.5f), ("Left Arm Down-Up", 0.3f, -0.4f),
            ("Left Forearm Stretch", -0.2f, 0.6f), ("Right Upper Leg Front-Back", 0.4f, -0.1f));
        var hn = Muscles("HN", ("Spine Front-Back", -0.3f, 0.2f), ("Left Forearm Stretch", 0.5f, 0.1f),
            ("Head Nod Down-Up", 0.2f, -0.5f), ("Right Arm Down-Up", -0.3f, 0.4f));
        var leftArm = new AvatarMask();
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            leftArm.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftArm);
        AssetDatabase.CreateAsset(leftArm, Dir + "MaskLeftArm.mask");
        foreach (bool wd in new[] { true, false })
        {
            string suffix = wd ? "On" : "Off";
            Switch("HumanSwitch" + suffix, hm, hn, wd, "HM", "HN");
            Tree("HumanTree" + suffix, hm, hn, wd);
            Switch("HumanFromA" + suffix, a, hm, wd, "A", "HM");
            Layered("HumanOver" + suffix, a, hm, AnimatorLayerBlendingMode.Override, 1f, null, wd);
            Layered("HumanOverHalf" + suffix, a, hm, AnimatorLayerBlendingMode.Override, 0.6f, null, wd);
            Layered("HumanMask" + suffix, a, hm, AnimatorLayerBlendingMode.Override, 1f, leftArm, wd);
            Layered("HumanMaskHalf" + suffix, a, hm, AnimatorLayerBlendingMode.Override, 0.6f, leftArm, wd);
            Layered("HumanAdd" + suffix, a, hm, AnimatorLayerBlendingMode.Additive, 0.6f, null, wd);
            Layered("HumanAddMask" + suffix, a, hm, AnimatorLayerBlendingMode.Additive, 0.6f, leftArm, wd);
            Layered("HumanOverN" + suffix, hn, hm, AnimatorLayerBlendingMode.Override, 0.5f, null, wd);
        }
    }

    /// <summary>Two layers: <paramref name="base"/> on the base layer and <paramref name="clip"/> on
    /// a second layer with the given blending, weight and mask.</summary>
    static void Layered(string name, AnimationClip @base, AnimationClip clip, AnimatorLayerBlendingMode mode, float weight, AvatarMask mask,
        bool writeDefaults)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + name + ".controller");
        var first = c.layers[0].stateMachine.AddState("Base");
        first.motion = @base;
        first.writeDefaultValues = writeDefaults;
        c.AddLayer("Layer");
        var layers = c.layers;
        layers[1].blendingMode = mode;
        layers[1].defaultWeight = weight;
        layers[1].avatarMask = mask;
        c.layers = layers;
        var second = c.layers[1].stateMachine.AddState("Top");
        second.motion = clip;
        second.writeDefaultValues = writeDefaults;
        EditorUtility.SetDirty(c);
    }

    /// <summary>One layer: P, to Q on "q" and back on not "q", 0.25 s fixed transitions.</summary>
    static void Switch(string name, AnimationClip p, AnimationClip q, bool writeDefaults, string first = "P", string second = "Q")
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + name + ".controller");
        c.AddParameter("q", AnimatorControllerParameterType.Bool);
        var machine = c.layers[0].stateMachine;
        var sp = machine.AddState(first);
        sp.motion = p;
        var sq = machine.AddState(second);
        sq.motion = q;
        machine.defaultState = sp;
        sp.writeDefaultValues = writeDefaults;
        sq.writeDefaultValues = writeDefaults;
        var to = sp.AddTransition(sq);
        to.hasExitTime = false; to.hasFixedDuration = true; to.duration = 0.25f;
        to.AddCondition(AnimatorConditionMode.If, 0f, "q");
        var back = sq.AddTransition(sp);
        back.hasExitTime = false; back.hasFixedDuration = true; back.duration = 0.25f;
        back.AddCondition(AnimatorConditionMode.IfNot, 0f, "q");
    }

    /// <summary>One layer: a 1D blend tree of P (0) and Q (1) on "blend".</summary>
    static void Tree(string name, AnimationClip p, AnimationClip q, bool writeDefaults)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + name + ".controller");
        c.AddParameter("blend", AnimatorControllerParameterType.Float);
        var state = c.CreateBlendTreeInController("Tree", out var tree, 0);
        tree.blendParameter = "blend";
        tree.useAutomaticThresholds = false;
        tree.AddChild(p, 0f);
        tree.AddChild(q, 1f);
        state.writeDefaultValues = writeDefaults;
        c.layers[0].stateMachine.defaultState = state;
    }
}
