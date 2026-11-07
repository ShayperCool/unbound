using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Shader variants by shader: a pass type and keywords each (Scripting API
    /// "ShaderVariantCollection", 6000.0). Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/lightingassets):
    /// <list type="bullet">
    /// <item>A variant passed to Add, Remove or Contains keeps only the keywords its shader declares
    /// (an undeclared one is dropped, as is everything when its shader is null), each once, sorted
    /// by name. A variant read from an asset keeps its keywords as written, so a hand-written list
    /// out of name order matches no query; the Editor writes them sorted.</item>
    /// <item>Add and Remove do not check the variant (a pass type the shader lacks is added);
    /// a null shader is never added, removed or contained.</item>
    /// <item>A shader stays counted after its last variant is removed, until Clear; an asset's
    /// shader that does not load (missing) is counted with its variants.</item>
    /// <item>Warming up counts variants: WarmUp warms all, WarmUpProgressively(n) n more (none for
    /// n ≤ 0) and returns whether all are warm (an empty collection: true). isWarmedUp needs at
    /// least one variant. Any change (an Add or Remove that does something, Clear) makes every
    /// variant cold again; a copy (Instantiate) starts cold. Graphics settings' preloaded
    /// collections are not warmed when Play mode starts.</item>
    /// </list>
    /// Warming up only counts: Unbound compiles a variant when it is first drawn (its compiled
    /// programs are cached on disk across runs).
    /// </summary>
    [ClassId(200)]
    public sealed partial class ShaderVariantCollection : Object, ISerializedAsset, Instantiation.ICloneable
    {
        readonly struct Variant : IEquatable<Variant>
        {
            internal readonly PassType Pass;
            /// <summary>The keywords joined by spaces.</summary>
            internal readonly string Keywords;

            internal Variant(PassType pass, string keywords)
            {
                Pass = pass;
                Keywords = keywords ?? "";
            }

            public bool Equals(Variant other) => Pass == other.Pass && string.Equals(Keywords, other.Keywords, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is Variant v && Equals(v);
            public override int GetHashCode() => ((int)Pass * 397) ^ StringComparer.Ordinal.GetHashCode(Keywords);
        }

        /// <summary>The variants of shaders that exist, by shader.</summary>
        readonly Dictionary<Shader, HashSet<Variant>> m_Shaders = new Dictionary<Shader, HashSet<Variant>>();
        /// <summary>The variants of an asset's shaders that did not load: counted, never matched.</summary>
        readonly List<HashSet<Variant>> m_Missing = new List<HashSet<Variant>>();
        int m_VariantCount;
        int m_Warmed;

        public ShaderVariantCollection() { }

        public int shaderCount => m_Shaders.Count + m_Missing.Count;
        public int variantCount => m_VariantCount;
        public int warmedUpVariantCount => m_Warmed;
        public bool isWarmedUp => m_VariantCount > 0 && m_Warmed >= m_VariantCount;

        public bool Add(ShaderVariant variant)
        {
            var shader = variant.shader;
            if (shader == null) return false;
            if (!m_Shaders.TryGetValue(shader, out var variants)) m_Shaders[shader] = variants = new HashSet<Variant>();
            if (!variants.Add(Normalize(variant))) return false;
            m_VariantCount++;
            m_Warmed = 0;
            return true;
        }

        public bool Remove(ShaderVariant variant)
        {
            var shader = variant.shader;
            if (shader == null || !m_Shaders.TryGetValue(shader, out var variants) || !variants.Remove(Normalize(variant))) return false;
            m_VariantCount--;
            m_Warmed = 0;
            return true;
        }

        public bool Contains(ShaderVariant variant)
        {
            var shader = variant.shader;
            return shader != null && m_Shaders.TryGetValue(shader, out var variants) && variants.Contains(Normalize(variant));
        }

        public void Clear()
        {
            m_Shaders.Clear();
            m_Missing.Clear();
            m_VariantCount = 0;
            m_Warmed = 0;
        }

        public void WarmUp() => m_Warmed = m_VariantCount;

        public bool WarmUpProgressively(int variantCount)
        {
            if (variantCount > 0) m_Warmed = Math.Min(m_VariantCount, m_Warmed + variantCount);
            return m_Warmed >= m_VariantCount;
        }

        /// <summary>A variant as a query names it: its shader's declared keywords (all non-blank
        /// names while the shader's keyword space is unknown), each once, sorted by name.</summary>
        static Variant Normalize(ShaderVariant variant)
        {
            var keywords = variant.keywords;
            if (keywords == null || keywords.Length == 0) return new Variant(variant.passType, "");
            var declared = ShaderDeclarations.Of(variant.shader);
            var kept = new List<string>(keywords.Length);
            foreach (var k in keywords)
            {
                if (declared.KeywordsKnown ? declared.IndexOf(k) < 0 : !ShaderVariant.IsKeywordName(k)) continue;
                if (!kept.Contains(k)) kept.Add(k);
            }
            kept.Sort(StringComparer.Ordinal);
            return new Variant(variant.passType, string.Join(" ", kept));
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            Clear();
            var list = body.Get("m_Shaders");
            if (list == null) return;
            foreach (var entry in list.Seq())
            {
                var reference = entry.Ref("first");
                var shader = reference.IsNull ? null : resolver.Resolve(reference, typeof(Shader)) as Shader;
                HashSet<Variant> variants;
                if (shader == null) m_Missing.Add(variants = new HashSet<Variant>());
                else if (!m_Shaders.TryGetValue(shader, out variants)) m_Shaders[shader] = variants = new HashSet<Variant>();
                var info = entry.Get("second")?.Get("variants");
                if (info == null) continue;
                foreach (var v in info.Seq())
                    if (variants.Add(new Variant((PassType)v.Int("passType"), v.Str("keywords")))) m_VariantCount++;
            }
        }

        Object Instantiation.ICloneable.CloneObject()
        {
            var copy = new ShaderVariantCollection { m_VariantCount = m_VariantCount };
            foreach (var pair in m_Shaders) copy.m_Shaders[pair.Key] = new HashSet<Variant>(pair.Value);
            foreach (var missing in m_Missing) copy.m_Missing.Add(new HashSet<Variant>(missing));
            return copy;
        }

        /// <summary>A shader, a pass type and keywords. The constructor checks the variant against
        /// the shader as far as its declarations are known: a null shader, a pass type none of its
        /// passes (its Fallback's included) has, an undeclared keyword or one given twice throw
        /// ArgumentException with Unity's messages. Whether each keyword belongs to that pass and
        /// whether two keywords of one <c>multi_compile</c> set are given together is not checked.</summary>
        public partial struct ShaderVariant
        {
            public Shader shader;
            public PassType passType;
            public string[] keywords;

            public ShaderVariant(Shader shader, PassType passType, params string[] keywords)
            {
                this.shader = shader;
                this.passType = passType;
                this.keywords = keywords;
                Check(shader, passType, keywords);
            }

            /// <summary>Whether a keyword could be one: not blank, without white space.</summary>
            internal static bool IsKeywordName(string keyword)
            {
                if (string.IsNullOrEmpty(keyword)) return false;
                foreach (var c in keyword)
                    if (char.IsWhiteSpace(c)) return false;
                return true;
            }

            static void Check(Shader shader, PassType passType, string[] keywords)
            {
                if (shader == null) throw new ArgumentException("null shader");
                var declared = ShaderDeclarations.Of(shader);
                if (declared.SubShadersKnown && !HasPass(declared, passType))
                    throw new ArgumentException($"shader {shader.name} does not have pass type {(int)passType}");
                if (keywords == null || !declared.KeywordsKnown) return;
                for (int i = 0; i < keywords.Length; i++)
                {
                    var k = keywords[i];
                    // An empty keyword is no keyword.
                    if (string.IsNullOrEmpty(k)) continue;
                    if (declared.IndexOf(k) < 0 || Array.IndexOf(keywords, k, 0, i) >= 0)
                        throw new ArgumentException($"passed shader keyword variant not found in shader {shader.name} pass type {(int)passType}");
                }
            }

            static bool HasPass(ShaderDeclarations declared, PassType passType)
            {
                foreach (var sub in declared.SubShaders)
                    foreach (var pass in sub.Passes)
                        if (PassTypeOf(ShaderDeclarations.Tag(pass, "LightMode")) == passType) return true;
                return false;
            }

            /// <summary>The pass type of a pass's LightMode tag under the built-in pipeline: an
            /// untagged pass is Normal; a tag the built-in pipeline does not know is a render
            /// pipeline's (ScriptableRenderPipeline).</summary>
            internal static PassType PassTypeOf(string lightMode)
            {
                if (string.IsNullOrEmpty(lightMode)) return PassType.Normal;
                foreach (var (name, type) in s_LightModes)
                    if (string.Equals(name, lightMode, StringComparison.OrdinalIgnoreCase)) return type;
                return PassType.ScriptableRenderPipeline;
            }

#pragma warning disable 618
            static readonly (string, PassType)[] s_LightModes =
            {
                ("Always", PassType.Normal), ("Vertex", PassType.Vertex), ("VertexLM", PassType.VertexLM),
                ("VertexLMRGBM", PassType.VertexLMRGBM), ("ForwardBase", PassType.ForwardBase),
                ("ForwardAdd", PassType.ForwardAdd), ("PrepassBase", PassType.LightPrePassBase),
                ("PrepassFinal", PassType.LightPrePassFinal), ("ShadowCaster", PassType.ShadowCaster),
                ("Deferred", PassType.Deferred), ("Meta", PassType.Meta), ("MotionVectors", PassType.MotionVectors),
                ("SRPDefaultUnlit", PassType.ScriptableRenderPipelineDefaultUnlit), ("GrabPass", PassType.GrabPass),
            };
#pragma warning restore 618
        }
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>The kind of a shader pass (its LightMode under the built-in pipeline).</summary>
    public enum PassType
    {
        Normal = 0,
        Vertex = 1,
        VertexLM = 2,
        [Obsolete("VertexLMRGBM PassType is obsolete. Please use VertexLM PassType together with DecodeLightmap shader function.")] VertexLMRGBM = 3,
        ForwardBase = 4,
        ForwardAdd = 5,
        [Obsolete("Deferred Lighting was removed, so LightPrePassBase pass type is never used anymore.")] LightPrePassBase = 6,
        [Obsolete("Deferred Lighting was removed, so LightPrePassFinal pass type is never used anymore.")] LightPrePassFinal = 7,
        ShadowCaster = 8,
        Deferred = 10,
        Meta = 11,
        MotionVectors = 12,
        ScriptableRenderPipeline = 13,
        ScriptableRenderPipelineDefaultUnlit = 14,
        GrabPass = 15,
    }
}
