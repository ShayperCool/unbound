using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Edit mode: animator controllers made through the Editor's controller API and their files: the
/// file CreateAnimatorControllerAtPath writes at once, the file after edits (parameters, a second
/// layer, states with clips, the default state, transitions with conditions from a state, from Any
/// State, from Entry and to Exit, two sub-state machines and transitions out of them (the later
/// one's first), a blend tree, a synced layer overriding a motion) before and after
/// AssetDatabase.SaveAssets, and a controller made with a clip. Each row records a file's hash or a
/// flag; run.py compares the files on disk after the run.
/// </summary>
public class ControllerAssetTests
{
    const string Folder = "Assets/Controllers";

    [Test]
    public void ControllerAssets()
    {
        AssetDatabase.CreateFolder("Assets", "Controllers");
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Native/Move.anim");

        var basic = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Basic.controller");
        ProbeLog.Snapshot("basic.created", Folder + "/Basic.controller");
        ProbeLog.Row("basic.dirty", EditorUtility.IsDirty(basic));

        basic.AddParameter("Speed", AnimatorControllerParameterType.Float);
        basic.AddParameter("Count", AnimatorControllerParameterType.Int);
        basic.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        basic.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        var root = basic.layers[0].stateMachine;
        var idle = root.AddState("Idle");
        idle.motion = walk;
        var run = root.AddState("Run", new Vector3(300f, 100f, 0f));
        run.motion = walk;
        run.speed = 1.5f;
        root.defaultState = idle;
        var toRun = idle.AddTransition(run);
        toRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        toRun.hasExitTime = false;
        toRun.duration = 0.2f;
        var back = run.AddTransition(idle);
        back.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        back.exitTime = 0.9f;
        var air = root.AddStateMachine("Air");
        var fall = air.AddState("Fall");
        var jump = root.AddAnyStateTransition(fall);
        jump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        var land = fall.AddExitTransition();
        land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        root.AddEntryTransition(run).AddCondition(AnimatorConditionMode.Equals, 2f, "Count");
        var swim = root.AddStateMachine("Swim");
        swim.AddState("Paddle");
        root.AddStateMachineTransition(swim, idle);
        var leave = root.AddStateMachineTransition(air, run);
        leave.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        root.AddStateMachineExitTransition(air);
        ProbeLog.Snapshot("basic.edited", Folder + "/Basic.controller");
        ProbeLog.Row("basic.editedDirty", EditorUtility.IsDirty(basic));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("basic.saved", Folder + "/Basic.controller");

        var layered = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Layered.controller");
        layered.AddParameter("Blend", AnimatorControllerParameterType.Float);
        var tree = layered.CreateBlendTreeInController("Locomotion", out var blend);
        blend.blendParameter = "Blend";
        blend.AddChild(walk, 0f);
        blend.AddChild(walk, 1f);
        layered.AddLayer("Upper");
        var layers = layered.layers;
        layers[1].defaultWeight = 0.5f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
        layered.layers = layers;
        layers[1].stateMachine.AddState("Wave").motion = walk;
        var synced = new AnimatorControllerLayer { name = "Synced", syncedLayerIndex = 0, defaultWeight = 1f };
        layered.AddLayer(synced);
        var syncedLayers = layered.layers;
        syncedLayers[2].SetOverrideMotion(tree, walk);
        layered.layers = syncedLayers;
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("layered.saved", Folder + "/Layered.controller");
        ProbeLog.Row("layered.tree", tree.name + ":" + blend.name);

        AnimatorController.CreateAnimatorControllerAtPathWithClip(Folder + "/WithClip.controller", walk);
        ProbeLog.Snapshot("withClip.created", Folder + "/WithClip.controller");
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("withClip.saved", Folder + "/WithClip.controller");
    }
}
