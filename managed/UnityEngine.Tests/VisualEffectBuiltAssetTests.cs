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
            // The project's graph at its path compiles to another graph, or there is none: it
            // does not run, and says why.
            var decoy = Read(Document.Replace(@"""data[3]"": 3", @"""data[3]"": 2"));
            decoy.TakeBuiltPrograms(null, path => null);
            built.TakeBuiltPrograms("assets/built.vfx", path => decoy);
            Assert.Equal("programs built for a graphics device, and the project's graph at assets/built.vfx compiles to another graph", VisualEffect.Unsupported(built.m_Data));
            var lost = Read(Document);
            lost.TakeBuiltPrograms("assets/gone.vfx", path => null);
            Assert.Equal("programs built for a graphics device, and the project has no graph at assets/gone.vfx to compile them from", VisualEffect.Unsupported(lost.m_Data));
            // Its bundle lists it under no path (a dependency of another asset): no graph is looked for.
            var unlisted = Read(Document);
            unlisted.TakeBuiltPrograms(null, path => throw new InvalidOperationException("no path to look at"));
            Assert.StartsWith("programs built for a graphics device, and its bundle does not list it", VisualEffect.Unsupported(unlisted.m_Data));
            // It does: the asset runs with that graph's programs and what a build does not keep
            // of its tasks, taken once.
            var other = Read(Document);
            // (A graph of the project: one not waiting for programs of its own.)
            var twin = Read(Document);
            twin.TakeBuiltPrograms(null, path => null);
            Object program = VisualEffectAsset.Create();
            twin.m_Data.Processors[1][0] = program;
            twin.m_Data.Systems[1].tasks[0].usesMaterialVariant = true;
            string asked = null;
            using (var logs = new RecordedLogs())
            {
                other.TakeBuiltPrograms("assets/built.vfx", path => { asked = path; return twin; });
                Assert.Equal(new[] { "Log: Unbound: the built VisualEffectAsset 'Built' runs with the programs the project's assets/built.vfx compiles to (values written into its blocks are the project's)." }, logs.Logged);
            }
            Assert.Equal("assets/built.vfx", asked);
            Assert.Same(program, other.m_Data.Processors[1][0]);
            Assert.True(other.m_Data.Systems[1].tasks[0].usesMaterialVariant);
            Assert.Null(other.m_Data.BuiltRefusal);
            other.TakeBuiltPrograms("assets/built.vfx", path => throw new InvalidOperationException("asked twice"));
        }

        [Fact]
        public void ABuiltAssetOfAnotherRuntimeDataVersionDoesNotRun()
        {
            var asset = Read(Document.Replace(@"""m_RuntimeVersion"": 38", @"""m_RuntimeVersion"": 37"));
            Assert.Equal("a compiled graph of runtime data version 37 (this release's is 38)", VisualEffect.Unsupported(asset.m_Data));
            asset.TakeBuiltPrograms("assets/built.vfx", path => throw new InvalidOperationException("not looked for"));
        }

        [Fact]
        public void EveryKindOfConstantIsRead()
        {
            const string Sheet = @"""m_PropertySheet"": {
      ""m_Float"": { ""m_Array"": [ { ""m_ExpressionIndex"": 0, ""m_Value"": 5.5 } ] },
      ""m_Vector2f"": { ""m_Array"": [ { ""m_ExpressionIndex"": 3, ""m_Value"": { ""x"": 4, ""y"": 5 } } ] },
      ""m_Vector3f"": { ""m_Array"": [ { ""m_ExpressionIndex"": 1, ""m_Value"": { ""x"": 1, ""y"": 2, ""z"": 3 } } ] },
      ""m_Vector4f"": { ""m_Array"": [ { ""m_ExpressionIndex"": 4, ""m_Value"": { ""x"": 6, ""y"": 7, ""z"": 8, ""w"": 9 } } ] },
      ""m_Uint"": { ""m_Array"": [ { ""m_ExpressionIndex"": 5, ""m_Value"": 4294967295 } ] },
      ""m_Int"": { ""m_Array"": [ { ""m_ExpressionIndex"": 6, ""m_Value"": -7 } ] },
      ""m_Matrix4x4f"": { ""m_Array"": [ { ""m_ExpressionIndex"": 7, ""m_Value"": { ""e00"": 1, ""e01"": 2, ""e02"": 0, ""e03"": 3, ""e10"": 0, ""e11"": 1, ""e12"": 0, ""e13"": 4, ""e20"": 0, ""e21"": 0, ""e22"": 1, ""e23"": 5, ""e30"": 0, ""e31"": 0, ""e32"": 0, ""e33"": 1 } } ] },
      ""m_AnimationCurve"": { ""m_Array"": [ { ""m_ExpressionIndex"": 8, ""m_Value"": { ""m_Curve"": [ { ""time"": 0, ""value"": 1, ""inSlope"": 0, ""outSlope"": 0, ""tangentMode"": 0, ""weightedMode"": 0 }, { ""time"": 2, ""value"": 3, ""inSlope"": 0, ""outSlope"": 0, ""tangentMode"": 0, ""weightedMode"": 0 } ], ""m_PreInfinity"": 2, ""m_PostInfinity"": 1 } } ] },
      ""m_Gradient"": { ""m_Array"": [ { ""m_ExpressionIndex"": 9, ""m_Value"": { ""key0"": { ""r"": 1, ""g"": 0, ""b"": 0, ""a"": 1 }, ""key1"": { ""r"": 0, ""g"": 0, ""b"": 1, ""a"": 0.5 }, ""ctime0"": 0, ""ctime1"": 65535, ""atime0"": 0, ""atime1"": 65535, ""m_Mode"": 1, ""m_NumColorKeys"": 2, ""m_NumAlphaKeys"": 2 } } ] },
      ""m_Bool"": { ""m_Array"": [ { ""m_ExpressionIndex"": 2, ""m_Value"": 1 } ] }
    },";
            const string Expressions = @"""data[3]"": 18 },
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 2 },
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 4 },
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 6 },
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 5 },
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 13 },
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 14 },
        { ""op"": 1, ""valueIndex"": 0, ""data[0]"": -1, ""data[1]"": -1, ""data[2]"": -1, ""data[3]"": 15 }";
            int sheetStart = Document.IndexOf(@"""m_PropertySheet"": {"), sheetEnd = Document.IndexOf(@"""m_ExposedExpressions""");
            var document = (Document.Substring(0, sheetStart) + Sheet + Document.Substring(sheetEnd))
                .Replace(@"""data[3]"": 18 }", Expressions).Replace(@"""m_MaxCommonExpressionsIndex"": 3", @"""m_MaxCommonExpressionsIndex"": 10");
            var data = Read(document).m_Data;
            Assert.Equal(10, data.Expressions.Length);
            var values = new VisualEffectValues(data);
            Assert.Equal(new Vector4(4f, 5f, 0f, 0f), values.GetVector(3, 2));
            Assert.Equal(new Vector4(6f, 7f, 8f, 9f), values.GetVector(4, 4));
            Assert.Equal(uint.MaxValue, values.GetWord(5));
            Assert.Equal(unchecked((uint)-7), values.GetWord(6));
            var matrix = values.GetMatrix(7);
            Assert.Equal(new Vector4(3f, 4f, 5f, 1f), matrix.GetColumn(3));
            Assert.Equal(2f, matrix[0, 1]);
            var curve = Assert.IsType<AnimationCurve>(values.GetObject(8));
            Assert.Equal(2, curve.length);
            Assert.Equal(WrapMode.Loop, curve.postWrapMode);
            var gradient = Assert.IsType<Gradient>(values.GetObject(9));
            Assert.Equal(GradientMode.Fixed, gradient.mode);
            Assert.Equal(0.5f, gradient.alphaKeys[1].alpha);
        }
    }
}
