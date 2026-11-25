using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
    /// <summary>
    /// The SpeedTree 7 and 8 importer's settings, read from its .meta section
    /// (<c>SpeedTreeImporter:</c>), with the values the Unity 6000.0.71f1 Editor gives them
    /// (SpeedTree8ImportTests): the per-level arrays are the levels' <c>lODSettings</c>, the
    /// "by default" settings the tree-wide ones. Changing a setting is not supported yet (the setters
    /// report it). Unbound imports SpeedTree 8 (<c>.st</c>) files; SpeedTree 7 (<c>.spm</c>) files are
    /// not imported.
    /// </summary>
    public partial class SpeedTreeImporter : AssetImporter
    {
        /// <summary>Whether this session has imported the tree (false while its first import runs).</summary>
        internal bool m_HasImported;

        float F(string path, float fallback = 0f) => MetaSettings.Float(m_Settings, path, fallback);
        bool B(string path, bool fallback = false) => MetaSettings.Bool(m_Settings, path, fallback);
        int I(string path, int fallback = 0) => MetaSettings.Int(m_Settings, path, fallback);

        Color C(string path)
        {
            var node = MetaSettings.Find(m_Settings, path);
            if (node == null) return new Color(0f, 0f, 0f, 0f);
            float Part(string key) => (float)(node.Get(key)?.AsDouble(0) ?? 0);
            return new Color(Part("r"), Part("g"), Part("b"), Part("a"));
        }

        /// <summary>The levels' settings, in level order.</summary>
        YNode[] Levels => MetaSettings.Find(m_Settings, "lODSettings")?.Items ?? Array.Empty<YNode>();

        T[] PerLevel<T>(Func<YNode, T> read)
        {
            var levels = Levels;
            var values = new T[levels.Length];
            for (int i = 0; i < values.Length; i++) values[i] = read(levels[i]);
            return values;
        }

        static bool LevelBool(YNode level, string key) => (level.Get(key)?.AsLong(0) ?? 0) != 0;

        /// <summary>Whether a level overrides the tree's settings: a level of an older .meta
        /// (serializedVersion 6, whose levels have no <c>enableSettingOverride</c> and no tree-wide
        /// settings) always does.</summary>
        static bool LevelOverrides(YNode level) => (level.Get("enableSettingOverride")?.AsLong(1) ?? 1) != 0;

        /// <summary>The tree's selected wind quality; an older .meta without one has its best.</summary>
        int SelectedWindQuality => I("selectedWindQuality", I("bestWindQuality"));

        void Unsupported(string member) => Stubs.NotImplemented("UnityEditor.SpeedTreeImporter." + member);

        public enum MaterialLocation
        {
            External = 0,
            InPrefab = 1,
        }

        public static readonly string[] windQualityNames = { "None", "Fastest", "Fast", "Better", "Best", "Palm" };

        public bool hasImported { get { CheckImporterState("get_hasImported"); return m_HasImported; } }

        /// <summary>The folder External materials come from: the tree's folder, "&lt;name&gt; Materials".</summary>
        public string materialFolderPath
        {
            get
            {
                CheckImporterState("get_materialFolderPath");
                string path = assetPath;
                int slash = path.LastIndexOf('/');
                return (slash >= 0 ? path.Substring(0, slash + 1) : "") + System.IO.Path.GetFileNameWithoutExtension(path) + " Materials";
            }
        }

        public MaterialLocation materialLocation
        {
            get { CheckImporterState("get_materialLocation"); return (MaterialLocation)I("materialLocation", (int)MaterialLocation.InPrefab); }
            set { CheckImporterState("set_materialLocation"); Unsupported("materialLocation"); }
        }

        /// <summary>Whether the tree is a SpeedTree 8 file (by its extension).</summary>
        public bool isV8
        {
            get { CheckImporterState("get_isV8"); return string.Equals(System.IO.Path.GetExtension(assetPath), ".st", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>The render pipeline's SpeedTree shader, else the built-in one.</summary>
        public Shader defaultShader
        {
            get
            {
                CheckImporterState("get_defaultShader");
                var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                var shader = pipeline == null ? null : isV8 ? pipeline.defaultSpeedTree8Shader : pipeline.defaultSpeedTree7Shader;
                return shader != null ? shader : Shader.Find(isV8 ? "Nature/SpeedTree8" : "Nature/SpeedTree");
            }
        }

        /// <summary>SpeedTree 7's billboard shader; none for SpeedTree 8, whose billboards are mesh levels.</summary>
        public Shader defaultBillboardShader
        {
            get { CheckImporterState("get_defaultBillboardShader"); return isV8 ? null : Shader.Find("Nature/SpeedTree Billboard"); }
        }

        /// <summary>The import scale: the unit conversion's (Leave As Is 1, feet, centimetres and
        /// inches to metres, Custom the .meta's <c>scaleFactor</c>); an older .meta without a unit
        /// conversion has its <c>scaleFactor</c> (tests/projects/speedtree8 Card_Units0, Card_Inches,
        /// Card_Custom: the getter answers the scale the import uses).</summary>
        public float scaleFactor
        {
            get { CheckImporterState("get_scaleFactor"); return ImportScale; }
            set { CheckImporterState("set_scaleFactor"); Unsupported("scaleFactor"); }
        }

        public Color mainColor
        {
            get { CheckImporterState("get_mainColor"); return C("mainColor"); }
            set { CheckImporterState("set_mainColor"); Unsupported("mainColor"); }
        }

        // The deprecated specColor and shininess are plain C# properties, as in Unity's C#: an
        // uncreated importer reads clear and 0 where its other members throw (Unity 6000.0.71f1
        // Editor, tests/projects/defaultstate), and they keep what is set.
        [Obsolete("specColor is no longer used and has been deprecated.", true)]
        public Color specColor { get; set; }

        [Obsolete("shininess is no longer used and has been deprecated.", true)]
        public float shininess { get; set; }

        public Color hueVariation
        {
            get { CheckImporterState("get_hueVariation"); return C("hueVariation"); }
            set { CheckImporterState("set_hueVariation"); Unsupported("hueVariation"); }
        }

        public float alphaTestRef
        {
            get { CheckImporterState("get_alphaTestRef"); return F("alphaTestRef", 0.33f); }
            set { CheckImporterState("set_alphaTestRef"); Unsupported("alphaTestRef"); }
        }

        public bool enableBumpByDefault
        {
            get { CheckImporterState("get_enableBumpByDefault"); return B("enableBumpMapping", true); }
            set { CheckImporterState("set_enableBumpByDefault"); Unsupported("enableBumpByDefault"); }
        }

        public bool enableHueByDefault
        {
            get { CheckImporterState("get_enableHueByDefault"); return B("enableHueVariation"); }
            set { CheckImporterState("set_enableHueByDefault"); Unsupported("enableHueByDefault"); }
        }

        public bool enableSubsurfaceByDefault
        {
            get { CheckImporterState("get_enableSubsurfaceByDefault"); return B("enableSubsurfaceScattering", true); }
            set { CheckImporterState("set_enableSubsurfaceByDefault"); Unsupported("enableSubsurfaceByDefault"); }
        }

        public bool castShadowsByDefault
        {
            get { CheckImporterState("get_castShadowsByDefault"); return B("enableShadowCasting", true); }
            set { CheckImporterState("set_castShadowsByDefault"); Unsupported("castShadowsByDefault"); }
        }

        public bool receiveShadowsByDefault
        {
            get { CheckImporterState("get_receiveShadowsByDefault"); return B("enableShadowReceiving", true); }
            set { CheckImporterState("set_receiveShadowsByDefault"); Unsupported("receiveShadowsByDefault"); }
        }

        public bool useLightProbesByDefault
        {
            get { CheckImporterState("get_useLightProbesByDefault"); return B("enableLightProbes", true); }
            set { CheckImporterState("set_useLightProbesByDefault"); Unsupported("useLightProbesByDefault"); }
        }

        public int reflectionProbeUsagesByDefault
        {
            get { CheckImporterState("get_reflectionProbeUsagesByDefault"); return I("reflectionProbeEnumValue", (int)ReflectionProbeUsage.BlendProbes); }
            set { CheckImporterState("set_reflectionProbeUsagesByDefault"); Unsupported("reflectionProbeUsagesByDefault"); }
        }

        public int bestWindQuality
        {
            get { CheckImporterState("get_bestWindQuality"); return I("bestWindQuality"); }
        }

        public int selectedWindQuality
        {
            get { CheckImporterState("get_selectedWindQuality"); return SelectedWindQuality; }
            set { CheckImporterState("set_selectedWindQuality"); Unsupported("selectedWindQuality"); }
        }

        public bool generateRigidbody
        {
            get { CheckImporterState("get_generateRigidbody"); return B("generateRigidbody", true); }
            set { CheckImporterState("set_generateRigidbody"); Unsupported("generateRigidbody"); }
        }

        public bool generateColliders
        {
            get { CheckImporterState("get_generateColliders"); return B("generateColliders", true); }
            set { CheckImporterState("set_generateColliders"); Unsupported("generateColliders"); }
        }

        public bool hasBillboard
        {
            get { CheckImporterState("get_hasBillboard"); return B("hasBillboard"); }
        }

        public bool enableSmoothLODTransition
        {
            get { CheckImporterState("get_enableSmoothLODTransition"); return B("enableSmoothLODTransition", true); }
            set { CheckImporterState("set_enableSmoothLODTransition"); Unsupported("enableSmoothLODTransition"); }
        }

        public bool animateCrossFading
        {
            get { CheckImporterState("get_animateCrossFading"); return B("animateCrossFading", true); }
            set { CheckImporterState("set_animateCrossFading"); Unsupported("animateCrossFading"); }
        }

        public float billboardTransitionCrossFadeWidth
        {
            get { CheckImporterState("get_billboardTransitionCrossFadeWidth"); return F("billboardTransitionCrossFadeWidth", 0.25f); }
            set { CheckImporterState("set_billboardTransitionCrossFadeWidth"); Unsupported("billboardTransitionCrossFadeWidth"); }
        }

        public float fadeOutWidth
        {
            get { CheckImporterState("get_fadeOutWidth"); return F("fadeOutWidth", 0.25f); }
            set { CheckImporterState("set_fadeOutWidth"); Unsupported("fadeOutWidth"); }
        }

        public bool[] enableSettingOverride
        {
            get { CheckImporterState("get_enableSettingOverride"); return PerLevel(LevelOverrides); }
            set { CheckImporterState("set_enableSettingOverride"); Unsupported("enableSettingOverride"); }
        }

        public float[] LODHeights
        {
            get { CheckImporterState("get_LODHeights"); return PerLevel(l => (float)(l.Get("height")?.AsDouble(0) ?? 0)); }
            set { CheckImporterState("set_LODHeights"); Unsupported("LODHeights"); }
        }

        public bool[] castShadows
        {
            get { CheckImporterState("get_castShadows"); return PerLevel(l => LevelBool(l, "castShadows")); }
            set { CheckImporterState("set_castShadows"); Unsupported("castShadows"); }
        }

        public bool[] receiveShadows
        {
            get { CheckImporterState("get_receiveShadows"); return PerLevel(l => LevelBool(l, "receiveShadows")); }
            set { CheckImporterState("set_receiveShadows"); Unsupported("receiveShadows"); }
        }

        public bool[] useLightProbes
        {
            get { CheckImporterState("get_useLightProbes"); return PerLevel(l => LevelBool(l, "useLightProbes")); }
            set { CheckImporterState("set_useLightProbes"); Unsupported("useLightProbes"); }
        }

        public ReflectionProbeUsage[] reflectionProbeUsages
        {
            get { CheckImporterState("get_reflectionProbeUsages"); return PerLevel(l => (ReflectionProbeUsage)(int)(l.Get("reflectionProbeUsage")?.AsLong(0) ?? 0)); }
            set { CheckImporterState("set_reflectionProbeUsages"); Unsupported("reflectionProbeUsages"); }
        }

        public bool[] enableBump
        {
            get { CheckImporterState("get_enableBump"); return PerLevel(l => LevelBool(l, "enableBump")); }
            set { CheckImporterState("set_enableBump"); Unsupported("enableBump"); }
        }

        public bool[] enableHue
        {
            get { CheckImporterState("get_enableHue"); return PerLevel(l => LevelBool(l, "enableHue")); }
            set { CheckImporterState("set_enableHue"); Unsupported("enableHue"); }
        }

        public bool[] enableSubsurface
        {
            get { CheckImporterState("get_enableSubsurface"); return PerLevel(l => LevelBool(l, "enableSubsurface")); }
            set { CheckImporterState("set_enableSubsurface"); Unsupported("enableSubsurface"); }
        }

        public int[] windQualities
        {
            get { CheckImporterState("get_windQualities"); return PerLevel(l => (int)(l.Get("windQuality")?.AsLong(0) ?? 0)); }
            set { CheckImporterState("set_windQualities"); Unsupported("windQualities"); }
        }

        /// <summary>The level of detail settings the import uses: the level's own when it overrides
        /// them, else the tree-wide ones.</summary>
        internal readonly struct Level
        {
            internal readonly float Height;
            internal readonly bool CastShadows, ReceiveShadows, UseLightProbes, EnableBump, EnableHue, EnableSubsurface;
            internal readonly ReflectionProbeUsage ReflectionProbes;
            internal readonly int WindQuality;
            internal readonly bool Overrides;

            internal Level(SpeedTreeImporter importer, YNode level)
            {
                Height = (float)(level.Get("height")?.AsDouble(0) ?? 0);
                Overrides = LevelOverrides(level);
                // A level that does not override takes the tree's selected wind quality (equal to
                // every measured level's own).
                WindQuality = Overrides ? (int)(level.Get("windQuality")?.AsLong(0) ?? 0) : importer.SelectedWindQuality;
                if (Overrides)
                {
                    CastShadows = LevelBool(level, "castShadows");
                    ReceiveShadows = LevelBool(level, "receiveShadows");
                    UseLightProbes = LevelBool(level, "useLightProbes");
                    ReflectionProbes = (ReflectionProbeUsage)(int)(level.Get("reflectionProbeUsage")?.AsLong(0) ?? 0);
                    EnableBump = LevelBool(level, "enableBump");
                    EnableHue = LevelBool(level, "enableHue");
                    EnableSubsurface = LevelBool(level, "enableSubsurface");
                }
                else
                {
                    CastShadows = importer.B("enableShadowCasting", true);
                    ReceiveShadows = importer.B("enableShadowReceiving", true);
                    UseLightProbes = importer.B("enableLightProbes", true);
                    ReflectionProbes = (ReflectionProbeUsage)importer.I("reflectionProbeEnumValue", (int)ReflectionProbeUsage.BlendProbes);
                    EnableBump = importer.B("enableBumpMapping", true);
                    EnableHue = importer.B("enableHueVariation");
                    EnableSubsurface = importer.B("enableSubsurfaceScattering", true);
                }
            }
        }

        internal Level[] ImportLevels()
        {
            var levels = Levels;
            var result = new Level[levels.Length];
            for (int i = 0; i < result.Length; i++) result[i] = new Level(this, levels[i]);
            return result;
        }

        internal float ImportScale
        {
            get
            {
                if (MetaSettings.Find(m_Settings, "unitConversionEnumValue") == null) return F("scaleFactor", 1f);
                switch (I("unitConversionEnumValue"))
                {
                    case 0: return 1f;
                    case 1: return UnityEditor.SpeedTree.Importer.SpeedTreeConstants.kFeetToMetersRatio;
                    case 2: return UnityEditor.SpeedTree.Importer.SpeedTreeConstants.kCentimetersToMetersRatio;
                    case 3: return UnityEditor.SpeedTree.Importer.SpeedTreeConstants.kInchesToMetersRatio;
                    default: return F("scaleFactor", 1f);
                }
            }
        }

        /// <summary>The settings' file id kind: 0 class number counters, 1 names, 2 hierarchy paths.</summary>
        internal int FileIdType => I("fileIDType");
        internal int MotionVectorMode => I("motionVectorModeEnumValue", (int)MotionVectorGenerationMode.Object);
    }
}
