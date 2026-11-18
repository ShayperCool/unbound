using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unbound.Runtime;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// An importer's section of an asset's .meta as an editable tree: the settings the Texture,
    /// Model and Audio importers' properties read and write. Unbound writes no .meta file; changed
    /// settings are what the asset imports with in this session (see
    /// <see cref="UnityEditor.AssetPostprocessing"/>). Paths name map keys separated by dots; a
    /// segment <c>[key=value]</c> picks the entry of a sequence whose <c>key</c> is <c>value</c>.
    /// </summary>
    internal static class MetaSettings
    {
        /// <summary>A deep copy (the native tree is shared by every reader of the .meta).</summary>
        internal static YNode Clone(YNode node)
        {
            if (node == null) return null;
            switch (node.Kind)
            {
                case YKind.Map:
                    var values = new YNode[node.Values.Length];
                    for (int i = 0; i < values.Length; i++) values[i] = Clone(node.Values[i]);
                    return new YNode { Kind = YKind.Map, Keys = (string[])node.Keys.Clone(), Values = values };
                case YKind.Seq:
                    var items = new YNode[node.Items.Length];
                    for (int i = 0; i < items.Length; i++) items[i] = Clone(node.Items[i]);
                    return new YNode { Kind = YKind.Seq, Items = items };
                case YKind.Scalar:
                    return YNode.MakeScalar(node.Scalar);
                default:
                    return new YNode { Kind = YKind.Null };
            }
        }

        /// <summary>The node at <paramref name="path"/>, or null.</summary>
        internal static YNode Find(YNode root, string path)
        {
            var node = root;
            foreach (var segment in path.Split('.'))
            {
                if (node == null) return null;
                node = Step(node, segment, create: false);
            }
            return node;
        }

        static YNode Step(YNode node, string segment, bool create)
        {
            if (segment.Length > 0 && segment[0] == '[')
            {
                int eq = segment.IndexOf('=');
                string key = segment.Substring(1, eq - 1), value = segment.Substring(eq + 1, segment.Length - eq - 2);
                if (node.Kind != YKind.Seq) return null;
                foreach (var item in node.Items)
                    if (item.Get(key)?.AsString() == value) return item;
                return null;
            }
            var child = node.Get(segment);
            if (child != null || !create || node.Kind != YKind.Map) return child;
            child = new YNode { Kind = YKind.Map, Keys = Array.Empty<string>(), Values = Array.Empty<YNode>() };
            Append(node, segment, child);
            return child;
        }

        static void Append(YNode map, string key, YNode value)
        {
            int n = map.Keys.Length;
            var keys = new string[n + 1];
            var values = new YNode[n + 1];
            Array.Copy(map.Keys, keys, n);
            Array.Copy(map.Values, values, n);
            keys[n] = key;
            values[n] = value;
            map.Keys = keys;
            map.Values = values;
        }

        /// <summary>Sets the scalar at <paramref name="path"/>, adding the missing maps on the way
        /// (a missing sequence entry is not added: the setting then stays unset).</summary>
        internal static void Set(YNode root, string path, string scalar)
        {
            var segments = path.Split('.');
            var node = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                node = Step(node, segments[i], create: true);
                if (node == null) return;
            }
            string last = segments[segments.Length - 1];
            if (node.Kind != YKind.Map) return;
            for (int i = 0; i < node.Keys.Length; i++)
                if (node.Keys[i] == last)
                {
                    node.Values[i] = YNode.MakeScalar(scalar);
                    return;
                }
            Append(node, last, YNode.MakeScalar(scalar));
        }

        internal static int Int(YNode root, string path, int fallback = 0) => Find(root, path)?.AsInt(fallback) ?? fallback;
        internal static float Float(YNode root, string path, float fallback = 0) => Find(root, path) is { } n ? (float)n.AsDouble(fallback) : fallback;
        internal static bool Bool(YNode root, string path, bool fallback = false) => Find(root, path) is { } n ? n.AsLong(fallback ? 1 : 0) != 0 : fallback;

        internal static void SetInt(YNode root, string path, long value) => Set(root, path, value.ToString(CultureInfo.InvariantCulture));
        internal static void SetBool(YNode root, string path, bool value) => Set(root, path, value ? "1" : "0");
        internal static void SetFloat(YNode root, string path, float value) => Set(root, path, value.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>The tree as JSON (maps as objects, sequences as arrays, scalars as strings), as
        /// the native side reads a .meta's tree.</summary>
        internal static string ToJson(YNode node)
        {
            var sb = new StringBuilder();
            Write(sb, node);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, YNode node)
        {
            switch (node?.Kind ?? YKind.Null)
            {
                case YKind.Map:
                    sb.Append('{');
                    for (int i = 0; i < node.Keys.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        String(sb, node.Keys[i]);
                        sb.Append(':');
                        Write(sb, node.Values[i]);
                    }
                    sb.Append('}');
                    break;
                case YKind.Seq:
                    sb.Append('[');
                    for (int i = 0; i < node.Items.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Write(sb, node.Items[i]);
                    }
                    sb.Append(']');
                    break;
                case YKind.Scalar:
                    String(sb, node.Scalar);
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }

        static void String(StringBuilder sb, string s) => sb.Append(System.Text.Json.JsonSerializer.Serialize(s ?? ""));
    }
}

