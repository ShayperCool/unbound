using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Audio
{
    /// <summary>
    /// An AudioMixer asset (.mixer, the Editor's AudioMixerController): a tree of groups whose
    /// volumes (dB) and pitches are parameters. A parameter's value comes from the current snapshot
    /// (blended during a transition), unless an exposed parameter is set by <c>SetFloat</c>.
    /// Each frame the audio update sends every routed group's level (its volume chain to the master,
    /// mute and solo) to the native mixer. Effects other than the attenuation are not applied
    /// (their exposed parameters still read and write).
    /// </summary>
    [ClassId(241)]
    public partial class AudioMixer : Object, ISerializedAsset
    {
        internal AudioMixerGroup m_OutputGroup;
        internal AudioMixerGroup m_MasterGroup;
        internal readonly List<AudioMixerSnapshot> m_Snapshots = new List<AudioMixerSnapshot>();
        internal AudioMixerSnapshot m_StartSnapshot;
        internal AudioMixerUpdateMode m_UpdateMode;
        /// <summary>Exposed parameter name → parameter GUID.</summary>
        internal readonly Dictionary<string, string> m_Exposed = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Parameter GUID → value set by the snapshots.</summary>
        internal readonly Dictionary<string, float> m_Values = new Dictionary<string, float>();
        /// <summary>Exposed parameters set by SetFloat (they no longer follow the snapshots).</summary>
        internal readonly Dictionary<string, float> m_Overrides = new Dictionary<string, float>();
        internal readonly Dictionary<string, float> m_Defaults = new Dictionary<string, float>();
        internal readonly List<AudioMixerGroup> m_Groups = new List<AudioMixerGroup>();
        bool m_Built;

        // A snapshot transition in progress.
        Dictionary<string, float> m_From;
        Dictionary<string, float> m_To;
        Dictionary<string, int> m_TransitionTypes;
        float m_Duration;
        float m_Elapsed;

        internal const float MinVolume = -80f;
        internal const float MaxVolume = 20f;

        internal AudioMixer() { }

        static AudioMixer() => AudioSystem.EnsureRegistered();

        public AudioMixerGroup outputAudioMixerGroup
        {
            get => m_OutputGroup;
            set => m_OutputGroup = value;
        }

        public AudioMixerUpdateMode updateMode
        {
            get => m_UpdateMode;
            set => m_UpdateMode = value;
        }

        public AudioMixerSnapshot FindSnapshot(string name)
        {
            Build();
            foreach (var snapshot in m_Snapshots)
                if (snapshot != null && snapshot.m_Name == name) return snapshot;
            return null;
        }

        /// <summary>The groups whose path (<c>Master/Music/...</c>) has <paramref name="subPath"/> at
        /// the start of one of its segments; every group for an empty path.</summary>
        public AudioMixerGroup[] FindMatchingGroups(string subPath)
        {
            Build();
            var result = new List<AudioMixerGroup>();
            foreach (var group in m_Groups)
            {
                var path = group.Path;
                if (string.IsNullOrEmpty(subPath) || MatchesAtSegment(path, subPath)) result.Add(group);
            }
            return result.ToArray();
        }

        static bool MatchesAtSegment(string path, string subPath)
        {
            for (int i = 0; i <= path.Length - subPath.Length; i++)
            {
                if (i > 0 && path[i - 1] != '/') continue;
                if (string.Compare(path, i, subPath, 0, subPath.Length, StringComparison.OrdinalIgnoreCase) == 0) return true;
            }
            return false;
        }

        internal void TransitionToSnapshot(AudioMixerSnapshot snapshot, float timeToReach)
        {
            if (snapshot == null) throw new ArgumentException("null Snapshot passed to AudioMixerSnapshot.TransitionTo of AudioMixer '" + m_Name + "'");
            TransitionToSnapshots(new[] { snapshot }, new[] { 1f }, timeToReach);
        }

        public void TransitionToSnapshots(AudioMixerSnapshot[] snapshots, float[] weights, float timeToReach)
        {
            if (snapshots == null || weights == null) throw new ArgumentNullException(snapshots == null ? nameof(snapshots) : nameof(weights));
            if (snapshots.Length != weights.Length) throw new ArgumentException("Snapshots and weights arrays must be of the same length");
            Build();
            var target = new Dictionary<string, float>(m_Defaults);
            var types = new Dictionary<string, int>();
            float total = 0f;
            for (int i = 0; i < snapshots.Length; i++)
                if (snapshots[i] != null) total += Mathf.Max(0f, weights[i]);
            if (total <= 0f) return;
            var keys = new List<string>(m_Defaults.Keys);
            foreach (var snapshot in snapshots)
                if (snapshot != null)
                    foreach (var key in snapshot.m_Values.Keys)
                        if (!target.ContainsKey(key)) { target[key] = 0f; keys.Add(key); }
            foreach (var key in keys)
            {
                float sum = 0f;
                for (int i = 0; i < snapshots.Length; i++)
                {
                    var snapshot = snapshots[i];
                    if (snapshot == null) continue;
                    float w = Mathf.Max(0f, weights[i]) / total;
                    sum += w * snapshot.ValueOf(key, m_Defaults);
                    if (!types.ContainsKey(key) && snapshot.m_TransitionTypes.TryGetValue(key, out var type)) types[key] = type;
                }
                target[key] = sum;
            }
            if (timeToReach <= 0f)
            {
                foreach (var pair in target) m_Values[pair.Key] = pair.Value;
                m_To = null;
                return;
            }
            m_From = new Dictionary<string, float>(m_Values);
            m_To = target;
            m_TransitionTypes = types;
            m_Duration = timeToReach;
            m_Elapsed = 0f;
        }

        public bool SetFloat(string name, float value)
        {
            Build();
            if (name == null || !m_Exposed.TryGetValue(name, out var guid))
            {
                if (Trace.AudioOn) Trace.Audio($"SetFloat '{name}' on mixer '{m_Name}': not an exposed parameter");
                return false;
            }
            m_Overrides[guid] = value;
            if (Trace.AudioOn) Trace.Audio($"SetFloat '{name}' = {value} on mixer '{m_Name}'");
            return true;
        }

        public bool ClearFloat(string name)
        {
            Build();
            if (name == null || !m_Exposed.TryGetValue(name, out var guid)) return false;
            m_Overrides.Remove(guid);
            return true;
        }

        public bool GetFloat(string name, out float value)
        {
            Build();
            if (name == null || !m_Exposed.TryGetValue(name, out var guid))
            {
                value = 0f;
                return false;
            }
            value = Value(guid);
            return true;
        }

        internal float GetAbsoluteAudibilityFromGroup(AudioMixerGroup group) => group == null ? 0f : group.EffectiveGain(0);

        /// <summary>A parameter's current value.</summary>
        internal float Value(string guid)
        {
            if (guid == null) return 0f;
            if (m_Overrides.TryGetValue(guid, out var v)) return v;
            if (m_Values.TryGetValue(guid, out v)) return v;
            return m_Defaults.TryGetValue(guid, out v) ? v : 0f;
        }

        internal bool AnySolo
        {
            get
            {
                foreach (var group in m_Groups) if (group.m_Solo) return true;
                return false;
            }
        }

        internal static float DecibelsToGain(float db) => db <= MinVolume ? 0f : Mathf.Pow(10f, Mathf.Min(db, MaxVolume) / 20f);

        internal static float GainToDecibels(float gain) => gain <= 1e-4f ? MinVolume : Mathf.Clamp(20f * Mathf.Log10(gain), MinVolume, MaxVolume);

        /// <summary>Advances a snapshot transition (scaled or unscaled time, by update mode).</summary>
        internal void Tick(float scaledDelta, float unscaledDelta)
        {
            if (m_To == null) return;
            m_Elapsed += m_UpdateMode == AudioMixerUpdateMode.UnscaledTime ? unscaledDelta : scaledDelta;
            float t = m_Duration > 0f ? Mathf.Clamp01(m_Elapsed / m_Duration) : 1f;
            foreach (var pair in m_To)
            {
                float from = m_From.TryGetValue(pair.Key, out var f) ? f : (m_Defaults.TryGetValue(pair.Key, out var d) ? d : 0f);
                m_TransitionTypes.TryGetValue(pair.Key, out var type);
                m_Values[pair.Key] = Interpolate(type, from, pair.Value, t);
            }
            if (t >= 1f) m_To = null;
        }

        /// <summary>UnityEditor.Audio.ParameterTransitionType: Lerp, Smoothstep, Squared, SquareRoot,
        /// BrickwallStart, BrickwallEnd, Attenuation (volumes interpolated as amplitudes).</summary>
        internal static float Interpolate(int type, float from, float to, float t)
        {
            switch (type)
            {
                case 1: t = t * t * (3f - 2f * t); break;
                case 2: t = t * t; break;
                case 3: t = Mathf.Sqrt(t); break;
                case 4: t = t > 0f ? 1f : 0f; break;
                case 5: t = t >= 1f ? 1f : 0f; break;
                case 6: return GainToDecibels(Mathf.Lerp(DecibelsToGain(from), DecibelsToGain(to), t));
            }
            return from + (to - from) * t;
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            T Resolve<T>(ObjectRef r) where T : Object => r.IsNull || resolver == null ? null : resolver.Resolve(r, typeof(T)) as T;
            m_OutputGroup = Resolve<AudioMixerGroup>(body.Ref("m_OutputGroup"));
            m_MasterGroup = Resolve<AudioMixerGroup>(body.Ref("m_MasterGroup"));
            m_Snapshots.Clear();
            foreach (var item in body.Get("m_Snapshots")?.Seq() ?? Array.Empty<YNode>())
            {
                var snapshot = Resolve<AudioMixerSnapshot>(item.AsRef());
                if (snapshot != null) m_Snapshots.Add(snapshot);
            }
            m_StartSnapshot = Resolve<AudioMixerSnapshot>(body.Ref("m_StartSnapshot")) ?? (m_Snapshots.Count > 0 ? m_Snapshots[0] : null);
            m_UpdateMode = (AudioMixerUpdateMode)body.Int("m_UpdateMode");
            m_Exposed.Clear();
            foreach (var item in body.Get("m_ExposedParameters")?.Seq() ?? Array.Empty<YNode>())
            {
                var name = item.Str("name");
                if (!string.IsNullOrEmpty(name)) m_Exposed[name] = item.Str("guid");
            }
            m_Built = false;
            AudioSystem.Add(this);
        }

        /// <summary>Links the groups (read after the mixer) and applies the start snapshot.</summary>
        internal void Build()
        {
            if (m_Built) return;
            m_Built = true;
            m_Groups.Clear();
            m_Defaults.Clear();
            if (m_MasterGroup != null) Collect(m_MasterGroup, null);
            m_Values.Clear();
            if (m_StartSnapshot != null)
                foreach (var pair in m_StartSnapshot.m_Values) m_Values[pair.Key] = pair.Value;
        }

        void Collect(AudioMixerGroup group, AudioMixerGroup parent)
        {
            if (m_Groups.Contains(group)) return;
            m_Groups.Add(group);
            group.m_Mixer = this;
            group.m_Parent = parent;
            if (group.m_VolumeGuid != null) m_Defaults[group.m_VolumeGuid] = 0f;
            if (group.m_PitchGuid != null) m_Defaults[group.m_PitchGuid] = 1f;
            foreach (var guid in group.m_EffectParameters) if (!m_Defaults.ContainsKey(guid)) m_Defaults[guid] = 0f;
            foreach (var child in group.m_Children) if (child != null) Collect(child, group);
        }
    }

    /// <summary>A group of a mixer (AudioMixerGroupController in the asset).</summary>
    [ClassId(243)]
    public partial class AudioMixerGroup : Object, ISerializedAsset
    {
        internal AudioMixer m_Mixer;
        internal AudioMixerGroup m_Parent;
        internal readonly List<AudioMixerGroup> m_Children = new List<AudioMixerGroup>();
        internal string m_GroupId;
        internal string m_VolumeGuid;
        internal string m_PitchGuid;
        internal bool m_Mute;
        internal bool m_Solo;
        internal bool m_BypassEffects;
        internal readonly List<string> m_EffectParameters = new List<string>();
        internal ulong m_Handle;

        internal AudioMixerGroup() { }

        public AudioMixer audioMixer => m_Mixer;

        /// <summary>The group's mixer handle (sources route to it).</summary>
        internal ulong Handle
        {
            get
            {
                if (m_Handle == 0)
                {
                    m_Handle = AudioBackend.NextHandle();
                    m_Mixer?.Build();
                    AudioSystem.PushGroup(this);
                }
                return m_Handle;
            }
        }

        /// <summary>`Master/Music/...`.</summary>
        internal string Path => m_Parent == null ? m_Name : m_Parent.Path + "/" + m_Name;

        /// <summary>The linear gain of the group's chain to its mixer's master (and on through the
        /// mixer's output group): volumes, mute and solo.</summary>
        internal float EffectiveGain(int depth)
        {
            if (m_Mixer == null || depth > 8) return 1f;
            m_Mixer.Build();
            float gain = 1f;
            for (var g = this; g != null; g = g.m_Parent)
            {
                if (g.m_Mute) return 0f;
                gain *= AudioMixer.DecibelsToGain(m_Mixer.Value(g.m_VolumeGuid));
            }
            if (m_Mixer.AnySolo && !InSoloPath()) return 0f;
            var output = m_Mixer.m_OutputGroup;
            if (output != null && output.m_Mixer != m_Mixer) gain *= output.EffectiveGain(depth + 1);
            return gain;
        }

        internal float EffectivePitch(int depth)
        {
            if (m_Mixer == null || depth > 8) return 1f;
            m_Mixer.Build();
            float pitch = 1f;
            for (var g = this; g != null; g = g.m_Parent)
                if (g.m_PitchGuid != null) pitch *= m_Mixer.Value(g.m_PitchGuid);
            var output = m_Mixer.m_OutputGroup;
            if (output != null && output.m_Mixer != m_Mixer) pitch *= output.EffectivePitch(depth + 1);
            return pitch;
        }

        bool InSoloPath()
        {
            for (var g = this; g != null; g = g.m_Parent) if (g.m_Solo) return true;
            return HasSoloDescendant(this);
        }

        static bool HasSoloDescendant(AudioMixerGroup group)
        {
            foreach (var child in group.m_Children)
                if (child != null && (child.m_Solo || HasSoloDescendant(child))) return true;
            return false;
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            T Resolve<T>(ObjectRef r) where T : Object => r.IsNull || resolver == null ? null : resolver.Resolve(r, typeof(T)) as T;
            m_Mixer = Resolve<AudioMixer>(body.Ref("m_AudioMixer"));
            m_GroupId = body.Str("m_GroupID");
            m_VolumeGuid = NullIfZero(body.Str("m_Volume"));
            m_PitchGuid = NullIfZero(body.Str("m_Pitch"));
            m_Mute = body.Bool("m_Mute");
            m_Solo = body.Bool("m_Solo");
            m_BypassEffects = body.Bool("m_BypassEffects");
            m_Children.Clear();
            foreach (var item in body.Get("m_Children")?.Seq() ?? Array.Empty<YNode>())
            {
                var child = Resolve<AudioMixerGroup>(item.AsRef());
                if (child != null) m_Children.Add(child);
            }
            m_EffectParameters.Clear();
            foreach (var item in body.Get("m_Effects")?.Seq() ?? Array.Empty<YNode>())
                if (Resolve<AudioMixerEffectController>(item.AsRef()) is AudioMixerEffectController effect)
                    m_EffectParameters.AddRange(effect.m_Parameters);
        }

        internal static string NullIfZero(string guid) =>
            string.IsNullOrEmpty(guid) || guid.Trim('0').Length == 0 ? null : guid;
    }

    /// <summary>A snapshot: stored parameter values (and per-parameter transition types).</summary>
    [ClassId(245)]
    public partial class AudioMixerSnapshot : Object, ISerializedAsset
    {
        internal AudioMixer m_Mixer;
        internal string m_SnapshotId;
        internal readonly Dictionary<string, float> m_Values = new Dictionary<string, float>();
        internal readonly Dictionary<string, int> m_TransitionTypes = new Dictionary<string, int>();

        internal AudioMixerSnapshot() { }

        public AudioMixer audioMixer => m_Mixer;

        public void TransitionTo(float timeToReach) => audioMixer.TransitionToSnapshot(this, timeToReach);

        internal float ValueOf(string guid, Dictionary<string, float> defaults) =>
            m_Values.TryGetValue(guid, out var v) ? v : defaults.TryGetValue(guid, out v) ? v : 0f;

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var mixer = body.Ref("m_AudioMixer");
            m_Mixer = mixer.IsNull || resolver == null ? null : resolver.Resolve(mixer, typeof(AudioMixer)) as AudioMixer;
            m_SnapshotId = body.Str("m_SnapshotID");
            m_Values.Clear();
            if (body.Get("m_FloatValues") is YNode values && values.Kind == YKind.Map)
                for (int i = 0; i < values.Keys.Length; i++) m_Values[values.Keys[i]] = values.Values[i].AsFloat();
            m_TransitionTypes.Clear();
            if (body.Get("m_TransitionOverrides") is YNode overrides && overrides.Kind == YKind.Map)
                for (int i = 0; i < overrides.Keys.Length; i++) m_TransitionTypes[overrides.Keys[i]] = overrides.Values[i].AsInt();
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>An effect of a mixer group (AudioMixerEffectController): its parameters' GUIDs.</summary>
    [ClassId(244)]
    internal sealed class AudioMixerEffectController : Object, ISerializedAsset
    {
        internal string m_EffectName;
        internal readonly List<string> m_Parameters = new List<string>();

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_EffectName = body.Str("m_EffectName");
            m_Parameters.Clear();
            var mix = UnityEngine.Audio.AudioMixerGroup.NullIfZero(body.Str("m_MixLevel"));
            if (mix != null) m_Parameters.Add(mix);
            foreach (var item in body.Get("m_Parameters")?.Seq() ?? Array.Empty<YNode>())
            {
                var guid = UnityEngine.Audio.AudioMixerGroup.NullIfZero(item.Str("m_GUID"));
                if (guid != null) m_Parameters.Add(guid);
            }
        }
    }
}
