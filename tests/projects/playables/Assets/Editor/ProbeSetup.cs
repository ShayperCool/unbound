using System;
using System.IO;
using Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Authors the probe's scene and assets with the Editor itself:
/// `Unity -batchmode -projectPath tests/projects/playables -executeMethod ProbeSetup.Build -quit`.
/// Clips, controller, mask, audio and the timeline go to Assets/Resources (scenarios load them);
/// the scene holds the timeline's director with its bindings and a rig for Animation Rigging.
/// </summary>
public static class ProbeSetup
{
    const string Res = "Assets/Resources/";

    public static void Build()
    {
        // A fresh scene first: opening one unloads assets nothing references yet.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.DeleteAsset("Assets/Resources");
        Directory.CreateDirectory(Res);

        WriteTone(Res + "Tone.wav", 1.0, 440.0);
        AssetDatabase.ImportAsset(Res + "Tone.wav");
        var tone = AssetDatabase.LoadAssetAtPath<AudioClip>(Res + "Tone.wav");

        var clipA = Clip("ClipA", loop: true, c =>
        {
            Curve(c, "", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 2));
            Curve(c, "", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, 1, 1));
            Curve(c, "", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0, 1, 0));
        });
        var clipB = Clip("ClipB", loop: false, c =>
        {
            Curve(c, "", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 2, -1));
            Curve(c, "", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0, 0, 2, 4));
            Curve(c, "", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0, 2, 2));
        });
        var clipRot = Clip("ClipRot", loop: true, c =>
        {
            Curve(c, "", typeof(Transform), "localEulerAnglesRaw.x", AnimationCurve.Constant(0, 1, 0));
            Curve(c, "", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0, 0, 1, 90));
            Curve(c, "", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Constant(0, 1, 0));
        });
        var clipChild = Clip("ClipChild", loop: true, c =>
        {
            Curve(c, "Child", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 1, 0));
            Curve(c, "Child", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
            Curve(c, "Child", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0, 1, 0));
            Curve(c, "Child", typeof(Transform), "m_LocalScale.x", AnimationCurve.Linear(0, 1, 1, 2));
            Curve(c, "Child", typeof(Transform), "m_LocalScale.y", AnimationCurve.Constant(0, 1, 1));
            Curve(c, "Child", typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0, 1, 1));
        });
        var clipEvent = Clip("ClipEvent", loop: false, c =>
        {
            Curve(c, "", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Linear(0, 0, 1, 1));
            Curve(c, "", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 1, 0));
            Curve(c, "", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, 1, 0));
            AnimationUtility.SetAnimationEvents(c, new[]
            {
                new AnimationEvent { time = 0.25f, functionName = "OnProbeEvent", floatParameter = 1f },
                new AnimationEvent { time = 0.75f, functionName = "OnProbeEvent", floatParameter = 2f },
            });
        });
        var clipActive = Clip("ClipActive", loop: false, c =>
        {
            Curve(c, "Child", typeof(GameObject), "m_IsActive", new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1), new Keyframe(1, 1)));
        });

        var controller = AnimatorController.CreateAnimatorControllerAtPath(Res + "Probe.controller");
        controller.AddParameter("go", AnimatorControllerParameterType.Trigger);
        var sm = controller.layers[0].stateMachine;
        var stateA = sm.AddState("A");
        stateA.motion = clipA;
        var stateB = sm.AddState("B");
        stateB.motion = clipB;
        var t = stateA.AddTransition(stateB);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = 0.25f;
        t.AddCondition(AnimatorConditionMode.If, 0, "go");
        sm.defaultState = stateA;

        var mask = new AvatarMask();
        mask.transformCount = 2;
        mask.SetTransformPath(0, "");
        mask.SetTransformActive(0, false);
        mask.SetTransformPath(1, "Child");
        mask.SetTransformActive(1, true);
        AssetDatabase.CreateAsset(mask, Res + "ChildOnly.mask");

        var ping = ScriptableObject.CreateInstance<SignalAsset>();
        AssetDatabase.CreateAsset(ping, Res + "Ping.signal");

        // ---------------------------------------------------------------- the scene

        var actor = new GameObject("Actor");
        var actorAnimator = actor.AddComponent<Animator>();
        new GameObject("Child").transform.SetParent(actor.transform, false);
        var flag = new GameObject("Flag");
        var speaker = new GameObject("Speaker").AddComponent<AudioSource>();
        speaker.playOnAwake = false;
        var receiverGo = new GameObject("Receiver");
        var receiverSink = receiverGo.AddComponent<SignalSink>();
        var receiver = receiverGo.AddComponent<SignalReceiver>();
        var reaction = new UnityEvent();
        UnityEventTools.AddStringPersistentListener(reaction, receiverSink.OnPing, "track");
        receiver.AddReaction(ping, reaction);
        var controlled = new GameObject("Controlled");
        controlled.SetActive(false);

        var directorGo = new GameObject("Director");
        var director = directorGo.AddComponent<PlayableDirector>();
        var directorSink = directorGo.AddComponent<SignalSink>();
        var directorReceiver = directorGo.AddComponent<SignalReceiver>();
        var markerReaction = new UnityEvent();
        UnityEventTools.AddStringPersistentListener(markerReaction, directorSink.OnPing, "marker");
        directorReceiver.AddReaction(ping, markerReaction);

        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, Res + "Probe.playable");
        var animTrack = timeline.CreateTrack<AnimationTrack>(null, "Anim");
        var a1 = animTrack.CreateClip(clipA);
        a1.start = 0.5;
        a1.duration = 1.0;
        var a2 = animTrack.CreateClip(clipB);
        a2.start = 1.25;
        a2.duration = 1.25;
        var actTrack = timeline.CreateTrack<ActivationTrack>(null, "Act");
        var act = actTrack.CreateDefaultClip();
        act.start = 1.0;
        act.duration = 1.0;
        var audioTrack = timeline.CreateTrack<AudioTrack>(null, "Audio");
        var au = audioTrack.CreateClip(tone);
        au.start = 0.75;
        var sigTrack = timeline.CreateTrack<SignalTrack>(null, "Signals");
        foreach (var at in new[] { 0.4, 1.6, 2.2 })
        {
            var e = sigTrack.CreateMarker<SignalEmitter>(at);
            e.asset = ping;
        }
        var ctlTrack = timeline.CreateTrack<ControlTrack>(null, "Control");
        var cc = ctlTrack.CreateDefaultClip();
        cc.start = 0.3;
        cc.duration = 1.5;
        var cpa = (ControlPlayableAsset)cc.asset;
        var source = cpa.sourceGameObject;
        source.exposedName = GUID.Generate().ToString();
        cpa.sourceGameObject = source;
        timeline.CreateMarkerTrack();
        var marker = timeline.markerTrack.CreateMarker<SignalEmitter>(1.0);
        marker.asset = ping;
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();

        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.Hold;
        director.SetGenericBinding(animTrack, actorAnimator);
        director.SetGenericBinding(actTrack, flag);
        director.SetGenericBinding(audioTrack, speaker);
        director.SetGenericBinding(sigTrack, receiverGo);
        director.SetReferenceValue(source.exposedName, controlled);

        BuildRig();
        BuildShots();

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    /// <summary>A camera with a Cinemachine brain, two virtual cameras, and a second director whose
    /// timeline cuts and blends between them on a Cinemachine track.</summary>
    static void BuildShots()
    {
        var cam = new GameObject("MainCam");
        cam.AddComponent<Camera>();
        var brain = cam.AddComponent<CinemachineBrain>();
        brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);
        var vcamA = new GameObject("VCamA").AddComponent<CinemachineVirtualCamera>();
        vcamA.transform.position = new Vector3(0, 1, -10);
        vcamA.m_Priority = 10;
        var vcamB = new GameObject("VCamB").AddComponent<CinemachineVirtualCamera>();
        vcamB.transform.position = new Vector3(6, 2, -8);
        vcamB.transform.rotation = Quaternion.Euler(0, -30, 0);
        vcamB.m_Priority = 5;

        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, Res + "Shots.playable");
        var track = timeline.CreateTrack<CinemachineTrack>(null, "Shots");
        var shotA = track.CreateClip<CinemachineShot>();
        shotA.start = 0;
        shotA.duration = 1.0;
        var shotB = track.CreateClip<CinemachineShot>();
        shotB.start = 0.6;
        shotB.duration = 1.0;
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();

        var go = new GameObject("ShotDirector");
        var director = go.AddComponent<PlayableDirector>();
        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.Hold;
        director.SetGenericBinding(track, brain);
        foreach (var (clip, vcam) in new[] { (shotA, vcamB), (shotB, vcamA) })
        {
            var shot = (CinemachineShot)clip.asset;
            var reference = shot.VirtualCamera;
            reference.exposedName = GUID.Generate().ToString();
            shot.VirtualCamera = reference;
            director.SetReferenceValue(reference.exposedName, vcam);
        }
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
    }

    /// <summary>A three-bone arm with a two-bone IK constraint and a head with an aim constraint,
    /// inactive until its scenario.</summary>
    static void BuildRig()
    {
        var root = new GameObject("RigRoot");
        root.AddComponent<Animator>();
        var builder = root.AddComponent<RigBuilder>();
        var bone1 = new GameObject("Bone1").transform;
        bone1.SetParent(root.transform, false);
        var bone2 = new GameObject("Bone2").transform;
        bone2.SetParent(bone1, false);
        bone2.localPosition = new Vector3(0, 1, 0);
        var bone3 = new GameObject("Bone3").transform;
        bone3.SetParent(bone2, false);
        bone3.localPosition = new Vector3(0, 1, 0);
        var head = new GameObject("Head").transform;
        head.SetParent(root.transform, false);
        head.localPosition = new Vector3(2, 0, 0);
        var ikTarget = new GameObject("IKTarget").transform;
        ikTarget.SetParent(root.transform, false);
        ikTarget.localPosition = new Vector3(1, 1, 0);
        var lookTarget = new GameObject("LookTarget").transform;
        lookTarget.SetParent(root.transform, false);
        lookTarget.localPosition = new Vector3(2, 0, 3);

        var rigGo = new GameObject("Rig");
        rigGo.transform.SetParent(root.transform, false);
        var rig = rigGo.AddComponent<Rig>();
        var ikGo = new GameObject("TwoBone");
        ikGo.transform.SetParent(rigGo.transform, false);
        var ik = ikGo.AddComponent<TwoBoneIKConstraint>();
        var ikData = ik.data;
        ikData.root = bone1;
        ikData.mid = bone2;
        ikData.tip = bone3;
        ikData.target = ikTarget;
        ikData.targetPositionWeight = 1f;
        ikData.targetRotationWeight = 0f;
        ikData.hintWeight = 0f;
        ik.data = ikData;
        var aimGo = new GameObject("Aim");
        aimGo.transform.SetParent(rigGo.transform, false);
        var aim = aimGo.AddComponent<MultiAimConstraint>();
        var aimData = aim.data;
        aimData.constrainedObject = head;
        var sources = new WeightedTransformArray(0);
        sources.Add(new WeightedTransform(lookTarget, 1f));
        aimData.sourceObjects = sources;
        aimData.aimAxis = MultiAimConstraintData.Axis.Z;
        aimData.upAxis = MultiAimConstraintData.Axis.Y;
        aimData.constrainedXAxis = aimData.constrainedYAxis = aimData.constrainedZAxis = true;
        aim.data = aimData;
        builder.layers.Add(new RigLayer(rig, true));
        root.SetActive(false);
    }

    static AnimationClip Clip(string name, bool loop, Action<AnimationClip> fill)
    {
        var c = new AnimationClip { name = name };
        fill(c);
        var settings = AnimationUtility.GetAnimationClipSettings(c);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(c, settings);
        AssetDatabase.CreateAsset(c, Res + name + ".anim");
        return c;
    }

    static void Curve(AnimationClip c, string path, Type type, string property, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve(path, type, property), curve);

    /// <summary>A 16-bit mono sine wave file.</summary>
    static void WriteTone(string path, double seconds, double frequency)
    {
        const int rate = 44100;
        int samples = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(path));
        w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        w.Write(36 + samples * 2);
        w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        w.Write(samples * 2);
        for (int i = 0; i < samples; i++)
            w.Write((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * 16000));
    }
}