namespace UnityEditor
{
    public partial class AssetImporter
    {
        /// <summary>The importer's section of the .meta (a copy the properties edit), its key
        /// (TextureImporter, ...) and whether a property changed it.</summary>
        internal YNode m_Settings;
        internal string m_SettingsType;
        internal bool m_SettingsChanged;
        /// <summary>The settings the asset imports with, as JSON (the .meta's until a
        /// preprocessor changes them).</summary>
        internal string m_ImportedSettings;

        internal void Changed() => m_SettingsChanged = true;
    }

    /// <summary>The texture importer's settings, read from and written to its .meta section (the
    /// default platform's for the per-platform ones).</summary>
    public sealed partial class TextureImporter : AssetImporter
    {
        const string Default = "platformSettings.[buildTarget=DefaultTexturePlatform].";

        int I(string path, int fallback = 0) => MetaSettings.Int(m_Settings, path, fallback);
        bool B(string path) => MetaSettings.Bool(m_Settings, path);
        void SetI(string path, long value) { NeedsPlatform(path); MetaSettings.SetInt(m_Settings, path, value); Changed(); }
        void SetB(string path, bool value) { NeedsPlatform(path); MetaSettings.SetBool(m_Settings, path, value); Changed(); }

        /// <summary>A default-platform setting of a .meta without that platform's entry is not kept.</summary>
        void NeedsPlatform(string path)
        {
            if (path.StartsWith(Default, StringComparison.Ordinal) && MetaSettings.Find(m_Settings, Default.TrimEnd('.')) == null)
                Debug.LogWarning($"Unbound: {assetPath}'s .meta has no DefaultTexturePlatform settings; {path.Substring(Default.Length)} set by a script is not kept");
        }

        /// <summary>Setting a type sets what the type implies, as
        /// <see cref="TextureImporterSettings.ApplyTextureType"/> does (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/assetpostprocessors Sprite.png: a Sprite type turns mip maps,
        /// non-power-of-two scaling and repeating off and alpha is transparency on).</summary>
        public TextureImporterType textureType
        {
            get { CheckImporterState("get_textureType"); return (TextureImporterType)I("textureType"); }
            set
            {
                CheckImporterState("set_textureType");
                var settings = new TextureImporterSettings();
                ReadTextureSettings(settings);
                settings.ApplyTextureType(value);
                SetTextureSettings(settings);
            }
        }

