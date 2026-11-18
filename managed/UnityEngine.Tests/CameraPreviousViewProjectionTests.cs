using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Camera.previousViewProjectionMatrix takes the device's projection for a render
    /// texture (GL.GetGPUProjectionMatrix with renderIntoTexture), as the motion vector texture is
    /// drawn: tests/projects/builtinrp, every previousViewProjection row of the macOS Metal Editor is
    /// the OpenGL Core Editor's row so mapped.</summary>
    public sealed class CameraPreviousViewProjectionTests
    {
        static string Reference(string relative, [CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "tests", "projects", "builtinrp",
                "Reference", relative));

        static Dictionary<string, Matrix4x4> Matrices(string path)
        {
            var rows = new Dictionary<string, Matrix4x4>();
            foreach (var line in File.ReadLines(path))
            {
                using var row = JsonDocument.Parse(line);
                var key = row.RootElement.GetProperty("k").GetString();
                if (!key.EndsWith(".previousViewProjection", StringComparison.Ordinal)) continue;
                var m = new Matrix4x4();
                int i = 0;
                // Row-major, as the probe writes it.
                foreach (var v in row.RootElement.GetProperty("v").EnumerateArray()) { m[i / 4, i % 4] = v.GetSingle(); i++; }
                rows[key] = m;
            }
            return rows;
        }

        static void Select(string device, Action body)
        {
            var previous = GraphicsDeviceProfile.Current;
            try
            {
                GraphicsDeviceProfile.Select(device);
                body();
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }

        [Fact]
        public void TheMetalEditorsMatricesAreTheOpenGLCoreOnesMappedForARenderTexture()
        {
            var opengl = Matrices(Reference("values.jsonl"));
            var metal = Matrices(Reference(Path.Combine("metal", "values.jsonl")));
            Assert.Equal(10, metal.Count);
            Assert.Equal(opengl.Keys, metal.Keys);
            Select("Metal", () =>
            {
                foreach (var (key, expected) in metal)
                {
                    var mapped = GraphicsDeviceProfile.Current.GpuProjection(opengl[key], true);
                    for (int i = 0; i < 16; i++)
                        Assert.True(Math.Abs(mapped[i] - expected[i]) <= 2e-6f * Math.Max(1f, Math.Abs(expected[i])),
                            $"{key}[{i}]: {mapped[i]} vs {expected[i]}");
                }
            });
        }

        [Theory]
        [InlineData("OpenGLCore")]
        [InlineData("Vulkan")]
        [InlineData("Metal")]
        public void ACamerasPreviousViewProjectionIsItsDevicesForARenderTexture(string device)
        {
            Select(device, () =>
            {
                var go = new GameObject("camera");
                try
                {
                    go.transform.SetPositionAndRotation(new Vector3(0f, 2f, -5f), Quaternion.Euler(20f, 10f, 0f));
                    // A new camera keeps the view-projection it has when it is made (the screen's
                    // aspect): nothing here changes it before the comparison.
                    var camera = go.AddComponent<Camera>();
                    var expected = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true) * camera.worldToCameraMatrix;
                    Assert.Equal(expected, camera.previousViewProjectionMatrix);
                    // The device's mapping changes the matrix there, OpenGL Core keeps it: the
                    // control that the test tells the conventions apart.
                    var opengl = camera.nonJitteredProjectionMatrix * camera.worldToCameraMatrix;
                    Assert.Equal(device == "OpenGLCore", opengl == camera.previousViewProjectionMatrix);
                    Assert.Equal(camera.previousViewProjectionMatrix, camera.MotionViewProjection(camera.worldToCameraMatrix));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            });
        }
    }
}
