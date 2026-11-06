using System;
using System.Text.Json;
using UnityEngine.Audio;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class AudioMixerFilterTests : IDisposable
    {
        public AudioMixerFilterTests()
        {
            AudioSystem.ResetForTests();
            AudioBackend.Use(new VirtualAudioBackend());
        }
        public void Dispose() => AudioSystem.ResetForTests();

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
        public void ClearFloatAndImmediateSnapshotBecomeVisibleOnTheNextAudioUpdate()
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
            Assert.Equal(4000, value);
            mixer.Tick(0, 0);
            mixer.GetFloat("Cutoff", out value);
            Assert.Equal(1000, value);
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
        public void ActiveUnsupportedEffectsAndWetMixFailLoudly()
        {
            var (_, group, effect) = Fixture();
            effect.m_EffectName = "Chorus";
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
            effect.m_Bypass = true;
            group.DspConfiguration();
            effect.m_Bypass = false; effect.m_EffectName = "Lowpass"; effect.m_EnableWetMix = true;
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
        }
    }
}
