using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;

/// <summary>The scene's timeline (animation, activation, audio, signal and control tracks and a
/// marker) played by its PlayableDirector: evaluated at set times, and played over frames.</summary>
public static class ProbeTimeline
{
    public static PlayableDirector Director => GameObject.Find("Director")?.GetComponent<PlayableDirector>();

    static GameObject Find(string name)
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go.name == name && go.scene.IsValid()) return go;
        return null;
    }

    public static string State(PlayableDirector d)
    {
        var actor = Find("Actor").transform;
        var b = new StringBuilder();
        b.Append($"time={ProbeLog.F(d.time)} state={d.state} graph={d.playableGraph.IsValid()} ");
        b.Append($"actor={ProbeAnimation.V(actor.localPosition)} flag={Find("Flag").activeSelf} controlled={Find("Controlled").activeSelf}");
        var g = d.playableGraph;
        if (g.IsValid())
        {
            for (int i = 0; i < g.GetOutputCount(); i++)
            {
                var o = g.GetOutput(i);
                if (!o.IsPlayableOutputOfType<AudioPlayableOutput>()) continue;
                var seen = new HashSet<PlayableHandle>();
                Collect(o.GetSourcePlayable(), o.GetSourceOutputPort(), seen, b, 0);
            }
        }
        return b.ToString();
    }

    /// <summary>With PLAYABLES_DEBUG set: the director graph's trees, for finding differences.</summary>
    public static void Dump(PlayableDirector d)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAYABLES_DEBUG"))) return;
        var g = d.playableGraph;
        if (!g.IsValid()) return;
        var b = new StringBuilder();
        for (int i = 0; i < g.GetOutputCount(); i++)
        {
            var o = g.GetOutput(i);
            b.Append($"output {i} {o.GetPlayableOutputType()?.Name} w={ProbeLog.F(o.GetWeight())}\n");
            DumpNode(o.GetSourcePlayable(), o.GetSourceOutputPort(), 1f, b, 1);
        }
        Debug.Log(b.ToString());
    }

    static void DumpNode(Playable p, int port, float w, StringBuilder b, int depth)
    {
        if (depth > 12) return;
        if (!p.IsValid()) { b.Append(new string(' ', depth * 2)).Append("null\n"); return; }
        b.Append(new string(' ', depth * 2)).Append($"{p.GetPlayableType()?.Name} w={ProbeLog.F(w)} t={ProbeLog.F(p.GetTime())} ps={p.GetPlayState()} in={p.GetInputCount()} mode={p.GetTraversalMode()}\n");
        if (p.GetTraversalMode() == PlayableTraversalMode.Passthrough)
        {
            if (port < p.GetInputCount()) DumpNode(p.GetInput(port), 0, p.GetInputWeight(port), b, depth + 1);
            return;
        }
        for (int i = 0; i < p.GetInputCount(); i++) DumpNode(p.GetInput(i), 0, p.GetInputWeight(i), b, depth + 1);
    }

    static void Collect(Playable p, int port, HashSet<PlayableHandle> seen, StringBuilder b, int depth)
    {
        if (!p.IsValid() || depth > 16) return;
        if (p.IsPlayableOfType<AudioClipPlayable>())
        {
            if (!seen.Add(p.GetHandle())) return;
            var a = (AudioClipPlayable)p;
            b.Append($" audio[t={ProbeLog.F(a.GetTime())} ps={a.GetPlayState()} clip={(a.GetClip() != null ? a.GetClip().name : "null")} loop={a.GetLooped()}]");
            return;
        }
        if (p.GetTraversalMode() == PlayableTraversalMode.Passthrough)
        {
            if (port < p.GetInputCount()) Collect(p.GetInput(port), 0, seen, b, depth + 1);
            return;
        }
        for (int i = 0; i < p.GetInputCount(); i++) Collect(p.GetInput(i), 0, seen, b, depth + 1);
    }

    /// <summary>director.time set, then Evaluate, at times over the whole timeline and back.</summary>
    public static void Evaluate()
    {
        ProbeLog.Begin("timeline.evaluate");
        TraceBehaviour.Reset();
        try
        {
            var d = Director;
            SignalSink.Director = d;
            SignalSink.Logging = true;
            ProbeLog.Event($"asset={d.playableAsset?.name} duration={ProbeLog.F(d.duration)} wrap={d.extrapolationMode} update={d.timeUpdateMode} playOnAwake={d.playOnAwake} state={d.state} graph={d.playableGraph.IsValid()}");
            ProbeLog.Event("initial: " + State(d));
            var times = new List<double>();
            for (double t = 0; t <= 2.7; t += 0.1) times.Add(Math.Round(t, 3));
            times.AddRange(new[] { 1.3, 0.45, 1.65, 0.0 });
            foreach (var t in times)
            {
                d.time = t;
                d.Evaluate();
                if (t == 0.0 || t == 1.3) Dump(d);
                ProbeLog.Event(State(d));
            }
            var g = d.playableGraph;
            ProbeLog.Event($"graph: outputs={g.GetOutputCount()} playables={g.GetPlayableCount()} roots={g.GetRootPlayableCount()} mode={g.GetTimeUpdateMode()} playing={g.IsPlaying()}");
            for (int i = 0; i < g.GetOutputCount(); i++)
            {
                var o = g.GetOutput(i);
                var reference = o.GetReferenceObject();
                var user = o.GetUserData();
                ProbeLog.Event($"output {i}: {o.GetPlayableOutputType()?.Name} ref={(reference != null ? reference.name : "null")} user={(user != null ? user.name : "null")} weight={ProbeLog.F(o.GetWeight())} port={o.GetSourceOutputPort()} receivers={o.GetNotificationReceivers().Length}");
            }
            d.Stop();
            ProbeLog.Event("stopped: " + State(d));
            SignalSink.Logging = false;
        }
        catch (Exception e) { ProbeLog.Event("exception " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
    }
}

/// <summary>A Cinemachine track cutting and blending between two virtual cameras on a brain:
/// the camera's pose after the LateUpdate phase (the brain's update) of each frame.</summary>
public class ShotsScenario : FrameScenario
{
    public override string Name => "cine.frames";
    PlayableDirector m_Director;
    Transform m_Camera;

    void Sample(string phase)
    {
        if (phase != "afterScriptRunBehaviourLateUpdate") return;
        ProbeLog.Event($"{FramePhases.Prefix()}time={ProbeLog.F(m_Director.time)} camera={ProbeAnimation.V(m_Camera.position)} rot={ProbeAnimation.V(m_Camera.eulerAngles)}");
    }

    public override void Begin()
    {
        TraceBehaviour.Prefix = FramePhases.Prefix;
        m_Director = GameObject.Find("ShotDirector").GetComponent<PlayableDirector>();
        m_Camera = GameObject.Find("MainCam").transform;
        FramePhases.Watchers.Add(Sample);
        m_Director.Play();
    }

    public override bool Frame(int k)
    {
        if (k < 100) return false;
        FramePhases.Watchers.Remove(Sample);
        m_Director.Stop();
        return true;
    }
}

/// <summary>The director playing the timeline over frames (50 per second), sampled after the
/// animation phase of each frame, then stopped.</summary>
public class TimelinePlayScenario : FrameScenario
{
    public override string Name => "timeline.play";
    PlayableDirector m_Director;

    public override void Begin()
    {
        TraceBehaviour.Prefix = FramePhases.Prefix;
        m_Director = ProbeTimeline.Director;
        SignalSink.Director = m_Director;
        SignalSink.Logging = true;
        m_Director.played += d => ProbeLog.Event(FramePhases.Prefix() + "played");
        m_Director.paused += d => ProbeLog.Event(FramePhases.Prefix() + "paused");
        m_Director.stopped += d => ProbeLog.Event(FramePhases.Prefix() + "stopped");
        FramePhases.Watchers.Add(Sample);
        ProbeLog.Event(FramePhases.Prefix() + "Play");
        m_Director.Play();
        ProbeLog.Event(FramePhases.Prefix() + "after Play: " + ProbeTimeline.State(m_Director));
    }

    void Sample(string phase)
    {
        if (phase == "ScriptRunBehaviourLateUpdate") ProbeLog.Event(FramePhases.Prefix() + ProbeTimeline.State(m_Director));
    }

    public override bool Frame(int k)
    {
        if (k == 60)
        {
            ProbeLog.Event(FramePhases.Prefix() + "Pause");
            m_Director.Pause();
        }
        if (k == 65)
        {
            ProbeLog.Event(FramePhases.Prefix() + "Resume");
            m_Director.Resume();
        }
        if (k == 150)
        {
            ProbeLog.Event(FramePhases.Prefix() + "Stop");
            m_Director.Stop();
            ProbeLog.Event(FramePhases.Prefix() + "after Stop: " + ProbeTimeline.State(m_Director));
        }
        if (k == 152)
        {
            FramePhases.Watchers.Remove(Sample);
            SignalSink.Logging = false;
            return true;
        }
        return false;
    }
}
