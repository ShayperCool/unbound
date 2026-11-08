using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Particle script API details measured in the 6000.0.71f1 Editor
    /// (tests/projects/particlemeshshape component/*, tests/projects/particletriggerapi
    /// carrier/* and */gone).</summary>
    public class ParticleScriptApiTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AddComponentAddsTheRendererAtOnce(bool active)
        {
            var go = new GameObject("particles");
            go.SetActive(active);
            try
            {
                go.AddComponent<ParticleSystem>();
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                Assert.NotNull(renderer);
                Assert.Equal(3, go.GetComponents<Component>().Length);
                Assert.Equal(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode);
                Assert.False(renderer.receiveShadows);
                Assert.Single(renderer.sharedMaterials);
                Assert.Null(renderer.sharedMaterial);
                go.SetActive(true);
                Assert.Single(go.GetComponents<ParticleSystemRenderer>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>A renderer the object already has is kept, not doubled.</summary>
        [Fact]
        public void AddComponentKeepsAnExistingRenderer()
        {
            var go = new GameObject("particles");
            try
            {
                var renderer = go.AddComponent<ParticleSystemRenderer>();
                go.AddComponent<ParticleSystem>();
                Assert.Same(renderer, go.GetComponent<ParticleSystemRenderer>());
                Assert.Single(go.GetComponents<ParticleSystemRenderer>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>TriggerSubEmitter from script on dead particles that carry fading trails emits
        /// as on living ones; with InheritLifetime the children start at zero life and leave at the
        /// child's next update.</summary>
        [Fact]
        public void TriggerSubEmitterOnTrailCarriersKeepsSpentChildrenUntilTheNextUpdate()
        {
            var parentGo = new GameObject("parent");
            parentGo.SetActive(false);
            var parent = parentGo.AddComponent<ParticleSystem>();
            var main = parent.main; main.playOnAwake = false; main.startLifetime = 0.1f; main.startSpeed = 1f;
            var emission = parent.emission; emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)3) });
            var shape = parent.shape; shape.enabled = false;
            var trails = parent.trails; trails.enabled = true; trails.dieWithParticles = false; trails.lifetime = 1f; trails.minVertexDistance = 0.01f;
            var childGo = new GameObject("child");
            childGo.SetActive(false);
            var child = childGo.AddComponent<ParticleSystem>();
            var childMain = child.main; childMain.playOnAwake = false; childMain.startLifetime = 5f; childMain.startSpeed = 0f;
            var childEmission = child.emission; childEmission.rateOverTime = 0f; childEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)2) });
            var childShape = child.shape; childShape.enabled = false;
            childGo.SetActive(true);
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritLifetime);
            parentGo.SetActive(true);
            try
            {
                // The particles die at .1 s; their trails keep them as carriers for about .1 s more.
                parent.Simulate(0.13f, false, true, false);
                Assert.Equal(3, parent.particleCount);
                var buffer = new ParticleSystem.Particle[3];
                parent.GetParticles(buffer);
                Assert.All(buffer, p => Assert.Equal(0f, p.remainingLifetime));

                parent.TriggerSubEmitter(0);
                Assert.Equal(6, child.particleCount);
                parent.TriggerSubEmitter(1);
                Assert.Equal(12, child.particleCount);
                var children = new ParticleSystem.Particle[12];
                child.GetParticles(children);
                Assert.Equal(6, Array.FindAll(children, p => p.remainingLifetime == 0f).Length);

                child.Simulate(0.02f, false, false, false);
                Assert.Equal(6, child.particleCount);
            }
            finally
            {
                Object.DestroyImmediate(parentGo);
                Object.DestroyImmediate(childGo);
            }
        }
    }

    /// <summary>The frame's particle update and a pending Destroy (sets the global clock, so it
    /// runs with the other frame tests).</summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleDestroyFrameTests : IDisposable
    {
        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly bool m_Playing = EditorSession.IsPlaying;
        readonly string m_Version = ProjectInfo.UnityVersion;

        public ParticleDestroyFrameTests()
        {
            Time.s_DeltaTime = 0.02f;
            Time.s_UnscaledDeltaTime = 0.02f;
            EditorSession.IsPlaying = true;
            ProjectInfo.UnityVersion = "6000.0.71f1";
        }

        public void Dispose()
        {
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_UnscaledDeltaTime = m_UnscaledDeltaTime;
            EditorSession.IsPlaying = m_Playing;
            ProjectInfo.UnityVersion = m_Version;
        }

        /// <summary>A system whose Destroy (or its parent's) is pending does not update any more:
        /// Unity destroys it after the Update loop, before the particle update. A control system
        /// beside it updates.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ASystemDestroyedDuringTheFrameDoesNotUpdate(bool destroyParent)
        {
            var root = new GameObject("root");
            var go = new GameObject("particles");
            go.transform.SetParent(root.transform, false);
            var control = new GameObject("control");
            try
            {
                ParticleSystem Playing(GameObject owner)
                {
                    var ps = owner.AddComponent<ParticleSystem>();
                    var main = ps.main; main.startLifetime = 5f; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    var emission = ps.emission; emission.rateOverTime = 100f;
                    ps.Play();
                    return ps;
                }
                var doomed = Playing(go);
                var kept = Playing(control);
                // Last frame's collision events must not go out again.
                doomed.m_CollisionEvents.Add((control, default));
                Object.Destroy(destroyParent ? root : go);
                // Run the scripting boundary between Update and particle simulation.
                Destruction.BeginDelivery();
                Destruction.DeliverRequested();
                ParticleSystemManager.UpdateAll();
                Assert.Equal(0f, doomed.time);
                Assert.Equal(0, doomed.particleCount);
                Assert.Equal(0, doomed.GetSafeCollisionEventSize());
                Assert.True(kept.time > 0f);
                Assert.True(kept.particleCount > 0);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(control);
            }
        }

        sealed class CollisionCounter : MonoBehaviour
        {
            public int Count;
            void OnParticleCollision(GameObject other) => Count++;
        }

        /// <summary>The frame's particle messages go out when its update completes, at
        /// PostLateUpdate.ParticleSystemEndUpdateAll, or earlier when a script uses a particle
        /// system first; a system whose Destroy is delivered before that sends none
        /// (6000.0.71f1, tests/projects/destroyphases trigger controls and particletriggerapi).</summary>
        [Theory]
        [InlineData("complete")]
        [InlineData("read")]
        [InlineData("destroyed")]
        public void ParticleMessagesGoOutWhenTheUpdateCompletes(string mode)
        {
            var go = new GameObject("messages");
            var other = new GameObject("other");
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main; main.startLifetime = 5f; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var counter = go.AddComponent<CollisionCounter>();
                ps.Play();
                ParticleSystemManager.UpdateAll();
                // This frame's collision, as the update would have found it.
                ps.m_CollisionEvents.Add((other, default));
                Assert.Equal(0, counter.Count);
                if (mode == "read")
                {
                    _ = ps.particleCount;
                    Assert.Equal(1, counter.Count);
                }
                if (mode == "destroyed")
                {
                    Object.Destroy(go);
                    Destruction.BeginDelivery();
                    Destruction.DeliverRequested();
                }
                ParticleSystemManager.CompleteUpdate();
                Assert.Equal(mode == "destroyed" ? 0 : 1, counter.Count);
                // Completing again sends nothing more.
                ParticleSystemManager.CompleteUpdate();
                Assert.Equal(mode == "destroyed" ? 0 : 1, counter.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(other);
            }
        }

        /// <summary>A Destroy requested after the Update delivery point (from a coroutine or
        /// LateUpdate) is delivered at the next point, after the particle update: until then the
        /// system simulates and sends its trigger messages like any other (6000.0.71f1,
        /// tests/projects/destroyphases NullCoroutine and particle controls).</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ASystemWhoseDestroyIsPendingUpdatesUntilDelivery(bool destroyParent)
        {
            var root = new GameObject("root");
            var go = new GameObject("particles");
            go.transform.SetParent(root.transform, false);
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main; main.startLifetime = 5f; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var emission = ps.emission; emission.rateOverTime = 100f;
                ps.Play();
                ParticleSystemManager.UpdateAll();
                float before = ps.time;
                Object.Destroy(destroyParent ? root : go);
                ParticleSystemManager.UpdateAll();
                Assert.True(ps.time > before);
                Assert.True(ps.particleCount > 0);
                Destruction.BeginDelivery();
                Destruction.DeliverRequested();
                Assert.True(ps == null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
