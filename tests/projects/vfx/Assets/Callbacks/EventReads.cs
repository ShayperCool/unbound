using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Spawner blocks written as scripts, for the probe's controls of event attributes read in a spawn
/// context: their inputs are the spawn context's own event attributes as Get Attribute (Current)
/// reads them (a float, a vector, an unsigned integer and a bool), written down as
/// <see cref="OpsInputs"/> does, each block's rows under its own name. A spawn context has the
/// first before its blocks that set attributes and the second after them.
/// </summary>
public abstract class EventReads : VFXSpawnerCallbacks
{
    public class Inputs
    {
        /// <summary>The event's lifetime, colour, seed and alive.</summary>
        public float f00;
        public Vector3 v00;
        public uint u00;
        public bool b00;
    }

    protected abstract string Block { get; }

    protected abstract FieldInfo[] Fields { get; }

    /// <summary>How many updates of a block are written down, counted across plays.</summary>
    const int RecordedUpdates = 20;

    static readonly Dictionary<string, int> s_Plays = new Dictionary<string, int>(), s_Updates = new Dictionary<string, int>();

    static int Next(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int count);
        return counts[key] = count + 1;
    }

    /// <summary>Rows `<block>-play<n>` and `<block>-update<n>`, numbered across plays.</summary>
    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) =>
        OpsInputs.Write(Block + "play" + Next(s_Plays, vfxComponent.name + Block), vfxValues, vfxComponent, Fields);

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent)
    {
        int update = Next(s_Updates, vfxComponent.name + Block);
        if (update <= RecordedUpdates) OpsInputs.Write(Block + "update" + update, vfxValues, vfxComponent, Fields);
    }

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) { }
}
