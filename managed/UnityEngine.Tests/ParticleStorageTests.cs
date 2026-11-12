using Xunit;

namespace UnityEngine.Tests
{
    // Expectations come from Unity 6000.0.71f1 Editor captures, tests/projects/particleseedaxis
    // storage_*, inherit_* and rotation_emit.
    public class ParticleStorageTests
    {
        static ParticleSystem Make(string name, Transform parent = null)
        {
            var go = new GameObject(name); go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.duration = 5;
            main.startLifetime = 2; main.startSpeed = 0; main.startSize = .75f; main.startRotation = .3f;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.rateOverTime = 0;
            // A Manual sub-emitter emits the child's bursts.
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            go.SetActive(true);
            return ps;
        }

        [Fact]
        public void StorageFollowsTheModulesAtUpdatesAndScriptEmissionOnly()
        {
            var ps = Make("storage");
            try
            {
                var main = ps.main;
                main.startRotation3D = true; main.startSize3D = true;
                // Changing a module does not change the storage until the system updates or emits.
                Assert.False(ps.has3DParticleRotations);
                Assert.False(ps.hasNonUniformParticleSizes);
                ps.Emit(1); ps.Clear(false);
                Assert.True(ps.has3DParticleRotations);
                Assert.True(ps.hasNonUniformParticleSizes);
                main.startRotation3D = false; main.startSize3D = false;
                ps.Emit(1); ps.Clear(false);
                Assert.False(ps.has3DParticleRotations);
                Assert.False(ps.hasNonUniformParticleSizes);
                // A Simulate call shorter than a fixed step still updates the storage.
                var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.separateAxes = true;
                ps.Simulate(.01f, false, true);
                Assert.True(ps.has3DParticleRotations);
                Assert.False(ps.hasNonUniformParticleSizes);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ScriptSupplied3DValuesKeepTheStorageUntilAnEmptyRestart()
        {
            var ps = Make("supplied");
            try
            {
                var particle = new ParticleSystem.Particle { startLifetime = 2, remainingLifetime = 2, randomSeed = 7 };
                particle.rotation3D = Vector3.zero;
                ps.SetParticles(new[] { particle }, 1);
                Assert.True(ps.has3DParticleRotations);
                ps.Simulate(.01f, false, false, false);
                Assert.True(ps.has3DParticleRotations);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(false);
                ps.Simulate(.01f, false, false, false);
                Assert.False(ps.has3DParticleRotations);
                // Scalar setters do not ask for 3D storage.
                var scalar = new ParticleSystem.Particle { startLifetime = 2, remainingLifetime = 2, rotation = 10, startSize = 2 };
                ps.SetParticles(new[] { scalar }, 1);
                Assert.False(ps.has3DParticleRotations);
                Assert.False(ps.hasNonUniformParticleSizes);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        static ParticleSystem.Particle Trigger(bool start3D, bool refreshed, bool rotationModule)
        {
            var parent = Make("parent");
            var child = Make("child", parent.transform);
            try
            {
                var pe = parent.emission; pe.enabled = false;
                var main = child.main;
                if (start3D) { main.startRotation3D = true; main.startRotationX = .1f; main.startRotationY = .2f; main.startRotationZ = .3f; }
                if (rotationModule)
                {
                    var r = child.rotationOverLifetime; r.enabled = true; r.separateAxes = true;
                    r.x = .5f; r.y = .6f; r.z = .7f;
                }
                if (refreshed) { child.Emit(1); child.Clear(false); }
                var subs = parent.subEmitters; subs.enabled = true;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritRotation);
                var supplied = new ParticleSystem.Particle
                {
                    startLifetime = 4, remainingLifetime = 2, startSize = 2,
                    rotation3D = new Vector3(10f, 20f, 30f), randomSeed = 1,
                };
                parent.TriggerSubEmitter(0, ref supplied);
                var particles = new ParticleSystem.Particle[2];
                Assert.Equal(1, child.GetParticles(particles));
                return particles[0];
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void InheritedRotationKeepsZUnlessTheChildStartsIn3D()
        {
            // A child that never updated keeps a z rotation only, also with a 3D start rotation.
            var stale = Trigger(start3D: true, refreshed: false, rotationModule: false);
            Assert.Equal(new Vector3(0f, 0f, 47.188736f), stale.rotation3D);
            // Refreshed with a 3D start rotation, it adds the parent's three angles to its own.
            var start3D = Trigger(start3D: true, refreshed: true, rotationModule: false);
            Assert.Equal(15.7295771f, start3D.rotation3D.x, 1e-3f);
            Assert.Equal(31.4591541f, start3D.rotation3D.y, 1e-3f);
            Assert.Equal(47.188736f, start3D.rotation3D.z, 1e-3f);
            // 3D only through its rotation module, it still takes the parent's z alone.
            var module3D = Trigger(start3D: false, refreshed: true, rotationModule: true);
            Assert.Equal(0f, module3D.rotation3D.x, 1e-4f);
            Assert.Equal(0f, module3D.rotation3D.y, 1e-4f);
            Assert.Equal(47.188736f, module3D.rotation3D.z, 1e-3f);
            Assert.Equal(new Vector3(28.64789f, 34.37747f, 40.1070442f), module3D.angularVelocity3D);
            // Without rotation speed storage yet, the module's speed is not kept.
            var staleModule = Trigger(start3D: false, refreshed: false, rotationModule: true);
            Assert.Equal(Vector3.zero, staleModule.angularVelocity3D);
        }

        [Fact]
        public void AnEmittedParticleHasItsRotationModuleSpeedAtBirth()
        {
            var ps = Make("emit");
            try
            {
                var r = ps.rotationOverLifetime; r.enabled = true; r.separateAxes = true; r.x = .5f; r.y = .6f; r.z = .7f;
                ps.Emit(1);
                var particles = new ParticleSystem.Particle[2];
                Assert.Equal(1, ps.GetParticles(particles));
                Assert.Equal(new Vector3(28.64789f, 34.37747f, 40.1070442f), particles[0].angularVelocity3D);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void AChildWithUniformSizeStorageTakesTheXSizes()
        {
            var parent = Make("parent");
            var child = Make("child", parent.transform);
            try
            {
                var pe = parent.emission; pe.enabled = false;
                var main = child.main; main.startSize3D = true; main.startSizeX = .5f; main.startSizeY = .75f; main.startSizeZ = 1f;
                var subs = parent.subEmitters; subs.enabled = true;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritSize);
                var supplied = new ParticleSystem.Particle { startLifetime = 4, remainingLifetime = 2, randomSeed = 1 };
                supplied.startSize3D = new Vector3(2f, 3f, 4f);
                // The parent never updated either: its uniform storage reads the x size.
                Assert.Equal(new Vector3(2f, 2f, 2f), supplied.GetCurrentSize3D(parent));
                parent.TriggerSubEmitter(0, ref supplied);
                var particles = new ParticleSystem.Particle[2];
                Assert.Equal(1, child.GetParticles(particles));
                Assert.Equal(new Vector3(1f, 1f, 1f), particles[0].startSize3D);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }
    }
}
