using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// A playable behaviour that writes every callback it gets, with the frame data, into the probe's
/// event list. Its name comes from the template it is cloned from.
/// </summary>
public class TraceBehaviour : PlayableBehaviour
{
    public string label = "?";

    /// <summary>Outputs by name, so frame data can say which output a traversal started from.</summary>
    public static readonly List<(PlayableOutput Output, string Name)> Outputs = new List<(PlayableOutput, string)>();
    public static ulong FirstFrame;
    public static bool HasFirstFrame;
    public static bool Quiet;
    /// <summary>Text in front of each event (the frame and player loop phase in frame scenarios).</summary>
    public static System.Func<string> Prefix;

    public static ScriptPlayable<TraceBehaviour> Create(PlayableGraph graph, string label, int inputs = 0) =>
        ScriptPlayable<TraceBehaviour>.Create(graph, new TraceBehaviour { label = label }, inputs);

    public static void Reset()
    {
        Outputs.Clear();
        HasFirstFrame = false;
        Quiet = false;
        Prefix = null;
    }

    static string OutputName(PlayableOutput output)
    {
        if (output.IsOutputNull()) return "null";
        foreach (var (o, name) in Outputs)
            if (o.GetHandle() == output.GetHandle()) return name;
        return "other";
    }

    static string Frame(ulong id)
    {
        if (!HasFirstFrame)
        {
            HasFirstFrame = true;
            FirstFrame = id;
        }
        return id.ToString();
    }

    public static string Describe(FrameData info) =>
        $"f={Frame(info.frameId)} dt={ProbeLog.F(info.deltaTime)} w={ProbeLog.F(info.weight)} ew={ProbeLog.F(info.effectiveWeight)} " +
        $"es={ProbeLog.F(info.effectiveSpeed)} eps={ProbeLog.F(info.effectiveParentSpeed)} {info.evaluationType} " +
        $"seek={info.seekOccurred} loop={info.timeLooped} held={info.timeHeld} eff={info.effectivePlayState} out={OutputName(info.output)}";

    public static string State(Playable p) =>
        $"t={ProbeLog.F(p.GetTime())} prev={ProbeLog.F(p.GetPreviousTime())} ps={p.GetPlayState()} done={p.IsDone()}";

    void Add(string what, Playable p, string extra = "")
    {
        if (Quiet) return;
        ProbeLog.Event($"{(Prefix != null ? Prefix() : "")}{label}.{what} {State(p)}{extra}");
    }

    public override void OnPlayableCreate(Playable playable) => Add("OnPlayableCreate", playable);
    public override void OnPlayableDestroy(Playable playable) => Add("OnPlayableDestroy", playable);
    public override void OnGraphStart(Playable playable) => Add("OnGraphStart", playable);
    public override void OnGraphStop(Playable playable) => Add("OnGraphStop", playable);
    public override void OnBehaviourPlay(Playable playable, FrameData info) => Add("OnBehaviourPlay", playable, " | " + Describe(info));
    public override void OnBehaviourPause(Playable playable, FrameData info) => Add("OnBehaviourPause", playable, " | " + Describe(info));
    public override void PrepareData(Playable playable, FrameData info) => Add("PrepareData", playable, " | " + Describe(info));
    public override void PrepareFrame(Playable playable, FrameData info) => Add("PrepareFrame", playable, " | " + Describe(info));

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        string data = playerData == null ? "null" : playerData is Object o ? (o == null ? "destroyed" : o.name) : playerData.GetType().Name;
        Add("ProcessFrame", playable, " | " + Describe(info) + " pd=" + data);
    }
}
