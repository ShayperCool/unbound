using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Xunit;

namespace UnityEngine.Tests
{
    public class NavMeshDetailHeightTests
    {
        [Theory]
        [InlineData(3.77734375f, 3.78125f, 11.33203125f, 11.34375f, 4.7216796875f, 4.7270627f)]
        [InlineData(3.23828125f, 3.2421875f, 22.66796875f, 22.6953125f, 2.4287109375f, 2.431640625f)]
        public void CollinearDetailTriangleDoesNotHideTheSurface(
            float bx, float bz, float cx, float cz, float queryX, float queryZ)
        {
            // Both triangles use the same sloped plane. The collinear one comes first,
            // and its rounded dot-product determinant can have either sign.
            var poly = new DtPoly(0, 3) { vertCount = 3 };
            poly.verts[0] = 0;
            poly.verts[1] = 1;
            poly.verts[2] = 2;
            var tile = new DtMeshTile(0)
            {
                data = new DtMeshData
                {
                    verts = new[] { 0f, 0f, 0f, 0f, 0f, 48f, 48f, 12f, 0f },
                    detailMeshes = new[] { new DtPolyDetail(0, 0, 2, 2) },
                    detailVerts = new[] { bx, bx / 4f, bz, cx, cx / 4f, cz },
                    detailTris = new[] { 0, 3, 4, 0, 0, 1, 2, 0 },
                },
            };
            var mesh = new DtNavMesh { dotProductDetailHeight = true };

            float height = mesh.PolyHeight(tile, poly, new RcVec3f(queryX, 0f, queryZ));

            Assert.Equal(queryX / 4f, height);
        }

        [Theory]
        [InlineData(3.77734375f, 3.78125f, 11.33203125f, 11.34377f)]
        [InlineData(8f, 1f, 24f, 3.025f)]
        public void ThinDetailTriangleKeepsItsHeightWhenDotProductsLosePrecision(
            float bx, float bz, float cx, float cz)
        {
            var a = RcVec3f.Zero;
            var b = new RcVec3f(bx, bx / 4f, bz);
            var c = new RcVec3f(cx, cx / 4f, cz);
            var point = (b + c) * 0.25f;

            Assert.True(DtUtils.ClosestHeightPointTriangle(point, a, b, c, out float previous));
            Assert.True(DtUtils.DotProductTriangleHeight(point, a, b, c, out float height));
            Assert.Equal(previous, height);
            Assert.InRange(height, point.Y - 0.00001f, point.Y + 0.00001f);
        }
    }
}
