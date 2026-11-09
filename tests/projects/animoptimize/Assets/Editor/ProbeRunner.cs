using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> (batchmode) gives every model a generic avatar created from the model, sets each
/// variant's Optimize Game Objects and Extra Transforms to Expose, and writes a controller per
/// model playing its take. With <c>ANIM_OPTIMIZE_OUT</c> set, a windowed Editor opens the scene,
/// enters Play mode and quits when <see cref="AnimOptimizeProbe"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("ANIM_OPTIMIZE_OUT") == null || Application.isBatchMode) return;
        if (SessionState.GetBool("AnimOptimizeProbe.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("AnimOptimizeProbe.finished", false))
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

    static void Tick()
    {
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !AnimOptimizeProbe.Finished) return;
        SessionState.SetBool("AnimOptimizeProbe.finished", true);
        EditorApplication.ExitPlaymode();
    }

    public static void Create()
    {
        foreach (var path in Directory.GetFiles("Assets/Resources/Models", "*.fbx").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            // Unreduced curves: the poses do not depend on keyframe reduction.
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            if (name == "Human")
            {
                // The humanoid probe's model as it is there.
            }
            else if (name == "HumanOpt")
            {
                // The humanoid probe's model (its avatar as there), with its left hand exposed.
                var hand = importer.humanDescription.human.First(b => b.humanName == "LeftHand").boneName;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var bone = model.GetComponentsInChildren<Transform>(true).First(t => t.name == hand);
                importer.optimizeGameObjects = true;
                importer.extraExposedTransformPaths = new[] { AnimationUtility.CalculateTransformPath(bone, model.transform) };
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.optimizeGameObjects = name != "Rig";
                importer.extraExposedTransformPaths = name == "RigExposed" || name == "TurnedOpt" ? new[] { "Rig/Hips/Spine/Hand" }
                    : name == "RigExposedRoot" ? new[] { "Rig/Hips" } : new string[0];
            }
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            AnimatorController.CreateAnimatorControllerAtPathWithClip("Assets/Resources/C_" + name + ".controller", clip);
        }
        AssetDatabase.SaveAssets();
    }
}
