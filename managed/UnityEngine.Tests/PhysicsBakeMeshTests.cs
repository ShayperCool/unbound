using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Physics.BakeMesh as the Unity 6000.0.71f1 Editor answers it (tests/projects/bakemesh): a
    /// mesh, also from another thread, is taken without a word; a null id, the id of no living
    /// object and the id of something else than a mesh are warned about, the last without its name.
    /// </summary>
    public class PhysicsBakeMeshTests
    {
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
                lock (m_Logs) m_Logs.Add(type + ": " + string.Format(format, args));
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        static List<string> Logged(Action action)
        {
            var logs = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new CaptureHandler(previous, logs);
            try { action(); }
            finally { Debug.unityLogger.logHandler = previous; }
            return logs;
        }

        [Fact]
        public void AMeshIsBakedWithoutAWordAlsoFromAnotherThread()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            try
            {
                Assert.Empty(Logged(() => Physics.BakeMesh(mesh.GetInstanceID(), false)));
                Assert.Empty(Logged(() => Physics.BakeMesh(mesh.GetInstanceID(), true, MeshColliderCookingOptions.None)));
                Assert.Empty(Logged(() => Task.Run(() => Physics.BakeMesh(mesh.GetInstanceID(), false)).Wait()));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void IdsOfNoMeshAreWarnedAbout()
        {
            Assert.Equal(new[] { "Warning: Null mesh instance id is not allowed." }, Logged(() => Physics.BakeMesh(0, false)));
            var gone = new Mesh();
            int id = gone.GetInstanceID();
            Object.DestroyImmediate(gone);
            Assert.Equal(new[] { "Warning: Failed to resolve instance id: " }, Logged(() => Physics.BakeMesh(id, false)));
            var texture = new Texture2D(2, 2) { name = "named texture" };
            try
            {
                Assert.Equal(new[] { "Warning: Resolved the instance id to a valid object, but that object is not a Mesh: " },
                    Logged(() => Physics.BakeMesh(texture.GetInstanceID(), true)));
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
