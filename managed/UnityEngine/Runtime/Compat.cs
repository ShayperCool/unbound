using System;
using System.Globalization;

namespace Unbound.Runtime
{
    /// <summary>
    /// Behaviour that differs between Unity releases, chosen by the project's release (the managed
    /// side of the compatibility profile). Each answer names what was measured.
    /// </summary>
    internal static class Compat
    {
        /// <summary>The project's release's major version (2022, 6000, ...).</summary>
        internal static int Major
        {
            get
            {
                var v = UnityEngine.ProjectInfo.UnityVersion ?? "";
                int dot = v.IndexOf('.');
                return int.TryParse(dot < 0 ? v : v.Substring(0, dot), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 6000;
            }
        }

        /// <summary>Where Input's errors point to its settings: 6000.0's Editor says (measured)
        /// "Edit -> Project Settings... -> Input Manager"; earlier releases "Edit -> Settings -> Input".</summary>
        internal static string InputSettingsMenu => Major >= 6000 ? "Edit -> Project Settings... -> Input Manager" : "Edit -> Settings -> Input";

        /// <summary>Rate batch initialization and birth ages measured by
        /// tests/projects/particlerandom for exactly 6000.0.71f1. Other releases
        /// keep the existing emission path until measured.</summary>
        internal static bool ParticleRateBatchesMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Combined shape modifiers measured by tests/projects/particlerandom.
        /// Other releases retain their existing sampling until measured.</summary>
        internal static bool ParticleShapeModifiersMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Standalone mesh-instancing batch limits measured on 6000.0.71f1 GLCore.
        /// x/y: transform capacities without/with uniform scaling; z: property-buffer bytes.
        /// Other releases keep sequential IDs until measured.</summary>
        internal static UnityEngine.Vector4 MeshInstanceBatchLimits => Release == (6000, 0, 71, 'f', 1)
            ? new UnityEngine.Vector4(511, 1023, 65536, 0) : UnityEngine.Vector4.zero;

        /// <summary>6000.0's BakeTrailsMesh joins ribbon strips in its index count and rejects
        /// the resulting out-of-bounds indices. Regular ribbon rendering is unaffected.</summary>
        internal static bool ParticleRibbonBakeRejectsMultipleStrips => Major >= 6000;

        /// <summary>6000.0.71f1 has no serialized PCM switch and ignores an injected m_EnablePCM
        /// (measured by physicsscenesettings). Older settings documents expose this legacy switch;
        /// their selectable SAT path is documented, but not yet measured by this fixture.</summary>
        internal static bool PhysicsPcmEnabled(bool projectValue) => Major >= 6000 || projectValue;

        /// <summary>The project's release as numbers and stage letter (6000.0.71f1: 6000, 0, 71,
        /// 'f', 1); null when it cannot be read.</summary>
        internal static (int Major, int Minor, int Patch, char Stage, int Build)? Release
        {
            get
            {
                var v = UnityEngine.ProjectInfo.UnityVersion ?? "";
                var parts = v.Split('.');
                if (parts.Length != 3) return null;
                int stageAt = 0;
                while (stageAt < parts[2].Length && char.IsDigit(parts[2][stageAt])) stageAt++;
                if (stageAt == 0 || stageAt >= parts[2].Length - 1) return null;
                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
                    || !int.TryParse(parts[2].Substring(0, stageAt), NumberStyles.None, CultureInfo.InvariantCulture, out var patch)
                    || !int.TryParse(parts[2].Substring(stageAt + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var build))
                    return null;
                return (major, minor, patch, parts[2][stageAt], build);
            }
        }

        /// <summary>Whether the shader keyword registry and keyword types below are measured for the
        /// project's release: exactly 6000.0.71f1 (tests/projects/shaderkeywords
        /// Reference/unity-6000.0.71f1.jsonl). The Rust side gives keyword-space rules to the same
        /// release only (unbound_compat::ShaderKeywordRules). Other patches of the family are not
        /// measured: they keep the name-based behaviour (every name is a global keyword) with a
        /// warning, not an extrapolated table.</summary>
        internal static bool ShaderKeywordsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>The engine's built-in global shader keywords: the ones that exist before any is
        /// made (canonical global.builtin.types and global.all.contains, 6000.0.71f1 Linux Editor).
        /// Registered, not enabled: which keywords start enabled depends on quality, pipeline and
        /// context (none did in the measured batchmode capture) and is not a rule of this table.
        /// Null for a release whose registry is not measured.</summary>
        internal static string[] BuiltinGlobalKeywords => ShaderKeywordsMeasured ? s_BuiltinGlobalKeywords6000_0_71f1 : null;

        static readonly string[] s_BuiltinGlobalKeywords6000_0_71f1 =
        {
            "BILLBOARD_FACE_CAMERA_POS", "DIRECTIONAL", "DIRECTIONAL_COOKIE", "DIRLIGHTMAP_COMBINED", "DOTS_INSTANCING_ON",
            "DYNAMICLIGHTMAP_ON", "EDITOR_VISUALIZATION", "EFFECT_BILLBOARD", "EFFECT_BUMP", "EFFECT_EXTRA_TEX",
            "EFFECT_HUE_VARIATION", "EFFECT_SUBSURFACE", "ETC1_EXTERNAL_ALPHA", "FOG_EXP", "FOG_EXP2", "FOG_LINEAR",
            "GEOM_TYPE_BRANCH", "GEOM_TYPE_BRANCH_DETAIL", "GEOM_TYPE_FROND", "GEOM_TYPE_LEAF", "GEOM_TYPE_MESH",
            "INSTANCING_ON", "LIGHTMAP_ON", "LIGHTMAP_SHADOW_MIXING", "LIGHTPROBE_SH", "LOD_FADE_CROSSFADE", "POINT",
            "POINT_COOKIE", "PROCEDURAL_INSTANCING_ON", "SHADOWS_CUBE", "SHADOWS_DEPTH", "SHADOWS_SCREEN",
            "SHADOWS_SHADOWMASK", "SHADOWS_SINGLE_CASCADE", "SHADOWS_SOFT", "SHADOWS_SPLIT_SPHERES", "SOFTPARTICLES_ON",
            "SPOT", "STEREO_CUBEMAP_RENDER_ON", "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON",
            "UNITY_DEVICE_SUPPORTS_INLINE_RAY_TRACING", "UNITY_DEVICE_SUPPORTS_NATIVE_16BIT",
            "UNITY_DEVICE_SUPPORTS_WAVE_128", "UNITY_DEVICE_SUPPORTS_WAVE_16", "UNITY_DEVICE_SUPPORTS_WAVE_32",
            "UNITY_DEVICE_SUPPORTS_WAVE_64", "UNITY_DEVICE_SUPPORTS_WAVE_8", "UNITY_DEVICE_SUPPORTS_WAVE_ANY",
            "UNITY_HDR_ON", "UNITY_SINGLE_PASS_STEREO", "VERTEXLIGHT_ON", "_EMISSION", "_WINDQUALITY_BEST",
            "_WINDQUALITY_BETTER", "_WINDQUALITY_FAST", "_WINDQUALITY_FASTEST", "_WINDQUALITY_NONE", "_WINDQUALITY_PALM",
        };

        /// <summary>The keywords of a shader's keyword space whose type is built-in (the four stereo
        /// keywords every space starts with; canonical *.space.keywords, 6000.0.71f1). Null for a
        /// release whose types are not measured: every keyword is then user-defined.</summary>
        internal static string[] ImplicitShaderKeywords => ShaderKeywordsMeasured ? s_ImplicitShaderKeywords6000_0_71f1 : null;

        static readonly string[] s_ImplicitShaderKeywords6000_0_71f1 =
        {
            "STEREO_INSTANCING_ON", "UNITY_SINGLE_PASS_STEREO", "STEREO_MULTIVIEW_ON", "STEREO_CUBEMAP_RENDER_ON",
        };
    }
}
