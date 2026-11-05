using System;
using System.Collections.Generic;
using Xunit;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using B2Shape = Unbound.Box2D.Collision.Shapes.Shape;
using B2Vector = System.Numerics.Vector2;
using B2Transform = Unbound.Box2D.Common.Transform;
using B2Rotation = Unbound.Box2D.Common.Rotation;
using B2Settings = Unbound.Box2D.Common.Settings;

namespace UnityEngine.Tests
{
    // Unity 6000.0 EdgeCollider2D is "an arbitrary set of connected edges"; useAdjacentStartPoint
    // and useAdjacentEndPoint form the collision normal at the chain's start and end points from
    // the adjacent points, and without them that normal follows the direction of motion.
    public class EdgeCollider2DAdjacencyTests : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();

        public EdgeCollider2DAdjacencyTests()
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.autoSyncTransforms = false;
            Physics2D.gravity = Vector2.zero;
        }

        public void Dispose()
        {
            foreach (var o in objects) if (o) Object.DestroyImmediate(o);
            Physics2D.gravity = new Vector2(0, -9.81f);
            Physics2D.Simulate(.02f);
        }

        EdgeCollider2D Edge(string name, params Vector2[] points)
        {
            var go = new GameObject(name); objects.Add(go);
            var edge = go.AddComponent<EdgeCollider2D>();
            edge.points = points;
            return edge;
        }

        // A unit box without friction, rotation or gravity: only the contact normals change its velocity.
        // A nonzero angle tilts the box, which then keeps that angle.
        Rigidbody2D Box(Vector2 center, Vector2 velocity, float gravityScale = 0f, float angle = 0f)
        {
            var go = new GameObject("box"); objects.Add(go);
            go.transform.position = center;
            if (angle != 0f) go.transform.rotation = Quaternion.Euler(0, 0, angle);
            go.AddComponent<BoxCollider2D>().sharedMaterial = new PhysicsMaterial2D { friction = 0f, bounciness = 0f };
            var body = go.AddComponent<Rigidbody2D>();
            body.freezeRotation = true;
            body.gravityScale = gravityScale;
            body.linearVelocity = velocity;
            return body;
        }

        // Contact normals point from the edge towards the box.
        static void AssertContactNormals(Rigidbody2D body, float sign)
        {
            var contacts = new ContactPoint2D[16];
            int count = body.GetContacts(contacts);
            Assert.True(count > 0, "the box touches the edge");
            for (int i = 0; i < count; i++)
                Assert.True(contacts[i].normal.y * sign > .999f, $"contact {i} normal {contacts[i].normal}");
        }

        // Collinear segments joined at x = 0. The box's core overlaps the edge by 0.01 and its
        // leading corner is 0.002 past the connection, so the side face of the box is the least
        // separating polygon axis of the next segment. An unconnected segment collides on that
        // axis (a ghost collision) and stops the box; a connected one keeps the face normal.
        // The edge is two-sided: the same holds from above and from below.
        [Theory]
        [InlineData(1f)]
        [InlineData(-1f)]
        public void BoxCrossingAnInternalVertexKeepsItsVelocityAndFaceNormal(float side)
        {
            Edge("ground", new Vector2(-6, 0), new Vector2(-3, 0), new Vector2(0, 0), new Vector2(3, 0), new Vector2(6, 0));
            var body = Box(new Vector2(-.498f, .49f * side), new Vector2(5, 0));
            Physics2D.SyncTransforms();
            Physics2D.Simulate(.02f);
            Assert.InRange(body.linearVelocity.x, 4.999f, 5.001f);
            AssertContactNormals(body, side);
        }

        // A box resting on a straight edge of many segments slides across every connection.
        [Fact]
        public void BoxSlidesAlongACollinearMultiSegmentEdge()
        {
            Physics2D.gravity = new Vector2(0, -9.81f);
            var points = new Vector2[11];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(i - 5, 0);
            Edge("ground", points);
            var body = Box(new Vector2(-3.25f, .5f), new Vector2(4, 0), 1f);
            Physics2D.SyncTransforms();
            for (int step = 0; step < 50; step++)
            {
                Physics2D.Simulate(.02f);
                Assert.InRange(body.linearVelocity.x, 3.999f, 4.001f);
                AssertContactNormals(body, 1f);
            }
            Assert.True(body.position.x > .7f, $"position {body.position}");
        }

        // Two separate edge colliders meet at x = 0; the box crosses from one onto the other with
        // its leading corner 0.002 past the shared point. Only an adjacent point given on that end
        // connects the chain end; otherwise the end vertex collides along the direction of motion.
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void AdjacentPointsConnectTheChainEnds(bool start, bool useAdjacent)
        {
            var left = Edge("left", new Vector2(-4, 0), new Vector2(0, 0));
            var right = Edge("right", new Vector2(0, 0), new Vector2(4, 0));
            Rigidbody2D body;
            if (start)
            {
                right.useAdjacentStartPoint = useAdjacent;
                right.adjacentStartPoint = new Vector2(-1, 0);
                body = Box(new Vector2(-.498f, .49f), new Vector2(5, 0));
            }
            else
            {
                left.useAdjacentEndPoint = useAdjacent;
                left.adjacentEndPoint = new Vector2(1, 0);
                body = Box(new Vector2(.498f, .49f), new Vector2(-5, 0));
            }
            Physics2D.SyncTransforms();
            Physics2D.Simulate(.02f);
            float direction = start ? 1f : -1f;
            if (useAdjacent)
            {
                Assert.InRange(body.linearVelocity.x * direction, 4.999f, 5.001f);
                AssertContactNormals(body, 1f);
            }
            else
            {
                Assert.InRange(body.linearVelocity.x * direction, -1f, 1f);
                var contacts = new ContactPoint2D[16];
                int count = body.GetContacts(contacts);
                bool sideways = false;
                for (int i = 0; i < count; i++) sideways |= contacts[i].normal.x * direction < -.999f;
                Assert.True(sideways, "the unconnected end vertex collides against the motion");
            }
        }

        static List<EdgeShape> Shapes(EdgeCollider2D edge)
        {
            var shapes = new List<B2Shape>();
            edge.BuildShapes(Matrix4x4.identity, shapes);
            var edges = new List<EdgeShape>();
            foreach (var shape in shapes) edges.Add(Assert.IsType<EdgeShape>(shape));
            return edges;
        }

        [Fact]
        public void SegmentsAreConnectedTwoSidedEdgesWithOptionalEndPoints()
        {
            var edge = Edge("chain", new Vector2(-2, 0), new Vector2(0, 1), new Vector2(2, 0));
            var shapes = Shapes(edge);
            Assert.Equal(2, shapes.Count);
            Assert.All(shapes, s => Assert.False(s.OneSided));
            Assert.False(shapes[0].HasVertex0);
            Assert.True(shapes[0].HasVertex3);
            Assert.Equal(new B2Vector(2, 0), shapes[0].Vertex3);
            Assert.True(shapes[1].HasVertex0);
            Assert.Equal(new B2Vector(-2, 0), shapes[1].Vertex0);
            Assert.False(shapes[1].HasVertex3);

            edge.useAdjacentStartPoint = true;
            edge.adjacentStartPoint = new Vector2(-3, -1);
            edge.useAdjacentEndPoint = true;
            edge.adjacentEndPoint = new Vector2(3, -1);
            shapes = Shapes(edge);
            Assert.True(shapes[0].HasVertex0);
            Assert.Equal(new B2Vector(-3, -1), shapes[0].Vertex0);
            Assert.True(shapes[1].HasVertex3);
            Assert.Equal(new B2Vector(3, -1), shapes[1].Vertex3);
            Assert.Equal(new B2Vector(2, 0), shapes[0].Vertex3);
            Assert.Equal(new B2Vector(-2, 0), shapes[1].Vertex0);
        }

        // A single segment without adjacent points stays an isolated two-sided edge, so its
        // contacts take the unchanged edge path.
        [Fact]
        public void SingleSegmentWithoutAdjacentPointsStaysIsolated()
        {
            var shape = Assert.Single(Shapes(Edge("single", new Vector2(-1, 0), new Vector2(1, 0))));
            Assert.False(shape.OneSided);
            Assert.False(shape.HasVertex0);
            Assert.False(shape.HasVertex3);
        }

        // A circle over the next segment does not also touch the end vertex of the previous one
        // when the segments are connected (Box2D v2.3's region test); queries, which collide
        // without adjacency, keep the vertex contact.
        [Fact]
        public void ConnectedSegmentSkipsTheVertexContactOfTheNextSegmentsRegion()
        {
            var edge = new EdgeShape();
            edge.SetTwoSided(new B2Vector(-3, 0), B2Vector.Zero);
            edge.HasVertex3 = true;
            edge.Vertex3 = new B2Vector(3, 0);
            var circle = new CircleShape { Radius = .5f };
            var xfA = new B2Transform(B2Vector.Zero, new B2Rotation(0));
            var xfB = new B2Transform(new B2Vector(.1f, .3f), new B2Rotation(0));
            var manifold = new Manifold();
            CollisionUtils.CollideEdgeAndCircle(ref manifold, edge, xfA, circle, xfB, useAdjacency: true);
            Assert.Equal(0, manifold.PointCount);
            manifold = new Manifold();
            CollisionUtils.CollideEdgeAndCircle(ref manifold, edge, xfA, circle, xfB);
            Assert.Equal(1, manifold.PointCount);
            Assert.Equal(ManifoldType.Circles, manifold.Type);
        }

        // Zero-length ghost segments. An adjacent point exactly on its chain end point, or an
        // internal neighbour exactly on the segment's own vertex, gives a ghost segment without a
        // direction; collision adjacency treats it as absent and keeps the stored flags and points.
        // Unity's behaviour for such points is UNMEASURED: these tests check that implementation
        // rule, not a measured Unity result.

        [Fact]
        public void ZeroLengthAdjacentPointsKeepTheStoredFlagsAndPointsButAreNotUsed()
        {
            var edge = Edge("chain", new Vector2(0, 0), new Vector2(2, 0), new Vector2(4, 0));
            edge.useAdjacentStartPoint = true;
            edge.adjacentStartPoint = new Vector2(0, 0);
            edge.useAdjacentEndPoint = true;
            edge.adjacentEndPoint = new Vector2(4, 0);
            Assert.True(edge.useAdjacentStartPoint);
            Assert.Equal(new Vector2(0, 0), edge.adjacentStartPoint);
            Assert.True(edge.useAdjacentEndPoint);
            Assert.Equal(new Vector2(4, 0), edge.adjacentEndPoint);

            var shapes = Shapes(edge);
            Assert.Equal(2, shapes.Count);
            Assert.True(shapes[0].HasVertex0);
            Assert.Equal(shapes[0].Vertex1, shapes[0].Vertex0);
            Assert.False(shapes[0].UsesVertex0);
            Assert.True(shapes[0].UsesVertex3);
            Assert.True(shapes[1].UsesVertex0);
            Assert.True(shapes[1].HasVertex3);
            Assert.Equal(shapes[1].Vertex2, shapes[1].Vertex3);
            Assert.False(shapes[1].UsesVertex3);

            // Adjacent points off the end points stay in use.
            edge.adjacentStartPoint = new Vector2(-1, 0);
            edge.adjacentEndPoint = new Vector2(5, 0);
            shapes = Shapes(edge);
            Assert.True(shapes[0].UsesVertex0);
            Assert.True(shapes[1].UsesVertex3);
        }

        // Segments up to half the linear slop long are dropped and their neighbours connect across
        // them, so a short back-and-forth leaves an exactly zero-length internal ghost segment.
        [Fact]
        public void ZeroLengthInternalGhostSegmentAcrossDroppedSegmentsIsNotUsed()
        {
            float h = B2Settings.LinearSlop * .5f;
            var edge = Edge("chain", new Vector2(0, 0), new Vector2(1.5f * h, 0), new Vector2(.75f * h, 0),
                new Vector2(0, 0), new Vector2(4, 0));
            var shapes = Shapes(edge);
            Assert.Equal(2, shapes.Count);
            Assert.True(shapes[1].HasVertex0);
            Assert.Equal(shapes[1].Vertex1, shapes[1].Vertex0);
            Assert.False(shapes[1].UsesVertex0);
            Assert.True(shapes[0].UsesVertex3);
        }

        static void AssertSameManifold(Manifold expected, Manifold actual)
        {
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.PointCount, actual.PointCount);
            Assert.Equal(expected.LocalNormal, actual.LocalNormal);
            Assert.Equal(expected.LocalPoint, actual.LocalPoint);
            for (int i = 0; i < expected.PointCount; i++)
            {
                Assert.Equal(expected.Points[i].LocalPoint, actual.Points[i].LocalPoint);
                Assert.Equal(expected.Points[i].Id.Key, actual.Points[i].Id.Key);
            }
        }

        // The v2.3 edge/polygon collider with one zero-length ghost segment gives the manifold of
        // the same edge without that ghost vertex. "start": a box resting on top of a segment whose
        // start ghost is zero-length (used as given, it would force the lower face side and a
        // downward normal). "end"/"internal": a box below a segment whose following ghost is
        // zero-length, its side face 0.002 past the segment's end (used as given, it would reject
        // that side face).
        [Theory]
        [InlineData("start")]
        [InlineData("end")]
        [InlineData("internal")]
        public void ZeroLengthGhostSegmentGivesThePolygonManifoldWithoutThatGhostVertex(string ghost)
        {
            var edge = new EdgeShape();
            B2Vector center;
            if (ghost == "start")
            {
                edge.SetTwoSided(B2Vector.Zero, new B2Vector(4, 0));
                edge.HasVertex0 = true;
                edge.Vertex0 = edge.Vertex1;
                edge.HasVertex3 = true;
                edge.Vertex3 = new B2Vector(8, 0);
                center = new B2Vector(2, .49f);
            }
            else
            {
                edge.SetTwoSided(new B2Vector(-4, 0), B2Vector.Zero);
                if (ghost == "internal")
                {
                    edge.HasVertex0 = true;
                    edge.Vertex0 = new B2Vector(-8, 0);
                }
                edge.HasVertex3 = true;
                edge.Vertex3 = edge.Vertex2;
                center = new B2Vector(.498f, -.49f);
            }
            var box = new PolygonShape();
            box.SetAsBox(.5f, .5f);
            var xfA = new B2Transform(B2Vector.Zero, new B2Rotation(0));
            var xfB = new B2Transform(center, new B2Rotation(0));

            var zeroGhost = new Manifold();
            CollisionUtils.CollideEdgeAndPolygonAdjacent(ref zeroGhost, edge, xfA, box, xfB);
            if (ghost == "start") edge.HasVertex0 = false;
            else edge.HasVertex3 = false;
            var withoutGhost = new Manifold();
            CollisionUtils.CollideEdgeAndPolygonAdjacent(ref withoutGhost, edge, xfA, box, xfB);

            Assert.True(withoutGhost.PointCount > 0, "the box touches the edge");
            AssertSameManifold(withoutGhost, zeroGhost);
        }

        // The circle region tests skip a zero-length ghost segment as well; a nonzero collinear
        // ghost vertex removes the end-vertex contact. (Here the zero-length case already matched
        // the no-ghost case arithmetically before the rule; this is a guard, not a red control.)
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ZeroLengthGhostSegmentGivesTheCircleManifoldWithoutThatGhostVertex(bool start)
        {
            var edge = new EdgeShape();
            edge.SetTwoSided(B2Vector.Zero, new B2Vector(4, 0));
            var circle = new CircleShape { Radius = .5f };
            var xfA = new B2Transform(B2Vector.Zero, new B2Rotation(0));
            var xfB = new B2Transform(start ? new B2Vector(-.1f, .3f) : new B2Vector(4.1f, .3f), new B2Rotation(0));

            var withoutGhost = new Manifold();
            CollisionUtils.CollideEdgeAndCircle(ref withoutGhost, edge, xfA, circle, xfB, useAdjacency: true);
            Assert.Equal(1, withoutGhost.PointCount);

            if (start)
            {
                edge.HasVertex0 = true;
                edge.Vertex0 = edge.Vertex1;
            }
            else
            {
                edge.HasVertex3 = true;
                edge.Vertex3 = edge.Vertex2;
            }
            var zeroGhost = new Manifold();
            CollisionUtils.CollideEdgeAndCircle(ref zeroGhost, edge, xfA, circle, xfB, useAdjacency: true);
            AssertSameManifold(withoutGhost, zeroGhost);

            if (start) edge.Vertex0 = new B2Vector(-4, 0);
            else edge.Vertex3 = new B2Vector(8, 0);
            var nonzeroGhost = new Manifold();
            CollisionUtils.CollideEdgeAndCircle(ref nonzeroGhost, edge, xfA, circle, xfB, useAdjacency: true);
            Assert.Equal(0, nonzeroGhost.PointCount);
        }

        // A chain from the origin with useAdjacentStartPoint and the default adjacentStartPoint (0,0).
        // A box resting on the first segment keeps an upward contact: that segment collides with
        // only its following neighbour as ghost vertex. Used as given, the zero-length start ghost
        // would select the edge's lower side and a downward normal that pushes the box through.
        [Fact]
        public void AdjacentStartPointOnTheFirstPointKeepsTheUpwardContactOfTheChainStart()
        {
            var edge = Edge("ground", new Vector2(0, 0), new Vector2(4, 0), new Vector2(8, 0));
            edge.useAdjacentStartPoint = true;
            edge.adjacentStartPoint = Vector2.zero;
            var body = Box(new Vector2(2, .49f), Vector2.zero);
            Physics2D.SyncTransforms();
            Physics2D.Simulate(.02f);
            AssertContactNormals(body, 1f);
            Assert.True(body.position.y > .48f, $"position {body.position}");
        }

        // A single segment whose only ghost segment is the zero-length one at its start has no ghost
        // vertex left, so its contacts are those of the same segment with useAdjacentStartPoint off
        // (a copy of the scene 10 units higher). The box is tilted so that the two edge/polygon
        // paths give different contacts. Used as given, the zero-length ghost would give a downward
        // normal.
        [Fact]
        public void AdjacentStartPointOnTheOnlySegmentsFirstPointCollidesAsWithoutIt()
        {
            var zeroGhostEdge = Edge("zero ghost", new Vector2(0, 0), new Vector2(4, 0));
            zeroGhostEdge.useAdjacentStartPoint = true;
            zeroGhostEdge.adjacentStartPoint = Vector2.zero;
            var zeroGhost = Box(new Vector2(2, .53f), Vector2.zero, angle: 5f);
            Edge("no adjacent point", new Vector2(0, 10), new Vector2(4, 10));
            var withoutGhost = Box(new Vector2(2, 10.53f), Vector2.zero, angle: 5f);
            Physics2D.SyncTransforms();
            Physics2D.Simulate(.02f);

            var expected = new ContactPoint2D[16];
            var actual = new ContactPoint2D[16];
            int expectedCount = withoutGhost.GetContacts(expected);
            int actualCount = zeroGhost.GetContacts(actual);
            Assert.True(expectedCount > 0, "the box touches the edge without an adjacent point");
            Assert.Equal(expectedCount, actualCount);
            for (int i = 0; i < actualCount; i++)
            {
                Assert.True(actual[i].normal.y > .9f, $"contact {i} normal {actual[i].normal}");
                Assert.True((actual[i].normal - expected[i].normal).magnitude < 1e-4f,
                    $"contact {i} normal {actual[i].normal}, without the adjacent point {expected[i].normal}");
            }
            Assert.InRange(zeroGhost.position.y - (withoutGhost.position.y - 10f), -1e-4f, 1e-4f);
        }
    }
}
