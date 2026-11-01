using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Writes the import settings (avatar, root node, clips with root motion settings) of the
/// probe's models and one controller per clip (run once in a copy of the project; the results are
/// committed), and records the probe in Play mode.</summary>
public static class ProbeRunner
{
    const string Dir = "Assets/Resources/";
    const string Take = "Scene";

    /// <summary>Model, avatar created from it, root node.</summary>
    internal static readonly (string Model, bool Avatar, string Node)[] Models =
    {
        ("Walker", true, "Hips"),
        ("WalkerTilt", true, "Hips"),
        ("WalkerNoNode", true, ""),
        ("WalkerNoAvatar", false, ""),
        ("Mover", false, ""),
        ("MoverAvatar", true, ""),
    };

    static ModelImporterClipAnimation Clip(string name, bool loop = true, float first = 0f, float last = 48f)
    {
        return new ModelImporterClipAnimation
        {
            name = name, takeName = Take, firstFrame = first, lastFrame = last, loopTime = loop,
            maskType = ClipAnimationMaskType.None, keepOriginalPositionY = true,
        };
    }

    static ModelImporterClipAnimation With(ModelImporterClipAnimation c, Action<ModelImporterClipAnimation> set)
    {
        set(c);
        return c;
    }

    internal static ModelImporterClipAnimation[] Clips() => new[]
    {
        Clip("Full", loop: false),
        Clip("Loop"),
        With(Clip("BakeRot"), c => c.lockRootRotation = true),
        With(Clip("BakeY"), c => c.lockRootHeightY = true),
        With(Clip("BakeXZ"), c => c.lockRootPositionXZ = true),
        With(Clip("BakeAll"), c => { c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true; }),
        With(Clip("KeepRot"), c => { c.lockRootRotation = true; c.keepOriginalOrientation = true; }),
        With(Clip("KeepXZ"), c => { c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true; }),
        With(Clip("FeetY"), c => { c.lockRootHeightY = true; c.keepOriginalPositionY = false; }),
        With(Clip("OffsetY"), c => { c.lockRootRotation = true; c.rotationOffset = 30f; }),
        With(Clip("Level"), c => { c.lockRootHeightY = true; c.heightOffset = 0.25f; }),
        With(Clip("OffsetOnly"), c => { c.rotationOffset = 30f; c.heightOffset = 0.25f; }),
        With(Clip("BakePose"), c => { c.loopPose = true; c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true; }),
        With(Clip("LoopPose"), c => c.loopPose = true),
        With(Clip("Cycle"), c => c.cycleOffset = 0.25f),
        Clip("Range", first: 12f, last: 36f),
        Clip("Once", loop: false, first: 12f, last: 36f),
    };

    public static void Create()
    {
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Resources" }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var (model, avatar, node) in Models)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(Dir + model + ".fbx");
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = avatar ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.importAnimation = true;
                importer.motionNodeName = node;
                importer.clipAnimations = Clips();
                importer.SaveAndReimport();
                var clips = AssetDatabase.LoadAllAssetsAtPath(Dir + model + ".fbx").OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).ToArray();
                foreach (var clip in clips)
                {
                    var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + "C_" + model + "_" + clip.name + ".controller");
                    c.layers[0].stateMachine.AddState(clip.name).motion = clip;
                }
            }
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static void Record()
    {
        SessionState.SetFloat("modelroot.deadline", (float)EditorApplication.timeSinceStartup + 600f);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    public static void CreateReviewController()
    {
        try
        {
            string path = Dir + "C_MatchReview.controller";
            AssetDatabase.DeleteAsset(path);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Dir + "Mover.fbx").OfType<AnimationClip>().ToArray();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var states = controller.layers[0].stateMachine;
            var loop = states.AddState("Loop"); loop.motion = clips.First(c => c.name == "Loop");
            var full = states.AddState("Full"); full.motion = clips.First(c => c.name == "Full");
            states.AddState("OtherLoop").motion = loop.motion;
            states.defaultState = loop;
            controller.AddParameter("Go", AnimatorControllerParameterType.Trigger);
            var transition = loop.AddTransition(full);
            transition.hasExitTime = false; transition.duration = .25f;
            transition.AddCondition(AnimatorConditionMode.If, 0, "Go");
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}

/// <summary>Watchdog of a recording (re-armed after every domain reload): the probe exits after
/// writing its output; fail if it never does.</summary>
[InitializeOnLoad]
static class ProbeWatchdog
{
    static ProbeWatchdog()
    {
        if (SessionState.GetFloat("modelroot.deadline", 0f) <= 0f) return;
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat("modelroot.deadline", 0f))
            {
                Debug.LogError("modelroot: watchdog expired");
                EditorApplication.Exit(2);
            }
        };
    }
}
