using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;
using Unbound.Box2D.Dynamics;
using Unbound.Box2D.Dynamics.Contacts;
using Xunit;
using V = System.Numerics.Vector2;

namespace UnityEngine.Tests
{
    public sealed class Physics2DEdgeChainTests : IDisposable
    {
        readonly Physics2DMeasuredScope release = new Physics2DMeasuredScope();
        readonly List<Object> objects = new List<Object>();

        GameObject Root()
        {
            var root = new GameObject("edge"); objects.Add(root); return root;
        }
        static PhysicsShapeGroup2D Shapes(Collider2D collider)
        {
            Physics2D.SyncTransforms(); var group = new PhysicsShapeGroup2D(); collider.GetShapes(group); return group;
        }
        [Theory]
        [InlineData("6000.0.71f1", 1)]
        [InlineData("6000.0.72f1", 3)]
        public void TheMeasuredReleaseUsesOneConnectedShape(string version, int count)
        {
            Physics2DWorld.Reset(); ProjectInfo.UnityVersion = version;
            var root = Root(); var edge = root.AddComponent<EdgeCollider2D>();
            edge.points = new[] { Vector2.zero, Vector2.right, Vector2.one, new Vector2(2, 1) };
            edge.useAdjacentStartPoint = edge.useAdjacentEndPoint = true;
            edge.adjacentStartPoint = Vector2.left; edge.adjacentEndPoint = new Vector2(3, 1);
            var group = Shapes(edge);
            Assert.Equal(count, edge.shapeCount); Assert.Equal(count, group.shapeCount);
            Assert.Equal(3, edge.edgeCount); Assert.Equal(4, edge.pointCount);
            var shape = group.GetShape(0);
            if (count == 1)
            {
                Assert.Equal(4, shape.vertexCount); Assert.True(shape.useAdjacentStart); Assert.True(shape.useAdjacentEnd);
                Assert.Equal(Vector2.left, shape.adjacentStart); Assert.Equal(new Vector2(3, 1), shape.adjacentEnd);
                Assert.Throws<ArgumentOutOfRangeException>(() => edge.GetShapes(group, 1, 1));
                Assert.Equal(1, group.shapeCount);
            }
            else Assert.Equal(1, edge.GetShapes(group, 1, 1));
        }
        [Theory]
        [InlineData(0f, 4)]
        [InlineData(.0056f, 4)]
        [InlineData(.00568f, 5)]
        public void NearbyConsecutivePointsJoinAfterApplyingScale(float distance, int vertices)
        {
            var root = Root(); root.transform.localScale = new Vector3(1.25f, .75f, 1);
            var edge = root.AddComponent<EdgeCollider2D>();
            edge.points = new[] { Vector2.zero, new Vector2(distance, 0), Vector2.right, Vector2.one, new Vector2(2, 1) };
            var group = Shapes(edge);
            Assert.Equal(1, group.shapeCount); Assert.Equal(vertices, group.vertexCount);
            Assert.Equal(5, edge.pointCount); Assert.Equal(4, edge.edgeCount);
            Assert.Equal(ColliderErrorState2D.None, edge.errorState);
        }
        [Fact]
        public void CoincidentPointsRemoveTheShapeButDistantFinitePointsRemain()
        {
            var edge = Root().AddComponent<EdgeCollider2D>();
            edge.points = new[] { Vector2.one, Vector2.one, Vector2.one };
            Assert.Equal(0, Shapes(edge).shapeCount); Assert.Equal(ColliderErrorState2D.NoShapes, edge.errorState);
            edge.points = new[] { new Vector2(9000000, 0), new Vector2(9001000, 0) };
            var group = Shapes(edge);
            Assert.Equal(1, edge.shapeCount); Assert.Equal(2, group.vertexCount);
            Assert.Equal(new Vector2(9000000, 0), group.GetShapeVertex(0, 0));
        }
        [Fact]
        public void MovingTheBodyPreservesLocalVerticesAndMovesTheirWorldFrame()
        {
            var root = Root(); root.transform.position = new Vector3(.3f, -.2f, 0);
            root.transform.rotation = Quaternion.Euler(0, 0, 23); root.transform.localScale = new Vector3(1.25f, .75f, 1);
            var body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
            var edge = root.AddComponent<EdgeCollider2D>(); edge.points = new[] { Vector2.zero, Vector2.right, Vector2.one };
            var before = Shapes(edge); var vertices = before.groupVertices.ToArray();
            body.position += new Vector2(3, .25f); body.rotation += 17;
            var after = Shapes(edge);
            Assert.Equal(vertices, after.groupVertices);
            Assert.True(Vector3.Distance(before.localToWorldMatrix.MultiplyPoint3x4(vertices[0]),
                after.localToWorldMatrix.MultiplyPoint3x4(vertices[0])) > 1);
        }
        [Fact]
        public void IncreasingRadiusExtendsOverlapWithoutChangingAutomaticMass()
        {
            var root = Root(); var body = root.AddComponent<Rigidbody2D>(); body.useAutoMass = true;
            var box = root.AddComponent<BoxCollider2D>(); box.size = new Vector2(.5f, .5f); box.offset = new Vector2(0, 3);
            var edge = root.AddComponent<EdgeCollider2D>(); edge.points = new[] { Vector2.left, Vector2.right };
            Shapes(edge); Assert.Equal(.25f, body.mass); Assert.False(edge.OverlapPoint(new Vector2(0, .1f)));
            edge.edgeRadius = .125f; var group = Shapes(edge);
            Assert.Equal(.25f, body.mass); Assert.True(edge.OverlapPoint(new Vector2(0, .1f)));
            Assert.Equal(.125f, group.GetShape(0).radius, 6);
        }
        [Theory]
        [InlineData(false, 0f)]
        [InlineData(false, .125f)]
        [InlineData(true, 0f)]
        [InlineData(true, .125f)]
        public void NonfiniteSegmentDataDoesNotProduceNonfiniteRayHits(bool goodFirst, float radius)
        {
            var root = Root(); CircleCollider2D good = null;
            CircleCollider2D MakeGood()
            {
                var child = new GameObject("finite"); child.transform.SetParent(root.transform, false); child.layer = 1;
                var value = child.AddComponent<CircleCollider2D>(); value.offset = new Vector2(5, 0); return value;
            }
            if (goodFirst) good = MakeGood();
            var edge = root.AddComponent<EdgeCollider2D>(); edge.edgeRadius = radius;
            Assert.True(edge.SetPoints(new List<Vector2> { Vector2.zero, new Vector2(float.NaN, 0), Vector2.right }));
            if (!goodFirst) good = MakeGood(); Physics2D.SyncTransforms();
            Assert.Equal(1, edge.shapeCount); Assert.Equal(ColliderErrorState2D.None, edge.errorState);
            Assert.True(edge.OverlapPoint(new Vector2(.5f, 0)));
            Assert.Same(good, Physics2D.OverlapPoint(new Vector2(5, 0), 1 << 1));
            var hit = Physics2D.Raycast(new Vector2(5, 2), Vector2.down, 3);
            Assert.Same(good, hit.collider); Assert.True(float.IsFinite(hit.distance));
            Assert.True(float.IsFinite(hit.point.x) && float.IsFinite(hit.point.y));
        }
        [Fact]
        public void InvalidShapeRangesPreserveTheGroupAndTheEmptyOverloadClearsIt()
        {
            var box = Root().AddComponent<BoxCollider2D>(); var group = Shapes(box);
            group.localToWorldMatrix = Matrix4x4.Translate(new Vector3(7, 0, 0));
            foreach (var (index, count) in new[] { (-1, 1), (0, 0), (0, -1), (0, 2), (1, 1), (0, int.MaxValue) })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => box.GetShapes(group, index, count));
                Assert.Equal(1, group.shapeCount); Assert.Equal(7, group.localToWorldMatrix.m03);
            }
            box.enabled = false;
            Assert.Equal(0, box.GetShapes(group)); Assert.Equal(0, group.shapeCount); Assert.Equal(Matrix4x4.identity, group.localToWorldMatrix);
            Assert.Throws<ArgumentOutOfRangeException>(() => box.GetShapes(group, 0, 1));
        }
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void RepeatedContactEvaluationAndRoundedRaysReuseTheirStorage(bool circle, bool oneSided)
        {
            var world = new World(V.Zero);
            var chain = new ChainShape { OneSided = oneSided, Radius = Settings.PolygonRadius + .125f };
            chain.CreateChain(new[] { new V(-1, 0), new V(1, 0), new V(1, 1) }, 3, default, default);
            chain.HasPrevVertex = chain.HasNextVertex = false;
            var fixtureA = world.CreateBody(new BodyDef()).CreateFixture(chain, 0);
            var bodyB = world.CreateBody(new BodyDef { BodyType = BodyType.DynamicBody, Position = new V(0, oneSided ? -.4f : .4f) });
            Unbound.Box2D.Collision.Shapes.Shape moving;
            if (circle) moving = new CircleShape { Radius = .5f };
            else { var polygon = new PolygonShape(); polygon.SetAsBox(.5f, .5f); moving = polygon; }
            var fixtureB = bodyB.CreateFixture(moving, 1);
            Contact contact = circle ? new ChainAndCircleContact() : new ChainAndPolygonContact();
            contact.Initialize(fixtureA, 0, fixtureB, 0);
            var xfA = fixtureA.Body.GetTransform(); var xfB = fixtureB.Body.GetTransform(); var manifold = new Manifold();
            var input = new RayCastInput { P1 = new V(0, 1), P2 = new V(0, -1), MaxFraction = 1 };
            for (int i = 0; i < 64; i++) { contact.Evaluate(ref manifold, xfA, xfB); chain.RayCast(out _, input, xfA, 0); }
            long before = GC.GetAllocatedBytesForCurrentThread(); int hits = 0;
            for (int i = 0; i < 128; i++)
            {
                contact.Evaluate(ref manifold, xfA, xfB);
                if (chain.RayCast(out _, input, xfA, 0)) hits++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated); Assert.Equal(128, hits); Assert.True(manifold.PointCount > 0);
            Assert.True(chain.RayCast(out var hit, input, xfA, 0)); Assert.Equal(.4375f, hit.Fraction, 6);
        }
        [Theory]
        [InlineData(32)]
        [InlineData(512)]
        public void CastsRetainOnlyNearbyTargetChildren(int segments)
        {
            var source = Root().AddComponent<EdgeCollider2D>();
            var target = Root().AddComponent<EdgeCollider2D>();
            var points = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++) points[i] = new Vector2(i, 0);
            source.points = target.points = points;
            target.transform.position = new Vector3(0, .5f, 0);
            Physics2D.SyncTransforms();
            var scratch = new Physics2DQueries.CastScratch();
            var colliders = new Collider2D[] { source };
            var hits = Physics2DQueries.CastColliders(colliders, null, Vector2.up, 1,
                new ContactFilter2D().NoFilter(), null, scratch);
            Assert.Single(hits); Assert.Same(target, hits[0].collider);
            // The last source segment only overlaps neighboring proxies of each chain.
            Assert.InRange(scratch.Query.Children.Count, 2, 6);
            Assert.Contains(scratch.Query.Children, x => x.Fixture.UserData == target && x.Child == segments - 1);
            foreach (var child in scratch.Query.Children) Assert.InRange(child.Child, segments - 3, segments - 1);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OverlappingCastsAndDistancesReuseChainChildren(bool castEdge)
        {
            var edge = Root().AddComponent<EdgeCollider2D>();
            edge.points = new[] { Vector2.left, Vector2.right, Vector2.one };
            var circle = Root().AddComponent<CircleCollider2D>(); circle.offset = new Vector2(0, .1f);
            Physics2D.SyncTransforms();
            var scratch = new Physics2DQueries.CastScratch();
            var colliders = new Collider2D[] { castEdge ? edge : circle };
            var filter = new ContactFilter2D().NoFilter();
            ColliderDistance2D distance = default;
            List<RaycastHit2D> hits = null;
            for (int i = 0; i < 64; i++)
            {
                hits = Physics2DQueries.CastColliders(colliders, null, Vector2.right, 0, filter, null, scratch);
                distance = edge.Distance(circle);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
            {
                hits = Physics2DQueries.CastColliders(colliders, null, Vector2.right, 0, filter, null, scratch);
                distance = edge.Distance(circle);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated); Assert.Single(hits); Assert.True(distance.isValid && distance.isOverlapped);
            Assert.Same(castEdge ? circle : edge, hits[0].collider);
        }
        public void Dispose()
        {
            foreach (var value in objects) if (value) Object.DestroyImmediate(value);
            release.Dispose();
        }
    }
}
