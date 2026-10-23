using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class GeometryTests
    {
        [Fact]
        public void RectEdgesAndContains()
        {
            Rect r = new Rect(1f, 2f, 3f, 4f);
            Assert.Equal(4f, r.xMax);
            Assert.Equal(new Vector2(2.5f, 4f), r.center);
            Assert.True(r.Contains(new Vector2(1f, 2f)));
            Assert.False(r.Contains(new Vector2(4f, 2f))); // max edge exclusive
            r.xMin = 0f; // keeps xMax
            Assert.Equal(4f, r.width);
            Assert.Equal(4f, r.xMax);
            Rect inverted = new Rect(4f, 4f, -2f, -2f);
            Assert.False(inverted.Contains(new Vector3(3f, 3f, 0f)));
            Assert.True(inverted.Contains(new Vector3(3f, 3f, 0f), true));
            Assert.False(inverted.Overlaps(new Rect(2.5f, 2.5f, 1f, 1f)));
            Assert.True(inverted.Overlaps(new Rect(2.5f, 2.5f, 1f, 1f), true));
            Assert.Equal(new Rect(1f, 2f, 2f, 2f), Rect.MinMaxRect(1f, 2f, 3f, 4f));
            Assert.Equal(new Vector2(2f, 3f), Rect.NormalizedToPoint(new Rect(0f, 0f, 4f, 6f), new Vector2(0.5f, 0.5f)));
            Assert.Equal(new Vector2(0.25f, 1f), Rect.PointToNormalized(new Rect(0f, 0f, 4f, 6f), new Vector2(1f, 7f)));
            Assert.Equal("(x:1.00, y:2.00, width:3.00, height:4.00)", new Rect(1f, 2f, 3f, 4f).ToString());
        }

        [Fact]
        public void RectIntPositionsAndClamp()
        {
            RectInt r = new RectInt(1, 2, 2, 3);
            var positions = new List<Vector2Int>();
            foreach (Vector2Int p in r.allPositionsWithin)
                positions.Add(p);
            Assert.Equal(6, positions.Count);
            Assert.Equal(new Vector2Int(1, 2), positions[0]);
            Assert.Equal(new Vector2Int(2, 2), positions[1]);
            Assert.Equal(new Vector2Int(1, 3), positions[2]);
            Assert.Equal(new Vector2Int(2, 4), positions[5]);

            var empty = new RectInt(0, 0, 0, 3);
            foreach (Vector2Int p in empty.allPositionsWithin)
                Assert.Fail("empty rect yielded " + p);

            RectInt c = new RectInt(-2, -2, 10, 10);
            c.ClampToBounds(new RectInt(0, 0, 5, 5));
            Assert.Equal(new RectInt(0, 0, 5, 5), c);
            Assert.True(r.Contains(new Vector2Int(2, 4)));
            Assert.False(r.Contains(new Vector2Int(3, 4)));
            Assert.Equal("(x:1, y:2, width:2, height:3)", r.ToString());
            RectInt neg = new RectInt(5, 5, -2, -2);
            Assert.Equal(3, neg.xMin);
            Assert.Equal(5, neg.xMax);
        }

        [Fact]
        public void BoundsIntPositions()
        {
            var b = new BoundsInt(0, 0, 0, 2, 2, 2);
            var positions = new List<Vector3Int>();
            foreach (Vector3Int p in b.allPositionsWithin)
                positions.Add(p);
            Assert.Equal(8, positions.Count);
            Assert.Equal(new Vector3Int(0, 0, 0), positions[0]);
            Assert.Equal(new Vector3Int(1, 0, 0), positions[1]);
            Assert.Equal(new Vector3Int(0, 1, 0), positions[2]);
            Assert.Equal(new Vector3Int(0, 0, 1), positions[4]);
            Assert.Equal(new Vector3Int(1, 1, 1), positions[7]);
            Assert.Equal("Position: (0, 0, 0), Size: (2, 2, 2)", b.ToString());
            Assert.True(b.Contains(new Vector3Int(1, 1, 1)));
            Assert.False(b.Contains(new Vector3Int(2, 1, 1)));
            Assert.Equal(new Vector3(1f, 1f, 1f), b.center);
        }

        [Fact]
        public void BoundsOperations()
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.one);
            Assert.Equal(new Vector3(0.5f, 0.5f, 0.5f), b.extents);
            Assert.True(b.Contains(new Vector3(0.5f, 0.5f, 0.5f))); // inclusive
            Assert.False(b.Contains(new Vector3(0.6f, 0f, 0f)));
            b.Encapsulate(new Vector3(2f, 0f, 0f));
            Assert.Equal(new Vector3(-0.5f, -0.5f, -0.5f), b.min);
            Assert.Equal(new Vector3(2f, 0.5f, 0.5f), b.max);
            b.Expand(1f);
            Assert.Equal(new Vector3(3.5f, 2f, 2f), b.size);
            Assert.Equal("Center: (0.75, 0.00, 0.00), Extents: (1.75, 1.00, 1.00)", b.ToString());

            Bounds unit = new Bounds(Vector3.zero, Vector3.one * 2f);
            Assert.Equal(9f, unit.SqrDistance(new Vector3(4f, 0f, 0f)));
            Assert.Equal(0f, unit.SqrDistance(new Vector3(0.5f, 0f, 0f)));
            Assert.Equal(new Vector3(1f, 0.5f, -1f), unit.ClosestPoint(new Vector3(3f, 0.5f, -7f)));
            Assert.True(unit.Intersects(new Bounds(new Vector3(2f, 0f, 0f), Vector3.one * 2f))); // touching
            Assert.False(unit.Intersects(new Bounds(new Vector3(2.1f, 0f, 0f), Vector3.one * 2f)));

            Assert.True(unit.IntersectRay(new Ray(new Vector3(-5f, 0f, 0f), Vector3.right), out float d));
            Assert.Equal(4f, d);
            Assert.False(unit.IntersectRay(new Ray(new Vector3(-5f, 0f, 0f), Vector3.left)));
            Assert.False(unit.IntersectRay(new Ray(new Vector3(-5f, 3f, 0f), Vector3.right)));
            Assert.True(unit.IntersectRay(new Ray(Vector3.zero, Vector3.up)));

            Bounds mm = default;
            mm.SetMinMax(new Vector3(1f, 2f, 3f), new Vector3(3f, 4f, 5f));
            Assert.Equal(new Vector3(2f, 3f, 4f), mm.center);
        }

        [Fact]
        public void PlaneOperations()
        {
            Plane ground = new Plane(Vector3.up * 5f, new Vector3(0f, 1f, 0f));
            Assert.Equal(Vector3.up, ground.normal);
            Assert.Equal(-1f, ground.distance);
            Assert.True(ground.GetSide(new Vector3(0f, 2f, 0f)));
            Assert.False(ground.GetSide(new Vector3(0f, 1f, 0f)));
            Assert.Equal(3f, ground.GetDistanceToPoint(new Vector3(7f, 4f, 0f)));
            Assert.Equal(new Vector3(7f, 1f, 2f), ground.ClosestPointOnPlane(new Vector3(7f, 4f, 2f)));
            Assert.True(ground.SameSide(new Vector3(0f, 0f, 0f), new Vector3(0f, -3f, 0f)));

            Assert.True(ground.Raycast(new Ray(new Vector3(0f, 5f, 0f), Vector3.down), out float enter));
            Assert.Equal(4f, enter);
            Assert.False(ground.Raycast(new Ray(new Vector3(0f, 5f, 0f), Vector3.up), out enter));
            Assert.Equal(-4f, enter);
            Assert.False(ground.Raycast(new Ray(new Vector3(0f, 5f, 0f), Vector3.right), out enter));
            Assert.Equal(0f, enter);

            Plane three = new Plane(Vector3.zero, Vector3.forward, Vector3.right);
            Assert.Equal(Vector3.up, three.normal);
            Plane flipped = ground.flipped;
            Assert.Equal(Vector3.down, flipped.normal);
            Assert.Equal(1f, flipped.distance);
            Plane moved = Plane.Translate(ground, new Vector3(0f, 2f, 0f));
            Assert.Equal(1f, moved.distance);
            Assert.Equal("(normal:(0.00, 1.00, 0.00), distance:-1.00)", ground.ToString());
        }

        [Fact]
        public void RaysNormalizeDirection()
        {
            Ray ray = new Ray(Vector3.one, new Vector3(0f, 0f, 5f));
            Assert.Equal(Vector3.forward, ray.direction);
            Assert.Equal(new Vector3(1f, 1f, 3f), ray.GetPoint(2f));
            ray.direction = new Vector3(3f, 0f, 0f);
            Assert.Equal(Vector3.right, ray.direction);
            Assert.Equal("Origin: (1.00, 1.00, 1.00), Dir: (1.00, 0.00, 0.00)", ray.ToString());
            Ray2D r2 = new Ray2D(Vector2.zero, new Vector2(0f, 4f));
            Assert.Equal(Vector2.up, r2.direction);
            Assert.Equal(new Vector2(0f, 3f), r2.GetPoint(3f));
        }

        [Fact]
        public void RectOffsetAddRemove()
        {
            var o = new RectOffset(1, 2, 3, 4);
            Assert.Equal(3, o.horizontal);
            Assert.Equal(7, o.vertical);
            Assert.Equal(new Rect(9f, 17f, 13f, 27f), o.Add(new Rect(10f, 20f, 10f, 20f)));
            Assert.Equal(new Rect(11f, 23f, 7f, 13f), o.Remove(new Rect(10f, 20f, 10f, 20f)));
            Assert.Equal("RectOffset (l:1 r:2 t:3 b:4)", o.ToString());
        }
    }
}
