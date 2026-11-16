using System;
using System.Collections.Generic;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Fewer vertices than a mesh's indices reference. Manual 6000.0 (Mesh): "Unity always checks
    /// the supplied triangle indices whether they don't reference out of bounds vertices"; the
    /// 6000.0.71f1 Editor (tests/projects/blendshapes misuse "vertices change", and the macOS Editor
    /// for the other cases here) logs "Mesh.vertices is too small. ..." and keeps the mesh as it
    /// was, through the vertices setter and SetVertices alike, null counting as no vertices and the
    /// indices of every topology counting. A shorter array that still covers the indices is taken.
    /// </summary>
    public sealed class MeshVertexCountGuardTests
    {
        const string TooSmall = "Mesh.vertices is too small. The supplied vertex array has less vertices than are referenced by the triangles array.";

        sealed class Capture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
            public readonly List<string> Errors = new List<string>();
            public Capture() { Debug.unityLogger.logHandler = this; }
            public void Dispose() { Debug.unityLogger.logHandler = m_Previous; }
            public void LogException(Exception exception, Object context) => Errors.Add(exception.GetType().Name);
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Errors.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }
        }

        /// <summary>A 3x3 grid (vertices at (i % 3, i / 3)), UVs, four triangles reaching vertex 8.</summary>
        static Mesh Grid()
        {
            var mesh = new Mesh();
            var vertices = new Vector3[9];
            for (int i = 0; i < 9; i++) vertices[i] = new Vector3(i % 3, i / 3, 0f);
            mesh.vertices = vertices;
            mesh.uv = new Vector2[9];
            mesh.triangles = new[] { 0, 1, 3, 1, 4, 3, 4, 5, 8, 2, 5, 8 };
            return mesh;
        }

        static void Unchanged(Mesh mesh)
        {
            Assert.Equal(9, mesh.vertexCount);
            Assert.Equal(new Vector3(2f, 2f, 0f), mesh.vertices[8]);
            Assert.Equal(12, mesh.triangles.Length);
            Assert.Equal(9, mesh.uv.Length);
            Assert.Equal(new Vector3(1f, 1f, 0f), mesh.bounds.center);
        }

        public static IEnumerable<object[]> Refused()
        {
            yield return new object[] { "vertices", (Action<Mesh>)(m => m.vertices = new Vector3[4]) };
            yield return new object[] { "SetVertices list", (Action<Mesh>)(m => m.SetVertices(new List<Vector3>(new Vector3[4]))) };
            yield return new object[] { "SetVertices array", (Action<Mesh>)(m => m.SetVertices(new Vector3[4])) };
            yield return new object[] { "SetVertices range", (Action<Mesh>)(m => m.SetVertices(new List<Vector3>(new Vector3[9]), 0, 4)) };
            yield return new object[] { "vertices null", (Action<Mesh>)(m => m.vertices = null) };
            yield return new object[] { "SetVertices null list", (Action<Mesh>)(m => m.SetVertices((List<Vector3>)null)) };
        }

        [Theory]
        [MemberData(nameof(Refused))]
        public void FewerVerticesThanTheIndicesReachAreRefused(string how, Action<Mesh> assign)
        {
            var mesh = Grid();
            try
            {
                using var capture = new Capture();
                assign(mesh);
                Assert.True(capture.Errors.Count == 1 && capture.Errors[0] == TooSmall, how + ": " + string.Join(" | ", capture.Errors));
                Unchanged(mesh);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void PointIndicesCountToo()
        {
            var mesh = Grid();
            try
            {
                mesh.SetIndices(new[] { 0, 1, 7 }, MeshTopology.Points, 0);
                using var capture = new Capture();
                mesh.vertices = new Vector3[5];
                Assert.Equal(new[] { TooSmall }, capture.Errors);
                Assert.Equal(9, mesh.vertexCount);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void AShorterArrayCoveringTheIndicesIsTaken()
        {
            var mesh = Grid();
            try
            {
                mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                var five = new Vector3[5];
                for (int i = 0; i < 5; i++) five[i] = Vector3.one * 7f;
                using var capture = new Capture();
                mesh.vertices = five;
                Assert.Empty(capture.Errors);
                Assert.Equal(5, mesh.vertexCount);
                Assert.Equal(Vector3.one * 7f, mesh.vertices[0]);
                Assert.Equal(3, mesh.triangles.Length);
                // A mesh without indices takes any count (macOS Editor: 4 then 2 vertices).
                var bare = new Mesh();
                bare.vertices = new Vector3[4];
                bare.vertices = new Vector3[2];
                Assert.Equal(2, bare.vertexCount);
                Assert.Empty(capture.Errors);
                Object.DestroyImmediate(bare);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