        public int maxTextureSize
        {
            get { CheckImporterState("get_maxTextureSize"); return I(Default + "maxTextureSize", 2048); }
            set { CheckImporterState("set_maxTextureSize"); SetI(Default + "maxTextureSize", value); }
        }

        public TextureImporterCompression textureCompression
        {
            get { CheckImporterState("get_textureCompression"); return (TextureImporterCompression)I(Default + "textureCompression", 1); }
            set { CheckImporterState("set_textureCompression"); SetI(Default + "textureCompression", (int)value); }
        }

        public int compressionQuality
        {
            get { CheckImporterState("get_compressionQuality"); return I(Default + "compressionQuality", 50); }
            set { CheckImporterState("set_compressionQuality"); SetI(Default + "compressionQuality", value); }
        }

        public bool crunchedCompression
        {
            get { CheckImporterState("get_crunchedCompression"); return B(Default + "crunchedCompression"); }
            set { CheckImporterState("set_crunchedCompression"); SetB(Default + "crunchedCompression", value); }
        }

        public FilterMode filterMode
        {
            get { CheckImporterState("get_filterMode"); return (FilterMode)I("textureSettings.filterMode", 1); }
            set { CheckImporterState("set_filterMode"); SetI("textureSettings.filterMode", (int)value); }
        }

        public int anisoLevel
        {
            get { CheckImporterState("get_anisoLevel"); return I("textureSettings.aniso", 1); }
            set { CheckImporterState("set_anisoLevel"); SetI("textureSettings.aniso", value); }
        }

        public float mipMapBias
        {
            get { CheckImporterState("get_mipMapBias"); return MetaSettings.Float(m_Settings, "textureSettings.mipBias"); }
            set { CheckImporterState("set_mipMapBias"); MetaSettings.SetFloat(m_Settings, "textureSettings.mipBias", value); Changed(); }
        }

        /// <summary>The U wrap mode; setting it sets all three.</summary>
        public TextureWrapMode wrapMode
        {
            get { CheckImporterState("get_wrapMode"); return (TextureWrapMode)I("textureSettings.wrapU"); }
            set
            {
                CheckImporterState("set_wrapMode");
                SetI("textureSettings.wrapU", (int)value);
                SetI("textureSettings.wrapV", (int)value);
                SetI("textureSettings.wrapW", (int)value);
            }
        }

        public TextureWrapMode wrapModeU
        {
            get { CheckImporterState("get_wrapModeU"); return (TextureWrapMode)I("textureSettings.wrapU"); }
            set { CheckImporterState("set_wrapModeU"); SetI("textureSettings.wrapU", (int)value); }
        }

        public TextureWrapMode wrapModeV
        {
            get { CheckImporterState("get_wrapModeV"); return (TextureWrapMode)I("textureSettings.wrapV"); }
            set { CheckImporterState("set_wrapModeV"); SetI("textureSettings.wrapV", (int)value); }
        }

        public TextureWrapMode wrapModeW
        {
            get { CheckImporterState("get_wrapModeW"); return (TextureWrapMode)I("textureSettings.wrapW"); }
            set { CheckImporterState("set_wrapModeW"); SetI("textureSettings.wrapW", (int)value); }
        }

        public bool mipmapEnabled
        {
            get { CheckImporterState("get_mipmapEnabled"); return B("mipmaps.enableMipMap"); }
            set { CheckImporterState("set_mipmapEnabled"); SetB("mipmaps.enableMipMap", value); }
        }

        public bool isReadable
        {
            get { CheckImporterState("get_isReadable"); return B("isReadable"); }
            set { CheckImporterState("set_isReadable"); SetB("isReadable", value); }
        }

        public bool sRGBTexture
        {
            get { CheckImporterState("get_sRGBTexture"); return B("mipmaps.sRGBTexture"); }
            set { CheckImporterState("set_sRGBTexture"); SetB("mipmaps.sRGBTexture", value); }
        }

