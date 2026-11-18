using System.Collections.Generic;
using UnityEngine.VFX;

/// <summary>
/// A spawner block written as a script, for the probe's controls of event attributes read in a
/// spawn context: in a spawn context whose loop duration is its own lifetime as Get Attribute
/// (Current) reads it, it writes down the loop's duration, index and time and the lifetime the
/// context holds, when it plays and in its first twenty updates (counted across plays), under
/// the rows `loop-play<n>` and `loop-update<n>` of <see cref="OpsInputs.Logs"/>.
/// </summary>
public class LoopReads : VFXSpawnerCallbacks
{
    public class InputProperties { }

    const int RecordedUpdates = 20;

    static readonly Dictionary<string, int> s_Plays = new Dictionary<string, int>(), s_Updates = new Dictionary<string, int>();

    static void Write(string what, VFXSpawnerState state, VisualEffect effect)
    {
        if (!OpsInputs.Logs.TryGetValue(effect.name, out var log)) OpsInputs.Logs[effect.name] = log = new List<string>();
        log.Add(what + " d=" + OpsInputs.F(state.loopDuration) + " i=" + state.loopIndex + " t=" + OpsInputs.F(state.totalTime)
            + " l=" + OpsInputs.F(state.vfxEventAttribute.GetFloat("lifetime")));
    }

    static int Next(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int count);
        return counts[key] = count + 1;
    }

    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) =>
        Write("loop-play" + Next(s_Plays, vfxComponent.name), state, vfxComponent);

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent)
    {
        int update = Next(s_Updates, vfxComponent.name);
        if (update <= RecordedUpdates) Write("loop-update" + update, state, vfxComponent);
    }

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) { }
}
