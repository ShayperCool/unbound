using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>A force field for the external forces module: particles inside its shape (between
    /// the start and end range) are pushed along its direction and pulled to its center.</summary>
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
        public Texture3D vectorField { get; set; }

        /// <summary>Whether a point in the field's local space is inside its shape.</summary>
        internal bool Contains(Vector3 local)
        {
            float start = Mathf.Max(0f, startRange), end = Mathf.Max(start, endRange);
            switch (shape)
            {
                case ParticleSystemForceFieldShape.Hemisphere:
                    if (local.y < 0f) return false;
                    goto default;
                case ParticleSystemForceFieldShape.Cylinder:
                {
                    float radial = new Vector2(local.x, local.z).magnitude;
                    return Mathf.Abs(local.y) <= length * 0.5f && radial >= start && radial <= end;
                }
                case ParticleSystemForceFieldShape.Box:
                {
                    float extent = Mathf.Max(Mathf.Abs(local.x), Mathf.Max(Mathf.Abs(local.y), Mathf.Abs(local.z)));
                    return extent >= start && extent <= end;
                }
                default:
                {
                    float d = local.magnitude;
                    return d >= start && d <= end;
                }
            }
        }

        // Rotation curves sample distance through the active range, independent of particle age.
        internal float RotationCurveTime(Vector3 local)
        {
            float distance;
            switch (shape)
            {
                case ParticleSystemForceFieldShape.Cylinder:
                    distance = new Vector2(local.x, local.z).magnitude;
                    break;
                case ParticleSystemForceFieldShape.Box:
                    distance = Mathf.Max(Mathf.Abs(local.x), Mathf.Max(Mathf.Abs(local.y), Mathf.Abs(local.z)));
                    break;
                default: distance = local.magnitude; break;
            }
            return Mathf.InverseLerp(startRange, endRange, distance);
        }

        public static ParticleSystemForceField[] FindAll() => s_All.ToArray();

        internal override void OnEngineEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
        }

        internal override void OnEngineDisable() => s_All.Remove(this);
    }
}