        public TextureImporterNPOTScale npotScale
        {
            get { CheckImporterState("get_npotScale"); return (TextureImporterNPOTScale)I("nPOTScale", 1); }
            set { CheckImporterState("set_npotScale"); SetI("nPOTScale", (int)value); }
        }

        public bool alphaIsTransparency
        {
            get { CheckImporterState("get_alphaIsTransparency"); return B("alphaIsTransparency"); }
            set { CheckImporterState("set_alphaIsTransparency"); SetB("alphaIsTransparency", value); }
        }

        public TextureImporterAlphaSource alphaSource
        {
            get { CheckImporterState("get_alphaSource"); return (TextureImporterAlphaSource)I("alphaUsage", 1); }
            set { CheckImporterState("set_alphaSource"); SetI("alphaUsage", (int)value); }
        }

        public SpriteImportMode spriteImportMode
        {
            get { CheckImporterState("get_spriteImportMode"); return (SpriteImportMode)I("spriteMode"); }
            set { CheckImporterState("set_spriteImportMode"); SetI("spriteMode", (int)value); }
        }

        public float spritePixelsPerUnit
        {
            get { CheckImporterState("get_spritePixelsPerUnit"); return MetaSettings.Float(m_Settings, "spritePixelsToUnits", 100); }
            set { CheckImporterState("set_spritePixelsPerUnit"); MetaSettings.SetFloat(m_Settings, "spritePixelsToUnits", value); Changed(); }
        }

        public bool convertToNormalmap
        {
            get { CheckImporterState("get_convertToNormalmap"); return B("bumpmap.convertToNormalMap"); }
            set { CheckImporterState("set_convertToNormalmap"); SetB("bumpmap.convertToNormalMap", value); }
        }

        public TextureImporterMipFilter mipmapFilter
        {
            get { CheckImporterState("get_mipmapFilter"); return (TextureImporterMipFilter)I("mipmaps.mipMapMode"); }
            set { CheckImporterState("set_mipmapFilter"); SetI("mipmaps.mipMapMode", (int)value); }
        }

        public TextureImporterShape textureShape
        {
            get { CheckImporterState("get_textureShape"); return (TextureImporterShape)I("textureShape", 1); }
            set { CheckImporterState("set_textureShape"); SetI("textureShape", (int)value); }
        }

        /// <summary>The settings <see cref="TextureImporterSettings"/> holds, from this importer's.</summary>
        public void ReadTextureSettings(TextureImporterSettings dest)
        {
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            foreach (var (path, read, _) in s_SettingsFields) read(dest, m_Settings, path);
        }

        public void SetTextureSettings(TextureImporterSettings src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            foreach (var (path, _, write) in s_SettingsFields) write(src, m_Settings, path);
            Changed();
        }

