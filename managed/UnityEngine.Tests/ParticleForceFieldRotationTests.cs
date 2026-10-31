using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleForceFieldRotationTests
    {
        // Independent Editor captures in the standalone force-field fixture establish these
        // one-substep values. A seed and a manually emitted particle isolate the field from emission.
        [Theory]
        [InlineData(0f, .5f, 1f, 0f, 2f)]
        [InlineData(2f, 0f, 1f, 0f, 2f)]
        [InlineData(2f, .5f, .25f, 0f, .5f)]
        [InlineData(2f, .5f, 3f, 0f, .5f)]
        [InlineData(-2f, .5f, 1f, 0f, 2f)]
        [InlineData(2f, 2f, 1f, 0f, -4f)]
        [InlineData(2f, -.5f, 1f, 0f, 3.5f)]
        [InlineData(2f, .5f, 0f, 0f, 1.25f)]
        public void RotationRelaxesStoredVelocityWithoutClamping(float speed, float attraction,
            float radius, float expectedX, float expectedZ)
        {
            var fieldObject = new GameObject("rotation field");
            var systemObject = new GameObject("rotation particles");
            try
            {
                var field = fieldObject.AddComponent<ParticleSystemForceField>();
                field.endRange = 100;
                field.gravity = 0;
                field.rotationSpeed = speed;
                field.rotationAttraction = attraction;
                systemObject.SetActive(false);
                var ps = systemObject.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
                var main = ps.main; main.playOnAwake = false; main.startLifetime = 10;
                var emission = ps.emission; emission.enabled = false;
                var shape = ps.shape; shape.enabled = false;
                var external = ps.externalForces; external.enabled = true;
                systemObject.SetActive(true);
                ps.Emit(new ParticleSystem.EmitParams {
                    position = new Vector3(radius, 0, 0), velocity = new Vector3(0, 0, 2),
                    startLifetime = 10, randomSeed = 6789 }, 1);
                ps.Simulate(.025f, false, false, false);
                var particles = new ParticleSystem.Particle[1];
                Assert.Equal(1, ps.GetParticles(particles));
                Assert.Equal(expectedX, particles[0].velocity.x, 5);
                Assert.Equal(expectedZ, particles[0].velocity.z, 5);
                Assert.Equal(Vector3.zero, particles[0].animatedVelocity);
                Assert.Equal(expectedZ * .025f, particles[0].position.z, 5);
                Assert.Equal(6789u, particles[0].randomSeed);
            }
            finally
            {
                Object.DestroyImmediate(systemObject);
                Object.DestroyImmediate(fieldObject);
            }
        }
    }
}
