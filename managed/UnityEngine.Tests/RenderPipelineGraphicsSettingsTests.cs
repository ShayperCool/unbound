using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor.Rendering;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A global settings asset gets the settings its pipeline supports when it is registered, as
    /// in the Unity 6000.0.71f1 Editor (tests/projects/rpglobalsettings, URP's asset without its
    /// SSAO resources and with a null shader): missing supported settings are created and reset
    /// after all are added, unsupported or error-obsolete ones are removed, and a resource
    /// container's null resource fields are loaded from their paths. The types below are stand-ins
    /// for a pipeline's.
    /// </summary>
    public sealed class RenderPipelineGraphicsSettingsTests : IDisposable
    {
        public sealed class ProbeAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => null;
        }

        public sealed class OtherAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => null;
        }

        public sealed class ProbePipeline : RenderPipeline
        {
            protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
        }

        [SupportedOnRenderPipeline(typeof(ProbeAsset))]
        public sealed class ProbeGlobalSettings : RenderPipelineGlobalSettings
        {
            [SerializeReference] internal List<IRenderPipelineGraphicsSettings> m_List = new List<IRenderPipelineGraphicsSettings>();
            protected override List<IRenderPipelineGraphicsSettings> settingsList => m_List;
        }

        /// <summary>Records the order in which the population made and reset settings.</summary>
        static readonly List<string> s_Events = new List<string>();

        [Serializable, SupportedOnRenderPipeline(typeof(ProbeAsset))]
        public sealed class Missing : IRenderPipelineGraphicsSettings
        {
            Missing() => s_Events.Add("new Missing");
            public int version => 0;
            public bool isAvailableInPlayerBuild => false;
            public void Reset() => s_Events.Add("reset Missing");
        }

        [Serializable, SupportedOnRenderPipeline(typeof(ProbeAsset))]
        public sealed class AlsoMissing : IRenderPipelineGraphicsSettings
        {
            AlsoMissing() => s_Events.Add("new AlsoMissing");
            public int version => 0;
            public bool isAvailableInPlayerBuild => false;
            public void Reset() => s_Events.Add("reset AlsoMissing");
        }

        /// <summary>Paths of an array: a missing array is made, a null element loaded.</summary>
        [Serializable, SupportedOnRenderPipeline(typeof(ProbeAsset))]
        public sealed class ArrayResources : IRenderPipelineResources
        {
            public int version => 0;
            public bool isAvailableInPlayerBuild => false;
            [ResourcePaths(new[] { "Cube.fbx", "Quad.fbx" }, SearchType.BuiltinPath)] public Mesh[] meshes;
        }

        [Serializable, SupportedOnRenderPipeline(typeof(ProbeAsset))]
        public sealed class Resources : IRenderPipelineResources
        {
            public int version => 0;
            public bool isAvailableInPlayerBuild => false;
            [ResourcePath("Cube.fbx", SearchType.BuiltinPath)] public Mesh mesh;
            [ResourcePath("Quad.fbx", SearchType.BuiltinPath)] public Mesh kept;
        }

        [Serializable, SupportedOnRenderPipeline(typeof(OtherAsset))]
        public sealed class OtherPipelines : IRenderPipelineGraphicsSettings
        {
            public int version => 0;
            public bool isAvailableInPlayerBuild => false;
        }

        [Serializable, SupportedOnRenderPipeline(typeof(ProbeAsset)), Obsolete("gone", true)]
        public sealed class Removed : IRenderPipelineGraphicsSettings
        {
            public int version => 0;
            public bool isAvailableInPlayerBuild => false;
        }

        static readonly List<Assembly> GameAssemblies =
            (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        static readonly FieldInfo CachedFor = typeof(UnityEditor.TypeCache).GetField("s_CachedFor", BindingFlags.NonPublic | BindingFlags.Static);

        readonly BuiltinPathTable m_Table = new BuiltinPathTable()
            .Default(10202, "Cube.fbx", "Cube", 43)
            .Default(10210, "Quad.fbx", "Quad", 43);

        public RenderPipelineGraphicsSettingsTests()
        {
            // The stand-in settings classes are found as a game assembly's would be.
            GameAssemblies.Add(typeof(RenderPipelineGraphicsSettingsTests).Assembly);
            CachedFor.SetValue(null, null);
            s_Events.Clear();
        }

        public void Dispose()
        {
            GameAssemblies.Remove(typeof(RenderPipelineGraphicsSettingsTests).Assembly);
            CachedFor.SetValue(null, null);
            m_Table.Dispose();
        }

        [Fact]
        public void TheAssetGetsWhatItsPipelineSupports()
        {
            var settings = ScriptableObject.CreateInstance<ProbeGlobalSettings>();
            var quad = BuiltinMeshes.Create("Quad");
            var stored = new Resources { kept = quad };
            // An error-obsolete class cannot be named: made by reflection.
            var removedType = typeof(RenderPipelineGraphicsSettingsTests).GetNestedType("Removed");
            var removed = (IRenderPipelineGraphicsSettings)Activator.CreateInstance(removedType);
            settings.m_List.AddRange(new IRenderPipelineGraphicsSettings[] { stored, new OtherPipelines(), removed });

            EditorGraphicsSettings.PopulateRenderPipelineGraphicsSettings(settings);

            Assert.True(settings.TryGet(typeof(Missing), out var missing));
            Assert.NotNull(missing);
            Assert.True(settings.TryGet(typeof(AlsoMissing), out _));
            // Every created setting is reset after all are added.
            Assert.Equal(4, s_Events.Count);
            Assert.StartsWith("new ", s_Events[0]);
            Assert.StartsWith("new ", s_Events[1]);
            Assert.StartsWith("reset ", s_Events[2]);
            Assert.StartsWith("reset ", s_Events[3]);
            Assert.True(settings.TryGet(typeof(ArrayResources), out var array));
            var meshes = ((ArrayResources)array).meshes;
            Assert.Equal(new[] { "Cube", "Quad" }, Array.ConvertAll(meshes, m => m.name));
            Assert.False(settings.TryGet(typeof(OtherPipelines), out _));
            Assert.False(settings.TryGet(removedType, out _));
            Assert.True(settings.TryGet(typeof(Resources), out var resources));
            Assert.Same(stored, resources);
            // The null field is loaded from its path; a set one is kept.
            Assert.NotNull(stored.mesh);
            Assert.Equal("Cube", stored.mesh.name);
            Assert.Same(quad, stored.kept);
            Object.DestroyImmediate(settings);
        }

        [Serializable, SupportedOnRenderPipeline(typeof(OtherAsset))]
        public sealed class Unfound : IRenderPipelineResources
        {
            public int version => 0;
            public bool isAvailableInPlayerBuild => false;
            [ResourcePath("Nope.fbx", SearchType.BuiltinPath)] public Mesh mesh;
        }

        [Fact]
        public void AResourcePathThatNamesNothingIsReported()
        {
            var logs = new List<string>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            logger.logHandler = new Capture(previous, logs);
            try
            {
                var result = UnityEditor.Rendering.RenderPipelineResourcesEditorUtils.TryReloadContainedNullFields(new Unfound());
                Assert.Equal(UnityEditor.Rendering.RenderPipelineResourcesEditorUtils.ResultStatus.InvalidPathOrNameFound, result);
            }
            finally
            {
                logger.logHandler = previous;
            }
            Assert.Contains("Error: Failed to find Nope.fbx in BuiltinPath.", logs);
        }

        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;

            internal Capture(ILogHandler inner, List<string> logs)
            {
                m_Inner = inner;
                m_Logs = logs;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                m_Logs.Add(type + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        [Fact]
        public void RegisteringTheAssetPopulatesIt()
        {
            var settings = ScriptableObject.CreateInstance<ProbeGlobalSettings>();
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<ProbePipeline>(settings);
            try
            {
                Assert.True(settings.TryGet(typeof(Missing), out _));
                Assert.True(settings.TryGet(typeof(Resources), out var resources));
                Assert.Equal("Cube", ((Resources)resources).mesh.name);
            }
            finally
            {
                EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<ProbePipeline>(null);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
