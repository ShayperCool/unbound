using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultPhysics2DStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultPhysics2DStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [Fact]
        public void UninitializedCollisionCachesItsEmptyLegacyArrayBeforeTheFirstCopyThrows()
        {
            var collision = new Collision2D();
            Assert.False(collision.enabled);
            Assert.Equal(0, collision.contactCount);
            Assert.Throws<ArgumentNullException>(() => collision.contacts);
            var contacts = collision.contacts;
            Assert.Empty(contacts);
            Assert.Same(contacts, collision.contacts);
            Assert.Equal(0, collision.GetContacts(Array.Empty<ContactPoint2D>()));
        }

        [Fact]
        public void ContactQueriesObserveMutationsToTheLegacyArray()
        {
            var point = new ContactPoint2D(Vector2.zero, Vector2.up, Vector2.zero, 0, 0, 0, 0, 0, null, null, true);
            var collision = new Collision2D { m_CollisionContacts = new[] { point }, m_CollisionContactCount = 1, m_CollisionEnabled = true };
            var contacts = collision.contacts;
            Assert.Same(contacts, collision.contacts);
            Assert.Equal(Vector2.up, collision.GetContact(0).normal);
            contacts[0] = default;
            Assert.Equal(Vector2.zero, collision.GetContact(0).normal);
            var copy = new ContactPoint2D[1];
            Assert.Equal(1, collision.GetContacts(copy));
            Assert.Equal(Vector2.zero, copy[0].normal);
            var list = new List<ContactPoint2D> { point, point };
            Assert.Equal(1, collision.GetContacts(list));
            Assert.Single(list);
            Assert.Equal(Vector2.zero, list[0].normal);
            Assert.Equal(Vector2.up, collision.m_CollisionContacts[0].normal);
        }

        [Fact]
        public void EdgeChainsCannotBecomeCompositeSourcesEvenWithClosedEndpoints()
        {
            var owner = new GameObject("composite capability");
            try
            {
                owner.SetActive(false);
                owner.AddComponent<Rigidbody2D>();
                owner.AddComponent<CompositeCollider2D>();
                var edge = owner.AddComponent<EdgeCollider2D>();
                edge.points = new[] { new Vector2(-1,-1), new Vector2(-1,1), new Vector2(1,1), new Vector2(1,-1), new Vector2(-1,-1) };
                edge.compositeOperation = Collider2D.CompositeOperation.Merge;
                Assert.False(edge.compositeCapable);
                Assert.Equal(Collider2D.CompositeOperation.None, edge.compositeOperation);
                var box = owner.AddComponent<BoxCollider2D>();
                box.compositeOperation = Collider2D.CompositeOperation.Merge;
                Assert.True(box.compositeCapable);
                Assert.Equal(Collider2D.CompositeOperation.Merge, box.compositeOperation);
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
