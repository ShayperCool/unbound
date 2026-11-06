using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEngine
{
    // The AudioSource filter components the mixer runs (docs/AUDIO.md). Unity 6000.0: "The ordering
    // of the components is important, since it reflects the order in which the effects will be
    // applied to the source audio", and an effect "is applied only when its component is enabled"
    // (Manual, Use audio filters); an OnAudioFilterRead script "is inserted in the same order as the
    // MonoBehaviour script is shown in the Inspector" (Scripting API). The DSP reuses the AudioMixer
    // Lowpass, Highpass and Echo kernels measured on mixer groups; no capture measures them as
    // AudioSource components. Defaults and ranges are the 6000.0 Scripting API's. The descriptor
    // clamps to those ranges. AudioLowPassFilter.cutoffFrequency clamps on set, as its Scripting
    // API page documents ("Values outside this range are clamped"); the other property pages do not
    // say, so whether Unity clamps them on set is unmeasured and they return what was set.
    // Serialized values are read as stored. Parameters reach the mixer with the source's next packet (at the latest the
    // next audio update).

    /// <summary>Passes the low frequencies of an AudioSource. Cutoff 10 to 22000 Hz (default 5000;
    /// the 6000.0 Manual gives 0.0 to 22000.0, the Scripting API 10.0 to 22000.0), resonance Q 1 to
    /// 10 (default 1). On an AudioListener, and the custom cutoff curve, are unsupported and fail
    /// loudly.</summary>
    [ClassId(169)]
    [RequireComponent(typeof(AudioBehaviour))]
    public sealed partial class AudioLowPassFilter : Behaviour
    {
        internal float m_CutoffFrequency = 5000f;
        internal float m_LowpassResonanceQ = 1f;
        bool m_ReportedInvalid;

        /// <summary>Clamped to 10 to 22000 on set (6000.0 Scripting API,
        /// AudioLowPassFilter.cutoffFrequency).</summary>
        public float cutoffFrequency
        {
            get => m_CutoffFrequency;
            set => m_CutoffFrequency = Mathf.Clamp(value, 10f, 22000f);
        }

        public float lowpassResonanceQ
        {
            get => m_LowpassResonanceQ;
            set => m_LowpassResonanceQ = value;
        }

        [Obsolete("AudioLowPassFilter.lowpassResonaceQ is obsolete. Use lowpassResonanceQ instead (UnityUpgradable) -> lowpassResonanceQ")]
        public float lowpassResonaceQ
        {
            get => lowpassResonanceQ;
            set => lowpassResonanceQ = value;
        }

        /// <summary>The distance-driven cutoff curve is not implemented: reading or setting it
        /// throws rather than leaving the cutoff silently fixed.</summary>
        public AnimationCurve customCutoffCurve
        {
            get => throw AudioFilterChain.CustomCutoffCurve();
            set => throw AudioFilterChain.CustomCutoffCurve();
        }

        internal bool Describe(out AudioFilterDescriptor d)
        {
            d = new AudioFilterDescriptor
            {
                Kind = AudioFilterKind.LowPass,
                Cutoff = Mathf.Clamp(m_CutoffFrequency, 10f, 22000f),
                Resonance = Mathf.Clamp(m_LowpassResonanceQ, 1f, 10f),
            };
            return AudioFilterChain.Finite(this, ref m_ReportedInvalid, m_CutoffFrequency, m_LowpassResonanceQ);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (AudioLowPassFilter)source;
            m_CutoffFrequency = s.m_CutoffFrequency;
            m_LowpassResonanceQ = s.m_LowpassResonanceQ;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_CutoffFrequency = body.Float("m_CutoffFrequency", 5000f);
            m_LowpassResonanceQ = body.Float("m_LowpassResonanceQ", 1f);
            if (body.Get("lowpassLevelCustomCurve") is YNode curve && curve.Kind == YKind.Map && (curve.Get("m_Curve")?.Count ?? 0) > 0)
                Debug.LogError("Unbound: AudioLowPassFilter's serialized custom cutoff curve is not supported; '" + m_GameObject?.m_Name + "' runs at a fixed cutoff of " + m_CutoffFrequency + " Hz", this);
            AudioFilterChain.CheckSerialized(this, body, "m_CutoffFrequency", "m_LowpassResonanceQ", "lowpassLevelCustomCurve");
        }
    }

    /// <summary>Passes the high frequencies of an AudioSource. Cutoff 10 to 22000 Hz (default
    /// 5000), resonance Q 1 to 10 (default 1).</summary>
    [ClassId(165)]
    [RequireComponent(typeof(AudioBehaviour))]
    public sealed partial class AudioHighPassFilter : Behaviour
    {
        internal float m_CutoffFrequency = 5000f;
        internal float m_HighpassResonanceQ = 1f;
        bool m_ReportedInvalid;

        public float cutoffFrequency
        {
            get => m_CutoffFrequency;
            set => m_CutoffFrequency = value;
        }

        public float highpassResonanceQ
        {
            get => m_HighpassResonanceQ;
            set => m_HighpassResonanceQ = value;
        }

        [Obsolete("AudioHighPassFilter.highpassResonaceQ is obsolete. Use highpassResonanceQ instead (UnityUpgradable) -> highpassResonanceQ")]
        public float highpassResonaceQ
        {
            get => highpassResonanceQ;
            set => highpassResonanceQ = value;
        }

        internal bool Describe(out AudioFilterDescriptor d)
        {
            d = new AudioFilterDescriptor
            {
                Kind = AudioFilterKind.HighPass,
                Cutoff = Mathf.Clamp(m_CutoffFrequency, 10f, 22000f),
                Resonance = Mathf.Clamp(m_HighpassResonanceQ, 1f, 10f),
            };
            return AudioFilterChain.Finite(this, ref m_ReportedInvalid, m_CutoffFrequency, m_HighpassResonanceQ);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (AudioHighPassFilter)source;
            m_CutoffFrequency = s.m_CutoffFrequency;
            m_HighpassResonanceQ = s.m_HighpassResonanceQ;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_CutoffFrequency = body.Float("m_CutoffFrequency", 5000f);
            m_HighpassResonanceQ = body.Float("m_HighpassResonanceQ", 1f);
            AudioFilterChain.CheckSerialized(this, body, "m_CutoffFrequency", "m_HighpassResonanceQ");
        }
    }

    /// <summary>Repeats an AudioSource's sound after a delay. Delay 10 to 5000 ms (default 500),
    /// decay ratio 0 to 1 (default 0.5), dry and wet mix 0 to 1 (default 1).</summary>
    [ClassId(168)]
    [RequireComponent(typeof(AudioBehaviour))]
    public sealed partial class AudioEchoFilter : Behaviour
    {
        internal float m_Delay = 500f;
        internal float m_DecayRatio = 0.5f;
        internal float m_DryMix = 1f;
        internal float m_WetMix = 1f;
        bool m_ReportedInvalid;

        public float delay
        {
            get => m_Delay;
            set => m_Delay = value;
        }

        public float decayRatio
        {
            get => m_DecayRatio;
            set => m_DecayRatio = value;
        }

        public float dryMix
        {
            get => m_DryMix;
            set => m_DryMix = value;
        }

        public float wetMix
        {
            get => m_WetMix;
            set => m_WetMix = value;
        }

        internal bool Describe(out AudioFilterDescriptor d)
        {
            d = new AudioFilterDescriptor
            {
                Kind = AudioFilterKind.Echo,
                Delay = Mathf.Clamp(m_Delay, 10f, 5000f),
                Decay = Mathf.Clamp01(m_DecayRatio),
                DryMix = Mathf.Clamp01(m_DryMix),
                WetMix = Mathf.Clamp01(m_WetMix),
            };
            return AudioFilterChain.Finite(this, ref m_ReportedInvalid, m_Delay, m_DecayRatio, m_DryMix, m_WetMix);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (AudioEchoFilter)source;
            m_Delay = s.m_Delay;
            m_DecayRatio = s.m_DecayRatio;
            m_DryMix = s.m_DryMix;
            m_WetMix = s.m_WetMix;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Delay = body.Float("m_Delay", 500f);
            m_DecayRatio = body.Float("m_DecayRatio", 0.5f);
            m_DryMix = body.Float("m_DryMix", 1f);
            m_WetMix = body.Float("m_WetMix", 1f);
            AudioFilterChain.CheckSerialized(this, body, "m_Delay", "m_DecayRatio", "m_DryMix", "m_WetMix");
        }
    }

    /// <summary>
    /// The AudioMixer SFX Reverb on one AudioSource (tests/projects/audioreverb, 6000.0.71f1). A
    /// new component is <see cref="AudioReverbPreset.User"/> with the values the Editor gives it.
    /// Selecting another preset writes its values except Dry Level and Reflections Delay;
    /// selecting User keeps the current ones. The
    /// parameter setters store what they get (no clamping) and are ignored while a preset other
    /// than User is selected, as measured. The DSP clamps to the mixer effect's ranges; its
    /// response outside the documented ranges is unmeasured.
    /// </summary>
    [ClassId(164)]
    [RequireComponent(typeof(AudioBehaviour))]
    public sealed partial class AudioReverbFilter : Behaviour
    {
        // The AudioMixer SFX Reverb parameter order (crates/unbound-runtime/src/audio/reverb.rs).
        const int Dry = 0, Room = 1, RoomHF = 2, DecayTime = 3, DecayHFRatio = 4, Reflections = 5,
            ReflectionsDelay = 6, Reverb = 7, ReverbDelay = 8, Diffusion = 9, Density = 10, HFReference = 11,
            RoomLF = 12, LFReference = 13;
        static readonly string[] s_Fields = {
            "m_DryLevel", "m_Room", "m_RoomHF", "m_DecayTime", "m_DecayHFRatio", "m_ReflectionsLevel",
            "m_ReflectionsDelay", "m_ReverbLevel", "m_ReverbDelay", "m_Diffusion", "m_Density", "m_HFReference",
            "m_RoomLF", "m_LFReference",
        };
        static readonly float[] s_Defaults = { 0f, 0f, 0f, 1f, 0.5f, -10000f, 0f, 0f, 0.04f, 100f, 100f, 5000f, 0f, 250f };
        // Room, Room HF, Decay Time, Decay HF Ratio, Reflections, Reverb, Reverb Delay, Diffusion and
        // Density of each preset from Off to Psychotic, as the Editor reports them. A preset also
        // sets Room LF to 0 and the references to 5000 and 250 Hz; it keeps Dry Level and
        // Reflections Delay.
        static readonly float[,] s_Presets = {
            { -10000f, -10000f, 1f, 1f, -2602f, 200f, 0.011f, 0f, 0f },
            { -1000f, -100f, 1.49f, 0.83f, -2602f, 200f, 0.011f, 100f, 100f },
            { -1000f, -6000f, 0.17f, 0.1f, -1204f, 207f, 0.002f, 100f, 100f },
            { -1000f, -454f, 0.4f, 0.83f, -1646f, 53f, 0.003f, 100f, 100f },
            { -1000f, -1200f, 1.49f, 0.54f, -370f, 1030f, 0.011f, 100f, 60f },
            { -1000f, -6000f, 0.5f, 0.1f, -1376f, -1104f, 0.004f, 100f, 100f },
            { -1000f, -300f, 2.31f, 0.64f, -711f, 83f, 0.017f, 100f, 100f },
            { -1000f, -476f, 4.32f, 0.59f, -789f, -289f, 0.03f, 100f, 100f },
            { -1000f, -500f, 3.92f, 0.7f, -1230f, -2f, 0.029f, 100f, 100f },
            { -1000f, 0f, 2.91f, 1.3f, -602f, -302f, 0.022f, 100f, 100f },
            { -1000f, -698f, 7.24f, 0.33f, -1166f, 16f, 0.03f, 100f, 100f },
            { -1000f, -1000f, 10.05f, 0.23f, -602f, 198f, 0.03f, 100f, 100f },
            { -1000f, -4000f, 0.3f, 0.1f, -1831f, -1630f, 0.03f, 100f, 100f },
            { -1000f, -300f, 1.49f, 0.59f, -1219f, 441f, 0.011f, 100f, 100f },
            { -1000f, -237f, 2.7f, 0.79f, -1214f, 395f, 0.02f, 100f, 100f },
            { -1000f, -270f, 1.49f, 0.86f, -1204f, -4f, 0.011f, 100f, 100f },
            { -1000f, -3300f, 1.49f, 0.54f, -2560f, -229f, 0.088f, 79f, 100f },
            { -1000f, -800f, 1.49f, 0.67f, -2273f, -1691f, 0.011f, 50f, 100f },
            { -1000f, -2500f, 1.49f, 0.21f, -2780f, -1434f, 0.1f, 27f, 100f },
            { -1000f, -1000f, 1.49f, 0.83f, -10000f, 500f, 0.025f, 100f, 100f },
            { -1000f, -2000f, 1.49f, 0.5f, -2466f, -1926f, 0.1f, 21f, 100f },
            { -1000f, 0f, 1.65f, 1.5f, -1363f, -1153f, 0.012f, 100f, 100f },
            { -1000f, -1000f, 2.81f, 0.14f, 429f, 1023f, 0.021f, 80f, 60f },
            { -1000f, -4000f, 1.49f, 0.1f, -449f, 1700f, 0.011f, 100f, 100f },
            { -1000f, 0f, 8.39f, 1.39f, -115f, 985f, 0.03f, 100f, 100f },
            { -1000f, -400f, 17.23f, 0.56f, -1713f, -613f, 0.03f, 100f, 100f },
            { -1000f, -151f, 7.56f, 0.91f, -626f, 774f, 0.03f, 100f, 100f },
        };
        static readonly int[] s_PresetColumns = { Room, RoomHF, DecayTime, DecayHFRatio, Reflections, Reverb, ReverbDelay, Diffusion, Density };

        internal readonly float[] m_Values = (float[])s_Defaults.Clone();
        internal AudioReverbPreset m_ReverbPreset = AudioReverbPreset.User;
        bool m_ReportedInvalid;

        public AudioReverbPreset reverbPreset
        {
            get => m_ReverbPreset;
            set
            {
                if (value < AudioReverbPreset.Off || value > AudioReverbPreset.User)
                {
                    Debug.LogError("Unbound: AudioReverbFilter.reverbPreset " + (int)value + " is not a preset; '" + m_GameObject?.m_Name + "' keeps " + m_ReverbPreset, this);
                    return;
                }
                m_ReverbPreset = value;
                if (value == AudioReverbPreset.User) return;
                for (int c = 0; c < s_PresetColumns.Length; c++) m_Values[s_PresetColumns[c]] = s_Presets[(int)value, c];
                m_Values[RoomLF] = 0f;
                m_Values[HFReference] = 5000f;
                m_Values[LFReference] = 250f;
            }
        }

        float Get(int index) => m_Values[index];
        void Set(int index, float value)
        {
            if (m_ReverbPreset == AudioReverbPreset.User) m_Values[index] = value;
        }

        public float dryLevel { get => Get(Dry); set => Set(Dry, value); }
        public float room { get => Get(Room); set => Set(Room, value); }
        public float roomHF { get => Get(RoomHF); set => Set(RoomHF, value); }
        public float roomLF { get => Get(RoomLF); set => Set(RoomLF, value); }
        public float decayTime { get => Get(DecayTime); set => Set(DecayTime, value); }
        public float decayHFRatio { get => Get(DecayHFRatio); set => Set(DecayHFRatio, value); }
        public float reflectionsLevel { get => Get(Reflections); set => Set(Reflections, value); }
        public float reflectionsDelay { get => Get(ReflectionsDelay); set => Set(ReflectionsDelay, value); }
        public float reverbLevel { get => Get(Reverb); set => Set(Reverb, value); }
        public float reverbDelay { get => Get(ReverbDelay); set => Set(ReverbDelay, value); }
        public float diffusion { get => Get(Diffusion); set => Set(Diffusion, value); }
        public float density { get => Get(Density); set => Set(Density, value); }
        public float hfReference { get => Get(HFReference); set => Set(HFReference, value); }
        public float lfReference { get => Get(LFReference); set => Set(LFReference, value); }

        [Obsolete("AudioReverbFilter.lFReference is obsolete. Use lfReference instead (UnityUpgradable) -> lfReference")]
        public float lFReference { get => lfReference; set => lfReference = value; }

        internal unsafe bool Describe(out AudioFilterDescriptor d)
        {
            var local = new AudioFilterDescriptor { Kind = AudioFilterKind.Reverb };
            bool finite = true;
            for (int i = 0; i < m_Values.Length; i++)
            {
                local.Reverb[i] = m_Values[i];
                finite &= float.IsFinite(m_Values[i]);
            }
            d = local;
            return AudioFilterChain.Finite(this, ref m_ReportedInvalid, finite ? 0f : float.NaN, 0f);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (AudioReverbFilter)source;
            Array.Copy(s.m_Values, m_Values, m_Values.Length);
            m_ReverbPreset = s.m_ReverbPreset;
        }

        /// <summary>The stored values, as the Editor writes them for every preset.</summary>
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            for (int i = 0; i < m_Values.Length; i++) m_Values[i] = body.Float(s_Fields[i], s_Defaults[i]);
            var preset = (AudioReverbPreset)body.Int("m_ReverbPreset", (int)AudioReverbPreset.User);
            m_ReverbPreset = preset < AudioReverbPreset.Off || preset > AudioReverbPreset.User ? AudioReverbPreset.User : preset;
            if (preset != m_ReverbPreset)
                Debug.LogError("Unbound: AudioReverbFilter on '" + m_GameObject?.m_Name + "' has the unknown preset " + (int)preset + "; it runs with its stored values as User", this);
            var read = new string[s_Fields.Length + 1];
            s_Fields.CopyTo(read, 0);
            read[s_Fields.Length] = "m_ReverbPreset";
            AudioFilterChain.CheckSerialized(this, body, read);
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>Collects an AudioSource's filter components for its packet.</summary>
    internal static class AudioFilterChain
    {
        static readonly HashSet<string> s_Bookkeeping = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "serializedVersion", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_Enabled",
        };
        static readonly HashSet<string> s_ReportedFields = new HashSet<string>();
        static readonly HashSet<int> s_ReportedListeners = new HashSet<int>();

        internal static NotSupportedException CustomCutoffCurve() =>
            new NotSupportedException("AudioLowPassFilter.customCutoffCurve is not supported by Unbound: the cutoff does not follow a distance curve");

        static bool IsFilterComponent(Component c) => c is AudioLowPassFilter || c is AudioHighPassFilter || c is AudioEchoFilter || c is AudioReverbFilter;

        /// <summary>False (with one error per component) when a parameter is not a number.</summary>
        internal static bool Finite(Behaviour component, ref bool reported, float a, float b, float c = 0f, float d = 0f)
        {
            if (float.IsFinite(a) && float.IsFinite(b) && float.IsFinite(c) && float.IsFinite(d))
            {
                reported = false;
                return true;
            }
            if (!reported) Debug.LogError("Unbound: " + component.GetType().Name + " on '" + component.m_GameObject?.m_Name + "' has a parameter that is not finite; it is not applied", component);
            reported = true;
            return false;
        }

        /// <summary>Reports, once per type and field, serialized data the reader does not
        /// understand, so a scene's filter never runs on a silently assumed value.</summary>
        internal static void CheckSerialized(Behaviour component, YNode body, params string[] read)
        {
            if (body == null || body.Kind != YKind.Map) return;
            foreach (var key in body.Keys)
            {
                if (s_Bookkeeping.Contains(key) || Array.IndexOf(read, key) >= 0) continue;
                var name = component.GetType().Name + "." + key;
                lock (s_ReportedFields)
                    if (!s_ReportedFields.Add(name)) continue;
                Debug.LogError("Unbound: the serialized field '" + key + "' of " + component.GetType().Name + " is not read; '" + component.m_GameObject?.m_Name + "' may not sound as authored", component);
            }
        }

        /// <summary>
        /// Fills <paramref name="table"/> with the enabled filter components of the source's
        /// GameObject in component order, each placed above or below its OnAudioFilterRead scripts,
        /// and returns how many. Chains Unbound cannot place (a component between two scripts, one
        /// above a clipless generator script, more than <see cref="AudioFilterDescriptor.MaxPerSource"/>)
        /// fail loudly and send no components.
        /// </summary>
        internal static int Collect(AudioSource source, bool generator, ref AudioFilterDescriptor[] table)
        {
            var go = source.m_GameObject;
            if (go == null) return 0;
            int count = 0;
            bool scripts = false, below = false;
            string error = null;
            foreach (var c in go.m_Components)
            {
                if (AudioFilters.IsFilterScript(c))
                {
                    if (below) error ??= "a filter component between two OnAudioFilterRead scripts is not supported";
                    scripts = true;
                    continue;
                }
                if (!IsFilterComponent(c)) continue;
                var behaviour = (Behaviour)c;
                if (behaviour.m_Destroyed || !behaviour.isActiveAndEnabled) continue;
                AudioFilterDescriptor d;
                bool ok;
                if (c is AudioLowPassFilter lowPass) ok = lowPass.Describe(out d);
                else if (c is AudioHighPassFilter highPass) ok = highPass.Describe(out d);
                else if (c is AudioReverbFilter reverb) ok = reverb.Describe(out d);
                else ok = ((AudioEchoFilter)c).Describe(out d);
                if (!ok) continue;
                d.Id = unchecked((ulong)(long)c.GetInstanceID());
                d.Stage = scripts ? AudioFilterStage.AfterScripts : AudioFilterStage.BeforeScripts;
                if (scripts) below = true;
                else if (generator) error ??= "a filter component above the OnAudioFilterRead script that generates a clipless source is not supported";
                if (count == AudioFilterDescriptor.MaxPerSource)
                {
                    error ??= "more than " + AudioFilterDescriptor.MaxPerSource + " filter components on one source are not supported";
                    break;
                }
                if (table == null || table.Length == count) Array.Resize(ref table, Math.Max(4, count * 2));
                table[count++] = d;
            }
            source.ReportFilterChain(error);
            return error == null ? count : 0;
        }

        /// <summary>
        /// Fills <paramref name="table"/> with the enabled AudioReverbFilter components of the
        /// hearing AudioListener's GameObject in component order and returns how many; they run
        /// on the final mix (tests/projects/audioreverb). Other filter kinds there, and reverb
        /// filters next to OnAudioFilterRead scripts (an unmeasured order) or on a GameObject
        /// that also has an AudioSource (whose chain already runs them; unmeasured), are reported
        /// once per listener and not applied on the listener; the reverb filters still run in the
        /// first case.
        /// </summary>
        internal static int CollectListener(AudioListener listener, bool scripts, ref AudioFilterDescriptor[] table)
        {
            var go = listener.m_GameObject;
            if (go == null) return 0;
            int count = 0;
            string error = null;
            foreach (var c in go.m_Components)
            {
                if (!IsFilterComponent(c) || ((Behaviour)c).m_Destroyed || !((Behaviour)c).isActiveAndEnabled) continue;
                if (!(c is AudioReverbFilter reverb))
                {
                    error ??= c.GetType().Name + " on the AudioListener '" + go.m_Name + "' is not supported; it is not applied";
                    continue;
                }
                if (scripts)
                {
                    error ??= "AudioReverbFilter next to OnAudioFilterRead scripts on the AudioListener '" + go.m_Name + "' is not supported; listener filter components are not applied";
                    count = 0;
                    break;
                }
                if (go.GetComponent<AudioSource>() != null)
                {
                    error ??= "AudioReverbFilter on the AudioListener '" + go.m_Name + "', which also has an AudioSource, is not supported on the listener; it runs on the source only";
                    count = 0;
                    break;
                }
                if (!reverb.Describe(out var d)) continue;
                if (count == AudioFilterDescriptor.MaxPerSource)
                {
                    error ??= "more than " + AudioFilterDescriptor.MaxPerSource + " filter components on the AudioListener are not supported";
                    break;
                }
                d.Id = unchecked((ulong)(long)c.GetInstanceID());
                if (table == null || table.Length == count) Array.Resize(ref table, Math.Max(4, count * 2));
                table[count++] = d;
            }
            if (error != null && s_ReportedListeners.Add(listener.GetInstanceID()))
                Debug.LogError("Unbound: " + error, listener);
            return count;
        }

        internal static void ResetForTests()
        {
            lock (s_ReportedFields) s_ReportedFields.Clear();
            s_ReportedListeners.Clear();
        }
    }
}
