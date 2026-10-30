using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public enum EffectorForceMode2D { Constant = 0, InverseLinear = 1, InverseSquared = 2 }

    [ClassId(249)]
    public partial class AreaEffector2D : Effector2D
    {
        internal Random.State m_Random = Random.State.FromSeed(0);
        internal float m_Angle, m_Force = 10f, m_Variation, m_Drag, m_AngularDrag;
        internal bool m_GlobalAngle;
        internal EffectorSelection2D m_Target;
        public AreaEffector2D() { }
        internal override bool Implemented => true;
        internal override bool TriggerEffector => true;
        public float forceAngle { get => m_Angle; set => m_Angle = Mathf.Clamp(value, -359.9999f, 359.9999f); }
        public bool useGlobalAngle { get => m_GlobalAngle; set => m_GlobalAngle = value; }
        public float forceMagnitude { get => m_Force; set => m_Force = Mathf.Clamp(value, -1000000f, 1000000f); }
        public float forceVariation { get => m_Variation; set => m_Variation = Mathf.Clamp(value, -1000000f, 1000000f); }
        public float drag { get => m_Drag; set => m_Drag = Mathf.Clamp(value, 0f, 1000000f); }
        public float angularDrag { get => m_AngularDrag; set => m_AngularDrag = Mathf.Clamp(value, 0f, 1000000f); }
        public EffectorSelection2D forceTarget { get => m_Target; set => m_Target = value; }
        [Obsolete("AreaEffector2D.forceDirection has been deprecated. Use AreaEffector2D.forceAngle instead (UnityUpgradable) -> forceAngle")]
        public float forceDirection { get => forceAngle; set => forceAngle = value; }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_Angle = body.Float("m_ForceAngle"); m_GlobalAngle = body.Bool("m_UseGlobalAngle");
            m_Force = body.Float("m_ForceMagnitude", 10f); m_Variation = body.Float("m_ForceVariation");
            m_Drag = body.Float("m_Drag"); m_AngularDrag = body.Float("m_AngularDrag"); m_Target = (EffectorSelection2D)body.Int("m_ForceTarget");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (AreaEffector2D)source;
            m_Angle = s.m_Angle; m_GlobalAngle = s.m_GlobalAngle; m_Force = s.m_Force; m_Variation = s.m_Variation;
            m_Drag = s.m_Drag; m_AngularDrag = s.m_AngularDrag; m_Target = s.m_Target;
        }
    }

    [ClassId(253)]
    public partial class BuoyancyEffector2D : Effector2D
    {
        internal Random.State m_Random = Random.State.FromSeed(0);
        internal float m_Surface, m_Density = 2f, m_LinearDrag = 5f, m_AngularDrag = 5f, m_FlowAngle, m_Flow, m_Variation;
        public BuoyancyEffector2D() { }
        internal override bool Implemented => true;
        internal override bool TriggerEffector => true;
        public float surfaceLevel { get => m_Surface; set => m_Surface = Mathf.Clamp(value, -1000000f, 1000000f); }
        public float density { get => m_Density; set => m_Density = Mathf.Clamp(value, 0f, 1000000f); }
        public float linearDrag { get => m_LinearDrag; set => m_LinearDrag = Mathf.Clamp(value, 0f, 1000000f); }
        public float angularDrag { get => m_AngularDrag; set => m_AngularDrag = Mathf.Clamp(value, 0f, 1000000f); }
        public float flowAngle { get => m_FlowAngle; set => m_FlowAngle = Mathf.Clamp(value, -359.9999f, 359.9999f); }
        public float flowMagnitude { get => m_Flow; set => m_Flow = Mathf.Clamp(value, -1000000f, 1000000f); }
        public float flowVariation { get => m_Variation; set => m_Variation = Mathf.Clamp(value, -1000000f, 1000000f); }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_Surface = body.Float("m_SurfaceLevel"); m_Density = body.Float("m_Density", 2f);
            m_LinearDrag = body.Float("m_LinearDrag", 5f); m_AngularDrag = body.Float("m_AngularDrag", 5f);
            m_FlowAngle = body.Float("m_FlowAngle"); m_Flow = body.Float("m_FlowMagnitude"); m_Variation = body.Float("m_FlowVariation");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (BuoyancyEffector2D)source;
            m_Surface = s.m_Surface; m_Density = s.m_Density; m_LinearDrag = s.m_LinearDrag; m_AngularDrag = s.m_AngularDrag;
            m_FlowAngle = s.m_FlowAngle; m_Flow = s.m_Flow; m_Variation = s.m_Variation;
        }
    }

    [ClassId(250)]
    public partial class PointEffector2D : Effector2D
    {
        internal Random.State m_Random = Random.State.FromSeed(0);
        internal float m_Force = -10f, m_Variation, m_DistanceScale = 1f, m_Drag, m_AngularDrag;
        internal EffectorSelection2D m_Source, m_Target;
        internal EffectorForceMode2D m_Mode;
        public PointEffector2D() { }
        internal override bool Implemented => true;
        internal override bool TriggerEffector => true;
        public float forceMagnitude { get => m_Force; set => m_Force = Mathf.Clamp(value, -1000000f, 1000000f); }
        public float forceVariation { get => m_Variation; set => m_Variation = Mathf.Clamp(value, -1000000f, 1000000f); }
        public float distanceScale { get => m_DistanceScale; set => m_DistanceScale = Mathf.Clamp(value, .0001f, 1000000f); }
        public float drag { get => m_Drag; set => m_Drag = Mathf.Clamp(value, 0f, 1000000f); }
        public float angularDrag { get => m_AngularDrag; set => m_AngularDrag = Mathf.Clamp(value, 0f, 1000000f); }
        public EffectorSelection2D forceSource { get => m_Source; set => m_Source = value; }
        public EffectorSelection2D forceTarget { get => m_Target; set => m_Target = value; }
        public EffectorForceMode2D forceMode { get => m_Mode; set => m_Mode = value; }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_Force = body.Float("m_ForceMagnitude", -10f); m_Variation = body.Float("m_ForceVariation");
            m_DistanceScale = body.Float("m_DistanceScale", 1f); m_Drag = body.Float("m_Drag"); m_AngularDrag = body.Float("m_AngularDrag");
            m_Source = (EffectorSelection2D)body.Int("m_ForceSource"); m_Target = (EffectorSelection2D)body.Int("m_ForceTarget"); m_Mode = (EffectorForceMode2D)body.Int("m_ForceMode");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (PointEffector2D)source;
            m_Force = s.m_Force; m_Variation = s.m_Variation; m_DistanceScale = s.m_DistanceScale;
            m_Drag = s.m_Drag; m_AngularDrag = s.m_AngularDrag; m_Source = s.m_Source; m_Target = s.m_Target; m_Mode = s.m_Mode;
        }
    }
}
