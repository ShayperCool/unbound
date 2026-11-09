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
            MirrorStates();
            FirstSteps();
            Leftovers();
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
        if (!list.Any(c => c.name == "Events"))
        {
            // HumanA's clip again (loop time) with three events calling ProbeEvent.
            AnimationEvent Event(float time, float marker) =>
                new AnimationEvent { time = time, functionName = "ProbeEvent", floatParameter = marker };
            list.Add(new ModelImporterClipAnimation
            {
                name = "Events", takeName = source.takeName, firstFrame = source.firstFrame, lastFrame = source.lastFrame,
                loopTime = true, keepOriginalPositionY = true,
                events = new[] { Event(0.1f, 1f), Event(0.45f, 2f), Event(0.8f, 3f) },
            });
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

    /// <summary>Mirrored states on HumanA (A: its clip, B: HumanB's): HumanStateMirror (A, the
    /// state's mirror on), HumanStateMirrorClip (the Mirror clip on a mirrored state),
    /// HumanStateMirrorOffset (the Offset clip mirrored), HumanStateMirrorB (B, 2/3 s long, mirrored),
    /// HumanStateMirrorOnce (the BakeOnce clip, no loop time, mirrored), HumanStateMirrorCycle (A
    /// mirrored with a state cycle offset of 0.25), HumanStateMirrorParam (A with mirror on
    /// and the bool parameter Mirror, default off, as its mirror parameter), HumanTreeMirror (a 1D
    /// tree of A at 0 and B mirrored at 1, blend 0.35), HumanTreeMirrorState (that tree on a
    /// mirrored state), HumanAdditiveMirror (A, B mirrored on an additive layer of weight 0.6),
    /// HumanTransitionMirror (A, then A mirrored after an exit time of 0.25 over 0.3 s),
    /// HumanAIKMirror (A mirrored on a layer with an IK pass) and HumanAIKClipMirror (the Mirror
    /// clip on a layer with an IK pass).</summary>
    static void MirrorStates()
    {
        var a = ClipOf("HumanA");
        var b = ClipOf("HumanB");
        AnimatorController Make(string name)
        {
            var path = Dir + name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) return null;
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        void Single(string name, AnimationClip clip, bool ik = false, System.Action<AnimatorController, AnimatorState> set = null)
        {
            var controller = Make(name);
            if (controller == null) return;
            var state = controller.layers[0].stateMachine.AddState("A");
            state.motion = clip;
            state.mirror = true;
            if (ik)
            {
                var layers = controller.layers;
                layers[0].iKPass = true;
                controller.layers = layers;
            }
            set?.Invoke(controller, state);
            EditorUtility.SetDirty(controller);
        }
        Single("HumanStateMirror", a);
        Single("HumanStateMirrorClip", ClipOf("HumanA", "Mirror"));
        Single("HumanStateMirrorOffset", ClipOf("HumanA", "Offset"));
        Single("HumanStateMirrorParam", a, set: (controller, state) =>
        {
            controller.AddParameter("Mirror", AnimatorControllerParameterType.Bool);
            state.mirrorParameterActive = true;
            state.mirrorParameter = "Mirror";
        });
        Single("HumanStateMirrorB", b);
        Single("HumanStateMirrorOnce", ClipOf("HumanA", "BakeOnce"));
        Single("HumanStateMirrorCycle", a, set: (_, state) => state.cycleOffset = 0.25f);
        Single("HumanAIKMirror", a, ik: true);
        Single("HumanAIKClipMirror", ClipOf("HumanA", "Mirror"), ik: true, set: (_, state) => state.mirror = false);
        foreach (var (name, mirrored) in new[] { ("HumanTreeMirror", false), ("HumanTreeMirrorState", true) })
        {
            var controller = Make(name);
            if (controller == null) continue;
            controller.AddParameter("Blend", AnimatorControllerParameterType.Float);
            var parameters = controller.parameters;
            parameters[0].defaultFloat = 0.35f;
            controller.parameters = parameters;
            var state = controller.CreateBlendTreeInController("Tree", out var blend, 0);
            state.mirror = mirrored;
            blend.blendParameter = "Blend";
            blend.AddChild(a, 0f);
            blend.AddChild(b, 1f);
            var children = blend.children;
            children[1].mirror = true;
            blend.children = children;
            EditorUtility.SetDirty(controller);
        }
        var additive = Make("HumanAdditiveMirror");
        if (additive != null)
        {
            additive.layers[0].stateMachine.AddState("A").motion = a;
            additive.AddLayer("B");
            var layers = additive.layers;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[1].defaultWeight = 0.6f;
            additive.layers = layers;
            var state = additive.layers[1].stateMachine.AddState("B");
            state.motion = b;
            state.mirror = true;
            EditorUtility.SetDirty(additive);
        }
        var transition = Make("HumanTransitionMirror");
        if (transition != null)
        {
            var machine = transition.layers[0].stateMachine;
            var first = machine.AddState("A");
            first.motion = a;
            var second = machine.AddState("AMirror");
            second.motion = a;
            second.mirror = true;
            machine.defaultState = first;
            var t = first.AddTransition(second);
            t.hasExitTime = true;
            t.exitTime = 0.25f;
            t.hasFixedDuration = true;
            t.duration = 0.3f;
            EditorUtility.SetDirty(transition);
        }
    }

    /// <summary>The first steps of states. Humanoid (A, B as in Blends): HumanExit{024,026,020,000,124}
    /// (A, then B after an exit time of 0.24, 0.26, 0.2, 0 and 1.24 over 0.3 s), HumanExit024Instant
    /// (exit time 0.24, no duration), HumanTwoStates (A and B, no transitions, for CrossFade and
    /// Play), HumanEmptyPartial (an empty state with B on an override layer of weight 0.5),
    /// HumanEmptyExit (an empty state, then A after an exit time of 0.5 over 0.3 s), HumanEmptyCond
    /// (an empty state, then A when the bool Go is set, over 0.3 s), HumanEvents{,Mirror,MirrorCycle}
    /// (HumanA's Events clip on a plain state, a mirrored one and a mirrored one with a cycle offset of
    /// 0.25) and HumanAdditiveMirrorClip (A with the Mirror clip on a mirrored state of an additive
    /// layer of weight 0.6). Generic (GenericMove G, 1 s, and GenericBack H, 0.8 s, moving and turning
    /// the root): GenericExit{024,025}, GenericTwoStates, GenericEmptyExit and GenericEmptyPartial like
    /// the humanoid ones.</summary>
    static void FirstSteps()
    {
        var a = ClipOf("HumanA");
        var b = ClipOf("HumanB");
        AnimatorController Make(string name)
        {
            var path = Dir + name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) return null;
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        AnimationClip Generic(string name, float length, (string attribute, float[] keys)[] curves)
        {
            var path = Dir + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;
            var clip = new AnimationClip();
            foreach (var (attribute, keys) in curves)
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < keys.Length; i += 2) curve.AddKey(keys[i], keys[i + 1]);
                clip.SetCurve("", typeof(Transform), attribute, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }
        var turnG = Quaternion.Euler(0f, 60f, 0f);
        var g = Generic("GenericMove", 1f, new[]
        {
            ("localPosition.x", new[] { 0f, 0f, 1f, 0.5f }),
            ("localPosition.y", new[] { 0f, 0f, 1f, 0f }),
            ("localPosition.z", new[] { 0f, 0f, 0.5f, 0.4f, 1f, 2f }),
            ("localRotation.x", new[] { 0f, 0f, 1f, 0f }),
            ("localRotation.y", new[] { 0f, 0f, 1f, turnG.y }),
            ("localRotation.z", new[] { 0f, 0f, 1f, 0f }),
            ("localRotation.w", new[] { 0f, 1f, 1f, turnG.w }),
        });
        var turnH = Quaternion.Euler(0f, -45f, 0f);
        var h = Generic("GenericBack", 0.8f, new[]
        {
            ("localPosition.x", new[] { 0f, 0f, 0.4f, 0.3f, 0.8f, 0f }),
            ("localPosition.y", new[] { 0f, 0f, 0.8f, 0f }),
            ("localPosition.z", new[] { 0f, 0f, 0.8f, -1f }),
            ("localRotation.x", new[] { 0f, 0f, 0.8f, 0f }),
            ("localRotation.y", new[] { 0f, 0f, 0.8f, turnH.y }),
            ("localRotation.z", new[] { 0f, 0f, 0.8f, 0f }),
            ("localRotation.w", new[] { 0f, 1f, 0.8f, turnH.w }),
        });

        void Exit(string name, Motion first, Motion second, float exitTime, float duration)
        {
            var controller = Make(name);
            if (controller == null) return;
            var machine = controller.layers[0].stateMachine;
            var from = machine.AddState(first != null ? "A" : "Empty");
            from.motion = first;
            var to = machine.AddState("B");
            to.motion = second;
            machine.defaultState = from;
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.hasFixedDuration = true;
            t.duration = duration;
            EditorUtility.SetDirty(controller);
        }
        foreach (var (suffix, exitTime) in new[] { ("024", 0.24f), ("026", 0.26f), ("020", 0.2f), ("000", 0f), ("124", 1.24f) })
            Exit("HumanExit" + suffix, a, b, exitTime, 0.3f);
        Exit("HumanExit024Instant", a, b, 0.24f, 0f);
        Exit("HumanEmptyExit", null, a, 0.5f, 0.3f);
        Exit("GenericExit024", g, h, 0.24f, 0.3f);
        Exit("GenericExit025", g, h, 0.25f, 0.3f);
        Exit("GenericEmptyExit", null, g, 0.5f, 0.3f);

        void Two(string name, Motion first, Motion second)
        {
            var controller = Make(name);
            if (controller == null) return;
            var machine = controller.layers[0].stateMachine;
            var from = machine.AddState("A");
            from.motion = first;
            machine.AddState("B").motion = second;
            machine.defaultState = from;
            EditorUtility.SetDirty(controller);
        }
        Two("HumanTwoStates", a, b);
        Two("GenericTwoStates", g, h);

        void Partial(string name, Motion over, float weight = 0.5f)
        {
            var controller = Make(name);
            if (controller == null) return;
            controller.layers[0].stateMachine.AddState("Empty");
            controller.AddLayer("B");
            var layers = controller.layers;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[1].defaultWeight = weight;
            controller.layers = layers;
            controller.layers[1].stateMachine.AddState("B").motion = over;
            EditorUtility.SetDirty(controller);
        }
        Partial("HumanEmptyPartial", b);
        Partial("GenericEmptyPartial", g);
        Partial("HumanEmptyPartialQuarter", b, 0.25f);

        // A motion state leaving into an empty state and back: A, then Empty after an exit time
        // of 0.2 (0.1 s), then A again (state Back) after an exit time of 0.3 of the empty state
        // (0.1 s).
        void Back(string name, Motion motion)
        {
            var controller = Make(name);
            if (controller == null) return;
            var machine = controller.layers[0].stateMachine;
            var first = machine.AddState("A");
            first.motion = motion;
            var empty = machine.AddState("Empty");
            var back = machine.AddState("Back");
            back.motion = motion;
            machine.defaultState = first;
            void Link(AnimatorState from, AnimatorState to, float exitTime)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = true;
                t.exitTime = exitTime;
                t.hasFixedDuration = true;
                t.duration = 0.1f;
            }
            Link(first, empty, 0.2f);
            Link(empty, back, 0.3f);
            EditorUtility.SetDirty(controller);
        }
        Back("HumanEmptyBack", a);
        Back("GenericEmptyBack", g);

        var cond = Make("HumanEmptyCond");
        if (cond != null)
        {
            cond.AddParameter("Go", AnimatorControllerParameterType.Bool);
            var machine = cond.layers[0].stateMachine;
            var empty = machine.AddState("Empty");
            var to = machine.AddState("A");
            to.motion = a;
            machine.defaultState = empty;
            var t = empty.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.3f;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Go");
            EditorUtility.SetDirty(cond);
        }

        var events = ClipOf("HumanA", "Events");
        foreach (var (name, mirror, cycle) in new[] { ("HumanEvents", false, 0f), ("HumanEventsMirror", true, 0f), ("HumanEventsMirrorCycle", true, 0.25f) })
        {
            var controller = Make(name);
            if (controller == null) continue;
            var state = controller.layers[0].stateMachine.AddState("A");
            state.motion = events;
            state.mirror = mirror;
            state.cycleOffset = cycle;
            EditorUtility.SetDirty(controller);
        }

        var additive = Make("HumanAdditiveMirrorClip");
        if (additive != null)
        {
            additive.layers[0].stateMachine.AddState("A").motion = a;
            additive.AddLayer("B");
            var layers = additive.layers;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[1].defaultWeight = 0.6f;
            additive.layers = layers;
            var state = additive.layers[1].stateMachine.AddState("B");
            state.motion = ClipOf("HumanA", "Mirror");
            state.mirror = true;
            EditorUtility.SetDirty(additive);
        }
    }

    /// <summary>The controls of target matching, bone rotations in the IK pass, translation DoF,
    /// loop pose on additive layers, feet without goal curves and layers that do not place the body:
    /// HumanT.fbx (HumanA's rig with bone translations) as Humanoid with translation DoF, its clip
    /// (Scene) and the clip mirrored (Mirror), with controllers HumanT and HumanTMirror;
    /// TDoFMuscles.anim and TDoFMirror.anim (muscles and translation DoF curves, the second mirrored)
    /// with controllers Human{name}; FeetNoGoals.anim (height from feet, no foot goal curves) and
    /// FeetLeftGoal.anim (only the left foot's goal curves) with controllers Human{name};
    /// HumanAdditiveLoopPose (A, LoopMuscles on an additive layer of weight 0.6) and
    /// HumanAdditiveLoopPoseModel (A, HumanA's LoopPose clip there); HumanNoRootOverEmpty (an empty
    /// base state), HumanNoRootOverGeneric (GenericMove in the base layer) and HumanNoRootNoWD (A,
    /// then after an exit time of 0.3 an empty state without write defaults), each with B on an
    /// override layer of weight 0.5 masked by MaskNoRoot; HumanAIK2 (A on two layers with an IK pass,
    /// the second an override of weight 1).</summary>
    static void Leftovers()
    {
        var a = ClipOf("HumanA");
        var b = ClipOf("HumanB");
        var tPath = Dir + "HumanT.fbx";
        var t = (ModelImporter)AssetImporter.GetAtPath(tPath);
        if (t.animationType != ModelImporterAnimationType.Human || !t.humanDescription.hasTranslationDoF)
        {
            t.animationType = ModelImporterAnimationType.Human;
            t.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            t.SaveAndReimport();
            var description = t.humanDescription;
            description.hasTranslationDoF = true;
            t.humanDescription = description;
            var source = t.defaultClipAnimations[0];
            source.name = "Scene";
            source.loopTime = true;
            var mirror = new ModelImporterClipAnimation
            {
                name = "Mirror", takeName = source.takeName, firstFrame = source.firstFrame, lastFrame = source.lastFrame,
                loopTime = true, keepOriginalPositionY = true, mirror = true,
            };
            t.clipAnimations = new[] { source, mirror };
            t.animationCompression = ModelImporterAnimationCompression.Off;
            t.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(tPath).OfType<Avatar>().FirstOrDefault();
            Debug.Log("PROBE HumanT avatar " + (avatar != null ? "valid " + avatar.isValid + " human " + avatar.isHuman
                + " tdof " + avatar.humanDescription.hasTranslationDoF : "none"));
        }
        foreach (var (name, clip) in new[] { ("HumanT", "Scene"), ("HumanTMirror", "Mirror") })
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + name + ".controller") == null)
                AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + name + ".controller", ClipOf("HumanT", clip));

        AnimationClip NewClip(string name, System.Action<System.Action<string, float[]>> curves, System.Action<AnimationClipSettings> settings)
        {
            var path = Dir + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;
            var clip = new AnimationClip();
            curves((attribute, keys) =>
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < keys.Length; i += 2) curve.AddKey(keys[i], keys[i + 1]);
                clip.SetCurve("", typeof(Animator), attribute, curve);
            });
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = true;
            settings?.Invoke(s);
            AnimationUtility.SetAnimationClipSettings(clip, s);
            AssetDatabase.CreateAsset(clip, path);
            AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + "Human" + name + ".controller", clip);
            return clip;
        }
        void TDoF(System.Action<string, float[]> set)
        {
            set("RootT.x", new[] { 0f, 0f, 1f, 0.2f });
            set("RootT.y", new[] { 0f, 1f, 1f, 1f });
            set("RootT.z", new[] { 0f, 0f, 1f, 0.4f });
            set("RootQ.w", new[] { 0f, 1f, 1f, 1f });
            set("Spine Front-Back", new[] { 0f, 0.1f, 1f, 0.4f });
            set("Left Upper Leg Front-Back", new[] { 0f, 0.2f, 1f, -0.3f });
            set("Right Arm Down-Up", new[] { 0f, -0.2f, 1f, 0.3f });
            set("SpineTDOF.x", new[] { 0f, 0.02f, 1f, -0.03f });
            set("SpineTDOF.y", new[] { 0f, 0.05f, 0.5f, -0.02f, 1f, 0.04f });
            set("SpineTDOF.z", new[] { 0f, -0.01f, 1f, 0.03f });
            set("NeckTDOF.y", new[] { 0f, 0.03f, 1f, -0.02f });
            set("LeftUpperLegTDOF.x", new[] { 0f, 0.04f, 1f, -0.02f });
            set("LeftUpperLegTDOF.z", new[] { 0f, -0.03f, 1f, 0.02f });
            set("RightUpperArmTDOF.x", new[] { 0f, 0.01f, 1f, 0.05f });
            set("RightUpperArmTDOF.y", new[] { 0f, -0.04f, 1f, 0.02f });
            set("LeftShoulderTDOF.z", new[] { 0f, 0.03f, 1f, -0.03f });
        }
        NewClip("TDoFMuscles", TDoF, null);
        NewClip("TDoFMirror", TDoF, s => s.mirror = true);
        void Feet(System.Action<string, float[]> set, bool left)
        {
            set("RootT.x", new[] { 0f, 0.1f, 1f, 0.4f });
            set("RootT.y", new[] { 0f, 1f, 0.5f, 0.97f, 1f, 1.05f });
            set("RootT.z", new[] { 0f, 0.2f, 1f, 0.6f });
            set("RootQ.w", new[] { 0f, 1f, 1f, 1f });
            set("Spine Front-Back", new[] { 0f, 0f, 1f, 0.4f });
            set("Left Upper Leg Front-Back", new[] { 0f, 0.3f, 1f, -0.2f });
            set("Right Lower Leg Stretch", new[] { 0f, 0.2f, 1f, 0.6f });
            if (!left) return;
            set("LeftFootT.x", new[] { 0f, -0.1f, 1f, -0.12f });
            set("LeftFootT.y", new[] { 0f, -0.75f, 1f, -0.8f });
            set("LeftFootT.z", new[] { 0f, 0.05f, 1f, 0f });
            set("LeftFootQ.w", new[] { 0f, 1f, 1f, 1f });
        }
        System.Action<AnimationClipSettings> fromFeet = s => { s.keepOriginalPositionY = false; s.heightFromFeet = true; };
        NewClip("FeetNoGoals", set => Feet(set, false), fromFeet);
        NewClip("FeetLeftGoal", set => Feet(set, true), fromFeet);

        AnimatorController Make(string name)
        {
            var path = Dir + name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) return null;
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        void Layer(AnimatorController controller, AnimationClip clip, AnimatorLayerBlendingMode mode, float weight, AvatarMask mask, bool ik = false)
        {
            controller.AddLayer("L" + controller.layers.Length);
            var layers = controller.layers;
            int i = layers.Length - 1;
            layers[i].blendingMode = mode;
            layers[i].defaultWeight = weight;
            layers[i].avatarMask = mask;
            layers[i].iKPass = ik;
            controller.layers = layers;
            controller.layers[i].stateMachine.AddState("S").motion = clip;
            EditorUtility.SetDirty(controller);
        }
        foreach (var (name, clip) in new[] { ("HumanAdditiveLoopPose", AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "LoopMuscles.anim")),
                     ("HumanAdditiveLoopPoseModel", ClipOf("HumanA", "LoopPose")) })
        {
            var controller = Make(name);
            if (controller == null) continue;
            controller.layers[0].stateMachine.AddState("A").motion = a;
            Layer(controller, clip, AnimatorLayerBlendingMode.Additive, 0.6f, null);
        }
        var noRoot = AssetDatabase.LoadAssetAtPath<AvatarMask>(Dir + "MaskNoRoot.mask");
        var empty = Make("HumanNoRootOverEmpty");
        if (empty != null)
        {
            empty.layers[0].stateMachine.AddState("Empty");
            Layer(empty, b, AnimatorLayerBlendingMode.Override, 0.5f, noRoot);
        }
        var generic = Make("HumanNoRootOverGeneric");
        if (generic != null)
        {
            generic.layers[0].stateMachine.AddState("Move").motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "GenericMove.anim");
            Layer(generic, b, AnimatorLayerBlendingMode.Override, 0.5f, noRoot);
        }
        var noWD = Make("HumanNoRootNoWD");
        if (noWD != null)
        {
            var machine = noWD.layers[0].stateMachine;
            var first = machine.AddState("A");
            first.motion = a;
            var hold = machine.AddState("Hold");
            hold.writeDefaultValues = false;
            machine.defaultState = first;
            var go = first.AddTransition(hold);
            go.hasExitTime = true;
            go.exitTime = 0.3f;
            go.hasFixedDuration = true;
            go.duration = 0f;
            Layer(noWD, b, AnimatorLayerBlendingMode.Override, 0.5f, noRoot);
        }
        // Empty states without write defaults after a motion (HumanA's clip after an exit time of
        // 0.3, at once and over 0.2 s; GenericMove likewise).
        void Hold(string name, Motion motion, float duration)
        {
            var controller = Make(name);
            if (controller == null) return;
            var machine = controller.layers[0].stateMachine;
            var first = machine.AddState("A");
            first.motion = motion;
            var hold = machine.AddState("Hold");
            hold.writeDefaultValues = false;
            machine.defaultState = first;
            var go = first.AddTransition(hold);
            go.hasExitTime = true;
            go.exitTime = 0.3f;
            go.hasFixedDuration = true;
            go.duration = duration;
            EditorUtility.SetDirty(controller);
        }
        Hold("HumanNoWDAlone", a, 0f);
        Hold("HumanNoWDFade", a, 0.2f);
        Hold("GenericNoWD", AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "GenericMove.anim"), 0f);
        var ik2 = Make("HumanAIK2");
        if (ik2 != null)
        {
            ik2.layers[0].stateMachine.AddState("A").motion = a;
            var layers = ik2.layers;
            layers[0].iKPass = true;
            ik2.layers = layers;
            Layer(ik2, a, AnimatorLayerBlendingMode.Override, 1f, null, ik: true);
        }
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
