using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Spherical harmonics and the ambient probe against the Editor's answers
    /// (tests/projects/urp/Reference/caps-6000.0.71f1.jsonl, CapsProbe.Record).</summary>
    public class SphericalHarmonicsTests
    {
        static void AssertCoefficients(float[] expected, SphericalHarmonicsL2 sh)
        {
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 9; i++)
                    Assert.True(Mathf.Abs(expected[c * 9 + i] - sh[c, i]) < 1e-5f, $"[{c},{i}] expected {expected[c * 9 + i]} got {sh[c, i]}");
        }

        [Fact]
        public void AmbientLightIsTheFirstCoefficient()
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(1f, 0.5f, 0.25f));
            AssertCoefficients(new float[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0.5f, 0, 0, 0, 0, 0, 0, 0, 0, 0.25f, 0, 0, 0, 0, 0, 0, 0, 0 }, sh);
        }

        [Fact]
        public void DirectionalLightProjectsAsTheEditorDoes()
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddDirectionalLight(new Vector3(0.36f, 0.48f, 0.8f), new Color(1f, 0.5f, 0.25f), 0.75f);
            AssertCoefficients(new[]
            {
                0.352941155f, 0.3388235f, 0.564705849f, 0.254117638f, 0.228705883f, 0.508235335f, 0.101470605f, 0.3811765f, -0.0333529375f,
                0.176470578f, 0.169411749f, 0.282352924f, 0.127058819f, 0.114352942f, 0.254117668f, 0.0507353023f, 0.190588251f, -0.0166764688f,
                0.08823529f, 0.0847058743f, 0.141176462f, 0.06352941f, 0.05717647f, 0.127058834f, 0.0253676511f, 0.0952941254f, -0.008338234f,
            }, sh);
            var directions = new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, new Vector3(0.6f, 0f, 0.8f) };
            var results = new Color[directions.Length];
            sh.Evaluate(directions, results);
            float[] red = { 0.623647f, -0.053999994f, 0.472235233f, 1.1205883f, 1.221487f };
            for (int i = 0; i < red.Length; i++) Assert.True(Mathf.Abs(red[i] - results[i].r) < 1e-5f, $"direction {i}: {results[i].r}");
        }

        [Fact]
        public void ABakedSkyboxProbeIsScaledByTheIntensityAsAGammaValue()
        {
            // Measured on the 6000.0 Editor (gamma project, a scene with lighting data): the
            // lighting data's probe times GammaToLinearSpace(ambientIntensity).
            var mode = RenderSettings.ambientMode;
            var intensity = RenderSettings.ambientIntensity;
            var baked = RenderSettings.s_BakedProbe;
            try
            {
                var sh = new SphericalHarmonicsL2();
                sh[0, 0] = 0.633008f; sh[0, 1] = 0.460993f; sh[0, 4] = -0.197994f;
                RenderSettings.s_BakedProbe = sh;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                foreach (var (value, l0) in new[] { (0.18f, 0.017225f), (0.36f, 0.067440f), (1f, 0.633008f), (2f, 2.908540f) })
                {
                    RenderSettings.ambientIntensity = value;
                    Assert.True(Mathf.Abs(RenderSettings.ambientProbe[0, 0] - l0) < 1e-4f * Mathf.Max(1f, l0), $"{value}: {RenderSettings.ambientProbe[0, 0]}");
                }
                RenderSettings.ambientIntensity = 0.18f;
                Assert.True(Mathf.Abs(RenderSettings.ambientProbe[0, 4] - -0.005388f) < 1e-5f);
            }
            finally
            {
                RenderSettings.s_BakedProbe = baked;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.ambientMode = mode;
            }
        }

        [Fact]
        public void AmbientProbeOfColorAndGradient()
        {
            var mode = RenderSettings.ambientMode;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.2f, 0.4f, 0.6f);
                AssertCoefficients(new[] { 0.0331047624f, 0, 0, 0, 0, 0, 0, 0, 0, 0.132868335f, 0, 0, 0, 0, 0, 0, 0, 0, 0.318546832f, 0, 0, 0, 0, 0, 0, 0, 0 },
                    RenderSettings.ambientProbe);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.8f, 0.4f, 0.2f);
                RenderSettings.ambientEquatorColor = new Color(0.1f, 0.5f, 0.3f);
                RenderSettings.ambientGroundColor = new Color(0.05f, 0.1f, 0.6f);
                AssertCoefficients(new[]
                {
                    0.107975774f, 0.163269758f, 0, 0, 0, 0, -0.0153051512f, 0, -0.04591544f,
                    0.1665093f, 0.0334343128f, 0, 0, 0, 0, 0.007426846f, 0, 0.0222805552f,
                    0.107434578f, -0.07768748f, 0, 0, 0, 0, -0.005343065f, 0, -0.01602919f,
                }, RenderSettings.ambientProbe);
            }
            finally
            {
                RenderSettings.ambientMode = mode;
            }
        }
    }
}
