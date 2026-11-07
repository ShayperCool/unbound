using System;
using System.Text.Json;
using UnityEngine.Audio;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class AudioMixerFilterTests : IDisposable
    {
        // The effect DSP is measured on this release (Compat); tests of other releases set theirs.
        readonly string m_Release = ProjectInfo.UnityVersion;
        public AudioMixerFilterTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            AudioSystem.ResetForTests();
            AudioBackend.Use(new VirtualAudioBackend());
        }
        public void Dispose()
        {
            AudioSystem.ResetForTests();
            ProjectInfo.UnityVersion = m_Release;
        }

        static AudioMixerEffectController UseEffect(AudioMixer mixer, AudioMixerEffectController effect, string name, string[] parameters)
        {
            effect.m_EffectName = name; effect.m_ParameterGuids.Clear();
            foreach (string parameter in parameters)
            {
                effect.m_ParameterGuids[parameter] = parameter; mixer.m_Exposed[parameter] = parameter;
            }
            return effect;
        }

        static (AudioMixer, AudioMixerGroup, AudioMixerEffectController) Fixture()
        {
            var mixer = new AudioMixer();
            var group = new AudioMixerGroup { m_Mixer = mixer, m_Name = "Master", m_VolumeGuid = "volume" };
            var effect = new AudioMixerEffectController { m_EffectName = "Lowpass", m_EffectId = "filter" };
            effect.m_ParameterGuids["Cutoff freq"] = "cutoff";
            effect.m_ParameterGuids["Resonance"] = "resonance";
            group.m_Effects.Add(effect);
            mixer.m_MasterGroup = group;
            mixer.m_Exposed["Cutoff"] = "cutoff";
            mixer.m_Exposed["Resonance"] = "resonance";
            return (mixer, group, effect);
        }

        [Fact]
        public void NamedFilterParametersUseMeasuredDefaultsAndSnapshotOverrides()
        {
            var (mixer, group, effect) = Fixture();
            Assert.True(mixer.GetFloat("Cutoff", out float cutoff));
            Assert.Equal(5000f, cutoff);
            Assert.True(mixer.GetFloat("Resonance", out float resonance));
            Assert.Equal(1f, resonance);
            mixer.SetFloat("Cutoff", 1700f);
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
            var dsp = payload.RootElement.GetProperty("effects")[0];
            Assert.Equal("Lowpass", dsp.GetProperty("kind").GetString());
            Assert.Equal(1700f, dsp.GetProperty("cutoff").GetSingle());
            Assert.Equal("filter", dsp.GetProperty("id").GetString());
        }

        [Fact]
        public void GroupPayloadPreservesSerializedEffectOrderBypassAndAttenuation()
        {
            var (mixer, group, filter) = Fixture();
            group.m_Effects.Insert(0, new AudioMixerEffectController { m_EffectName = "Attenuation", m_EffectId = "level" });
            group.m_BypassEffects = true;
            var snapshot = new AudioMixerSnapshot { m_Mixer = mixer };
            snapshot.m_Values["volume"] = -6;
            mixer.m_StartSnapshot = snapshot;
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
            var root = payload.RootElement;
            Assert.True(root.GetProperty("bypass").GetBoolean());
            Assert.Equal("Attenuation", root.GetProperty("effects")[0].GetProperty("kind").GetString());
            Assert.Equal(0.5010594725608826f, root.GetProperty("effects")[0].GetProperty("gain").GetSingle());
            Assert.Equal("Lowpass", root.GetProperty("effects")[1].GetProperty("kind").GetString());
        }

        [Fact]
        public void ClearFloatGetterRestoresTheSnapshotWhileDspChangesOnTheNextAudioUpdate()
        {
            var (mixer, _, _) = Fixture();
            var snapshot = new AudioMixerSnapshot { m_Mixer = mixer };
            snapshot.m_Values["cutoff"] = 1000;
            snapshot.TransitionTo(0);
            mixer.GetFloat("Cutoff", out var value);
            Assert.Equal(5000, value);
            mixer.Tick(0, 0);
            mixer.GetFloat("Cutoff", out value);
            Assert.Equal(1000, value);
            mixer.SetFloat("Cutoff", 4000);
            Assert.True(mixer.ClearFloat("Cutoff"));
            mixer.GetFloat("Cutoff", out value);
            // Unity 6000.0 GetFloat contract and audiodistortion's independent captures.
            Assert.Equal(1000, value);
            Assert.Equal(4000, mixer.Value("cutoff"));
            mixer.Tick(0, 0);
            mixer.GetFloat("Cutoff", out value);
            Assert.Equal(1000, value);
        }

        [Fact]
        public void ClearFloatRetainsSubmittedOverrideUntilTheNextAudioUpdate()
        {
            var (mixer, _, _) = Fixture();
            var snapshot = new AudioMixerSnapshot { m_Mixer = mixer };
            snapshot.m_Values["cutoff"] = 1000;
            snapshot.TransitionTo(0);
            mixer.Tick(0, 0);
            mixer.SetFloat("Cutoff", 4000);
            mixer.Tick(0, 0);
            Assert.True(mixer.ClearFloat("Cutoff"));
            Assert.True(mixer.GetFloat("Cutoff", out var value));
            // mixerfilters rate-48000/rate-44100: set_lp.cutoff.8, before submission.
            Assert.Equal(4000, value);
            mixer.Tick(0, 0);
            Assert.True(mixer.GetFloat("Cutoff", out value));
            Assert.Equal(1000, value);
        }

        [Fact]
        public void ClearOfAnAppliedOverrideRetainsItsValueUntilTheAudioUpdate()
        {
            var (mixer, _, _) = Fixture();
            mixer.SetFloat("Cutoff", 4000);
            mixer.Tick(0, 0);
            Assert.True(mixer.ClearFloat("Cutoff"));
            for (int i = 0; i < 3; i++)
            {
                Assert.True(mixer.GetFloat("Cutoff", out var value));
                Assert.Equal(4000, value);
            }
            Assert.True(mixer.ClearFloat("Cutoff"));
            Assert.True(mixer.SetFloat("Cutoff", 6000));
            Assert.True(mixer.GetFloat("Cutoff", out var set));
            Assert.Equal(6000, set);
            Assert.True(mixer.ClearFloat("Cutoff"));
            Assert.True(mixer.GetFloat("Cutoff", out var cleared));
            Assert.Equal(4000, cleared);
            mixer.Tick(0, 0);
            Assert.True(mixer.GetFloat("Cutoff", out var applied));
            Assert.Equal(5000, applied);
        }

        [Fact]
        public void ClearingAnAppliedOverrideResumesTheCurrentSnapshot()
        {
            var (mixer, _, _) = Fixture();
            mixer.SetFloat("Cutoff", 4000);
            mixer.Tick(0, 0);
            var snapshot = new AudioMixerSnapshot { m_Mixer = mixer };
            snapshot.m_Values["cutoff"] = 2000;
            snapshot.TransitionTo(0);
            mixer.Tick(0, 0);
            Assert.True(mixer.GetFloat("Cutoff", out var overridden));
            Assert.Equal(4000, overridden);
            Assert.True(mixer.ClearFloat("Cutoff"));
            Assert.True(mixer.GetFloat("Cutoff", out var clearing));
            Assert.Equal(4000, clearing);
            mixer.Tick(0, 0);
            Assert.True(mixer.GetFloat("Cutoff", out var restored));
            Assert.Equal(2000, restored);
            mixer.SetFloat("Cutoff", 6000);
            mixer.ClearFloat("Cutoff");
            mixer.GetFloat("Cutoff", out var unapplied);
            Assert.Equal(2000, unapplied);
        }

        [Fact]
        public unsafe void GroupObservationDoesNotModifyTheProductionBuffer()
        {
            var (_, group, _) = Fixture();
            float* samples = stackalloc float[] { 1, 2, 3, 4 };
            int observed = 0;
            AudioFilters.ObserveGroup(group, (buffer, channels) => {
                Assert.Equal(2, channels); Assert.Equal(4, buffer.Length);
                observed++; buffer[0] = 999;
            });
            try
            {
                AudioFilters.Dispatch(AudioCallbackKind.GroupOutput, group.m_Handle, samples, 2, 2, 0);
                Assert.Equal(1, observed);
                Assert.Equal(1, samples[0]);
                Assert.Equal(4, samples[3]);
            }
            finally { AudioFilters.ObserveGroup(group, null); }
        }

        [Fact]
        public void EchoDefaultsAndExposedValuesMatchTheNativeStereoMatrix()
        {
            var (mixer, group, effect) = Fixture();
            effect.m_EffectName = "Echo";
            effect.m_ParameterGuids.Clear(); mixer.m_Exposed.Clear();
            string[] names = { "Delay", "Decay", "Max channels", "Drymix", "Wetmix" };
            float[] defaults = { 500, 0, 0, 1, 1 };
            float[] updates = { 40.125f, 0.25f, 0, 0.25f, 0.5f };
            for (int i = 0; i < names.Length; i++)
            {
                effect.m_ParameterGuids[names[i]] = names[i];
                mixer.m_Exposed[names[i]] = names[i];
            }
            for (int i = 0; i < names.Length; i++)
            {
                Assert.True(mixer.GetFloat(names[i], out float value));
                Assert.Equal(defaults[i], value);
                Assert.True(mixer.SetFloat(names[i], updates[i]));
                Assert.True(mixer.GetFloat(names[i], out value));
                Assert.Equal(updates[i], value);
            }
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
            var dsp = payload.RootElement.GetProperty("effects")[0];
            string[] fields = { "delay", "decay", "maxChannels", "dryMix", "wetMix" };
            Assert.Equal("Echo", dsp.GetProperty("kind").GetString());
            for (int i = 0; i < fields.Length; i++) Assert.Equal(updates[i], dsp.GetProperty(fields[i]).GetSingle());
            effect.m_EnableWetMix = true;
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
        }

        [Fact]
        public void SfxReverbSendsEditorDefaultsOverridesAndItsWetMix()
        {
            var (mixer, group, effect) = Fixture();
            effect.m_EffectName = "SFX Reverb";
            effect.m_ParameterGuids.Clear(); mixer.m_Exposed.Clear();
            foreach (var name in AudioMixerEffectController.SfxReverbParameters) effect.m_ParameterGuids[name] = name;
            mixer.m_Exposed["Room"] = "Room";
            Assert.True(mixer.GetFloat("Room", out float room));
            Assert.Equal(-10000f, room);
            Assert.True(mixer.SetFloat("Room", -600f));
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration())))
            {
                var dsp = payload.RootElement.GetProperty("effects")[0];
                Assert.Equal("SFX Reverb", dsp.GetProperty("kind").GetString());
                var sfx = dsp.GetProperty("sfx");
                Assert.Equal(-600f, sfx.GetProperty("room").GetSingle());
                Assert.Equal(0f, sfx.GetProperty("roomLf").GetSingle());
                Assert.Equal(0.02f, sfx.GetProperty("reflectDelay").GetSingle());
                Assert.Equal(250f, sfx.GetProperty("lfReference").GetSingle());
                Assert.Equal(JsonValueKind.Null, dsp.GetProperty("mixLevel").ValueKind);
            }
            effect.m_EnableWetMix = true; effect.m_MixLevelGuid = "wet";
            mixer.m_Exposed["Wet"] = "wet";
            Assert.True(mixer.SetFloat("Wet", -6f));
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration())))
            {
                var dsp = payload.RootElement.GetProperty("effects")[0];
                Assert.Equal(AudioMixer.DspGain(-6f), dsp.GetProperty("mixLevel").GetSingle());
                Assert.True(dsp.GetProperty("mixAudible").GetBoolean());
            }
            Assert.True(mixer.SetFloat("Wet", -70f));
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration())))
                Assert.False(payload.RootElement.GetProperty("effects")[0].GetProperty("mixAudible").GetBoolean());
            Assert.True(mixer.SetFloat("Wet", 3f));
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
        }

        [Fact]
        public void SidechainOwnershipLookupCoversEveryGroupWithoutUpdateAllocations()
        {
            var (mixer, master, target) = Fixture();
            target.m_EffectName = "Receive";
            var sender = new AudioMixerGroup { m_Mixer = mixer, m_Name = "Voice" };
            var send = new AudioMixerEffectController { m_EffectName = "Send", m_SendTarget = target };
            sender.m_Effects.Add(send); master.m_Children.Add(sender);
            mixer.Build();
            Assert.Same(master, mixer.FindEffectGroup(target));
            Assert.Same(sender, mixer.FindEffectGroup(send));
            Assert.Null(mixer.FindEffectGroup(new AudioMixerEffectController()));
            sender.ValidateSidechains();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) sender.ValidateSidechains();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            // Parameter changes and display names do not change ownership or require a rebuild.
            master.m_Name = "Output"; sender.m_Name = "New voice name";
            Assert.Same(master, mixer.FindEffectGroup(target));
            Assert.Same(sender, mixer.FindEffectGroup(send));
        }

        [Fact]
        public void CrossMixerSendTargetsFailOnBothBackendsWithoutDroppingTheTarget()
        {
            var (sender, group, send) = Fixture();
            var (receiver, _, target) = Fixture();
            send.m_EffectName = "Send"; send.m_SendTarget = target;
            receiver.outputAudioMixerGroup = group;
            foreach (Action submit in new Action[] { () => group.DspConfiguration(), () => AudioBackend.Current.UpdateGroup(group) })
                Assert.Contains("outside their mixer", Assert.Throws<NotSupportedException>(submit).Message);
            Assert.Same(target, send.m_SendTarget);
            send.m_SendTarget = null;
            group.DspConfiguration(); AudioBackend.Current.UpdateGroup(group);
        }

        [Theory]
        [InlineData("Send")]
        [InlineData("Receive")]
        [InlineData("Duck Volume")]
        public void SidechainAcceptanceIsSharedAndReleaseSpecific(string kind)
        {
            var (_, group, effect) = Fixture(); effect.m_EffectName = kind;
            foreach (Action submit in new Action[] { () => group.DspConfiguration(), () => AudioBackend.Current.UpdateGroup(group) })
            {
                ProjectInfo.UnityVersion = "6000.0.72f1";
                Assert.Contains("6000.0.71f1", Assert.Throws<NotSupportedException>(submit).Message);
                ProjectInfo.UnityVersion = "6000.0.71f1";
                effect.m_EnableWetMix = true;
                Assert.Contains("wet mixing", Assert.Throws<NotSupportedException>(submit).Message);
                effect.m_EnableWetMix = false;
                submit();
            }
        }

        [Theory]
        [InlineData("Send", "level")]
        [InlineData("Duck Volume", "Threshold")]
        [InlineData("Duck Volume", "Ratio")]
        [InlineData("Duck Volume", "Attack Time")]
        [InlineData("Duck Volume", "Release Time")]
        [InlineData("Duck Volume", "Make-up Gain")]
        [InlineData("Duck Volume", "Knee")]
        [InlineData("Duck Volume", "Sidechain Mix")]
        public void TimedSnapshotsRejectOnlyChangingUnmeasuredSidechainParameters(string kind, string parameter)
        {
            var (mixer, group, effect) = Fixture();
            UseEffect(mixer, effect, kind, kind == "Send" ? Array.Empty<string>() : AudioMixerEffectController.DuckParameters);
            if (kind == "Send") { effect.m_MixLevelGuid = parameter; mixer.m_Exposed[parameter] = parameter; }
            mixer.Build();
            float original = mixer.Value(parameter);
            float change = parameter == "Sidechain Mix" ? -0.25f : 1f;
            var target = new AudioMixerSnapshot { m_Mixer = mixer };
            target.m_Values[parameter] = original + change;
            Assert.Contains("timed snapshot", Assert.Throws<NotSupportedException>(() => target.TransitionTo(0.1f)).Message);
            Assert.Throws<NotSupportedException>(() => mixer.TransitionToSnapshots(new[] { target }, new[] { 1f }, 0.2f));
            Assert.Equal(original, mixer.Value(parameter));
            // Immediate changes retain detector history; the mixerduck captures measure them.
            target.TransitionTo(0f); mixer.Tick(0f, 0f);
            Assert.Equal(original + change, mixer.Value(parameter));
            // Changing only a group fader on this mixer remains supported.
            target.m_Values["volume"] = -12f;
            target.TransitionTo(0.2f); mixer.Tick(0.2f, 0.2f);
            Assert.Equal(-12f, mixer.Value("volume"));
            target.m_Values[parameter] = original;
            Assert.True(mixer.SetFloat(parameter, original + 2f * change));
            target.TransitionTo(0.2f);
            Assert.Throws<NotSupportedException>(() => mixer.ClearFloat(parameter));
            Assert.Equal(original + 2f * change, mixer.Value(parameter));
            mixer.Tick(0.2f, 0.2f);
            Assert.Equal(original + 2f * change, mixer.Value(parameter));
            Assert.True(mixer.ClearFloat(parameter));
            target.m_Values[parameter] = original + change;
            Assert.Throws<NotSupportedException>(() => target.TransitionTo(0.2f));
        }

        [Fact]
        public void WeightedSnapshotsWithIdenticalSidechainValuesCanChangeOnlyTheirFaders()
        {
            var (mixer, _, effect) = Fixture();
            UseEffect(mixer, effect, "Duck Volume", AudioMixerEffectController.DuckParameters);
            mixer.Build();
            var snapshots = new AudioMixerSnapshot[3];
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i] = new AudioMixerSnapshot { m_Mixer = mixer };
                snapshots[i].m_Values["volume"] = -3f * i;
            }
            mixer.TransitionToSnapshots(snapshots, new[] { 0.11f, 0.29f, 0.63f }, 0.5f);
            mixer.Tick(0.1f, 0.1f);
            Assert.True(mixer.Value("volume") < 0f);
        }

        [Fact]
        public void SendsTargetADuckWithMeasuredDefaultsAndIgnoreBypass()
        {
            var (mixer, master, duck) = Fixture();
            duck.m_EffectName = "Duck Volume"; duck.m_EffectId = "duck";
            duck.m_ParameterGuids.Clear(); mixer.m_Exposed.Clear();
            foreach (var name in AudioMixerEffectController.DuckParameters) duck.m_ParameterGuids[name] = name;
            var send = new AudioMixerEffectController { m_EffectName = "Send", m_EffectId = "send", m_MixLevelGuid = "level", m_SendTarget = duck };
            var voice = new AudioMixerGroup { m_Mixer = mixer, m_Name = "Voice", m_VolumeGuid = "voice" };
            voice.m_Effects.Add(send);
            master.m_Children.Add(voice);
            mixer.m_Exposed["Release"] = "Release Time";
            mixer.m_Exposed["Send"] = "level";
            // Native's unset Release Time is 0.3 s (the Editor shows 0.1); a new Send is at -80 dB.
            Assert.True(mixer.GetFloat("Release", out float release));
            Assert.Equal(0.3f, release);
            Assert.True(mixer.GetFloat("Send", out float level));
            Assert.Equal(-80f, level);
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(voice.DspConfiguration())))
            {
                var dsp = payload.RootElement.GetProperty("effects")[0];
                Assert.Equal("duck", dsp.GetProperty("sendTarget").GetString());
                Assert.Equal(0f, dsp.GetProperty("sendLevel").GetSingle());
            }
            Assert.NotEqual(0UL, master.m_Handle); // the target's group is registered
            Assert.True(mixer.SetFloat("Send", -6f));
            send.m_Bypass = true; voice.m_BypassEffects = true;
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(voice.DspConfiguration())))
                Assert.Equal(AudioMixer.DspGain(-6f), payload.RootElement.GetProperty("effects")[0].GetProperty("sendLevel").GetSingle());
            duck.m_Bypass = true;
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(master.DspConfiguration())))
            {
                var config = payload.RootElement.GetProperty("effects")[0].GetProperty("duck");
                Assert.Equal(-10f, config.GetProperty("threshold").GetSingle());
                Assert.Equal(0.3f, config.GetProperty("releaseTime").GetSingle());
                Assert.Equal(1f, config.GetProperty("sidechainMix").GetSingle());
            }
            mixer.m_Exposed["Mix"] = "Sidechain Mix";
            Assert.True(mixer.SetFloat("Mix", 1.5f));
            Assert.Throws<NotSupportedException>(() => master.DspConfiguration());
            Assert.True(mixer.SetFloat("Mix", 0.5f));
            Assert.True(mixer.SetFloat("Send", 3f));
            Assert.Throws<NotSupportedException>(() => voice.DspConfiguration());
            duck.m_EnableWetMix = true;
            Assert.Throws<NotSupportedException>(() => master.DspConfiguration());
        }

        [Fact]
        public void CompressorParamEqAndSimpleFiltersSendMeasuredDefaults()
        {
            var (mixer, group, compressor) = Fixture();
            compressor.m_EffectName = "Compressor"; compressor.m_EffectId = "comp";
            compressor.m_ParameterGuids.Clear(); mixer.m_Exposed.Clear();
            foreach (var name in AudioMixerEffectController.CompressorParameters) compressor.m_ParameterGuids[name] = name;
            var eq = new AudioMixerEffectController { m_EffectName = "ParamEQ", m_EffectId = "eq" };
            eq.m_ParameterGuids["Frequency gain"] = "eqgain";
            var high = new AudioMixerEffectController { m_EffectName = "Highpass Simple", m_EffectId = "high" };
            high.m_ParameterGuids["Cutoff freq"] = "highcut";
            var low = new AudioMixerEffectController { m_EffectName = "Lowpass Simple", m_EffectId = "low" };
            group.m_Effects.Add(eq); group.m_Effects.Add(high); group.m_Effects.Add(low);
            mixer.m_Exposed["Threshold"] = "Threshold";
            mixer.m_Exposed["HighCut"] = "highcut";
            Assert.True(mixer.GetFloat("Threshold", out float threshold));
            Assert.Equal(0f, threshold);
            Assert.True(mixer.GetFloat("HighCut", out float highCut));
            Assert.Equal(1000f, highCut);
            Assert.True(mixer.SetFloat("Threshold", -20f));
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration())))
            {
                var effects = payload.RootElement.GetProperty("effects");
                var config = effects[0].GetProperty("compressor");
                Assert.Equal(-20f, config.GetProperty("threshold").GetSingle());
                Assert.Equal(50f, config.GetProperty("attack").GetSingle());
                Assert.Equal(50f, config.GetProperty("release").GetSingle());
                Assert.Equal(0f, config.GetProperty("makeupGain").GetSingle());
                var peak = effects[1].GetProperty("eq");
                Assert.Equal(8000f, peak.GetProperty("center").GetSingle());
                Assert.Equal(1f, peak.GetProperty("octaves").GetSingle());
                Assert.Equal(1f, peak.GetProperty("gain").GetSingle());
                Assert.Equal(1000f, effects[2].GetProperty("cutoff").GetSingle());
                Assert.Equal(5000f, effects[3].GetProperty("cutoff").GetSingle());
            }
            // Values outside the Editor's ranges are unmeasured, unless the unit is bypassed.
            Assert.True(mixer.SetFloat("Threshold", -70f));
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
            compressor.m_Bypass = true;
            using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration())))
                Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("effects")[0].GetProperty("compressor").ValueKind);
            Assert.True(mixer.SetFloat("HighCut", 23000f));
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
            high.m_Bypass = true;
            group.DspConfiguration();
            compressor.m_EnableWetMix = true; compressor.m_Bypass = false;
            Assert.True(mixer.SetFloat("Threshold", -10f));
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
        }

        [Fact]
        public void DistortionExposedLevelKeepsRawValuesWhileDspClampsAndWetMixFails()
        {
            var (mixer, group, effect) = Fixture();
            effect.m_EffectName = "Distortion"; effect.m_ParameterGuids.Clear();
            effect.m_ParameterGuids["Level"] = "level"; mixer.m_Exposed["Level"] = "level";
            Assert.True(mixer.GetFloat("Level", out float initial)); Assert.Equal(0.5f, initial);
            foreach (float value in new[] { -1f, 0f, 0.37f, 1f, 2f })
            {
                Assert.True(mixer.SetFloat("Level", value));
                Assert.True(mixer.GetFloat("Level", out float raw)); Assert.Equal(value, raw);
                using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
                Assert.Equal(Mathf.Clamp01(value), payload.RootElement.GetProperty("effects")[0].GetProperty("distortion").GetSingle());
            }
            effect.m_EnableWetMix = true;
            Assert.Contains("wet mixing", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
            effect.m_Bypass = true; group.DspConfiguration();
            effect.m_EnableWetMix = false; effect.m_Bypass = false;
            mixer.SetFloat("Level", float.NaN);
            Assert.Contains("finite", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
        }

        [Fact]
        public void ChorusClampsDspKeepsExposedValuesAndRefusesSlotWetMixing()
        {
            var (mixer, group, effect) = Fixture();
            effect.m_EffectName = "Chorus"; effect.m_ParameterGuids.Clear();
            for (int i = 0; i < AudioMixerEffectController.ChorusParameters.Length; i++)
            {
                string name = AudioMixerEffectController.ChorusParameters[i];
                effect.m_ParameterGuids[name] = name; mixer.m_Exposed[name] = name;
            }
            Assert.True(mixer.GetFloat("Depth", out float depth)); Assert.Equal(0.03f, depth);
            Assert.True(mixer.SetFloat("Delay", -1f)); Assert.True(mixer.SetFloat("Rate", 200f));
            Assert.True(mixer.SetFloat("Wet mix tap 3", 200f));
            Assert.True(mixer.GetFloat("Delay", out float delay)); Assert.Equal(-1f, delay);
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
            var chorus = payload.RootElement.GetProperty("effects")[0].GetProperty("chorus");
            Assert.Equal(0f, chorus.GetProperty("delay").GetSingle());
            Assert.Equal(20f, chorus.GetProperty("rate").GetSingle());
            // Wet mix tap 3 keeps its exposed value but has no measured effect on stereo output.
            Assert.True(mixer.GetFloat("Wet mix tap 3", out float tap3)); Assert.Equal(200f, tap3);
            Assert.False(chorus.TryGetProperty("wet3", out _));
            effect.m_EnableWetMix = true;
            Assert.Contains("wet mixing", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
            effect.m_Bypass = true; group.DspConfiguration();
            effect.m_Bypass = false; effect.m_EnableWetMix = false;
            mixer.SetFloat("Depth", float.NaN);
            Assert.Contains("finite", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
        }

        [Fact]
        public void FlangeDefaultsRawSettersDspClampsAndUnsupportedWetMixing()
        {
            var (mixer, group, effect) = Fixture();
            effect.m_EffectName = "Flange"; effect.m_ParameterGuids.Clear();
            foreach (string name in AudioMixerEffectController.FlangeParameters)
            {
                effect.m_ParameterGuids[name] = name; mixer.m_Exposed[name] = name;
            }
            foreach (var pair in new[] { ("Drymix", 0.45f), ("Wetmix", 0.55f), ("Depth", 1f), ("Rate", 0.1f) })
            {
                Assert.True(mixer.GetFloat(pair.Item1, out float value)); Assert.Equal(pair.Item2, value);
            }
            Assert.False(mixer.SetFloat("Feedback", 1f)); Assert.False(mixer.SetFloat("Delay", 20f));
            Assert.True(mixer.SetFloat("Depth", -1f)); Assert.True(mixer.SetFloat("Rate", 40f));
            Assert.True(mixer.SetFloat("Drymix", 2f)); Assert.True(mixer.SetFloat("Wetmix", -1f));
            Assert.True(mixer.GetFloat("Depth", out float raw)); Assert.Equal(-1f, raw);
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
            var flange = payload.RootElement.GetProperty("effects")[0].GetProperty("flange");
            Assert.Equal(0.01f, flange.GetProperty("depth").GetSingle());
            Assert.Equal(20f, flange.GetProperty("rate").GetSingle());
            Assert.Equal(1f, flange.GetProperty("dry").GetSingle());
            Assert.Equal(0f, flange.GetProperty("wet").GetSingle());
            effect.m_EnableWetMix = true;
            Assert.Contains("wet mixing", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
            effect.m_Bypass = true; group.DspConfiguration();
            effect.m_Bypass = false; effect.m_EnableWetMix = false;
            mixer.SetFloat("Depth", float.NaN);
            Assert.Contains("finite", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
            group.m_BypassEffects = true; group.DspConfiguration();
        }

        [Fact]
        public void NormalizeDefaultsRawSettersDspClampsAndUnsupportedWetMixing()
        {
            var (mixer, group, effect) = Fixture();
            effect.m_EffectName = "Normalize"; effect.m_ParameterGuids.Clear();
            foreach (string name in AudioMixerEffectController.NormalizeParameters)
            {
                effect.m_ParameterGuids[name] = name; mixer.m_Exposed[name] = name;
            }
            foreach (var pair in new[] { ("Fade in time", 5000f), ("Lowest volume", 0.1f), ("Maximum amp", 20f) })
            {
                Assert.True(mixer.GetFloat(pair.Item1, out float value)); Assert.Equal(pair.Item2, value);
            }
            Assert.False(mixer.SetFloat("Missing", 1f));
            mixer.SetFloat("Fade in time", -1f); mixer.SetFloat("Lowest volume", 2f); mixer.SetFloat("Maximum amp", 200000f);
            Assert.True(mixer.GetFloat("Maximum amp", out float raw)); Assert.Equal(200000f, raw);
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
            var normalize = payload.RootElement.GetProperty("effects")[0].GetProperty("normalize");
            Assert.Equal(0f, normalize.GetProperty("fade").GetSingle());
            Assert.Equal(1f, normalize.GetProperty("lowest").GetSingle());
            Assert.Equal(100000f, normalize.GetProperty("maximum").GetSingle());
            effect.m_EnableWetMix = true;
            Assert.Contains("wet mixing", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
            effect.m_Bypass = true; group.DspConfiguration();
            effect.m_Bypass = false; effect.m_EnableWetMix = false;
            mixer.SetFloat("Fade in time", float.NaN);
            Assert.Contains("finite", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
            group.m_BypassEffects = true; group.DspConfiguration();
        }

        [Fact]
        public void PitchShifterKeepsRawValuesAndRejectsUnmeasuredContexts()
        {
            string release = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                var (mixer, group, effect) = Fixture();
                effect.m_EffectName = "Pitch Shifter"; effect.m_ParameterGuids.Clear();
                foreach (string name in AudioMixerEffectController.PitchShifterParameters)
                { effect.m_ParameterGuids[name] = name; mixer.m_Exposed[name] = name; }
                foreach (var pair in new[] { ("Pitch", 1f), ("FFT size", 1024f), ("Overlap", 4f), ("Max channels", 0f) })
                {
                    Assert.True(mixer.GetFloat(pair.Item1, out float value)); Assert.Equal(pair.Item2, value);
                }
                foreach (float raw in new[] { -1f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f })
                {
                    Assert.True(mixer.SetFloat("Pitch", raw));
                    Assert.True(mixer.GetFloat("Pitch", out float value)); Assert.Equal(raw, value);
                    using var payload = JsonDocument.Parse(JsonSerializer.Serialize(group.DspConfiguration()));
                    Assert.Equal(Mathf.Clamp(raw, 0.5f, 2f), payload.RootElement.GetProperty("effects")[0].GetProperty("pitchShifter").GetProperty("pitch").GetSingle());
                }
                foreach (var pair in new[] { ("FFT size", 2048f, 1024f), ("Overlap", 8f, 4f), ("Max channels", 1f, 0f) })
                {
                    mixer.SetFloat(pair.Item1, pair.Item2);
                    Assert.Contains("requires measured defaults", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
                    mixer.SetFloat(pair.Item1, pair.Item3);
                }
                mixer.SetFloat("Pitch", float.NaN);
                Assert.Contains("finite", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
                mixer.SetFloat("Pitch", 1f); effect.m_EnableWetMix = true;
                Assert.Contains("wet mixing", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
                effect.m_EnableWetMix = false;
                ProjectInfo.UnityVersion = "6000.0.72f1";
                Assert.Contains("6000.0.71f1", Assert.Throws<NotSupportedException>(() => group.DspConfiguration()).Message);
                effect.m_Bypass = true; group.DspConfiguration();
            }
            finally { ProjectInfo.UnityVersion = release; }
        }

        [Theory]
        [InlineData("Chorus")]
        [InlineData("Flange")]
        [InlineData("Normalize")]
        [InlineData("Distortion")]
        [InlineData("Pitch Shifter")]
        public void MeasuredEffectsFailOnOtherReleasesUnlessBypassed(string name)
        {
            var (mixer, group, effect) = Fixture();
            var parameters = name == "Chorus" ? AudioMixerEffectController.ChorusParameters
                : name == "Flange" ? AudioMixerEffectController.FlangeParameters
                : name == "Normalize" ? AudioMixerEffectController.NormalizeParameters
                : name == "Pitch Shifter" ? AudioMixerEffectController.PitchShifterParameters
                : new[] { "Level" };
            UseEffect(mixer, effect, name, parameters);
            group.DspConfiguration();
            ProjectInfo.UnityVersion = "6000.0.72f1";
            var error = Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
            Assert.Contains("AudioMixer " + name + " is measured only for Unity 6000.0.71f1", error.Message);
            effect.m_Bypass = true; group.DspConfiguration();
            effect.m_Bypass = false; group.m_BypassEffects = true; group.DspConfiguration();
        }

        [Theory]
        [InlineData("Normalize")]
        [InlineData("Pitch Shifter")]
        public void LayoutLimitedEffectsFailOnMonoAndOtherUnmeasuredOutputs(string name)
        {
            var (mixer, group, effect) = Fixture();
            UseEffect(mixer, effect, name, name == "Normalize" ? AudioMixerEffectController.NormalizeParameters : AudioMixerEffectController.PitchShifterParameters);
            var measured = AudioSettings.GetConfiguration();
            Assert.Equal(AudioSpeakerMode.Stereo, measured.speakerMode);
            group.DspConfiguration();
            var mono = measured; mono.speakerMode = AudioSpeakerMode.Mono;
            var quad = measured; quad.speakerMode = AudioSpeakerMode.Quad;
            var rate = measured; rate.sampleRate = 22050;
            var buffer = measured; buffer.dspBufferSize = 256;
            foreach (var config in new[] { mono, quad, rate, buffer })
            {
                Assert.True(AudioSettings.Reset(config));
                var error = Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
                Assert.Contains("measured only at 48000/44100 Hz with 1024-frame stereo output", error.Message);
                effect.m_Bypass = true; group.DspConfiguration(); effect.m_Bypass = false;
            }
            var other = measured; other.sampleRate = 44100;
            Assert.True(AudioSettings.Reset(other));
            group.DspConfiguration();
        }

        [Theory]
        [InlineData("6000.0.71f1", 4000f)]
        [InlineData("6000.0.72f1", 5000f)]
        [InlineData("not a release", 5000f)]
        public void ClearFloatGetterReadsTheSnapshotExceptAMeasuredSubmittedOverride(string release, float submittedRead)
        {
            var (mixer, _, _) = Fixture();
            ProjectInfo.UnityVersion = release;
            // Cleared before the audio update submits it: the documented snapshot value everywhere.
            mixer.SetFloat("Cutoff", 4000);
            Assert.True(mixer.ClearFloat("Cutoff"));
            Assert.True(mixer.GetFloat("Cutoff", out float value));
            Assert.Equal(5000f, value);
            // Cleared after submission: only the measured release keeps reading it until the update.
            mixer.SetFloat("Cutoff", 4000);
            mixer.Tick(0, 0);
            Assert.True(mixer.ClearFloat("Cutoff"));
            Assert.True(mixer.GetFloat("Cutoff", out value));
            Assert.Equal(submittedRead, value);
            mixer.Tick(0, 0);
            Assert.True(mixer.GetFloat("Cutoff", out value));
            Assert.Equal(5000f, value);
        }

        [Fact]
        public void ActiveUnsupportedEffectsAndWetMixFailLoudly()
        {
            var (_, group, effect) = Fixture();
            effect.m_EffectName = "Unmeasured Effect";
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
            effect.m_Bypass = true;
            group.DspConfiguration();
            effect.m_Bypass = false; effect.m_EffectName = "Lowpass"; effect.m_EnableWetMix = true;
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
        }
    }
}
