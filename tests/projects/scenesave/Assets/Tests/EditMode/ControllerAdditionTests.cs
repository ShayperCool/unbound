using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Edit mode: an animator controller read from its file (Assets/Existing/Basic.controller, one
/// the Editor wrote) given new objects through the controller API, which adds them to the asset with
/// AssetDatabase.AddObjectToAsset: a parameter, a second layer (its state machine), a state with a
/// transition from the existing default state, a sub-state machine with a state, and a blend tree
/// state; the file before and after AssetDatabase.SaveAssets, and whether the new objects are
/// sub-assets and dirty. run.py compares the file on disk after the run.
/// </summary>
public class ControllerAdditionTests
{
    const string Path = "Assets/Existing/Basic.controller";

    [Test]
    public void ControllerAdditions()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Path);
        ProbeLog.Row("existing.loaded", controller != null);
        ProbeLog.Row("existing.objects", AssetDatabase.LoadAllAssetsAtPath(Path).Length);
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Native/Move.anim");

        controller.AddParameter("Added", AnimatorControllerParameterType.Float);
        var root = controller.layers[0].stateMachine;
        var start = root.defaultState;
        var added = root.AddState("AddedState", new Vector3(500f, 200f, 0f));
        added.motion = walk;
        var toAdded = start.AddTransition(added);
        toAdded.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Added");
        var machine = root.AddStateMachine("AddedMachine");
        machine.AddState("Inner");
        controller.CreateBlendTreeInController("AddedBlend", out var tree);
        tree.blendParameter = "Added";
        tree.AddChild(walk);
        controller.AddLayer("AddedLayer");
        controller.layers[1].stateMachine.AddState("LayerState");

        ProbeLog.Row("added.subAsset", AssetDatabase.IsSubAsset(added));
        ProbeLog.Row("added.path", AssetDatabase.GetAssetPath(added));
        ProbeLog.Row("added.dirty", EditorUtility.IsDirty(added));
        ProbeLog.Row("controller.dirty", EditorUtility.IsDirty(controller));
        ProbeLog.Row("edited.objects", AssetDatabase.LoadAllAssetsAtPath(Path).Length);
        ProbeLog.Snapshot("existing.edited", Path);

        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("existing.saved", Path);
        ProbeLog.Row("saved.added.dirty", EditorUtility.IsDirty(added));
        ProbeLog.Row("saved.controller.dirty", EditorUtility.IsDirty(controller));
        ProbeLog.Row("saved.objects", AssetDatabase.LoadAllAssetsAtPath(Path).Length);
        ProbeLog.Row("saved.names", string.Join(",", AssetDatabase.LoadAllAssetsAtPath(Path).Select(o => o.GetType().Name + ":" + o.name).OrderBy(n => n, System.StringComparer.Ordinal)));
    }
}
