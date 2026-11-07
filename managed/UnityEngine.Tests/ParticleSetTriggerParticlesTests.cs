using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        [NonSerialized] public Action<ParticleSystem> handler;
        [NonSerialized] public Exception thrown;
        [NonSerialized] public int calls;

        void OnParticleTrigger()
        {
            calls++;
            try { handler?.Invoke(GetComponent<ParticleSystem>()); }
            catch (Exception e) { thrown ??= e; }
        }
    }

    /// <summary>ParticlePhysicsExtensions.Get/SetTriggerParticles (6000.0 Scripting API: "Write
    /// modified particles back to the Particle System, during a call to OnParticleTrigger"; offset
    /// is "the offset into the destination particle array"), with what the 6000.0.71f1 Editor
    /// shows in tests/projects/particletriggerapi. Nothing here assumes an order of the trigger
    /// entries: Unity does not document one.</summary>
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
            f.ps.ClearTriggerEvents();
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

        // Measured (write/subset): the list's first count particles go to the entries from offset
        // on, whole (seed included); offset + count must stay below the list's size.
        [Fact]
        public void OffsetAndCountWriteTheListsFirstParticlesFromTheOffset()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center), At(3, Center), At(4, Center) });
            uint[] listed = null;
            Exception reachingTheEnd = null;
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                listed = list.Select(p => p.randomSeed).ToArray();
                Recolor(list);
                reachingTheEnd = Record.Exception(() => ps.SetTriggerParticles(TriggerEvent.Inside, list, 2, 2));
                ps.SetTriggerParticles(TriggerEvent.Inside, list, 1, 2);
            };
            TriggerFrame(f);
            Assert.IsType<ArgumentOutOfRangeException>(reachingTheEnd);
            var all = new ParticleSystem.Particle[4];
            Assert.Equal(4, f.ps.GetParticles(all));
            // Entry 0 and 3 unchanged; entries 1 and 2 now hold the list's particles 0 and 1, red.
            var expected = new[] { (listed[0], White), (listed[0], Red), (listed[1], Red), (listed[3], White) }.OrderBy(e => e.Item1).ThenBy(e => e.Item2.r).ThenBy(e => e.Item2.g).ToArray();
            var actual = all.Select(p => (p.randomSeed, p.startColor)).OrderBy(e => e.Item1).ThenBy(e => e.Item2.r).ThenBy(e => e.Item2.g).ToArray();
            Assert.Equal(expected, actual);
        }

        // Measured (write/all): GetTriggerParticles reads the particles as they are now, so a
        // write-back to one event's list shows in another event's list read after it.
        [Fact]
        public void AReadAfterAWriteBackShowsTheWrittenParticles()
        {
            using var f = new Fixture(inside: Overlap.Callback, enter: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center) });
            Color32 entered = default;
            f.writer.handler = ps =>
            {
                var inside = Entries(ps, TriggerEvent.Inside);
                Recolor(inside);
                ps.SetTriggerParticles(TriggerEvent.Inside, inside);
                entered = Entries(ps, TriggerEvent.Enter).Single().startColor;
            };
            TriggerFrame(f);
            Assert.Equal(Red, entered);
        }

        // Measured (write/subset frame 63): an entry whose particle a write-back replaced with
        // another particle (another seed) is no longer returned; the safe size still counts it.
        [Fact]
        public void AnEntryOverwrittenWithAnotherParticleIsNotReturned()
        {
            using var f = new Fixture(inside: Overlap.Callback, enter: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center) });
            int safe = -1, read = -1;
            f.writer.handler = ps =>
            {
                var inside = Entries(ps, TriggerEvent.Inside);
                var other = inside.First(p => p.randomSeed == 1);
                // Write particle 1 over both entries.
                ps.SetTriggerParticles(TriggerEvent.Inside, new List<ParticleSystem.Particle> { other, other });
                safe = ps.GetSafeTriggerParticlesSize(TriggerEvent.Enter);
                read = ps.GetTriggerParticles(TriggerEvent.Enter, new List<ParticleSystem.Particle>());
            };
            TriggerFrame(f);
            Assert.Equal(2, safe);
            Assert.Equal(1, read);
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
        public void ArgumentsFailAsTheEditorDoes()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center) });
            Exception triggerNull = null, triggerNegative = null, pastTheList = null;
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                Assert.Equal(2, list.Count);
                Recolor(list);
                triggerNull = Record.Exception(() => ps.SetTriggerParticles(TriggerEvent.Inside, null, 0, 1));
                triggerNegative = Record.Exception(() => ps.SetTriggerParticles(TriggerEvent.Inside, list, -1, 1));
                pastTheList = Record.Exception(() => ps.SetTriggerParticles(TriggerEvent.Inside, list, 0, list.Count + 5));
                // The whole-list overload writes a list longer than the entries up to the entries.
                list.Add(list[0]);
                ps.SetTriggerParticles(TriggerEvent.Inside, list);
            };
            TriggerFrame(f);
            Assert.IsType<ArgumentNullException>(triggerNull);
            Assert.IsType<ArgumentOutOfRangeException>(triggerNegative);
            Assert.IsType<ArgumentOutOfRangeException>(pastTheList);
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

        [Fact]
        public void GetTriggerParticlesReadsTheLiveParticlesDuringTheCallbackOnly()
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
            // Measured (update/*): after the callback the lists are empty.
            Assert.Empty(Entries(f.ps, TriggerEvent.Inside));
            Assert.Equal(0, f.ps.GetSafeTriggerParticlesSize(TriggerEvent.Inside));
        }

        // Measured (update/*): outside OnParticleTrigger a write-back writes nothing and logs nothing.
        [Fact]
        public void AWriteBackOutsideOnParticleTriggerWritesNothing()
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
                if (type == LogType.Error || type == LogType.Exception) lock (errors) errors.Add(message);
            }
            Application.logMessageReceived += Capture;
            try { f.ps.SetTriggerParticles(TriggerEvent.Inside, kept); }
            finally { Application.logMessageReceived -= Capture; }
            Assert.Empty(errors);
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

        // Unbound's choice (unmeasured): after a write-back removed a listed particle, positions in
        // a list read again count among the particles it returns, so nothing lands on another one.
        [Fact]
        public void AWriteBackAfterAListedParticleLeftMapsOntoTheReturnedParticles()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Center), At(3, Center) });
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Inside);
                int first = list.FindIndex(p => p.randomSeed == 1);
                var gone = list[first];
                gone.remainingLifetime = 0f;
                list[first] = gone;
                ps.SetTriggerParticles(TriggerEvent.Inside, list);
                Assert.Equal(3, ps.GetSafeTriggerParticlesSize(TriggerEvent.Inside));
                var rest = Entries(ps, TriggerEvent.Inside);
                Assert.Equal(new uint[] { 2, 3 }, Seeds(rest));
                Recolor(rest);
                ps.SetTriggerParticles(TriggerEvent.Inside, rest);
            };
            TriggerFrame(f);
            var all = new ParticleSystem.Particle[3];
            Assert.Equal(2, f.ps.GetParticles(all));
            Assert.Equal(new uint[] { 2, 3 }, Seeds(all.Take(2)));
            Assert.All(all.Take(2), p => Assert.Equal(Red, p.startColor));
        }

        // Measured (read/*): ColliderData lists the colliders each particle is inside as the query
        // mode asks: Disabled none (with a warning per call), One the first in the module's list,
        // All every one; Outside and Exit throw.
        [Theory]
        [InlineData(ParticleSystemColliderQueryMode.Disabled, 0)]
        [InlineData(ParticleSystemColliderQueryMode.One, 1)]
        [InlineData(ParticleSystemColliderQueryMode.All, 2)]
        public void ColliderDataFollowsTheQueryMode(ParticleSystemColliderQueryMode mode, int expected)
        {
            using var f = new Fixture(inside: Overlap.Callback, outside: Overlap.Callback, enter: Overlap.Callback, exit: Overlap.Callback);
            var box = new GameObject("trigger box");
            box.transform.position = Center;
            box.AddComponent<BoxCollider>().isTrigger = true;
            Physics.SyncTransforms();
            try
            {
                var trigger = f.ps.trigger;
                trigger.AddCollider(box.GetComponent<Collider>());
                trigger.colliderQueryMode = mode;
                f.ps.SetParticles(new[] { At(1, Center), At(2, Away) });
                Component[] colliders = null;
                Exception outside = null, exit = null;
                var warnings = new List<string>();
                void Capture(string message, string stackTrace, LogType type) { if (type == LogType.Warning) lock (warnings) warnings.Add(message); }
                f.writer.handler = ps =>
                {
                    var list = new List<ParticleSystem.Particle>();
                    Assert.Equal(1, ps.GetTriggerParticles(TriggerEvent.Inside, list, out var data));
                    colliders = Enumerable.Range(0, data.GetColliderCount(0)).Select(i => data.GetCollider(0, i)).ToArray();
                    Assert.Throws<IndexOutOfRangeException>(() => data.GetCollider(0, colliders.Length));
                    outside = Record.Exception(() => ps.GetTriggerParticles(TriggerEvent.Outside, list, out _));
                    exit = Record.Exception(() => ps.GetTriggerParticles(TriggerEvent.Exit, list, out _));
                };
                Application.logMessageReceived += Capture;
                try { TriggerFrame(f); }
                finally { Application.logMessageReceived -= Capture; }
                Assert.Equal(expected, colliders.Length);
                // The module's order: the sphere was added first.
                if (expected > 0) Assert.Same(f.sphere.GetComponent<Collider>(), colliders[0]);
                if (expected > 1) Assert.Same(box.GetComponent<Collider>(), colliders[1]);
                Assert.Equal("Querying the collider data for the Outside event is not supported, because when a particle is outside the collision volume, it is always outside every collider.",
                    Assert.IsType<InvalidOperationException>(outside).Message);
                Assert.Equal("Querying the collider data for the Exit event is not currently supported.", Assert.IsType<InvalidOperationException>(exit).Message);
                Assert.Equal(mode == ParticleSystemColliderQueryMode.Disabled, warnings.Exists(w => w.StartsWith("Trigger module must enable collider queries")));
            }
            finally { Object.DestroyImmediate(box); }
        }

        // Measured (read/all frames 68-69): a particle that leaves a box during the step is still
        // inside in that step (the step's movement is swept), and exits in the next.
        [Fact]
        public void AParticleLeavingABoxDuringTheStepIsStillInside()
        {
            using var f = new Fixture(inside: Overlap.Callback, exit: Overlap.Callback);
            var box = new GameObject("trigger box");
            box.transform.position = Away;
            box.AddComponent<BoxCollider>().isTrigger = true;
            Physics.SyncTransforms();
            try
            {
                var trigger = f.ps.trigger;
                trigger.RemoveCollider(0);
                trigger.AddCollider(box.GetComponent<Collider>());
                // Radius .1; the box's face at x .5: starts at x .55 (inside by .05), moves .1 a step.
                var start = At(1, Away + new Vector3(.55f, 0f, 0f));
                start.velocity = new Vector3(4f, 0f, 0f);
                f.ps.SetParticles(new[] { start });
                var seen = new List<(int, int)>();
                f.writer.handler = ps => seen.Add((ps.GetSafeTriggerParticlesSize(TriggerEvent.Inside), ps.GetSafeTriggerParticlesSize(TriggerEvent.Exit)));
                TriggerFrame(f);
                TriggerFrame(f);
                Assert.Equal(new[] { (1, 0), (0, 1) }, seen);
            }
            finally { Object.DestroyImmediate(box); }
        }

        static readonly FieldInfo SerialNext = typeof(ParticleSystem).GetField("m_TriggerSerialNext", BindingFlags.NonPublic | BindingFlags.Instance);

        [Fact]
        public void AParticleNoLongerListedIsNotWrittenAfterTheSerialCounterWraps()
        {
            Assert.NotNull(SerialNext);
            using var f = new Fixture(enter: Overlap.Callback);
            // Seed 1 (first in the buffer) enters now and gets the counter's last serial; seed 2
            // enters in the next frame, when the counter is back at that serial, as after
            // 2^24 - 1 further stamps. Seed 1 stays inside and is not listed again.
            f.ps.SetParticles(new[] { At(1, Center), At(2, Away) });
            SerialNext.SetValue(f.ps, 0xFFFFFFu);
            uint[] first = null;
            f.writer.handler = ps => first = Seeds(Entries(ps, TriggerEvent.Enter));
            TriggerFrame(f);
            Assert.Equal(new uint[] { 1 }, first);
            var all = new ParticleSystem.Particle[2];
            Assert.Equal(2, f.ps.GetParticles(all));
            for (int i = 0; i < all.Length; i++)
                if (all[i].randomSeed == 2) all[i].position = Center + new Vector3(.2f, 0f, 0f);
            f.ps.SetParticles(all);
            SerialNext.SetValue(f.ps, 0xFFFFFFu);
            uint[] second = null;
            f.writer.handler = ps =>
            {
                var list = Entries(ps, TriggerEvent.Enter);
                second = Seeds(list);
                Recolor(list);
                ps.SetTriggerParticles(TriggerEvent.Enter, list);
            };
            TriggerFrame(f);
            Assert.Equal(new uint[] { 2 }, second);
            Assert.Equal(2, f.ps.GetParticles(all));
            Assert.Equal(new uint[] { 1, 2 }, Seeds(all));
            Assert.Equal(White, all.Single(p => p.randomSeed == 1).startColor);
            Assert.Equal(Red, all.Single(p => p.randomSeed == 2).startColor);
        }

        /// <summary>A system whose particles rotate at a constant rate and run a Birth
        /// sub-emitter whose rate never completes a child in the test, so its counter only
        /// accumulates. Seed 1 sits in the trigger, seed 2 (its twin) away from it.</summary>
        static ParticleSystem RotatingWithBirthCounter(Fixture f)
        {
            var rotation = f.ps.rotationOverLifetime; rotation.enabled = true; rotation.z = 1f;
            var go = new GameObject("birth child");
            go.SetActive(false);
            var child = go.AddComponent<ParticleSystem>();
            var main = child.main; main.playOnAwake = false; main.startSpeed = 0f;
            var shape = child.shape; shape.enabled = false;
            var emission = child.emission; emission.rateOverTime = 4f;
            go.SetActive(true);
            var subs = f.ps.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
            f.ps.SetParticles(new[] { At(1, Center), At(2, Away) });
            return child;
        }

        static ParticleSystem.Particle Internal(ParticleSystem ps, uint seed) =>
            ps.m_Particles.Take(ps.particleCount).Single(p => p.randomSeed == seed);

        /// <summary>The listed particle's rotation, life and Birth counter advanced exactly as
        /// its unlisted twin's did.</summary>
        static void AssertAdvancedLikeItsTwin(ParticleSystem ps)
        {
            var live = Live(ps);
            Assert.NotEqual(0f, live[2].rotation3D.z);
            Assert.Equal(live[2].rotation3D, live[1].rotation3D);
            Assert.Equal(live[2].angularVelocity3D, live[1].angularVelocity3D);
            Assert.Equal(live[2].remainingLifetime, live[1].remainingLifetime);
            var twin = Internal(ps, 2);
            var listed = Internal(ps, 1);
            Assert.NotEqual(0f, twin.m_EmitAccumulator0);
            Assert.Equal(twin.m_EmitAccumulator0, listed.m_EmitAccumulator0);
        }

        [Fact]
        public void AnUnchangedWriteBackLeavesRotationAndBirthCountersAdvancing()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            var child = RotatingWithBirthCounter(f);
            try
            {
                f.writer.handler = ps =>
                {
                    var list = Entries(ps, TriggerEvent.Inside);
                    Assert.Equal(new uint[] { 1 }, Seeds(list));
                    ps.SetTriggerParticles(TriggerEvent.Inside, list);
                };
                for (int frame = 0; frame < 3; frame++) TriggerFrame(f);
                Assert.Equal(3, f.writer.calls);
                Assert.Equal(0, child.particleCount);
                AssertAdvancedLikeItsTwin(f.ps);
            }
            finally { Object.DestroyImmediate(child.gameObject); }
        }

        [Fact]
        public void AColorOnlyChangePersistsWithoutRollingBackTheRestOfTheParticle()
        {
            using var f = new Fixture(inside: Overlap.Callback);
            var child = RotatingWithBirthCounter(f);
            try
            {
                // .05 s runs as two .025 s steps: each lists seed 1, and the first entry predates
                // the second step.
                f.writer.handler = ps =>
                {
                    var list = Entries(ps, TriggerEvent.Inside);
                    Assert.Equal(new uint[] { 1, 1 }, Seeds(list));
                    Recolor(list);
                    ps.SetTriggerParticles(TriggerEvent.Inside, list);
                };
                for (int frame = 0; frame < 2; frame++) TriggerFrame(f, .05f);
                Assert.Equal(2, f.writer.calls);
                Assert.Equal(0, child.particleCount);
                var live = Live(f.ps);
                Assert.Equal(Red, live[1].startColor);
                Assert.Equal(White, live[2].startColor);
                AssertAdvancedLikeItsTwin(f.ps);
            }
            finally { Object.DestroyImmediate(child.gameObject); }
        }
    }
}
