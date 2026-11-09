using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>How the forward paths rank an object's lights and which lights a scriptable
    /// pipeline's culling makes visible (tests/projects/urp light-order.jsonl).</summary>
    public sealed class LightImportanceTests : IDisposable
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly Camera m_Camera;

        public LightImportanceTests()
        {
            var cameraObject = Track(new GameObject("light importance camera"));
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            m_Camera = cameraObject.AddComponent<Camera>();
        }

        public void Dispose()
        {
            Culling.EndFrame();
            foreach (var go in m_Objects) Object.DestroyImmediate(go);
        }

        GameObject Track(GameObject go)
        {
            m_Objects.Add(go);
            return go;
        }

        Light Add(string name, LightType type, Vector3 position, float intensity, float range = 10f)
        {
            var go = Track(new GameObject(name));
            go.transform.position = position;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.intensity = intensity;
            l.range = range;
            l.color = Color.white;
            return l;
        }

        static float Of(Light l, Vector3 center) => LightImportance.Of(l, l.type, l.transform.position, l.range, center);

        [Fact]
        public void BrightnessIsTheLuminanceOfTheColourAsSetTimesTheIntensity()
        {
            var red = Add("red", LightType.Point, Vector3.zero, 2f);
            red.color = Color.red;
            var green = Add("green", LightType.Point, Vector3.zero, 2f);
            green.color = Color.green;
            Assert.Equal(0.6f, LightImportance.Brightness(red), 5);
            Assert.Equal(1.18f, LightImportance.Brightness(green), 5);
            // Not the sum of the channels: green outranks red at the same place.
            Assert.True(Of(green, Vector3.zero) > Of(red, Vector3.zero));
        }

        [Fact]
        public void LocalLightsFallOffWithTheDistanceToTheCentreOverTheRange()
        {
            var l = Add("p", LightType.Point, new Vector3(2f, 0f, 0f), 1f, 8f);
            // 1 / (1 + 25 * 4 / 64)
            Assert.Equal(1f / (1f + 25f * 4f / 64f), Of(l, Vector3.zero), 5);
        }

        [Fact]
        public void RenderModesBiasAndShadowsWeighDirectionalLights()
        {
            var important = Add("important", LightType.Point, Vector3.one, 0.1f);
            important.renderMode = LightRenderMode.ForcePixel;
            var notImportant = Add("not important", LightType.Point, Vector3.one, 5f);
            notImportant.renderMode = LightRenderMode.ForceVertex;
            var auto = Add("auto", LightType.Point, Vector3.one, 1f);
            Assert.True(Of(important, Vector3.one) > Of(auto, Vector3.one));
            Assert.True(Of(auto, Vector3.one) > Of(notImportant, Vector3.one));
            var directional = Add("directional", LightType.Directional, Vector3.zero, 0.1f);
            Assert.Equal(0.1f, Of(directional, Vector3.zero), 5);
            directional.shadows = LightShadows.Soft;
            Assert.Equal(1.6f, Of(directional, Vector3.zero), 5);
        }

        [Fact]
        public void EqualImportancesKeepTheirOrder()
        {
            var list = new List<LightImportance.Candidate>();
            for (int i = 0; i < 40; i++) list.Add(new LightImportance.Candidate(i, i % 3 == 0 ? 2f : 1f, i));
            LightImportance.Sort(list);
            for (int i = 1; i < list.Count; i++)
                Assert.True(list[i - 1].Importance > list[i].Importance || list[i - 1].Index < list[i].Index);
        }

        [Fact]
        public void SpotLightsReachWhatTheirConeTouches()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 0.2f));
            var spot = Add("spot", LightType.Spot, new Vector3(2f, 0f, 0f), 1f, 5f);
            spot.spotAngle = 40f;
            spot.transform.rotation = Quaternion.LookRotation(Vector3.left);
            bool Reaches() => LightImportance.Reaches(LightType.Spot, spot.transform.position, spot.range, bounds)
                && LightImportance.SpotReaches(spot.transform.localToWorldMatrix, spot.range, spot.spotAngle, bounds);
            Assert.True(Reaches());
            // Pointing away, or to the side: the range still covers the bounds.
            spot.transform.rotation = Quaternion.LookRotation(Vector3.right);
            Assert.False(Reaches());
            spot.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            Assert.False(Reaches());
            // Out of range.
            spot.transform.rotation = Quaternion.LookRotation(Vector3.left);
            spot.range = 1.5f;
            Assert.False(Reaches());
            var point = Add("point", LightType.Point, new Vector3(0f, 1.2f, 0f), 1f, 1.15f);
            Assert.True(LightImportance.Reaches(LightType.Point, point.transform.position, point.range, bounds));
            point.range = 1f;
            Assert.False(LightImportance.Reaches(LightType.Point, point.transform.position, point.range, bounds));
            // A range of 0 at the bounds' centre: reached, with a finite importance.
            point.range = 0f;
            point.transform.position = Vector3.zero;
            Assert.True(LightImportance.Reaches(LightType.Point, point.transform.position, point.range, bounds));
            Assert.False(float.IsNaN(Of(point, Vector3.zero)));
        }

        /// <summary>The built-in forward path: the Auto light that fills the pixel lights keeps
        /// (its importance - the next one's) / (the previous one's - the next one's) of its pixel
        /// pass and lights the base pass per vertex with the rest (tests/projects/builtinrp
        /// light_rank_directional, the cube).</summary>
        [Fact]
        public void TheBuiltInFillingPixelLightFadesIntoAVertexLight()
        {
            int pixelLights = QualitySettings.pixelLightCount;
            try
            {
                QualitySettings.pixelLightCount = 1;
                var sun = Add("sun", LightType.Directional, Vector3.zero, 0.5f);
                RenderSettings.sun = sun;
                var extra = Add("extra", LightType.Directional, Vector3.zero, 0.3f);
                extra.color = new Color(0.2f, 0.4f, 1f);
                var point = Add("point", LightType.Point, new Vector3(0f, 1f, -0.5f), 1.5f, 4f);
                point.color = new Color(1f, 0.3f, 0.2f);
                ForwardLights.BeginCamera(m_Camera);
                var bounds = new Bounds(new Vector3(0f, 0.5f, 1f), Vector3.one);
                var lights = new ForwardLights.ObjectLights();
                ForwardLights.Classify(bounds, 0, lights, ForwardLights.Main);
                Assert.Same(sun, ForwardLights.Main);
                Assert.Equal(new[] { point }, lights.Pixel);
                Assert.Equal(new[] { point }, lights.Vertex);
                Assert.Equal(new[] { extra }, lights.Spherical);
                float main = Of(sun, bounds.center), own = Of(point, bounds.center), next = Of(extra, bounds.center);
                float fade = (own - next) / (main - next + 0.001f);
                Assert.Equal(fade, lights.PixelFade, 5);
                Assert.Equal(fade, lights.PixelScale(0), 5);
                Assert.Equal(1f - fade, lights.VertexScale(0, 1), 5);
                // An Important light does not fade: its bias puts its neighbours 1000 apart.
                point.renderMode = LightRenderMode.ForcePixel;
                ForwardLights.Classify(bounds, 0, lights, ForwardLights.Main);
                Assert.Equal(1f, lights.PixelFade);
                Assert.Empty(lights.Vertex);
            }
            finally
            {
                QualitySettings.pixelLightCount = pixelLights;
                RenderSettings.sun = null;
            }
        }

        CullingData Cull(int maximumVisibleLights, uint? cullingMask = null)
        {
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            parameters.maximumVisibleLights = maximumVisibleLights;
            if (cullingMask.HasValue) parameters.cullingMask = cullingMask.Value;
            return Culling.Get(Culling.Cull(ref parameters));
        }

        static string Names(CullingData data)
        {
            var names = new List<string>();
            foreach (var v in data.Lights) names.Add(v.light.name);
            return string.Join(",", names);
        }

        [Fact]
        public void VisibleLightsAreDirectionalThenSpotThenPointEachNearestFirst()
        {
            Add("point mid", LightType.Point, new Vector3(0f, 0f, 0f), 1f);
            Add("spot far", LightType.Spot, new Vector3(0f, 0f, 4f), 5f).transform.rotation = Quaternion.LookRotation(Vector3.back);
            Add("point near", LightType.Point, new Vector3(0f, 0f, -3f), 0.5f);
            Add("sun", LightType.Directional, Vector3.zero, 1f);
            Add("spot near", LightType.Spot, new Vector3(0f, 0f, -2f), 1f);
            Assert.Equal("sun,spot near,spot far,point near,point mid", Names(Cull(32)));
        }

        [Fact]
        public void TheMaximumKeepsTheNearestLocalLightsAfterTheDirectionalOnes()
        {
            Add("sun", LightType.Directional, Vector3.zero, 1f);
            Add("far bright", LightType.Point, new Vector3(0f, 0f, 5f), 8f);
            Add("near dim", LightType.Point, new Vector3(0f, 0f, -5f), 0.1f);
            Add("mid", LightType.Point, new Vector3(0f, 0f, 0f), 1f);
            Assert.Equal("sun,near dim,mid", Names(Cull(3)));
            Culling.EndFrame();
            Assert.Equal("sun", Names(Cull(1)));
            Culling.EndFrame();
            Assert.Equal("", Names(Cull(0)));
            Culling.EndFrame();
            // No maximum (-1): every light.
            Assert.Equal(4, Cull(-1).Lights.Length);
        }

        [Fact]
        public void DimLightsAndLightsOutsideTheCullingMaskAreNotVisible()
        {
            Add("zero", LightType.Point, Vector3.zero, 0f);
            Add("dim", LightType.Point, Vector3.zero, 0.0005f);
            Add("faint", LightType.Point, Vector3.zero, 0.002f);
            Add("other layer", LightType.Point, Vector3.zero, 1f).cullingMask = 1 << 6;
            Assert.Equal("faint", Names(Cull(32, 1u << 5)));
            Culling.EndFrame();
            Assert.Equal("faint,other layer", Names(Cull(32, (1u << 5) | (1u << 6))));
        }
    }
}
