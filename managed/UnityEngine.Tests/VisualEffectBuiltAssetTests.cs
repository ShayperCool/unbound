using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A VisualEffectAsset as a build serializes it (an AssetBundle's document): its
    /// compiled graph read, and what it runs with.</summary>
    public class VisualEffectBuiltAssetTests
    {
        sealed class NoObjects : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected) => null;
        }

        /// <summary>A spawn system at an exposed rate feeding a particle system, as a build
        /// writes it (the fields of a bundle's document).</summary>
        const string Document = @"{
  ""m_Name"": ""Built"",
  ""m_Infos"": {
    ""m_RendererSettings"": { ""motionVectorGenerationMode"": 0, ""shadowCastingMode"": 0, ""rayTracingMode"": 0, ""receiveShadows"": 0, ""reflectionProbeUsage"": 0, ""lightProbeUsage"": 0 },
    ""m_CullingFlags"": 3, ""m_UpdateMode"": 1, ""m_PreWarmDeltaTime"": 0.25, ""m_PreWarmStepCount"": 2, ""m_InitialEventName"": ""Go"",
    ""m_InstancingMode"": 0, ""m_InstancingCapacity"": 64,
    ""m_Expressions"": {
      ""m_Expressions"": [
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 1 },
        { ""op"": 1, ""valueIndex"": 1, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 3 },
        { ""op"": 1, ""valueIndex"": 4, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 18 }
      ],
      ""m_MaxCommonExpressionsIndex"": 3
    },
    ""m_PropertySheet"": {
      ""m_Float"": { ""m_Array"": [ { ""m_ExpressionIndex"": 0, ""m_Value"": 5.5 } ] },
      ""m_Vector3f"": { ""m_Array"": [ { ""m_ExpressionIndex"": 1, ""m_Value"": { ""x"": 1, ""y"": 2, ""z"": 3 } } ] },
      ""m_Bool"": { ""m_Array"": [ { ""m_ExpressionIndex"": 2, ""m_Value"": 1 } ] }
    },
    ""m_ExposedExpressions"": [ { ""mapping"": { ""nameId"": ""Rate"", ""index"": 0 }, ""space"": -1 } ],
    ""m_Buffers"": [ { ""target"": 16, ""size"": 2, ""layout"": [], ""capacity"": 0, ""stride"": 4, ""mode"": 1 } ],
    ""m_TemporaryBuffers"": [],
    ""m_CPUBuffers"": [
      { ""capacity"": 1, ""stride"": 1, ""layout"": [ { ""name"": ""spawnCount"", ""type"": 1, ""offset"": { ""bucket"": 0, ""structure"": 1, ""element"": 0 } } ], ""initialData"": { ""data"": [ 1065353216 ] } },
      { ""capacity"": 1, ""stride"": 1, ""layout"": [ { ""name"": ""spawnCount"", ""type"": 1, ""offset"": { ""bucket"": 0, ""structure"": 1, ""element"": 0 } } ], ""initialData"": { ""data"": [ 1065353216 ] } }
    ],
    ""m_Events"": [ { ""name"": ""Go"", ""playSystems"": [ 0 ], ""stopSystems"": [], ""initSystems"": [] } ],
    ""m_InstancingDisabledReason"": 0, ""m_RuntimeVersion"": 38, ""m_CompilationVersion"": 7
  },
  ""m_Systems"": [
    { ""type"": 0, ""flags"": 0, ""capacity"": 0, ""layer"": 4294967295, ""name"": ""Spawn"", ""buffers"": [ { ""nameId"": ""spawner_output"", ""index"": 1 } ], ""values"": [],
      ""tasks"": [ { ""type"": 268435456, ""buffers"": [], ""temporaryBuffers"": [], ""values"": [ { ""nameId"": ""_vfx_enabled"", ""index"": 2 }, { ""nameId"": ""Rate"", ""index"": 0 } ], ""params"": [], ""processor"": { ""fileID"": 0 }, ""instanceSplitIndex"": 0 } ],
      ""instanceSplitDescs"": [] },
    { ""type"": 1, ""flags"": 2, ""capacity"": 16, ""layer"": 0, ""name"": ""Particles"", ""buffers"": [ { ""nameId"": ""spawner_input"", ""index"": 1 } ], ""values"": [],
      ""tasks"": [ { ""type"": 536870912, ""buffers"": [ { ""nameId"": ""graphValuesBuffer"", ""index"": 0 } ], ""temporaryBuffers"": [], ""values"": [], ""params"": [], ""processor"": { ""fileID"": 77 }, ""instanceSplitIndex"": 0 } ],
      ""instanceSplitDescs"": [ { ""values"": [] } ] }
  ]
}";

        static VisualEffectAsset Read(string json)
        {
            var asset = VisualEffectAsset.Create();
            asset.name = "Built";
            using var document = JsonDocument.Parse(json);
            asset.ReadSerialized(YNode.FromJson(document.RootElement), new NoObjects());
            return asset;
        }

        [Fact]
        public void ABuiltAssetReadsItsCompiledGraph()
        {
            var asset = Read(Document);
            Assert.Equal(VFXUpdateMode.DeltaTime, asset.m_Settings.UpdateMode);
            Assert.Equal(2u, asset.m_Settings.PreWarmStepCount);
            Assert.Equal("Go", asset.m_Settings.InitialEventName);
            var events = new List<string>();
            asset.GetEvents(events);
            Assert.Equal(new[] { "Go" }, events);
            var exposed = new List<VFXExposedProperty>();
            asset.GetExposedProperties(exposed);
            Assert.Equal("Rate", Assert.Single(exposed).name);
            var systems = new List<string>();
            asset.GetSystemNames(systems);
            Assert.Equal(new[] { "Spawn", "Particles" }, systems);
            var data = asset.m_Data;
            Assert.Equal(7u, data.CompiledVersion);
            Assert.Equal(VFXTaskType.ConstantRateSpawner, data.Systems[0].tasks[0].type);
            Assert.Equal(new uint[] { 1065353216 }, data.CpuInitialWords[1]);
            var values = new VisualEffectValues(data);
            Assert.Equal(5.5f, values.GetFloat(0));
            Assert.Equal(new Vector4(1f, 2f, 3f, 0f), values.GetVector(1, 3));
            Assert.Equal(1u, values.GetWord(2));
        }

        [Fact]
        public void ABuiltAssetRunsOnlyWithTheProgramsOfTheSameGraph()
        {
            var built = Read(Document);
            // The same graph, another constant: the same graph; another operation: not.
            Assert.True(built.m_Data.SameGraph(Read(Document.Replace("5.5", "8")).m_Data));
            Assert.False(built.m_Data.SameGraph(Read(Document.Replace(@"""data[3]"": 3", @"""data[3]"": 2")).m_Data));
            Assert.False(built.m_Data.SameGraph(Read(Document.Replace(@"""m_CompilationVersion"": 7", @"""m_CompilationVersion"": 8")).m_Data));
            // No graph of the project compiles to it: it does not run, and says why.
            built.TakeBuiltPrograms(name => new[] { Read(Document.Replace(@"""data[3]"": 3", @"""data[3]"": 2")) });
            Assert.Equal("programs built for a graphics device, and no graph of the project compiles to the same graph", VisualEffect.Unsupported(built.m_Data));
            // One does: it runs with that graph's programs (taken once).
            var other = Read(Document);
            // (A graph of the project: one not waiting for programs of its own.)
            var twin = Read(Document);
            twin.TakeBuiltPrograms(name => Array.Empty<VisualEffectAsset>());
            Object program = VisualEffectAsset.Create();
            twin.m_Data.Processors[1][0] = program;
            string asked = null;
            other.TakeBuiltPrograms(name => { asked = name; return new[] { twin }; });
            Assert.Equal("Built", asked);
            Assert.Same(program, other.m_Data.Processors[1][0]);
            Assert.Null(other.m_Data.BuiltRefusal);
            other.TakeBuiltPrograms(name => throw new InvalidOperationException("asked twice"));
        }
    }
}
