using System;
using System.Collections.Generic;
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
    /// curve, components on the AudioListener and unread serialized fields fail loudly. The wire
    /// layout is NativeApi 18's (added at 16), checked against the same offsets in
    /// crates/unbound-runtime/src/audio/protocol.rs. These tests do not measure the DSP; the Rust
    /// mixer tests check the kernel wiring and no capture establishes component parity.
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

        AudioSource Source(string name)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            return go.AddComponent<AudioSource>();
        }

        AudioFilterDescriptor[] Collected(AudioSource source, bool generator = false)
        {
            AudioFilterDescriptor[] table = null;
            int count = AudioFilterChain.Collect(source, generator, ref table);
            return count == 0 ? Array.Empty<AudioFilterDescriptor>() : table.AsSpan(0, count).ToArray();
        }

        static ulong Id(Component c) => unchecked((ulong)(long)c.GetInstanceID());

        [Fact]
        public void PacketAndDescriptorLayoutsUnchangedInNativeApi28()
        {
            // Added at 16; 20 adds reverb parameters, 21 the frame zone; 22 adds render commands,
            // 23 adds Distortion, 24 adds Chorus, 25-27 mixer effects and 28 render protocol 12
            // without changing the audio descriptor layout.
            Assert.Equal(28u, NativeApi.CurrentVersion);
            var frame = default(AudioFramePacket);
            byte* f = (byte*)&frame;
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
            Assert.Equal(136, sizeof(AudioSourcePacket));
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

#pragma warning disable CS0618 // the obsolete resonance spellings are part of the API
        [Fact]
        public void ComponentsStartAtTheScriptingApiDefaults()
        {
            var go = Source("defaults").gameObject;
            var lowPass = go.AddComponent<AudioLowPassFilter>();
            var highPass = go.AddComponent<AudioHighPassFilter>();
            var echo = go.AddComponent<AudioEchoFilter>();
            Assert.Equal((5000f, 1f, 1f), (lowPass.cutoffFrequency, lowPass.lowpassResonanceQ, lowPass.lowpassResonaceQ));
            Assert.Equal((5000f, 1f, 1f), (highPass.cutoffFrequency, highPass.highpassResonanceQ, highPass.highpassResonaceQ));
            Assert.Equal((500f, 0.5f, 1f, 1f), (echo.delay, echo.decayRatio, echo.dryMix, echo.wetMix));
            lowPass.lowpassResonaceQ = 4f;
            highPass.highpassResonaceQ = 6f;
            Assert.Equal((4f, 6f), (lowPass.lowpassResonanceQ, highPass.highpassResonanceQ));
            Assert.Equal(new[] { AudioFilterKind.LowPass, AudioFilterKind.HighPass, AudioFilterKind.Echo },
                Array.ConvertAll(Collected(go.GetComponent<AudioSource>()), f => f.Kind));
        }
#pragma warning restore CS0618

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
            Assert.Equal((10f, 0f, 1f, 0f), (d[2].Delay, d[2].Decay, d[2].DryMix, d[2].WetMix));
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
            Assert.Empty(Collected(source));
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
#pragma warning restore CS0618
            filter.enabled = false; Assert.Empty(Collected(source)); filter.enabled = true;
            filter.depth = float.NaN;
            Assert.Empty(Collected(source)); Assert.Empty(Collected(source));
            Assert.Single(Errors("AudioChorusFilter"));
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
        public void ComponentsFollowComponentOrderAndOnlyEnabledOnesCount()
        {
            var source = Source("order");
            var echo = source.gameObject.AddComponent<AudioEchoFilter>();
            var lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            var highPass = source.gameObject.AddComponent<AudioHighPassFilter>();
            lowPass.enabled = false;
            var d = Collected(source);
            Assert.Equal(new[] { Id(echo), Id(highPass) }, Array.ConvertAll(d, f => f.Id));
            Assert.Equal(new[] { AudioFilterKind.Echo, AudioFilterKind.HighPass }, Array.ConvertAll(d, f => f.Kind));
            Assert.All(d, f => Assert.Equal(AudioFilterStage.BeforeScripts, f.Stage));
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
            for (int i = 0; i <= AudioFilterDescriptor.MaxPerSource; i++) many.gameObject.AddComponent<AudioEchoFilter>();
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
            Assert.Empty(m_Backend.FiltersOf(source.m_Handle));
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
                ("kind", valid with { Kind = 0 }), ("kind", valid with { Kind = AudioFilterKind.Last + 1 }), ("stage", valid with { Stage = 2 }),
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

        [Fact]
        public void OnlyReverbFiltersRunOnTheListener()
        {
            var go = new GameObject("ears");
            m_Objects.Add(go);
            var listener = go.AddComponent<AudioListener>();
            go.AddComponent<AudioLowPassFilter>();
            var reverb = go.AddComponent<AudioReverbFilter>();
            AudioFilterDescriptor[] table = null;
            Assert.Equal(1, AudioFilterChain.CollectListener(listener, false, ref table));
            Assert.Equal(1, AudioFilterChain.CollectListener(listener, false, ref table));
            Assert.Equal(AudioFilterKind.Reverb, table[0].Kind);
            Assert.Equal(unchecked((ulong)(long)reverb.GetInstanceID()), table[0].Id);
            Assert.Contains("AudioLowPassFilter on the AudioListener 'ears' is not supported", Assert.Single(Errors("AudioListener 'ears'")));

            var scripted = new GameObject("scripted ears");
            m_Objects.Add(scripted);
            var other = scripted.AddComponent<AudioListener>();
            scripted.AddComponent<AudioReverbFilter>();
            Assert.Equal(0, AudioFilterChain.CollectListener(other, true, ref table));
            Assert.Contains("listener filter components are not applied", Assert.Single(Errors("OnAudioFilterRead scripts on the AudioListener 'scripted ears'")));

            var shared = new GameObject("source ears");
            m_Objects.Add(shared);
            var third = shared.AddComponent<AudioListener>();
            shared.AddComponent<AudioSource>();
            shared.AddComponent<AudioReverbFilter>();
            Assert.Equal(0, AudioFilterChain.CollectListener(third, false, ref table));
            Assert.Contains("runs only in that source's chain", Assert.Single(Errors("AudioListener 'source ears'")));
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
