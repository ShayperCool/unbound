using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Audio
{
    /// <summary>
    /// An AudioMixer asset (.mixer, the Editor's AudioMixerController): a tree of groups whose
    /// volumes (dB) and pitches are parameters. A parameter's value comes from the current snapshot
    /// (blended during a transition), unless an exposed parameter is set by <c>SetFloat</c>.
    /// Each frame the audio update sends the group graph and current parameters to the native
    /// mixer. Lowpass, Highpass, Echo, SFX Reverb, Send, Receive and Duck Volume process the summed
    /// group signal in serialized effect order.
    /// </summary>
    [ClassId(241)]
    public partial class AudioMixer : Object, ISerializedAsset
    {
        internal AudioMixerGroup m_OutputGroup;
        internal AudioMixerGroup m_MasterGroup;
        internal readonly List<AudioMixerSnapshot> m_Snapshots = new List<AudioMixerSnapshot>();
        internal AudioMixerSnapshot m_StartSnapshot;
        internal AudioMixerUpdateMode m_UpdateMode;
        internal bool m_EnableSuspend = true;
        internal float m_SuspendThreshold = -80f;
        /// <summary>Exposed parameter name → parameter GUID.</summary>
        internal readonly Dictionary<string, string> m_Exposed = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Parameter GUID → value set by the snapshots.</summary>
        internal readonly Dictionary<string, float> m_Values = new Dictionary<string, float>();
        /// <summary>Exposed parameters set by SetFloat (they no longer follow the snapshots).</summary>
        internal readonly Dictionary<string, float> m_Overrides = new Dictionary<string, float>();
        internal readonly Dictionary<string, float> m_Defaults = new Dictionary<string, float>();
        internal readonly List<AudioMixerGroup> m_Groups = new List<AudioMixerGroup>();
        bool m_Built;
        readonly HashSet<string> m_PendingClears = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, float> m_SubmittedOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        Dictionary<string, float> m_InstantTarget;

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
                m_InstantTarget = target;
                m_To = null;
                return;
            }
            m_InstantTarget = null;
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
            m_PendingClears.Remove(guid);
            m_Overrides[guid] = value;
            if (Trace.AudioOn) Trace.Audio($"SetFloat '{name}' = {value} on mixer '{m_Name}'");
            return true;
        }

        public bool ClearFloat(string name)
        {
            Build();
            if (name == null || !m_Exposed.TryGetValue(name, out var guid)) return false;
            m_PendingClears.Add(guid);
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
            // The 6000.0.71f1 captures distinguish an override already submitted to audio
            // (mixerfilters set_lp) from a set/clear before submission (audiodistortion mix_clear).
            if (m_PendingClears.Contains(guid) && m_SubmittedOverrides.TryGetValue(guid, out value))
                return true;
            value = m_PendingClears.Contains(guid)
                ? (m_Values.TryGetValue(guid, out var snapshot) ? snapshot : m_Defaults.TryGetValue(guid, out var fallback) ? fallback : 0f)
                : Value(guid);
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

        // Unity's mixer DSP uses this quadratic exponent approximation. The parameter itself
        // remains in dB; the independently repeated gain matrix constrains this conversion.
        internal static float DspGain(float db)
        {
            if (db <= MinVolume) return 0f;
            float exponent = (Mathf.Min(db, MaxVolume) * 0.05f) * 3.3229959f;
            int integral = Mathf.FloorToInt(exponent);
            float fraction = exponent - integral;
            float polynomial = (1f + fraction * 0.6822701f) + (fraction * fraction) * 0.31772992f;
            return polynomial * BitConverter.Int32BitsToSingle((integral + 127) << 23);
        }

        internal static float GainToDecibels(float gain) => gain <= 1e-4f ? MinVolume : Mathf.Clamp(20f * Mathf.Log10(gain), MinVolume, MaxVolume);

        /// <summary>Advances a snapshot transition (scaled or unscaled time, by update mode).</summary>
        internal void Tick(float scaledDelta, float unscaledDelta)
        {
            foreach (var guid in m_PendingClears) m_Overrides.Remove(guid);
            m_PendingClears.Clear();
            m_SubmittedOverrides.Clear();
            foreach (var pair in m_Overrides) m_SubmittedOverrides[pair.Key] = pair.Value;
            if (m_InstantTarget != null)
            {
                foreach (var pair in m_InstantTarget) m_Values[pair.Key] = pair.Value;
                m_InstantTarget = null;
            }
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
            m_EnableSuspend = body.Bool("m_EnableSuspend", true);
            m_SuspendThreshold = body.Float("m_SuspendThreshold", -80f);
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
            foreach (var effect in group.m_Effects)
            {
                if (effect.Parameter("Cutoff freq") is string cutoff) m_Defaults[cutoff] = AudioMixerEffectController.CutoffDefault(effect.m_EffectName);
                if (effect.Parameter("Resonance") is string resonance) m_Defaults[resonance] = 1f;
                if (effect.m_EffectName == "Echo")
                {
                    if (effect.Parameter("Delay") is string delay) m_Defaults[delay] = 500f;
                    if (effect.Parameter("Decay") is string decay) m_Defaults[decay] = 0f;
                    if (effect.Parameter("Max channels") is string channels) m_Defaults[channels] = 0f;
                    if (effect.Parameter("Drymix") is string dry) m_Defaults[dry] = 1f;
                    if (effect.Parameter("Wetmix") is string wet) m_Defaults[wet] = 1f;
                }
                if (effect.m_EffectName == AudioMixerEffectController.SfxReverb)
                    for (int i = 0; i < AudioMixerEffectController.SfxReverbParameters.Length; i++)
                        if (effect.Parameter(AudioMixerEffectController.SfxReverbParameters[i]) is string guid)
                            m_Defaults[guid] = AudioMixerEffectController.SfxReverbDefaults[i];
                if (effect.m_EffectName == AudioMixerEffectController.DuckVolume)
                    for (int i = 0; i < AudioMixerEffectController.DuckParameters.Length; i++)
                        if (effect.Parameter(AudioMixerEffectController.DuckParameters[i]) is string guid)
                            m_Defaults[guid] = AudioMixerEffectController.DuckDefaults[i];
                var (names, defaults, _, _) = AudioMixerEffectController.Ranges(effect.m_EffectName);
                for (int i = 0; names != null && i < names.Length; i++)
                    if (effect.Parameter(names[i]) is string guid) m_Defaults[guid] = defaults[i];
                // A Send's level is its mix level, -80 dB until a snapshot sets it.
                if (effect.m_MixLevelGuid != null) m_Defaults[effect.m_MixLevelGuid] = effect.m_EffectName == "Send" ? MinVolume : 0f;
            }
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
        internal readonly List<AudioMixerEffectController> m_Effects = new List<AudioMixerEffectController>();
        internal ulong m_Handle;
        internal bool m_ObserveOutput;

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

        internal object DspConfiguration()
        {
            m_Mixer?.Build();
            var effects = new List<object>();
            foreach (var effect in m_Effects)
            {
                // Native ignores bypass on Send, Receive and Duck Volume (the Editor disallows it).
                bool routed = effect.m_EffectName == "Send" || effect.m_EffectName == "Receive" || effect.m_EffectName == AudioMixerEffectController.DuckVolume;
                bool bypass = !routed && (m_BypassEffects || effect.m_Bypass);
                bool reverb = effect.m_EffectName == AudioMixerEffectController.SfxReverb;
                bool measured = Array.IndexOf(AudioMixerEffectController.MeasuredEffects, effect.m_EffectName) >= 0;
                if (!bypass && !reverb && !routed && !measured)
                    throw new NotSupportedException("Unsupported AudioMixer effect '" + effect.m_EffectName + "' in group '" + Path + "'");
                bool simple = effect.m_EffectName == "Lowpass Simple" || effect.m_EffectName == "Highpass Simple";
                float simpleCutoff = effect.Parameter("Cutoff freq") is string simpleGuid ? m_Mixer.Value(simpleGuid) : AudioMixerEffectController.CutoffDefault(effect.m_EffectName);
                if (!bypass && simple && !(simpleCutoff >= 10f && simpleCutoff <= 22000f))
                    throw new NotSupportedException("AudioMixer " + effect.m_EffectName + " Cutoff freq " + simpleCutoff + " is outside the Editor's range 10 to 22000");
                // A send's target registers its group, which may have no source of its own.
                var target = effect.m_EffectName == "Send" ? effect.m_SendTarget : null;
                var targetGroup = target == null ? null : m_Mixer?.m_Groups.Find(g => g.m_Effects.Contains(target));
                if (targetGroup != null) _ = targetGroup.Handle;
                // Wet mixing is measured only on SFX Reverb (tests/projects/mixerreverb).
                if (!bypass && !reverb && effect.m_EnableWetMix)
                    throw new NotSupportedException("AudioMixer wet mixing is unsupported for effect '" + effect.m_EffectName + "' in group '" + Path + "'");
                float wetDb = effect.m_MixLevelGuid != null ? m_Mixer.Value(effect.m_MixLevelGuid) : 0f;
                // The Editor's Wet ends at 0 dB; a louder level (e.g. via an exposed parameter) is unmeasured.
                if (!bypass && reverb && effect.m_EnableWetMix && wetDb > 0f)
                    throw new NotSupportedException("AudioMixer wet level " + wetDb + " dB above 0 dB is unmeasured for effect '" + effect.m_EffectName + "' in group '" + Path + "'");
                // tests/projects/mixerduck measures Send levels up to 0 dB.
                if (targetGroup != null && !(wetDb <= 0f))
                    throw new NotSupportedException("AudioMixer send level " + wetDb + " dB above 0 dB is unmeasured in group '" + Path + "'");
                effects.Add(new {
                    id = effect.m_EffectId, kind = effect.m_EffectName, bypass = effect.m_Bypass,
                    gain = AudioMixer.DspGain(m_Mixer.Value(m_VolumeGuid)),
                    cutoff = effect.Parameter("Cutoff freq") is string cutoff ? m_Mixer.Value(cutoff) : AudioMixerEffectController.CutoffDefault(effect.m_EffectName),
                    resonance = effect.Parameter("Resonance") is string resonance ? m_Mixer.Value(resonance) : 1f,
                    delay = effect.Parameter("Delay") is string delay ? m_Mixer.Value(delay) : 500f,
                    decay = effect.Parameter("Decay") is string decay ? m_Mixer.Value(decay) : 0f,
                    maxChannels = effect.Parameter("Max channels") is string channels ? m_Mixer.Value(channels) : 0f,
                    dryMix = effect.Parameter("Drymix") is string dry ? m_Mixer.Value(dry) : 1f,
                    wetMix = effect.Parameter("Wetmix") is string wet ? m_Mixer.Value(wet) : 1f,
                    sfx = reverb ? effect.SfxReverbConfiguration(m_Mixer) : null,
                    // The slot uses the mixer's gain approximation; Native keeps -68 dB and drops the
                    // effect output at -70 dB (the boundary between them is unmeasured).
                    mixLevel = reverb && effect.m_EnableWetMix ? AudioMixer.DspGain(wetDb) : (float?)null,
                    mixAudible = wetDb > -69f,
                    duck = effect.m_EffectName == AudioMixerEffectController.DuckVolume ? effect.DuckConfiguration(m_Mixer) : null,
                    compressor = effect.m_EffectName == AudioMixerEffectController.Compressor && !bypass ? effect.CompressorConfiguration(m_Mixer) : null,
                    normalize = effect.m_EffectName == AudioMixerEffectController.Normalize && !bypass ? effect.NormalizeConfiguration(m_Mixer) : null,
                    flange = effect.m_EffectName == AudioMixerEffectController.Flange && !bypass ? effect.FlangeConfiguration(m_Mixer) : null,
                    chorus = effect.m_EffectName == AudioMixerEffectController.Chorus && !bypass ? effect.ChorusConfiguration(m_Mixer) : null,
                    distortion = effect.m_EffectName == AudioMixerEffectController.Distortion && !bypass ? effect.DistortionConfiguration(m_Mixer) : (float?)null,
                    eq = effect.m_EffectName == AudioMixerEffectController.ParamEq && !bypass ? effect.ParamEqConfiguration(m_Mixer) : null,
                    sendTarget = targetGroup != null ? target.m_EffectId : null,
                    sendLevel = targetGroup != null ? AudioMixer.DspGain(wetDb) : 0f,
                });
            }
            var parent = m_Parent ?? m_Mixer?.m_OutputGroup;
            return new { handle = m_Handle, parent = parent?.Handle ?? 0UL,
                mixer = m_Mixer?.m_MasterGroup?.Handle ?? 0UL,
                enableSuspend = m_Parent == null && m_Mixer != null && m_Mixer.m_EnableSuspend,
                suspendThreshold = SuspensionGain(m_Mixer?.m_SuspendThreshold ?? -80f),
                muted = m_Mute || (m_Mixer != null && m_Mixer.AnySolo && !InSoloPath()),
                bypass = m_BypassEffects, observe = m_ObserveOutput, effects };
        }

        // The constant-tail boundary in tests/projects/mixersuspend distinguishes this
        // approximation from exact pow(10, db/20). Suspension has no attenuation mute floor.
        static float SuspensionGain(float db)
        {
            float exponent = (db * 0.05f) * 3.3229959f;
            int integral = Mathf.FloorToInt(exponent);
            float fraction = exponent - integral;
            float polynomial = (1f + fraction * 0.6822701f) + (fraction * fraction) * 0.31772992f;
            return polynomial * BitConverter.Int32BitsToSingle((integral + 127) << 23);
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
            m_Effects.Clear();
            foreach (var item in body.Get("m_Effects")?.Seq() ?? Array.Empty<YNode>())
                if (Resolve<AudioMixerEffectController>(item.AsRef()) is AudioMixerEffectController effect)
                {
                    m_Effects.Add(effect);
                    m_EffectParameters.AddRange(effect.m_Parameters);
                }
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
        internal string m_EffectId;
        internal bool m_Bypass, m_EnableWetMix;
        internal string m_MixLevelGuid;
        internal const string SfxReverb = "SFX Reverb";
        // Editor parameter definitions of 6000.0.71f1 (the Manual's Room LF default of -10000 differs).
        internal static readonly string[] SfxReverbParameters = {
            "Dry Level", "Room", "Room HF", "Decay Time", "Decay HF Ratio", "Reflections", "Reflect Delay",
            "Reverb", "Reverb Delay", "Diffusion", "Density", "HF Reference", "Room LF", "LF Reference" };
        internal static readonly float[] SfxReverbDefaults = {
            0f, -10000f, 0f, 1f, 0.5f, -10000f, 0.02f, 0f, 0.04f, 100f, 100f, 5000f, 0f, 250f };
        internal const string DuckVolume = "Duck Volume";
        // Parameter definitions of 6000.0.71f1. Native's runtime default Release Time is 0.3 s,
        // not the Editor's 0.1 s: tests/projects/mixerduck (release01 against default).
        internal static readonly string[] DuckParameters = {
            "Threshold", "Ratio", "Attack Time", "Release Time", "Make-up Gain", "Knee", "Sidechain Mix" };
        internal static readonly float[] DuckDefaults = { -10f, 2f, 0.1f, 0.3f, 0f, 10f, 1f };
        static readonly float[] DuckMinimums = { -80f, 0.2f, 0f, 0f, -80f, 0f, 0f };
        static readonly float[] DuckMaximums = { 0f, 10f, 10f, 10f, 40f, 50f, 1f };
        internal const string Compressor = "Compressor";
        internal const string ParamEq = "ParamEQ";
        internal const string Normalize = "Normalize";
        internal static readonly string[] NormalizeParameters = { "Fade in time", "Lowest volume", "Maximum amp" };
        static readonly float[] NormalizeDefaults = { 5000f, 0.1f, 20f };
        static readonly float[] NormalizeMinimums = { 0f, 0f, 0f };
        static readonly float[] NormalizeMaximums = { 20000f, 1f, 100000f };
        internal const string Flange = "Flange";
        internal static readonly string[] FlangeParameters = { "Drymix", "Wetmix", "Depth", "Rate" };
        static readonly float[] FlangeDefaults = { 0.45f, 0.55f, 1f, 0.1f };
        static readonly float[] FlangeMinimums = { 0f, 0f, 0.01f, 0f };
        static readonly float[] FlangeMaximums = { 1f, 1f, 1f, 20f };
        internal const string Chorus = "Chorus";
        internal static readonly string[] ChorusParameters = { "Dry mix", "Wet mix tap 1", "Wet mix tap 2", "Wet mix tap 3", "Delay", "Rate", "Depth", "Feedback" };
        static readonly float[] ChorusDefaults = { 0.5f, 0.5f, 0.5f, 0.5f, 40f, 0.8f, 0.03f, 0f };
        static readonly float[] ChorusMinimums = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1f };
        static readonly float[] ChorusMaximums = { 1f, 1f, 1f, 1f, 100f, 20f, 1f, 1f };
        internal const string Distortion = "Distortion";
        static readonly string[] DistortionParameters = { "Level" };
        static readonly float[] DistortionDefaults = { 0.5f }, DistortionMinimums = { 0f }, DistortionMaximums = { 1f };
        // Editor parameter definitions of 6000.0.71f1; tests/projects/mixercompressor measures the
        // same values as Native's defaults for unset parameters.
        internal static readonly string[] CompressorParameters = { "Threshold", "Attack", "Release", "Make up gain" };
        static readonly float[] CompressorDefaults = { 0f, 50f, 50f, 0f };
        static readonly float[] CompressorMinimums = { -60f, 10f, 20f, 0f };
        static readonly float[] CompressorMaximums = { 0f, 200f, 1000f, 30f };
        internal static readonly string[] ParamEqParameters = { "Center freq", "Octave range", "Frequency gain" };
        static readonly float[] ParamEqDefaults = { 8000f, 1f, 1f };
        static readonly float[] ParamEqMinimums = { 20f, 0.2f, 0.05f };
        static readonly float[] ParamEqMaximums = { 22000f, 5f, 3f };
        /// <summary>Kinds the DSP runs besides SFX Reverb and the routed units.</summary>
        internal static readonly string[] MeasuredEffects = {
            "Attenuation", "Lowpass", "Highpass", "Echo", Compressor, ParamEq, Distortion, Chorus, Flange, Normalize, "Lowpass Simple", "Highpass Simple" };
        internal static float CutoffDefault(string effect) => effect == "Highpass Simple" ? 1000f : 5000f;
        internal static (string[], float[], float[], float[]) Ranges(string effect) =>
            effect == Compressor ? (CompressorParameters, CompressorDefaults, CompressorMinimums, CompressorMaximums)
            : effect == ParamEq ? (ParamEqParameters, ParamEqDefaults, ParamEqMinimums, ParamEqMaximums)
            : effect == Normalize ? (NormalizeParameters, NormalizeDefaults, NormalizeMinimums, NormalizeMaximums)
            : effect == Flange ? (FlangeParameters, FlangeDefaults, FlangeMinimums, FlangeMaximums)
            : effect == Chorus ? (ChorusParameters, ChorusDefaults, ChorusMinimums, ChorusMaximums)
            : effect == Distortion ? (DistortionParameters, DistortionDefaults, DistortionMinimums, DistortionMaximums)
            : (null, null, null, null);
        /// <summary>The effect's parameters; values outside the Editor's ranges (reachable through
        /// exposed parameters) are unmeasured and fail.</summary>
        float[] Values(UnityEngine.Audio.AudioMixer mixer)
        {
            var (names, defaults, minimums, maximums) = Ranges(m_EffectName);
            var v = new float[names.Length];
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = Parameter(names[i]) is string guid ? mixer.Value(guid) : defaults[i];
                if (!(v[i] >= minimums[i] && v[i] <= maximums[i]))
                    throw new NotSupportedException("AudioMixer " + m_EffectName + " " + names[i] + " " + v[i] + " is outside the Editor's range " + minimums[i] + " to " + maximums[i]);
            }
            return v;
        }
        internal object CompressorConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            var v = Values(mixer);
            return new { threshold = v[0], attack = v[1], release = v[2], makeupGain = v[3] };
        }
        // Native 6000.0.71f1 keeps exposed values verbatim but clamps the DSP amount.
        internal float DistortionConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            float value = Parameter("Level") is string guid ? mixer.Value(guid) : 0.5f;
            if (!float.IsFinite(value)) throw new NotSupportedException("AudioMixer Distortion Level must be finite");
            return Mathf.Clamp01(value);
        }
        // Unity 6000.0.71f1 captures retain exposed values and clamp only the DSP inputs.
        internal object NormalizeConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            var v = new float[3];
            for (int i = 0; i < v.Length; i++)
            {
                float value = Parameter(NormalizeParameters[i]) is string guid ? mixer.Value(guid) : NormalizeDefaults[i];
                if (!float.IsFinite(value)) throw new NotSupportedException("AudioMixer Normalize " + NormalizeParameters[i] + " must be finite");
                v[i] = Mathf.Clamp(value, NormalizeMinimums[i], NormalizeMaximums[i]);
            }
            var config = AudioSettings.GetConfiguration();
            if ((config.sampleRate != 48000 && config.sampleRate != 44100) || config.dspBufferSize != 1024 || config.speakerMode != AudioSpeakerMode.Stereo)
                throw new NotSupportedException("AudioMixer Normalize is measured only at 48000/44100 Hz with 1024-frame stereo output");
            return new { fade = v[0], lowest = v[1], maximum = v[2] };
        }
        internal object FlangeConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            var v = new float[4];
            for (int i = 0; i < v.Length; i++)
            {
                float value = Parameter(FlangeParameters[i]) is string guid ? mixer.Value(guid) : FlangeDefaults[i];
                if (!float.IsFinite(value)) throw new NotSupportedException("AudioMixer Flange " + FlangeParameters[i] + " must be finite");
                v[i] = Mathf.Clamp(value, FlangeMinimums[i], FlangeMaximums[i]);
            }
            return new { dry = v[0], wet = v[1], depth = v[2], rate = v[3] };
        }
        internal object ChorusConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            var v = new float[7];
            for (int i = 0; i < v.Length; i++)
            {
                float value = Parameter(ChorusParameters[i]) is string guid ? mixer.Value(guid) : ChorusDefaults[i];
                if (!float.IsFinite(value)) throw new NotSupportedException("AudioMixer Chorus " + ChorusParameters[i] + " must be finite");
                v[i] = Mathf.Clamp(value, ChorusMinimums[i], ChorusMaximums[i]);
            }
            // Feedback is obsolete and has no measured effect, including the mixer's signed range.
            float rawDelay = Parameter("Delay") is string delayGuid ? mixer.Value(delayGuid) : ChorusDefaults[4];
            float? delay = rawDelay <= 100f ? Mathf.Max(0f, rawDelay) : null;
            return new { dry = v[0], wet1 = v[1], wet2 = v[2], wet3 = v[3], delay, rate = v[5], depth = v[6] };
        }
        internal object ParamEqConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            var v = Values(mixer);
            return new { center = v[0], octaves = v[1], gain = v[2] };
        }
        internal AudioMixerEffectController m_SendTarget;
        internal object DuckConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            var v = new float[DuckParameters.Length];
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = Parameter(DuckParameters[i]) is string guid ? mixer.Value(guid) : DuckDefaults[i];
                // Values outside the Editor's ranges (reachable through exposed parameters) are unmeasured.
                if (!(v[i] >= DuckMinimums[i] && v[i] <= DuckMaximums[i]))
                    throw new NotSupportedException("AudioMixer Duck Volume " + DuckParameters[i] + " " + v[i] + " is outside the Editor's range " + DuckMinimums[i] + " to " + DuckMaximums[i]);
            }
            return new {
                threshold = v[0], ratio = v[1], attackTime = v[2], releaseTime = v[3],
                makeupGain = v[4], knee = v[5], sidechainMix = v[6] };
        }
        internal object SfxReverbConfiguration(UnityEngine.Audio.AudioMixer mixer)
        {
            var v = new float[SfxReverbParameters.Length];
            for (int i = 0; i < v.Length; i++)
                v[i] = Parameter(SfxReverbParameters[i]) is string guid ? mixer.Value(guid) : SfxReverbDefaults[i];
            return new {
                dryLevel = v[0], room = v[1], roomHf = v[2], decayTime = v[3], decayHfRatio = v[4],
                reflections = v[5], reflectDelay = v[6], reverb = v[7], reverbDelay = v[8], diffusion = v[9],
                density = v[10], hfReference = v[11], roomLf = v[12], lfReference = v[13] };
        }
        internal readonly Dictionary<string, string> m_ParameterGuids = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string Parameter(string name) => m_ParameterGuids.TryGetValue(name, out var guid) ? guid : null;
        internal readonly List<string> m_Parameters = new List<string>();

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_EffectName = body.Str("m_EffectName");
            m_EffectId = body.Str("m_EffectID");
            m_Bypass = body.Bool("m_Bypass");
            m_EnableWetMix = body.Bool("m_EnableWetMix");
            var send = body.Ref("m_SendTarget");
            m_SendTarget = send.IsNull || resolver == null ? null : resolver.Resolve(send, typeof(AudioMixerEffectController)) as AudioMixerEffectController;
            m_ParameterGuids.Clear();
            m_Parameters.Clear();
            var mix = UnityEngine.Audio.AudioMixerGroup.NullIfZero(body.Str("m_MixLevel"));
            m_MixLevelGuid = mix;
            if (mix != null) m_Parameters.Add(mix);
            foreach (var item in body.Get("m_Parameters")?.Seq() ?? Array.Empty<YNode>())
            {
                var guid = UnityEngine.Audio.AudioMixerGroup.NullIfZero(item.Str("m_GUID"));
                if (guid != null)
                {
                    m_Parameters.Add(guid);
                    m_ParameterGuids[item.Str("m_ParameterName")] = guid;
                }
            }
        }
    }
}
