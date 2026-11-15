using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// A spawner block written as a script, for the probe: it has inputs of every type a block's
/// input can have, and writes down what each holds when the spawn context plays, in its first
/// updates and when it stops (per effect, by the component's name). The graph connects operators
/// to the inputs: what the inputs hold is what the effect's expressions evaluated to.
/// </summary>
public class ProbeInputs : VFXSpawnerCallbacks
{
    public class InputProperties
    {
        public float f00, f01, f02, f03, f04, f05, f06, f07, f08, f09, f10, f11, f12, f13, f14, f15, f16, f17, f18, f19, f20, f21, f22, f23, f24, f25, f26, f27, f28, f29, f30, f31, f32, f33, f34, f35, f36, f37, f38, f39, f40, f41, f42, f43, f44, f45, f46, f47;
        public int i00, i01, i02, i03, i04, i05, i06, i07, i08, i09, i10, i11, i12, i13, i14, i15;
        public uint u00, u01, u02, u03, u04, u05, u06, u07, u08, u09, u10, u11, u12, u13, u14, u15, u16, u17, u18, u19, u20;
        public bool b00, b01, b02, b03, b04, b05, b06, b07, b08, b09, b10, b11, b12, b13, b14, b15, b16, b17, b18, b19;
        public Vector3 v00, v01, v02, v03, v04, v05;
        public Vector4 q00, q01, q02, q03;
        public Matrix4x4 m00, m01, m02, m03;
        /// <summary>The game's time as the effect's expressions read it, in the order of
        /// <see cref="GameTime"/>: compared with the engine's own values, not written down.</summary>
        public float g00, g01, g02, g03, g04, g05, g06;
    }

    /// <summary>How many updates of an effect are written down.</summary>
    public const int RecordedUpdates = 3;

    public static readonly Dictionary<string, List<string>> Logs = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, int> s_Updates = new Dictionary<string, int>(), s_Plays = new Dictionary<string, int>();
    static readonly FieldInfo[] s_Inputs = typeof(InputProperties).GetFields();

    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");

    /// <summary>What the inputs g00.. are compared with: delta time, unscaled delta time, smooth
    /// delta time, time, unscaled time, time since the scene loaded, time scale.</summary>
    static float GameTime(int index)
    {
        switch (index)
        {
            case 0: return Time.deltaTime;
            case 1: return Time.unscaledDeltaTime;
            case 2: return Time.smoothDeltaTime;
            case 3: return Time.time;
            case 4: return Time.unscaledTime;
            case 5: return Time.timeSinceLevelLoad;
            default: return Time.timeScale;
        }
    }

    static string Read(VFXExpressionValues values, FieldInfo input)
    {
        string name = input.Name;
        // Wall-clock values differ from run to run: whether the input is the engine's value is the fact.
        if (name[0] == 'g') return values.GetFloat(name) == GameTime(int.Parse(name.Substring(1))) ? "same" : "other";
        if (input.FieldType == typeof(float)) return F(values.GetFloat(name));
        if (input.FieldType == typeof(int)) return values.GetInt(name).ToString();
        if (input.FieldType == typeof(uint)) return values.GetUInt(name).ToString();
        if (input.FieldType == typeof(bool)) return values.GetBool(name) ? "1" : "0";
        if (input.FieldType == typeof(Vector3)) { var v = values.GetVector3(name); return F(v.x) + "," + F(v.y) + "," + F(v.z); }
        if (input.FieldType == typeof(Matrix4x4))
        {
            var m = values.GetMatrix4x4(name);
            var cells = new string[16];
            for (int i = 0; i < 16; i++) cells[i] = F(m[i]);
            return string.Join(",", cells);
        }
        var q = values.GetVector4(name);
        return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
    }

    static void Write(string what, VFXSpawnerState state, VFXExpressionValues values, VisualEffect effect)
    {
        if (!Logs.TryGetValue(effect.name, out var log)) Logs[effect.name] = log = new List<string>();
        var row = new StringBuilder(what);
        row.Append(" state=").Append(state.loopState).Append(" new=").Append(state.newLoop ? 1 : 0).Append(" count=").Append(F(state.spawnCount));
        foreach (var input in s_Inputs) row.Append(' ').Append(input.Name).Append('=').Append(Read(values, input));
        log.Add(row.ToString());
    }

    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent)
    {
        s_Updates[vfxComponent.name] = 0;
        s_Plays.TryGetValue(vfxComponent.name, out int plays);
        s_Plays[vfxComponent.name] = ++plays;
        Write("play" + plays, state, vfxValues, vfxComponent);
        // What an input the block does not have, and an input read as another type, answer.
        var log = Logs[vfxComponent.name];
        try { log.Add("missing " + F(vfxValues.GetFloat("no such input"))); }
        catch (Exception e) { log.Add("missing " + e.GetType().Name + ": " + e.Message); }
        try { log.Add("float as uint " + vfxValues.GetUInt("f00") + " as bool " + (vfxValues.GetBool("f00") ? 1 : 0) + " uint as float " + F(vfxValues.GetFloat("u00"))); }
        catch (Exception e) { log.Add("other type " + e.GetType().Name + ": " + e.Message); }
    }

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent)
    {
        s_Updates.TryGetValue(vfxComponent.name, out int updates);
        s_Updates[vfxComponent.name] = ++updates;
        s_Plays.TryGetValue(vfxComponent.name, out int plays);
        if (updates <= RecordedUpdates) Write("play" + plays + "update" + updates, state, vfxValues, vfxComponent);
    }

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent)
    {
        s_Plays.TryGetValue(vfxComponent.name, out int plays);
        Write("play" + plays + "stop", state, vfxValues, vfxComponent);
    }
}
