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
                foreach (var clip in clips)
                    if (!Settings.Any(setting => setting.name == clip.name)) clip.loopTime = true;
                if (name == "HumanA") clips = WithSettings(clips);
                importer.clipAnimations = clips;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.SaveAndReimport();
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                var avatar = assets.OfType<Avatar>().FirstOrDefault();
                Debug.Log("PROBE " + name + " avatar " + (avatar != null ? avatar.name + " valid " + avatar.isValid + " human " + avatar.isHuman : "none")
                    + " human bones " + importer.humanDescription.human.Length);
                if (avatar == null || !avatar.isValid || !avatar.isHuman) code = 1;
                var motion = assets.OfType<AnimationClip>().First(c => c.name == "Scene");
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
            SettingsControllers();
            MuscleClips();
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static AnimationClip ClipOf(string name, string clip = "Scene") =>
        AssetDatabase.LoadAllAssetsAtPath(Dir + name + ".fbx").OfType<AnimationClip>().First(c => c.name == clip);

    /// <summary>HumanA's clip again with non-default root settings (the take's whole range, loop
    /// time on unless named Once): bake into pose of the rotation, Y and XZ; based upon the original
    /// (rotation, XZ), the center of mass and the feet (Y); offsets; loop pose; mirror; cycle
    /// offset; and a few combinations.</summary>
    internal static readonly (string name, System.Action<ModelImporterClipAnimation> set)[] Settings =
    {
        ("BakeRot", c => c.lockRootRotation = true),
        ("BakeY", c => c.lockRootHeightY = true),
        ("BakeXZ", c => c.lockRootPositionXZ = true),
        ("OrigRot", c => c.keepOriginalOrientation = true),
        ("OrigXZ", c => c.keepOriginalPositionXZ = true),
        ("CoM", c => c.keepOriginalPositionY = false),
        ("Feet", c => { c.keepOriginalPositionY = false; c.heightFromFeet = true; }),
        ("Offset", c => { c.rotationOffset = 30f; c.heightOffset = 0.1f; }),
        ("LoopPose", c => c.loopPose = true),
        ("Mirror", c => c.mirror = true),
        ("Cycle", c => c.cycleOffset = 0.25f),
        ("BakeAll", c => { c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true; }),
        ("BakeOrig", c => { c.lockRootRotation = true; c.keepOriginalOrientation = true; c.lockRootHeightY = true;
            c.keepOriginalPositionY = false; c.heightFromFeet = true; c.keepOriginalPositionXZ = true; }),
        ("BakeOffset", c => { c.lockRootRotation = true; c.rotationOffset = -20f; c.lockRootHeightY = true; c.heightOffset = -0.05f; }),
        ("BakeLoopPose", c => { c.lockRootRotation = true; c.lockRootHeightY = true; c.loopPose = true; }),
        ("OrigOnce", c => { c.loopTime = false; c.keepOriginalOrientation = true; c.keepOriginalPositionXZ = true; c.rotationOffset = 15f; }),
        ("BakeOnce", c => { c.loopTime = false; c.lockRootRotation = true; c.lockRootPositionXZ = true; }),
        ("MirrorOffset", c => { c.mirror = true; c.rotationOffset = 30f; c.heightOffset = 0.1f; c.keepOriginalPositionXZ = true; }),
    };

    static ModelImporterClipAnimation[] WithSettings(ModelImporterClipAnimation[] clips)
    {
        var list = clips.ToList();
        var source = list.First(c => c.name == "Scene");
        foreach (var (name, set) in Settings)
        {
            if (list.Any(c => c.name == name)) continue;
            var clip = new ModelImporterClipAnimation
            {
                name = name, takeName = source.takeName, firstFrame = source.firstFrame, lastFrame = source.lastFrame,
                loopTime = true, keepOriginalPositionY = true,
            };
            set(clip);
            list.Add(clip);
        }
        return list.ToArray();
    }

    /// <summary>Humanoid .anim clips (muscle curves) whose start and stop differ, each with a
    /// controller Human{name}: LoopMuscles (loop pose), LoopMusclesBake (loop pose, rotation and Y
    /// baked into the pose) and MirrorMuscles (mirror); OrigTurned (rotation and XZ based upon the
    /// original, the body turned 40 degrees at the start) and FeetRight (Y based upon the feet,
    /// foot goal curves with the right foot lower).</summary>
    static void MuscleClips()
    {
        TurnedClips();
        foreach (var name in new[] { "LoopMuscles", "LoopMusclesBake", "MirrorMuscles" })
        {
            var path = Dir + name + ".anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) continue;
            var clip = new AnimationClip();
            void Set(string attribute, params float[] keys)
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < keys.Length; i += 2) curve.AddKey(keys[i], keys[i + 1]);
                clip.SetCurve("", typeof(Animator), attribute, curve);
            }
            var q = Quaternion.Euler(10f, 20f, 5f);
            Set("RootT.x", 0f, 0.02f, 1f, 0.3f);
            Set("RootT.y", 0f, 1f, 0.5f, 0.95f, 1f, 1.1f);
            Set("RootT.z", 0f, 0f, 1f, 0.5f);
            Set("RootQ.x", 0f, 0f, 1f, q.x);
            Set("RootQ.y", 0f, 0f, 1f, q.y);
            Set("RootQ.z", 0f, 0f, 1f, q.z);
            Set("RootQ.w", 0f, 1f, 1f, q.w);
            Set("Spine Front-Back", 0f, 0f, 0.5f, 0.3f, 1f, 0.6f);
            Set("Spine Left-Right", 0f, 0.2f, 1f, -0.1f);
            Set("Head Turn Left-Right", 0f, 0.3f, 1f, -0.2f);
            Set("Left Arm Down-Up", 0f, 0.2f, 1f, -0.4f);
            Set("Right Arm Front-Back", 0f, 0.1f, 1f, 0.5f);
            Set("Right Upper Leg Front-Back", 0f, -0.1f, 1f, 0.3f);
            Set("Left Lower Leg Stretch", 0f, 0.4f, 1f, 0.1f);
            Set("LeftHand.Index.1 Stretched", 0f, 0.5f, 1f, -0.5f);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = name != "MirrorMuscles";
            settings.loopBlendOrientation = settings.loopBlendPositionY = name == "LoopMusclesBake";
            settings.mirror = name == "MirrorMuscles";
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + "Human" + name + ".controller", clip);
        }
    }

    static void TurnedClips()
    {
        foreach (var name in new[] { "OrigTurned", "FeetRight" })
        {
            var path = Dir + name + ".anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) continue;
            var clip = new AnimationClip();
            void Set(string attribute, params float[] keys)
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < keys.Length; i += 2) curve.AddKey(keys[i], keys[i + 1]);
                clip.SetCurve("", typeof(Animator), attribute, curve);
            }
            var q0 = Quaternion.Euler(0f, 40f, 0f);
            var q1 = Quaternion.Euler(5f, 70f, 0f);
            Set("RootT.x", 0f, 0.1f, 1f, 0.4f);
            Set("RootT.y", 0f, 1f, 0.5f, 0.97f, 1f, 1.05f);
            Set("RootT.z", 0f, 0.2f, 1f, 0.6f);
            Set("RootQ.x", 0f, q0.x, 1f, q1.x);
            Set("RootQ.y", 0f, q0.y, 1f, q1.y);
            Set("RootQ.z", 0f, q0.z, 1f, q1.z);
            Set("RootQ.w", 0f, q0.w, 1f, q1.w);
            Set("Spine Front-Back", 0f, 0f, 1f, 0.4f);
            Set("Left Upper Leg Front-Back", 0f, 0.3f, 1f, -0.2f);
            Set("Right Lower Leg Stretch", 0f, 0.2f, 1f, 0.6f);
            if (name == "FeetRight")
            {
                Set("LeftFootT.x", 0f, -0.1f, 1f, -0.12f);
                Set("LeftFootT.y", 0f, -0.75f, 1f, -0.8f);
                Set("LeftFootT.z", 0f, 0.05f, 1f, 0f);
                Set("LeftFootQ.w", 0f, 1f, 1f, 1f);
                Set("RightFootT.x", 0f, 0.1f, 1f, 0.12f);
                Set("RightFootT.y", 0f, -0.9f, 1f, -0.85f);
                Set("RightFootT.z", 0f, -0.05f, 1f, 0.02f);
                Set("RightFootQ.w", 0f, 1f, 1f, 1f);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            if (name == "OrigTurned") settings.keepOriginalOrientation = settings.keepOriginalPositionXZ = true;
            else { settings.keepOriginalPositionY = false; settings.heightFromFeet = true; }
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + "Human" + name + ".controller", clip);
        }
    }

    /// <summary>HumanSet{name}: one state playing HumanA's clip with the settings of that name.</summary>
    static void SettingsControllers()
    {
        foreach (var (name, _) in Settings)
        {
            var path = Dir + "HumanSet" + name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) continue;
            AnimatorController.CreateAnimatorControllerAtPathWithClip(path, ClipOf("HumanA", name));
        }
    }

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
