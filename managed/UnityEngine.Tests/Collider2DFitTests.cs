using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The shapes 2D colliders take when added to an object with a renderer (Unity
    /// 6000.0.71f1 Editor rules, tests/projects/spriteoutlines).</summary>
    public sealed class Collider2DFitTests : IDisposable
    {
        readonly Physics2DMeasuredScope release = new Physics2DMeasuredScope();
        readonly List<Object> objects = new List<Object>();

        /// <summary>A 40x20 texel sprite at 20 pixels per unit, pivot (0.25, 0.75): bounds 2 x 1
        /// units centred (0.5, -0.25) from the pivot.</summary>
        Sprite OffCentre()
        {
            var texture = new Texture2D(40, 20); objects.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 40, 20), new Vector2(.25f, .75f), 20); objects.Add(sprite);
            return sprite;
        }

        GameObject Sprited(Sprite sprite, bool flipX, bool flipY)
        {
            var go = new GameObject("sprited"); objects.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.flipX = flipX; renderer.flipY = flipY;
            return go;
        }

        static void Near(Vector2 expected, Vector2 actual)
        {
            Assert.Equal(expected.x, actual.x, 5);
            Assert.Equal(expected.y, actual.y, 5);
        }

        [Fact]
        public void BoundsColliderShapesFollowTheFlippedSprite()
        {
            var sprite = OffCentre();
            var plain = Sprited(sprite, false, false);
            var box = plain.AddComponent<BoxCollider2D>();
            Near(new Vector2(2, 1), box.size); Near(new Vector2(.5f, -.25f), box.offset);
            var flipped = Sprited(sprite, true, true);
            flipped.transform.localScale = new Vector3(3, .5f, 1);
            flipped.transform.rotation = Quaternion.Euler(0, 0, 40);
            Near(new Vector3(-.5f, .25f, 0), flipped.GetComponent<SpriteRenderer>().localBounds.center);
            var circle = flipped.AddComponent<CircleCollider2D>();
            Assert.Equal(1f, circle.radius, 5); Near(new Vector2(-.5f, .25f), circle.offset);
            var capsule = flipped.AddComponent<CapsuleCollider2D>();
            Near(new Vector2(2, 1), capsule.size); Near(new Vector2(-.5f, .25f), capsule.offset);
            var edge = flipped.AddComponent<EdgeCollider2D>();
            Near(new Vector2(-1.5f, .25f), edge.points[0]); Near(new Vector2(.5f, .25f), edge.points[1]);
            var flippedBox = flipped.AddComponent<BoxCollider2D>();
            Near(new Vector2(-.5f, .25f), flippedBox.offset);
        }

        [Fact]
        public void ABoxFitsTheSimpleSpriteWhileTheOthersFitTheDrawnSize()
        {
            var go = Sprited(OffCentre(), true, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = new Vector2(4, 2);
            var box = go.AddComponent<BoxCollider2D>();
            Near(new Vector2(2, 1), box.size); Near(new Vector2(-.5f, -.25f), box.offset);
            var circle = go.AddComponent<CircleCollider2D>();
            Assert.Equal(2f, circle.radius, 5); Near(new Vector2(-1f, -.5f), circle.offset);
        }

        [Fact]
        public void WithoutBoundsTheDefaultsStay()
        {
            var empty = new GameObject("empty"); objects.Add(empty);
            Near(Vector2.one, empty.AddComponent<BoxCollider2D>().size);
            Assert.Equal(.5f, empty.AddComponent<CircleCollider2D>().radius);
            var polygon = empty.AddComponent<PolygonCollider2D>();
            Near(new Vector2(0, 1), polygon.GetPath(0)[0]);
            // A sprite renderer without a sprite: the box takes the empty sprite bounds, kept at the
            // smallest size; the others keep their defaults.
            var noSprite = new GameObject("noSprite"); objects.Add(noSprite);
            noSprite.AddComponent<SpriteRenderer>();
            Near(new Vector2(.0001f, .0001f), noSprite.AddComponent<BoxCollider2D>().size);
            Near(new Vector2(.5f, 1), noSprite.AddComponent<CapsuleCollider2D>().size);
            // A renderer added after the collider changes nothing.
            var later = new GameObject("later"); objects.Add(later);
            var box = later.AddComponent<BoxCollider2D>();
            later.AddComponent<SpriteRenderer>().sprite = OffCentre();
            Near(Vector2.one, box.size);
        }

        [Fact]
        public void APolygonWithoutASpriteOutlineCirclesTheBounds()
        {
            // Without the engine host the sprite has no traced outline: a pentagon around the
            // flipped bounds, radius the larger half extent.
            var go = Sprited(OffCentre(), false, true);
            var points = go.AddComponent<PolygonCollider2D>().GetPath(0);
            Assert.Equal(5, points.Length);
            Near(new Vector2(.5f, 1.25f), points[0]);
            Near(new Vector2(.5f - Mathf.Sin(2f * Mathf.PI / 5), .25f + Mathf.Cos(2f * Mathf.PI / 5)), points[1]);
        }

        [Fact]
        public void PrimitivesAndSizesAreThoseUnityKeeps()
        {
            var go = new GameObject("shaped"); objects.Add(go);
            var polygon = go.AddComponent<PolygonCollider2D>();
            polygon.CreatePrimitive(3, new Vector2(2, 1), new Vector2(1, -1));
            var points = polygon.GetPath(0);
            Near(new Vector2(1, 0), points[0]);
            Near(new Vector2(1 - 2 * Mathf.Sin(2f * Mathf.PI / 3), -1 + Mathf.Cos(2f * Mathf.PI / 3)), points[1]);
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(-3, 0);
            Near(new Vector2(.0001f, .0001f), box.size);
            var capsule = go.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(2, -1);
            Near(new Vector2(2, .0001f), capsule.size);
        }

        public void Dispose()
        {
            foreach (var value in objects) if (value) Object.DestroyImmediate(value);
            release.Dispose();
        }
    }
}
