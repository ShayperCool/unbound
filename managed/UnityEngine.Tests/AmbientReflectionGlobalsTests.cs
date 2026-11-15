using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>What the lighting settings' intensities do to the built-in shader variables and
    /// the default reflection's decode values (Unity 6000.0.71f1, tests/projects/indirectspec:
    /// Trilight ambient, a half-float default reflection, ambient intensity 1/0.5 x reflection
    /// intensity 1/0.25, both color spaces).</summary>
    public class AmbientReflectionGlobalsTests
    {
        sealed class State : System.IDisposable
        {
            readonly ColorSpace m_Space = QualitySettings.s_ColorSpace;
            readonly AmbientMode m_Mode = RenderSettings.ambientMode;
            readonly Color m_Sky = RenderSettings.ambientSkyColor, m_Equator = RenderSettings.ambientEquatorColor, m_Ground = RenderSettings.ambientGroundColor;
            readonly float m_Ambient = RenderSettings.ambientIntensity, m_Reflection = RenderSettings.reflectionIntensity;

            public void Dispose()
            {
                QualitySettings.s_ColorSpace = m_Space;
                RenderSettings.ambientMode = m_Mode;
                RenderSettings.ambientSkyColor = m_Sky;
                RenderSettings.ambientEquatorColor = m_Equator;
                RenderSettings.ambientGroundColor = m_Ground;
                RenderSettings.ambientIntensity = m_Ambient;
                RenderSettings.reflectionIntensity = m_Reflection;
            }
        }

        static Dictionary<string, Vector4> Globals()
        {
            var values = new List<(string Name, Vector4 Value)>();
            Unbound.Runtime.LightingGlobals.Collect(values, new List<string>());
            var result = new Dictionary<string, Vector4>();
            foreach (var (name, value) in values) result[name] = value;
            return result;
        }

        static void Near(Vector4 expected, Vector4 actual)
        {
            for (int i = 0; i < 4; i++) Assert.InRange(actual[i], expected[i] - 1e-6f, expected[i] + 1e-6f);
        }

        [Theory]
        [InlineData(ColorSpace.Linear)]
        [InlineData(ColorSpace.Gamma)]
        public void AmbientIntensityLeavesTheAmbientColorsAsTheSettingsHoldThem(ColorSpace space)
        {
            using var state = new State();
            QualitySettings.s_ColorSpace = space;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.25f, 0.3f, 0.4f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.05f, 0.02f, 1f);
            bool linear = space == ColorSpace.Linear;
            Color Space(Color c) => linear ? c.linear : c;
            foreach (float intensity in new[] { 1f, 0.5f })
            {
                RenderSettings.ambientIntensity = intensity;
                var globals = Globals();
                Near(Space(RenderSettings.ambientSkyColor), globals["unity_AmbientSky"]);
                Near(Space(RenderSettings.ambientEquatorColor), globals["unity_AmbientEquator"]);
                Near(Space(RenderSettings.ambientGroundColor), globals["unity_AmbientGround"]);
                Near((Vector4)Space(RenderSettings.ambientSkyColor) * 0.5f, globals["glstate_lightmodel_ambient"]);
            }
        }

        [Theory]
        [InlineData(ColorSpace.Linear, 0.25f, 0.05087609f)]
        [InlineData(ColorSpace.Gamma, 0.25f, 0.25f)]
        [InlineData(ColorSpace.Linear, 1f, 1f)]
        [InlineData(ColorSpace.Gamma, 0f, 0f)]
        public void TheDefaultReflectionDecodesWithTheReflectionIntensityInTheColorSpace(ColorSpace space, float intensity, float x)
        {
            using var state = new State();
            QualitySettings.s_ColorSpace = space;
            RenderSettings.reflectionIntensity = intensity;
            var decode = ReflectionProbe.defaultTextureHDRDecodeValues;
            Assert.Equal(new Vector4(x, 1f, 0f, 0f), decode);
            // The DOTS instancing defaults take the same value.
            Assert.Equal(decode, Globals()["unity_DOTS_SpecCube0_HDR"]);
        }
    }
}
