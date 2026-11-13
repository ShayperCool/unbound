using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>A force field for the external forces module: particles inside its shape (between
    /// the start and end range) are pushed along its direction and pulled to its center.</summary>
    [RequireComponent(typeof(Transform))]
    public partial class ParticleSystemForceField : Behaviour
    {
        static readonly List<ParticleSystemForceField> s_All = new List<ParticleSystemForceField>();

        public ParticleSystemForceField() { if (Unbound.Runtime.Compat.DefaultConstructorStateMeasured) length = 1f; }

        public ParticleSystemForceFieldShape shape { get; set; }
        public float startRange { get; set; }
        public float endRange { get; set; } = 1f;
        public float length { get; set; }
        public float gravityFocus { get; set; }
        public Vector2 rotationRandomness { get; set; }
        public bool multiplyDragByParticleSize { get; set; } = true;
        public bool multiplyDragByParticleVelocity { get; set; } = true;
        // Stored like particle module properties (one scalar per curve, see ParticleSystem.ModuleCurve);
        // a new field's gravity and vector-field speed are 1, the rest 0 (6000.0.71f1, particlecurves).
        internal ParticleSystem.ModuleCurve m_DirectionX, m_DirectionY, m_DirectionZ, m_RotationSpeed,
            m_RotationAttraction, m_Drag, m_VectorFieldAttraction;
        internal ParticleSystem.ModuleCurve m_Gravity = ParticleSystem.ModuleCurve.Constant(1f);
        internal ParticleSystem.ModuleCurve m_VectorFieldSpeed = ParticleSystem.ModuleCurve.Constant(1f);
        public ParticleSystem.MinMaxCurve directionX { get => m_DirectionX.Get(); set => m_DirectionX.Set(value); }
        public ParticleSystem.MinMaxCurve directionY { get => m_DirectionY.Get(); set => m_DirectionY.Set(value); }
        public ParticleSystem.MinMaxCurve directionZ { get => m_DirectionZ.Get(); set => m_DirectionZ.Set(value); }
        public ParticleSystem.MinMaxCurve gravity { get => m_Gravity.Get(); set => m_Gravity.Set(value); }
        public ParticleSystem.MinMaxCurve rotationSpeed { get => m_RotationSpeed.Get(); set => m_RotationSpeed.Set(value); }
        public ParticleSystem.MinMaxCurve rotationAttraction { get => m_RotationAttraction.Get(); set => m_RotationAttraction.Set(value); }
        public ParticleSystem.MinMaxCurve drag { get => m_Drag.Get(); set => m_Drag.Set(value); }
        public ParticleSystem.MinMaxCurve vectorFieldSpeed { get => m_VectorFieldSpeed.Get(); set => m_VectorFieldSpeed.Set(value); }
        public ParticleSystem.MinMaxCurve vectorFieldAttraction { get => m_VectorFieldAttraction.Get(); set => m_VectorFieldAttraction.Set(value); }
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
