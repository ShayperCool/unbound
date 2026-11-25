using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>GetAnimatableBindings of engine components from the measured table
    /// (tests/projects/animbindings), and other families' earlier listing.</summary>
    public class AnimatableBindingsTests
    {
        static string[] Names(GameObject go, System.Type type) =>
            AnimationUtility.GetAnimatableBindings(go, go).Where(b => b.type == type).Select(b => b.propertyName).ToArray();

        [Fact]
        public void ALightListsItsMeasuredBindingsAndNotItsType()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("light");
            try
            {
                go.AddComponent<Light>();
                var names = Names(go, typeof(Light));
                // tests/projects/animbindings UnityEngine.Light
                Assert.Equal(new[] { "m_Color.r", "m_Color.g", "m_Color.b", "m_Color.a", "m_CookieSize", "m_DrawHalo", "m_Intensity", "m_BounceIntensity", "m_Range", "m_Shadows.m_Strength", "m_Shadows.m_Bias", "m_Shadows.m_NormalBias", "m_Shadows.m_NearPlane", "m_SpotAngle", "m_InnerSpotAngle", "m_ColorTemperature", "m_Enabled" }, names);
                Assert.DoesNotContain("m_Type", names);
            }
            finally { Object.DestroyImmediate(go); }
        }

        sealed class Capture : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            public void LogException(System.Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        [Fact]
        public void OtherMaterialCountsListTheFreshSlotAndSaySo()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            var go = new GameObject("renderer");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new Material[3];
                var slots = AnimationUtility.GetAnimatableBindings(go, go)
                    .Where(b => b.type == typeof(MeshRenderer) && b.isPPtrCurve).Select(b => b.propertyName).ToArray();
                // tests/projects/animbindings UnityEngine.MeshRenderer (a fresh renderer)
                Assert.Equal(new[] { "m_Materials.Array.data[0]" }, slots);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(go);
            }
            Assert.Contains(capture.Lines, line => line.Contains("material slots of a fresh renderer only"));
        }

        [Fact]
        public void OtherFamiliesKeepTheSerializedListing()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var go = new GameObject("light");
            try
            {
                go.AddComponent<Light>();
                Assert.NotEqual(AnimationUtility.s_EngineBindings["UnityEngine.Light"].Floats, Names(go, typeof(Light)));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
