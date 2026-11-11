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
        /// <summary>Legacy dynamic fonts: per-character OS fallback through the font names and the fallback families, and the built-in font without font data and Font.HasCharacter of dynamic fonts, measured in tests/projects/glyphraster.</summary>
        internal static bool LegacyGlyphRasterMeasured => Release == (6000, 0, 71, 'f', 1);
        /// <summary>Constructor, animation-job API contexts and component initialization defaults measured in tests/projects/threadapis.</summary>
        internal static bool MainThreadContextsMeasured => Release == (6000, 0, 71, 'f', 1);
        /// <summary>Invoke deadlines and delayed-call points measured in tests/projects/invokesteps.</summary>
        internal static bool InvokeTimingMeasured => Release == (6000, 0, 71, 'f', 1);
        /// <summary>Separate preparation and activation of scene operations, measured by
        /// tests/projects/editorcallbacks/SceneLoop on Unity 6000.0.71f1.</summary>
        internal static bool SceneAsyncPreparationMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Pending Starts without an earlier callback visit run at the PostLateUpdate
        /// scripting point, measured by editorcallbacks/SceneLoop on Unity 6000.0.71f1.</summary>
        internal static bool PendingStartsAfterLateUpdate => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Camera dependency repair measured by
        /// flarelayerdependency in Unity 6000.0.71f1.</summary>
        internal static bool FlareCameraRepairMeasured => Release == (6000, 0, 71, 'f', 1);
        internal static bool FlareCameraRepairMeasuredFor(string release) => Parse(release ?? "") == (6000, 0, 71, 'f', 1);

        /// <summary>Deferred object lifetime at delayed scripting points, measured in
        /// tests/projects/destroyphases on Unity 6000.0.71f1.</summary>
        internal static bool DestroyPhasesMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Automatic rendering follows the public player loop and precedes end-of-frame
        /// coroutine resumes, measured by destroyphases' rendering controls on 6000.0.71f1.</summary>
        internal static bool RenderingAfterPublicLoop => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Simulation mode checks, contact refresh across sub-steps and contact payload
        /// sampling/impulse accumulation, measured by physics2dadvanced and physics2ddynamics
        /// on Unity 6000.0.71f1.</summary>
        internal static bool Physics2DSimulationMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Slide contact spacing, iteration accounting and argument validation,
        /// measured by physics2dadvanced on Unity 6000.0.71f1.</summary>
        internal static bool Physics2DSlideMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>The fixed scheduler uses the same whole-tick time domain as the frame clock,
        /// measured by physics2dadvanced automatic modes on Unity 6000.0.71f1.</summary>
        internal static bool FixedClockTicksMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>A scene without lighting data whose RenderSettings an earlier release saved
        /// with a new scene's environment takes the built-in lighting data, measured in
        /// tests/projects/realtimegi (legacy*) on Unity 6000.0.71f1. Other releases keep such a
        /// scene without lighting data until measured.</summary>
        internal static bool EarlierDefaultSceneLightingMeasured => Release == (6000, 0, 71, 'f', 1);

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

        /// <summary>Old GUILayer documents are discarded on scene load in the measured release
        /// (tests/projects/classids). Other releases retain the unsupported-class diagnostic.</summary>
        internal static bool RemovesLegacyGuiLayer(string release) => Parse(release ?? "") == (6000, 0, 71, 'f', 1);

        /// <summary>Asset resource ownership and clone diagnostics measured by assetinstantiate
        /// in 6000.0.71f1. Other releases require their own measurement.</summary>
        internal static bool AssetCloneResourcesMeasured => Release == (6000, 0, 71, 'f', 1);

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

        /// <summary>Static effect-slot Wet mixing measured on 6000.0.71f1 by the five
        /// audio effect probes. Other releases keep rejecting this unmeasured mechanism.</summary>
        internal static bool MixerStaticWetMeasured => Release == (6000, 0, 71, 'f', 1);


        /// <summary>The Editor starts with its saved mixer snapshot selection, measured on
        /// exactly 6000.0.71f1 (tests/projects/mixersuspend). Other releases retain the start
        /// snapshot until their Editor selection behavior is measured.</summary>
        internal static bool MixerSnapshotSelectionMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Trailing attenuation submits gain directly; interior attenuation ramps per
        /// frame. Measured on exactly 6000.0.71f1 (tests/projects/mixersuspend, mixerduck).
        /// Other releases retain their unmeasured gain ramp.</summary>
        internal static bool MixerParameterGainMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>AudioSettings.Reset invalidates generated clip data and reloads
        /// imported data once per shared resource. Measured on exactly 6000.0.71f1
        /// (tests/projects/mixersuspend); clone ownership/loading uses AssetCloneResourcesMeasured.
        /// Other releases retain their existing clip data until measured.</summary>
        internal static bool AudioClipResetDataMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Closing offline capture for a reset processes the old output buffer,
        /// measured on 6000.0.71f1 by tests/projects/mixersuspend's reset delivery control.
        /// Other releases retain their unmeasured reset delivery.</summary>
        internal static bool AudioResetCaptureTailMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Script-created clip duration uses whole milliseconds, measured on exactly
        /// 6000.0.71f1 (tests/projects/mixersuspend). Imported duration remains sample-based.
        /// Other releases retain sample-based duration until measured.</summary>
        internal static bool ScriptAudioClipDurationMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Decoded resampling uses a single-precision rate ratio and holds the final
        /// frame until non-looping playback ends,
        /// measured on exactly 6000.0.71f1 (tests/projects/mixersuspend). Other releases retain
        /// a double rate ratio and zero padding until measured; looping still wraps.</summary>
        internal static bool DecodedAudioResamplingMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Destroy requests stop AudioSource voices before the deferred object
        /// destruction, including sources below a destroyed GameObject. Measured on exactly
        /// 6000.0.71f1 (tests/projects/mixersuspend). Other releases retain end-of-frame cleanup.</summary>
        internal static bool AudioSourceDestroyRequestMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>A streamed clip's cached buffer is owned by the most recent voice
        /// that starts it; stopping its source releases the buffer. Measured on exactly
        /// 6000.0.71f1 (tests/projects/mixersuspend). Other releases keep their previous
        /// loading and stop behavior until measured.</summary>
        internal static bool StreamingClipBufferLifetimeMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Send/Receive routing, startup and Duck Volume detector state, measured on
        /// exactly 6000.0.71f1 (tests/projects/mixerduck). Other releases fail until measured.</summary>
        internal static bool MixerSidechainMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Linear scaled-time Send and Duck Volume snapshot changes, measured by
        /// tests/projects/mixerduck. Unscaled-time transitions remain unmeasured.</summary>
        internal static bool MixerTimedSidechainMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Normalized snapshot progress accumulation, measured by the live public
        /// parameter trace in tests/projects/audiodistortion. Other releases retain elapsed-time division.</summary>
        internal static bool MixerSnapshotProgressMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Single-snapshot scalar curves and linear weighted-array transitions,
        /// measured by tests/projects/mixerduck on exactly 6000.0.71f1.</summary>
        internal static bool MixerSnapshotCurvesMeasured => Release == (6000, 0, 71, 'f', 1);

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

        /// <summary>Block-constant effect parameters applying the measured history rules,
        /// measured on 6000.0.71f1 by tests/projects/audiodistortion's live profile.</summary>
        internal static bool MixerEffectTransitionsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Shared effect-slot wet/dry gains over 64 frames, measured on 6000.0.71f1
        /// by tests/projects/audiodistortion's live profile. Other releases keep static slots.</summary>
        internal static bool MixerWetTransitionsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Component tails end one DSP buffer after the last voice's containing
        /// buffer, including partial device periods (tests/projects/audioreverb, 6000.0.71f1).
        /// Other releases retain their previous render-chunk frontier until measured.</summary>
        internal static bool AudioComponentTailMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Echo component delay converts to unsigned whole milliseconds before the shared
        /// mixer kernel's frame conversion (tests/projects/audiodistortion, 6000.0.71f1).
        /// Other releases retain their previous float setter and 10 ms DSP minimum until measured.</summary>
        internal static bool AudioComponentEchoDelayMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Listener component ordering and per-source bypass, measured on exactly
        /// 6000.0.71f1 by tests/projects/audiodistortion. Other releases retain reverb-only
        /// listener support and their prior bypassEffects routing until measured.</summary>
        internal static bool AudioListenerChainMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Mono/stereo source mapping, gain ramps, distance curves, relative-velocity
        /// Doppler and interpolation lookahead measured by tests/projects/audiodistortion on
        /// 6000.0.71f1. Other releases retain the previous approximate matrix, sampled curves,
        /// inverse-source-velocity Doppler and max-distance clamp until measured.</summary>
        internal static bool AudioSpatialStereoMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>A live bypassEffects change deactivates the source's filter components and the
        /// first one after a clip starts playing reconnects the chain, measured by the
        /// tests/projects/audiodistortion spatial-script live bypass controls on 6000.0.71f1.
        /// Other releases keep components frozen on the source's bypass flag.</summary>
        internal static bool AudioComponentActivationMeasured => Release == (6000, 0, 71, 'f', 1);

        // Per-voice zone send placement and completion (tests/projects/audioreverb).
        internal static bool AudioZoneVoiceSendMeasured => Release == (6000, 0, 71, 'f', 1);

        // First zone processing defaults (tests/projects/audioreverb).
        internal static bool AudioZoneStartupMeasured => Release == (6000, 0, 71, 'f', 1);

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

        /// <summary>Start batches (a step's rate and burst births, and Emit(count)): their
        /// initialization, birth ages and removal of births that die at their start, measured
        /// by tests/projects/particlerandom and particlemeshshape texture/clip/* for exactly
        /// 6000.0.71f1. Other releases keep the per-particle emission path until measured.</summary>
        internal static bool ParticleRateBatchesMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Combined shape modifiers measured by tests/projects/particlerandom.
        /// Other releases retain their existing sampling until measured.</summary>
        internal static bool ParticleShapeModifiersMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Batch physics queries use the last synchronized transforms even with
        /// autoSyncTransforms enabled (physicsbatch, 6000.0.71f1). Other releases retain automatic
        /// preparation until measured.</summary>
        internal static bool PhysicsBatchRetainsSyncedTransforms => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Terrain tree material bindings retain their creation material, measured in
        /// tests/projects/physicsbatch on 6000.0.71f1. Other releases keep assignment propagation
        /// until their binding lifetime is measured.</summary>
        internal static bool TerrainTreeMaterialRetainsCreationBinding => Release == (6000, 0, 71, 'f', 1);

        /// <summary>The release Unbound's rendering parity was measured on (tests/projects/*
        /// Reference/unity-6000.0.71f1). The rendering answers below give its measured behaviour
        /// to exactly that release; the other releases' answers are listed in
        /// docs/COMPATIBILITY_GAPS.md ("Rendering rules measured on one release").</summary>
        internal static bool RenderingMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>A render pass subpass with several color outputs of which exactly one clears
        /// (its load action, first use in the pass) clears every output it binds to that color on
        /// the Editor's OpenGL Core device; two or more clear each to its own color and loading
        /// ones are kept (tests/projects/urp subpass_mrt, subpass_mrt_cleared, subpass_mrt_mixed).
        /// Other releases: each clearing output to its own color, the others kept.</summary>
        internal static bool SingleSubpassClearFillsOutputs => RenderingMeasured;

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

        /// <summary>The automatic Animator update split over two player-loop leaves (state machines,
        /// events and movement in DirectorUpdateAnimationBegin; pose writes and IK passes, layer by
        /// layer over all Animators, in DirectorUpdateAnimationEnd; the fixed-step leaves likewise),
        /// measured by tests/projects/animcallbacks on exactly 6000.0.71f1
        /// (<see cref="AnimationPhases"/>). Other releases keep Unbound's earlier single pass in the
        /// Begin leaf (unmeasured there).</summary>
        internal static bool AnimationUpdateSplitMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Humanoid root motion composed by mixer trees (any input count and nesting, any
        /// input weight sum blended unnormalized, weights changed between evaluations or while
        /// culled), measured by animculling humanMixerMotion/humanMixerShape on exactly
        /// 6000.0.71f1. Other releases keep the unsupported diagnostic.</summary>
        internal static bool HumanPlayableMixerMotionMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Humanoid root motion and bodies composed by AnimationLayerMixerPlayables over
        /// humanoid clips and mixers (override layers by weight through their masks' body parts,
        /// additive layers leaving the motion), measured by tests/projects/humanoid mixer.* on exactly
        /// 6000.0.71f1. Other releases keep the unsupported diagnostic.</summary>
        internal static bool HumanPlayableLayerMixerMotionMeasured => Release == (6000, 0, 71, 'f', 1);

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
        /// playable and pauses held behaviours instead of preparing them with a zero step.
        /// The same measured release also re-blends a held clip/mixer tree whose weights changed
        /// while culled at the visibility return (animculling humanMixerShape/live,
        /// genericMixerSum/liveCulled); other releases replay the last blend and warn.</summary>
        internal static bool PlayableCullingHoldsAnimationPlayablesOnly => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Animator automatic Play-entry timing and startup controls measured by
        /// tests/projects/animstartup in exactly 6000.0.71f1's Linux Editor, and the time of a state
        /// a Play or CrossFade request enters (held for the update it enters in: animstartup's Play
        /// offsets, humanoid playState and crossFade*). Other releases retain the existing behavior
        /// (the entered state advances) until measured.</summary>
        internal static bool AnimatorStartupMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Root-space skinned bounds, single-bone defaults and cached public world
        /// bounds measured by skinnedbounds in the Linux 6000.0.71f1 Editor.</summary>
        internal static bool SkinnedBoundsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Birth sub-emitter origins, emission intervals and ages measured by
        /// tests/projects/particlerandom (Simulate) and tests/projects/particlerender birthstart/*
        /// (the frame's update). Other releases retain their previous path.</summary>
        internal static bool ParticleBirthStepsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Sub-emitters of every type play from each update of their playing parent and
        /// stop at the end of a frame that leaves them empty, measured by
        /// tests/projects/particlerender birthstart/* and subemitters played/* on 6000.0.71f1.
        /// Other releases keep the previous rule (a child plays once it receives particles).</summary>
        internal static bool ParticleSubEmitterStartMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Prewarm simulates the longest start lifetime plus the sub-emitter chains'
        /// lifetimes in one call ending on a loop's end, keeps the clock where it ends and emits
        /// sub-emitter children within their lifetime of the end without aging them, measured by
        /// tests/projects/particlerender prewarm/* on 6000.0.71f1. Other releases keep the previous
        /// model (one longest lifetime from time 0, then time 0) until measured.</summary>
        internal static bool ParticlePrewarmMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Start lifetimes take a reciprocal estimate with two Newton corrections
        /// instead of a correctly rounded division, measured by tests/projects/particlerandom
        /// (birthclock/, model/clock/). Other releases keep the division until measured.</summary>
        internal static bool ParticleLifetimeReciprocalEstimate => Release == (6000, 0, 71, 'f', 1);

        /// <summary>BakeTexture of mesh particles whose bake has a 16-bit index buffer fills the
        /// indices texture with that buffer read as 32-bit words (two indices a texel), measured
        /// by tests/projects/particlerender meshindex/* and bake/mesh on 6000.0.71f1. Other
        /// releases write one index a texel, as for billboards, until measured.</summary>
        internal static bool ParticleMeshBakeTextureReadsIndexPairs => Release == (6000, 0, 71, 'f', 1);

        /// <summary>6000.0's BakeTrailsMesh joins ribbon strips in its index count and rejects
        /// the resulting out-of-bounds indices. Regular ribbon rendering is unaffected.</summary>
        internal static bool ParticleRibbonBakeRejectsMultipleStrips => Major >= 6000;

        /// <summary>6000.0.71f1 has no serialized PCM switch and ignores an injected m_EnablePCM
        /// (measured by physicsscenesettings). Older settings documents expose this legacy switch;
        /// their selectable SAT path is documented, but not yet measured by this fixture.</summary>
        internal static bool PhysicsPcmEnabled(bool projectValue) => Major >= 6000 || projectValue;

        /// <summary>Contact point requests and compound collision-message routing, measured by
        /// contactmodify3d in 6000.0.71f1. Other releases retain unconditional point reporting and
        /// collider-plus-body delivery until
        /// measured (docs/COMPATIBILITY_GAPS.md).</summary>
        internal static bool SelectiveContactReportsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>ContinuousDynamic also sweeps Discrete dynamic targets, measured by
        /// physicsccdhinge geometry-controls in 6000.0.71f1. Other releases retain the previous
        /// exclusion until their pair eligibility is measured.</summary>
        internal static bool CcdDiscreteDynamicTargetsMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Constrained velocity assignments and dynamic pose publication,
        /// measured by physicsccdhinge on 6000.0.71f1. Other releases remain unmeasured.</summary>
        internal static bool ConstrainedBodyStateMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Convex collider-local cooking and pose separation measured by physicsccdhinge
        /// on 6000.0.71f1. Other releases retain body-frame cooking until measured.</summary>
        internal static bool ConvexColliderFrameMeasured => Release == (6000, 0, 71, 'f', 1);

        /// <summary>Collider updates, attachment transfers, static mesh actor replacement and
        /// idle-scene lifetime measured by physicsccdhinge in 6000.0.71f1.</summary>
        internal static bool ColliderLifetimeMeasured => Release == (6000, 0, 71, 'f', 1);

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

        /// <summary>The standalone ray tracing shader's main object, measured for distinct source
        /// names in cmdbuffers on 6000.0.71f1. This import has no disk cache.</summary>
        internal static long RayTracingShaderFileId => RenderingMeasured ? 4807578003741378534L
            : throw new NotSupportedException("Ray tracing shader asset identifiers are not measured for this Unity release.");

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
