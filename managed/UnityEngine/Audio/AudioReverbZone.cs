using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A sphere of reverb: full effect within <see cref="minDistance"/>, none beyond
    /// <see cref="maxDistance"/> (6000.0 Manual, Audio Reverb Zone). Matches the 6000.0.71f1
    /// Editor (tests/projects/audioreverb): a new zone is Generic; a preset writes every
    /// parameter except the distances and User keeps the current values; the setters apply
    /// under any preset and clamp to the documented ranges. Serialized values are read as stored.
    /// The enabled zones blend at the listener into one SFX Reverb (<see cref="Blend"/>) that
    /// every AudioSource feeds at its <c>reverbZoneMix</c>.
    /// </summary>
    [ClassId(167)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class AudioReverbZone : Behaviour
    {
        // Room, Room HF, Decay Time, Decay HF Ratio, Reflections, Reflections Delay, Reverb, Reverb
        // Delay, Diffusion and Density of each preset from Off to Psychotic, as the Editor reports
        // them. A preset also sets Room LF to 0 and the references to 5000 and 250 Hz.
        static readonly float[,] s_Presets = {
            { -10000f, -10000f, 1f, 1f, -2602f, 0.007f, 200f, 0.011f, 0f, 0f },
            { -1000f, -100f, 1.49f, 0.83f, -2602f, 0.007f, 200f, 0.011f, 100f, 100f },
            { -1000f, -6000f, 0.17f, 0.1f, -1204f, 0.001f, 207f, 0.002f, 100f, 100f },
            { -1000f, -454f, 0.4f, 0.83f, -1646f, 0.002f, 53f, 0.003f, 100f, 100f },
            { -1000f, -1200f, 1.49f, 0.54f, -370f, 0.007f, 1030f, 0.011f, 100f, 60f },
            { -1000f, -6000f, 0.5f, 0.1f, -1376f, 0.003f, -1104f, 0.004f, 100f, 100f },
            { -1000f, -300f, 2.31f, 0.64f, -711f, 0.012f, 83f, 0.017f, 100f, 100f },
            { -1000f, -476f, 4.32f, 0.59f, -789f, 0.02f, -289f, 0.03f, 100f, 100f },
            { -1000f, -500f, 3.92f, 0.7f, -1230f, 0.02f, -2f, 0.029f, 100f, 100f },
            { -1000f, 0f, 2.91f, 1.3f, -602f, 0.015f, -302f, 0.022f, 100f, 100f },
            { -1000f, -698f, 7.24f, 0.33f, -1166f, 0.02f, 16f, 0.03f, 100f, 100f },
            { -1000f, -1000f, 10.05f, 0.23f, -602f, 0.02f, 198f, 0.03f, 100f, 100f },
            { -1000f, -4000f, 0.3f, 0.1f, -1831f, 0.002f, -1630f, 0.03f, 100f, 100f },
            { -1000f, -300f, 1.49f, 0.59f, -1219f, 0.007f, 441f, 0.011f, 100f, 100f },
            { -1000f, -237f, 2.7f, 0.79f, -1214f, 0.013f, 395f, 0.02f, 100f, 100f },
            { -1000f, -270f, 1.49f, 0.86f, -1204f, 0.007f, -4f, 0.011f, 100f, 100f },
            { -1000f, -3300f, 1.49f, 0.54f, -2560f, 0.162f, -229f, 0.088f, 79f, 100f },
            { -1000f, -800f, 1.49f, 0.67f, -2273f, 0.007f, -1691f, 0.011f, 50f, 100f },
            { -1000f, -2500f, 1.49f, 0.21f, -2780f, 0.3f, -1434f, 0.1f, 27f, 100f },
            { -1000f, -1000f, 1.49f, 0.83f, -10000f, 0.061f, 500f, 0.025f, 100f, 100f },
            { -1000f, -2000f, 1.49f, 0.5f, -2466f, 0.179f, -1926f, 0.1f, 21f, 100f },
            { -1000f, 0f, 1.65f, 1.5f, -1363f, 0.008f, -1153f, 0.012f, 100f, 100f },
            { -1000f, -1000f, 2.81f, 0.14f, 429f, 0.014f, 1023f, 0.021f, 80f, 60f },
            { -1000f, -4000f, 1.49f, 0.1f, -449f, 0.007f, 1700f, 0.011f, 100f, 100f },
            { -1000f, 0f, 8.39f, 1.39f, -115f, 0.002f, 985f, 0.03f, 100f, 100f },
            { -1000f, -400f, 17.23f, 0.56f, -1713f, 0.02f, -613f, 0.03f, 100f, 100f },
            { -1000f, -151f, 7.56f, 0.91f, -626f, 0.02f, 774f, 0.03f, 100f, 100f },
        };

        internal float m_MinDistance = 10f;
        internal float m_MaxDistance = 15f;
        internal AudioReverbPreset m_ReverbPreset = AudioReverbPreset.Generic;
        internal int m_Room = -1000;
        internal int m_RoomHF = -100;
        internal int m_RoomLF;
        internal float m_DecayTime = 1.49f;
        internal float m_DecayHFRatio = 0.83f;
        internal int m_Reflections = -2602;
        internal float m_ReflectionsDelay = 0.007f;
        internal int m_Reverb = 200;
        internal float m_ReverbDelay = 0.011f;
        internal float m_HFReference = 5000f;
        internal float m_LFReference = 250f;
        internal float m_Diffusion = 100f;
        internal float m_Density = 100f;

        public AudioReverbZone() { }

        static AudioReverbZone() => AudioSystem.EnsureRegistered();

        public float minDistance { get => m_MinDistance; set => m_MinDistance = Math.Max(value, 0f); }
        public float maxDistance { get => m_MaxDistance; set => m_MaxDistance = Math.Max(value, m_MinDistance); }

        public AudioReverbPreset reverbPreset
        {
            get => m_ReverbPreset;
            set
            {
                if (value < AudioReverbPreset.Off || value > AudioReverbPreset.User)
                {
                    Debug.LogError("Unbound: AudioReverbZone.reverbPreset " + (int)value + " is not a preset; '" + m_GameObject?.m_Name + "' keeps " + m_ReverbPreset, this);
                    return;
                }
                m_ReverbPreset = value;
                if (value == AudioReverbPreset.User) return;
                int p = (int)value;
                m_Room = (int)s_Presets[p, 0];
                m_RoomHF = (int)s_Presets[p, 1];
                m_DecayTime = s_Presets[p, 2];
                m_DecayHFRatio = s_Presets[p, 3];
                m_Reflections = (int)s_Presets[p, 4];
                m_ReflectionsDelay = s_Presets[p, 5];
                m_Reverb = (int)s_Presets[p, 6];
                m_ReverbDelay = s_Presets[p, 7];
                m_Diffusion = s_Presets[p, 8];
                m_Density = s_Presets[p, 9];
                m_RoomLF = 0;
                m_HFReference = 5000f;
                m_LFReference = 250f;
            }
        }

        public int room { get => m_Room; set => m_Room = Math.Clamp(value, -10000, 0); }
        public int roomHF { get => m_RoomHF; set => m_RoomHF = Math.Clamp(value, -10000, 0); }
        public int roomLF { get => m_RoomLF; set => m_RoomLF = Math.Clamp(value, -10000, 0); }
        public float decayTime { get => m_DecayTime; set => m_DecayTime = Mathf.Clamp(value, 0.1f, 20f); }
        public float decayHFRatio { get => m_DecayHFRatio; set => m_DecayHFRatio = Mathf.Clamp(value, 0.1f, 2f); }
        public int reflections { get => m_Reflections; set => m_Reflections = Math.Clamp(value, -10000, 1000); }
        public float reflectionsDelay { get => m_ReflectionsDelay; set => m_ReflectionsDelay = Mathf.Clamp(value, 0f, 0.3f); }
        public int reverb { get => m_Reverb; set => m_Reverb = Math.Clamp(value, -10000, 2000); }
        public float reverbDelay { get => m_ReverbDelay; set => m_ReverbDelay = Mathf.Clamp(value, 0f, 0.1f); }
        public float HFReference { get => m_HFReference; set => m_HFReference = Mathf.Clamp(value, 1000f, 20000f); }
        public float LFReference { get => m_LFReference; set => m_LFReference = Mathf.Clamp(value, 20f, 1000f); }
        public float diffusion { get => m_Diffusion; set => m_Diffusion = Mathf.Clamp(value, 0f, 100f); }
        public float density { get => m_Density; set => m_Density = Mathf.Clamp(value, 0f, 100f); }

        /// <summary>1 within minDistance of the listener, 0 from maxDistance on, linear between.</summary>
        float Weight(Vector3 listener)
        {
            var p = transform.position;
            float dx = listener.x - p.x, dy = listener.y - p.y, dz = listener.z - p.z;
            float distance = MathF.Sqrt(dz * dz + (dy * dy + dx * dx));
            float min = Math.Min(m_MinDistance, m_MaxDistance);
            if (min >= distance) return 1f;
            if (distance >= m_MaxDistance) return 0f;
            float range = m_MaxDistance - min;
            return range > 0f ? 1f - (distance - min) / range : 1f;
        }

        static float Level(int mB) => MathF.Exp(mB * 0.002f);

        static float Millibels(float sum, float factor) => sum > 0f ? (int)(MathF.Log(sum * factor) * 500f + 0.5f) : -10000f;

        /// <summary>
        /// Writes the SFX Reverb parameters (the mixer order, Dry Level first) of the zones heard at
        /// <paramref name="listener"/>, as the Editor blends them (tests/projects/audioreverb): each
        /// zone with a weight of at least 0.001 adds its parameters times its weight, levels as
        /// e^(mB/500), the references as logarithms. Below a total weight of 1 the rest is an Off
        /// room (-10000) and the sum is not divided, so a partly heard zone also shortens its
        /// delays and lowers its references; above 1 the sum is divided by the total. The result
        /// is clamped to the zone ranges; HF Reference stays at least 1000 Hz.
        /// </summary>
        internal static unsafe void Blend(List<AudioReverbZone> zones, Vector3 listener, float* r)
        {
            float room = 0f, roomHF = 0f, roomLF = 0f, reflections = 0f, reverb = 0f, decay = 0f, decayHF = 0f,
                reflectionsDelay = 0f, reverbDelay = 0f, hf = 0f, lf = 0f, diffusion = 0f, density = 0f, total = 0f;
            foreach (var z in zones)
            {
                if (z == null || z.m_GameObject == null) continue;
                float w = z.Weight(listener);
                if (!(w >= 0.001f)) continue;
                room += Level(z.m_Room) * w;
                roomHF += Level(z.m_RoomHF) * w;
                roomLF += Level(z.m_RoomLF) * w;
                decay += z.m_DecayTime * w;
                decayHF += z.m_DecayHFRatio * w;
                reflections += Level(z.m_Reflections) * w;
                reflectionsDelay += z.m_ReflectionsDelay * w;
                reverb += Level(z.m_Reverb) * w;
                reverbDelay += z.m_ReverbDelay * w;
                if (z.m_HFReference > 0f) hf += MathF.Log(z.m_HFReference) * w;
                if (z.m_LFReference > 0f) lf += MathF.Log(z.m_LFReference) * w;
                diffusion += z.m_Diffusion * w;
                density += z.m_Density * w;
                total += w;
            }
            if (total < 1f)
            {
                room += Level(-10000) * (1f - total);
                total = 1f;
            }
            float f = 1f / total;
            r[0] = -10000f;
            r[1] = Math.Clamp(Millibels(room, f), -10000f, 0f);
            r[2] = Math.Clamp(Millibels(roomHF, f), -10000f, 0f);
            r[3] = Mathf.Clamp(decay * f, 0.1f, 20f);
            r[4] = Mathf.Clamp(decayHF * f, 0.1f, 2f);
            r[5] = Math.Clamp(Millibels(reflections, f), -10000f, 1000f);
            r[6] = Mathf.Clamp(reflectionsDelay * f, 0f, 0.3f);
            r[7] = Math.Clamp(Millibels(reverb, f), -10000f, 2000f);
            r[8] = Mathf.Clamp(reverbDelay * f, 0f, 0.1f);
            r[9] = Mathf.Clamp(diffusion * f, 0f, 100f);
            r[10] = Mathf.Clamp(density * f, 0f, 100f);
            r[11] = Mathf.Clamp(MathF.Exp(hf * f), 1000f, 20000f);
            r[12] = Math.Clamp(Millibels(roomLF, f), -10000f, 0f);
            r[13] = Mathf.Clamp(MathF.Exp(f * lf), 20f, 1000f);
        }

        internal override void OnEngineEnable() => AudioSystem.Add(this);

        internal override void OnEngineDisable() => AudioSystem.Remove(this);

        internal override void OnEngineDestroy() => AudioSystem.Remove(this);

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (AudioReverbZone)source;
            m_MinDistance = s.m_MinDistance;
            m_MaxDistance = s.m_MaxDistance;
            m_ReverbPreset = s.m_ReverbPreset;
            m_Room = s.m_Room;
            m_RoomHF = s.m_RoomHF;
            m_RoomLF = s.m_RoomLF;
            m_DecayTime = s.m_DecayTime;
            m_DecayHFRatio = s.m_DecayHFRatio;
            m_Reflections = s.m_Reflections;
            m_ReflectionsDelay = s.m_ReflectionsDelay;
            m_Reverb = s.m_Reverb;
            m_ReverbDelay = s.m_ReverbDelay;
            m_HFReference = s.m_HFReference;
            m_LFReference = s.m_LFReference;
            m_Diffusion = s.m_Diffusion;
            m_Density = s.m_Density;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_MinDistance = body.Float("m_MinDistance", 10f);
            m_MaxDistance = body.Float("m_MaxDistance", 15f);
            var preset = (AudioReverbPreset)body.Int("m_ReverbPreset", (int)AudioReverbPreset.Generic);
            m_ReverbPreset = preset < AudioReverbPreset.Off || preset > AudioReverbPreset.User ? AudioReverbPreset.User : preset;
            if (preset != m_ReverbPreset)
                Debug.LogError("Unbound: AudioReverbZone on '" + m_GameObject?.m_Name + "' has the unknown preset " + (int)preset + "; it runs with its stored values as User", this);
            m_Room = body.Int("m_Room", -1000);
            m_RoomHF = body.Int("m_RoomHF", -100);
            m_RoomLF = body.Int("m_RoomLF", 0);
            m_DecayTime = body.Float("m_DecayTime", 1.49f);
            m_DecayHFRatio = body.Float("m_DecayHFRatio", 0.83f);
            m_Reflections = body.Int("m_Reflections", -2602);
            m_ReflectionsDelay = body.Float("m_ReflectionsDelay", 0.007f);
            m_Reverb = body.Int("m_Reverb", 200);
            m_ReverbDelay = body.Float("m_ReverbDelay", 0.011f);
            m_HFReference = body.Float("m_HFReference", 5000f);
            m_LFReference = body.Float("m_LFReference", 250f);
            m_Diffusion = body.Float("m_Diffusion", 100f);
            m_Density = body.Float("m_Density", 100f);
            AudioFilterChain.CheckSerialized(this, body, "m_MinDistance", "m_MaxDistance", "m_ReverbPreset", "m_Room",
                "m_RoomHF", "m_RoomLF", "m_DecayTime", "m_DecayHFRatio", "m_Reflections", "m_ReflectionsDelay", "m_Reverb",
                "m_ReverbDelay", "m_HFReference", "m_LFReference", "m_Diffusion", "m_Density");
        }
    }
}
