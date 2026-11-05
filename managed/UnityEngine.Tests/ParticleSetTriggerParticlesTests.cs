using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using Xunit;
using TriggerEvent = UnityEngine.ParticleSystemTriggerEventType;
using Overlap = UnityEngine.ParticleSystemOverlapAction;

namespace UnityEngine.Tests
{
    /// <summary>Runs the test's handler from OnParticleTrigger and keeps what it throws (message
    /// dispatch logs a callback's exception instead of passing it on).</summary>
    public class TriggerWriteBack : MonoBehaviour
    {
        public Action<ParticleSystem> handler;
        public Exception thrown;
        public int calls;

        void OnParticleTrigger()
        {
            calls++;
            try { handler?.Invoke(GetComponent<ParticleSystem>()); }
            catch (Exception e) { thrown ??= e; }
        }
    }

    /// <summary>ParticlePhysicsExtensions.SetTriggerParticles (6000.0 Scripting API: "Write
    /// modified particles back to the Particle System, during a call to OnParticleTrigger";
    /// offset and count select "a subset of the returned particles"). Nothing here assumes an
    /// order of the trigger entries: Unity does not document one.</summary>
    public class ParticleSetTriggerParticlesTests
    {
        static readonly Color32 White = new Color32(255, 255, 255, 255), Red = new Color32(255, 0, 0, 255);
        static readonly Vector3 Center = new Vector3(0f, 0f, 1.5f), Away = new Vector3(0f, 0f, -5f);

        /// <summary>A radius-.5 sphere trigger at <see cref="Center"/> and a still, non-emitting
        /// system whose trigger module uses it with the given actions.</summary>
        sealed class Fixture : IDisposable
        {
            public readonly GameObject sphere;
            public readonly ParticleSystem ps;
            public readonly TriggerWriteBack writer;

            public Fixture(Overlap inside = Overlap.Ignore, Overlap outside = Overlap.Ignore, Overlap enter = Overlap.Ignore, Overlap exit = Overlap.Ignore)
            {
                sphere = new GameObject("trigger sphere");
                sphere.transform.position = Center;
                sphere.AddComponent<SphereCollider>().isTrigger = true;
                Physics.SyncTransforms();
                var go = new GameObject("particles");
                go.SetActive(false);
                ps = go.AddComponent<ParticleSystem>();
                var main = ps.main; main.startLifetime = 10f; main.startSpeed = 0f; main.startSize = .2f;
                var emission = ps.emission; emission.rateOverTime = 0f;
                var shape = ps.shape; shape.enabled = false;
                var trigger = ps.trigger; trigger.enabled = true; trigger.AddCollider(sphere.GetComponent<Collider>());
                trigger.inside = inside; trigger.outside = outside; trigger.enter = enter; trigger.exit = exit;
                writer = go.AddComponent<TriggerWriteBack>();
                go.SetActive(true);
                ps.Simulate(0f, true, true, false);
            }

            public void Dispose()
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(sphere);
            }
        }

        static ParticleSystem.Particle At(uint seed, Vector3 position, float startLifetime = 10f, float remaining = 10f) => new ParticleSystem.Particle
        {
            position = position, velocity = Vector3.zero, startLifetime = startLifetime, remainingLifetime = remaining,
            startSize = .2f, startColor = White, randomSeed = seed,
        };

        /// <summary>A player-loop frame of the system: FrameUpdate's trigger collection around the
        /// step, then UpdateAll's OnParticleTrigger.</summary>
        static void TriggerFrame(Fixture f, float dt = .025f)
        {
            foreach (var list in f.ps.m_TriggerParticles) list.Clear();
            f.ps.m_CollectTriggers = true;
            try { f.ps.Simulate(dt, false, false, false); }
            finally { f.ps.m_CollectTriggers = false; }
            f.ps.SendTriggerMessage();
            if (f.writer.thrown != null) ExceptionDispatchInfo.Capture(f.writer.thrown).Throw();
        }

