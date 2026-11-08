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
        readonly string m_Release = ProjectInfo.UnityVersion;

        public AudioReverbZoneTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            AudioSystem.ResetForTests();
            AudioBackend.Use(m_Backend);
        }

        public void Dispose()
        {
            foreach (var go in m_Objects)
                if (go != null) Object.DestroyImmediate(go);
            AudioSystem.ResetForTests();
            ProjectInfo.UnityVersion = m_Release;
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

        sealed class Errors : ILogHandler
        {
            internal readonly List<string> Messages = new List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Messages.Add(string.Format(format, args));
            }
            public void LogException(Exception error, Object context) => throw error;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MidPlaybackZoneActivationFailsClearlyAndCanRecoverAfterStopping(bool reactivate)
        {
            var errors = new Errors();
            var previous = Debug.unityLogger.logHandler;
            var delta = Time.s_RealDeltaTime;
            var clip = AudioClip.Create("zone-activation", 32768, 1, 48000, false);
            try
            {
                Debug.unityLogger.logHandler = errors;
                AudioReverbZone zone = reactivate ? Zone(0) : null;
                if (zone != null) zone.enabled = false;
                var host = new GameObject("source"); m_Objects.Add(host);
                var source = host.AddComponent<AudioSource>(); source.clip = clip; source.Play();
                Time.s_RealDeltaTime = 0.01;
                AudioSystem.Update();
                if (reactivate) zone.enabled = true;
                else zone = Zone(0);
                AudioSystem.Update();
                Assert.Single(errors.Messages);
                Assert.Contains("activating zones during playback is not supported", errors.Messages[0]);
                Assert.Equal(0, m_Backend.LastFrame.Zone.Kind);
                Assert.True(source.isPlaying);
                source.Stop();
                zone.enabled = false; zone.enabled = true;
                AudioSystem.Update();
                Assert.Equal(AudioFilterKind.Reverb, m_Backend.LastFrame.Zone.Kind);
                Assert.Single(errors.Messages);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Time.s_RealDeltaTime = delta;
                Object.DestroyImmediate(clip);
            }
        }

        [Fact]
        public void ANewOneShotAfterStopCanStartAlongsideANewZone()
        {
            var errors = new Errors();
            var previous = Debug.unityLogger.logHandler;
            var clip = AudioClip.Create("zone-start", 32768, 1, 48000, false);
            try
            {
                Debug.unityLogger.logHandler = errors;
                var host = new GameObject("source"); m_Objects.Add(host);
                var source = host.AddComponent<AudioSource>(); source.clip = clip;
                source.Play(); AudioSystem.Update(); source.Stop();
                source.PlayOneShot(clip);
                Zone(0);
                AudioSystem.Update();
                Assert.Empty(errors.Messages);
                Assert.Equal(AudioFilterKind.Reverb, m_Backend.LastFrame.Zone.Kind);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(clip);
            }
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

            zone.reverbPreset = AudioReverbPreset.Cave;
            Assert.Equal(-500, zone.room);
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

        [Theory]
        [InlineData(AudioReverbPreset.Generic, 0.02f, 0f, 0.04f)]
        [InlineData(AudioReverbPreset.Cave, 0.015f, -301f, 0.022f)]
        public void FirstZoneRetainsOnlyUnchangedGenericDefaults(AudioReverbPreset preset, float early, float level, float late)
        {
            var zone = Zone(0f, preset);
            AudioSystem.Update();
            var first = m_Backend.LastFrame.Zone;
            Assert.Equal((early, level, late), (first.Reverb[6], first.Reverb[7], first.Reverb[8]));
            AudioSystem.Update();
            Assert.Equal(early, m_Backend.LastFrame.Zone.Reverb[6]);
            zone.reflectionsDelay = 0.023f;
            AudioSystem.Update();
            Assert.Equal(early, m_Backend.LastFrame.Zone.Reverb[6]);
            zone.reverbPreset = AudioReverbPreset.User;
            AudioSystem.Update();
            Assert.Equal(0.023f, m_Backend.LastFrame.Zone.Reverb[6]);
            Assert.Equal(late, m_Backend.LastFrame.Zone.Reverb[8]);
            zone.enabled = false;
            AudioSystem.Update();
            zone.enabled = true;
            AudioSystem.Update();
            Assert.Equal(zone.reverbDelay, m_Backend.LastFrame.Zone.Reverb[8]);
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
                var ear = new GameObject("curve-listener");
                m_Objects.Add(ear);
                ear.transform.position = go.transform.position;
                ear.AddComponent<AudioListener>();
                source.UpdateMotion(0);
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

        [Theory]
        [InlineData(AudioSourceCurveType.CustomRolloff)]
        [InlineData(AudioSourceCurveType.SpatialBlend)]
        [InlineData(AudioSourceCurveType.Spread)]
        [InlineData(AudioSourceCurveType.ReverbZoneMix)]
        public void CustomCurvesNormalizeLongRangesWithoutChangingTheCaller(AudioSourceCurveType type)
        {
            var go = new GameObject("source"); m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            var first = new Keyframe(-2f, 0.2f, 0.1f, -0.2f) { weightedMode = WeightedMode.Both, inWeight = 0.2f, outWeight = 0.4f };
            var last = new Keyframe(4f, 0.8f, 0.3f, -0.4f);
            var curve = new AnimationCurve(last, first) { postWrapMode = WrapMode.Loop };
            source.SetCustomCurve(type, curve);
            var stored = source.GetCustomCurve(type);
            Assert.Equal(-0.5f, stored[0].time);
            Assert.Equal(1f, stored[1].time);
            Assert.Equal(0.4f, stored[0].inTangent);
            Assert.Equal(-0.8f, stored[0].outTangent);
            Assert.Equal(first.weightedMode, stored[0].weightedMode);
            Assert.Equal(first.inWeight, stored[0].inWeight);
            Assert.Equal(first.outWeight, stored[0].outWeight);
            Assert.Equal(WrapMode.ClampForever, stored.postWrapMode);
            Assert.Equal(-2f, curve[0].time);
            Assert.Equal(4f, curve[1].time);
            Assert.Equal(WrapMode.Loop, curve.postWrapMode);
            stored.ClearKeys();
            Assert.Equal(2, source.GetCustomCurve(type).length);
        }

        [Theory]
        [InlineData(-2f, -0.5f)]
        [InlineData(-0.2f, 0.5f)]
        [InlineData(0f, 1f)]
        public void CustomCurveRangesEndingAtOrBeforeOneStayUnscaled(float start, float end)
        {
            var go = new GameObject("source"); m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            foreach (AudioSourceCurveType type in Enum.GetValues<AudioSourceCurveType>())
            {
                source.SetCustomCurve(type, AnimationCurve.Linear(start, 0.2f, end, 0.8f));
                var stored = source.GetCustomCurve(type);
                Assert.Equal(start, stored[0].time);
                Assert.Equal(end, stored[1].time);
            }
        }

        [Theory]
        [InlineData(AudioReverbPreset.Generic)]
        [InlineData(AudioReverbPreset.Forest)]
        public void ScalarValuesWaitForUserBeforeReplacingTheSelectedPreset(AudioReverbPreset preset)
        {
            var zone = Zone(0f, preset);
            var selected = Blend(Vector3.zero, zone);
            zone.room = -200;
            zone.roomHF = -300;
            zone.roomLF = -400;
            zone.decayTime = 0.75f;
            zone.decayHFRatio = 0.25f;
            zone.reflections = -500;
            zone.reflectionsDelay = 0.025f;
            zone.reverb = -600;
            zone.reverbDelay = 0.035f;
            zone.HFReference = 8000f;
            zone.LFReference = 500f;
            zone.diffusion = 30f;
            zone.density = 40f;
            Assert.Equal(selected, Blend(Vector3.zero, zone));
            Assert.Equal(-200, zone.room);
            zone.reverbPreset = AudioReverbPreset.User;
            var custom = Blend(Vector3.zero, zone);
            Assert.Equal(-199f, custom[1]);
            Assert.Equal(0.025f, custom[6]);
            Assert.Equal(0.035f, custom[8]);
            Assert.Equal(30f, custom[9]);
            Assert.Equal(40f, custom[10]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZoneActivationAfterDirectBackendRenderingStillReportsTheUnsupportedTransition(bool anotherZoneIsActive)
        {
            if (anotherZoneIsActive) Zone(0);
            var host = new GameObject("rendered-source"); m_Objects.Add(host);
            var source = host.AddComponent<AudioSource>();
            var clip = AudioClip.Create("tone", 48000, 1, 48000, false);
            source.clip = clip;
            source.Play();
            Assert.False(source.m_HasAudioFrame);
            m_Backend.Frame(new AudioFramePacket { DeltaTime = 0.04 });
            var previous = Debug.unityLogger.logHandler;
            var errors = new Errors();
            Debug.unityLogger.logHandler = errors;
            try
            {
                Zone(0);
                Assert.Contains(errors.Messages, message => message.Contains("activating zones during playback is not supported"));
            }
            finally { Debug.unityLogger.logHandler = previous; Object.DestroyImmediate(clip); }
        }

        [Fact]
        public void ResetAndNonHearingListenerRemovalPreserveTheHearingPose()
        {
            var other = new GameObject("other-listener"); m_Objects.Add(other);
            var otherListener = other.AddComponent<AudioListener>();
            var ear = new GameObject("hearing-listener"); m_Objects.Add(ear);
            ear.transform.position = Vector3.right * 9;
            ear.AddComponent<AudioListener>().velocityUpdateMode = AudioVelocityUpdateMode.Fixed;
            Assert.Equal(Vector3.right * 9, AudioSystem.s_HearingPose.Position);
            ear.transform.position = Vector3.right * 12;
            otherListener.enabled = false;
            Assert.Equal(Vector3.right * 9, AudioSystem.s_HearingPose.Position);
            var configuration = AudioSettings.GetConfiguration();
            Assert.True(AudioSettings.Reset(configuration));
            Assert.Equal(Vector3.right * 12, AudioSystem.s_HearingPose.Position);
            var host = new GameObject("source"); m_Objects.Add(host);
            var source = host.AddComponent<AudioSource>();
            source.UpdateMotion(0);
            source.SendPacket(19, null, 1, false);
            Assert.Equal(Vector3.right * 12, m_Backend.ParamsOf(19).DopplerListenerPosition);
        }

        [Fact]
        public void FrameSourceUsesTheLatestListenerPoseAndVelocity()
        {
            double previous = Time.s_FrameDeltaTime;
            try
            {
                Time.s_FrameDeltaTime = 0.04;
                var ear = new GameObject("frame-listener"); m_Objects.Add(ear);
                ear.AddComponent<AudioListener>().velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
                var host = new GameObject("frame-source"); m_Objects.Add(host);
                var source = host.AddComponent<AudioSource>();
                source.velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
                AudioSystem.Update();
                ear.transform.position = Vector3.forward;
                ear.transform.rotation = Quaternion.Euler(0, 90, 0);
                AudioSystem.Update();
                var packet = m_Backend.ParamsOf(source.Handle);
                Assert.Equal(Vector3.forward, packet.ListenerPosition);
                Assert.Equal(Vector3.forward, packet.DopplerListenerPosition);
                Assert.Equal(ear.transform.forward, packet.ListenerForward);
                Assert.Equal(-25f, packet.Velocity.z);
            }
            finally { Time.s_FrameDeltaTime = previous; }
        }

        [Fact]
        public void SourceDopplerKeepsTheListenerVelocityFromItsSelectedLoop()
        {
            var previous = Time.s_FrameDeltaTime;
            try
            {
                Time.s_FrameDeltaTime = 0.04;
                var ear = new GameObject("listener"); m_Objects.Add(ear);
                ear.AddComponent<AudioListener>().velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
                var host = new GameObject("source"); m_Objects.Add(host);
                var source = host.AddComponent<AudioSource>();
                source.velocityUpdateMode = AudioVelocityUpdateMode.Fixed;
                AudioSystem.Update(); AudioSystem.FixedUpdate();
                ear.transform.position = Vector3.forward;
                AudioSystem.Update(); AudioSystem.FixedUpdate();
                Assert.Equal(-25f, m_Backend.ParamsOf(source.Handle).Velocity.z);
                Assert.Equal(Vector3.forward, m_Backend.ParamsOf(source.Handle).ListenerPosition);
                Assert.Equal(Vector3.forward, m_Backend.ParamsOf(source.Handle).DopplerListenerPosition);
                Assert.NotEqual(0u, m_Backend.ParamsOf(source.Handle).Flags & AudioSourceFlags.ListenerSnapshot);
                ear.transform.position = Vector3.forward * 3;
                AudioSystem.Update();
                Assert.Equal(50f, AudioSystem.s_HearingVelocity.z);
                Assert.Equal(-25f, m_Backend.ParamsOf(source.Handle).Velocity.z);
                Assert.Equal(Vector3.forward, m_Backend.ParamsOf(source.Handle).DopplerListenerPosition);
                AudioSystem.FixedUpdate();
                Assert.Equal(-50f, m_Backend.ParamsOf(source.Handle).Velocity.z);
                Assert.Equal(Vector3.forward * 3, m_Backend.ParamsOf(source.Handle).DopplerListenerPosition);
            }
            finally { Time.s_FrameDeltaTime = previous; }
        }

        [Fact]
        public void FixedSamplesReachTheBackendWithoutResamplingTheDynamicListener()
        {
            var previous = Time.s_FrameDeltaTime;
            try
            {
                Time.s_FrameDeltaTime = 0.04;
                var host = new GameObject("source"); m_Objects.Add(host);
                var source = host.AddComponent<AudioSource>();
                source.velocityUpdateMode = AudioVelocityUpdateMode.Fixed;
                var ear = new GameObject("listener"); m_Objects.Add(ear);
                var listener = ear.AddComponent<AudioListener>();
                listener.velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
                AudioSystem.Update();
                AudioSystem.FixedUpdate();
                host.transform.position = Vector3.forward;
                ear.transform.position = Vector3.right;
                AudioSystem.FixedUpdate();
                var packet = m_Backend.ParamsOf(source.Handle);
                Assert.Equal(Vector3.forward, packet.Position);
                Assert.Equal(source.m_Velocity, packet.Velocity);
                Assert.True(packet.Velocity.z > 0);
                Assert.Equal(Vector3.zero, m_Backend.LastFrame.ListenerPosition);
                Assert.Equal(Vector3.zero, m_Backend.LastFrame.ListenerVelocity);
                listener.velocityUpdateMode = AudioVelocityUpdateMode.Fixed;
                AudioSystem.FixedUpdate();
                ear.transform.position = Vector3.right * 2;
                AudioSystem.FixedUpdate();
                Assert.Equal(Vector3.right * 2, m_Backend.LastFrame.ListenerPosition);
                Assert.Equal(listener.m_Velocity, m_Backend.LastFrame.ListenerVelocity);
                Assert.True(m_Backend.LastFrame.ListenerVelocity.x > 0);
                ear.transform.position = Vector3.right * 3;
                host.transform.position = Vector3.forward * 2;
                AudioSystem.Update();
                Assert.Equal(Vector3.right * 2, m_Backend.LastFrame.ListenerPosition);
                Assert.Equal(Vector3.forward, m_Backend.ParamsOf(source.Handle).Position);
            }
            finally { Time.s_FrameDeltaTime = previous; }
        }

        [Fact]
        public void FixedStepRoundTripsThroughTheMeasuredClockBeforePhysicsAndAudio()
        {
            float previous = Time.fixedDeltaTime;
            try
            {
                Time.fixedDeltaTime = 0.0232199468f;
                Assert.Equal(0.0232199412f, Time.fixedDeltaTime);
                Assert.Equal((float)Time.FixedClockDuration, Time.fixedDeltaTime);
            }
            finally { Time.fixedDeltaTime = previous; }
        }

        [Theory]
        [InlineData(AudioRolloffMode.Logarithmic)]
        [InlineData(AudioRolloffMode.Linear)]
        [InlineData(AudioRolloffMode.Custom)]
        public void SettingCustomRolloffPreservesTheSelectedMode(AudioRolloffMode mode)
        {
            var go = new GameObject("source"); m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            source.rolloffMode = mode;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(-2, 0.2f, 4, 0.8f));
            Assert.Equal(mode, source.rolloffMode);
            Assert.Equal(1f, source.GetCustomCurve(AudioSourceCurveType.CustomRolloff)[1].time);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PlayResetsTransformVelocityAtTheCurrentPosition(bool fixedStep)
        {
            var go = new GameObject("source"); m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            source.m_HasLastPosition = true;
            source.m_MotionInFixedStep = fixedStep;
            source.m_LastPosition = new Vector3(0, 0, 10);
            source.m_Velocity = new Vector3(0, 0, 20);
            go.transform.position = new Vector3(0, 0, 11);
            source.Play();
            Assert.Equal(Vector3.zero, source.m_Velocity);
            Assert.Equal(new Vector3(0, 0, 11), source.m_LastPosition);
            source.UpdateMotion(0.05);
            Assert.Equal(Vector3.zero, source.m_Velocity);
        }

        [Fact]
        public void PlayPreservesTheOwnBodyVelocityAlreadySampledOnTheSelectedLoop()
        {
            var go = new GameObject("source"); m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>();
            var body = go.AddComponent<Rigidbody>();
            body.linearVelocity = new Vector3(0, 0, -7);
            source.m_HasLastPosition = true;
            source.m_Velocity = new Vector3(0, 0, 20);
            source.m_LastPosition = Vector3.back;
            go.transform.position = Vector3.forward;
            source.Play();
            Assert.Equal(new Vector3(0, 0, 20), source.m_Velocity);
            Assert.Equal(Vector3.forward, source.m_LastPosition);
        }

        [Theory]
        [InlineData(WrapMode.Default, 0.2f)]
        [InlineData(WrapMode.Loop, 0.8f)]
        [InlineData(WrapMode.PingPong, 0.8f)]
        public void DistanceCurvesHonorTheirWrapModeBeyondMaximumDistance(WrapMode mode, float expected)
        {
            string release = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                var go = new GameObject("wrapping-source");
                m_Objects.Add(go);
                go.transform.position = new Vector3(0, 0, 40);
                var source = go.AddComponent<AudioSource>();
                source.maxDistance = 20;
                var curve = AnimationCurve.Linear(0, 0.8f, 1, 0.2f);
                curve.postWrapMode = mode;
                source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, curve);
                source.SetCustomCurve(AudioSourceCurveType.Spread, curve);
                source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, curve);
                AudioSystem.s_HearingPosition = Vector3.zero;
                source.SendPacket(19, null, 1, false);
                Assert.Equal(expected, m_Backend.ParamsOf(19).SpatialBlend);
                Assert.Equal(expected * 360f, m_Backend.ParamsOf(19).Spread);
                float send = source.ReverbZoneSend();
                source.reverbZoneMix = expected;
                Assert.Equal(source.ReverbZoneSend(), send);
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
