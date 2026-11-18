using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>A scene with instances of missing prefabs, in the documents the engine makes of it
    /// (tests/projects/missingprefab): the errors its loads log and the prefab state of the
    /// placeholders.</summary>
    [Collection("Frames")]
    public class MissingPrefabTests : IDisposable
    {
        const string Gone = "ffff0000000000000000000000000002";
        const string AlsoGone = "0000000000000000000000000000aaa1";

        readonly bool m_Playing = EditorSession.IsPlaying;
        readonly List<SceneData> m_Loaded = new List<SceneData>(SceneRegistry.Loaded);
        readonly SceneData m_Active = SceneRegistry.Active;
        readonly ILogHandler m_Handler = Debug.unityLogger.logHandler;
        readonly List<string> m_Errors = new List<string>();
        SceneData m_Scene;

        public MissingPrefabTests()
        {
            Debug.unityLogger.logHandler = new Capture(m_Handler, m_Errors);
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_Handler;
            if (m_Scene != null)
            {
                foreach (var root in m_Scene.Roots.ToArray()) if (root != null) Object.DestroyImmediate(root);
                if (SceneRegistry.Get(m_Scene.Handle) != null) SceneRegistry.Forget(m_Scene);
            }
            SceneRegistry.Loaded.Clear();
            SceneRegistry.Loaded.AddRange(m_Loaded);
            SceneRegistry.Active = m_Active;
            EditorSession.IsPlaying = m_Playing;
        }

        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Errors;
            internal Capture(ILogHandler inner, List<string> errors) { m_Inner = inner; m_Errors = errors; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error)
                    m_Errors.Add(args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        /// <summary>An instance (file id <paramref name="id"/>) of <paramref name="guid"/> whose root
        /// shows <paramref name="name"/>, with one placeholder child, at the scene's root.</summary>
        static string Instance(long id, string guid, string name) => $@"--- !u!1001 &{id}
PrefabInstance:
  m_UnboundMissingPrefabName: '{name}'
  m_Modification:
    m_TransformParent: {{fileID: 0}}
  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}
--- !u!1 &{id + 1}
GameObject:
  m_CorrespondingSourceObject: {{fileID: 10, guid: {guid}, type: 3}}
  m_PrefabInstance: {{fileID: {id}}}
  m_Component:
  - component: {{fileID: {id + 2}}}
  m_Name: '{name}'
  m_IsActive: 1
--- !u!4 &{id + 2}
Transform:
  m_CorrespondingSourceObject: {{fileID: 11, guid: {guid}, type: 3}}
  m_PrefabInstance: {{fileID: {id}}}
  m_GameObject: {{fileID: {id + 1}}}
  m_Children:
  - {{fileID: {id + 4}}}
  m_Father: {{fileID: 0}}
--- !u!1 &{id + 3}
GameObject:
  m_CorrespondingSourceObject: {{fileID: 20, guid: {guid}, type: 3}}
  m_PrefabInstance: {{fileID: {id}}}
  m_Component:
  - component: {{fileID: {id + 4}}}
  m_Name: Placeholder for referenced GameObject in Prefab instance (1)
  m_IsActive: 1
--- !u!4 &{id + 4}
Transform:
  m_PrefabInstance: {{fileID: {id}}}
  m_GameObject: {{fileID: {id + 3}}}
  m_Children: []
  m_Father: {{fileID: {id + 2}}}
";

        SceneData Load()
        {
            EditorSession.IsPlaying = false;
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
                + Instance(100, Gone, $"First (Missing Prefab with guid: {Gone})")
                + Instance(200, AlsoGone, $"Other (Missing Prefab with guid: {AlsoGone})")
                + Instance(300, Gone, $"Second (Missing Prefab with guid: {Gone})");
            m_Scene = SceneRegistry.Create("missing", "Assets/MissingPrefabTests.unity", -1);
            m_Scene.IsLoaded = true;
            m_Scene.LoadingState = Scene.LoadingState.Loaded;
            SceneRegistry.Loaded.Add(m_Scene);
            SceneLoader.LoadPrepared(m_Scene, UnityYaml.Parse(yaml));
            return m_Scene;
        }

        [Fact]
        public void EachLoadLogsOneErrorPerMissingPrefabInGuidOrder()
        {
            var scene = Load();
            var expected = new[]
            {
                $"Prefab instance problem. Missing Prefab Asset: 'Other (Missing Prefab with guid: {AlsoGone})'",
                $"Prefab instance problem: 'First (Missing Prefab with guid: {Gone})'. 2 instances are missing the same Prefab Asset with guid {Gone}.",
            };
            Assert.Equal(expected, m_Errors);
            // The Editor logs them again whenever it loads the scene (entering Play mode reloads it).
            SceneLoader.LogMissingPrefabs(scene);
            Assert.Equal(4, m_Errors.Count);
            Assert.Equal(expected, m_Errors.GetRange(2, 2));
        }

        [Fact]
        public void APlaceholdersInstanceIsMissingAndOnlyItsTopmostObjectIsARoot()
        {
            var scene = Load();
            var root = scene.Roots.Find(r => r.name.StartsWith("First", StringComparison.Ordinal));
            var placeholder = root.transform.GetChild(0).gameObject;
            Assert.True(PrefabUtility.IsAnyPrefabInstanceRoot(root));
            Assert.False(PrefabUtility.IsAnyPrefabInstanceRoot(placeholder));
            Assert.Same(root, PrefabUtility.GetNearestPrefabInstanceRoot(placeholder));
            foreach (var go in new[] { root, placeholder })
            {
                Assert.True(PrefabUtility.IsPrefabAssetMissing(go));
                Assert.Equal(PrefabInstanceStatus.MissingAsset, PrefabUtility.GetPrefabInstanceStatus(go));
                Assert.True(PrefabUtility.IsPartOfPrefabInstance(go));
            }
        }

        [Fact]
        public void AnInstanceWhoseSourceLoadedIsNotMissing()
        {
            // A connected instance's objects (no engine mark) keep the source-chain rule: a source
            // that does not exist makes them missing, the mark alone is what the engine adds.
            var data = PrefabLinks.ReadInstance(UnityYaml.Parse("%YAML 1.1\n--- !u!1001 &1\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: " + Gone + ", type: 3}\n")[0].Body, false);
            Assert.False(data.SourceMissing);
            var marked = PrefabLinks.ReadInstance(UnityYaml.Parse("%YAML 1.1\n--- !u!1001 &1\nPrefabInstance:\n  m_UnboundMissingPrefabName: x\n  m_SourcePrefab: {fileID: 100100000, guid: " + Gone + ", type: 3}\n")[0].Body, false);
            Assert.True(marked.SourceMissing);
        }
    }
}
