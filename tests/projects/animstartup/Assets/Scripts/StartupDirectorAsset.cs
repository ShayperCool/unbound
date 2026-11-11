using UnityEngine;
using UnityEngine.Playables;

/// <summary>A playable asset of one script playable with a script output, so that a
/// PlayableDirector's graph prepares and processes a frame the probe can count.</summary>
public class StartupDirectorAsset : PlayableAsset
{
    public override double duration => 10.0;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<StartupDirectorBehaviour>.Create(graph);
        ScriptPlayableOutput.Create(graph, "probe").SetSourcePlayable(playable);
        return playable;
    }
}

public class StartupDirectorBehaviour : PlayableBehaviour
{
    public int starts, plays, prepares, processes;
    /// <summary>The last prepared frame: its delta time, the playable's time and the evaluation type.</summary>
    public float delta = -1f;
    public double time = -1.0;
    public int evaluation = -1;

    public override void OnGraphStart(Playable playable) => starts++;

    public override void OnBehaviourPlay(Playable playable, FrameData info) => plays++;

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        prepares++;
        delta = info.deltaTime;
        time = playable.GetTime();
        evaluation = (int)info.evaluationType;
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData) => processes++;
}
