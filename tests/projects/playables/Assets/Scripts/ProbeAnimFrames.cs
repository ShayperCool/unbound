using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
using UnityEngine.Audio;
using UnityEngine.Playables;

/// <summary>An animation graph the player loop plays: in which phase its script playables run and
/// the Animator writes, next to an Animator with a controller.</summary>
public class AnimFramesScenario : FrameScenario
{
    public override string Name => "anim.frames";
    PlayableGraph m_Graph;
    Animator m_Actor, m_Controlled;

    void Watch(string phase)
    {
        if (phase == "afterDirectorUpdate" || phase == "afterDirectorUpdateAnimationBegin" || phase == "afterDirectorUpdateAnimationEnd" || phase == "ScriptRunBehaviourLateUpdate")
            ProbeLog.Event($"{FramePhases.Prefix()}actor x={ProbeLog.F(m_Actor.transform.localPosition.x)} controlled x={ProbeLog.F(m_Controlled.transform.localPosition.x)}");
    }

    public override void Begin()
    {
        TraceBehaviour.Prefix = FramePhases.Prefix;
        m_Actor = ProbeAnimation.MakeActor("FramesActor");
        m_Controlled = ProbeAnimation.MakeActor("FramesControlled");
        m_Controlled.runtimeAnimatorController = ProbeAnimation.Load<RuntimeAnimatorController>("Probe");
        m_Graph = PlayableGraph.Create("animFrames");
        m_Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(m_Graph, "anim", m_Actor);
        TraceBehaviour.Outputs.Add((output, "anim"));
        var s = TraceBehaviour.Create(m_Graph, "S", 1);
        var clip = AnimationClipPlayable.Create(m_Graph, ProbeAnimation.Load<AnimationClip>("ClipA"));
        m_Graph.Connect(clip, 0, s, 0);
        s.SetInputWeight(0, 1f);
        output.SetSourcePlayable(s);
        m_Graph.Play();
        FramePhases.Watchers.Add(Watch);
    }

    public override bool Frame(int k)
    {
        if (k == 4)
        {
            ProbeLog.Event(FramePhases.Prefix() + "stop");
            m_Graph.Stop();
        }
        if (k == 6)
        {
            ProbeLog.Event(FramePhases.Prefix() + "destroy");
            FramePhases.Watchers.Remove(Watch);
            m_Graph.Destroy();
            UnityEngine.Object.Destroy(m_Actor.gameObject);
            UnityEngine.Object.Destroy(m_Controlled.gameObject);
            return true;
        }
        return false;
    }
}

/// <summary>Animation Rigging: a two-bone IK and an aim constraint following moving targets.</summary>
public class RigFramesScenario : FrameScenario
{
    public override string Name => "rig.frames";
    GameObject m_Root;
    Transform m_IK, m_Look, m_Bone1, m_Bone2, m_Bone3, m_Head;

    static GameObject FindInactive(string name)
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go.name == name && go.scene.IsValid()) return go;
        return null;
    }

    static string R(Quaternion q) => $"({ProbeLog.F(q.x)}, {ProbeLog.F(q.y)}, {ProbeLog.F(q.z)}, {ProbeLog.F(q.w)})";

    void Watch(string phase)
    {
        if (phase != "ScriptRunBehaviourLateUpdate") return;
        ProbeLog.Event($"{FramePhases.Prefix()}bone2={ProbeAnimation.V(m_Bone2.position)} bone3={ProbeAnimation.V(m_Bone3.position)} bone1rot={R(m_Bone1.rotation)} head={R(m_Head.rotation)}");
    }

    public override void Begin()
    {
        TraceBehaviour.Prefix = FramePhases.Prefix;
        m_Root = FindInactive("RigRoot");
        var t = m_Root.transform;
        m_IK = t.Find("IKTarget");
        m_Look = t.Find("LookTarget");
        m_Bone1 = t.Find("Bone1");
        m_Bone2 = m_Bone1.Find("Bone2");
        m_Bone3 = m_Bone2.Find("Bone3");
        m_Head = t.Find("Head");
        m_Root.SetActive(true);
        var builder = m_Root.GetComponent<RigBuilder>();
        ProbeLog.Event($"graph valid={builder.graph.IsValid()} playing={builder.graph.IsValid() && builder.graph.IsPlaying()} outputs={(builder.graph.IsValid() ? builder.graph.GetOutputCount() : -1)}");
        FramePhases.Watchers.Add(Watch);
    }

    public override bool Frame(int k)
    {
        float a = k * 0.15f;
        m_IK.localPosition = new Vector3(1f + 0.3f * Mathf.Sin(a), 1f + 0.4f * Mathf.Cos(a), 0.2f * k / 10f);
        m_Look.localPosition = new Vector3(2f + 3f * Mathf.Sin(a), 0.5f, 3f * Mathf.Cos(a));
        if (k == 20)
        {
            FramePhases.Watchers.Remove(Watch);
            m_Root.SetActive(false);
            return true;
        }
        return false;
    }
}

/// <summary>Audio playables evaluated synchronously: clip playable time, looping, delay and seek.</summary>
public static class ProbeAudio
{
    public static void RunAll()
    {
        if (!ProbeEntry.Wanted("audio.eval")) return;
        ProbeLog.Begin("audio.eval");
        TraceBehaviour.Reset();
        try
        {
            var go = new GameObject("AudioProbe");
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var tone = ProbeAnimation.Load<AudioClip>("Tone");
            var g = PlayableGraph.Create("audio");
            var output = AudioPlayableOutput.Create(g, "audio", source);
            ProbeLog.Event($"target={output.GetTarget().name} evaluateOnSeek={output.GetEvaluateOnSeek()} clipLength={ProbeLog.F(tone.length)}");
            var clip = AudioClipPlayable.Create(g, tone, false);
            var mixer = AudioMixerPlayable.Create(g, 1);
            mixer.ConnectInput(0, clip, 0, 1f);
            output.SetSourcePlayable(mixer);
            ProbeLog.Event($"clip={clip.GetClip().name} looped={clip.GetLooped()} duration={ProbeLog.F(clip.GetDuration())} delay={ProbeLog.F(clip.GetStartDelay())} pauseDelay={ProbeLog.F(clip.GetPauseDelay())}");
            foreach (var dt in new[] { 0f, 0.25f, 0.5f, 0.5f, 0.5f })
            {
                g.Evaluate(dt);
                ProbeLog.Event($"+{ProbeLog.F(dt)}: t={ProbeLog.F(clip.GetTime())} done={clip.IsDone()} ps={clip.GetPlayState()} graphDone={g.IsDone()}");
            }
            clip.SetLooped(true);
            clip.SetDone(false);
            clip.SetTime(0.8);
            foreach (var dt in new[] { 0f, 0.5f })
            {
                g.Evaluate(dt);
                ProbeLog.Event($"looped +{ProbeLog.F(dt)}: t={ProbeLog.F(clip.GetTime())} done={clip.IsDone()}");
            }
            clip.Seek(0.2, 0.1, 0.5);
            ProbeLog.Event($"seek: t={ProbeLog.F(clip.GetTime())} delay={ProbeLog.F(clip.GetStartDelay())} duration={ProbeLog.F(clip.GetDuration())} done={clip.IsDone()}");
            g.Evaluate(0.25f);
            ProbeLog.Event($"seek +0.25: t={ProbeLog.F(clip.GetTime())} done={clip.IsDone()}");
            g.Destroy();
            UnityEngine.Object.DestroyImmediate(go);
        }
        catch (Exception e) { ProbeLog.Event("exception " + e.GetType().Name + ": " + e.Message); }
    }
}
