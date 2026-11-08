using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class FollowupAuthor
{
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Resources/FollowupGraph.controller") != null) return;
        var clip = new AnimationClip { name = "FollowupMotion" };
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
        AssetDatabase.CreateAsset(clip, "Assets/Resources/FollowupMotion.anim");
        var replacement = new AnimationClip { name = "FollowupReplacement" };
        replacement.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 2, 1, 3));
        AssetDatabase.CreateAsset(replacement, "Assets/Resources/FollowupReplacement.anim");
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/FollowupGraph.controller");
        controller.AddParameter("Value", AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        var first = machine.AddState("Base"); first.motion = clip; machine.defaultState = first;
        var other = machine.AddState("Other"); other.motion = clip;
        controller.AddLayer("Upper");
        var layers = controller.layers;
        layers[1].defaultWeight = 1;
        var upper = layers[1].stateMachine.AddState("UpperBase"); upper.motion = clip;
        layers[1].stateMachine.defaultState = upper;
        controller.layers = layers;
        AssetDatabase.SaveAssets();
    }
}
