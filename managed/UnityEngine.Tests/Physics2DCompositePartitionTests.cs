using System;
using System.Collections.Generic;
using Unbound.Runtime;
using V = System.Numerics.Vector2;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class Physics2DCompositePartitionTests : IDisposable
    {
        readonly Physics2DMeasuredScope release = new Physics2DMeasuredScope();
        readonly List<GameObject> roots = new List<GameObject>();

        CompositeCollider2D Composite()
        {
            var root = new GameObject("composite"); roots.Add(root);
            root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            var composite = root.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Manual;
            return composite;
        }
        static BoxCollider2D Box(CompositeCollider2D composite, Vector2 size, Vector2 offset,
            Collider2D.CompositeOperation operation = Collider2D.CompositeOperation.Merge, int order = 0)
        {
            var box = composite.gameObject.AddComponent<BoxCollider2D>();
            box.size = size; box.offset = offset; box.compositeOperation = operation; box.compositeOrder = order;
            return box;
        }
        static PhysicsShapeGroup2D Shapes(CompositeCollider2D composite)
        {
            Physics2D.SyncTransforms(); composite.GenerateGeometry(); Physics2D.SyncTransforms();
            var group = new PhysicsShapeGroup2D(); composite.GetShapes(group); return group;
        }
        [Theory]
        [InlineData("6000.0.71f1", false, 4, 0f)]
        [InlineData("6000.0.71f1", true, 2, 0f)]
        [InlineData("6000.0.72f1", false, 2, .125f)]
        public void PublicGeometrySelectsTheMeasuredPartitionAndRefinement(string version, bool delaunay,
            int expectedShapes, float expectedRadius)
        {
            Physics2DWorld.Reset(); ProjectInfo.UnityVersion = version;
            var composite = Composite(); composite.useDelaunayMesh = delaunay; composite.edgeRadius = .125f;
            Box(composite, new Vector2(2, .8f), Vector2.zero);
            Box(composite, Vector2.one, new Vector2(.375f, .125f));
            var group = Shapes(composite);
            Assert.Equal(expectedShapes, group.shapeCount);
            Assert.Equal(1, composite.pathCount);
            for (int i = 0; i < group.shapeCount; i++)
            {
                var shape = group.GetShape(i);
                Assert.InRange(shape.vertexCount, 3, 8);
                Assert.Equal(expectedRadius, shape.radius);
            }
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HoleAndIslandSurviveDegenerateInputAndRequireManualRegeneration(bool delaunay)
        {
            var composite = Composite(); composite.useDelaunayMesh = delaunay;
            Box(composite, new Vector2(4, 4), Vector2.zero);
            var hole = Box(composite, new Vector2(2, 2), Vector2.zero, Collider2D.CompositeOperation.Difference, 1);
            Box(composite, Vector2.one, new Vector2(4, 0), order: 2);
            var flat = composite.gameObject.AddComponent<PolygonCollider2D>();
            flat.points = new[] { new Vector2(10, 0), new Vector2(10, 0), new Vector2(11, 0), new Vector2(12, 0) };
            flat.compositeOperation = Collider2D.CompositeOperation.Merge; flat.compositeOrder = 3;
            var group = Shapes(composite);
            Assert.Equal(5, group.shapeCount); Assert.Equal(3, composite.pathCount);
            Assert.False(composite.OverlapPoint(Vector2.zero));
            Assert.True(composite.OverlapPoint(new Vector2(1.5f, 0)));
            Assert.True(composite.OverlapPoint(new Vector2(4, 0)));
            Assert.False(composite.OverlapPoint(new Vector2(11, 0)));
            var sample = new Vector2(1.2f, 0);
            Assert.True(composite.OverlapPoint(sample));
            hole.offset = new Vector2(.5f, 0); Physics2D.SyncTransforms();
            Assert.True(composite.OverlapPoint(sample));
            composite.GenerateGeometry(); Physics2D.SyncTransforms();
            Assert.False(composite.OverlapPoint(sample));
        }
        [Fact]
        public void TiltedOutlinePathsShareTheBodyFrameAcrossGeometryTypeChanges()
        {
            var anchor = new GameObject("anchor"); roots.Add(anchor);
            anchor.transform.rotation = Quaternion.Euler(17, 29, 11);
            anchor.transform.localScale = new Vector3(1.4f, .7f, 1.2f);
            var root = new GameObject("outline"); root.transform.SetParent(anchor.transform, false);
            root.transform.localRotation = Quaternion.Euler(0, 0, 23);
            var body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Static;
            var composite = root.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
            composite.generationType = CompositeCollider2D.GenerationType.Manual; composite.edgeRadius = .125f;
            Box(composite, new Vector2(2, 2), Vector2.zero);
            Physics2D.SyncTransforms(); composite.GenerateGeometry(); Physics2D.SyncTransforms();
            var path = new Vector2[composite.GetPathPointCount(0)]; composite.GetPath(0, path);
            var point = body.position + (Vector2)(Quaternion.Euler(0, 0, body.rotation) * (Vector3)path[0]);
            Assert.True(composite.OverlapPoint(point)); Assert.False(composite.OverlapPoint(body.position));
            var near = point + Vector2.right * .0625f;
            Assert.True(composite.OverlapPoint(near)); Assert.False(composite.OverlapPoint(point + Vector2.right));
            composite.edgeRadius = 0; Physics2D.SyncTransforms(); Assert.True(composite.OverlapPoint(near));
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons; Physics2D.SyncTransforms();
            Assert.False(composite.OverlapPoint(body.position));
            var clone = Object.Instantiate(root); roots.Add(clone);
            clone.transform.position += Vector3.right * 10; Physics2D.SyncTransforms();
            Assert.True(clone.GetComponent<CompositeCollider2D>().OverlapPoint(clone.GetComponent<Rigidbody2D>().position));
            Object.DestroyImmediate(clone);
            composite.GenerateGeometry(); Physics2D.SyncTransforms();
            Assert.True(composite.OverlapPoint(body.position));
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines; Physics2D.SyncTransforms();
            Assert.True(composite.OverlapPoint(body.position));
            composite.GenerateGeometry(); Physics2D.SyncTransforms();
            Assert.False(composite.OverlapPoint(body.position)); Assert.False(composite.OverlapPoint(near));
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous; Physics2D.SyncTransforms();
            Assert.False(composite.OverlapPoint(body.position));
            composite.GetComponent<BoxCollider2D>().offset = new Vector2(.25f, 0); Physics2D.SyncTransforms();
            Assert.True(composite.OverlapPoint(body.position));
        }
        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.72f1", false)]
        public void OutlinePointOverlapSelectsTheMeasuredRelease(string version, bool overlaps)
        {
            Physics2DWorld.Reset(); ProjectInfo.UnityVersion = version;
            var composite = Composite(); composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
            composite.edgeRadius = .125f;
            Box(composite, new Vector2(2, 2), Vector2.zero); Shapes(composite);
            Assert.Equal(overlaps, composite.OverlapPoint(new Vector2(1.0625f, 0)));
            Assert.False(composite.OverlapPoint(Vector2.zero));
            Assert.False(composite.OverlapPoint(new Vector2(1.25f, 0)));
        }
        [Fact]
        public void PartitionsTolerateRepeatedVerticesAndBoundEachConvexPiece()
        {
            var points = new List<V>();
            for (int i = 0; i < 256; i++)
            {
                float angle = i * MathF.Tau / 256;
                var point = new V(MathF.Cos(angle) * 10, MathF.Sin(angle) * 10);
                points.Add(point); points.Add(point);
            }
            Assert.True(PolygonPartition.TryGenerate(new IReadOnlyList<V>[] { points }, false, out var pieces));
            Assert.NotEmpty(pieces);
            foreach (var piece in pieces)
            {
                Assert.InRange(piece.Count, 3, 8);
                foreach (var point in piece) Assert.True(float.IsFinite(point.X) && float.IsFinite(point.Y));
            }
            Assert.False(PolygonPartition.TryGenerate(new IReadOnlyList<V>[]
                { new[] { V.Zero, V.One, new V(float.NaN, 0) } }, false, out var invalid));
            Assert.Empty(invalid);
        }
        public void Dispose()
        {
            foreach (var root in roots) if (root) Object.DestroyImmediate(root);
            release.Dispose();
        }
    }
}
