using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

/// <summary>
/// Compiled graphs handed to a resource by hand, as the graph compiler hands its own: which the
/// engine takes, which it refuses and how. The probe runs each case by name (it lives in the
/// runtime assembly and cannot name these types) and records what was logged and thrown.
/// </summary>
public static class RuntimeDataCases
{
    static VFXExpressionDesc Value(VFXValueType type) =>
        new VFXExpressionDesc { op = VFXExpressionOperation.Value, data = new[] { -1, -1, -1, (int)type } };

    static VFXExpressionDesc Expression(VFXExpressionOperation op, params int[] data) => new VFXExpressionDesc { op = op, data = data };

    static VFXEditorSystemDesc System(string name, VFXSystemType type, uint layer) => new VFXEditorSystemDesc
    {
        name = name, type = type, layer = layer, buffers = new VFXMapping[0], values = new VFXMapping[0],
        tasks = new VFXEditorTaskDesc[0], instanceSplitDescs = new VFXInstanceSplitDesc[0],
    };

    static VFXEventDesc Event(string name) =>
        new VFXEventDesc { name = name, startSystems = new uint[0], stopSystems = new uint[0], initSystems = new uint[0] };

    static readonly VFXValueType Float = VFXValueType.Float;

    static readonly Dictionary<string, (VFXExpressionDesc[] Expressions, VFXExpressionDesc[] PerEvent, VFXEditorSystemDesc[] Systems, VFXEventDesc[] Events)> s_Cases =
        new Dictionary<string, (VFXExpressionDesc[], VFXExpressionDesc[], VFXEditorSystemDesc[], VFXEventDesc[])>
    {
        ["valid"] = (new[] { Value(Float), Value(Float), Expression(VFXExpressionOperation.Add, 0, 1, -1, (int)Float) }, null,
            new[] { System("Spawn", VFXSystemType.Spawner, uint.MaxValue), System("A", VFXSystemType.Particle, 0), System("B", VFXSystemType.Particle, 1) },
            new[] { Event("OnPlay"), Event("OnStop") }),
        ["layers decrease"] = (new[] { Value(Float) }, null,
            new[] { System("A", VFXSystemType.Particle, 2), System("B", VFXSystemType.Particle, 1) }, null),
        ["layers equal"] = (new[] { Value(Float) }, null,
            new[] { System("A", VFXSystemType.Particle, 1), System("B", VFXSystemType.Particle, 1) }, null),
        ["no layer between"] = (new[] { Value(Float) }, null,
            new[] { System("A", VFXSystemType.Particle, 2), System("Spawn", VFXSystemType.Spawner, uint.MaxValue), System("B", VFXSystemType.Particle, 3) }, null),
        ["no layer then lower"] = (new[] { Value(Float) }, null,
            new[] { System("A", VFXSystemType.Particle, 2), System("Spawn", VFXSystemType.Spawner, uint.MaxValue), System("B", VFXSystemType.Particle, 1) }, null),
        ["operation outside"] = (new[] { Expression((VFXExpressionOperation)155, -1, -1, -1, -1) }, null, null, null),
        ["value without type"] = (new[] { Value(VFXValueType.None) }, null, null, null),
        ["value type outside"] = (new[] { Value((VFXValueType)21) }, null, null, null),
        ["empty expression"] = (new[] { Expression(VFXExpressionOperation.None, -1, -1, -1, -1) }, null, null, null),
        ["parent outside"] = (new[] { Value(Float), Expression(VFXExpressionOperation.Add, 0, 5, -1, (int)Float) }, null, null, null),
        ["parent is itself"] = (new[] { Value(Float), Expression(VFXExpressionOperation.Add, 0, 1, -1, (int)Float) }, null, null, null),
        ["parent among the per-event expressions"] = (new[] { Value(Float), Expression(VFXExpressionOperation.Add, 0, 2, -1, (int)Float) },
            new[] { Value(Float) }, null, null),
        ["extract inside"] = (new[] { Value(VFXValueType.Float3), Expression(VFXExpressionOperation.ExtractComponent, 0, -1, 2, 3) }, null, null, null),
        ["extract at the size"] = (new[] { Value(VFXValueType.Float3), Expression(VFXExpressionOperation.ExtractComponent, 0, -1, 3, 3) }, null, null, null),
        ["extract of five"] = (new[] { Value(VFXValueType.Float3), Expression(VFXExpressionOperation.ExtractComponent, 0, -1, 4, 5) }, null, null, null),
        ["sample a curve"] = (new[] { Value(VFXValueType.Curve), Value(Float), Expression(VFXExpressionOperation.SampleCurve, 0, 1, -1, -1) }, null, null, null),
        ["sample a float as a curve"] = (new[] { Value(Float), Value(Float), Expression(VFXExpressionOperation.SampleCurve, 0, 1, -1, -1) }, null, null, null),
        ["sample a curve as a gradient"] = (new[] { Value(VFXValueType.Curve), Value(Float), Expression(VFXExpressionOperation.SampleGradient, 0, 1, -1, -1) }, null, null, null),
        // Which of an operation's words are parents: a third word outside the sheet.
        ["branch third word outside"] = (new[] { Value(VFXValueType.Boolean), Value(Float), Expression(VFXExpressionOperation.Branch, 0, 1, 999, (int)Float) }, null, null, null),
        ["branch second word outside"] = (new[] { Value(VFXValueType.Boolean), Value(Float), Expression(VFXExpressionOperation.Branch, 0, 999, 1, (int)Float) }, null, null, null),
        ["fixed random first word outside"] = (new[] { Expression(VFXExpressionOperation.GenerateFixedRandom, 999, -1, -1, -1) }, null, null, null),
        ["row to matrix fourth word outside"] = (new[] { Value(VFXValueType.Float4), Expression(VFXExpressionOperation.RowToMatrix, 0, 0, 0, 999) }, null, null, null),
        ["event named twice"] = (new[] { Value(Float) }, null, new[] { System("A", VFXSystemType.Particle, 0) },
            new[] { Event("Fire"), Event("Cease"), Event("Fire") }),
    };

    public static string[] Names() => new List<string>(s_Cases.Keys).ToArray();

    public static void Run(string name)
    {
        var (expressions, perEvent, systems, events) = s_Cases[name];
        var resource = new VisualEffectResource();
        var sheet = new VFXExpressionSheet
        {
            expressions = expressions, expressionsPerSpawnEventAttribute = perEvent ?? new VFXExpressionDesc[0],
            values = new VFXExpressionValueContainerDesc[0], exposed = new VFXExposedMapping[0],
        };
        resource.SetRuntimeData(sheet, systems ?? new VFXEditorSystemDesc[0], events ?? new VFXEventDesc[0], new VFXGPUBufferDesc[0],
            new VFXCPUBufferDesc[0], new VFXTemporaryGPUBufferDesc[0], new VFXShaderSourceDesc[0], ShadowCastingMode.Off,
            MotionVectorGenerationMode.Camera, VFXInstancingDisabledReason.None, 7);
    }
}
