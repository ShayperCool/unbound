using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ParticleSystem.Clear and the systems a sub-emitters module links. 6000.0 Scripting
    /// API, ParticleSystem.Clear remarks: "This method also removes the particles from any linked
    /// sub-emitters. Use the withChildren parameter to remove particles from child Particle Systems
    /// that are not sub-emitters of the system." Tests marked internal choice or preservation
    /// pin what the docs leave open (nested links, cycles, a disabled module); they are not
    /// measured Unity behaviour.</summary>
    public class ParticleClearLinkedTests
    {
        static ParticleSystem Make(string name, Transform parent = null)
        {
            var go = new GameObject(name); go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.startLifetime = 10; main.startSpeed = 0;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            go.SetActive(true);
            return ps;
        }

        static void Link(ParticleSystem parent, ParticleSystem sub, bool enabled = true)
        {
            var subs = parent.subEmitters;
            subs.enabled = enabled;
            subs.AddSubEmitter(sub, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        }

        static void Done(params ParticleSystem[] roots)
        {
            foreach (var ps in roots)
                if (ps != null) Object.DestroyImmediate(ps.gameObject);
        }

        [Fact]
        public void ClearWithoutChildrenRemovesDetachedLinkedSubEmitterParticles()
        {
            var parent = Make("parent");
            var sub = Make("sub");
            Link(parent, sub);
            try
            {
                parent.Emit(3);
                sub.Emit(5);
                Assert.True(sub.IsAlive());
                parent.Clear(false);
                Assert.Equal(0, parent.particleCount);
                Assert.Equal(0, sub.particleCount);
                Assert.False(sub.IsAlive());
            }
            finally { Done(parent, sub); }
        }

        [Fact]
        public void ClearWithChildrenRemovesDetachedLinkedSubEmitterParticles()
        {
            var parent = Make("parent");
            var sub = Make("sub");
            Link(parent, sub);
            try
            {
                sub.Emit(5);
                parent.Clear();
                Assert.Equal(0, sub.particleCount);
            }
            finally { Done(parent, sub); }
        }

        [Fact]
        public void ClearWithoutChildrenRemovesLinkedChildSubEmitter()
        {
            var parent = Make("parent");
            var sub = Make("sub", parent.transform);
            Link(parent, sub);
            try
            {
                sub.Emit(5);
                parent.Clear(false);
                Assert.Equal(0, sub.particleCount);
            }
            finally { Done(parent); }
        }

        // Control: unlinked children follow withChildren (already the case before linked clearing).
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnlinkedChildrenFollowWithChildren(bool withChildren)
        {
            var parent = Make("parent");
            var child = Make("child", parent.transform);
            var grandchild = Make("grandchild", child.transform);
            try
            {
                parent.Emit(2);
                child.Emit(4);
                grandchild.Emit(6);
                parent.Clear(withChildren);
                Assert.Equal(0, parent.particleCount);
                Assert.Equal(withChildren ? 0 : 4, child.particleCount);
                Assert.Equal(withChildren ? 0 : 6, grandchild.particleCount);
            }
            finally { Done(parent); }
        }

        [Fact]
        public void ClearWithoutChildrenRemovesLinkedChildAndKeepsUnlinkedSibling()
        {
            var parent = Make("parent");
            var linked = Make("linked", parent.transform);
            var unlinked = Make("unlinked", parent.transform);
            Link(parent, linked);
            try
            {
                linked.Emit(3);
                unlinked.Emit(4);
                parent.Clear(false);
                Assert.Equal(0, linked.particleCount);
                Assert.Equal(4, unlinked.particleCount);
                Assert.True(unlinked.IsAlive());
            }
            finally { Done(parent); }
        }

        // Internal choice: nested links are followed (the docs do not address nesting).
        [Fact]
        public void ClearFollowsNestedLinksOfLinkedSystems()
        {
            var parent = Make("parent");
            var sub = Make("sub");
            var nested = Make("nested");
            Link(parent, sub);
            Link(sub, nested);
            try
            {
                nested.Emit(5);
                parent.Clear(false);
                Assert.Equal(0, nested.particleCount);
            }
            finally { Done(parent, sub, nested); }
        }

        // Internal choice: a child Clear covers with children is cleared with its own links.
        [Fact]
        public void ClearWithChildrenFollowsLinksOfChildSystems()
        {
            var parent = Make("parent");
            var child = Make("child", parent.transform);
            var detached = Make("detached");
            Link(child, detached);
            try
            {
                detached.Emit(5);
                parent.Clear(true);
                Assert.Equal(0, detached.particleCount);
            }
            finally { Done(parent, detached); }
        }

        // Internal safety: shared, mutual and self links terminate and clear every system once.
        [Fact]
        public void ClearTerminatesOnSharedCyclicAndSelfLinks()
        {
            var a = Make("a");
            var b = Make("b");
            var shared = Make("shared");
            Link(a, shared);
            Link(a, b);
            Link(a, a);
            Link(b, shared);
            Link(b, a);
            try
            {
                a.Emit(1);
                b.Emit(2);
                shared.Emit(3);
                a.Clear(false);
                Assert.Equal(0, a.particleCount);
                Assert.Equal(0, b.particleCount);
                Assert.Equal(0, shared.particleCount);
            }
            finally { Done(a, b, shared); }
        }

        // Internal safety: a destroyed linked system is skipped.
        [Fact]
        public void ClearSkipsDestroyedLinkedSystems()
        {
            var parent = Make("parent");
            var gone = Make("gone");
            var sub = Make("sub");
            Link(parent, gone);
            Link(parent, sub);
            try
            {
                Object.DestroyImmediate(gone.gameObject);
                sub.Emit(2);
                parent.Clear(false);
                Assert.Equal(0, sub.particleCount);
            }
            finally { Done(parent, sub); }
        }

        // Preservation (unresolved in the docs): a disabled module links nothing, as for emission.
        [Fact]
        public void ClearKeepsSystemsListedByADisabledModule()
        {
            var parent = Make("parent");
            var listed = Make("listed");
            Link(parent, listed, enabled: false);
            try
            {
                listed.Emit(5);
                parent.Clear(false);
                Assert.Equal(5, listed.particleCount);
                Assert.True(listed.IsAlive());
            }
            finally { Done(parent, listed); }
        }

        [Fact]
        public void LinkedSystemGetsTheSameClearAsClearingItDirectly()
        {
            var parent = Make("parent");
            var sub = Make("sub");
            var control = Make("control");
            Link(parent, sub);
            var stops = new List<StopRecorder>();
            try
            {
                foreach (var ps in new[] { sub, control })
                {
                    var main = ps.main; main.stopAction = ParticleSystemStopAction.Callback;
                    var trails = ps.trails; trails.enabled = true;
                    stops.Add(ps.gameObject.AddComponent<StopRecorder>());
                    ps.time = .5f;
                    ps.Emit(3);
                    Assert.Equal(3, ps.m_TrailList.Count);
                }
                parent.Clear(false);
                control.Clear(false);
                foreach (var ps in new[] { sub, control })
                {
                    Assert.Equal(0, ps.particleCount);
                    Assert.Empty(ps.m_TrailList);
                    Assert.False(ps.isPlaying);
                    Assert.True(ps.isStopped);
                    Assert.False(ps.IsAlive());
                    Assert.Equal(.5f, ps.time);
                }
                Assert.Equal(0, stops[0].stops);
                Assert.Equal(0, stops[1].stops);
            }
            finally { Done(parent, sub, control); }
        }

        [Fact]
        public void ClearRemovesParticlesALinkEmittedAndKeepsTheLink()
        {
            var parent = Make("parent");
            var main = parent.main; main.startLifetime = .2f;
            var sub = Make("sub");
            var emission = sub.emission; emission.enabled = true; emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });
            Link(parent, sub);
            try
            {
                parent.Emit(2);
                for (int i = 0; i < 15; i++) parent.Step(.02f, 0f);
                Assert.Equal(0, parent.particleCount);
                Assert.Equal(8, sub.particleCount);
                parent.Clear(false);
                Assert.Equal(0, sub.particleCount);
                Assert.Same(sub, parent.subEmitters.GetSubEmitterSystem(0));
                Assert.Same(parent, sub.m_SubEmitterParent);
                parent.Emit(1);
                for (int i = 0; i < 15; i++) parent.Step(.02f, 0f);
                Assert.Equal(4, sub.particleCount);
            }
            finally { Done(parent, sub); }
        }
    }
}
