using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Animations
{
    [ClassId(1818360610)]
    public sealed partial class ScaleConstraint : Behaviour, IConstraint, IConstraintInternal
    {
        internal readonly ConstraintData m_Constraint = new ConstraintData(ConstraintKind.Scale);
        public float weight { get => m_Constraint.Weight; set => m_Constraint.Weight = value; }
        public bool constraintActive { get => m_Constraint.Active; set => m_Constraint.Active = value; }
        public bool locked { get => m_Constraint.Locked; set => m_Constraint.Locked = value; }
        public int sourceCount => m_Constraint.Sources.Count;
        public Vector3 scaleAtRest { get => m_Constraint.ScaleRest; set => m_Constraint.ScaleRest = value; }
        public Vector3 scaleOffset { get => m_Constraint.ScaleOffset; set => m_Constraint.ScaleOffset = value; }
        public Axis scalingAxis { get => m_Constraint.ScaleAxis; set => m_Constraint.ScaleAxis = value; }

        public void GetSources(List<ConstraintSource> sources) => m_Constraint.GetSources(sources);
        public void SetSources(List<ConstraintSource> sources) => m_Constraint.SetSources(sources);
        public int AddSource(ConstraintSource source) => m_Constraint.AddSource(source);
        public void RemoveSource(int index) => m_Constraint.RemoveSource(index);
        public ConstraintSource GetSource(int index) => m_Constraint.GetSource(index);
        public void SetSource(int index, ConstraintSource source) => m_Constraint.SetSource(index, source);
        void IConstraintInternal.ActivateAndPreserveOffset() => m_Constraint.Activate(true);
        void IConstraintInternal.ActivateWithZeroOffset() => m_Constraint.Activate(false);
        void IConstraintInternal.UserUpdateOffset() => m_Constraint.UpdateOffset();
        Transform IConstraintInternal.transform => transform;

        internal override void OnAttached() => m_Constraint.Owner = this;
        internal override void OnEngineEnable() => m_Constraint.Enable();
        internal override void OnEngineDisable() => ConstraintSystem.Remove(m_Constraint);
        internal override void OnEngineDestroy() { ConstraintSystem.Remove(m_Constraint); base.OnEngineDestroy(); }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => m_Constraint.Read(body, resolver);
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            m_Enabled = ((ScaleConstraint)source).m_Enabled;
            m_Constraint.Copy(((ScaleConstraint)source).m_Constraint, remap);
        }
    }
}
