using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Writes the clip settings of Rig.fbx's .meta, the masks and the controllers of the
/// model clip probe (run once in a copy of the project; the results are committed), and records
/// the probe in Play mode.</summary>
public static class ProbeRunner
{
    const string Dir = "Assets/Resources/";
    const string Model = Dir + "Rig.fbx";
    const string Take = "Scene";

    static ModelImporterClipAnimation Clip(string name, float first = 0f, float last = 48f, bool loop = false)
    {
        return new ModelImporterClipAnimation
        {
            name = name, takeName = Take, firstFrame = first, lastFrame = last, loopTime = loop,
            maskType = ClipAnimationMaskType.None, keepOriginalPositionY = true,
        };
    }

    static Keyframe Linear(float t, float v, float inSlope, float outSlope) => new Keyframe(t, v, inSlope, outSlope);

    /// <summary>An AvatarMask over the model's transforms, with the named paths off.</summary>
    static AvatarMask MaskOf(GameObject model, params string[] off)
    {
        var mask = new AvatarMask();
        mask.AddTransformPath(model.transform, true);
        for (int i = 0; i < mask.transformCount; i++)
            mask.SetTransformActive(i, Array.IndexOf(off, mask.GetTransformPath(i)) < 0);
        return mask;
    }

    static void Generic(ModelImporter importer)
    {
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.importAnimation = true;
    }

    public static void Create()
    {
        try
        {
            // Remade from scratch: the masks and controllers of an earlier run go.
            AssetDatabase.DeleteAsset("Assets/Masks");
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Resources" }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var (file, compression) in new[] { ("RigDefault", ModelImporterAnimationCompression.Off),
                ("RigReduced", ModelImporterAnimationCompression.KeyframeReduction), ("RigOptimal", ModelImporterAnimationCompression.Optimal) })
            {
                var defaults = (ModelImporter)AssetImporter.GetAtPath(Dir + file + ".fbx");
                Generic(defaults);
                defaults.animationCompression = compression;
                defaults.clipAnimations = new ModelImporterClipAnimation[0];
                defaults.SaveAndReimport();
            }

            var still = (ModelImporter)AssetImporter.GetAtPath(Dir + "RigStatic.fbx");
            Generic(still);
            still.importAnimation = false;
            still.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            AssetDatabase.CreateFolder("Assets", "Masks");
            var noProp = MaskOf(model, "Prop");
            AssetDatabase.CreateAsset(noProp, "Assets/Masks/NoProp.mask");
            var noHips = MaskOf(model, "Hips");
            AssetDatabase.CreateAsset(noHips, "Assets/Masks/NoHips.mask");

            var clips = new List<ModelImporterClipAnimation>
            {
                Clip("Full"),
                Clip("Loop", loop: true),
                Clip("Range", 6f, 30f, true),
                Clip("RangeOnce", 6f, 30f),
                Clip("Fraction", 4.5f, 28.5f, true),
                Clip("Beyond", 40f, 60f, true),
            };
            var loopPose = Clip("LoopPose", loop: true); loopPose.loopPose = true; clips.Add(loopPose);
            var loopPoseRange = Clip("LoopPoseRange", 6f, 30f, true); loopPoseRange.loopPose = true; clips.Add(loopPoseRange);
            var loopPoseOnce = Clip("LoopPoseOnce", 6f, 30f); loopPoseOnce.loopPose = true; clips.Add(loopPoseOnce);
            var cycle = Clip("Cycle", loop: true); cycle.cycleOffset = 0.25f; clips.Add(cycle);
            var cycleOnce = Clip("CycleOnce"); cycleOnce.cycleOffset = 0.25f; clips.Add(cycleOnce);
            var cycleNeg = Clip("CycleNeg", 6f, 30f, true); cycleNeg.cycleOffset = -0.4f; clips.Add(cycleNeg);
            var cyclePose = Clip("CyclePose", 6f, 30f, true); cyclePose.cycleOffset = 0.5f; cyclePose.loopPose = true; clips.Add(cyclePose);
            var mirror = Clip("Mirror", loop: true); mirror.mirror = true; clips.Add(mirror);

            var maskModel = Clip("MaskModel", loop: true);
            maskModel.ConfigureClipFromMask(MaskOf(model, "Hips/Spine"));
            maskModel.maskType = ClipAnimationMaskType.CreateFromThisModel;
            clips.Add(maskModel);
            var maskAsset = Clip("MaskAsset", loop: true);
            maskAsset.ConfigureClipFromMask(noProp);
            maskAsset.maskType = ClipAnimationMaskType.CopyFromOther;
            maskAsset.maskSource = noProp;
            clips.Add(maskAsset);
            // The copy in the .meta differs from its source: which one the importer uses.
            var maskStale = Clip("MaskStale", loop: true);
            maskStale.ConfigureClipFromMask(noHips);
            maskStale.maskType = ClipAnimationMaskType.CopyFromOther;
            maskStale.maskSource = noProp;
            clips.Add(maskStale);
            // A transform mask kept while the mask type is None.
            var maskNone = Clip("MaskNone", loop: true);
            maskNone.ConfigureClipFromMask(noProp);
            maskNone.maskType = ClipAnimationMaskType.None;
            clips.Add(maskNone);

