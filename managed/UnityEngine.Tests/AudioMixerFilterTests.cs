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
            Assert.InRange(root.GetProperty("effects")[0].GetProperty("gain").GetSingle(), 0.50118f, 0.50120f);
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
        public void ActiveUnsupportedEffectsAndWetMixFailLoudly()
        {
            var (_, group, effect) = Fixture();
            effect.m_EffectName = "Echo";
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
            effect.m_Bypass = true;
            group.DspConfiguration();
            effect.m_Bypass = false; effect.m_EffectName = "Lowpass"; effect.m_EnableWetMix = true;
            Assert.Throws<NotSupportedException>(() => group.DspConfiguration());
        }
    }
}
