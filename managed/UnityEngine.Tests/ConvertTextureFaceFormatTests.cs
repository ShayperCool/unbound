using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The target Graphics.ConvertTexture copies a cubemap's face into: the face's own
    /// format where the device renders into it, else the device's compatible render format, drawn
    /// by sampling. The macOS Editor converts an RGB24 cubemap's face on Metal, which renders into
    /// no RGB24 target (tests/projects/cubecompression, its Metal reference).</summary>
    public class ConvertTextureFaceFormatTests
    {
        /// <summary>The cube's own format, the face target's format and whether the face is
        /// drawn, with <paramref name="device"/>'s answers.</summary>
        static (GraphicsFormat Own, GraphicsFormat Face, bool Drawn, bool Renders) FaceOn(string device, TextureFormat format, bool linear)
        {
            var previous = GraphicsDeviceProfile.Current;
            var cube = new Cubemap(8, format, false, linear);
            try
            {
                GraphicsDeviceProfile.Select(device);
                var face = Graphics.ConvertFaceFormat(cube, out bool drawn);
                return (cube.graphicsFormat, face, drawn, SystemInfo.IsFormatSupported(face, GraphicsFormatUsage.Render));
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
                Object.DestroyImmediate(cube);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnRgb24FaceIsDrawnIntoTheDevicesRenderFormatOnMetal(bool linear)
        {
            var face = FaceOn("Metal", TextureFormat.RGB24, linear);
            Assert.True(face.Drawn);
            Assert.True(face.Renders);
            Assert.NotEqual(face.Own, face.Face);
            Assert.Equal(GraphicsFormatUtility.IsSRGBFormat(face.Own), GraphicsFormatUtility.IsSRGBFormat(face.Face));
            Assert.Equal(4u, GraphicsFormatUtility.GetComponentCount(face.Face));
        }

        [Theory]
        [InlineData("Metal", TextureFormat.RGBA32, false)]
        [InlineData("Metal", TextureFormat.RGBAHalf, true)]
        // RGB24 where the device renders into it: OpenGL Core, the Linux reference's device.
        [InlineData("OpenGLCore", TextureFormat.RGB24, false)]
        public void AFormatTheDeviceRendersIntoIsCopied(string device, TextureFormat format, bool linear)
        {
            var face = FaceOn(device, format, linear);
            Assert.False(face.Drawn);
            Assert.True(face.Renders);
            Assert.Equal(face.Own, face.Face);
        }
    }
}
