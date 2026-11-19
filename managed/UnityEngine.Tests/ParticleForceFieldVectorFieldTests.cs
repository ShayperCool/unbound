using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A force field's vector field (tests/projects/particlecollision field/*): the stored
    /// velocity relaxes toward the nearest texel times the speed by attraction × 30 × dt, not on a
    /// particle's first step, not at all without a positive attraction, unclamped.</summary>
    public class ParticleForceFieldVectorFieldTests
    {
        const int Size = 4;

        static Texture3D Field(Color centre)
        {
            var texture = new Texture3D(Size, Size, Size, TextureFormat.RGBAFloat, false);
            var colors = new Color[Size * Size * Size];
            for (int z = 0; z < Size; z++)
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        colors[x + Size * (y + Size * z)] = new Color(x / 3f, 1f - y / 3f, .5f * z / 3f, 1f);
            // The texel the field's centre falls in: index (int)(4 × 0.5) = 2 on each axis.
            colors[2 + Size * (2 + Size * 2)] = centre;
            texture.SetPixels(colors);
            texture.Apply();
            return texture;
        }

        static Vector3[] Run(float attraction, Vector3 start, Texture3D texture, System.Action<Texture3D> between = null)
        {
            var fieldObject = new GameObject("vector field");
            var systemObject = new GameObject("vector field particles");
            try
            {
                var field = fieldObject.AddComponent<ParticleSystemForceField>();
                field.shape = ParticleSystemForceFieldShape.Box;
                field.endRange = 2f;
                field.directionX = field.directionY = field.directionZ = 0f;
                field.gravity = 0f;
                field.vectorField = texture;
                field.vectorFieldSpeed = 3f;
                field.vectorFieldAttraction = attraction;
                systemObject.SetActive(false);
                var ps = systemObject.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false; ps.randomSeed = 3;
                var main = ps.main; main.playOnAwake = false; main.startLifetime = 10; main.startSpeed = 0;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission; emission.enabled = false;
                var shape = ps.shape; shape.enabled = false;
                var external = ps.externalForces; external.enabled = true;
                systemObject.SetActive(true);
                ps.Emit(new ParticleSystem.EmitParams { position = start, velocity = new Vector3(.5f, 0, 0), startLifetime = 10 }, 1);
                var particles = new ParticleSystem.Particle[1];
                var velocities = new Vector3[3];
                for (int step = 0; step < 3; step++)
                {
                    if (step == 2) between?.Invoke(texture);
                    ps.Simulate(.02f, false, false, false);
                    Assert.Equal(1, ps.GetParticles(particles));
                    velocities[step] = particles[0].velocity;
                }
                return velocities;
            }
            finally
            {
                Object.DestroyImmediate(systemObject);
                Object.DestroyImmediate(fieldObject);
            }
        }

        static void Near(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 4);
            Assert.Equal(expected.y, actual.y, 4);
            Assert.Equal(expected.z, actual.z, 4);
        }

        [Theory]
        [InlineData(1f, .6f)]
        [InlineData(.5f, .3f)]
        [InlineData(2f, 1.2f)] // not clamped
        public void TheVelocityRelaxesTowardTheNearestTexelFromTheSecondStep(float attraction, float factor)
        {
            var v0 = new Vector3(.5f, 0, 0);
            var target = new Vector3(2f, 1f, 1f); // texel (2/3, 1/3, 1/3) × speed 3
            var velocities = Run(attraction, Vector3.zero, Field(new Color(2f / 3f, 1f / 3f, 1f / 3f, 1f)));
            Near(v0, velocities[0]);
            var v1 = v0 + (target - v0) * factor;
            Near(v1, velocities[1]);
            Near(v1 + (target - v1) * factor, velocities[2]);
        }

        [Fact]
        public void NoPositiveAttractionAndOutsideTheFieldLeaveTheVelocity()
        {
            var texture = Field(new Color(2f / 3f, 1f / 3f, 1f / 3f, 1f));
            foreach (var v in Run(0f, Vector3.zero, texture)) Near(new Vector3(.5f, 0, 0), v);
            foreach (var v in Run(1f, new Vector3(3f, 0, 0), texture)) Near(new Vector3(.5f, 0, 0), v);
        }

        [Fact]
        public void AnAppliedChangeOfTheTextureIsSampled()
        {
            var velocities = Run(1f, Vector3.zero, Field(new Color(2f / 3f, 1f / 3f, 1f / 3f, 1f)), texture =>
            {
                var colors = texture.GetPixels();
                colors[2 + Size * (2 + Size * 2)] = new Color(0f, 0f, 0f, 1f);
                texture.SetPixels(colors);
                texture.Apply();
            });
            var v1 = velocities[1];
            Near(v1 + (Vector3.zero - v1) * .6f, velocities[2]);
        }
    }
}
