using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>A lens flare's element quads (Unity 6000.0.71f1, tests/projects/flares): built in
    /// view space at the flare's depth and placed by the camera's projection alone.</summary>
    public class FlareGeometryTests : IDisposable
    {
        readonly Texture2D m_Texture = new Texture2D(256, 128, TextureFormat.RGBA32, false);

        public void Dispose() => Object.DestroyImmediate(m_Texture);

        Halos.Source Source(Vector3 point, bool directional, params Flare.Element[] elements) => new Halos.Source
        {
            Asset = new Flare { m_FlareTexture = m_Texture, m_TextureLayout = 2, m_Elements = elements },
            Point = point,
            Directional = directional,
            LightColor = Vector3.one,
            Brightness = 1f,
        };

        static Flare.Element Element(float size, float position = 0f, bool rotate = false, bool zoom = false) =>
            new Flare.Element { Size = size, Position = position, Rotate = rotate, Zoom = zoom, Color = Color.white };

        /// <summary>The pixel a view-space vertex lands on under a projection.</summary>
        static Vector2 Pixel(in Matrix4x4 projection, Vector3 v, float width, float height)
        {
            var clip = projection * new Vector4(v.x, v.y, v.z, 1f);
            return new Vector2((clip.x / clip.w * 0.5f + 0.5f) * width, (clip.y / clip.w * 0.5f + 0.5f) * height);
        }

        [Fact]
        public void AnElementIsAsBigOnScreenAtAnyDepthUnderAPerspectiveCamera()
        {
            var projection = Matrix4x4.Perspective(60f, 1f, 0.3f, 1000f);
            float expected = 20f * 0.01f * projection.m11 * 64f;
            foreach (float depth in new[] { 1f, 5f, 400f })
            {
                var source = Source(new Vector3(0.25f * depth, -0.125f * depth, depth), false, Element(20f));
                var point = Halos.FlarePoint(Matrix4x4.Scale(new Vector3(1f, 1f, -1f)), source, 0.3f);
                Assert.Equal(new Vector3(0.25f * depth, -0.125f * depth, -depth), point);
                Assert.True(Halos.BuildFlare(source, 1f, point));
                var pixels = Halos.BuiltVertices.Select(v => Pixel(projection, v, 128f, 128f)).ToArray();
                // In front of the camera the first corner is the top right one, with the image's top right.
                Assert.True(pixels[0].x > pixels[2].x && pixels[0].y > pixels[2].y);
                Assert.Equal(new Vector2(1f - 0.5f / 256f, 1f - 0.5f / 128f), Halos.BuiltUvs[0]);
                Assert.Equal(new Vector2(0.5f / 256f, 0.5f / 128f), Halos.BuiltUvs[2]);
                Assert.Equal(expected, (pixels[0].x - pixels[2].x) * 0.5f, 3);
                Assert.Equal(expected, (pixels[0].y - pixels[2].y) * 0.5f, 3);
                Assert.All(Halos.BuiltVertices, v => Assert.Equal(-depth, v.z));
            }
        }

        /// <summary>An orthographic camera does not divide by the depth: the element grows with it.</summary>
        [Fact]
        public void AnOrthographicCameraScalesAnElementByItsDepth()
        {
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            foreach (float depth in new[] { 2f, 8f })
            {
                var source = Source(new Vector3(0f, 0f, depth), false, Element(10f));
                Assert.True(Halos.BuildFlare(source, 1f, Halos.FlarePoint(view, source, 0.3f)));
                Assert.Equal(10f * depth * 0.01f, Math.Abs(Halos.BuiltVertices[0].x), 6);
            }
        }

        /// <summary>Elements lie on the line from the flare to the view axis; a rotating one is a
        /// diamond whose first corner points away from the axis, 1.4 half sides out; on the axis
        /// it points along +x.</summary>
        [Fact]
        public void ElementsFollowTheLineToTheAxisAndRotateAwayFromIt()
        {
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            var source = Source(new Vector3(3f, 4f, 10f), false, Element(10f, 0.5f), Element(10f, 1f), Element(10f, -1f), Element(10f, 0f, rotate: true));
            Assert.True(Halos.BuildFlare(source, 1f, Halos.FlarePoint(view, source, 0.3f)));
            Vector3 Center(int element) => (Halos.BuiltVertices[element * 4] + Halos.BuiltVertices[element * 4 + 2]) * 0.5f;
            Assert.Equal(new Vector3(1.5f, 2f, -10f), Center(0));
            Assert.Equal(new Vector3(0f, 0f, -10f), Center(1));
            Assert.Equal(new Vector3(6f, 8f, -10f), Center(2));
            var corner = Halos.BuiltVertices[12] - Center(3);
            Assert.Equal(1.4f, corner.magnitude, 5);
            Assert.Equal(0.6f, corner.x / corner.magnitude, 5);
            Assert.Equal(0.8f, corner.y / corner.magnitude, 5);

            var onAxis = Source(new Vector3(0f, 0f, 10f), false, Element(10f, 0f, rotate: true));
            Assert.True(Halos.BuildFlare(onAxis, 1f, Halos.FlarePoint(view, onAxis, 0.3f)));
            Assert.Equal(new Vector3(1.4f, 0f, -10f), Halos.BuiltVertices[0]);
        }

        /// <summary>A zooming element scales with the brightness and visibility, the others do
        /// not; nothing is built without a size.</summary>
        [Fact]
        public void AZoomingElementScalesWithTheVisibility()
        {
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            var source = Source(new Vector3(0f, 0f, 10f), false, Element(10f, 0f, zoom: true), Element(10f), Element(0f));
            Assert.True(Halos.BuildFlare(source, 0.25f, Halos.FlarePoint(view, source, 0.3f)));
            Assert.Equal(8, Halos.BuiltVertices.Count);
            Assert.Equal(0.25f, Halos.BuiltVertices[0].x, 6);
            Assert.Equal(1f, Halos.BuiltVertices[4].x, 6);
            source.Brightness = 2f;
            Assert.True(Halos.BuildFlare(source, 0.25f, Halos.FlarePoint(view, source, 0.3f)));
            Assert.Equal(0.5f, Halos.BuiltVertices[0].x, 6);
            Assert.Equal(1f, Halos.BuiltVertices[4].x, 6);
        }

        /// <summary>A directional flare is its direction at twice the near plane's distance,
        /// whatever the camera's position.</summary>
        [Fact]
        public void ADirectionalFlareSitsAtTwiceTheNearPlane()
        {
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.Translate(new Vector3(100f, 50f, 25f));
            var direction = new Vector3(0.6f, 0f, 0.8f);
            var point = Halos.FlarePoint(view, Source(direction, true), 0.3f);
            Assert.Equal(0.36f, point.x, 6);
            Assert.Equal(0f, point.y, 6);
            Assert.Equal(-0.48f, point.z, 6);
        }

        /// <summary>The image cells of the layouts, half a texel in from each side; the large and
        /// medium images of layout 1 keep their left and top sides whole.</summary>
        [Fact]
        public void ImagesAreTheirCellsLessHalfATexel()
        {
            const float U = 0.5f / 256f, V = 0.5f / 128f;
            static (float, float, float, float) Uv(int layout, int image)
            {
                Halos.ImageUv(layout, image, U, V, out float uMin, out float uMax, out float vMin, out float vMax);
                return (uMin, uMax, vMin, vMax);
            }
            Assert.Equal((U, 1f - U, V, 1f - V), Uv(2, 0));
            Assert.Equal((U, 1f - U, 0.5f + V, 1f - V), Uv(0, 0));
            Assert.Equal((0.5f + U, 1f - U, 0.25f + V, 0.5f - V), Uv(0, 2));
            Assert.Equal((U, 0.5f - U, V, 0.25f - V), Uv(0, 3));
            Assert.Equal((0f, 1f - U, 0.5f + V, 1f), Uv(1, 0));
            Assert.Equal((0f, 0.5f - U, 0.25f + V, 0.5f), Uv(1, 1));
            Assert.Equal((0f, 0.5f - U, V, 0.25f), Uv(1, 2));
            Assert.Equal((0.5f + U, 0.75f - U, 0.375f + V, 0.5f - V), Uv(1, 3));
            Assert.Equal((0.75f + U, 1f - U, 0.25f + V, 0.375f - V), Uv(1, 6));
            Assert.Equal((0.5f + U, 1f - U, 0.5f + V, 1f - V), Uv(3, 1));
            Assert.Equal((1f / 3f + U, 2f * (1f / 3f) - U, 1f - 2f * (1f / 3f) + V, 1f - 1f / 3f - V), Uv(4, 4));
            Assert.Equal((0.75f + U, 1f - U, V, 0.25f - V), Uv(5, 15));
        }
    }
}
