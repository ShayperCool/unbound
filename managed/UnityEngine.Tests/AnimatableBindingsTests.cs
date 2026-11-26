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

        [Fact]
        public void EachMaterialSlotGetsItsBinding()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("renderer");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                var shader = ProbeShader("Q758 test/slots");
                renderer.sharedMaterials = new[] { new Material(shader), new Material(shader) };
                var slots = AnimationUtility.GetAnimatableBindings(go, go)
                    .Where(b => b.type == typeof(MeshRenderer) && b.isPPtrCurve).Select(b => b.propertyName).ToArray();
                // tests/projects/animmaterialbindings two/MeshRenderer: one binding per slot.
                Assert.Equal(new[] { "m_Materials.Array.data[0]", "m_Materials.Array.data[1]" }, slots);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static Shader ProbeShader(string name, params (string Name, Rendering.ShaderPropertyType Type, Rendering.ShaderPropertyFlags Flags)[] properties)
        {
            var shader = Shader.Create(name, 2000);
            shader.m_Properties.Clear();
            foreach (var (n, type, flags) in properties)
                shader.m_Properties.Add(new ShaderPropertyInfo { Name = n, Type = type, Flags = flags });
            // As a loaded shader: its names have ids from its load.
            shader.PropertiesChanged();
            return shader;
        }

        static int Hits(string member) => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        static string[] MaterialNames(GameObject go, System.Type type) =>
            Names(go, type).Where(n => n.StartsWith("material.")).Select(n => n.Substring(9)).ToArray();

        // tests/projects/animmaterialbindings: floats, then vectors, each by Unity's name ids; an
        // engine name keeps its fixed id (_Color 177, _MainTex_ST 34), a new name is numbered as
        // its shader declares it, a texture's _ST, _TexelSize and _HDR right after it.
        [Fact]
        public void AMaterialsPropertiesAreFloatsThenVectorsInNameIdOrder()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            // (The ids' order is the release's the ids started under: a suite run under a release
            // whose order is not measured has the other one.)
            if (!Shader.EngineOrderMeasured) return;
            var shader = ProbeShader("Q758 test/order",
                ("_L4TexB", Rendering.ShaderPropertyType.Texture, 0),
                ("_L4FloatB", Rendering.ShaderPropertyType.Float, 0),
                ("_Color", Rendering.ShaderPropertyType.Color, 0),
                ("_L4Int", Rendering.ShaderPropertyType.Int, 0),
                ("_L4Hdr", Rendering.ShaderPropertyType.Color, Rendering.ShaderPropertyFlags.HDR),
                ("_MainTex", Rendering.ShaderPropertyType.Texture, 0),
                ("_L4FloatA", Rendering.ShaderPropertyType.Range, 0));
            var material = new Material(shader);
            var go = new GameObject("renderer");
            try
            {
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                material.SetFloat("_L4Extra", 1f);
                Assert.Equal(new[]
                {
                    "_L4FloatB", "_L4FloatA", "_L4Extra",
                    "_MainTex_ST.x", "_MainTex_ST.y", "_MainTex_ST.z", "_MainTex_ST.w",
                    "_MainTex_TexelSize.x", "_MainTex_TexelSize.y", "_MainTex_TexelSize.z", "_MainTex_TexelSize.w",
                    "_MainTex_HDR.x", "_MainTex_HDR.y", "_MainTex_HDR.z", "_MainTex_HDR.w",
                    "_Color.r", "_Color.g", "_Color.b", "_Color.a",
                    "_L4TexB_ST.x", "_L4TexB_ST.y", "_L4TexB_ST.z", "_L4TexB_ST.w",
                    "_L4TexB_TexelSize.x", "_L4TexB_TexelSize.y", "_L4TexB_TexelSize.z", "_L4TexB_TexelSize.w",
                    "_L4TexB_HDR.x", "_L4TexB_HDR.y", "_L4TexB_HDR.z", "_L4TexB_HDR.w",
                    "_L4Hdr.x", "_L4Hdr.y", "_L4Hdr.z", "_L4Hdr.w",
                }, MaterialNames(go, typeof(MeshRenderer)));
                // A binding per name: a second material of the same shader adds none; the material
                // bindings come before the renderer's own.
                go.GetComponent<MeshRenderer>().sharedMaterials = new[] { material, new Material(shader) };
                var names = Names(go, typeof(MeshRenderer));
                Assert.Equal(35, names.Count(n => n.StartsWith("material.")));
                Assert.Equal("m_ReceiveShadows", names[35]);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
            }
        }

        // Names the Editor's built-in shaders register at startup keep their ids (ids.txt: _ZWrite
        // 413, _Glossiness 414, _MetallicGlossMap 418, _Mode 455, _SpecColor 456); a texture's _ST,
        // _TexelSize and _HDR follow it.
        [Fact]
        public void BuiltInShaderNamesKeepTheirStartupIds()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            // (The ids' order is the release's the ids started under: a suite run under a release
            // whose order is not measured has the other one.)
            if (!Shader.EngineOrderMeasured) return;
            var shader = ProbeShader("Q758 test/startup",
                ("_Mode", Rendering.ShaderPropertyType.Float, 0),
                ("_SpecColor", Rendering.ShaderPropertyType.Color, 0),
                ("_Glossiness", Rendering.ShaderPropertyType.Range, 0),
                ("_MetallicGlossMap", Rendering.ShaderPropertyType.Texture, 0),
                ("_ZWrite", Rendering.ShaderPropertyType.Float, 0));
            var material = new Material(shader);
            var go = new GameObject("renderer");
            try
            {
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                var names = MaterialNames(go, typeof(MeshRenderer));
                Assert.Equal(new[] { "_ZWrite", "_Glossiness", "_Mode", "_MetallicGlossMap_ST.x" }, names.Take(4).ToArray());
                Assert.Equal("_SpecColor.r", names[15]);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
            }
        }

        // A binding names a property: its value is the first material's that has it.
        [Fact]
        public void AMaterialBindingReadsTheMaterialThatHasTheProperty()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var plain = new Material(ProbeShader("Q758 test/plain", ("_L4Plain", Rendering.ShaderPropertyType.Float, 0)));
            var second = new Material(ProbeShader("Q758 test/second slot", ("_L4Later", Rendering.ShaderPropertyType.Float, 0)));
            second.SetFloat("_L4Later", 0.5f);
            var go = new GameObject("renderer");
            try
            {
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { plain, second };
                Assert.True(AnimationUtility.GetFloatValue(go, EditorCurveBinding.FloatCurve("", typeof(MeshRenderer), "material._L4Later"), out var value));
                Assert.Equal(0.5f, value);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(second);
            }
        }

        // The unmeasured cases report: more than two slots, a name two shaders declare, and a
        // texture's _TexelSize read through a binding (counted: a notice logs only its first use).
        [Fact]
        public void TheUnmeasuredMaterialCasesReport()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string slots = "AnimationUtility.GetAnimatableBindings material slots";
            const string shared = "AnimationUtility.GetAnimatableBindings names shaders share";
            const string texel = "Material property binding texel size and HDR channels";
            int slotsBefore = Hits(slots), sharedBefore = Hits(shared), texelBefore = Hits(texel);
            var a = new Material(ProbeShader("Q758 test/shared a", ("_L4Shared", Rendering.ShaderPropertyType.Float, 0)));
            var b = new Material(ProbeShader("Q758 test/shared b", ("_L4Shared", Rendering.ShaderPropertyType.Float, 0)));
            var go = new GameObject("renderer");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { a, a, a };
                AnimationUtility.GetAnimatableBindings(go, go);
                Assert.Equal(slotsBefore + 1, Hits(slots));
                Assert.Equal(sharedBefore, Hits(shared));
                renderer.sharedMaterials = new[] { a, b };
                AnimationUtility.GetAnimatableBindings(go, go);
                Assert.Equal(sharedBefore + 1, Hits(shared));
                Assert.Equal(slotsBefore + 1, Hits(slots));
                AnimationUtility.GetFloatValue(go, EditorCurveBinding.FloatCurve("", typeof(MeshRenderer), "material._MainTex_TexelSize.x"), out _);
                Assert.Equal(texelBefore + 1, Hits(texel));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        // tests/projects/animmaterialbindings swapped/MeshRenderer: after the shader is replaced,
        // the earlier shader's values are not listed.
        [Fact]
        public void AReplacedShadersPropertiesAreNotListed()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var first = ProbeShader("Q758 test/first", ("_L4First", Rendering.ShaderPropertyType.Float, 0));
            var second = ProbeShader("Q758 test/second", ("_L4Second", Rendering.ShaderPropertyType.Float, 0));
            var material = new Material(first);
            var go = new GameObject("renderer");
            try
            {
                material.shader = second;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                Assert.Equal(new[] { "_L4Second" }, MaterialNames(go, typeof(MeshRenderer)));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
            }
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
