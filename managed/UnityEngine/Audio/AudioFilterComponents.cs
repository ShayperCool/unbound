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
    // say, so whether Unity clamps them on set is unmeasured and they return what was set, as do
    // serialized values. Parameters reach the mixer with the source's next packet (at the latest the
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

        static bool IsFilterComponent(Component c) => c is AudioLowPassFilter || c is AudioHighPassFilter || c is AudioEchoFilter;

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

        /// <summary>Filter components on the hearing AudioListener are not applied: reported once
        /// per listener.</summary>
        internal static void CheckListener(AudioListener listener)
        {
            var go = listener.m_GameObject;
            if (go == null) return;
            foreach (var c in go.m_Components)
            {
                if (!IsFilterComponent(c) || ((Behaviour)c).m_Destroyed || !((Behaviour)c).isActiveAndEnabled) continue;
                if (s_ReportedListeners.Add(listener.GetInstanceID()))
                    Debug.LogError("Unbound: " + c.GetType().Name + " on the AudioListener '" + go.m_Name + "' is not supported; listener filter components are not applied", c);
                return;
            }
        }

        internal static void ResetForTests()
        {
            lock (s_ReportedFields) s_ReportedFields.Clear();
            s_ReportedListeners.Clear();
        }
    }
}
