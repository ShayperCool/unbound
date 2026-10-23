using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>A force field for the external forces module. Unbound keeps its settings; particle
    /// systems are not affected by force fields yet.</summary>
    [RequireComponent(typeof(Transform))]
    public partial class ParticleSystemForceField : Behaviour
    {
        static readonly List<ParticleSystemForceField> s_All = new List<ParticleSystemForceField>();

        public ParticleSystemForceField() { }

        public ParticleSystemForceFieldShape shape { get; set; }
        public float startRange { get; set; }
        public float endRange { get; set; } = 1f;
        public float length { get; set; }
        public float gravityFocus { get; set; }
        public Vector2 rotationRandomness { get; set; }
        public bool multiplyDragByParticleSize { get; set; } = true;
        public bool multiplyDragByParticleVelocity { get; set; } = true;
        public ParticleSystem.MinMaxCurve directionX { get; set; }
        public ParticleSystem.MinMaxCurve directionY { get; set; }
        public ParticleSystem.MinMaxCurve directionZ { get; set; }
        public ParticleSystem.MinMaxCurve gravity { get; set; }
        public ParticleSystem.MinMaxCurve rotationSpeed { get; set; }
        public ParticleSystem.MinMaxCurve rotationAttraction { get; set; }
        public ParticleSystem.MinMaxCurve drag { get; set; }
        public ParticleSystem.MinMaxCurve vectorFieldSpeed { get; set; }
        public ParticleSystem.MinMaxCurve vectorFieldAttraction { get; set; }

        public static ParticleSystemForceField[] FindAll() => s_All.ToArray();

        internal override void OnEngineEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
        }

        internal override void OnEngineDisable() => s_All.Remove(this);
    }
}
