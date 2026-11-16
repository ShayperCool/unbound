using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;
using static UnityEngine.Tests.VisualEffectSimulationTests;

namespace UnityEngine.Tests
{
    /// <summary>Output events on compiled graphs made by hand: two spawn contexts at constant
    /// rates feed an output event "Tick" (both) and "Fast" (the second).</summary>
    public class VisualEffectOutputEventTests
    {
        static VFXEditorSystemDesc Spawner(string name, int output, int rate) => new VFXEditorSystemDesc
        {
            name = name, type = VFXSystemType.Spawner, layer = uint.MaxValue, buffers = new[] { new VFXMapping("spawner_output", output) },
            tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.ConstantRateSpawner, values = new[] { new VFXMapping("Rate", rate) } } },
        };

        static VFXEditorSystemDesc Output(string name, params int[] inputs)
        {
            var buffers = new VFXMapping[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) buffers[i] = new VFXMapping("spawner_input", inputs[i]);
            return new VFXEditorSystemDesc { name = name, type = VFXSystemType.OutputEvent, layer = uint.MaxValue, buffers = buffers };
        }

        /// <summary>Thirty a second (one each step of a thirtieth) and sixty a second.</summary>
        static VisualEffectAsset Graph()
        {
            var data = Data(new[] { Value(VFXValueType.Float), Value(VFXValueType.Float) }, new[] { Constant(0, 30f), Constant(1, 60f) },
                new[] { Spawner("Slow", 1, 0), Spawner("Fast", 2, 1), Output("Tick", 1, 2), Output("Fast", 2) }, new[]
                {
                    new VFXEventDesc { name = "OnPlay", startSystems = new uint[] { 0, 1 }, stopSystems = Array.Empty<uint>(), initSystems = Array.Empty<uint>() },
                }, spawners: 2);
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            asset.m_Settings.CullingFlags = VFXCullingFlags.CullNone;
            asset.m_Settings.PreWarmStepCount = 0;
            return asset;
        }

        [Fact]
        public void EachStepHandsItsEventsInOrderWithTheAttributesThatRaisedThem()
        {
            var asset = Graph();
            var effect = Running(asset);
            try
            {
                Assert.Null(VisualEffect.Unsupported(asset.m_Data));
                var names = new List<string>();
                effect.GetOutputEventNames(names);
                Assert.Equal(new[] { "Tick", "Fast" }, names);
                var received = new List<(string, float, VFXEventAttribute)>();
                // The first update takes the play event: no listener yet, its events are dropped.
                effect.UpdateEffect(Thirtieth);
                effect.outputEventReceived += args => received.Add((args.nameId == Shader.PropertyToID("Tick") ? "Tick" : "Fast", args.eventAttribute.GetFloat("spawnCount"), args.eventAttribute));
                effect.UpdateEffect(Thirtieth);
                // Each input that asked for a particle raises its event once, in the order of
                // the inputs, then of the systems; every event hands over one attribute set.
                Assert.Equal(new[] { "Tick", "Tick", "Fast" }, received.ConvertAll(r => r.Item1));
                Assert.Equal(new[] { 1f, 2f, 2f }, received.ConvertAll(r => MathF.Floor(r.Item2)));
                Assert.Same(received[0].Item3, received[2].Item3);
                // An exception of a listener is logged; the next events are still handed over.
                received.Clear();
                bool thrown = false;
                Action<VFXOutputEventArgs> failing = args => { if (!thrown) { thrown = true; throw new InvalidOperationException("listener"); } };
                effect.outputEventReceived = failing + effect.outputEventReceived;
                Debug.unityLogger.logEnabled = false;
                try { effect.UpdateEffect(Thirtieth); }
                finally { Debug.unityLogger.logEnabled = true; }
                Assert.Equal(2, received.Count);
                // Without a listener a step's events are not kept for later.
                effect.outputEventReceived = null;
                effect.UpdateEffect(Thirtieth);
                received.Clear();
                effect.outputEventReceived += args => received.Add(("", 0f, null));
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(3, received.Count);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }
    }
}
