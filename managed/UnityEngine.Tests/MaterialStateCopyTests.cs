using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Material state copying and the render queue reset on a shader change, from the Unity 6000.0
    /// Scripting API: CopyPropertiesFromMaterial ("property values … shader keywords, render queue
    /// and global illumination flags … Material's shader is not changed"), Material.renderQueue
    /// ("if a shader on the material is changed, the render queue resets to that of the shader
    /// itself"; its batch-mode SRP limitation is about which SubShader gives the shader's queue,
    /// not tested here) and CopyMatchingPropertiesFromMaterial (keyword states of keywords both
    /// shaders declare; doubleSidedGI, enableInstancing, renderQueue, globalIlluminationFlags).
    /// Tests named "Unmeasured" guard a choice the documentation does not make; they are not a
    /// claim about Unity. Expected queues are read from the shaders, not written as numbers.
    /// </summary>
    public class MaterialStateCopyTests : IDisposable
    {
        readonly Dictionary<string, string> m_Spaces = new Dictionary<string, string>();
        readonly List<Object> m_Owned = new List<Object>();
        static int s_Made;

        const string ResetFromNoShader = "Material.shader set to or from no shader";
        const string ResetOnInheritingVariant = "Material.shader of a variant that takes its parent's render queue";
        const string CopyIntoVariant = "Material.CopyPropertiesFromMaterial into a material variant";
        const string MatchingIntoVariant = "Material.CopyMatchingPropertiesFromMaterial into a material variant";
        const string MatchingKeywordsUnknown = "Material.CopyMatchingPropertiesFromMaterial keyword states";

        public MaterialStateCopyTests()
        {
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = Call;
        }

        public void Dispose()
        {
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.s_FrameForTests = null;
            ShaderDeclarations.ResetForTests();
            for (int i = m_Owned.Count - 1; i >= 0; i--)
                if (m_Owned[i]) Object.DestroyImmediate(m_Owned[i], true);
        }

        const string NotFound = "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";

        string Call(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            return method == "shader-declarations" && m_Spaces.TryGetValue(document.RootElement.GetProperty("name").GetString(), out var s) ? s : NotFound;
        }

        static string Space(IEnumerable<string> keywords) =>
            "{\"found\":true,\"builtin\":false,\"subshaders_known\":true,\"keywords_known\":true,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":["
            + string.Join(",", keywords.Select(k => "{\"name\":\"" + k + "\",\"local\":true,\"dynamic\":false}")) + "]}";

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        /// <summary>A shader with every property kind, the SubShader <paramref name="queue"/> tag and,
        /// when given, a known keyword space (null: unknown to the host).</summary>
        Shader Make(string queue, params string[] keywords) => Make(queue, keywords, true);

        Shader Make(string queue, string[] keywords, bool known)
        {
            var shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/StateCopy/S" + (++s_Made) + "\" {\n"
                + "Properties { _Color (\"Color\", Color) = (1, 0, 0, 1) _Value (\"Value\", Float) = 0.5 _Other (\"Other\", Range(0, 2)) = 1"
                + " _Count (\"Count\", Integer) = 3 _Offset (\"Offset\", Vector) = (0, 0, 0, 0) _MainTex (\"Texture\", 2D) = \"white\" {} }\n"
                + "SubShader { Tags { \"Queue\"=\"" + queue + "\" } Pass { } } }"));
            if (known) m_Spaces[shader.name] = Space(keywords);
            return shader;
        }

        static int Hits(string member) =>
            Stubs.HitReport().Where(h => h.Key == member).Select(h => h.Value).FirstOrDefault();

        sealed class ByGuid : IReferenceResolver
        {
            readonly Dictionary<string, Object> m_Objects;
            internal ByGuid(Dictionary<string, Object> objects) => m_Objects = objects;
            public Object Resolve(ObjectRef reference, Type expected) =>
                reference.Guid != null && m_Objects.TryGetValue(reference.Guid, out var o) ? o : null;
        }

        Material Read(string yaml, Dictionary<string, Object> objects)
        {
            var m = Own(new Material());
            ((ISerializedAsset)m).ReadSerialized(TestYaml.DocumentBody(yaml), new ByGuid(objects));
            return m;
        }

        const string ShaderGuid = "ab000000000000000000000000000001";
        const string ParentGuid = "ab000000000000000000000000000002";

        /// <summary>A saved parent and a saved variant of it: with <paramref name="modified"/> 0 and
        /// no queue of its own the variant takes the parent's queue and flags.</summary>
        (Material Parent, Material Variant) Family(Shader shader, int modified = 0, int variantQueue = -1)
        {
            var objects = new Dictionary<string, Object> { [ShaderGuid] = shader };
            var parent = Read(@"Material:
  m_Name: StateParent
  m_Shader: {fileID: 4800000, guid: " + ShaderGuid + @", type: 3}
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  m_SavedProperties:
    m_Floats:
    - _Value: 0.25
    - _Other: 1.5
", objects);
            objects[ParentGuid] = parent;
            var variant = Read(@"Material:
  m_Name: StateVariant
  m_Shader: {fileID: 4800000, guid: " + ShaderGuid + @", type: 3}
  m_Parent: {fileID: 2100000, guid: " + ParentGuid + @", type: 2}
  m_ModifiedSerializedProperties: " + modified + @"
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_CustomRenderQueue: " + variantQueue + @"
  m_SavedProperties:
    m_Floats:
    - _Value: 0.75
", objects);
            return (parent, variant);
        }

        Material Filled(Shader shader, MaterialGlobalIlluminationFlags flags, Texture texture)
        {
            var m = Own(new Material(shader));
            m.SetColor("_Color", new Color(0f, 1f, 0f, 1f));
            m.SetFloat("_Value", 0.125f);
            m.SetInteger("_Count", 7);
            m.SetVector("_Offset", new Vector4(1, 2, 3, 4));
            m.SetMatrix("_Basis", Matrix4x4.Scale(new Vector3(2, 3, 4)));
            m.SetTexture("_MainTex", texture);
            m.SetTextureScale("_MainTex", new Vector2(2f, 3f));
            m.SetTextureOffset("_MainTex", new Vector2(0.5f, 0.25f));
            m.EnableKeyword("SHARED_KW");
            m.EnableKeyword("SOURCE_ONLY");
            m.renderQueue = 2450;
            m.globalIlluminationFlags = flags;
            m.doubleSidedGI = true;
            return m;
        }

        // ------------------------------------------------------------------ CopyPropertiesFromMaterial

        /// <summary>Every documented part is copied, for each GI flag combination; the destination's
        /// shader is not changed. Old source: only EmissiveIsBlack (a new material's flags) passed.</summary>
        [Theory]
        [InlineData(MaterialGlobalIlluminationFlags.None)]
        [InlineData(MaterialGlobalIlluminationFlags.RealtimeEmissive)]
        [InlineData(MaterialGlobalIlluminationFlags.BakedEmissive)]
        [InlineData(MaterialGlobalIlluminationFlags.AnyEmissive)]
        [InlineData(MaterialGlobalIlluminationFlags.EmissiveIsBlack)]
        [InlineData(MaterialGlobalIlluminationFlags.RealtimeEmissive | MaterialGlobalIlluminationFlags.EmissiveIsBlack)]
        public void CopyPropertiesCopiesValuesKeywordsQueueAndGIFlagsAndKeepsTheShader(MaterialGlobalIlluminationFlags flags)
        {
            var geometry = Make("Geometry", "SHARED_KW", "SOURCE_ONLY");
            var transparent = Make("Transparent", "SHARED_KW");
            var texture = Own(new Texture2D(2, 2));
            var source = Filled(geometry, flags, texture);
            var destination = Own(new Material(transparent));
            destination.EnableKeyword("DESTINATION_ONLY");
            destination.SetFloat("_Other", 0.75f);

            destination.CopyPropertiesFromMaterial(source);

            Assert.Same(transparent, destination.shader);
            Assert.Equal(source.rawRenderQueue, destination.rawRenderQueue);
            Assert.Equal(flags, destination.globalIlluminationFlags);
            Assert.Equal(new Color(0f, 1f, 0f, 1f), destination.GetColor("_Color"));
            Assert.Equal(0.125f, destination.GetFloat("_Value"));
            Assert.Equal(7, destination.GetInteger("_Count"));
            Assert.Equal(new Vector4(1, 2, 3, 4), destination.GetVector("_Offset"));
            Assert.Equal(Matrix4x4.Scale(new Vector3(2, 3, 4)), destination.GetMatrix("_Basis"));
            Assert.Same(texture, destination.GetTexture("_MainTex"));
            Assert.Equal(new Vector2(2f, 3f), destination.GetTextureScale("_MainTex"));
            Assert.Equal(new Vector2(0.5f, 0.25f), destination.GetTextureOffset("_MainTex"));
            Assert.Equal(new HashSet<string> { "SHARED_KW", "SOURCE_ONLY" }, destination.m_Keywords);
            // The source's value, not the destination's own one: the destination's values went.
            Assert.Equal(transparent.PropertyById(Shader.PropertyToID("_Other")).DefaultValue.x, destination.GetFloat("_Other"));
        }

        /// <summary>Unmeasured: doubleSidedGI is not named by CopyPropertiesFromMaterial and stays.
        /// The source's override is copied as stored: no override stays no override.</summary>
        [Fact]
        public void UnmeasuredCopyPropertiesKeepsDoubleSidedGIAndCopiesTheStoredOverride()
        {
            var geometry = Make("Geometry");
            var transparent = Make("Transparent");
            var source = Own(new Material(geometry) { doubleSidedGI = true });
            var destination = Own(new Material(transparent) { renderQueue = 2600 });
            destination.CopyPropertiesFromMaterial(source);
            Assert.False(destination.doubleSidedGI);
            Assert.Equal(-1, destination.rawRenderQueue);
            Assert.Equal(transparent.renderQueue, destination.renderQueue);
        }

        /// <summary>Source and destination are independent after the copy (values, texture slots,
        /// arrays, keywords, queue and flags). Old source: the flags assertion failed.</summary>
        [Fact]
        public void TheCopyIsIndependentOfItsSource()
        {
            var shader = Make("Geometry", "SHARED_KW", "SOURCE_ONLY");
            var texture = Own(new Texture2D(2, 2));
            var source = Filled(shader, MaterialGlobalIlluminationFlags.BakedEmissive, texture);
            source.SetFloatArray("_Weights", new[] { 1f, 2f });
            var destination = Own(new Material(Make("Transparent")));
            destination.CopyPropertiesFromMaterial(source);

            source.SetFloat("_Value", 0.9f);
            source.SetTextureScale("_MainTex", new Vector2(9f, 9f));
            source.SetFloatArray("_Weights", new[] { 5f, 6f });
            source.DisableKeyword("SHARED_KW");
            source.renderQueue = 2900;
            source.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            Assert.Equal(0.125f, destination.GetFloat("_Value"));
            Assert.Equal(new Vector2(2f, 3f), destination.GetTextureScale("_MainTex"));
            Assert.Equal(new[] { 1f, 2f }, destination.GetFloatArray("_Weights"));
            Assert.Contains("SHARED_KW", destination.m_Keywords);
            Assert.Equal(2450, destination.rawRenderQueue);
            Assert.Equal(MaterialGlobalIlluminationFlags.BakedEmissive, destination.globalIlluminationFlags);

            destination.SetFloat("_Value", 0.3f);
            destination.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Assert.Equal(0.9f, source.GetFloat("_Value"));
            Assert.Equal(MaterialGlobalIlluminationFlags.RealtimeEmissive, source.globalIlluminationFlags);
        }

        /// <summary>A material copied onto itself keeps everything: the documented result depends
        /// on the source only. Old source: cleared before it read, so the values, keywords and
        /// texture went.</summary>
        [Fact]
        public void CopyingAMaterialOntoItselfKeepsIt()
        {
            var shader = Make("Geometry", "SHARED_KW", "SOURCE_ONLY");
            var texture = Own(new Texture2D(2, 2));
            var m = Filled(shader, MaterialGlobalIlluminationFlags.BakedEmissive, texture);
            int crc = m.ComputeCRC();
            m.CopyPropertiesFromMaterial(m);
            Assert.Equal(crc, m.ComputeCRC());
            Assert.Equal(0.125f, m.GetFloat("_Value"));
            Assert.Same(texture, m.GetTexture("_MainTex"));
            Assert.Equal(new HashSet<string> { "SHARED_KW", "SOURCE_ONLY" }, m.m_Keywords);
            Assert.Equal(2450, m.rawRenderQueue);
            Assert.Equal(MaterialGlobalIlluminationFlags.BakedEmissive, m.globalIlluminationFlags);
            Assert.True(m.doubleSidedGI);

            m.CopyMatchingPropertiesFromMaterial(m);
            Assert.Equal(crc, m.ComputeCRC());
            Assert.Equal(MaterialGlobalIlluminationFlags.BakedEmissive, m.globalIlluminationFlags);
        }

        [Fact]
        public void CopyingFromNoMaterialThrows()
        {
            var m = Own(new Material(Make("Geometry")));
            Assert.Throws<ArgumentNullException>(() => m.CopyPropertiesFromMaterial(null));
            Assert.Throws<ArgumentNullException>(() => m.CopyMatchingPropertiesFromMaterial(null));
        }

        /// <summary>A variant source is copied as its getters answer, after a later change of its
        /// parent too (its value, the parent's queue and flags it takes). Old source: it read what
        /// the variant last took.</summary>
        [Fact]
        public void AVariantSourceIsCopiedAsItAnswers()
        {
            var shader = Make("Geometry");
            var (parent, variant) = Family(shader);
            Assert.Equal(1.5f, variant.GetFloat("_Other"));
            parent.SetFloat("_Other", 0.5f);
            parent.renderQueue = 2600;
            parent.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            var destination = Own(new Material(Make("Transparent")));
            destination.CopyPropertiesFromMaterial(variant);
            Assert.Equal(0.5f, destination.GetFloat("_Other"));
            Assert.Equal(0.75f, destination.GetFloat("_Value"));
            Assert.Equal(2600, destination.rawRenderQueue);
            Assert.Equal(MaterialGlobalIlluminationFlags.RealtimeEmissive, destination.globalIlluminationFlags);
        }

        /// <summary>Unmeasured: into a variant the copy holds until its parent changes again, and
        /// each call warns that its values are not overrides. Old source: a variant whose parent
        /// had changed took the parent's values over the copy at its next read, without warning.</summary>
        [Fact]
        public void UnmeasuredACopyIntoAVariantHoldsAndWarns()
        {
            var shader = Make("Geometry");
            var (parent, variant) = Family(shader);
            Assert.Equal(1.5f, variant.GetFloat("_Other"));
            parent.SetFloat("_Other", 0.5f);
            var source = Own(new Material(shader));
            source.SetFloat("_Other", 1.25f);
            int before = Hits(CopyIntoVariant);
            variant.CopyPropertiesFromMaterial(source);
            Assert.Equal(before + 1, Hits(CopyIntoVariant));
            Assert.Equal(1.25f, variant.GetFloat("_Other"));
            Assert.Same(parent, variant.parent);
        }

        // ------------------------------------------------------------------ shader change

        /// <summary>Material.renderQueue: a shader change resets the queue to the new shader's.
        /// The defaults the old shader leaves, the values and the keywords stay. Old source:
        /// rawRenderQueue kept 2450.</summary>
        [Fact]
        public void AShaderChangeResetsTheQueueToTheNewShaders()
        {
            var geometry = Make("Geometry", "SHARED_KW");
            var transparent = Make("Transparent", "SHARED_KW");
            var m = Own(new Material(geometry));
            m.renderQueue = 2450;
            m.SetFloat("_Value", 0.125f);
            m.EnableKeyword("SHARED_KW");
            int version = m.m_Version;

            m.shader = transparent;

            Assert.Equal(-1, m.rawRenderQueue);
            Assert.Equal(transparent.renderQueue, m.renderQueue);
            Assert.True(m.m_Version > version);
            Assert.Equal(0.125f, m.GetFloat("_Value"));
            Assert.Equal(geometry.PropertyById(Shader.PropertyToID("_Other")).DefaultValue.x, m.m_Floats[Shader.PropertyToID("_Other")]);
            Assert.Contains("SHARED_KW", m.m_Keywords);
            // A queue set after the change stays.
            m.renderQueue = 3100;
            Assert.Equal(3100, m.rawRenderQueue);
        }

        /// <summary>No override: nothing to reset, no warning.</summary>
        [Fact]
        public void AShaderChangeWithoutAnOverrideFollowsTheShader()
        {
            var geometry = Make("Geometry");
            var transparent = Make("Transparent");
            var m = Own(new Material(geometry));
            int warned = Hits(ResetFromNoShader) + Hits(ResetOnInheritingVariant);
            m.shader = transparent;
            Assert.Equal(-1, m.rawRenderQueue);
            Assert.Equal(transparent.renderQueue, m.renderQueue);
            Assert.Equal(warned, Hits(ResetFromNoShader) + Hits(ResetOnInheritingVariant));
        }

        /// <summary>Unmeasured: assigning the shader it has is no change ("is changed").</summary>
        [Fact]
        public void UnmeasuredTheSameShaderKeepsTheQueue()
        {
            var geometry = Make("Geometry");
            var m = Own(new Material(geometry) { renderQueue = 2450 });
            m.shader = geometry;
            Assert.Equal(2450, m.rawRenderQueue);
        }

        /// <summary>Unmeasured: to and from no shader the override stays, with a warning. Old
        /// source: kept, silently.</summary>
        [Fact]
        public void UnmeasuredNoShaderKeepsTheQueueAndWarns()
        {
            var geometry = Make("Geometry");
            var transparent = Make("Transparent");
            var m = Own(new Material(geometry) { renderQueue = 2450 });
            int before = Hits(ResetFromNoShader);
            m.shader = null;
            Assert.Equal(2450, m.rawRenderQueue);
            m.shader = transparent;
            Assert.Equal(2450, m.rawRenderQueue);
            Assert.Equal(before + 2, Hits(ResetFromNoShader));
        }

        /// <summary>Unmeasured: a variant taking its parent's queue keeps it, with a warning; a
        /// variant with a queue of its own resets like any material. Old source: both kept their
        /// queue, silently.</summary>
        [Fact]
        public void UnmeasuredVariantsResetOnlyAQueueOfTheirOwn()
        {
            var geometry = Make("Geometry");
            var transparent = Make("Transparent");
            var (parent, inheriting) = Family(geometry);
            parent.renderQueue = 2600;
            int before = Hits(ResetOnInheritingVariant);
            inheriting.shader = transparent;
            Assert.Equal(2600, inheriting.rawRenderQueue);
            Assert.Equal(before + 1, Hits(ResetOnInheritingVariant));

            var (_, own) = Family(geometry, modified: 16, variantQueue: 2100);
            Assert.Equal(2100, own.rawRenderQueue);
            own.shader = transparent;
            Assert.Equal(-1, own.rawRenderQueue);
            Assert.Equal(transparent.renderQueue, own.renderQueue);
        }

        // ------------------------------------------------------------------ copies and invalidation

        /// <summary>A copy (and an instance) has the source's GI flags and double-sided GI. Old
        /// source: both were a new material's.</summary>
        [Fact]
        public void CopiesKeepGIFlagsAndDoubleSidedGI()
        {
            var source = Own(new Material(Make("Geometry")) { globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive, doubleSidedGI = true, renderQueue = 2450 });
            foreach (var copy in new[] { Own(new Material(source)), Own(Object.Instantiate(source)) })
            {
                Assert.Equal(MaterialGlobalIlluminationFlags.BakedEmissive, copy.globalIlluminationFlags);
                Assert.True(copy.doubleSidedGI);
                Assert.Equal(2450, copy.rawRenderQueue);
            }
        }

        /// <summary>Reverting to the saved settings (Resources.UnloadAsset) takes the saved flags.
        /// Old source: the flags set at run time stayed.</summary>
        [Fact]
        public void RevertingTakesTheSavedFlags()
        {
            var shader = Make("Geometry");
            var saved = Own(new Material(shader) { globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive, doubleSidedGI = true });
            var m = Own(new Material(saved));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.doubleSidedGI = false;
            m.RevertTo(new Material(saved));
            Assert.Equal(MaterialGlobalIlluminationFlags.RealtimeEmissive, m.globalIlluminationFlags);
            Assert.True(m.doubleSidedGI);
        }

        /// <summary>A flag change is a change of the material: a variant taking its parent's flags
        /// sees it, and copies keyed by the version are made again; setting the same value is no
        /// change. Old source: the version stayed and the variant kept the old flags.</summary>
        [Fact]
        public void FlagChangesReachVariantsAndCopies()
        {
            var (parent, variant) = Family(Make("Geometry"));
            Assert.Equal(MaterialGlobalIlluminationFlags.EmissiveIsBlack, variant.globalIlluminationFlags);
            int version = parent.m_Version;
            parent.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            Assert.True(parent.m_Version > version);
            Assert.Equal(MaterialGlobalIlluminationFlags.BakedEmissive, variant.globalIlluminationFlags);
            parent.doubleSidedGI = true;
            Assert.True(variant.doubleSidedGI);
            parent.enableInstancing = true;
            Assert.True(variant.enableInstancing);
            version = parent.m_Version;
            parent.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            parent.doubleSidedGI = true;
            parent.enableInstancing = true;
            Assert.Equal(version, parent.m_Version);
        }

        // ------------------------------------------------------------------ CopyMatchingPropertiesFromMaterial

        /// <summary>The documented settings are copied whatever the shaders; the shader stays.
        /// Old source: none was.</summary>
        [Fact]
        public void CopyMatchingCopiesTheSettings()
        {
            var source = Own(new Material(Make("Geometry")) { renderQueue = 2450, globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive, doubleSidedGI = true, enableInstancing = true });
            var transparent = Make("Transparent");
            var destination = Own(new Material(transparent));
            destination.CopyMatchingPropertiesFromMaterial(source);
            Assert.Same(transparent, destination.shader);
            Assert.Equal(2450, destination.rawRenderQueue);
            Assert.Equal(MaterialGlobalIlluminationFlags.BakedEmissive, destination.globalIlluminationFlags);
            Assert.True(destination.doubleSidedGI);
            Assert.True(destination.enableInstancing);
        }

        /// <summary>The state of a keyword both spaces declare is copied, on and off; a keyword
        /// only one declares, or neither, stays as it is. Old source: no keyword state was copied.</summary>
        [Fact]
        public void CopyMatchingCopiesTheStatesOfKeywordsBothShadersDeclare()
        {
            var a = Make("Geometry", "SHARED_ON", "SHARED_OFF", "ONLY_A");
            var b = Make("Geometry", "SHARED_ON", "SHARED_OFF", "ONLY_B");
            var source = Own(new Material(a));
            source.EnableKeyword("SHARED_ON");
            source.EnableKeyword("ONLY_A");
            source.EnableKeyword("UNDECLARED");
            var destination = Own(new Material(b));
            destination.EnableKeyword("SHARED_OFF");
            destination.EnableKeyword("ONLY_B");
            destination.CopyMatchingPropertiesFromMaterial(source);
            Assert.Equal(new HashSet<string> { "SHARED_ON", "ONLY_B" }, destination.m_Keywords);
            Assert.Equal(new HashSet<string> { "SHARED_ON", "ONLY_A", "UNDECLARED" }, source.m_Keywords);
        }

        /// <summary>While a keyword space is unknown no keyword state is copied, with a warning;
        /// the settings still are. Old source: no warning.</summary>
        [Fact]
        public void CopyMatchingWithAnUnknownSpaceKeepsKeywordsAndWarns()
        {
            var known = Make("Geometry", "SHARED_ON");
            var unknown = Make("Geometry", new[] { "SHARED_ON" }, known: false);
            var source = Own(new Material(unknown) { renderQueue = 2450 });
            source.EnableKeyword("SHARED_ON");
            var destination = Own(new Material(known));
            int before = Hits(MatchingKeywordsUnknown);
            destination.CopyMatchingPropertiesFromMaterial(source);
            Assert.Empty(destination.m_Keywords);
            Assert.Equal(2450, destination.rawRenderQueue);
            Assert.Equal(before + 1, Hits(MatchingKeywordsUnknown));
        }

        /// <summary>Unmeasured: into a variant, as CopyPropertiesFromMaterial, it warns.</summary>
        [Fact]
        public void UnmeasuredCopyMatchingIntoAVariantWarns()
        {
            var shader = Make("Geometry");
            var (_, variant) = Family(shader);
            var source = Own(new Material(shader));
            int before = Hits(MatchingIntoVariant);
            variant.CopyMatchingPropertiesFromMaterial(source);
            Assert.Equal(before + 1, Hits(MatchingIntoVariant));
        }
    }
}
