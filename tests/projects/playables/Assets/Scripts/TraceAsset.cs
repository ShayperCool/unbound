using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>A playable asset whose graph is two trace behaviours (root, child); it says when the
/// director asks for it, and optionally makes its own script output.</summary>
public class TraceAsset : PlayableAsset
{
    public double length = 1.0;
    public bool makeOutput = true;

    public override double duration => length;

    public override IEnumerable<PlayableBinding> outputs
    {
        get { yield return ScriptPlayableBinding.Create("D", this, typeof(GameObject)); }
    }

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        ProbeLog.Event($"{(TraceBehaviour.Prefix != null ? TraceBehaviour.Prefix() : "")}TraceAsset.CreatePlayable owner={owner?.name} graph={graph.GetEditorName()} mode={graph.GetTimeUpdateMode()} outputs={graph.GetOutputCount()} resolver={(graph.GetResolver() as Object)?.name}");
        var root = TraceBehaviour.Create(graph, "D.root", 1);
        var child = TraceBehaviour.Create(graph, "D.child");
        root.ConnectInput(0, child, 0, 1f);
        if (makeOutput)
        {
            var o = ScriptPlayableOutput.Create(graph, "D");
            TraceBehaviour.Outputs.Add((o, "D"));
            o.SetSourcePlayable(root);
        }
        return root;
    }
}
