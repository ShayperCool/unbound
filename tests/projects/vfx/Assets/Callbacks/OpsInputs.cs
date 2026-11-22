using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// A spawner block written as a script, for the probe's operation controls: its inputs are fed
/// by operators over exposed properties (matrices built from and split into rows and columns,
/// a look-at frame and its position, angles and scale, colour spaces, texture and buffer sizes,
/// a camera's viewport point in the world),
/// and it writes down what they hold when the
/// spawn context plays and in its first updates (per effect, by the component's name).
/// </summary>
public class OpsInputs : VFXSpawnerCallbacks
{
    public class InputProperties
    {
        /// <summary>Matrices built from rows and from columns, and a look-at frame.</summary>
        public Matrix4x4 m00, m01, m02;
        /// <summary>Rows 1 and 2, columns 1 and 2 of a matrix; a colour from hue, saturation and value.</summary>
        public Vector4 q00, q01, q02, q03, q04;
        /// <summary>The look-at frame's position, angles and scale; a colour's hue, saturation and
        /// value; another matrix's position, angles and scale; a camera's viewport point in the
        /// world.</summary>
        public Vector3 v00, v01, v02, v03, v04, v05, v06, v07;
        /// <summary>A texture's width and height, a buffer's count.</summary>
        public uint u00, u01, u02;
    }

    /// <summary>How many updates of an effect are written down.</summary>
    public const int RecordedUpdates = 16;

    public static readonly Dictionary<string, List<string>> Logs = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, int> s_Updates = new Dictionary<string, int>();
    static readonly FieldInfo[] s_Inputs = typeof(InputProperties).GetFields();

    /// <summary>A float's bits in hexadecimal.</summary>
    public static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");

    static string Read(VFXExpressionValues values, FieldInfo input)
    {
        string name = input.Name;
        if (input.FieldType == typeof(uint)) return values.GetUInt(name).ToString();
        if (input.FieldType == typeof(float)) return F(values.GetFloat(name));
        if (input.FieldType == typeof(bool)) return values.GetBool(name) ? "true" : "false";
        if (input.FieldType == typeof(Vector2)) { var w = values.GetVector2(name); return F(w.x) + "," + F(w.y); }
        if (input.FieldType == typeof(Vector3)) { var v = values.GetVector3(name); return F(v.x) + "," + F(v.y) + "," + F(v.z); }
        if (input.FieldType == typeof(Vector4)) { var q = values.GetVector4(name); return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w); }
        var m = values.GetMatrix4x4(name);
        var cells = new string[16];
        for (int i = 0; i < 16; i++) cells[i] = F(m[i]);
        return string.Join(",", cells);
    }

    /// <summary>Writes down the inputs as one row named <paramref name="what"/>.</summary>
    public static void Write(string what, VFXExpressionValues values, VisualEffect effect, FieldInfo[] inputs)
    {
        if (!Logs.TryGetValue(effect.name, out var log)) Logs[effect.name] = log = new List<string>();
        var row = new StringBuilder(what);
        foreach (var input in inputs) row.Append(' ').Append(input.Name).Append('=').Append(Read(values, input));
        log.Add(row.ToString());
    }

    /// <summary>Writes down the inputs when a spawn context of the effect plays.</summary>
    public static void Played(VFXExpressionValues values, VisualEffect effect, FieldInfo[] inputs)
    {
        s_Updates[effect.name] = 0;
        Write("play", values, effect, inputs);
    }

    /// <summary>Writes down the inputs in the effect's first updates.</summary>
    public static void Updated(VFXExpressionValues values, VisualEffect effect, FieldInfo[] inputs)
    {
        s_Updates.TryGetValue(effect.name, out int updates);
        s_Updates[effect.name] = ++updates;
        if (updates <= RecordedUpdates) Write("update" + updates, values, effect, inputs);
    }

    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => Played(vfxValues, vfxComponent, s_Inputs);

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => Updated(vfxValues, vfxComponent, s_Inputs);

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) { }
}
