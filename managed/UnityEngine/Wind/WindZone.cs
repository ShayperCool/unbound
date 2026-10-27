using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public enum WindZoneMode
    {
        Directional = 0,
        Spherical = 1,
    }

    /// <summary>A wind zone: it pushes particles whose external forces module it influences
    /// (measured: tests/projects/wind). A directional zone blows along its forward axis everywhere;
    /// a spherical one away from its center, fading to nothing at its radius.</summary>
    public partial class WindZone : Component
    {
        static readonly List<WindZone> s_All = new List<WindZone>();

        public WindZone() { }

        public WindZoneMode mode { get; set; } = WindZoneMode.Directional;
        public float radius { get; set; } = 20f;
        public float windMain { get; set; } = 1f;
        public float windTurbulence { get; set; } = 1f;
        public float windPulseMagnitude { get; set; } = 0.5f;
        public float windPulseFrequency { get; set; } = 0.01f;

        internal static List<WindZone> All => s_All;

        /// <summary>The pulse at a time: around 1, by the magnitude, as the sum of three cosines of
        /// the frequency times the time (measured, exact).</summary>
        internal float Pulse(float time)
        {
            float x = windPulseFrequency * time;
            return 1f + windPulseMagnitude * 0.333f * (Mathf.Cos(Mathf.PI * x) + Mathf.Cos(Mathf.PI * 0.375f * x) + Mathf.Cos(Mathf.PI * 0.05f * x));
        }

        /// <summary>The wind's acceleration on a particle at a world position (turbulence moves no
        /// particles: measured).</summary>
        internal Vector3 Acceleration(Vector3 world, float time)
        {
            var t = transform;
            float strength = windMain * Pulse(time);
            if (mode == WindZoneMode.Directional) return t.forward * strength;
            var d = world - t.position;
            float r2 = radius * radius, d2 = d.sqrMagnitude;
            if (d2 >= r2 || d2 <= 0f) return Vector3.zero;
            return d / Mathf.Sqrt(d2) * (strength * (1f - d2 / r2));
        }

        internal override void OnEngineEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
        }

        internal override void OnEngineDisable() => s_All.Remove(this);

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (WindZone)source;
            mode = s.mode;
            radius = s.radius;
            windMain = s.windMain;
            windTurbulence = s.windTurbulence;
            windPulseMagnitude = s.windPulseMagnitude;
            windPulseFrequency = s.windPulseFrequency;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            mode = (WindZoneMode)body.Int("m_Mode", (int)WindZoneMode.Directional);
            radius = body.Float("m_Radius", 20f);
            windMain = body.Float("m_WindMain", 1f);
            windTurbulence = body.Float("m_WindTurbulence", 1f);
            windPulseMagnitude = body.Float("m_WindPulseMagnitude", 0.5f);
            windPulseFrequency = body.Float("m_WindPulseFrequency", 0.01f);
        }
    }
}