        /// <summary>A step that collects nothing and sends no message.</summary>
        static void Frame(Fixture f, float dt = .025f) => f.ps.Simulate(dt, false, false, false);

        static Dictionary<uint, ParticleSystem.Particle> Live(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount + 1];
            int n = ps.GetParticles(particles);
            return particles.Take(n).ToDictionary(p => p.randomSeed);
        }

        static List<ParticleSystem.Particle> Entries(ParticleSystem ps, TriggerEvent type)
        {
            var list = new List<ParticleSystem.Particle>();
            ps.GetTriggerParticles(type, list);
            return list;
        }

        static void Recolor(List<ParticleSystem.Particle> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                p.startColor = Red;
                list[i] = p;
            }
        }

        static uint[] Seeds(IEnumerable<ParticleSystem.Particle> particles) => particles.Select(p => p.randomSeed).OrderBy(s => s).ToArray();

        [Fact]
        public void ColorAndVelocityChangedInOnParticleTriggerPersistIntoTheNextFrame()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center) });
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                Assert.Single(list);
                var p = list[0];
                p.startColor = Red;
                p.velocity = new Vector3(1f, 0f, 0f);
                list[0] = p;
                ps.SetTriggerParticles(TriggerEvent.Inside, list);
            };
            TriggerFrame(f);
            Assert.Equal(1, f.writer.calls);
            f.writer.handler = null;
            Frame(f);
            var after = Live(f.ps)[1];
            Assert.Equal(Red, after.startColor);
            Assert.Equal(new Vector3(1f, 0f, 0f), after.velocity);
            Assert.InRange(after.position.x, .02f, .03f);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void ANonpositiveRemainingLifetimeKillsTheParticleAsSetParticlesDoes(float remaining)
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center), At(3, Away) });
            int countAfterWrite = -1;
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                Assert.Equal(new uint[] { 1, 2 }, Seeds(list));
                int i = list.FindIndex(p => p.randomSeed == 2);
                var killed = list[i];
                killed.remainingLifetime = remaining;
                list[i] = killed;
                ps.SetTriggerParticles(TriggerEvent.Inside, list);
                countAfterWrite = ps.particleCount;
            };
            TriggerFrame(f);
            // SetParticles removes such a particle at once; so does the write-back.
            Assert.Equal(2, countAfterWrite);
            Assert.Equal(new uint[] { 1, 3 }, Live(f.ps).Keys.OrderBy(s => s).ToArray());
        }

        [Fact]
        public void OffsetAndCountWriteBackOnlyThatSubset()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center), At(3, Center), At(4, Center) });
            uint[] listed = null;
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                listed = list.Select(p => p.randomSeed).ToArray();
                Recolor(list);
                ps.SetTriggerParticles(TriggerEvent.Inside, list, 1, 2);
            };
            TriggerFrame(f);
            Assert.Equal(new uint[] { 1, 2, 3, 4 }, listed.OrderBy(s => s).ToArray());
            var live = Live(f.ps);
            for (int k = 0; k < listed.Length; k++)
                Assert.Equal(k == 1 || k == 2 ? Red : White, live[listed[k]].startColor);
        }

        [Fact]
        public void WriteBackFindsItsParticleAfterAnEarlierOneDiedInTheSameUpdate()
        {
            // .05 s runs as two .025 s steps at the default maximumParticleDeltaTime. Seed 1,
            // first in the buffer, dies in the second step; its removal moves the inside particle
            // (seed 3, listed in the first step) to the front of the buffer.
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Away, startLifetime: 1f, remaining: .035f), At(2, Away + Vector3.right), At(3, Center) });
            int countInCallback = -1;
            f.writer.handler = ps =>
            {
                countInCallback = ps.particleCount;
                var list = Entries(ps, TriggerEvent.Inside);
                Assert.NotEmpty(list);
                Assert.All(list, p => Assert.Equal(3u, p.randomSeed));
                Recolor(list);
                ps.SetTriggerParticles(TriggerEvent.Inside, list);
            };
            TriggerFrame(f, .05f);
            Assert.Equal(2, countInCallback);
            var live = Live(f.ps);
            Assert.Equal(new uint[] { 2, 3 }, live.Keys.OrderBy(s => s).ToArray());
            Assert.Equal(Red, live[3].startColor);
            Assert.Equal(White, live[2].startColor);
        }

        [Fact]
        public void WriteBackFindsItsParticleAfterSetParticlesReorderedTheBuffer()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Away), At(2, Away + Vector3.right), At(3, Center) });
            f.writer.handler = ps =>
            {
                // Another script drops seed 1 and reverses the rest before the write-back.
                var all = new ParticleSystem.Particle[3];
                Assert.Equal(3, ps.GetParticles(all));
                ps.SetParticles(all.Where(p => p.randomSeed != 1).Reverse().ToArray());
                var list = Entries(ps, TriggerEvent.Inside);
                Assert.Equal(new uint[] { 3 }, Seeds(list));
                Recolor(list);
                ps.SetTriggerParticles(TriggerEvent.Inside, list);
            };
            TriggerFrame(f);
            var live = Live(f.ps);
            Assert.Equal(new uint[] { 2, 3 }, live.Keys.OrderBy(s => s).ToArray());
            Assert.Equal(Red, live[3].startColor);
            Assert.Equal(White, live[2].startColor);
        }

        [Fact]
        public void ArgumentsFailAsInSetParticles()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center) });
            Exception setNull = null, triggerNull = null, setNegative = null, triggerNegative = null;
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                Assert.Equal(2, list.Count);
                Recolor(list);
                var array = new ParticleSystem.Particle[2];
                Assert.Equal(2, ps.GetParticles(array));
                setNull = Record.Exception(() => ps.SetParticles((ParticleSystem.Particle[])null, 1, 0));
                triggerNull = Record.Exception(() => ps.SetTriggerParticles(TriggerEvent.Inside, null, 0, 1));
                setNegative = Record.Exception(() => ps.SetParticles(array, 1, -1));
                triggerNegative = Record.Exception(() => ps.SetTriggerParticles(TriggerEvent.Inside, list, -1, 1));
                // SetParticles(array, size) with size past the array writes the whole array.
                ps.SetTriggerParticles(TriggerEvent.Inside, list, 0, list.Count + 5);
            };
            TriggerFrame(f);
            Assert.IsType<ArgumentNullException>(setNull);
            Assert.IsType<ArgumentNullException>(triggerNull);
            Assert.IsType<ArgumentOutOfRangeException>(setNegative);
            Assert.IsType<ArgumentOutOfRangeException>(triggerNegative);
            var live = Live(f.ps);
            Assert.Equal(2, live.Count);
            Assert.All(live.Values, p => Assert.Equal(Red, p.startColor));
        }

        public static IEnumerable<object[]> EventLists()
        {
            yield return new object[] { TriggerEvent.Inside, new uint[] { 1 } };
            yield return new object[] { TriggerEvent.Enter, new uint[] { 1 } };
            yield return new object[] { TriggerEvent.Outside, new uint[] { 2, 3 } };
            yield return new object[] { TriggerEvent.Exit, new uint[] { 3 } };
        }

        [Theory]
        [MemberData(nameof(EventLists))]
        public void EachEventListWritesBackToItsOwnParticles(TriggerEvent type, uint[] expected)
        {
            using var f = new Fixture(Overlap.Callback, Overlap.Callback, Overlap.Callback, Overlap.Callback);
            // Seed 3 starts inside; then seed 1 moves in (enters) and seed 3 out (exits).
            f.ps.SetParticles(new[] { At(1, Away + Vector3.left), At(2, Away + Vector3.right), At(3, Center) });
            Frame(f);
            var all = new ParticleSystem.Particle[3];
            Assert.Equal(3, f.ps.GetParticles(all));
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].randomSeed == 1) all[i].position = Center;
                if (all[i].randomSeed == 3) all[i].position = Away + Vector3.up;
            }
            f.ps.SetParticles(all);
            uint[] listed = null;
            f.writer.handler = ps =>
            {
                var list = Entries(ps, type);
                listed = Seeds(list);
                Recolor(list);
                ps.SetTriggerParticles(type, list);
            };
            TriggerFrame(f);
            Assert.Equal(expected, listed);
            Assert.Equal(expected, Seeds(Live(f.ps).Values.Where(p => p.startColor.Equals(Red))));
        }

        /// <summary>Guard: the read side keeps its behaviour (green before and after).</summary>
        [Fact]
        public void GetTriggerParticlesIsUnchanged()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center + new Vector3(.1f, 0f, 0f)), At(3, Away) });
            f.writer.handler = ps =>
            {
                Assert.Equal(2, ps.GetSafeTriggerParticlesSize(TriggerEvent.Inside));
                Assert.Equal(0, ps.GetSafeTriggerParticlesSize(TriggerEvent.Outside));
                var list = new List<ParticleSystem.Particle> { default, default, default };
                Assert.Equal(2, ps.GetTriggerParticles(TriggerEvent.Inside, list));
                Assert.Equal(new uint[] { 1, 2 }, Seeds(list));
                var withData = new List<ParticleSystem.Particle>();
                Assert.Equal(2, ps.GetTriggerParticles(TriggerEvent.Inside, withData, out _));
                Assert.Equal(new uint[] { 1, 2 }, Seeds(withData));
                var live = Live(ps);
                foreach (var p in list)
                {
                    Assert.Equal(live[p.randomSeed].position, p.position);
                    Assert.Equal(live[p.randomSeed].velocity, p.velocity);
                    Assert.Equal(live[p.randomSeed].startColor, p.startColor);
                    Assert.Equal(live[p.randomSeed].startSize, p.startSize);
                }
            };
            TriggerFrame(f);
            Assert.Equal(1, f.writer.calls);
            // The frame's entries stay readable after the callback, until the next update.
            Assert.Equal(new uint[] { 1, 2 }, Seeds(Entries(f.ps, TriggerEvent.Inside)));
        }

        [Fact]
        public void AWriteBackOutsideOnParticleTriggerIsAnErrorAndWritesNothing()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center) });
            List<ParticleSystem.Particle> kept = null;
            f.writer.handler = ps => kept = Entries(ps, TriggerEvent.Inside);
            TriggerFrame(f);
            Assert.Single(kept);
            Recolor(kept);
            var errors = new List<string>();
            void Capture(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Error && message.Contains("SetTriggerParticles")) lock (errors) errors.Add(message);
            }
            Application.logMessageReceived += Capture;
            try { f.ps.SetTriggerParticles(TriggerEvent.Inside, kept); }
            finally { Application.logMessageReceived -= Capture; }
            Assert.Single(errors);
            Assert.Equal(White, Live(f.ps)[1].startColor);
        }

        /// <summary>Guard for an Unbound choice (unmeasured): a particle a trigger Kill ended in
        /// this update stays dead whatever is written back for it (green before and after).</summary>
        [Fact]
        public void AWriteBackDoesNotUndoATriggerKillOfTheSameUpdate()
        {
            using var f = new Fixture(inside: Overlap.Callback, enter: Overlap.Kill);
            f.ps.SetParticles(new[] { At(1, Center) });
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                Assert.Single(list);
                Recolor(list);
                ps.SetTriggerParticles(TriggerEvent.Inside, list);
            };
            TriggerFrame(f);
            var killed = Live(f.ps)[1];
            Assert.Equal(0f, killed.remainingLifetime);
            Assert.Equal(White, killed.startColor);
            Frame(f);
            Assert.Equal(0, f.ps.particleCount);
        }
    }
}
