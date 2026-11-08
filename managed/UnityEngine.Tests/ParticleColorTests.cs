using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ParticleSystem.MinMaxGradient values, the colours particles take from them, and the
    /// deprecated ParticleSystem.start* properties (measured in 6000.0.71f1,
    /// tests/projects/particlecurves gradient/* and deprecated/*).</summary>
    public class ParticleColorTests
    {
        static Gradient Ramp(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) }, new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(b.a, 1f) });
            return g;
        }

        static ParticleSystem Make()
        {
            var go = new GameObject("colors");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 5;
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 2f;
            main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            go.SetActive(true);
            return ps;
        }

        [Fact]
        public void ParticleColourBytesRoundHalfUpUnlikeTheScriptConversion()
        {
            // 0.3 × 255 is 76.5 in floats: scripts round it to even, particles up.
            Assert.Equal(76, ((Color32)new Color(0.3f, 0f, 0f, 0f)).r);
            Assert.Equal(77, ParticleColors.ToByte(0.3f));
            Assert.Equal(255, ParticleColors.ToByte(2f));
            Assert.Equal(0, ParticleColors.ToByte(-1f));
        }

        [Fact]
        public void ColourOverLifetimeModulatesByTheFactorPlusOneOver256()
        {
            var c = new Color32(166, 218, 134, 245);
            var f = new Color32(153, 153, 230, 128);
            Assert.Equal(new Color32(99, 131, 120, 123), ParticleColors.Modulate(c, f));
            Assert.Equal(c, ParticleColors.Modulate(c, new Color32(255, 255, 255, 255)));
            Assert.Equal(new Color32(0, 0, 0, 0), ParticleColors.Modulate(c, new Color32(0, 0, 0, 0)));
        }

        [Fact]
        public void TwoGradientsBlendBytesInStepsOfTheRandomByte()
        {
            var min = new Color32(242, 115, 13, 77);
            var max = new Color32(37, 218, 87, 245);
            // r = 0.9 → byte 229: (min·27 + max·229 + 128) / 256.
            Assert.Equal(new Color32(59, 207, 79, 227), ParticleColors.Lerp(min, max, 0.9f));
            Assert.Equal(min, ParticleColors.Lerp(min, max, 0f));
            Assert.Equal(min, ParticleColors.Lerp(min, max, -3f));
        }

        [Fact]
        public void EvaluateInAGradientModeWithoutItsGradientThrows()
        {
            Assert.Throws<NullReferenceException>(() => new ParticleSystem.MinMaxGradient((Gradient)null).Evaluate(0.5f));
            var two = new ParticleSystem.MinMaxGradient(Ramp(Color.black, Color.white), null);
            Assert.Throws<NullReferenceException>(() => two.Evaluate(0.5f, 0.5f));
            var random = new ParticleSystem.MinMaxGradient(Color.red) { mode = ParticleSystemGradientMode.RandomColor };
            Assert.Throws<NullReferenceException>(() => random.Evaluate(0.5f, 0.5f));
            Assert.Equal(Color.red, random.colorMax);
            // The colour modes need no gradient.
            Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), new ParticleSystem.MinMaxGradient(Color.black, Color.white).Evaluate(0f, 0.5f));
        }

        [Fact]
        public void AModuleHandsOutItsOwnGradientAndKeepsACopyOfWhatItIsGiven()
        {
            var ps = Make();
            try
            {
                var given = Ramp(Color.black, Color.white);
                var main = ps.main;
                main.startColor = given;
                given.SetKeys(new[] { new GradientColorKey(Color.red, 0f) }, new[] { new GradientAlphaKey(1f, 0f) });
                Assert.Equal(Color.white, main.startColor.Evaluate(1f));

                var read = main.startColor;
                read.gradient.SetKeys(new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.green, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                Assert.Equal(Color.green, main.startColor.Evaluate(1f));

                // A copy made by Instantiate has gradients of its own.
                var copy = Object.Instantiate(ps.gameObject).GetComponent<ParticleSystem>();
                var copyMain = copy.main;
                copyMain.startColor.gradient.SetKeys(new[] { new GradientColorKey(Color.blue, 0f) }, new[] { new GradientAlphaKey(1f, 0f) });
                Assert.Equal(Color.green, main.startColor.Evaluate(1f));
                Object.DestroyImmediate(copy.gameObject);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void AModuleGivenAGradientModeWithoutAGradientKeepsTheDefaultWhiteOne()
        {
            var ps = Make();
            try
            {
                var col = ps.colorOverLifetime;
                col.color = new ParticleSystem.MinMaxGradient((Gradient)null);
                Assert.NotNull(col.color.gradient);
                Assert.Equal(Color.white, col.color.Evaluate(0.3f));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void StartColourTimesColourOverLifetimeUsesBytes()
        {
            var ps = Make();
            try
            {
                var main = ps.main;
                main.startColor = new Color(0.65f, 0.6f, 0.5f, 1f); // bytes 166, 153, 128, 255
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new Color(0.6f, 0.3f, 0.5f, 0.5f);      // bytes 153, 77, 128, 128
                ps.Emit(1);
                var particles = new ParticleSystem.Particle[1];
                ps.GetParticles(particles);
                Assert.Equal(new Color32(166, 153, 128, 255), particles[0].startColor);
                Assert.Equal(new Color32((166 * 154) >> 8, (153 * 78) >> 8, (128 * 129) >> 8, (255 * 129) >> 8), particles[0].GetCurrentColor(ps));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void PerceptualBlendReturnsColoursIn8BitSteps()
        {
            var g = Ramp(new Color(0.77f, 0.21f, 0.5f, 1f), new Color(0.05f, 0.6f, 1f, 1f));
            g.mode = GradientMode.PerceptualBlend;
            foreach (float t in new[] { -1f, 0f, 0.12f, 0.5f, 1f, 2f })
            {
                var c = g.Evaluate(t);
                for (int k = 0; k < 3; k++)
                    Assert.Equal(Mathf.Round(c[k] * 255f), c[k] * 255f, 3);
            }
            // Blend keeps its unquantized values.
            g.mode = GradientMode.Blend;
            Assert.Equal(0.21f + (0.6f - 0.21f) * 0.12f, g.Evaluate(0.12f).g, 5);
        }

        [Fact]
        public void DeprecatedStartSettersWriteTheStoredScalarAndKeepTheMode()
        {
            var ps = Make();
            try
            {
#pragma warning disable 618
                var main = ps.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, AnimationCurve.Linear(0f, 0f, 1f, 1f), AnimationCurve.Linear(0f, 1f, 1f, 2f));
                ps.startSpeed = 2.5f;
                Assert.Equal(ParticleSystemCurveMode.TwoCurves, main.startSpeed.mode);
                Assert.Equal(2.5f, main.startSpeed.curveMultiplier);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 4f);
                ps.startLifetime = -1.5f;
                Assert.Equal(ParticleSystemCurveMode.TwoConstants, main.startLifetime.mode);
                Assert.Equal(0.0001f, main.startLifetime.constantMax);
                Assert.Equal(1f, main.startLifetime.constantMin);
                main.startRotationX = new ParticleSystem.MinMaxCurve(1f, 4f);
                ps.startRotation3D = new Vector3(0.5f, -2f, 7f);
                Assert.Equal(ParticleSystemCurveMode.TwoConstants, main.startRotationX.mode);
                Assert.Equal(0.5f, main.startRotationXMultiplier);
                // emissionRate and startColor set a constant.
                var emission = ps.emission;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(3f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                ps.emissionRate = 2.5f;
                Assert.Equal(ParticleSystemCurveMode.Constant, emission.rateOverTime.mode);
                main.startColor = Ramp(Color.black, Color.white);
                ps.startColor = Color.red;
                Assert.Equal(ParticleSystemGradientMode.Color, main.startColor.mode);
#pragma warning restore 618
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
    }
}
