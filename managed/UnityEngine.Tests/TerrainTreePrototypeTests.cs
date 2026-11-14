using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A tree prototype whose prefab is not an asset (a scene or run-time object) is an
    /// error when the prototypes are set, and its trees are not drawn (tests/projects/treebillboards
    /// `wide`). Unbound's choices, unmeasured: the same error on RefreshPrototypes, the prototype
    /// kept in the list, nothing said for a missing prefab.</summary>
    public class TerrainTreePrototypeTests
    {
        sealed class ErrorCapture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Inner = Debug.unityLogger.logHandler;
            internal readonly List<string> Errors = new List<string>();
            internal ErrorCapture() { Debug.unityLogger.logHandler = this; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Errors.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
            public void Dispose() => Debug.unityLogger.logHandler = m_Inner;
        }

        [Fact]
        public void PrototypesWhosePrefabIsNotAnAssetAreErrorsAndNotDrawn()
        {
            var data = new TerrainData { heightmapResolution = 33 };
            var runtime = new GameObject("Runtime Tree");
            var asset = new GameObject("Asset Tree") { m_IsPrefabAsset = true };
            try
            {
                using (var capture = new ErrorCapture())
                {
                    data.treePrototypes = new[] { new TreePrototype { prefab = asset }, new TreePrototype { prefab = runtime } };
                    Assert.Equal(new[] { "Tree 'Runtime Tree' couldn't be selected because it is not persistent." }, capture.Errors);
                    data.RefreshPrototypes();
                    Assert.Equal(2, capture.Errors.Count);
                    data.treePrototypes = new[] { new TreePrototype { prefab = asset }, new TreePrototype() };
                    Assert.Equal(2, capture.Errors.Count);
                }
                Assert.Equal(2, data.treePrototypes.Length);
                Assert.True(TerrainData.IsPersistentPrefab(asset));
                Assert.False(TerrainData.IsPersistentPrefab(runtime));
                Assert.False(TerrainData.IsPersistentPrefab(null));
            }
            finally
            {
                Object.DestroyImmediate(runtime);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(data);
            }
        }
    }
}
