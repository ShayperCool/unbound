using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AudioReverbZone as the 6000.0.71f1 Editor measures it (tests/projects/audioreverb): the
    /// defaults, presets, setter clamps, the blend of the zones heard at the listener into one SFX
    /// Reverb, the zone descriptor of the frame packet and each AudioSource's send gain. The probe
    /// checks the sound; these tests pin the values the mixer receives.
    /// </summary>
    public sealed unsafe class AudioReverbZoneTests : IDisposable
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();

        public AudioReverbZoneTests()
        {
            AudioSystem.ResetForTests();
            AudioBackend.Use(m_Backend);
        }

        public void Dispose()
        {
            foreach (var go in m_Objects)
                if (go != null) Object.DestroyImmediate(go);
            AudioSystem.ResetForTests();
        }

        AudioReverbZone Zone(float x, AudioReverbPreset preset = AudioReverbPreset.Generic)
        {
            var go = new GameObject("zone");
            m_Objects.Add(go);
            go.transform.position = new Vector3(x, 0f, 0f);
            var zone = go.AddComponent<AudioReverbZone>();
            zone.reverbPreset = preset;
            return zone;
        }

        static float[] Blend(Vector3 listener, params AudioReverbZone[] zones)
        {
            var r = new float[14];
            fixed (float* p = r) AudioReverbZone.Blend(new List<AudioReverbZone>(zones), listener, p);
            return r;
        }

        [Fact]
        public void PresetsWriteEverythingButTheDistancesAndSettersClampUnderAnyPreset()
        {
            var zone = Zone(0f);
            Assert.Equal((AudioReverbPreset.Generic, 10f, 15f, -1000, -100, 0, 1.49f, 0.83f, -2602, 0.007f, 200, 0.011f),
                (zone.reverbPreset, zone.minDistance, zone.maxDistance, zone.room, zone.roomHF, zone.roomLF, zone.decayTime,
                 zone.decayHFRatio, zone.reflections, zone.reflectionsDelay, zone.reverb, zone.reverbDelay));
            Assert.Equal((5000f, 250f, 100f, 100f), (zone.HFReference, zone.LFReference, zone.diffusion, zone.density));

            zone.reverbPreset = AudioReverbPreset.Cave;
            zone.room = -500;
            Assert.Equal((AudioReverbPreset.Cave, -500, 0, 2.91f, 1.3f, -602, 0.015f, -302, 0.022f),
                (zone.reverbPreset, zone.room, zone.roomHF, zone.decayTime, zone.decayHFRatio, zone.reflections,
                 zone.reflectionsDelay, zone.reverb, zone.reverbDelay));

            zone.reverbPreset = AudioReverbPreset.User;
            zone.roomLF = -200;
            zone.reflectionsDelay = 0.05f;
            zone.HFReference = 4000f;
            zone.LFReference = 150f;
            zone.minDistance = 2f;
            zone.maxDistance = 4f;
            zone.reverbPreset = AudioReverbPreset.Forest;
            Assert.Equal((2f, 4f, -1000, -3300, 0, 0.162f, 5000f, 250f, 79f), (zone.minDistance, zone.maxDistance, zone.room,
                zone.roomHF, zone.roomLF, zone.reflectionsDelay, zone.HFReference, zone.LFReference, zone.diffusion));

            var fresh = Zone(0f);
            fresh.maxDistance = -1e9f;
            fresh.minDistance = -1e9f;
            fresh.room = 1000000000;
            fresh.reverb = 1000000000;
            fresh.reflections = -1000000000;
            fresh.decayTime = 1e9f;
            fresh.decayHFRatio = -1e9f;
            fresh.reflectionsDelay = 1e9f;
            fresh.reverbDelay = 1e9f;
            fresh.HFReference = -1e9f;
            fresh.LFReference = 1e9f;
            fresh.diffusion = -1e9f;
            Assert.Equal((10f, 0f, 0, 2000, -10000, 20f, 0.1f, 0.3f, 0.1f, 1000f, 1000f, 0f, AudioReverbPreset.Generic),
                (fresh.maxDistance, fresh.minDistance, fresh.room, fresh.reverb, fresh.reflections, fresh.decayTime,
                 fresh.decayHFRatio, fresh.reflectionsDelay, fresh.reverbDelay, fresh.HFReference, fresh.LFReference,
                 fresh.diffusion, fresh.reverbPreset));
        }

        [Fact]
        public void ZonesBlendAtTheListenerAsTheEditorMeasures()
        {
            var generic = Zone(0f);
            // Full weight: the levels move up to the next whole mB, as measured (-1000 sounds as -999).
            var r = Blend(Vector3.zero, generic);
            Assert.Equal(new[] { -10000f, -999f, -99f }, r[..3]);
            Assert.Equal((-2601f, 200f, 0f), (r[5], r[7], r[12]));
            Assert.Equal((1.49f, 0.83f, 0.007f, 0.011f, 100f, 100f), (r[3], r[4], r[6], r[8], r[9], r[10]));
            Assert.Equal(5000f, r[11], 0.01f);
            Assert.Equal(250f, r[13], 0.001f);

            // Half heard (12.5 between 10 and 15): every parameter is weighted without dividing,
            // the references fall to 5000^0.5 and 250^0.5 and clamp to 1000 and 20 Hz.
            r = Blend(new Vector3(12.5f, 0f, 0f), generic);
            Assert.Equal(new[] { -1346f, -446f, -2948f, -146f, -346f }, new[] { r[1], r[2], r[5], r[7], r[12] });
            Assert.Equal((0.745f, 0.415f, 0.0035f, 0.0055f, 50f, 50f, 1000f, 20f),
                (r[3], r[4], r[6], r[8], r[9], r[10], r[11], r[13]));

            // Two full zones: the mean, levels as e^(mB/500).
            r = Blend(Vector3.zero, generic, Zone(0f, AudioReverbPreset.Cave));
            Assert.Equal(new[] { -999f, -47f, -938f, 10f, 0f }, new[] { r[1], r[2], r[5], r[7], r[12] });
            Assert.Equal(2.2f, r[3], 1e-6f);
            Assert.Equal(1.065f, r[4], 1e-6f);
            Assert.Equal(0.011f, r[6], 1e-9f);
            Assert.Equal(0.0165f, r[8], 1e-9f);

            // Out of range: the Off room alone (-10000 + 0.5 truncates to -9999) with every other
            // parameter at its lower clamp.
            r = Blend(new Vector3(20f, 0f, 0f), generic);
            Assert.Equal(new[] { -10000f, -9999f, -10000f, 0.1f, 0.1f, -10000f, 0f, -10000f, 0f, 0f, 0f, 1000f, -10000f, 20f }, r);
        }

        [Fact]
        public void TheFramePacketCarriesTheZoneAndRestartsItAfterTheLastZoneWent()
        {
            AudioSystem.Update();
            Assert.Equal(0, m_Backend.LastFrame.Zone.Kind);
            var zone = Zone(0f);
            AudioSystem.Update();
            var first = m_Backend.LastFrame.Zone;
            Assert.Equal(AudioFilterKind.Reverb, first.Kind);
            Assert.Equal(-999f, first.Reverb[1]);
            var second = Zone(30f);
            AudioSystem.Update();
            Assert.Equal(first.Id, m_Backend.LastFrame.Zone.Id);
            Object.DestroyImmediate(zone.gameObject);
            second.enabled = false;
            AudioSystem.Update();
            Assert.Equal(0, m_Backend.LastFrame.Zone.Kind);
            second.enabled = true;
            AudioSystem.Update();
            Assert.Equal(AudioFilterKind.Reverb, m_Backend.LastFrame.Zone.Kind);
            Assert.NotEqual(first.Id, m_Backend.LastFrame.Zone.Id);
            Assert.Equal(-9999f, m_Backend.LastFrame.Zone.Reverb[1]);
        }

        [Fact]
        public void DistanceCurvesReachThePacketWithoutAUniformSamplingTable()
        {
            string release = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                var go = new GameObject("curved-source");
                m_Objects.Add(go);
                go.transform.position = new Vector3(0, 0, 5);
                var source = go.AddComponent<AudioSource>();
                source.maxDistance = 20;
                var curve = new AnimationCurve(new Keyframe(0, 0.2f, 0, 0),
                    new Keyframe(0.4f, 0.8f, 0, 0), new Keyframe(1, 0.3f, 0, 0));
                source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, curve);
                source.SetCustomCurve(AudioSourceCurveType.Spread, curve);
                AudioSystem.s_HearingPosition = Vector3.zero;
                source.SendPacket(19, null, 1, false);
                var packet = m_Backend.ParamsOf(19);
                Assert.Equal(curve.Evaluate(0.25f), packet.SpatialBlend);
                Assert.Equal(curve.Evaluate(0.25f) * 360f, packet.Spread);
                Assert.Equal(1, packet.CurveLength);
                Assert.NotEqual(0u, packet.Flags & AudioSourceFlags.SpatialStereo);
                AudioSystem.s_HearingPosition = go.transform.position;
                source.SendPacket(19, null, 1, false);
                Assert.Equal(0.2f, m_Backend.ParamsOf(19).SpatialBlend);
                ProjectInfo.UnityVersion = "6000.0.70f1";
                source.spatialBlend = 0.5f;
                source.SendPacket(19, null, 1, false);
                Assert.Equal(64, m_Backend.ParamsOf(19).CurveLength);
                Assert.Equal(0u, m_Backend.ParamsOf(19).Flags & AudioSourceFlags.SpatialStereo);
            }
            finally { ProjectInfo.UnityVersion = release; }
        }

        [Fact]
        public void TheSendGainIsTheMixInWholeMillibels()
        {
            var go = new GameObject("source");
            m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            Assert.Equal(1f, source.ReverbZoneSend());
            source.reverbZoneMix = 0.5f;
            Assert.Equal(MathF.Pow(10f, -602f / 2000f), source.ReverbZoneSend());
            source.reverbZoneMix = 1.1f;
            Assert.Equal(MathF.Pow(10f, 0.5f), source.ReverbZoneSend(), 1e-5f);
            source.reverbZoneMix = 0f;
            Assert.Equal(1e-5f, source.ReverbZoneSend(), 1e-9f);
            source.reverbZoneMix = 1f;
            source.bypassReverbZones = true;
            Assert.Equal(0f, source.ReverbZoneSend());
        }
    }
}
