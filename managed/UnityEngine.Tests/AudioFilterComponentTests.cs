using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AudioLowPassFilter, AudioHighPassFilter and AudioEchoFilter on an AudioSource. Documented
    /// (Unity 6000.0): defaults and ranges of the Scripting API; components apply in component order
    /// and only while enabled (Manual, Use audio filters); an OnAudioFilterRead script sits at its
    /// own place in that order (Scripting API); AudioLowPassFilter.cutoffFrequency clamps to 10 to
    /// 22000 on set (its Scripting API page). Unbound's choices (unmeasured): the other properties
    /// keep what was set while the descriptor clamps to the documented ranges; chains Unbound cannot place
    /// (a component between two scripts, one above a clipless generator script), a custom cutoff
    /// curve and unread serialized fields fail loudly. Listener component order is measured by
    /// tests/projects/audiodistortion; listener scripts and components are measured in audiopitch. The wire
    /// layout is NativeApi 18's (added at 16), checked against the same offsets in
    /// crates/unbound-runtime/src/audio/protocol.rs. These tests do not measure the DSP; the Rust
    /// mixer tests check kernel wiring; the named probes qualify their measured component paths.
    /// </summary>
    public sealed unsafe class AudioFilterComponentTests : IDisposable
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly List<string> m_Errors = new List<string>();
        readonly List<string> m_Warnings = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();
        // Chorus and Distortion are measured on this release (Compat).
        readonly string m_Release = ProjectInfo.UnityVersion;

        [Theory]
        [InlineData(AudioSpeakerMode.Mono, 0f, false)]
        [InlineData(AudioSpeakerMode.Stereo, 1f, false)]
        [InlineData(AudioSpeakerMode.Stereo, 0f, true)]
        public void SixInputPlaybackRefusesUnmeasuredContexts(AudioSpeakerMode mode, float blend, bool stream)
        {
            var config = AudioSettings.GetConfiguration();
            config.speakerMode = mode;
            Assert.True(AudioSettings.Reset(config));
            var source = Source("Six input admission");
            source.playOnAwake = false;
            source.spatialBlend = blend;
            var clip = stream ? AudioClip.Create("Six input stream", 1024, 6, 48000, true, data => Array.Clear(data))
                : AudioClip.Create("Six input", 1024, 6, 48000, false);
            try
            {
                source.clip = clip;
                Assert.Equal(AudioBackend.SixInputUnsupportedMessage, Assert.Throws<NotSupportedException>(() => source.Play()).Message);
                Assert.Throws<NotSupportedException>(() => source.PlayScheduled(1));
                Assert.Throws<NotSupportedException>(() => source.PlayOneShot(clip));
                Assert.Equal(AudioVoiceState.None, m_Backend.State(source.Handle).Main);
                Assert.Equal(0, m_Backend.State(source.Handle).OneShots);
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Fact]
        public void SixInputAdmissionUsesTheActiveDriverLayout()
        {
            var config = AudioSettings.GetConfiguration();
            config.speakerMode = AudioSpeakerMode.Quad;
            Assert.True(AudioSettings.Reset(config));
            Assert.Equal(AudioSpeakerMode.Stereo, m_Backend.ActiveSpeakerMode);
            var source = Source("Clamped six input layout");
            source.playOnAwake = false;
            var clip = AudioClip.Create("Six input", 1024, 6, 48000, false);
            try
            {
                source.clip = clip;
                source.Play();
                Assert.Equal(AudioVoiceState.Playing, m_Backend.State(source.Handle).Main);
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LeavingSixInputScopeStopsOnlyThoseVoicesAndReportsOnce(bool curve)
        {
            var source = Source("Live six input admission");
            source.playOnAwake = false;
            var six = AudioClip.Create("Six input", 1024, 6, 48000, false);
            var stereo = AudioClip.Create("Stereo control", 1024, 2, 48000, false);
            try
            {
                source.clip = six;
                source.Play();
                source.PlayOneShot(six);
                source.PlayOneShot(stereo);
                if (curve) source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1)));
                else source.spatialBlend = 1;
                source.Flush(always: true);
                Assert.Equal(AudioVoiceState.None, m_Backend.State(source.Handle).Main);
                Assert.Equal(1, m_Backend.State(source.Handle).OneShots);
                Assert.Single(Errors("six-channel playback"));
                source.Flush(always: true);
                Assert.Single(Errors("six-channel playback"));
                source.PlayOneShot(stereo);
                Assert.Equal(2, m_Backend.State(source.Handle).OneShots);
            }
            finally { Object.DestroyImmediate(six); Object.DestroyImmediate(stereo); }
        }

        [Fact]
        public void SixInputStreamingLoadMetadataIsRejectedBeforePlayback()
        {
            var source = Source("Streamed six input metadata");
            source.playOnAwake = false;
            var clip = AudioClip.Create("Six input metadata", 1024, 6, 48000, false);
            try
            {
                clip.m_LoadType = AudioClipLoadType.Streaming;
                source.clip = clip;
                Assert.Throws<NotSupportedException>(() => source.Play());
                Assert.Throws<NotSupportedException>(() => source.PlayOneShot(clip));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        static int s_CommandStatus, s_UpdateStatus;
        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        static int CommandStatus(ulong handle, AudioSourceCommand* command) => s_CommandStatus;
        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        static int UpdateStatus(ulong handle, AudioSourcePacket* packet) => s_UpdateStatus;

        [Fact]
        public void NativePlaybackStatusBecomesAnExceptionOrLiveDiagnostic()
        {
            var apiField = typeof(Native).GetField("s_Api", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var readyField = typeof(Native).GetField("s_Ready", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var previousApi = (NativeApi)apiField.GetValue(null);
            bool previousReady = (bool)readyField.GetValue(null);
            try
            {
                var api = new NativeApi { Version = NativeApi.CurrentVersion, AudioSourceCommand = &CommandStatus, AudioUpdateSource = &UpdateStatus };
                Native.Initialize(&api);
                var backend = new NativeAudioBackend();
                s_CommandStatus = -1;
                Assert.Throws<NotSupportedException>(() => backend.Command(1, new AudioSourceCommand()));
                s_CommandStatus = 0;
                Assert.False(backend.Command(1, new AudioSourceCommand()));
                s_CommandStatus = 1;
                Assert.True(backend.Command(1, new AudioSourceCommand()));
                var packet = new AudioSourcePacket();
                s_UpdateStatus = -1;
                backend.UpdateSource(1, ref packet);
                Assert.Single(Errors("six-channel playback"));
                s_UpdateStatus = 0;
                backend.UpdateSource(1, ref packet);
                Assert.Single(Errors("six-channel playback"));
            }
            finally { apiField.SetValue(null, previousApi); readyField.SetValue(null, previousReady); }
        }

        public AudioFilterComponentTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            AudioSystem.ResetForTests();
            AudioBackend.Use(m_Backend);
            m_PreviousHandler = m_Logger.logHandler;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Errors, m_Warnings);
        }

        public void Dispose()
        {
            m_Logger.logHandler = m_PreviousHandler;
            foreach (var go in m_Objects)
                if (go != null) Object.DestroyImmediate(go);
            AudioSystem.ResetForTests();
            ProjectInfo.UnityVersion = m_Release;
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Errors, m_Warnings;

            internal CaptureHandler(ILogHandler inner, List<string> errors, List<string> warnings)
            {
                m_Inner = inner;
                m_Errors = errors; m_Warnings = warnings;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    lock (m_Errors) m_Errors.Add(message);
                }
                if (type == LogType.Warning) m_Warnings.Add(string.Format(format, args));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        /// <summary>Errors of this file's components (other tests may log at the same time).</summary>
        List<string> Errors(string about)
        {
            lock (m_Errors) return m_Errors.FindAll(e => e.Contains(about));
        }

        sealed class FilterScript : MonoBehaviour
        {
            void OnAudioFilterRead(float[] data, int channels) { }
        }

        // One audio frame without advancing the virtual device clock, so short clips keep
        // playing however long the previous frame took.
        static void UpdateWithoutAdvancing()
        {
            double previous = Time.s_RealDeltaTime;
            Time.s_RealDeltaTime = 0;
            try { AudioSystem.Update(); }
            finally { Time.s_RealDeltaTime = previous; }
        }

        AudioSource Source(string name)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            return go.AddComponent<AudioSource>();
        }

        AudioFilterDescriptor[] Collected(AudioSource source, bool generator = false)
        {
            int count = AudioFilterChain.Collect(source, generator, ref source.m_FilterTable);
            return count == 0 ? Array.Empty<AudioFilterDescriptor>() : source.m_FilterTable.AsSpan(0, count).ToArray();
        }

        static ulong Id(Component c) => unchecked((ulong)(long)c.GetInstanceID());

        [Fact]
        public void PacketAndDescriptorLayoutsUnchangedInNativeApi60()
        {
            // Added at 16; 20 adds reverb parameters, 21 the frame zone; 22 adds render commands,
            // 23 adds Distortion, 24 adds Chorus, 25-27 mixer effects, 28 render protocol 12, 29
            // frame input versions, 30 the decoded-resampling flag and 31 the imported texture
            // format, 32 mesh colors, 33 component tail timing, 34 the instance multiplier, 35 deferred render-target mips, 36 source-loop listener poses 37 the downloaded audio decoder 38 prepared callback buffers and 39 retained MSAA resolves and 40 parameterised mixer Pitch Shifter FFT size, 41 native rendering plugins, 42 legacy glyph bearings, 43 the zone startup frame flag, 44 Pitch Shifter Max channels, 45 realtime GI, 46 driver capabilities, 47 component activation flags, 48 the realtime GI update threshold, 49 realtime GI light probes, 50 the screen's stand-in, 51 the realtime GI environment and 52 directional realtime lightmaps; 60 BatchRendererGroup draws; 61 scripts' profiler markers for native plugins; 63 TextCore glyph loads; 65 mesh instances' call counts.
            Assert.Equal(76u, NativeApi.CurrentVersion);
            var frame = default(AudioFramePacket);
            byte* f = (byte*)&frame;
            Assert.Equal(68, (int)((byte*)&frame.ListenerPlan - f));
            Assert.Equal((64, 72, 80, 176), ((int)((byte*)&frame.FilterCount - f), (int)((byte*)&frame.Filters - f),
                (int)((byte*)&frame.Zone - f), sizeof(AudioFramePacket)));
            var p = default(AudioSourcePacket);
            byte* b = (byte*)&p;
            Assert.Equal(40, (int)((byte*)&p.Volume - b));
            Assert.Equal(84, (int)((byte*)&p.Flags - b));
            Assert.Equal(96, (int)((byte*)&p.RolloffCurve - b));
            Assert.Equal(112, (int)((byte*)&p.SpreadCurve - b));
            Assert.Equal(120, (int)((byte*)&p.FilterCount - b));
            Assert.Equal(128, (int)((byte*)&p.Filters - b));
            Assert.Equal(184, sizeof(AudioSourcePacket));
            Assert.Equal(136, Marshal.OffsetOf<AudioSourcePacket>(nameof(AudioSourcePacket.ListenerPosition)).ToInt32());
            Assert.Equal(148, Marshal.OffsetOf<AudioSourcePacket>(nameof(AudioSourcePacket.ListenerForward)).ToInt32());
            Assert.Equal(160, Marshal.OffsetOf<AudioSourcePacket>(nameof(AudioSourcePacket.ListenerUp)).ToInt32());
            Assert.Equal(172, Marshal.OffsetOf<AudioSourcePacket>(nameof(AudioSourcePacket.DopplerListenerPosition)).ToInt32());
            var d = default(AudioFilterDescriptor);
            byte* e = (byte*)&d;
            Assert.Equal(96, sizeof(AudioFilterDescriptor));
            Assert.Equal(new[] { 8, 12, 16, 20, 24, 28, 32, 36, 40 }, new[]
            {
                (int)((byte*)&d.Kind - e), (int)((byte*)&d.Stage - e), (int)((byte*)&d.Cutoff - e), (int)((byte*)&d.Resonance - e),
                (int)((byte*)&d.Delay - e), (int)((byte*)&d.Decay - e), (int)((byte*)&d.DryMix - e), (int)((byte*)&d.WetMix - e),
                (int)((byte*)d.Reverb - e),
            });
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, new[] { AudioFilterKind.LowPass, AudioFilterKind.HighPass, AudioFilterKind.Echo, AudioFilterKind.Reverb, AudioFilterKind.Distortion, AudioFilterKind.Chorus });
            Assert.Equal(new[] { 0, 1 }, new[] { AudioFilterStage.BeforeScripts, AudioFilterStage.AfterScripts });
            Assert.Equal(64, AudioFilterDescriptor.MaxPerSource);
        }

        [Fact]
        public void ComponentsStartAtTheScriptingApiDefaults()
        {
            var go = Source("defaults").gameObject;
            var lowPass = go.AddComponent<AudioLowPassFilter>();
            var highPass = go.AddComponent<AudioHighPassFilter>();
            var echo = go.AddComponent<AudioEchoFilter>();
            var legacyLow = typeof(AudioLowPassFilter).GetProperty("lowpassResonaceQ");
            var legacyHigh = typeof(AudioHighPassFilter).GetProperty("highpassResonaceQ");
            Assert.Equal((5000f, 1f, 1f), (lowPass.cutoffFrequency, lowPass.lowpassResonanceQ, (float)legacyLow.GetValue(lowPass)));
            Assert.Equal((5000f, 1f, 1f), (highPass.cutoffFrequency, highPass.highpassResonanceQ, (float)legacyHigh.GetValue(highPass)));
            Assert.Equal((500f, 0.5f, 1f, 1f), (echo.delay, echo.decayRatio, echo.dryMix, echo.wetMix));
            legacyLow.SetValue(lowPass, 4f);
            legacyHigh.SetValue(highPass, 6f);
            Assert.Equal((4f, 6f), (lowPass.lowpassResonanceQ, highPass.highpassResonanceQ));
            Assert.Equal(new[] { AudioFilterKind.LowPass, AudioFilterKind.HighPass, AudioFilterKind.Echo },
                Array.ConvertAll(Collected(go.GetComponent<AudioSource>()), f => f.Kind));
        }

        [Fact]
        public void DescriptorsClampToTheDocumentedRangesAndOnlyTheLowPassCutoffClampsOnSet()
        {
            var source = Source("ranges");
            var lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            var highPass = source.gameObject.AddComponent<AudioHighPassFilter>();
            var echo = source.gameObject.AddComponent<AudioEchoFilter>();
            lowPass.cutoffFrequency = 0f;
            lowPass.lowpassResonanceQ = 0.5f;
            highPass.cutoffFrequency = 30000f;
            highPass.highpassResonanceQ = 20f;
            echo.delay = 1f;
            echo.decayRatio = -1f;
            echo.dryMix = 2f;
            echo.wetMix = -0.5f;
            var d = Collected(source);
            Assert.Equal((10f, 1f), (d[0].Cutoff, d[0].Resonance));
            Assert.Equal((22000f, 10f), (d[1].Cutoff, d[1].Resonance));
            Assert.Equal((1f, 0f, 1f, 0f), (d[2].Delay, d[2].Decay, d[2].DryMix, d[2].WetMix));
            echo.delay = 6000f;
            Assert.Equal(5000f, Collected(source)[2].Delay);
            Assert.Equal((10f, 30000f, 6000f), (lowPass.cutoffFrequency, highPass.cutoffFrequency, echo.delay));
            Assert.Equal(0.5f, lowPass.lowpassResonanceQ);
            lowPass.cutoffFrequency = float.PositiveInfinity;
            Assert.Equal(22000f, lowPass.cutoffFrequency);
            lowPass.cutoffFrequency = 4000f;
            Assert.Equal(4000f, lowPass.cutoffFrequency);
        }

        [Fact]
        public void EchoUsesWholeMillisecondsOnTheMeasuredRelease()
        {
            var source = Source("echo delay");
            var echo = source.gameObject.AddComponent<AudioEchoFilter>();
            foreach (float value in new[] { 10.125f, 17.1f, 17.5f, 17.9f, 18f })
            {
                echo.delay = value;
                Assert.Equal(MathF.Truncate(value), echo.delay);
                Assert.Equal(MathF.Truncate(value), Assert.Single(Collected(source)).Delay);
            }
            echo.delay = -1.25f;
            Assert.Equal((float)uint.MaxValue, echo.delay);
            Assert.Equal(5000f, Assert.Single(Collected(source)).Delay);
            echo.delay = 1.25f;
            Assert.Equal(1f, Assert.Single(Collected(source)).Delay);
            ProjectInfo.UnityVersion = "6000.0.70f1";
            echo.delay = 17.5f;
            Assert.Equal(17.5f, Assert.Single(Collected(source)).Delay);
        }

        [Fact]
        public void DistortionKeepsSetValuesButClampsItsDescriptorAndClonesSerializedValues()
        {
            var source = Source("distortion");
            var filter = source.gameObject.AddComponent<AudioDistortionFilter>();
            Assert.Equal(0.5f, filter.distortionLevel);
            foreach (float value in new[] { -1f, 0f, 0.37f, 1f, 2f })
            {
                filter.distortionLevel = value;
                source.Flush(always: true);
                var d = Assert.Single(m_Backend.FiltersOf(source.m_Handle));
                Assert.Equal(AudioFilterKind.Distortion, d.Kind);
                Assert.Equal(Mathf.Clamp01(value), d.Cutoff);
                Assert.Equal(value, filter.distortionLevel);
            }
            filter.distortionLevel = 0.37f;
            var clone = Object.Instantiate(source.gameObject); m_Objects.Add(clone);
            Assert.Equal(0.37f, clone.GetComponent<AudioDistortionFilter>().distortionLevel);
            filter.enabled = false;
            Assert.Equal(AudioFilterStage.Bypassed, Assert.Single(Collected(source)).Stage);
            filter.enabled = true; filter.distortionLevel = float.NaN;
            Assert.Empty(Collected(source));
            Assert.Single(Errors("AudioDistortionFilter"));
            Assert.Empty(m_Backend.FilterRejections);
        }

        [Fact]
        public void ChorusKeepsRawSettersClonesValuesAndReportsInvalidParametersOnce()
        {
            var source = Source("chorus");
            var filter = source.gameObject.AddComponent<AudioChorusFilter>();
            Assert.Equal((0.5f, 0.5f, 0.5f, 0.5f, 40f, 0.8f, 0.03f),
                (filter.dryMix, filter.wetMix1, filter.wetMix2, filter.wetMix3, filter.delay, filter.rate, filter.depth));
            filter.delay = -1; filter.rate = 200; filter.depth = 0.37f; filter.wetMix3 = 200;
            var d = Assert.Single(Collected(source));
            Assert.Equal(AudioFilterKind.Chorus, d.Kind);
            Assert.Equal((-1f, 20f, 0.37f, 1f), (d.Reverb[4], d.Reverb[5], d.Reverb[6], d.Reverb[3]));
            var clone = Object.Instantiate(source.gameObject); m_Objects.Add(clone);
            var copied = clone.GetComponent<AudioChorusFilter>();
            Assert.Equal((-1f, 200f, 0.37f, 200f), (copied.delay, copied.rate, copied.depth, copied.wetMix3));
            copied.delay = 10; Assert.Equal(-1f, filter.delay);
#pragma warning disable CS0618
            filter.feedback = 1; Assert.Equal(0, filter.feedback);
            Assert.Equal(2, m_Warnings.FindAll(w => w == "Warning! Feedback is deprecated. This property does nothing.").Count);
            filter.enabled = false; Assert.Equal(AudioFilterStage.Bypassed, Assert.Single(Collected(source)).Stage); filter.enabled = true;
            filter.depth = float.NaN;
            Assert.Empty(Collected(source)); Assert.Empty(Collected(source));
            Assert.Single(Errors("AudioChorusFilter"));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void SpatialComponentsRetainInspectorPlacementAroundSourceScripts(bool chorus, bool scriptFirst)
        {
            var source = Source("scripted-spatial");
            source.spatialBlend = 1;
            var script = scriptFirst ? source.gameObject.AddComponent<FilterScript>() : null;
            if (chorus) source.gameObject.AddComponent<AudioChorusFilter>();
            else source.gameObject.AddComponent<AudioDistortionFilter>();
            if (!scriptFirst) script = source.gameObject.AddComponent<FilterScript>();
            var descriptor = Assert.Single(Collected(source));
            Assert.Equal(scriptFirst ? AudioFilterStage.AfterScripts : AudioFilterStage.BeforeScripts, descriptor.Stage);
            Assert.Single(Collected(source));
            Assert.Empty(Errors("AudioSource on 'scripted-spatial'"));
            script.enabled = false;
            Assert.Single(Collected(source));
        }

        uint Flags(AudioSource source)
        {
            UpdateWithoutAdvancing();
            return m_Backend.ParamsOf(source.Handle).Flags;
        }

        const uint Inactive = AudioSourceFlags.ComponentsInactive;
        const uint LastActive = AudioSourceFlags.LastComponentActive;

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void FirstLiveBypassChangeAfterPlayReconnectsTheChain(bool script, bool spatial)
        {
            var source = Source("reconnect");
            if (spatial) source.spatialBlend = 1;
            if (script) source.gameObject.AddComponent<FilterScript>();
            source.gameObject.AddComponent<AudioDistortionFilter>();
            source.gameObject.AddComponent<AudioChorusFilter>();
            source.clip = AudioClip.Create("input", 48000, 1, 48000, false);
            source.Play();
            Assert.Equal(0u, Flags(source) & (Inactive | LastActive));
            source.bypassEffects = true;
            // The first change after Play restarts every component; the last one stays active.
            Assert.Equal(Inactive | LastActive, Flags(source) & (Inactive | LastActive));
            Assert.Equal(1, m_Backend.ComponentResets);
            source.bypassEffects = false;
            Assert.Equal(0u, Flags(source) & (Inactive | LastActive));
            source.bypassEffects = true;
            // Later changes only deactivate the components.
            Assert.Equal(Inactive, Flags(source) & (Inactive | LastActive));
            Assert.Equal(1, m_Backend.ComponentResets);
            Assert.Empty(m_Errors);
        }

        [Fact]
        public void ListenerBypassReconnectsOnceAndDisabledComponentsRetainTheirIdentity()
        {
            var source = Source("listener activation");
            var echo = source.gameObject.AddComponent<AudioEchoFilter>();
            source.gameObject.AddComponent<AudioLowPassFilter>();
            source.clip = AudioClip.Create("input", 48000, 1, 48000, false);
            source.Play();
            Assert.Equal(0u, Flags(source) & (Inactive | LastActive));
            source.bypassListenerEffects = true;
            Assert.Equal(0u, Flags(source) & (Inactive | LastActive));
            Assert.Equal(1, m_Backend.ComponentResets);
            echo.enabled = false;
            Flags(source);
            var disabled = m_Backend.FiltersOf(source.m_Handle);
            Assert.Equal(Id(echo), disabled[0].Id);
            Assert.Equal(AudioFilterStage.Bypassed, disabled[0].Stage);
            echo.enabled = true;
            source.bypassListenerEffects = false;
            Flags(source);
            Assert.Equal(1, m_Backend.ComponentResets);
            Assert.Equal(AudioFilterStage.BeforeScripts, m_Backend.FiltersOf(source.m_Handle)[0].Stage);
            source.gameObject.AddComponent<AudioHighPassFilter>();
            Flags(source);
            Assert.Equal(2, m_Backend.ComponentResets);
        }

        [Fact]
        public void DisabledEditsAreDeferredAndNeverEnabledInvalidUnitsStayAbsent()
        {
            var source = Source("deferred validation");
            var echo = source.gameObject.AddComponent<AudioEchoFilter>();
            echo.enabled = false;
            echo.wetMix = float.NaN;
            Assert.Empty(Collected(source));
            Assert.Empty(m_Errors);
            echo.wetMix = .5f;
            echo.enabled = true;
            var accepted = Assert.Single(Collected(source));
            echo.enabled = false;
            echo.wetMix = float.NaN;
            var frozen = Assert.Single(Collected(source));
            Assert.Equal(accepted.WetMix, frozen.WetMix);
            Assert.Equal(AudioFilterStage.Bypassed, frozen.Stage);
            Assert.Empty(m_Errors);
            echo.enabled = true;
            Assert.Empty(Collected(source));
            Assert.Single(Errors("AudioEchoFilter"));
        }

        [Fact]
        public void NeverEnabledListenerFiltersDoNotRejectAnExistingScriptChain()
        {
            var go = new GameObject("disabled listener filter");
            m_Objects.Add(go);
            var listener = go.AddComponent<AudioListener>();
            go.AddComponent<AudioLowPassFilter>().enabled = false;
            go.AddComponent<FilterScript>();
            AudioFilterDescriptor[] table = null;
            Assert.Equal(0, AudioFilterChain.CollectListener(listener, true, ref table));
            Assert.Empty(m_Errors);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SimultaneousEnableChangesKeepALaterUnitsAcceptedDescriptor(bool listener)
        {
            var go = new GameObject("swapped filter states");
            m_Objects.Add(go);
            var source = listener ? null : go.AddComponent<AudioSource>();
            var hearing = listener ? go.AddComponent<AudioListener>() : null;
            var low = go.AddComponent<AudioLowPassFilter>();
            low.enabled = false;
            var echo = go.AddComponent<AudioEchoFilter>();
            AudioFilterDescriptor[] table = null;
            int Collect() => listener ? AudioFilterChain.CollectListener(hearing, false, ref table)
                : AudioFilterChain.Collect(source, false, ref table);
            Assert.Equal(1, Collect());
            Assert.Equal(Id(echo), table[0].Id);
            low.enabled = true;
            echo.enabled = false;
            Assert.Equal(2, Collect());
            Assert.Equal(Id(echo), table[1].Id);
            Assert.Equal(AudioFilterStage.Bypassed, table[1].Stage);
            low.enabled = false;
            echo.enabled = true;
            Assert.Equal(2, Collect());
            Assert.Equal(AudioFilterStage.Bypassed, table[0].Stage);
            Assert.Equal(AudioFilterStage.BeforeScripts, table[1].Stage);
            Assert.Empty(m_Errors);
        }

        [Fact]
        public void ASecondDistortionComponentIsRejectedWithoutChangingTheFirst()
        {
            var source = Source("duplicate distortion");
            var first = source.gameObject.AddComponent<AudioDistortionFilter>();
            first.distortionLevel = .6f;
            Assert.Null(source.gameObject.AddComponent<AudioDistortionFilter>());
            Assert.Equal(.6f, first.distortionLevel);
            Assert.Single(source.GetComponents<AudioDistortionFilter>());
            Assert.Single(Errors("already contains the same component"));
        }

        [Theory]
        [InlineData(typeof(AudioLowPassFilter))]
        [InlineData(typeof(AudioHighPassFilter))]
        [InlineData(typeof(AudioEchoFilter))]
        [InlineData(typeof(AudioChorusFilter))]
        [InlineData(typeof(AudioReverbFilter))]
        [InlineData(typeof(AudioDistortionFilter))]
        public void FilterUniquenessIncludesDisabledInstancesAndAllowsReplacement(Type type)
        {
            foreach (bool enabled in new[] { true, false })
            {
                var source = Source("filter uniqueness");
                var first = (Behaviour)source.gameObject.AddComponent(type);
                first.enabled = enabled;
                int errors = Errors("already contains the same component").Count;
                Assert.Null(source.gameObject.AddComponent(type));
                Assert.Same(first, source.gameObject.GetComponent(type));
                Assert.Single(source.gameObject.GetComponents(type));
                Assert.Equal(errors + 1, Errors("already contains the same component").Count);

                Object.DestroyImmediate(first);
                Assert.NotNull(source.gameObject.AddComponent(type));
                Assert.Single(source.gameObject.GetComponents(type));
                var other = type == typeof(AudioHighPassFilter) ? typeof(AudioLowPassFilter) : typeof(AudioHighPassFilter);
                Assert.NotNull(source.gameObject.AddComponent(other));
            }
        }

        [Fact]
        public void StaticBypassKeepsComponentsActiveUntilStopAndPlay()
        {
            var source = Source("static bypass");
            source.gameObject.AddComponent<AudioEchoFilter>();
            source.clip = AudioClip.Create("input", 48000, 1, 48000, false);
            source.bypassEffects = true;
            source.Play();
            Assert.Equal(0u, Flags(source) & Inactive);
            // After a Stop, Play restores activation from bypassEffects without a reconnection.
            source.Stop();
            source.Play();
            Assert.Equal(Inactive, Flags(source) & (Inactive | LastActive));
            Assert.Equal(0, m_Backend.ComponentResets);
            // A changed filter list reconnects and activates every component.
            source.gameObject.AddComponent<AudioLowPassFilter>();
            Assert.Equal(0u, Flags(source) & Inactive);
        }

        [Fact]
        public void BypassInThePlayFrameAndClipless()
        {
            var source = Source("clipless");
            source.gameObject.AddComponent<FilterScript>();
            source.gameObject.AddComponent<AudioChorusFilter>();
            source.Play();
            UpdateWithoutAdvancing();
            source.bypassEffects = true;
            // A clipless generator's Play does not prepare a reconnection.
            Assert.Equal(Inactive, Flags(source) & (Inactive | LastActive));
            Assert.Equal(0, m_Backend.ComponentResets);
            var clip = Source("before first frame");
            clip.gameObject.AddComponent<AudioChorusFilter>();
            clip.clip = AudioClip.Create("input", 48000, 1, 48000, false);
            clip.Play();
            clip.bypassEffects = true;
            // The chain connected by Play completes at the audio update: a static bypass.
            Assert.Equal(0u, Flags(clip) & (Inactive | LastActive));
            Assert.Equal(0, m_Backend.ComponentResets);
        }

        [Fact]
        public void OtherReleasesKeepTheBypassFlagOnly()
        {
            ProjectInfo.UnityVersion = "2022.3.10f1";
            var source = Source("legacy");
            source.gameObject.AddComponent<AudioLowPassFilter>();
            source.clip = AudioClip.Create("input", 48000, 1, 48000, false);
            source.Play();
            UpdateWithoutAdvancing();
            source.bypassEffects = true;
            Assert.Equal(AudioSourceFlags.BypassEffects, Flags(source) & (AudioSourceFlags.BypassEffects | Inactive | LastActive));
            Assert.Equal(0, m_Backend.ComponentResets);
            Assert.Equal(0u, m_Backend.ParamsOf(source.m_Handle).Flags & AudioSourceFlags.ComponentHistory);
        }

        [Fact]
        public void NarrowSpatialCurveRetainsTheMeasuredSpatialComponent()
        {
            var source = Source("narrow-spatial-curve");
            source.gameObject.AddComponent<AudioDistortionFilter>();
            source.maxDistance = 20;
            source.transform.position = new Vector3(0, 0, 5);
            AudioSystem.s_HearingPosition = Vector3.zero;
            source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, new AnimationCurve(
                new Keyframe(0, 0), new Keyframe(0.249f, 0, 0, 1000),
                new Keyframe(0.251f, 0, -1000, 0), new Keyframe(1, 0)));
            AudioFilterDescriptor[] table = null;
            Assert.True(source.UsesSpatialBlend);
            Assert.Equal(1, AudioFilterChain.Collect(source, false, ref table));
            Assert.Empty(Errors("on a 3D AudioSource is not supported"));
        }

        [Fact]
        public void CustomCutoffCurveFailsLoudly()
        {
            var lowPass = Source("curve").gameObject.AddComponent<AudioLowPassFilter>();
            Assert.Throws<NotSupportedException>(() => lowPass.customCutoffCurve);
            Assert.Throws<NotSupportedException>(() => lowPass.customCutoffCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f));
            Assert.Equal(5000f, lowPass.cutoffFrequency);
        }

        [Fact]
        public void ComponentsKeepTheirOrderAndDisableOnlyTheirOwnProcessing()
        {
            var source = Source("order");
            var echo = source.gameObject.AddComponent<AudioEchoFilter>();
            var lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            var highPass = source.gameObject.AddComponent<AudioHighPassFilter>();
            Assert.Equal(3, Collected(source).Length);
            lowPass.enabled = false;
            var d = Collected(source);
            Assert.Equal(new[] { Id(echo), Id(lowPass), Id(highPass) }, Array.ConvertAll(d, f => f.Id));
            Assert.Equal(new[] { AudioFilterKind.Echo, AudioFilterKind.LowPass, AudioFilterKind.HighPass }, Array.ConvertAll(d, f => f.Kind));
            Assert.Equal(new[] { 0, AudioFilterStage.Bypassed, 0 }, Array.ConvertAll(d, f => f.Stage));
            lowPass.enabled = true;
            Assert.Equal(new[] { Id(echo), Id(lowPass), Id(highPass) }, Array.ConvertAll(Collected(source), f => f.Id));
            Object.DestroyImmediate(echo);
            Assert.Equal(new[] { Id(lowPass), Id(highPass) }, Array.ConvertAll(Collected(source), f => f.Id));
        }

        [Fact]
        public void ComponentsSitAboveOrBelowTheOnAudioFilterReadScripts()
        {
            var source = Source("stages");
            source.gameObject.AddComponent<AudioLowPassFilter>();
            source.gameObject.AddComponent<FilterScript>();
            source.gameObject.AddComponent<FilterScript>();
            source.gameObject.AddComponent<AudioEchoFilter>();
            var d = Collected(source);
            Assert.Equal(new[] { AudioFilterStage.BeforeScripts, AudioFilterStage.AfterScripts }, Array.ConvertAll(d, f => f.Stage));
            // Below every script of a clipless generator is accepted.
            var generator = Source("generator below");
            generator.gameObject.AddComponent<FilterScript>();
            generator.gameObject.AddComponent<AudioHighPassFilter>();
            Assert.Equal(AudioFilterStage.AfterScripts, Assert.Single(Collected(generator, generator: true)).Stage);
            Assert.Empty(Errors("AudioSource on 'generator below'"));
        }

        [Fact]
        public void ChainsUnboundCannotPlaceFailLoudlyOnceAndSendNothing()
        {
            var between = Source("between");
            between.gameObject.AddComponent<FilterScript>();
            between.gameObject.AddComponent<AudioLowPassFilter>();
            between.gameObject.AddComponent<FilterScript>();
            Assert.Empty(Collected(between));
            Assert.Empty(Collected(between));
            Assert.Contains("between two OnAudioFilterRead scripts", Assert.Single(Errors("AudioSource on 'between'")));

            var above = Source("above");
            above.gameObject.AddComponent<AudioEchoFilter>();
            above.gameObject.AddComponent<FilterScript>();
            Assert.Empty(Collected(above, generator: true));
            Assert.Contains("clipless", Assert.Single(Errors("AudioSource on 'above'")));
            // The same chain on a source with a clip is placed (above the scripts).
            Assert.Equal(AudioFilterStage.BeforeScripts, Assert.Single(Collected(above)).Stage);

            var many = Source("many");
            // Construct a synthetic oversized chain: AddComponent permits one of each filter type.
            for (int i = 0; i <= AudioFilterDescriptor.MaxPerSource; i++)
                many.gameObject.m_Components.Add(ComponentFactory.Create(typeof(AudioEchoFilter), many.gameObject));
            Assert.Empty(Collected(many));
            Assert.Contains("more than 64", Assert.Single(Errors("AudioSource on 'many'")));
        }

        [Fact]
        public void NonFiniteParametersAreReportedAndNotApplied()
        {
            var source = Source("nan");
            var lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            var echo = source.gameObject.AddComponent<AudioEchoFilter>();
            lowPass.cutoffFrequency = float.NaN;
            Assert.Equal(new[] { Id(echo) }, Array.ConvertAll(Collected(source), f => f.Id));
            Assert.Equal(new[] { Id(echo) }, Array.ConvertAll(Collected(source), f => f.Id));
            Assert.Single(Errors("AudioLowPassFilter on 'nan'"));
            echo.wetMix = float.PositiveInfinity;
            Assert.Empty(Collected(source));
            Assert.Single(Errors("AudioEchoFilter on 'nan'"));
        }

        [Fact]
        public void TheSourcePacketCarriesOwnedCopiesOfTheComponents()
        {
            var source = Source("packet");
            var highPass = source.gameObject.AddComponent<AudioHighPassFilter>();
            highPass.cutoffFrequency = 800f;
            source.bypassEffects = true;
            source.Flush(always: true);
            var sent = m_Backend.FiltersOf(source.m_Handle);
            Assert.Equal(Id(highPass), Assert.Single(sent).Id);
            Assert.Equal(800f, sent[0].Cutoff);
            Assert.NotEqual(0u, m_Backend.ParamsOf(source.m_Handle).Flags & AudioSourceFlags.BypassEffects);
            // The source reuses its table for the next packet: the backend's copy does not follow it.
            highPass.cutoffFrequency = 900f;
            Assert.Equal(1, AudioFilterChain.Collect(source, false, ref source.m_FilterTable));
            Assert.Equal(900f, source.m_FilterTable[0].Cutoff);
            Assert.Equal(800f, m_Backend.FiltersOf(source.m_Handle)[0].Cutoff);
            source.Flush(always: true);
            Assert.Equal(900f, m_Backend.FiltersOf(source.m_Handle)[0].Cutoff);
            highPass.enabled = false;
            source.Flush(always: true);
            Assert.Equal(AudioFilterStage.Bypassed, Assert.Single(m_Backend.FiltersOf(source.m_Handle)).Stage);
            Assert.Empty(m_Backend.FilterRejections);
        }

        [Fact]
        public void ASourceWithoutComponentsSendsTheSamePacketAsBefore()
        {
            var source = Source("plain");
            source.volume = 0.5f;
            source.Flush(always: true);
            var sent = m_Backend.ParamsOf(source.m_Handle);
            Assert.Equal(0, sent.FilterCount);
            Assert.True(sent.Filters == null);
            Assert.Equal(0.5f, sent.Volume);
            Assert.Empty(m_Backend.FiltersOf(source.m_Handle));
            Assert.Empty(m_Backend.FilterRejections);
            Assert.Null(source.m_FilterTable);
        }

        static AudioFilterDescriptor NonFiniteReverb(AudioFilterDescriptor d)
        {
            d.Reverb[AudioFilterDescriptor.ReverbCount - 1] = float.NaN;
            return d;
        }

        /// <summary>The virtual backend accepts every kind the native mixer accepts (both use
        /// AudioFilterKind.Supported; managed_filter_kinds_match in protocol.rs pins it to Rust).</summary>
        [Fact]
        public void VirtualBackendAcceptsEveryNativeFilterKind()
        {
            AudioFilterDescriptor* descriptors = stackalloc AudioFilterDescriptor[AudioFilterKind.Last];
            for (int kind = AudioFilterKind.LowPass; kind <= AudioFilterKind.Last; kind++)
                descriptors[kind - 1] = new AudioFilterDescriptor { Id = (ulong)kind, Kind = kind, Resonance = 1f, DryMix = 1f, WetMix = 1f };
            var packet = new AudioSourcePacket { Clip = 1, Volume = 1f, Pitch = 1f, FilterCount = AudioFilterKind.Last, Filters = descriptors };
            m_Backend.UpdateSource(4, ref packet);
            Assert.Empty(m_Backend.FilterRejections);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, Array.ConvertAll(m_Backend.FiltersOf(4), f => f.Kind));
            Assert.True(AudioFilterKind.Supported(AudioFilterKind.Chorus));
            Assert.False(AudioFilterKind.Supported(AudioFilterKind.Last + 1));
            Assert.False(AudioFilterKind.Supported(0));
        }

        [Fact]
        public void ChorusAndDistortionComponentsFailOnUnmeasuredReleases()
        {
            var chorus = Source("chorus-release");
            chorus.gameObject.AddComponent<AudioLowPassFilter>();
            chorus.gameObject.AddComponent<AudioChorusFilter>();
            var distortion = Source("distortion-release");
            distortion.gameObject.AddComponent<AudioDistortionFilter>();
            Assert.Equal(2, Collected(chorus).Length);
            Assert.Single(Collected(distortion));
            ProjectInfo.UnityVersion = "6000.0.72f1";
            Assert.Empty(Collected(chorus));
            Assert.Empty(Collected(distortion));
            Assert.Single(Errors("AudioChorusFilter is measured only for Unity 6000.0.71f1"));
            Assert.Single(Errors("AudioDistortionFilter is measured only for Unity 6000.0.71f1"));
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Assert.Equal(2, Collected(chorus).Length);
        }

        [Fact]
        public void MalformedDescriptorsAreRefusedAndKeepThePreviousChain()
        {
            var valid = new AudioFilterDescriptor { Id = 1, Kind = AudioFilterKind.Echo, Delay = 500f, Decay = 0.5f, DryMix = 1f, WetMix = 1f };
            AudioFilterDescriptor* descriptors = stackalloc AudioFilterDescriptor[AudioFilterDescriptor.MaxPerSource + 1];
            for (int i = 0; i <= AudioFilterDescriptor.MaxPerSource; i++) descriptors[i] = valid with { Id = (ulong)i + 1 };
            AudioSourcePacket Packet(int count, AudioFilterDescriptor* filters, ulong clip = 1, uint flags = 0) =>
                new AudioSourcePacket { Clip = clip, Volume = 0.75f, Pitch = 1f, Flags = flags, FilterCount = count, Filters = filters };
            var packet = Packet(1, descriptors);
            m_Backend.UpdateSource(9, ref packet);
            Assert.Equal(1ul, Assert.Single(m_Backend.FiltersOf(9)).Id);

            ulong* storage = stackalloc ulong[16];
            var cases = new List<(string Expected, AudioSourcePacket Packet)>
            {
                ("outside", Packet(-1, descriptors)),
                ("outside", Packet(AudioFilterDescriptor.MaxPerSource + 1, descriptors)),
                ("null", Packet(1, null)),
                ("misaligned", Packet(1, (AudioFilterDescriptor*)((byte*)storage + 4))),
            };
            var bad = new[]
            {
                ("kind", valid with { Kind = 0 }), ("kind", valid with { Kind = AudioFilterKind.Last + 1 }), ("stage", valid with { Stage = 4 }),
                ("finite", valid with { Delay = float.NaN }), ("finite", valid with { Cutoff = float.PositiveInfinity }),
                ("finite", NonFiniteReverb(valid with { Kind = AudioFilterKind.Reverb })),
            };
            AudioFilterDescriptor* singles = stackalloc AudioFilterDescriptor[bad.Length];
            for (int i = 0; i < bad.Length; i++)
            {
                singles[i] = bad[i].Item2;
                cases.Add((bad[i].Item1, Packet(1, singles + i)));
            }
            AudioFilterDescriptor* twice = stackalloc AudioFilterDescriptor[2];
            twice[0] = valid;
            twice[1] = valid with { Kind = AudioFilterKind.LowPass };
            cases.Add(("duplicate", Packet(2, twice)));
            cases.Add(("generator", Packet(1, descriptors, clip: 0, flags: AudioSourceFlags.Filter)));
            foreach (var (expected, p) in cases)
            {
                var copy = p;
                copy.Volume = 0.25f;
                m_Backend.FilterRejections.Clear();
                m_Backend.UpdateSource(9, ref copy);
                Assert.Contains(expected, Assert.Single(m_Backend.FilterRejections));
                Assert.Equal(1ul, Assert.Single(m_Backend.FiltersOf(9)).Id);
                Assert.Equal(0.25f, m_Backend.ParamsOf(9).Volume);
            }
            // The bound itself, and count 0 with any pointer, are accepted.
            var most = Packet(AudioFilterDescriptor.MaxPerSource, descriptors);
            m_Backend.UpdateSource(9, ref most);
            Assert.Equal(AudioFilterDescriptor.MaxPerSource, m_Backend.FiltersOf(9).Length);
            var none = Packet(0, (AudioFilterDescriptor*)((byte*)storage + 4));
            m_Backend.UpdateSource(9, ref none);
            Assert.Empty(m_Backend.FiltersOf(9));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SpatialDistortionAndChorusRetainTheirMeasuredDescriptors(bool chorus)
        {
            var source = Source("spatial filter");
            if (chorus) source.gameObject.AddComponent<AudioChorusFilter>();
            else source.gameObject.AddComponent<AudioDistortionFilter>();
            Assert.Single(Collected(source));
            source.spatialBlend = 1f;
            Assert.Single(Collected(source));
            Assert.Single(Collected(source));
            Assert.Empty(Errors("on a 3D AudioSource is not supported"));
            source.bypassEffects = true;
            Assert.Single(Collected(source));
            source.bypassEffects = false;
            source.SetCustomCurve(AudioSourceCurveType.SpatialBlend,
                new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 0)));
            Assert.Single(Collected(source));
            source.SetCustomCurve(AudioSourceCurveType.SpatialBlend,
                new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1)));
            Assert.Single(Collected(source));
            Assert.Empty(Errors("on a 3D AudioSource is not supported"));
        }

        [Fact]
        public void ListenerFiltersKeepInspectorOrderAndRejectUnmeasuredPlacements()
        {
            var go = new GameObject("ears");
            m_Objects.Add(go);
            var listener = go.AddComponent<AudioListener>();
            var filters = new Behaviour[] {
                go.AddComponent<AudioLowPassFilter>(), go.AddComponent<AudioDistortionFilter>(),
                go.AddComponent<AudioChorusFilter>(), go.AddComponent<AudioHighPassFilter>(),
                go.AddComponent<AudioEchoFilter>(), go.AddComponent<AudioReverbFilter>()
            };
            AudioFilterDescriptor[] table = null;
            Assert.Equal(6, AudioFilterChain.CollectListener(listener, false, ref table));
            filters[1].enabled = false;
            Assert.Equal(6, AudioFilterChain.CollectListener(listener, false, ref table));
            Assert.Equal(6, AudioFilterChain.CollectListener(listener, false, ref table));
            Assert.Equal(new[] { 1, 5, 6, 2, 3, 4 }, table.Take(6).Select(d => d.Kind));
            Assert.Equal(AudioFilterStage.Bypassed, table[1].Stage);
            Assert.Equal(filters.Select(c => unchecked((ulong)(long)c.GetInstanceID())), table.Take(6).Select(d => d.Id));
            Assert.Empty(Errors("AudioListener 'ears'"));
            ProjectInfo.UnityVersion = "6000.0.70f1";
            Assert.False(Compat.AudioListenerChainMeasured);
            Assert.Equal(1, AudioFilterChain.CollectListener(listener, false, ref table));
            Assert.Equal(AudioFilterKind.Reverb, table[0].Kind);
            Assert.Single(Errors("AudioListener 'ears'"));
            ProjectInfo.UnityVersion = "6000.0.71f1";

            var scripted = new GameObject("scripted ears");
            m_Objects.Add(scripted);
            var other = scripted.AddComponent<AudioListener>();
            scripted.AddComponent<AudioReverbFilter>();
            Assert.Equal(1, AudioFilterChain.CollectListener(other, true, ref table));
            Assert.Empty(Errors("OnAudioFilterRead scripts on the AudioListener 'scripted ears'"));
            ProjectInfo.UnityVersion = "6000.0.70f1";
            Assert.Equal(0, AudioFilterChain.CollectListener(other, true, ref table));
            Assert.Contains("listener filter components are not applied", Assert.Single(Errors("OnAudioFilterRead scripts on the AudioListener 'scripted ears'")));
            ProjectInfo.UnityVersion = "6000.0.71f1";

            var shared = new GameObject("source ears");
            m_Objects.Add(shared);
            var third = shared.AddComponent<AudioListener>();
            shared.AddComponent<AudioSource>();
            shared.AddComponent<AudioReverbFilter>();
            Assert.Equal(0, AudioFilterChain.CollectListener(third, false, ref table));
            Assert.Contains("runs only in that source's chain", Assert.Single(Errors("AudioListener 'source ears'")));
        }

        sealed class AffineListenerScript : MonoBehaviour
        {
            internal float Gain = 1, Offset;
            internal int Calls;
            void OnAudioFilterRead(float[] data, int channels)
            {
                Calls++;
                for (int i = 0; i < data.Length; i++) data[i] = data[i] * Gain + Offset;
            }
        }

        [Fact]
        public void ListenerComponentsKeepPositionsAcrossScriptsAndDisabledUnits()
        {
            var go = new GameObject("ordered listener"); m_Objects.Add(go);
            var listener = go.AddComponent<AudioListener>();
            var echo = go.AddComponent<AudioEchoFilter>();
            var first = go.AddComponent<FilterScript>();
            go.AddComponent<AudioDistortionFilter>();
            go.AddComponent<FilterScript>();
            go.AddComponent<AudioHighPassFilter>();
            AudioFilterDescriptor[] table = null;
            Assert.Equal(3, AudioFilterChain.CollectListener(listener, true, ref table));
            Assert.Equal(new[] { 0, 1, 5 }, table.Take(3).Select(d => d.Stage));
            echo.enabled = false;
            Assert.Equal(3, AudioFilterChain.CollectListener(listener, true, ref table));
            Assert.Equal(new[] { 2, 1, 5 }, table.Take(3).Select(d => d.Stage));
            first.enabled = false;
            Assert.Equal(3, AudioFilterChain.CollectListener(listener, true, ref table));
            Assert.Equal(new[] { 2, 0, 1 }, table.Take(3).Select(d => d.Stage));
            Assert.Equal(3, AudioFilterChain.CollectListener(listener, true, ref table, scriptOffset: 3));
            Assert.Equal(new[] { 11, 9, 13 }, table.Take(3).Select(d => d.Stage));
            Assert.Empty(m_Errors);
        }

        [Fact]
        public void AnInitiallyInactiveScriptJoinsTheChainOnItsFirstActivation()
        {
            var source = Source("inactive script source");
            source.gameObject.AddComponent<AudioEchoFilter>();
            Collected(source);
            source.m_HasAudioFrame = true;
            var script = source.gameObject.AddComponent<FilterScript>(); script.enabled = false;
            Collected(source);
            Assert.False(source.m_ResetComponents);
            script.enabled = true;
            Collected(source);
            Assert.True(source.m_ResetComponents);
            source.m_ResetComponents = false;
            script.enabled = false;
            Collected(source);
            Assert.False(source.m_ResetComponents);

            var owner = new GameObject("inactive script listener"); m_Objects.Add(owner);
            var listener = owner.AddComponent<AudioListener>();
            owner.AddComponent<AudioEchoFilter>();
            AudioFilterDescriptor[] table = null;
            AudioFilterChain.CollectListener(listener, false, ref table);
            script = owner.AddComponent<FilterScript>(); script.enabled = false;
            AudioFilterChain.CollectListener(listener, false, ref table);
            Assert.Equal(0, table[0].Stage);
            script.enabled = true;
            AudioFilterChain.CollectListener(listener, true, ref table);
            Assert.Equal(AudioFilterStage.Reconnected, table[0].Stage);
            script.enabled = false;
            AudioFilterChain.CollectListener(listener, false, ref table);
            Assert.Equal(0, table[0].Stage);
        }

        [Fact]
        public void ScriptActivationKeepsMembershipAndRemovalDependsOnTheOwner()
        {
            var source = Source("script activation source");
            source.gameObject.AddComponent<AudioEchoFilter>();
            Collected(source);
            source.m_HasAudioFrame = true;
            var script = source.gameObject.AddComponent<FilterScript>();
            Collected(source);
            Assert.True(source.m_ResetComponents);
            source.m_ResetComponents = false;
            ulong identity = source.m_FilterIdentity;
            foreach (bool enabled in new[] { false, true, false })
            {
                script.enabled = enabled;
                Collected(source);
                Assert.Equal(identity, source.m_FilterIdentity);
                Assert.False(source.m_ResetComponents);
            }
            Object.DestroyImmediate(script);
            Collected(source);
            Assert.True(source.m_ResetComponents);

            var owner = new GameObject("script activation listener"); m_Objects.Add(owner);
            var listener = owner.AddComponent<AudioListener>();
            owner.AddComponent<AudioEchoFilter>();
            AudioFilterDescriptor[] table = null;
            AudioFilterChain.CollectListener(listener, false, ref table);
            script = owner.AddComponent<FilterScript>();
            AudioFilterChain.CollectListener(listener, true, ref table);
            Assert.Equal(AudioFilterStage.Reconnected, table[0].Stage);
            foreach (bool enabled in new[] { false, true, false })
            {
                script.enabled = enabled;
                AudioFilterChain.CollectListener(listener, enabled, ref table);
                Assert.Equal(0, table[0].Stage);
            }
            Object.DestroyImmediate(script);
            AudioFilterChain.CollectListener(listener, false, ref table);
            Assert.Equal(0, table[0].Stage);
        }

        [Fact]
        public void UnchangedScriptMembershipReusesItsStorage()
        {
            var source = Source("stable script source");
            source.gameObject.AddComponent<AudioEchoFilter>();
            source.gameObject.AddComponent<FilterScript>();
            var owner = new GameObject("stable script listener"); m_Objects.Add(owner);
            var listener = owner.AddComponent<AudioListener>();
            owner.AddComponent<AudioEchoFilter>();
            owner.AddComponent<FilterScript>();
            AudioFilterDescriptor[] sourceTable = null, listenerTable = null;
            for (int i = 0; i < 8; i++)
            {
                AudioFilterChain.Collect(source, false, ref sourceTable);
                AudioFilterChain.CollectListener(listener, true, ref listenerTable);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++)
            {
                AudioFilterChain.Collect(source, false, ref sourceTable);
                AudioFilterChain.CollectListener(listener, true, ref listenerTable);
            }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
            Assert.Single(source.m_FilterScripts);
            Assert.Equal(2, listener.m_FilterMembers.Count);
            Assert.Empty(m_Errors);
        }

        [Fact]
        public void ListenerHistoryCommandsBelongToTheEditedOwnerAndPublishOnce()
        {
            var primary = new GameObject("edited listener"); m_Objects.Add(primary);
            var other = new GameObject("unchanged listener"); m_Objects.Add(other);
            var listener = primary.AddComponent<AudioListener>();
            var alternate = other.AddComponent<AudioListener>();
            var echo = primary.AddComponent<AudioEchoFilter>();
            primary.AddComponent<AudioChorusFilter>();
            other.AddComponent<AudioEchoFilter>();
            AudioFilterDescriptor[] table = null, otherTable = null;
            int Collect() => AudioFilterChain.CollectListener(listener, false, ref table);
            Assert.Equal(2, Collect());
            Assert.Equal(1, AudioFilterChain.CollectListener(alternate, false, ref otherTable));

            // A never-enabled unit still changes the connected component list.
            var added = primary.AddComponent<AudioDistortionFilter>(); added.enabled = false;
            Assert.Equal(2, Collect());
            Assert.All(table.Take(2), d => Assert.Equal(AudioFilterStage.Reconnected, d.Stage));
            Assert.Equal(1, AudioFilterChain.CollectListener(alternate, false, ref otherTable));
            Assert.Equal(0, otherTable[0].Stage);
            Assert.Equal(2, Collect());
            Assert.All(table.Take(2), d => Assert.Equal(0, d.Stage));

            Object.DestroyImmediate(added);
            Assert.Equal(2, Collect());
            Assert.All(table.Take(2), d => Assert.Equal(0, d.Stage));
            Collect();
            echo.delay = 17;
            echo.enabled = false;
            Assert.Equal(2, Collect());
            Assert.Equal(AudioFilterStage.Bypassed, table[0].Stage);
            Assert.Equal(0, table[1].Stage);
            echo.enabled = true;
            Collect();

            // The lifetime changes even when no audio frame observes the disabled owner.
            listener.enabled = false;
            listener.enabled = true;
            Assert.Equal(2, Collect());
            Assert.All(table.Take(2), d => Assert.Equal(AudioFilterStage.Recreated, d.Stage));
            Assert.Equal(2, Collect());
            Assert.All(table.Take(2), d => Assert.Equal(0, d.Stage));
        }

        [Fact]
        public void EveryActiveListenerContributesScriptsAndRangesPreserveTheirOrder()
        {
            var primary = new GameObject("primary scripts"); m_Objects.Add(primary);
            var listener = primary.AddComponent<AudioListener>();
            var first = primary.AddComponent<AffineListenerScript>(); first.Gain = 2;
            primary.AddComponent<AudioEchoFilter>();
            var second = primary.AddComponent<AffineListenerScript>(); second.Offset = 0.125f;
            var alternate = new GameObject("alternate scripts"); m_Objects.Add(alternate);
            var other = alternate.AddComponent<AudioListener>();
            alternate.AddComponent<AudioHighPassFilter>();
            var third = alternate.AddComponent<AffineListenerScript>(); third.Gain = -1;
            UpdateWithoutAdvancing();
            Assert.Equal(2, m_Backend.LastFrame.FilterCount);
            Assert.NotEqual(0, m_Backend.LastFrame.Flags & AudioFrameFlags.ListenerChain);
            Assert.Equal(5, other.m_FilterTable[0].Stage);
            Assert.Single(m_Warnings.FindAll(message => message.Contains("2 audio listeners")));
            var data = new[] { 0.25f, -0.25f };
            fixed (float* pointer = data)
            {
                AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, 0, pointer, 1, 2, (1L << 32) | 1);
                Assert.Equal(new[] { 0.5f, -0.5f }, data);
                AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, 0, pointer, 1, 2, (3L << 32) | 2);
                Assert.Equal(new[] { -0.625f, 0.375f }, data);
            }
            Assert.Equal(new[] { 1, 1, 1 }, new[] { first.Calls, second.Calls, third.Calls });
            other.enabled = false;
            UpdateWithoutAdvancing();
            data = new[] { 0.25f, -0.25f };
            fixed (float* pointer = data) AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, 0, pointer, 1, 2, 0);
            Assert.Equal(new[] { 0.625f, -0.375f }, data);
            Assert.Equal(1, third.Calls);
            Assert.Empty(m_Errors);
            alternate.GetComponent<AudioHighPassFilter>().enabled = false;
            other.enabled = true;
            ProjectInfo.UnityVersion = "6000.0.70f1";
            UpdateWithoutAdvancing();
            Assert.Equal(0, m_Backend.LastFrame.Flags & AudioFrameFlags.ListenerChain);
            data = new[] { 0.25f, -0.25f };
            fixed (float* pointer = data) AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, 0, pointer, 1, 2, 0);
            Assert.Equal(new[] { -0.25f, 0.25f }, data);
            Assert.Equal(new[] { 2, 2, 2 }, new[] { first.Calls, second.Calls, third.Calls });
            Assert.Empty(m_Errors);
        }

        [Fact]
        public void ListenerScriptPlansStayPairedWithTheirSubmittedComponentPositions()
        {
            var go = new GameObject("published listener plan"); m_Objects.Add(go);
            go.AddComponent<AudioListener>();
            var first = go.AddComponent<AffineListenerScript>(); first.Gain = 2;
            go.AddComponent<AudioEchoFilter>();
            var second = go.AddComponent<AffineListenerScript>(); second.Offset = 0.125f;
            UpdateWithoutAdvancing();
            uint previous = m_Backend.LastFrame.ListenerPlan;
            Assert.NotEqual(0u, previous);
            // Warm reflection/delegate metadata before measuring unchanged publications.
            for (int i = 0; i < 8; i++) AudioFilters.UpdateListeners(AudioSystem.Listeners);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) AudioFilters.UpdateListeners(AudioSystem.Listeners);
            Assert.Equal(allocated, GC.GetAllocatedBytesForCurrentThread());
            first.enabled = false;
            Assert.True(AudioFilters.UpdateListeners(AudioSystem.Listeners));
            uint next = AudioFilters.ListenerPlan;
            Assert.NotEqual(previous, next);
            var oldData = new[] { 0.25f, -0.25f };
            var newData = (float[])oldData.Clone();
            fixed (float* pointer = oldData) AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, previous, pointer, 1, 2, (1L << 32) | 1);
            fixed (float* pointer = newData) AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, next, pointer, 1, 2, (1L << 32) | 1);
            Assert.Equal(new[] { 0.5f, -0.5f }, oldData);
            Assert.Equal(new[] { 0.375f, -0.125f }, newData);
            UpdateWithoutAdvancing();
            Assert.Equal(next, m_Backend.LastFrame.ListenerPlan);
            var retired = new[] { 0.25f, -0.25f };
            fixed (float* pointer = retired) AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, previous, pointer, 1, 2, (1L << 32) | 1);
            Assert.Equal(new[] { 0.25f, -0.25f }, retired);
            Assert.Empty(m_Errors);
        }

        [Fact]
        public void ListenerBypassIsIndependentOfSourceComponentBypassOnTheMeasuredRelease()
        {
            var source = Source("bypass");
            source.bypassEffects = true;
            source.SendPacket(9, null, 1, false);
            Assert.Equal(0u, m_Backend.ParamsOf(9).Flags & AudioSourceFlags.BypassListenerEffects);
            source.bypassListenerEffects = true;
            source.SendPacket(9, null, 1, false);
            Assert.NotEqual(0u, m_Backend.ParamsOf(9).Flags & AudioSourceFlags.BypassListenerEffects);
            source.bypassListenerEffects = false;
            ProjectInfo.UnityVersion = "6000.0.70f1";
            source.SendPacket(9, null, 1, false);
            Assert.NotEqual(0u, m_Backend.ParamsOf(9).Flags & AudioSourceFlags.BypassListenerEffects);
        }

        // Measured in tests/projects/audioreverb (6000.0.71f1).
        [Fact]
        public unsafe void ReverbFilterPresetsWriteAllButDryLevelAndReflectionsDelay()
        {
            var reverb = Source("reverb").gameObject.AddComponent<AudioReverbFilter>();
            Assert.Equal(AudioReverbPreset.User, reverb.reverbPreset);
            Assert.Equal((0f, 0f, 1f, 0.5f, -10000f, 0f, 0.04f), (reverb.room, reverb.reverbLevel, reverb.decayTime,
                reverb.decayHFRatio, reverb.reflectionsLevel, reverb.reflectionsDelay, reverb.reverbDelay));
            reverb.dryLevel = -300; reverb.reflectionsDelay = 0.007f; reverb.roomLF = -200; reverb.hfReference = 4000;
            reverb.room = 1e9f;
            Assert.Equal(1e9f, reverb.room);
            reverb.reverbPreset = AudioReverbPreset.Cave;
            Assert.Equal((-300f, 0.007f, 0f, 5000f, 250f), (reverb.dryLevel, reverb.reflectionsDelay, reverb.roomLF, reverb.hfReference, reverb.lfReference));
            Assert.Equal((-1000f, 0f, 2.91f, 1.3f, -602f, -302f, 0.022f), (reverb.room, reverb.roomHF, reverb.decayTime,
                reverb.decayHFRatio, reverb.reflectionsLevel, reverb.reverbLevel, reverb.reverbDelay));
            // Ignored while a preset is selected; User keeps the preset's values.
            reverb.room = 0;
            reverb.reverbPreset = AudioReverbPreset.User;
            Assert.Equal(-1000f, reverb.room);
            reverb.room = -500;
            Assert.True(reverb.Describe(out var d));
            Assert.Equal(AudioFilterKind.Reverb, d.Kind);
            var values = new float[14];
            for (int i = 0; i < 14; i++) values[i] = d.Reverb[i];
            Assert.Equal(new[] { -300f, -500f, 0f, 2.91f, 1.3f, -602f, 0.007f, -302f, 0.022f, 100f, 100f, 5000f, 0f, 250f }, values);
            Assert.Contains(AudioFilterKind.Reverb, Array.ConvertAll(Collected(reverb.GetComponent<AudioSource>()), f => f.Kind));

            // Unbound's choice: values are read as stored (the Editor writes a preset's values).
            reverb.ReadSerialized(YNode.MakeMap(("m_Room", YNode.MakeScalar("-700")), ("m_ReverbPreset", YNode.MakeScalar("9")),
                ("m_LFReference", YNode.MakeScalar("150"))), null);
            Assert.Equal((AudioReverbPreset.Cave, -700f, 150f, 0f, 1f), (reverb.reverbPreset, reverb.room, reverb.lfReference, reverb.dryLevel, reverb.decayTime));
            Assert.Empty(Errors("of AudioReverbFilter"));
        }

        [Fact]
        public void SerializedFieldsTheReaderDoesNotKnowFailLoudly()
        {
            var echo = Source("serialized").gameObject.AddComponent<AudioEchoFilter>();
            echo.ReadSerialized(YNode.MakeMap(("m_Enabled", YNode.MakeScalar("1")), ("m_Delay", YNode.MakeScalar("250")),
                ("m_DecayRatio", YNode.MakeScalar("0.25")), ("m_Unknown", YNode.MakeScalar("3"))), null);
            Assert.Equal((250f, 0.25f, 1f, 1f), (echo.delay, echo.decayRatio, echo.dryMix, echo.wetMix));
            Assert.Contains("'m_Unknown'", Assert.Single(Errors("of AudioEchoFilter")));

            var lowPass = echo.gameObject.AddComponent<AudioLowPassFilter>();
            var key = YNode.MakeMap(("time", YNode.MakeScalar("0")), ("value", YNode.MakeScalar("0.5")));
            var curve = YNode.MakeMap(("m_Curve", new YNode { Kind = YKind.Seq, Items = new[] { key } }));
            lowPass.ReadSerialized(YNode.MakeMap(("m_LowpassResonanceQ", YNode.MakeScalar("2")), ("lowpassLevelCustomCurve", curve)), null);
            Assert.Equal((5000f, 2f), (lowPass.cutoffFrequency, lowPass.lowpassResonanceQ));
            Assert.Contains("custom cutoff curve is not supported", Assert.Single(Errors("AudioLowPassFilter's serialized")));
        }
    }
}
