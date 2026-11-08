using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> (batchmode) writes the probe's clips and controllers. With
/// <c>ANIM_CALLBACKS_OUT</c> set, a windowed Editor opens the empty scene, enters Play mode and
/// quits when <see cref="AnimCallbacks"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("ANIM_CALLBACKS_OUT") == null || Application.isBatchMode) return;
        if (SessionState.GetBool("AnimCallbacks.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("AnimCallbacks.finished", false)) EditorApplication.Exit(0);
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

    static void Tick()
    {
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !AnimCallbacks.Finished) return;
        SessionState.SetBool("AnimCallbacks.finished", true);
        EditorApplication.ExitPlaymode();
    }

    static AnimationEvent[] Events(string name) => new[]
    {
        new AnimationEvent { time = .03f, functionName = "OnClipEvent", stringParameter = name + "@0.03" },
        new AnimationEvent { time = .25f, functionName = "OnClipEvent", stringParameter = name + "@0.25" },
    };

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        var generic = new AnimationClip { name = "GenericEvents", frameRate = 60f };
        AnimationUtility.SetEditorCurve(generic, EditorCurveBinding.FloatCurve("Marker", typeof(Transform), "m_LocalPosition.x"),
            AnimationCurve.Linear(0f, 0f, .5f, 1f));
        var settings = AnimationUtility.GetAnimationClipSettings(generic);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(generic, settings);
        AnimationUtility.SetAnimationEvents(generic, Events("GenericEvents"));
        AssetDatabase.CreateAsset(generic, "Assets/Resources/GenericEvents.anim");

        var human = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/LoopMuscles.anim"));
        human.name = "HumanEvents";
        AnimationUtility.SetAnimationEvents(human, Events("HumanEvents"));
        AssetDatabase.CreateAsset(human, "Assets/Resources/HumanEvents.anim");

        var legacy = new AnimationClip { name = "LegacyEvents", frameRate = 60f, legacy = true, wrapMode = WrapMode.Loop };
        AnimationUtility.SetEditorCurve(legacy, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y"),
            AnimationCurve.Linear(0f, 0f, .5f, 1f));
        AnimationUtility.SetAnimationEvents(legacy, Events("LegacyEvents"));
        AssetDatabase.CreateAsset(legacy, "Assets/Resources/LegacyEvents.anim");

        Controller("GenericOrder", generic);
        Controller("HumanOrder", human);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Base layer: A, then B after 12% of A with a 0.04 s transition; a second layer at
    /// weight 0.5 playing C. Both layers have an IK pass; every state has an OrderState.</summary>
    static void Controller(string name, AnimationClip clip)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/" + name + ".controller");
        controller.AddLayer("Second");
        var layers = controller.layers;
        layers[0].iKPass = true;
        layers[1].iKPass = true;
        layers[1].defaultWeight = .5f;
        controller.layers = layers;
        var machine = controller.layers[0].stateMachine;
        var a = machine.AddState("A");
        a.motion = clip;
        a.AddStateMachineBehaviour<OrderState>();
        var b = machine.AddState("B");
        b.motion = clip;
        b.AddStateMachineBehaviour<OrderState>();
        machine.defaultState = a;
        var toB = a.AddTransition(b);
        toB.hasExitTime = true;
        toB.exitTime = .12f;
        toB.duration = .04f;
        toB.hasFixedDuration = true;
        var second = controller.layers[1].stateMachine;
        var c = second.AddState("C");
        c.motion = clip;
        c.AddStateMachineBehaviour<OrderState>();
        second.defaultState = c;
    }
}
