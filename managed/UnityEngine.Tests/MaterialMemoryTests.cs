using System;
using Unbound.Runtime;
using UnityEngine.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MaterialMemoryTests : IDisposable
    {
        const string PlainShader = "Shader \"Tests/MaterialMemory\" { Properties { _Parameter (\"P\", Float) = 0.5 } SubShader { Pass {} } }";
        readonly GraphicsDeviceProfile m_PreviousDevice = GraphicsDeviceProfile.Current;
        readonly string m_PreviousThreading = GraphicsDeviceProfile.CurrentThreadingMode.ToString();

        public MaterialMemoryTests() => GraphicsDeviceProfile.Select("Metal");

        public void Dispose() => GraphicsDeviceProfile.Select(m_PreviousDevice.Type.ToString(), m_PreviousThreading);

        static Shader CreateShader(string source)
        {
            var shader = Shader.Create("Tests/MaterialMemory", 2000);
            BuiltinShaders.ApplySource(shader, source);
            return shader;
        }

        [Fact]
        public void AnUnmeasuredWriteLeavesTheExistingPropertyBehaviorAndReportsUnknownSize()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var material = new Material(shader);
            try
            {
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(material));
                material.SetFloat("_Parameter", 8f);
                Assert.Equal(8f, material.GetFloat("_Parameter"));
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(material));
                var matrix = Matrix4x4.Translate(new Vector3(1, 2, 3));
                material.SetMatrix("_UnmeasuredMatrix", matrix);
                Assert.Equal(matrix, material.GetMatrix("_UnmeasuredMatrix"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Theory]
        [InlineData("Shader \"Tests/MaterialMemory\" { Properties { _Known (\"P\", Float) = 0.5 _Unknown (\"Q\", CustomType) = 3 } SubShader { Pass {} } }")]
        [InlineData("Shader \"Tests/MaterialMemory\" { Properties { _Known (\"P\", Float) = 0.5")]
        public void APartialShaderPropertyLayoutIsNotUsedForTheProfiler(string source)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(source);
            var material = new Material(shader);
            try
            {
                Assert.Equal(0.5f, material.GetFloat("_Known"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void AReparsedShaderLayoutIsOnlyMeasuredForNewMaterials()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var original = new Material(shader);
            try
            {
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(original));
                const string updated = "Shader \"Tests/MaterialMemory\" { Properties { _Parameter (\"P\", Color) = (0, 0, 0, 1) } SubShader { Pass {} } }";
                BuiltinShaders.ApplySource(shader, updated);
                var fresh = new Material(shader);
                try
                {
                    Assert.Equal(1888, Profiler.GetRuntimeMemorySizeLong(fresh));
                    Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(original));
                }
                finally { Object.DestroyImmediate(fresh); }
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void AnUnmeasuredReleaseKeepsMaterialAllocationUnknown()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var shader = CreateShader(PlainShader);
            var material = new Material(shader);
            try
            {
                Assert.Equal(0.5f, material.GetFloat("_Parameter"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }
    }
}
