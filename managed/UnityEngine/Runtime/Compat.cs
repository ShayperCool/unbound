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

        /// <summary>Linux Editor display answers measured by inputplatform on 6000.0.71f1.
        /// Screen's resolution choices describe the Editor, while DisplayInfo comes from the OS.</summary>
        internal static bool EditorDisplayMeasured => OperatingSystem.IsLinux() && Release == (6000, 0, 71, 'f', 1);

        // Standard sizes measured on a desktop larger than every size in the list. The smaller
        // captures establish that each dimension is filtered independently against the desktop.
        internal static readonly (int Width, int Height)[] EditorDisplaySizes =
        {
            (640, 480), (720, 480), (768, 480), (800, 480), (854, 480),
            (960, 540), (1024, 576), (800, 600), (1024, 600), (960, 640),
            (1024, 640), (1136, 640), (960, 720), (1152, 720), (1280, 720),
            (1024, 768), (1152, 768), (1280, 768), (1366, 768), (1280, 800),
            (1152, 864), (1280, 864), (1440, 900), (1600, 900), (1280, 960),
            (1440, 960), (1280, 1024), (1600, 1024), (1400, 1050), (1680, 1050),
            (1920, 1080), (2560, 1080), (2048, 1152), (1600, 1200), (1920, 1200),
            (1792, 1344), (1856, 1392), (1920, 1440), (2160, 1440), (2304, 1440),
            (2560, 1440), (3440, 1440), (2048, 1536), (2560, 1600), (2880, 1800),
            (3200, 1800), (2560, 1920), (2560, 2048), (3200, 2048), (3840, 2160),
            (4096, 2160), (5120, 2160), (3200, 2400), (3840, 2400), (3200, 2560),
            (4096, 2560), (5120, 2880), (4096, 3072), (5120, 3200), (5760, 3240),
            (5120, 4096), (6400, 4096), (7680, 4320), (6400, 4800), (7680, 4800),
            (8192, 5120),
        };

        /// <summary>Group input startup fades follow the mixer graph's processing age, including
        /// groups first used later (tests/projects/mixerduck, exactly 6000.0.71f1).</summary>
        internal static bool MixerGraphStartupMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Send/Receive routing, startup and Duck Volume detector state, measured on
        /// exactly 6000.0.71f1 (tests/projects/mixerduck). Other releases fail until measured.</summary>
        internal static bool MixerSidechainMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Default mixer Pitch Shifter DSP measured on exactly 6000.0.71f1.
        /// Other releases fail until their kernels and state have been measured.</summary>
        internal static bool MixerPitchShifterMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Mixer Normalize's linked peak envelope, measured on exactly 6000.0.71f1
        /// (tests/projects/audionormalize). Other releases fail until measured.</summary>
        internal static bool MixerNormalizeMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>The modulated Q14 delay line that mixer Chorus and Flange and AudioChorusFilter
        /// share, measured on exactly 6000.0.71f1 (tests/projects/audiochorus,
        /// tests/projects/audioflange). Other releases fail until measured.</summary>
        internal static bool ModulatedDelayMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>The Distortion waveshaper of the mixer effect and AudioDistortionFilter, measured
        /// on exactly 6000.0.71f1 (tests/projects/audiodistortion). Other releases fail until measured.</summary>
        internal static bool DistortionMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>AudioMixer.GetFloat after ClearFloat, before the next audio update, measured on
        /// exactly 6000.0.71f1 (tests/projects/mixerparams): an override already submitted to audio
        /// still reads until that update. Every release reads the snapshot value for a clear of an
        /// unsubmitted override, as GetFloat documents; other releases read it for a submitted one
        /// too (the exception is unmeasured there; docs/COMPATIBILITY_GAPS.md).</summary>
        internal static bool MixerClearFloatSubmissionMeasured => Release == (6000, 0, 71, 'f', 1);

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

        /// <summary>The release Unbound's rendering parity was measured on (tests/projects/*
        /// Reference/unity-6000.0.71f1). The rendering answers below give its measured behaviour
        /// to exactly that release; the other releases' answers are listed in
        /// docs/COMPATIBILITY_GAPS.md ("Rendering rules measured on one release").</summary>
        internal static bool RenderingMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Light-probe gizmo scale observed outside repaint callbacks in renderapi.</summary>
        internal static float GizmoProbeSize => RenderingMeasured ? 0.299999982f : 0.3f;

        /// <summary>Standalone mesh-instancing batch limits measured on 6000.0.71f1 GLCore.
        /// x/y: transform capacities without/with uniform scaling; z: property-buffer bytes.
        /// Other releases keep sequential IDs until measured.</summary>
        internal static UnityEngine.Vector4 MeshInstanceBatchLimits => RenderingMeasured
            ? new UnityEngine.Vector4(511, 1023, 65536, 0) : UnityEngine.Vector4.zero;

        /// <summary>Frames after a CPU synchronisation GraphicsFence ran until it reports passed
        /// (measured on the 6000.0.71f1 OpenGLCore Editor: tests/projects/cmdbuffers fence.*).
        /// Other releases report it once the GPU has passed it.</summary>
        internal static int GraphicsFenceFrames => RenderingMeasured ? 2 : 0;

        /// <summary>Graphics.RenderPrimitivesIndexed with an instance count of zero draws one
        /// instance (measured on 6000.0.71f1 OpenGLCore: cmdbuffers render.indexed.instances.zero);
        /// other releases draw none, as the count says.</summary>
        internal static bool ZeroIndexedInstancesDrawOne => RenderingMeasured;

        /// <summary>The built-in pipeline's ambient projection of the sky leaves out the main
        /// light's colour (<c>_LightColor0</c> zero; the procedural sky's sun disk then adds
        /// nothing, while its scattering keeps the light's direction). Measured on 6000.0.71f1:
        /// procedural skies with no, simple, high-quality and zero-size disks give the same
        /// ambient probe. That the colour, not only the disk, is left out for every sky shader
        /// is a hypothesis: a sky tinted by <c>_LightColor0</c> is unmeasured
        /// (docs/COMPATIBILITY_GAPS.md). Other releases project the sky as drawn.</summary>
        internal static bool SkyAmbientWithoutLightColor => RenderingMeasured;

        /// <summary>LineRenderer's open-strip joins of positive width as measured by the lines
        /// probe on 6000.0.71f1 (miterrev.*, miterchain.*): near-reversal intersections, the
        /// reach limit from the segment two points back and the un-eased lighting tangent. Other
        /// releases keep the general miter rules.</summary>
        internal static bool LineOpenStripJoinsMeasured => RenderingMeasured;

        /// <summary>Humanoid root motion composed by mixer trees (any input count and nesting,
        /// input weights summing to one in each mixer, weights changed between evaluations),
        /// measured by animculling humanMixerMotion/humanMixerShape on exactly 6000.0.71f1.
        /// Other releases keep the unsupported diagnostic.</summary>
        internal static bool HumanPlayableMixerMotionMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>After skipped updates, a visibility return replays the last event intervals of
        /// clips that a mixer or layer mixer combined (at any depth), but not of a clip that is the
        /// output's source or is reached only through passthrough playables (6000.0.71f1,
        /// animculling explicit/later and explicit/eventShape). Other releases fire no events on
        /// visibility return, as the culling documentation describes.</summary>
        internal static bool PlayableEventsReplayOnVisibility => Release == (6000, 0, 71, 'f', 1);

        /// <summary>A CullCompletely-culled Animator holds only the animation playables of its
        /// outputs' trees (and what is below them); plain and script playables above them keep the
        /// graph's clock. Measured by tests/projects/animculling clock/ on exactly 6000.0.71f1.
        /// Other releases (unmeasured) hold each culled output's whole source tree, as Unbound did
        /// before, but with the measured hold itself: it wins over other outputs reaching a held
        /// playable and pauses held behaviours instead of preparing them with a zero step.</summary>
        internal static bool PlayableCullingHoldsAnimationPlayablesOnly => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Animator automatic Play-entry timing and startup controls measured by
        /// tests/projects/animstartup in exactly 6000.0.71f1's Linux Editor. Other releases
        /// retain the existing behavior until measured.</summary>
        internal static bool AnimatorStartupMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Root-space skinned bounds, single-bone defaults and cached public world
        /// bounds measured by skinnedbounds in the Linux 6000.0.71f1 Editor.</summary>
        internal static bool SkinnedBoundsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Birth sub-emitter origins and Simulate ages measured by
        /// tests/projects/particlerandom. Other releases retain their previous path.</summary>
        internal static bool ParticleBirthStepsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Start lifetimes take a reciprocal estimate with two Newton corrections
        /// instead of a correctly rounded division, measured by tests/projects/particlerandom
        /// (birthclock/, model/clock/). Other releases keep the division until measured.</summary>
        internal static bool ParticleLifetimeReciprocalEstimate => Release == (6000, 0, 71, 'f', 1);

        /// <summary>6000.0's BakeTrailsMesh joins ribbon strips in its index count and rejects
        /// the resulting out-of-bounds indices. Regular ribbon rendering is unaffected.</summary>
        internal static bool ParticleRibbonBakeRejectsMultipleStrips => Major >= 6000;

        /// <summary>6000.0.71f1 has no serialized PCM switch and ignores an injected m_EnablePCM
        /// (measured by physicsscenesettings). Older settings documents expose this legacy switch;
        /// their selectable SAT path is documented, but not yet measured by this fixture.</summary>
        internal static bool PhysicsPcmEnabled(bool projectValue) => Major >= 6000 || projectValue;

        /// <summary>Hinge solver limit clamping and contact padding measured by physicsccdhinge in 6000.0.71f1.
        /// Other releases keep the previous unclamped limits and 0.01-radian default pad.</summary>
        internal static bool HingeLimitClampingMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>The project's release as numbers and stage letter (6000.0.71f1: 6000, 0, 71,
        /// 'f', 1); null when it cannot be read.</summary>
        internal static (int Major, int Minor, int Patch, char Stage, int Build)? Release
        {
            get
            {
                // Parsed once per version string: hot paths (per draw, per particle) ask. The
                // cache is replaced whole, so another thread sees a consistent pair.
                var v = UnityEngine.ProjectInfo.UnityVersion ?? "";
                var cached = s_Release;
                if (cached != null && string.Equals(cached.Version, v, StringComparison.Ordinal)) return cached.Parsed;
                var parsed = Parse(v);
                s_Release = new ParsedRelease(v, parsed);
                return parsed;
            }
        }

        sealed class ParsedRelease
        {
            internal readonly string Version;
            internal readonly (int Major, int Minor, int Patch, char Stage, int Build)? Parsed;
            internal ParsedRelease(string version, (int, int, int, char, int)? parsed) { Version = version; Parsed = parsed; }
        }

        static ParsedRelease s_Release;

        static (int Major, int Minor, int Patch, char Stage, int Build)? Parse(string v)
        {
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

        /// <summary>Whether the shader keyword registry and keyword types below are measured for the
        /// project's release: exactly 6000.0.71f1 (tests/projects/shaderkeywords
        /// Reference/unity-6000.0.71f1.jsonl). The Rust side gives keyword-space rules to the same
        /// release only (unbound_compat::ShaderKeywordRules). Other patches of the family are not
        /// measured: they keep the name-based behaviour (every name is a global keyword) with a
        /// warning, not an extrapolated table.</summary>
        internal static bool ShaderKeywordsMeasured => RenderingMeasured;

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