        /// <summary>The .meta key of each setting <see cref="TextureImporterSettings"/> holds that
        /// the import reads.</summary>
        static readonly (string, Action<TextureImporterSettings, YNode, string>, Action<TextureImporterSettings, YNode, string>)[] s_SettingsFields =
        {
            Int("textureType", s => (int)s.textureType, (s, v) => s.textureType = (TextureImporterType)v),
            Int("textureShape", s => (int)s.textureShape, (s, v) => s.textureShape = (TextureImporterShape)v),
            Int("mipmaps.enableMipMap", s => s.mipmapEnabled ? 1 : 0, (s, v) => s.mipmapEnabled = v != 0),
            Int("mipmaps.sRGBTexture", s => s.sRGBTexture ? 1 : 0, (s, v) => s.sRGBTexture = v != 0),
            Int("mipmaps.mipMapMode", s => (int)s.mipmapFilter, (s, v) => s.mipmapFilter = (TextureImporterMipFilter)v),
            Int("mipmaps.borderMipMap", s => s.borderMipmap ? 1 : 0, (s, v) => s.borderMipmap = v != 0),
            Int("mipmaps.fadeOut", s => s.fadeOut ? 1 : 0, (s, v) => s.fadeOut = v != 0),
            Int("mipmaps.mipMapsPreserveCoverage", s => s.mipMapsPreserveCoverage ? 1 : 0, (s, v) => s.mipMapsPreserveCoverage = v != 0),
            Int("bumpmap.convertToNormalMap", s => s.convertToNormalMap ? 1 : 0, (s, v) => s.convertToNormalMap = v != 0),
            Int("isReadable", s => s.readable ? 1 : 0, (s, v) => s.readable = v != 0),
            Int("nPOTScale", s => (int)s.npotScale, (s, v) => s.npotScale = (TextureImporterNPOTScale)v),
            Int("alphaUsage", s => (int)s.alphaSource, (s, v) => s.alphaSource = (TextureImporterAlphaSource)v),
            Int("alphaIsTransparency", s => s.alphaIsTransparency ? 1 : 0, (s, v) => s.alphaIsTransparency = v != 0),
            Int("spriteMode", s => s.spriteMode, (s, v) => s.spriteMode = v),
            Int("spriteMeshType", s => (int)s.spriteMeshType, (s, v) => s.spriteMeshType = (SpriteMeshType)v),
            Int("singleChannelComponent", s => (int)s.singleChannelComponent, (s, v) => s.singleChannelComponent = (TextureImporterSingleChannelComponent)v),
            Int("textureSettings.filterMode", s => (int)s.filterMode, (s, v) => s.filterMode = (FilterMode)v),
            Int("textureSettings.aniso", s => s.aniso, (s, v) => s.aniso = v),
            Int("textureSettings.wrapU", s => (int)s.wrapModeU, (s, v) => s.wrapModeU = (TextureWrapMode)v),
            Int("textureSettings.wrapV", s => (int)s.wrapModeV, (s, v) => s.wrapModeV = (TextureWrapMode)v),
            Int("textureSettings.wrapW", s => (int)s.wrapModeW, (s, v) => s.wrapModeW = (TextureWrapMode)v),
            Int("generateCubemap", s => (int)s.generateCubemap, (s, v) => s.generateCubemap = (TextureImporterGenerateCubemap)v),
            Float("spritePixelsToUnits", s => s.spritePixelsPerUnit, (s, v) => s.spritePixelsPerUnit = v),
        };

        static (string, Action<TextureImporterSettings, YNode, string>, Action<TextureImporterSettings, YNode, string>) Int(
            string path, Func<TextureImporterSettings, int> get, Action<TextureImporterSettings, int> set) =>
            (path, (s, node, p) => { if (MetaSettings.Find(node, p) is { } v) set(s, v.AsInt()); }, (s, node, p) => MetaSettings.SetInt(node, p, get(s)));

        static (string, Action<TextureImporterSettings, YNode, string>, Action<TextureImporterSettings, YNode, string>) Float(
            string path, Func<TextureImporterSettings, float> get, Action<TextureImporterSettings, float> set) =>
            (path, (s, node, p) => { if (MetaSettings.Find(node, p) is { } v) set(s, (float)v.AsDouble()); }, (s, node, p) => MetaSettings.SetFloat(node, p, get(s)));
    }

    /// <summary>The model importer's settings, read from and written to its .meta section.</summary>
    public partial class ModelImporter : AssetImporter
    {
        int I(string path, int fallback = 0) => MetaSettings.Int(m_Settings, path, fallback);
        bool B(string path) => MetaSettings.Bool(m_Settings, path);
        void SetI(string path, long value) { MetaSettings.SetInt(m_Settings, path, value); Changed(); }
        void SetB(string path, bool value) { MetaSettings.SetBool(m_Settings, path, value); Changed(); }

        public float globalScale
        {
            get { CheckImporterState("get_globalScale"); return MetaSettings.Float(m_Settings, "meshes.globalScale", 1); }
            set { CheckImporterState("set_globalScale"); MetaSettings.SetFloat(m_Settings, "meshes.globalScale", value); Changed(); }
        }