            var addRef = Clip("AddRef", loop: true); addRef.hasAdditiveReferencePose = true; addRef.additiveReferencePoseFrame = 12f; clips.Add(addRef);
            var addRefRange = Clip("AddRefRange", 6f, 30f, true); addRefRange.hasAdditiveReferencePose = true; addRefRange.additiveReferencePoseFrame = 18f; clips.Add(addRefRange);
            var addRefOut = Clip("AddRefOut", 6f, 30f, true); addRefOut.hasAdditiveReferencePose = true; addRefOut.additiveReferencePoseFrame = 40f; clips.Add(addRefOut);
            var addRefOff = Clip("AddRefOff", 6f, 30f, true); addRefOff.hasAdditiveReferencePose = false; addRefOff.additiveReferencePoseFrame = 18f; clips.Add(addRefOff);

            var curves = Clip("Curves", loop: true);
            curves.curves = new[]
            {
                new ClipAnimationInfoCurve { name = "Speed", curve = new AnimationCurve(Linear(0f, 0f, 4f, 4f), Linear(0.5f, 2f, 4f, -2f), Linear(1f, 1f, -2f, -2f)) },
                new ClipAnimationInfoCurve { name = "Count", curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 7f)) },
                new ClipAnimationInfoCurve { name = "On", curve = new AnimationCurve(Linear(0f, 0f, 2f, 2f), Linear(1f, 2f, 2f, 2f)) },
                new ClipAnimationInfoCurve { name = "Missing", curve = new AnimationCurve(Linear(0f, 5f, 0f, 0f), Linear(1f, 5f, 0f, 0f)) },
            };
            clips.Add(curves);
            var curvesRange = Clip("CurvesRange", 6f, 30f, true);
            curvesRange.curves = new[] { new ClipAnimationInfoCurve { name = "Speed", curve = new AnimationCurve(Linear(0f, 1f, 3f, 3f), Linear(1f, 4f, 3f, 3f)) } };
            clips.Add(curvesRange);
            var curvesPose = Clip("CurvesPose", 6f, 30f, true);
            curvesPose.loopPose = true;
            curvesPose.curves = new[] { new ClipAnimationInfoCurve { name = "Speed", curve = new AnimationCurve(Linear(0f, 1f, 3f, 3f), Linear(1f, 4f, 3f, 3f)) } };
            clips.Add(curvesPose);

            var events = Clip("Events", loop: true);
            events.events = new[]
            {
                new AnimationEvent { time = 0.25f, functionName = "Hit", floatParameter = 1.5f },
                new AnimationEvent { time = 0.6f, functionName = "Ping", stringParameter = "x", intParameter = 3, floatParameter = 0.5f },
            };
            clips.Add(events);
            var eventsRange = Clip("EventsRange", 6f, 30f, true);
            eventsRange.events = new[] { new AnimationEvent { time = 0.5f, functionName = "Hit", floatParameter = 2f } };
            clips.Add(eventsRange);
            var eventsCycle = Clip("EventsCycle", loop: true);
            eventsCycle.cycleOffset = 0.25f;
            eventsCycle.events = events.events;
            clips.Add(eventsCycle);
            var eventsOnce = Clip("EventsOnce");
            eventsOnce.events = events.events;
            clips.Add(eventsOnce);
            var wrap = Clip("Wrap"); wrap.wrapMode = WrapMode.PingPong; clips.Add(wrap);

            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            Generic(importer);
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();

            foreach (var c in clips) Single("C_" + c.name, Load(c.name));
            Single("C_Default", AssetDatabase.LoadAllAssetsAtPath(Dir + "RigDefault.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__")));
            foreach (var name in new[] { "AddRef", "AddRefRange", "AddRefOut", "AddRefOff", "Range" })
                Layered("A_" + name, Load(name), additive: true);
            foreach (var name in new[] { "MaskModel", "MaskAsset", "MaskStale", "MaskNone" })
                Layered("O_" + name, Load(name), additive: false);
            // The state's cycle offset with the clip's, on a looping and a non-looping clip.
            foreach (var name in new[] { "Cycle", "CycleOnce", "Loop", "Full", "Events", "EventsOnce" })
                Single("S_" + name, Load(name), 0.1f);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static AnimationClip Load(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().First(c => c.name == name);

    static AnimatorController Controller(string name)
    {
        var c = AnimatorController.CreateAnimatorControllerAtPath(Dir + name + ".controller");
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Count", AnimatorControllerParameterType.Int);
        c.AddParameter("On", AnimatorControllerParameterType.Bool);
        return c;
    }

    static void Single(string name, AnimationClip clip, float stateCycleOffset = 0f)
    {
        var c = Controller(name);
        var state = c.layers[0].stateMachine.AddState(clip.name);
        state.motion = clip;
        state.cycleOffset = stateCycleOffset;
    }

    /// <summary>The Full clip in the base layer, the clip in a second layer at weight 1.</summary>
    static void Layered(string name, AnimationClip clip, bool additive)
    {
        var c = Controller(name);
        c.layers[0].stateMachine.AddState("Full").motion = Load("Full");
        c.AddLayer("Top");
        var layers = c.layers;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = additive ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override;
        c.layers = layers;
        c.layers[1].stateMachine.AddState(clip.name).motion = clip;
    }

    public static void Record()
    {
        SessionState.SetFloat("modelclips.deadline", (float)EditorApplication.timeSinceStartup + 600f);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}

/// <summary>Watchdog of a recording (re-armed after every domain reload): the probe exits after
/// writing its output; fail if it never does.</summary>
[InitializeOnLoad]
static class ProbeWatchdog
{
    static ProbeWatchdog()
    {
        if (SessionState.GetFloat("modelclips.deadline", 0f) <= 0f) return;
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat("modelclips.deadline", 0f))
            {
                Debug.LogError("modelclips: watchdog expired");
                EditorApplication.Exit(2);
            }
        };
    }
}
