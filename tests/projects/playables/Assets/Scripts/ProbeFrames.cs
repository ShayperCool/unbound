using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Playables;
using PL = UnityEngine.PlayerLoop;

/// <summary>A scenario that runs over frames: Begin in the first frame's Update, then Frame(k)
/// in each later Update until it returns true.</summary>
public abstract class FrameScenario
{
    public abstract string Name { get; }
    public virtual void Begin() { }
    public abstract bool Frame(int k);
}

/// <summary>
/// Marks where in the player loop things happen: a marker system runs before each Director system
/// (and the script update phases) and names the phase that events are logged in.
/// </summary>
public static class FramePhases
{
    public static string Phase = "-";
    public static int StartFrame;
    public static readonly List<Action<string>> Watchers = new List<Action<string>>();

    static readonly Type[] s_Marked =
    {
        typeof(PL.Initialization.DirectorSampleTime),
        typeof(PL.FixedUpdate.DirectorFixedSampleTime),
        typeof(PL.FixedUpdate.DirectorFixedUpdate),
        typeof(PL.FixedUpdate.DirectorFixedUpdatePostPhysics),
        typeof(PL.Update.ScriptRunBehaviourUpdate),
        typeof(PL.Update.DirectorUpdate),
        typeof(PL.PreLateUpdate.DirectorUpdateAnimationBegin),
        typeof(PL.PreLateUpdate.DirectorUpdateAnimationEnd),
        typeof(PL.PreLateUpdate.DirectorDeferredEvaluate),
        typeof(PL.PreLateUpdate.ScriptRunBehaviourLateUpdate),
        typeof(PL.PostLateUpdate.DirectorLateUpdate),
        typeof(PL.PostLateUpdate.DirectorRenderImage),
    };

    static bool s_Installed;

    public static void Install()
    {
        if (s_Installed) return;
        s_Installed = true;
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i];
            if (phase.subSystemList == null) continue;
            var list = new List<PlayerLoopSystem>();
            foreach (var s in phase.subSystemList)
            {
                if (Array.IndexOf(s_Marked, s.type) >= 0)
                {
                    string name = s.type.Name;
                    list.Add(new PlayerLoopSystem
                    {
                        type = typeof(FramePhases),
                        updateDelegate = () => Enter(name),
                    });
                }
                list.Add(s);
                if (Array.IndexOf(s_Marked, s.type) >= 0)
                {
                    string after = "after" + s.type.Name;
                    list.Add(new PlayerLoopSystem
                    {
                        type = typeof(FramePhases),
                        updateDelegate = () => Enter(after),
                    });
                }
            }
            phase.subSystemList = list.ToArray();
            loop.subSystemList[i] = phase;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void Enter(string name)
    {
        Phase = name;
        foreach (var w in Watchers.ToArray()) w(name);
    }

    public static string Prefix() => $"[{Time.frameCount - StartFrame} {Phase}] ";
}

/// <summary>Script playables in graphs that the player loop updates.</summary>
public class ScriptFramesScenario : FrameScenario
{
    public override string Name => "frames.script";
    PlayableGraph m_Game, m_Manual;
    ScriptPlayable<TraceBehaviour> m_Root;

    PlayableGraph Make(string name, DirectorUpdateMode mode, string prefix)
    {
        var graph = PlayableGraph.Create(name);
        graph.SetTimeUpdateMode(mode);
        var o = ScriptPlayableOutput.Create(graph, prefix);
        TraceBehaviour.Outputs.Add((o, prefix));
        var ud = ScriptableObject.CreateInstance<UserData>();
        ud.name = "user" + prefix;
        o.SetUserData(ud);
        var root = TraceBehaviour.Create(graph, prefix + ".root", 1);
        var child = TraceBehaviour.Create(graph, prefix + ".child");
        root.ConnectInput(0, child, 0, 1f);
        o.SetSourcePlayable(root);
        if (mode == DirectorUpdateMode.GameTime) m_Root = root;
        return graph;
    }

    public override void Begin()
    {
        TraceBehaviour.Prefix = FramePhases.Prefix;
        m_Game = Make("game", DirectorUpdateMode.GameTime, "G");
        m_Manual = Make("manual", DirectorUpdateMode.Manual, "M");
        ProbeLog.Event(FramePhases.Prefix() + "Play all");
        m_Game.Play();
        m_Manual.Play();
    }

    public override bool Frame(int k)
    {
        string p = FramePhases.Prefix();
        switch (k)
        {
            case 2:
                ProbeLog.Event(p + "G root pause");
                m_Root.Pause();
                break;
            case 3:
                ProbeLog.Event(p + "G root play, timeScale 0.5");
                m_Root.Play();
                Time.timeScale = 0.5f;
                break;
            case 4:
                ProbeLog.Event(p + "G stop, timeScale 1");
                Time.timeScale = 1f;
                m_Game.Stop();
                break;
            case 5:
                ProbeLog.Event(p + "G play, M evaluate");
                m_Game.Play();
                m_Manual.Evaluate(0.5f);
                break;
            case 6:
                ProbeLog.Event(p + "destroy all");
                m_Game.Destroy();
                m_Manual.Destroy();
                break;
            case 8:
                return true;
        }
        return false;
    }
}

/// <summary>Runs the frame scenarios one after the other, then writes the probe and quits.</summary>
public class FrameRunner : MonoBehaviour
{
    readonly Queue<FrameScenario> m_Pending = new Queue<FrameScenario>();
    FrameScenario m_Current;
    int m_Start;
    public Action Finished;

    public void Add(FrameScenario s) => m_Pending.Enqueue(s);

    void Update()
    {
        if (m_Current == null)
        {
            if (m_Pending.Count == 0)
            {
                enabled = false;
                Finished?.Invoke();
                return;
            }
            m_Current = m_Pending.Dequeue();
            ProbeLog.Begin(m_Current.Name);
            TraceBehaviour.Reset();
            FramePhases.StartFrame = Time.frameCount;
            m_Start = Time.frameCount;
            try { m_Current.Begin(); }
            catch (Exception e) { ProbeLog.Event("exception " + e.GetType().Name + ": " + e.Message); m_Current = null; }
            return;
        }
        bool done;
        try { done = m_Current.Frame(Time.frameCount - m_Start); }
        catch (Exception e) { ProbeLog.Event("exception " + e.GetType().Name + ": " + e.Message); done = true; }
        // The prefix stays: deferred callbacks (graph destruction) still say when they come.
        if (done || Time.frameCount - m_Start > 600) m_Current = null;
    }
}
