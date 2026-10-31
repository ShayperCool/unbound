using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    // Native solverbodyorder controls distinguish enabled state from body lifetime.
    // These invariants already agree; the rejected dense-seed candidate must not
    // turn simulation toggles or nonstatic type changes into body recreation.
    public class Physics2DSolverBodyOrderTests : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();
        public Physics2DSolverBodyOrderTests()
        {
            Physics2D.SyncTransforms();
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = Vector2.zero;
        }
        Rigidbody2D Body(string name)
        {
            var o = new GameObject(name); objects.Add(o);
            o.transform.position = new Vector3(10 + objects.Count * 3, 0, 0);
            o.AddComponent<BoxCollider2D>();
            var b = o.AddComponent<Rigidbody2D>();
            b.sleepMode = RigidbodySleepMode2D.NeverSleep;
            Physics2D.SyncTransforms();
            return b;
        }
        [Fact]
        public void SimulationToggleRetainsBodyThroughAnExcludedStep()
        {
            var a = Body("A"); var b = Body("B");
            Physics2D.Simulate(.02f);
            var stored = a.m_Body;
            var peer = b.m_Body;
            Vector2 position = a.position;
            a.simulated = false;
            Physics2D.Simulate(.02f);
            Assert.Same(stored, a.m_Body);
            Assert.False(stored.IsEnabled);
            Assert.Equal(position, a.position);
            a.simulated = true;
            Physics2D.Simulate(.02f);
            Assert.Same(stored, a.m_Body);
            Assert.Same(peer, b.m_Body);
            Assert.True(stored.IsEnabled);
        }
        [Theory]
        [InlineData(RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic)]
        [InlineData(RigidbodyType2D.Kinematic, RigidbodyType2D.Dynamic)]
        public void NonstaticTypeTransitionRetainsBodyAndPeer(RigidbodyType2D from, RigidbodyType2D to)
        {
            var a = Body("A"); a.bodyType = from;
            var b = Body("B");
            Physics2D.Simulate(.02f);
            var stored = a.m_Body; var peer = b.m_Body;
            a.bodyType = to;
            Physics2D.Simulate(.02f);
            Assert.Same(stored, a.m_Body);
            Assert.Same(peer, b.m_Body);
            Assert.Equal(to, a.bodyType);
            Assert.True(stored.IsEnabled);
        }
        [Fact]
        public void ReactivationStartsANewBodyLifetimeWithoutRecreatingPeer()
        {
            var a = Body("A"); var b = Body("B");
            Physics2D.Simulate(.02f);
            var stored = a.m_Body; var peer = b.m_Body;
            a.gameObject.SetActive(false);
            Physics2D.Simulate(.02f);
            Assert.Null(a.m_Body);
            a.gameObject.SetActive(true);
            Physics2D.Simulate(.02f);
            Assert.NotNull(a.m_Body);
            Assert.NotSame(stored, a.m_Body);
            Assert.Same(peer, b.m_Body);
        }
        public void Dispose()
        {
            foreach (var o in objects) if (o) Object.DestroyImmediate(o);
            Physics2D.gravity = new Vector2(0, -9.81f);
            Physics2D.Simulate(.02f);
        }
    }
}
