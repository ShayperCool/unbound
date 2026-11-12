using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The release profile's registered paths of built-in resources, put in the tables for one
    /// test and taken out again (the host fills them from data/profiles/builtin-*.json).
    /// </summary>
    internal sealed class BuiltinPathTable : IDisposable
    {
        readonly List<(bool Extra, string Path, int Class, long FileId)> m_Rows = new List<(bool, string, int, long)>();

        internal BuiltinPathTable Default(long fileId, string path, string name, int classId) => Add(false, fileId, path, name, classId);

        internal BuiltinPathTable Extra(long fileId, string path, string name, int classId) => Add(true, fileId, path, name, classId);

        readonly List<string> m_Removed = new List<string>();

        /// <summary>A path the release removed, looked up as <paramref name="replacement"/>.</summary>
        internal BuiltinPathTable Removed(string path, string replacement, string message)
        {
            BuiltinResources.RemovedPaths[path] = (replacement, message);
            m_Removed.Add(path);
            return this;
        }

        BuiltinPathTable Add(bool extra, long fileId, string path, string name, int classId)
        {
            (extra ? BuiltinResources.ExtraPaths : BuiltinResources.DefaultPaths)[(path, classId)] = fileId;
            (extra ? BuiltinShaders.ExtraNames : BuiltinShaders.DefaultNames)[fileId] = name;
            (extra ? BuiltinObjects.ExtraClasses : BuiltinObjects.DefaultClasses)[fileId] = classId;
            m_Rows.Add((extra, path, classId, fileId));
            return this;
        }

        public void Dispose()
        {
            foreach (var path in m_Removed) BuiltinResources.RemovedPaths.Remove(path);
            foreach (var (extra, path, classId, fileId) in m_Rows)
            {
                (extra ? BuiltinResources.ExtraPaths : BuiltinResources.DefaultPaths).Remove((path, classId));
                (extra ? BuiltinShaders.ExtraNames : BuiltinShaders.DefaultNames).Remove(fileId);
                (extra ? BuiltinObjects.ExtraClasses : BuiltinObjects.DefaultClasses).Remove(fileId);
            }
        }
    }

    /// <summary>
    /// Resources.GetBuiltinResource and AssetDatabase.GetBuiltinExtraResource look a path up with
    /// the requested type's engine class, as the Unity 6000.0.71f1 Editor does
    /// (tests/projects/builtinresources). Without a host the meshes are the generated primitives.
    /// </summary>
    public sealed class BuiltinResourcesTests : IDisposable
    {
        const int MeshClass = 43, MaterialClass = 21;

        readonly BuiltinPathTable m_Table = new BuiltinPathTable()
            .Default(10202, "Cube.fbx", "Cube", MeshClass)
            .Default(10210, "Quad.fbx", "Quad", MeshClass);
        readonly List<string> m_Logs = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;

        public BuiltinResourcesTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
        }

        public void Dispose()
        {
            m_Logger.logHandler = m_PreviousHandler;
            m_Table.Dispose();
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;

            internal CaptureHandler(ILogHandler inner, List<string> logs)
            {
                m_Inner = inner;
                m_Logs = logs;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                m_Logs.Add(type + ": " + (args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        static string[] NotFound(string path) => new[]
        {
            "Assert: Failed to find " + path,
            "Error: The resource " + path + " could not be loaded from the resource file!",
        };

        [Fact]
        public void ARegisteredPathAnswersItsObjectEveryTime()
        {
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Assert.NotNull(cube);
            Assert.Equal("Cube", cube.name);
            Assert.Same(cube, Resources.GetBuiltinResource(typeof(Mesh), "Cube.fbx"));
            Assert.NotSame(cube, Resources.GetBuiltinResource<Mesh>("Quad.fbx"));
            Assert.Empty(m_Logs);
        }

        [Theory]
        [InlineData("cube.fbx")]
        [InlineData("Cube.FBX")]
        [InlineData("Nope.fbx")]
        public void AnotherPathIsNotFound(string path)
        {
            Assert.Null(Resources.GetBuiltinResource<Mesh>(path));
            Assert.Equal(NotFound(path), m_Logs);
        }

        [Fact]
        public void APathMatchesOnlyWithItsObjectsOwnClass()
        {
            // A base class and an unrelated class find nothing, though the path is registered.
            Assert.Null(Resources.GetBuiltinResource(typeof(Object), "Cube.fbx"));
            Assert.Null(Resources.GetBuiltinResource<Texture2D>("Cube.fbx"));
            var expected = new List<string>(NotFound("Cube.fbx"));
            expected.AddRange(NotFound("Cube.fbx"));
            Assert.Equal(expected, m_Logs);
        }

        [Fact]
        public void EachLookupSearchesItsOwnFile()
        {
            using var extra = new BuiltinPathTable().Extra(10303, "Default-Material.mat", "Default-Material", MaterialClass);
            // Default resources hold no Default-Material.mat; the extra resources hold no Cube.fbx.
            Assert.Null(Resources.GetBuiltinResource<Material>("Default-Material.mat"));
            Assert.Null(UnityEditor.AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx"));
            var expected = new List<string>(NotFound("Default-Material.mat"));
            expected.AddRange(NotFound("Cube.fbx"));
            Assert.Equal(expected, m_Logs);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ANullOrEmptyPathThrows(string path)
        {
            var e = Assert.Throws<ArgumentException>(() => Resources.GetBuiltinResource<Mesh>(path));
            Assert.Equal("Invalid path", e.Message);
            Assert.Empty(m_Logs);
        }

        const string ArialMessage = "Arial.ttf is no longer a valid built in font. Please use LegacyRuntime.ttf";

        [Fact]
        public void ARemovedPathIsLookedUpAsItsReplacementThenThrows()
        {
            using var removed = new BuiltinPathTable().Removed("Arial.ttf", "LegacyRuntime.ttf", ArialMessage);
            // No LegacyRuntime.ttf Mesh is registered: the replacement path is searched (and not
            // found) before the call throws.
            var e = Assert.Throws<ArgumentException>(() => Resources.GetBuiltinResource<Mesh>("Arial.ttf"));
            Assert.Equal(ArialMessage, e.Message);
            Assert.Equal(NotFound("LegacyRuntime.ttf"), m_Logs);
            // A found replacement throws as well (6000.0: the LegacyRuntime font is searched, then
            // the call throws).
            m_Logs.Clear();
            using var replacement = new BuiltinPathTable().Default(10202, "LegacyRuntime.ttf", "Cube", MeshClass);
            e = Assert.Throws<ArgumentException>(() => Resources.GetBuiltinResource<Mesh>("Arial.ttf"));
            Assert.Equal(ArialMessage, e.Message);
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void APathTheProfileDoesNotRemoveIsAnOrdinaryPath()
        {
            // Releases whose profile removes no path: Arial.ttf is looked up as itself.
            Assert.Null(Resources.GetBuiltinResource<Font>("Arial.ttf"));
            Assert.Equal(NotFound("Arial.ttf"), m_Logs);
        }

        [Fact]
        public void ScriptClassesStandForMonoBehaviourObjects()
        {
            Assert.Equal(114, EngineTypes.EngineClassIdOf(typeof(GUISkin)));
            Assert.Equal(114, EngineTypes.EngineClassIdOf(typeof(ScriptableObject)));
            Assert.Equal(114, EngineTypes.EngineClassIdOf(typeof(MonoBehaviour)));
            Assert.Equal(115, EngineTypes.EngineClassIdOf(typeof(UnityEditor.MonoScript)));
            Assert.Equal(43, EngineTypes.EngineClassIdOf(typeof(Mesh)));
            Assert.Equal(-1, EngineTypes.EngineClassIdOf(typeof(Object)));
        }
    }
}
