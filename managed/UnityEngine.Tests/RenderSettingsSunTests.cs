using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>RenderSettings.sun and the forward main light (tests/projects/rendersettingssun).</summary>
    [Collection("RenderSettings state")]
    public sealed class RenderSettingsSunTests : IDisposable
    {
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly Light m_OldSun = RenderSettings.NamedSun;

        public RenderSettingsSunTests() => RenderSettings.sun = null;

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            RenderSettings.sun = m_OldSun;
        }

        Light Make(string name, float intensity, Color? color = null, LightShadows shadows = LightShadows.None,
            LightType type = LightType.Directional)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.intensity = intensity;
            light.color = color ?? Color.white;
            light.shadows = shadows;
            return light;
        }

        [Fact]
        public void NoDirectionalLightIsNoSun()
        {
            Make("point", 3f, type: LightType.Point);
            Assert.Null(RenderSettings.sun);
            Assert.Null(LightingGlobals.MainLightOf());
        }

        [Fact]
        public void ShadowsWeighSixteenTimes()
        {
            Make("plain", 1f);
            var shadowed = Make("shadowed", 0.1f, shadows: LightShadows.Soft);
            Assert.Same(shadowed, RenderSettings.sun);
            Assert.Same(shadowed, LightingGlobals.MainLightOf());
            shadowed.intensity = 0.05f;
            Assert.Equal("plain", RenderSettings.sun.name);
            Assert.Equal("plain", LightingGlobals.MainLightOf().name);
            shadowed.intensity = 0.1f;
            shadowed.shadows = LightShadows.Hard;
            Assert.Same(shadowed, RenderSettings.sun);
        }

        /// <summary>0.3 r + 0.59 g + 0.11 b: red at 1 beats blue at 2.7 (Color.grayscale's
        /// 0.299/0.114 would not), green at 1 beats red at 1.9.</summary>
        [Fact]
        public void ChannelsWeighAsTheEditorsLuminance()
        {
            var red = Make("red", 1f, Color.red);
            var blue = Make("blue", 2.7f, Color.blue);
            Assert.Same(red, RenderSettings.sun);
            Object.DestroyImmediate(blue.gameObject);
            red.intensity = 1.9f;
            var green = Make("green", 1f, Color.green);
            Assert.Same(green, RenderSettings.sun);
            // The colour as set, beyond 1 too; not the colour temperature's.
            var bright = Make("bright", 1f, new Color(2f, 2f, 2f));
            bright.useColorTemperature = true;
            bright.colorTemperature = 1500f;
            Assert.Same(bright, RenderSettings.sun);
        }

        [Fact]
        public void NotImportantLightsAreNeverTheSun()
        {
            var vertex = Make("vertex", 2f);
            vertex.renderMode = LightRenderMode.ForceVertex;
            var pixel = Make("pixel", 0.5f);
            pixel.renderMode = LightRenderMode.ForcePixel;
            var auto = Make("auto", 1f);
            Assert.Same(auto, RenderSettings.sun);
            Assert.Same(auto, LightingGlobals.MainLightOf());
        }

        /// <summary>The getter takes what the main light leaves out: zero intensity, an empty
        /// culling mask, a cookie; disabled and inactive lights neither.</summary>
        [Fact]
        public void TheSunNeedNotLightAnything()
        {
            var zero = Make("zero", 0f);
            Assert.Same(zero, RenderSettings.sun);
            Assert.Null(LightingGlobals.MainLightOf());
            var masked = Make("masked", 2f);
            masked.cullingMask = 0;
            var open = Make("open", 1f);
            Assert.Same(masked, RenderSettings.sun);
            Assert.Same(open, LightingGlobals.MainLightOf());
            masked.cullingMask = -1;
            masked.cookie = Texture2D.whiteTexture;
            Assert.Same(masked, RenderSettings.sun);
            Assert.Same(open, LightingGlobals.MainLightOf());
            var disabled = Make("disabled", 5f);
            disabled.enabled = false;
            var inactive = Make("inactive", 5f);
            inactive.gameObject.SetActive(false);
            Assert.Same(masked, RenderSettings.sun);
        }

        [Fact]
        public void TheFirstEnabledOfEqualsWins()
        {
            var first = Make("first", 1f);
            var second = Make("second", 1f);
            Assert.Same(first, RenderSettings.sun);
            first.enabled = false;
            first.enabled = true;
            Assert.Same(second, RenderSettings.sun);
            second.gameObject.SetActive(false);
            second.gameObject.SetActive(true);
            Assert.Same(first, RenderSettings.sun);
        }

        /// <summary>A named light is the sun whatever its state; the main light ignores the name.</summary>
        [Fact]
        public void ANamedLightIsTheSunWhateverItsState()
        {
            var named = Make("named", 0.2f);
            var brighter = Make("brighter", 1f);
            RenderSettings.sun = named;
            Assert.Same(named, RenderSettings.sun);
            Assert.Same(brighter, LightingGlobals.MainLightOf());
            named.enabled = false;
            Assert.Same(named, RenderSettings.sun);
            named.enabled = true;
            named.gameObject.SetActive(false);
            Assert.Same(named, RenderSettings.sun);
            var point = Make("point", 1f, type: LightType.Point);
            RenderSettings.sun = point;
            Assert.Same(point, RenderSettings.sun);
            Assert.Same(point, LightingGlobals.SkySun());
            // A destroyed one leaves the brightest; assigning null names none.
            Object.DestroyImmediate(point.gameObject);
            Assert.Same(brighter, RenderSettings.sun);
            RenderSettings.sun = named;
            RenderSettings.sun = null;
            Assert.Same(brighter, RenderSettings.sun);
            Assert.Null(RenderSettings.NamedSun);
        }

        [Fact]
        public void TheGetterFollowsChangesAtOnce()
        {
            var a = Make("a", 1f);
            var b = Make("b", 0.5f);
            Assert.Same(a, RenderSettings.sun);
            b.intensity = 2f;
            Assert.Same(b, RenderSettings.sun);
            b.intensity = 0.9f;
            Assert.Same(a, RenderSettings.sun);
            b.shadows = LightShadows.Soft;
            Assert.Same(b, RenderSettings.sun);
            b.type = LightType.Point;
            Assert.Same(a, RenderSettings.sun);
        }

        [Fact]
        public void TheRankIsTheEditorsSum()
        {
            var light = Make("rank", 1.5f, new Color(0.2f, 0.7f, 0.4f), LightShadows.Soft);
            float expected = (0.2f * 0.3f + 0.7f * 0.59f + 0.4f * 0.11f) * 1.5f * 16f;
            Assert.Equal(expected, LightImportance.SunRank(light));
        }
    }
}
