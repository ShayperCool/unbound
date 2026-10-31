using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Configures the humanoid probe's rigs and controllers, and records the probe.</summary>
public static class ProbeRunner
{
    const string Dir = "Assets/Resources/";

    /// <summary>Sets both rigs to Humanoid with an avatar created from the model (the importer maps
    /// the bones and writes the human description into the .meta), loops their clips without
    /// keyframe reduction and makes one controller per clip, plus HumanAIK (HumanA's clip on a layer
    /// with an IK pass). Exits nonzero when an avatar is not a valid human.</summary>
    public static void Create()
    {
        int code = 0;
        try
        {
            foreach (var name in new[] { "HumanA", "HumanB" })
            {
                var path = Dir + name + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.SaveAndReimport();
                }
                var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                foreach (var clip in clips) clip.loopTime = true;
                importer.clipAnimations = clips;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.SaveAndReimport();
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                var avatar = assets.OfType<Avatar>().FirstOrDefault();
                Debug.Log("PROBE " + name + " avatar " + (avatar != null ? avatar.name + " valid " + avatar.isValid + " human " + avatar.isHuman : "none")
                    + " human bones " + importer.humanDescription.human.Length);
                if (avatar == null || !avatar.isValid || !avatar.isHuman) code = 1;
                var motion = assets.OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + name + ".controller") == null)
                    AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + name + ".controller", motion);
                if (name == "HumanA" && AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "HumanAIK.controller") == null)
                {
                    var ik = AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + "HumanAIK.controller", motion);
                    var layers = ik.layers;
                    layers[0].iKPass = true;
                    ik.layers = layers;
                    EditorUtility.SetDirty(ik);
                }
            }
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
