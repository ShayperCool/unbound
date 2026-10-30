using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    public static void Create()
    {
        var clip = new AnimationClip { name = "RootMotion", frameRate = 60f };
        void Curve(string path, string property, float from, float to) => AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(path, typeof(Transform), property), AnimationCurve.Linear(0f, from, 1f, to));
        Curve("", "m_LocalPosition.x", 0f, 2f);
        Curve("", "m_LocalPosition.y", 0f, 0f);
        Curve("", "m_LocalPosition.z", 0f, 1f);
        Curve("", "localEulerAnglesRaw.y", 0f, 90f);
        Curve("Child", "m_LocalPosition.x", 0f, 4f);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, "Assets/Resources/RootMotion.anim");
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/RootMotion.controller");
        var machine = controller.layers[0].stateMachine;
        var state = machine.AddState("Move");
        state.motion = clip;
        state.AddStateMachineBehaviour<StateMoveObserver>();
        machine.defaultState = state;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
        CreatePlain();
        BlendSetup.Create();
    }

    public static void CreatePlain()
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/RootMotionPlain.controller");
        var machine = controller.layers[0].stateMachine;
        var state = machine.AddState("Move");
        state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/RootMotion.anim");
        machine.defaultState = state;
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
