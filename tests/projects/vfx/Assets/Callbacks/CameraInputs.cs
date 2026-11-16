using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// A spawner block written as a script, for the probe's camera controls: its inputs are fed by
/// the graph's Main Camera operator, and it writes down what they hold when the spawn context
/// plays and in each of its first updates (per effect, by the component's name), with the
/// frame each update ran in.
/// </summary>
public class CameraInputs : VFXSpawnerCallbacks
{
    public class InputProperties
    {
        /// <summary>The camera's transform.</summary>
        public Matrix4x4 m00;
        /// <summary>Field of view, near and far planes, orthographic size, aspect ratio.</summary>
        public float f00, f01, f02, f03, f04;
        /// <summary>Whether it is orthographic.</summary>
        public bool b00;
        /// <summary>Pixel dimensions, scaled pixel dimensions, lens shift.</summary>
        public Vector2 p00, p01, p02;
    }

    /// <summary>How many updates of an effect are written down.</summary>
    public const int RecordedUpdates = 16;

    /// <summary>The probe's frame (it counts its own frames from the first of play).</summary>
    public static int Frame;

    public static readonly Dictionary<string, List<string>> Logs = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, int> s_Updates = new Dictionary<string, int>();
    static readonly FieldInfo[] s_Inputs = typeof(InputProperties).GetFields();

    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");

    static string Read(VFXExpressionValues values, FieldInfo input)
    {
        string name = input.Name;
        if (input.FieldType == typeof(float)) return F(values.GetFloat(name));
        if (input.FieldType == typeof(bool)) return values.GetBool(name) ? "1" : "0";
        if (input.FieldType == typeof(Vector2)) { var v = values.GetVector2(name); return F(v.x) + "," + F(v.y); }
        var m = values.GetMatrix4x4(name);
        var cells = new string[16];
        for (int i = 0; i < 16; i++) cells[i] = F(m[i]);
        return string.Join(",", cells);
    }

    static void Write(string what, VFXExpressionValues values, VisualEffect effect)
    {
        if (!Logs.TryGetValue(effect.name, out var log)) Logs[effect.name] = log = new List<string>();
        var row = new StringBuilder(what).Append(" frame=").Append(Frame);
        foreach (var input in s_Inputs) row.Append(' ').Append(input.Name).Append('=').Append(Read(values, input));
        log.Add(row.ToString());
    }

    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent)
    {
        s_Updates[vfxComponent.name] = 0;
        Write("play", vfxValues, vfxComponent);
    }

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent)
    {
        s_Updates.TryGetValue(vfxComponent.name, out int updates);
        s_Updates[vfxComponent.name] = ++updates;
        if (updates <= RecordedUpdates) Write("update" + updates.ToString("00"), vfxValues, vfxComponent);
    }

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) { }
}
