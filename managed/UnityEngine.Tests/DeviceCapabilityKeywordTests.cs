using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The device capability keywords a device enables globally from the start: the
    /// macOS Editor's Metal device the native 16-bit, 32-wide and any-size wave ones
    /// (tests/projects/shaderkeywords metal.jsonl `global.enabled.initial`); OpenGL Core none. A
    /// script may still disable and enable them; another device's selection replaces them.</summary>
    public sealed class DeviceCapabilityKeywordTests
    {
        static readonly string[] Metal =
            { "UNITY_DEVICE_SUPPORTS_NATIVE_16BIT", "UNITY_DEVICE_SUPPORTS_WAVE_32", "UNITY_DEVICE_SUPPORTS_WAVE_ANY" };

        [Fact]
        public void MetalEnablesItsCapabilityKeywordsAndOtherDevicesNone()
        {
            var previous = GraphicsDeviceProfile.Current;
            try
            {
                GraphicsDeviceProfile.Select("OpenGLCore");
                foreach (var k in Metal) Assert.False(Shader.IsKeywordEnabled(k));
                GraphicsDeviceProfile.Select("Metal");
                foreach (var k in Metal) Assert.True(Shader.IsKeywordEnabled(k));
                Assert.False(Shader.IsKeywordEnabled("UNITY_DEVICE_SUPPORTS_WAVE_64"));
                Shader.DisableKeyword("UNITY_DEVICE_SUPPORTS_WAVE_32");
                Assert.False(Shader.IsKeywordEnabled("UNITY_DEVICE_SUPPORTS_WAVE_32"));
                Shader.EnableKeyword("UNITY_DEVICE_SUPPORTS_WAVE_32");
                Assert.True(Shader.IsKeywordEnabled("UNITY_DEVICE_SUPPORTS_WAVE_32"));
                GraphicsDeviceProfile.Select("OpenGLCore");
                foreach (var k in Metal) Assert.False(Shader.IsKeywordEnabled(k));
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }
    }
}
