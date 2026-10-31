using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// An asset postprocessor: the Editor runs its <c>OnPreprocess*</c>/<c>OnPostprocess*</c>
    /// methods while an asset imports. Unbound runs <c>OnPreprocessMaterialDescription</c> when a
    /// model's material loads (<see cref="Unbound.Runtime.ModelMaterialImport"/>).
    /// </summary>
    public partial class AssetPostprocessor
    {
        string m_AssetPath = "";

        public AssetPostprocessor() { }

        /// <summary>The path of the asset being imported.</summary>
        public string assetPath { get => m_AssetPath; set => m_AssetPath = value ?? ""; }

        /// <summary>The import context of the asset being imported.</summary>
        public AssetImporters.AssetImportContext context { get; internal set; }

        /// <summary>The importer of the asset being imported.</summary>
        public AssetImporter assetImporter { get; internal set; }

        /// <summary>The version of the postprocessor (part of its assets' import inputs).</summary>
        [Unbound.EditorFaithful("Unity's default for postprocessors that do not override it")]
        public virtual uint GetVersion() => 0;

        /// <summary>Postprocessors run by increasing order.</summary>
        [Unbound.EditorFaithful("Unity's default for postprocessors that do not override it")]
        public virtual int GetPostprocessOrder() => 0;
    }
}

namespace UnityEditor.AssetImporters
{
    /// <summary>A texture of a <see cref="MaterialDescription"/>: the file the model names and the
    /// texture asset the importer found for it.</summary>
    public partial struct TexturePropertyDescription
    {
        public Vector2 offset;
        public Vector2 scale;
        public Texture texture;
        public string relativePath;
        public string path;
    }

    /// <summary>
    /// A material of an imported model as its file describes it: named float, vector, string and
    /// texture properties (FBX: the material's properties, see the model importer). Material
    /// animation curves are not imported: <see cref="HasAnimationCurve"/> is false.
    /// </summary>
    public partial class MaterialDescription : IDisposable
    {
        internal readonly Dictionary<string, float> m_Floats = new Dictionary<string, float>();
        internal readonly Dictionary<string, Vector4> m_Vectors = new Dictionary<string, Vector4>();
        internal readonly Dictionary<string, string> m_Strings = new Dictionary<string, string>();
        internal readonly Dictionary<string, TexturePropertyDescription> m_Textures =
            new Dictionary<string, TexturePropertyDescription>();
        internal string m_MaterialName = "";

        public MaterialDescription() { }

        [Unbound.EditorFaithful("the description holds no native memory")]
        public void Dispose() { }

        public string materialName => m_MaterialName;

        public bool TryGetProperty(string propertyName, out float value) =>
            m_Floats.TryGetValue(propertyName ?? "", out value);

        public bool TryGetProperty(string propertyName, out Vector4 value) =>
            m_Vectors.TryGetValue(propertyName ?? "", out value);

        public bool TryGetProperty(string propertyName, out string value) =>
            m_Strings.TryGetValue(propertyName ?? "", out value);

        public bool TryGetProperty(string propertyName, out TexturePropertyDescription value) =>
            m_Textures.TryGetValue(propertyName ?? "", out value);

        public void GetFloatPropertyNames(List<string> names) => Names(m_Floats.Keys, names);

        public void GetVector4PropertyNames(List<string> names) => Names(m_Vectors.Keys, names);

        public void GetStringPropertyNames(List<string> names) => Names(m_Strings.Keys, names);

        public void GetTexturePropertyNames(List<string> names) => Names(m_Textures.Keys, names);

        static void Names(IEnumerable<string> keys, List<string> names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            names.Clear();
            names.AddRange(keys);
        }

        [Unbound.EditorFaithful(NoCurves)]
        public bool TryGetAnimationCurve(string clipName, string propertyName, out AnimationCurve value)
        {
            value = null;
            return false;
        }

        [Unbound.EditorFaithful(NoCurves)]
        public bool HasAnimationCurveInClip(string clipName, string propertyName) => false;

        [Unbound.EditorFaithful(NoCurves)]
        public bool HasAnimationCurve(string propertyName) => false;

        const string NoCurves = "material animation is not imported; the model importer warns when the file has some";
    }
}
