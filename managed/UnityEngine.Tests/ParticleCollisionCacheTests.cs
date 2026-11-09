using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>World collisions at Medium and Low quality and planes in a scaled simulation space.
    /// The 6000.0 Manual: Medium and Low "use a grid of voxels to cache previous collisions",
    /// Medium "makes more queries per frame than Low", both are "only suitable for static
    /// colliders". The rules asserted (which frames query, a paused system never querying, the
    /// plane carried through its point nearest the world origin) are those Unity 6000.0.71f1
    /// shows in tests/projects/particlerender collide/world/slope_*, collide/cache/*,
    /// collide/planes/scaled_y.</summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleCollisionCacheTests : IDisposable
    {
        const float Dt = 0.02f;
        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly int m_FrameCount = Time.s_FrameCount;
        readonly int m_Budget = QualitySettings.particleRaycastBudget;
        readonly string m_Version = ProjectInfo.UnityVersion;
        readonly List<Object> m_Objects = new List<Object>();

        public ParticleCollisionCacheTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Time.s_DeltaTime = Dt;
            Time.s_UnscaledDeltaTime = Dt;
            QualitySettings.particleRaycastBudget = 4096;
        }

        public void Dispose()
        {
            foreach (var o in m_Objects) if (o != null) Object.DestroyImmediate(o);
            ParticleSystemManager.EndUpdateAll();
            ProjectInfo.UnityVersion = m_Version;
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_UnscaledDeltaTime = m_UnscaledDeltaTime;
            Time.s_FrameCount = m_FrameCount;
            QualitySettings.particleRaycastBudget = m_Budget;
        }

        static void Frame()
        {
            ParticleSystemManager.UpdateAll();
            ParticleSystemManager.CompleteUpdate();
            ParticleSystemManager.EndUpdateAll();
            Time.s_FrameCount++;
        }

        /// <summary>A box whose top is at y 0 and a system 0.5 above it dropping one particle
        /// (radius 0.1) at 5 units/s, which reaches the box in its fourth frame.</summary>
        ParticleSystem Dropping(ParticleSystemCollisionQuality quality)
        {
            var box = new GameObject("box").AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 2f, 4f);
            box.transform.position = new Vector3(0f, -1f, 0f);
            m_Objects.Add(box.gameObject);
            Physics.SyncTransforms();
            var go = new GameObject("particles");
            m_Objects.Add(go);
            go.SetActive(false);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.5f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.startLifetime = 5f; main.startSpeed = 5f; main.startSize = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = quality; c.bounce = 1f; c.dampen = 0f; c.lifetimeLoss = 0f;
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Particle Only(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[2];
            Assert.Equal(1, ps.GetParticles(particles));
            return particles[0];
        }

        [Theory]
        [InlineData(ParticleSystemCollisionQuality.High)]
        [InlineData(ParticleSystemCollisionQuality.Medium)]
        public void HighAndMediumWithTheDefaultBudgetBounceInTheSameFrame(ParticleSystemCollisionQuality quality)
        {
            var ps = Dropping(quality);
            ps.Play();
            for (int frame = 0; frame < 6; frame++) Frame();
            Assert.True(Only(ps).velocity.y > 0f, quality + " did not bounce");
        }

        [Fact]
        public void ALowSystemQueriesOnlyEveryFifthFrame()
        {
            // Its first query comes in its fifth frame: the particle meets the box in its fourth,
            // with no plane cached, and goes through.
            var ps = Dropping(ParticleSystemCollisionQuality.Low);
            ps.Play();
            for (int frame = 0; frame < 6; frame++) Frame();
            Assert.True(Only(ps).velocity.y < 0f);
        }

        [Fact]
        public void APausedSystemNeverQueries()
        {
            // Advanced by Simulate only, a Medium system gets no queries and meets no plane.
            var ps = Dropping(ParticleSystemCollisionQuality.Medium);
            ps.Simulate(0f, true, true, false);
            for (int frame = 0; frame < 6; frame++)
            {
                ps.Simulate(Dt, true, false, false);
                Frame();
            }
            Assert.True(Only(ps).velocity.y < 0f);
        }

        [Fact]
        public void APlaneIsCarriedIntoAScaledSpaceThroughItsPointNearestTheWorldOrigin()
        {
            // A Local system scaled (1, 2.5, 1), turned 90 degrees about x, far from the origin; a
            // plane turned 20 degrees about x near it. In simulation space the plane's up carried
            // in like a direction is the normal; the plane goes through its point nearest the world
            // origin, carried in like a point. A particle placed half a radius in front of that
            // plane bounces; by the plane through the transform's position it would be far from it.
            var plane = new GameObject("plane").transform;
            m_Objects.Add(plane.gameObject);
            plane.SetPositionAndRotation(new Vector3(96f, -0.5f, 60f), Quaternion.Euler(20f, 0f, 0f));
            var go = new GameObject("particles");
            m_Objects.Add(go);
            go.SetActive(false);
            go.transform.SetPositionAndRotation(new Vector3(96f, 1f, 60f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(1f, 2.5f, 1f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.startSpeed = 0f; main.startSize = 0.2f; main.startLifetime = 10f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane); c.bounce = 1f;
            go.SetActive(true);
            ps.Simulate(0f, false, true, false);

            var toSim = go.transform.localToWorldMatrix.inverse;
            var n = plane.up;
            var nearest = n * Vector3.Dot(n, plane.position);
            var normal = toSim.MultiplyVector(n).normalized;
            float offset = -Vector3.Dot(normal, toSim.MultiplyPoint3x4(nearest));
            float byPosition = -Vector3.Dot(normal, toSim.MultiplyPoint3x4(plane.position));
            Assert.True(Mathf.Abs(offset - byPosition) > 1f);
            // Moving into the plane at 1 unit/s, ending the step 0.05 in front of it.
            var velocity = -normal;
            var end = -normal * (offset - 0.05f);
            ps.SetParticles(new[] { new ParticleSystem.Particle
            {
                position = end - velocity * 0.025f, velocity = velocity, startLifetime = 10f, remainingLifetime = 10f, startSize = 0.2f,
            } });
            ps.Simulate(0.025f, false, false, false);
            var particles = new ParticleSystem.Particle[1];
            ps.GetParticles(particles);
            Assert.True(Vector3.Dot(particles[0].velocity, normal) > 0f, "no bounce");
        }
    }
}
