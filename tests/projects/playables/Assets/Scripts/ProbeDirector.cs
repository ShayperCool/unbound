using UnityEngine;
using UnityEngine.Playables;

/// <summary>A PlayableDirector added at runtime playing a TraceAsset: Play, Pause, Resume, time
/// changes, Evaluate, Stop, the wrap modes at the end, events, play on awake, disabling.</summary>
public class DirectorFramesScenario : FrameScenario
{
    public override string Name => "director.frames";
    PlayableDirector m_Director;
    TraceAsset m_Asset;
    GameObject m_Go;

    string Info() => $"time={ProbeLog.F(m_Director.time)} state={m_Director.state} graph={m_Director.playableGraph.IsValid()} playing={(m_Director.playableGraph.IsValid() && m_Director.playableGraph.IsPlaying())}";

    void Log(string what) => ProbeLog.Event(FramePhases.Prefix() + what + " | " + Info());

    void Watch(string phase)
    {
        if (phase == "afterDirectorUpdate") Log("sample");
    }

    public override void Begin()
    {
        TraceBehaviour.Prefix = FramePhases.Prefix;
        m_Asset = ScriptableObject.CreateInstance<TraceAsset>();
        m_Asset.name = "Trace";
        m_Go = new GameObject("Dir");
        m_Director = m_Go.AddComponent<PlayableDirector>();
        ProbeLog.Event($"defaults: playOnAwake={m_Director.playOnAwake} wrap={m_Director.extrapolationMode} update={m_Director.timeUpdateMode} initial={ProbeLog.F(m_Director.initialTime)} duration={ProbeLog.F(m_Director.duration)} asset={m_Director.playableAsset == null}");
        m_Director.played += d => Log("played");
        m_Director.paused += d => Log("paused");
        m_Director.stopped += d => Log("stopped");
        m_Director.playableAsset = m_Asset;
        m_Director.extrapolationMode = DirectorWrapMode.None;
        Log($"asset set duration={ProbeLog.F(m_Director.duration)}");
        m_Director.Play();
        var g = m_Director.playableGraph;
        Log($"Play: outputs={g.GetOutputCount()} name={g.GetEditorName()} mode={g.GetTimeUpdateMode()}");
        FramePhases.Watchers.Add(Watch);
    }

    public override bool Frame(int k)
    {
        switch (k)
        {
            case 3: Log("Pause"); m_Director.Pause(); Log("after Pause"); break;
            case 5: Log("Resume"); m_Director.Resume(); Log("after Resume"); break;
            case 7: Log("time = 0.5"); m_Director.time = 0.5; Log("after time"); break;
            case 9: Log("Evaluate"); m_Director.Evaluate(); Log("after Evaluate"); break;
            case 10: Log("Play while playing"); m_Director.Play(); break;
            case 40:
                Log("Loop + Play");
                m_Director.extrapolationMode = DirectorWrapMode.Loop;
                m_Director.Play();
                Log("after Play");
                break;
            case 100: Log("Stop"); m_Director.Stop(); Log("after Stop"); break;
            case 102:
                Log("Hold + initialTime 0.25 + Play");
                m_Director.extrapolationMode = DirectorWrapMode.Hold;
                m_Director.initialTime = 0.25;
                m_Director.Play();
                Log("after Play");
                break;
            case 150: Log("disable"); m_Director.enabled = false; Log("after disable"); break;
            case 152: Log("enable"); m_Director.enabled = true; Log("after enable"); break;
            case 154: Log("Stop"); m_Director.Stop(); Log("after Stop"); break;
            case 156:
                Log("Evaluate when stopped");
                m_Director.time = 0.75;
                m_Director.Evaluate();
                Log("after Evaluate");
                break;
            case 158:
                Log("asset without its own output");
                m_Director.Stop();
                var other = ScriptableObject.CreateInstance<TraceAsset>();
                other.makeOutput = false;
                m_Director.playableAsset = other;
                m_Director.Play();
                Log($"outputs={m_Director.playableGraph.GetOutputCount()}");
                break;
            case 162:
                Log("play on awake");
                m_Director.Stop();
                var go = new GameObject("Dir2");
                go.SetActive(false);
                var d2 = go.AddComponent<PlayableDirector>();
                d2.playableAsset = m_Asset;
                d2.played += d => Log("Dir2 played");
                go.SetActive(true);
                Log($"Dir2 active: state={d2.state} graph={d2.playableGraph.IsValid()}");
                break;
            case 165:
                Log("destroy");
                FramePhases.Watchers.Remove(Watch);
                m_Go.AddComponent<LifecycleLogger>();
                Object.Destroy(m_Go);
                Object.Destroy(GameObject.Find("Dir2"));
                break;
            case 168:
                return true;
        }
        return false;
    }
}

/// <summary>Graph destruction while frames run: when the callbacks come, and who is paused.</summary>
public class DestroyFramesScenario : FrameScenario
{
    public override string Name => "frames.destroy";
    PlayableGraph m_A, m_B, m_C, m_E;

    PlayableGraph Make(string name, bool play)
    {
        var g = PlayableGraph.Create(name);
        g.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var o = ScriptPlayableOutput.Create(g, name);
        TraceBehaviour.Outputs.Add((o, name));
        var root = TraceBehaviour.Create(g, name + ".root", 2);
        var c0 = TraceBehaviour.Create(g, name + ".c0");
        var c1 = TraceBehaviour.Create(g, name + ".c1");
        root.ConnectInput(0, c0, 0, 1f);
        root.ConnectInput(1, c1, 0, 0.5f);
        o.SetSourcePlayable(root);
        if (play) g.Play();
        return g;
    }

    public override void Begin()
    {
        TraceBehaviour.Prefix = FramePhases.Prefix;
        m_A = Make("A", true);
        m_B = Make("B", false);
        m_C = Make("C", false);
        m_C.Evaluate(0.1f);
        ((Playable)m_C.GetRootPlayable(0)).GetInput(1).Pause();
        m_C.Evaluate(0.1f);
    }

    public override bool Frame(int k)
    {
        string p = FramePhases.Prefix();
        switch (k)
        {
            case 2:
                ProbeLog.Event(p + "destroy A (playing), B (never evaluated), C (evaluated, c1 paused)");
                m_A.Destroy();
                m_B.Destroy();
                m_C.Destroy();
                ProbeLog.Event(p + "after destroy");
                break;
            case 3:
                ProbeLog.Event(p + "E: play, stop, destroy");
                m_E = Make("E", true);
                m_E.Stop();
                m_E.Destroy();
                break;
            case 4:
                ProbeLog.Event(p + "F: play then stop next frame");
                m_E = Make("F", true);
                break;
            case 6:
                ProbeLog.Event(p + "F stop");
                m_E.Stop();
                break;
            case 7:
                ProbeLog.Event(p + "F destroy");
                m_E.Destroy();
                break;
            case 9:
                return true;
        }
        return false;
    }
}
