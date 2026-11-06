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
            Blends();
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static AnimationClip ClipOf(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(Dir + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

    /// <summary>The blended root motion controllers, HumanA's clip (A) and HumanB's (B):
    /// HumanBlendTransition (A, then B after an exit time of 0.25 over 0.3 s), HumanBlendOverride
    /// (A, B on an override layer of weight 0.5), HumanBlendAdditive (A, B on an additive layer of
    /// weight 0.6) and HumanBlendTree (a 1D blend tree of A at 0 and B at 1, blend 0.35).</summary>
    static void Blends()
    {
        var a = ClipOf("HumanA");
        var b = ClipOf("HumanB");
        AnimatorController Make(string name)
        {
            var path = Dir + name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) return null;
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        var transition = Make("HumanBlendTransition");
        if (transition != null)
        {
            var machine = transition.layers[0].stateMachine;
            var first = machine.AddState("A");
            first.motion = a;
            var second = machine.AddState("B");
            second.motion = b;
            machine.defaultState = first;
            var t = first.AddTransition(second);
            t.hasExitTime = true;
            t.exitTime = 0.25f;
            t.hasFixedDuration = true;
            t.duration = 0.3f;
            EditorUtility.SetDirty(transition);
        }
        foreach (var (name, mode, weight) in new[] { ("HumanBlendOverride", AnimatorLayerBlendingMode.Override, 0.5f),
                     ("HumanBlendAdditive", AnimatorLayerBlendingMode.Additive, 0.6f) })
        {
            var controller = Make(name);
            if (controller == null) continue;
            controller.layers[0].stateMachine.AddState("A").motion = a;
            controller.AddLayer("B");
            var layers = controller.layers;
            layers[1].blendingMode = mode;
            layers[1].defaultWeight = weight;
            controller.layers = layers;
            controller.layers[1].stateMachine.AddState("B").motion = b;
            EditorUtility.SetDirty(controller);
        }
        var tree = Make("HumanBlendTree");
        if (tree != null)
        {
            tree.AddParameter("Blend", AnimatorControllerParameterType.Float);
            var parameters = tree.parameters;
            parameters[0].defaultFloat = 0.35f;
            tree.parameters = parameters;
            var state = tree.CreateBlendTreeInController("Tree", out var blend, 0);
            blend.blendParameter = "Blend";
            blend.AddChild(a, 0f);
            blend.AddChild(b, 1f);
            EditorUtility.SetDirty(tree);
        }
        Masks(a, b);
    }

    /// <summary>The humanoid masks (no transforms): MaskNoRoot (every body part but Root), MaskUpper
    /// (Body, Head, arms, fingers and hand IK) and MaskUpperRoot (MaskUpper and Root). For each mask,
    /// HumanMask{Override,Additive}{mask} plays A with B on a layer of weight 0.5 (override) or 0.6
    /// (additive) with the mask; MaskUpperFootIK (MaskUpperRoot and the foot IK parts) and MaskNoFootIK
    /// (every part but the foot IK ones) on an override layer of weight 0.5; HumanAdditiveTree plays A with a 1D blend tree of A at 0 and B at 1
    /// (blend 0.35) on an additive layer of weight 0.6.</summary>
    static void Masks(AnimationClip a, AnimationClip b)
    {
        var upper = new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK };
        AvatarMask Mask(string name, System.Func<AvatarMaskBodyPart, bool> active)
        {
            var path = Dir + name + ".mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask != null) return mask;
            mask = new AvatarMask();
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, active(part));
            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }
        var masks = new[]
        {
            ("NoRoot", Mask("MaskNoRoot", p => p != AvatarMaskBodyPart.Root)),
            ("Upper", Mask("MaskUpper", p => System.Array.IndexOf(upper, p) >= 0)),
            ("UpperRoot", Mask("MaskUpperRoot", p => p == AvatarMaskBodyPart.Root || System.Array.IndexOf(upper, p) >= 0)),
        };
        // The feet: legs off with the foot IK parts on, and the reverse (override layers).
        var feet = new[]
        {
            ("UpperFootIK", Mask("MaskUpperFootIK", p => p == AvatarMaskBodyPart.Root || p == AvatarMaskBodyPart.LeftFootIK
                || p == AvatarMaskBodyPart.RightFootIK || System.Array.IndexOf(upper, p) >= 0)),
            ("NoFootIK", Mask("MaskNoFootIK", p => p != AvatarMaskBodyPart.LeftFootIK && p != AvatarMaskBodyPart.RightFootIK)),
        };
        foreach (var (maskName, mask) in feet)
        {
            var path = Dir + "HumanMaskOverride" + maskName + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) continue;
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.layers[0].stateMachine.AddState("A").motion = a;
            controller.AddLayer("B");
            var layers = controller.layers;
            layers[1].defaultWeight = 0.5f;
            layers[1].avatarMask = mask;
            controller.layers = layers;
            controller.layers[1].stateMachine.AddState("B").motion = b;
            EditorUtility.SetDirty(controller);
        }
        foreach (var (maskName, mask) in masks)
            foreach (var (kind, mode, weight) in new[] { ("Override", AnimatorLayerBlendingMode.Override, 0.5f),
                         ("Additive", AnimatorLayerBlendingMode.Additive, 0.6f) })
            {
                var path = Dir + "HumanMask" + kind + maskName + ".controller";
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) continue;
                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                controller.layers[0].stateMachine.AddState("A").motion = a;
                controller.AddLayer("B");
                var layers = controller.layers;
                layers[1].blendingMode = mode;
                layers[1].defaultWeight = weight;
                layers[1].avatarMask = mask;
                controller.layers = layers;
                controller.layers[1].stateMachine.AddState("B").motion = b;
                EditorUtility.SetDirty(controller);
            }
        var treePath = Dir + "HumanAdditiveTree.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(treePath) == null)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(treePath);
            controller.AddParameter("Blend", AnimatorControllerParameterType.Float);
            var parameters = controller.parameters;
            parameters[0].defaultFloat = 0.35f;
            controller.parameters = parameters;
            controller.layers[0].stateMachine.AddState("A").motion = a;
            controller.AddLayer("Tree");
            var layers = controller.layers;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[1].defaultWeight = 0.6f;
            controller.layers = layers;
            controller.CreateBlendTreeInController("Tree", out var blend, 1);
            blend.blendParameter = "Blend";
            blend.AddChild(a, 0f);
            blend.AddChild(b, 1f);
            EditorUtility.SetDirty(controller);
        }
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
