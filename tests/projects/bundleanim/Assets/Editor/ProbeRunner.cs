using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `-executeMethod ProbeRunner.Create` makes the bundled animation assets once (clips with every
/// kind of curve, an animator controller with a trigger transition and a blend tree, a rig prefab
/// that plays it, a legacy clip and its Animation prefab) and builds the `anim` bundle for
/// StandaloneLinux64 into Reference/bundles. `-executeMethod ProbeRunner.Record` with
/// <c>ANIM_PROBE_OUT</c> set plays the probe scene, where <see cref="AnimProbe"/> loads it.
/// </summary>
public static class ProbeRunner
{
    static AnimationCurve Smooth(params float[] tv)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i < tv.Length; i += 2) curve.AddKey(tv[i], tv[i + 1]);
        for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        return curve;
    }

    static AnimationCurve Linear(params float[] tv)
    {
        var curve = Smooth(tv);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    static AnimationCurve Step(params float[] tv)
    {
        var curve = Smooth(tv);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
        }
        return curve;
    }

    static void Set(AnimationClip clip, string path, System.Type type, string property, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);

    public static void Create()
    {
        foreach (var path in new[] { "Assets/Bundled", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        foreach (var dir in new[] { "Assets/Bundled", "Assets/Scenes" })
            Directory.CreateDirectory(dir);

        // Move: position, euler rotation, scale, script fields, a material color, activity, events.
        var move = new AnimationClip { name = "Move", frameRate = 30f };
        Set(move, "", typeof(Transform), "m_LocalPosition.x", Smooth(0f, 0f, 0.5f, 1f, 1f, 0f));
        Set(move, "", typeof(Transform), "m_LocalPosition.y", Smooth(0f, 0.5f, 1f, 0.5f));
        Set(move, "", typeof(Transform), "m_LocalPosition.z", Linear(0f, 0f, 1f, 2f));
        Set(move, "Arm", typeof(Transform), "localEulerAnglesRaw.x", Linear(0f, 0f, 1f, 30f));
        Set(move, "Arm", typeof(Transform), "localEulerAnglesRaw.y", Smooth(0f, 0f, 1f, 0f));
        Set(move, "Arm", typeof(Transform), "localEulerAnglesRaw.z", Smooth(0f, 0f, 0.4f, 120f, 1f, 90f));
        Set(move, "Arm/Hand", typeof(Transform), "m_LocalScale.x", Smooth(0f, 1f, 1f, 2f));
        Set(move, "Arm/Hand", typeof(Transform), "m_LocalScale.y", Smooth(0f, 1f, 1f, 0.5f));
        Set(move, "Arm/Hand", typeof(Transform), "m_LocalScale.z", Smooth(0f, 1f, 1f, 1f));
        Set(move, "", typeof(AnimTarget), "amount", Smooth(0f, 0f, 0.3f, 4f, 1f, 10f));
        Set(move, "", typeof(AnimTarget), "count", Linear(0f, 0f, 1f, 5f));
        Set(move, "", typeof(AnimTarget), "flag", Step(0f, 0f, 0.505f, 1f));
        Set(move, "", typeof(AnimTarget), "offset.y", Smooth(0f, -1f, 1f, 1f));
        Set(move, "", typeof(AnimTarget), "tint.g", Linear(0f, 1f, 1f, 0f));
        Set(move, "Arm", typeof(MeshRenderer), "material._Color.r", Linear(0f, 1f, 1f, 0.25f));
        Set(move, "Arm", typeof(MeshRenderer), "material._Color.g", Linear(0f, 0.5f, 1f, 1f));
        Set(move, "Arm", typeof(MeshRenderer), "material._Color.a", Linear(0f, 1f, 1f, 0.5f));
        Set(move, "Arm", typeof(MeshRenderer), "material._Glossiness", Linear(0f, 0.2f, 1f, 0.9f));
        Set(move, "Arm", typeof(MeshRenderer), "material._MainTex_ST.z", Linear(0f, 0f, 1f, 0.5f));
        Set(move, "Arm/Hand", typeof(GameObject), "m_IsActive", Step(0f, 1f, 0.75f, 0f));
        AnimationUtility.SetAnimationEvents(move, new[]
        {
            new AnimationEvent { time = 0.25f, functionName = "OnEvent", floatParameter = 1.5f },
            new AnimationEvent { time = 0.6f, functionName = "OnText", stringParameter = "hi" },
        });
        AssetDatabase.CreateAsset(move, "Assets/Bundled/Move.anim");

        // Spin: quaternion rotation curves, a looping clip.
        var spin = new AnimationClip { name = "Spin" };
        var q0 = Quaternion.identity;
        var q1 = Quaternion.Euler(0f, 90f, 45f);
        var q2 = Quaternion.Euler(10f, 180f, 0f);
        spin.SetCurve("Arm", typeof(Transform), "localRotation.x", Smooth(0f, q0.x, 0.5f, q1.x, 1f, q2.x));
        spin.SetCurve("Arm", typeof(Transform), "localRotation.y", Smooth(0f, q0.y, 0.5f, q1.y, 1f, q2.y));
        spin.SetCurve("Arm", typeof(Transform), "localRotation.z", Smooth(0f, q0.z, 0.5f, q1.z, 1f, q2.z));
        spin.SetCurve("Arm", typeof(Transform), "localRotation.w", Smooth(0f, q0.w, 0.5f, q1.w, 1f, q2.w));
        spin.SetCurve("", typeof(Transform), "localPosition.y", Smooth(0f, 0f, 0.5f, 0.3f, 1f, 0f));
        var settings = AnimationUtility.GetAnimationClipSettings(spin);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(spin, settings);
        AssetDatabase.CreateAsset(spin, "Assets/Bundled/Spin.anim");

        // Bob: a legacy clip for the Animation component.
        var bob = new AnimationClip { name = "Bob", legacy = true, wrapMode = WrapMode.Loop };
        bob.SetCurve("", typeof(Transform), "localPosition.y", Smooth(0f, 0f, 0.5f, 1f, 1f, 0f));
        bob.SetCurve("", typeof(Transform), "localPosition.x", Linear(0f, 0f, 1f, 1f));
        AssetDatabase.CreateAsset(bob, "Assets/Bundled/Bob.anim");

        // The controller: Idle (Move, at the speed parameter) -> Spin on a trigger -> Mix (a 1D blend
        // tree of both) after Spin's exit time.
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Bundled/Rig.controller");
        controller.AddParameter("go", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("speed", AnimatorControllerParameterType.Float);
        controller.parameters = SetDefault(controller.parameters, "speed", 1f);
        var machine = controller.layers[0].stateMachine;
        var idle = machine.AddState("Idle");
        idle.motion = move;
        idle.speedParameterActive = true;
        idle.speedParameter = "speed";
        var spinState = machine.AddState("Spin");
        spinState.motion = spin;
        var mix = controller.CreateBlendTreeInController("Mix", out var tree, 0);
        tree.blendParameter = "speed";
        tree.AddChild(move, 0f);
        tree.AddChild(spin, 2f);
        machine.defaultState = idle;
        var toSpin = idle.AddTransition(spinState);
        toSpin.AddCondition(AnimatorConditionMode.If, 0f, "go");
        toSpin.hasExitTime = false;
        toSpin.duration = 0.25f;
        var toMix = spinState.AddTransition(mix);
        toMix.hasExitTime = true;
        toMix.exitTime = 0.91f;
        toMix.duration = 0.1f;
        AssetDatabase.SaveAssets();

        var rig = new GameObject("Rig");
        rig.AddComponent<AnimTarget>();
        rig.AddComponent<Animator>().runtimeAnimatorController = controller;
        var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "Arm";
        Object.DestroyImmediate(arm.GetComponent<Collider>());
        arm.transform.SetParent(rig.transform, false);
        arm.transform.localPosition = new Vector3(0f, 1f, 0f);
        var material = new Material(Shader.Find("Standard")) { color = new Color(0.8f, 0.8f, 0.8f) };
        AssetDatabase.CreateAsset(material, "Assets/Bundled/Arm.mat");
        arm.GetComponent<MeshRenderer>().sharedMaterial = material;
        var hand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hand.name = "Hand";
        Object.DestroyImmediate(hand.GetComponent<Collider>());
        hand.transform.SetParent(arm.transform, false);
        hand.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        PrefabUtility.SaveAsPrefabAsset(rig, "Assets/Bundled/Rig.prefab");
        Object.DestroyImmediate(rig);

        var bobber = new GameObject("Bobber");
        var animation = bobber.AddComponent<Animation>();
        animation.clip = bob;
        animation.AddClip(bob, "Bob");
        animation.playAutomatically = true;
        PrefabUtility.SaveAsPrefabAsset(bobber, "Assets/Bundled/Bobber.prefab");
        Object.DestroyImmediate(bobber);

        foreach (var path in new[] { "Assets/Bundled/Move.anim", "Assets/Bundled/Spin.anim", "Assets/Bundled/Bob.anim", "Assets/Bundled/Rig.controller", "Assets/Bundled/Arm.mat", "Assets/Bundled/Rig.prefab", "Assets/Bundled/Bobber.prefab" })
            AssetImporter.GetAtPath(path).assetBundleName = "anim";

        var probeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(probeScene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();

        var output = "Reference/bundles";
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Directory.CreateDirectory(output);
        BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneLinux64);
    }

    static AnimatorControllerParameter[] SetDefault(AnimatorControllerParameter[] parameters, string name, float value)
    {
        foreach (var p in parameters)
            if (p.name == name) p.defaultFloat = value;
        return parameters;
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