        public bool useFileScale
        {
            get { CheckImporterState("get_useFileScale"); return MetaSettings.Bool(m_Settings, "meshes.useFileScale", true); }
            set { CheckImporterState("set_useFileScale"); SetB("meshes.useFileScale", value); }
        }

        public bool importAnimation
        {
            get { CheckImporterState("get_importAnimation"); return MetaSettings.Bool(m_Settings, "importAnimation", true); }
            set { CheckImporterState("set_importAnimation"); SetB("importAnimation", value); }
        }

        public ModelImporterMaterialImportMode materialImportMode
        {
            get { CheckImporterState("get_materialImportMode"); return (ModelImporterMaterialImportMode)I("materials.materialImportMode", 2); }
            set { CheckImporterState("set_materialImportMode"); SetI("materials.materialImportMode", (int)value); }
        }

        /// <summary>Read/Write: the meshes section's in older .meta layouts, else the animations
        /// section's (the native importer reads the same).</summary>
        string ReadableKey => MetaSettings.Find(m_Settings, "meshes.isReadable") != null ? "meshes.isReadable" : "animations.isReadable";

        public bool isReadable
        {
            get { CheckImporterState("get_isReadable"); return B(ReadableKey); }
            set { CheckImporterState("set_isReadable"); SetB(ReadableKey, value); }
        }

        public bool addCollider
        {
            get { CheckImporterState("get_addCollider"); return B("meshes.addColliders"); }
            set { CheckImporterState("set_addCollider"); SetB("meshes.addColliders", value); }
        }

        public ModelImporterAnimationType animationType
        {
            get { CheckImporterState("get_animationType"); return (ModelImporterAnimationType)I("animationType", 2); }
            set { CheckImporterState("set_animationType"); SetI("animationType", (int)value); }
        }

        public bool importBlendShapes
        {
            get { CheckImporterState("get_importBlendShapes"); return MetaSettings.Bool(m_Settings, "meshes.importBlendShapes", true); }
            set { CheckImporterState("set_importBlendShapes"); SetB("meshes.importBlendShapes", value); }
        }

        public ModelImporterMeshCompression meshCompression
        {
            get { CheckImporterState("get_meshCompression"); return (ModelImporterMeshCompression)I("meshes.meshCompression"); }
            set { CheckImporterState("set_meshCompression"); SetI("meshes.meshCompression", (int)value); }
        }

        public ModelImporterNormals importNormals
        {
            get { CheckImporterState("get_importNormals"); return (ModelImporterNormals)I("tangentSpace.normalImportMode"); }
            set { CheckImporterState("set_importNormals"); SetI("tangentSpace.normalImportMode", (int)value); }
        }

        public bool importCameras
        {
            get { CheckImporterState("get_importCameras"); return MetaSettings.Bool(m_Settings, "meshes.importCameras", true); }
            set { CheckImporterState("set_importCameras"); SetB("meshes.importCameras", value); }
        }

        public bool importLights
        {
            get { CheckImporterState("get_importLights"); return MetaSettings.Bool(m_Settings, "meshes.importLights", true); }
            set { CheckImporterState("set_importLights"); SetB("meshes.importLights", value); }
        }

        public bool generateSecondaryUV
        {
            get { CheckImporterState("get_generateSecondaryUV"); return B("meshes.generateSecondaryUV"); }
            set { CheckImporterState("set_generateSecondaryUV"); SetB("meshes.generateSecondaryUV", value); }
        }
    }

    /// <summary>The audio importer's settings, read from and written to its .meta section (the
    /// default sample settings are its <c>defaultSettings</c>).</summary>
    public sealed partial class AudioImporter : AssetImporter
    {
        bool B(string path) => MetaSettings.Bool(m_Settings, path);
        void SetB(string path, bool value) { MetaSettings.SetBool(m_Settings, path, value); Changed(); }

