using Xunit;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using B2Vector = System.Numerics.Vector2;
using B2Transform = Unbound.Box2D.Common.Transform;
using B2Rotation = Unbound.Box2D.Common.Rotation;

namespace UnityEngine.Tests
{
    public class Physics2DRoundedContactTests
    {
        [Fact]
        public void CapsuleTipsTouchAcrossDisjointCoreIntervals()
        {
            var a = new CapsuleShape(new B2Vector(0, -1), new B2Vector(0, 1), .5f);
            var b = new CapsuleShape(new B2Vector(0, -1), new B2Vector(0, 1), .5f);
            var xfA = new B2Transform(B2Vector.Zero, new B2Rotation(0));
            var xfB = new B2Transform(new B2Vector(0, 2.5f), new B2Rotation(0));
            var manifold = new Manifold();
            CollisionUtils.CollidePolygons(ref manifold, a, xfA, b, xfB);
            Assert.Equal(1, manifold.PointCount);
            var world = new WorldManifold(); world.Initialize(manifold, xfA, a.Radius, xfB, b.Radius);
            Assert.Equal(B2Vector.UnitY, world.Normal);
            Assert.Equal(new B2Vector(0, 1.25f), world.Points[0]);
            Assert.Equal(-.5f, world.Separations[0]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CollapsedCapsuleUsesOnePointAndAUnitNormal(bool reverse)
        {
            var capsule = new CapsuleShape(B2Vector.Zero, B2Vector.Zero, .5f);
            var box = new PolygonShape(); box.SetAsBox(.5f, .5f);
            var origin = new B2Transform(B2Vector.Zero, new B2Rotation(0));
            var tilted = new B2Transform(new B2Vector(-1.1f, 0), new B2Rotation(Mathf.PI / 4));
            PolygonShape a = reverse ? box : capsule, b = reverse ? capsule : box;
            var xfA = reverse ? tilted : origin; var xfB = reverse ? origin : tilted;
            var manifold = new Manifold();
            CollisionUtils.CollidePolygons(ref manifold, a, xfA, b, xfB);
            Assert.Equal(1, manifold.PointCount);
            var world = new WorldManifold(); world.Initialize(manifold, xfA, a.Radius, xfB, b.Radius);
            Assert.InRange(world.Normal.Length(), .999999f, 1.000001f);
            Assert.InRange(world.Normal.X * (reverse ? 1 : -1), .999999f, 1.000001f);
            Assert.InRange(world.Points[0].X, -.441448f, -.441445f);
            Assert.InRange(world.Separations[0], -.117108f, -.117105f);
            Assert.Equal(capsule.P1, capsule.P2);
        }
    }
}
