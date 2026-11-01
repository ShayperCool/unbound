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
            CreateBlends();
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static AnimationClip ClipOf(string model) =>
        AssetDatabase.LoadAllAssetsAtPath(Dir + model + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

    static AvatarMask Mask(string name, AvatarMaskBodyPart[] parts, string[] transforms = null)
    {
        var path = Dir + name + ".mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask != null) return mask;
        mask = new AvatarMask();
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive(part, System.Array.IndexOf(parts, part) >= 0);
        if (transforms != null)
        {
            mask.transformCount = transforms.Length;
            for (int i = 0; i < transforms.Length; i++)
            {
                mask.SetTransformPath(i, transforms[i]);
                mask.SetTransformActive(i, true);
            }
        }
        AssetDatabase.CreateAsset(mask, path);
        return mask;
    }

    /// <summary>A controller whose base layer plays <paramref name="clip"/>, with an optional second
    /// layer playing <paramref name="layerClip"/> (override or additive, with a mask and weight).</summary>
    static AnimatorController Layered(string name, AnimationClip clip, AnimationClip layerClip, AvatarMask mask, bool additive, float weight,
        bool footIK = false, bool ikPass = false)
    {
        var path = Dir + name + ".controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) return null;
        var c = AnimatorController.CreateAnimatorControllerAtPathWithClip(path, clip);
        var layers = c.layers;
        layers[0].stateMachine.defaultState.iKOnFeet = footIK;
        layers[0].iKPass = ikPass;
        c.layers = layers;
        if (layerClip != null)
        {
            c.AddLayer("Layer");
            layers = c.layers;
            var state = layers[1].stateMachine.AddState("Second");
            state.motion = layerClip;
            layers[1].avatarMask = mask;
            layers[1].blendingMode = additive ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override;
            layers[1].defaultWeight = weight;
            c.layers = layers;
        }
        EditorUtility.SetDirty(c);
        return c;
    }

    /// <summary>The stage-four controllers: layers with humanoid body masks and transform masks, a
    /// transition and a blend tree between the two clips, two full-body layers, Foot IK, and a copy of
    /// HumanA with translation DoF.</summary>
    static void CreateBlends()
    {
        var walk = ClipOf("HumanA");
        var wave = ClipOf("HumanB");
        Layered("MaskArms", walk, wave, Mask("Arms", new[] { AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers }), false, 1f);
        Layered("MaskBodyAdd", walk, wave, Mask("BodyHead", new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head }), true, 0.7f);
        Layered("MaskLegTransform", walk, wave, Mask("LegTransform", new[] { AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.Root },
            new[] { "Hips", "Hips/LeftUpperLeg", "Hips/Spine/Chest/UpperChest/Neck/Head/Jaw" }), false, 0.8f);
        Layered("TwoLayers", walk, wave, null, false, 0.5f);
        Layered("FootIK", walk, null, null, false, 0f, footIK: true);
        Layered("BoneLocal", walk, null, null, false, 0f, ikPass: true);

        var path = Dir + "Transition.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) == null)
        {
            var c = AnimatorController.CreateAnimatorControllerAtPathWithClip(path, walk);
            var sm = c.layers[0].stateMachine;
            var from = sm.defaultState;
            var to = sm.AddState("Wave");
            to.motion = wave;
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 0.3f;
            t.duration = 0.25f;
            t.hasFixedDuration = false;
            EditorUtility.SetDirty(c);
        }
        path = Dir + "Tree.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) == null)
        {
            var c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("blend", AnimatorControllerParameterType.Float);
            var state = c.CreateBlendTreeInController("Tree", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "blend";
            tree.AddChild(walk, 0f);
            tree.AddChild(wave, 1f);
            EditorUtility.SetDirty(c);
        }

        // HumanA with translation DoF: the same file, its avatar's hasTranslationDoF on.
        var tdof = Dir + "HumanT.fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(tdof) == null) AssetDatabase.CopyAsset(Dir + "HumanA.fbx", tdof);
        var importer = (ModelImporter)AssetImporter.GetAtPath(tdof);
        var d = importer.humanDescription;
        if (!d.hasTranslationDoF)
        {
            d.hasTranslationDoF = true;
            importer.humanDescription = d;
            importer.SaveAndReimport();
        }
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "HumanT.controller") == null)
            AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + "HumanT.controller", ClipOf("HumanT"));
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