        public bool forceToMono
        {
            get { CheckImporterState("get_forceToMono"); return B("forceToMono"); }
            set { CheckImporterState("set_forceToMono"); SetB("forceToMono", value); }
        }

        public bool loadInBackground
        {
            get { CheckImporterState("get_loadInBackground"); return B("loadInBackground"); }
            set { CheckImporterState("set_loadInBackground"); SetB("loadInBackground", value); }
        }

        public bool ambisonic
        {
            get { CheckImporterState("get_ambisonic"); return B("ambisonic"); }
            set { CheckImporterState("set_ambisonic"); SetB("ambisonic", value); }
        }

        public AudioImporterSampleSettings defaultSampleSettings
        {
            get
            {
                CheckImporterState("get_defaultSampleSettings");
                return new AudioImporterSampleSettings
                {
                    loadType = (AudioClipLoadType)MetaSettings.Int(m_Settings, "defaultSettings.loadType"),
                    sampleRateSetting = (AudioSampleRateSetting)MetaSettings.Int(m_Settings, "defaultSettings.sampleRateSetting"),
                    sampleRateOverride = (uint)MetaSettings.Int(m_Settings, "defaultSettings.sampleRateOverride", 44100),
                    compressionFormat = (AudioCompressionFormat)MetaSettings.Int(m_Settings, "defaultSettings.compressionFormat", 1),
                    quality = MetaSettings.Float(m_Settings, "defaultSettings.quality", 1),
                    conversionMode = MetaSettings.Int(m_Settings, "defaultSettings.conversionMode"),
                    preloadAudioData = B("defaultSettings.preloadAudioData"),
                };
            }
            set
            {
                CheckImporterState("set_defaultSampleSettings");
                MetaSettings.SetInt(m_Settings, "defaultSettings.loadType", (int)value.loadType);
                MetaSettings.SetInt(m_Settings, "defaultSettings.sampleRateSetting", (int)value.sampleRateSetting);
                MetaSettings.SetInt(m_Settings, "defaultSettings.sampleRateOverride", value.sampleRateOverride);
                MetaSettings.SetInt(m_Settings, "defaultSettings.compressionFormat", (int)value.compressionFormat);
                MetaSettings.SetFloat(m_Settings, "defaultSettings.quality", value.quality);
                MetaSettings.SetInt(m_Settings, "defaultSettings.conversionMode", value.conversionMode);
                MetaSettings.SetBool(m_Settings, "defaultSettings.preloadAudioData", value.preloadAudioData);
                Changed();
            }
        }
    }

    public enum ModelImporterMaterialImportMode
    {
        None = 0,
        ImportStandard = 1,
        ImportViaMaterialDescription = 2,
        [global::System.Obsolete("Use ImportStandard (UnityUpgradable) -> ImportStandard")] LegacyImport = 1,
        [global::System.Obsolete("Use ImportViaMaterialDescription (UnityUpgradable) -> ImportViaMaterialDescription")] Import = 2,
    }

    public enum ModelImporterAnimationType
    {
        None = 0,
        Legacy = 1,
        Generic = 2,
        Human = 3,
    }

    public enum ModelImporterMeshCompression
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3,
    }

    public enum ModelImporterNormals
    {
        Import = 0,
        Calculate = 1,
        None = 2,
    }

    public enum AudioSampleRateSetting
    {
        PreserveSampleRate = 0,
        OptimizeSampleRate = 1,
        OverrideSampleRate = 2,
    }

    [global::System.Serializable]
    public partial struct AudioImporterSampleSettings
    {
        public AudioClipLoadType loadType;
        public AudioSampleRateSetting sampleRateSetting;
        public uint sampleRateOverride;
        public AudioCompressionFormat compressionFormat;
        public float quality;
        public int conversionMode;
        public bool preloadAudioData;
    }
}
