using System;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;
using static UnityEngine.Tests.VisualEffectSimulationTests;

namespace UnityEngine.Tests
{
    /// <summary>Event attributes read in a spawn context (Get Attribute, Current), on compiled
    /// graphs made by hand: the spawn context's own event attribute, as its blocks left it.</summary>
    public class VisualEffectEventReadTests
    {
        /// <summary>A spawn context whose one task evaluates expressions 1..3: its first event
        /// attribute word read as a float, as a bool and as an unsigned integer.</summary>
        static VisualEffectAsset Graph(VFXValueType second = VFXValueType.Boolean)
        {
            var spawner = new VFXEditorSystemDesc
            {
                name = "Spawn", type = VFXSystemType.Spawner, layer = uint.MaxValue, buffers = new[] { new VFXMapping("spawner_output", 1) },
                tasks = new[]
                {
                    new VFXEditorTaskDesc { type = VFXTaskType.EvaluateExpressionsSpawner, values = new[] { new VFXMapping("start", 1), new VFXMapping("end", 4) } },
                },
            };
            var data = Data(new[] { Value(VFXValueType.Float) }, new[] { Constant(0, 0f) }, new[] { spawner }, new[]
            {
                new VFXEventDesc { name = "OnPlay", startSystems = new uint[] { 0 }, stopSystems = Array.Empty<uint>(), initSystems = Array.Empty<uint>() },
            }, spawners: 1, perSpawnEvent: new[]
            {
                Op(VFXExpressionOperation.ReadEventAttribute, -1, -1, 0, (int)VFXValueType.Float),
                Op(VFXExpressionOperation.ReadEventAttribute, -1, -1, 0, (int)second),
                Op(VFXExpressionOperation.ReadEventAttribute, -1, -1, 0, (int)VFXValueType.Uint32),
            });
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            asset.m_Settings.CullingFlags = VFXCullingFlags.CullNone;
            asset.m_Settings.PreWarmStepCount = 0;
            return asset;
        }

        [Fact]
        public void ASpawnContextReadsItsOwnEventAttributeAsItsBlocksLeftIt()
        {
            var asset = Graph();
            var effect = Running(asset);
            try
            {
                Assert.Null(VisualEffect.Unsupported(asset.m_Data));
                // The spawn context's attribute as a block left it.
                var spawner = new VFXSpawnerSystem(effect, 0);
                spawner.EventAttribute.Words[0] = 0x41a00102u;
                var values = effect.Values;
                VFXExpressionEvaluator.Evaluate(effect.Data, values, 1, 4, new VisualEffectState(), spawner);
                Assert.Equal(0x41a00102u, values.GetWord(1));
                // A bool is the word's low byte; an unsigned integer the word itself.
                Assert.Equal(0x02u, values.GetWord(2));
                Assert.Equal(0x41a00102u, values.GetWord(3));
                // Outside a spawn context the values stay as they are.
                values.SetWord(1, 7u);
                VFXExpressionEvaluator.Evaluate(effect.Data, values, 1, 4, new VisualEffectState(), null);
                Assert.Equal(7u, values.GetWord(1));
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void AnAttributeOfAnotherTypeIsRefused()
        {
            Assert.Equal("an event attribute of the type Matrix4x4 read in a spawn context",
                VisualEffect.Unsupported(Graph(VFXValueType.Matrix4x4).m_Data));
        }
    }
}
