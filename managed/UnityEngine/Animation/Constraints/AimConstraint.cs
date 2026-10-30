using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Animations
{
    [ClassId(895512359)]
    public sealed partial class AimConstraint : Behaviour, IConstraint, IConstraintInternal
    {
        internal readonly ConstraintData m_Constraint = new ConstraintData(ConstraintKind.Aim);
        public float weight { get => m_Constraint.Weight; set => m_Constraint.Weight = value; }
        public bool constraintActive { get => m_Constraint.Active; set => m_Constraint.Active = value; }
        public bool locked { get => m_Constraint.Locked; set => m_Constraint.Locked = value; }
        public int sourceCount => m_Constraint.Sources.Count;
        public Vector3 rotationAtRest { get => m_Constraint.RotationRest; set => m_Constraint.RotationRest = value; }
        public Vector3 rotationOffset { get => m_Constraint.RotationOffset; set => m_Constraint.RotationOffset = value; }
        public Axis rotationAxis { get => m_Constraint.RotationAxis; set => m_Constraint.RotationAxis = value; }
        public Vector3 aimVector { get => m_Constraint.Aim; set => m_Constraint.Aim = value; }
        public Vector3 upVector { get => m_Constraint.Up; set => m_Constraint.Up = value; }
        public Vector3 worldUpVector { get => m_Constraint.WorldUp; set => m_Constraint.WorldUp = value; }
        public Transform worldUpObject { get => m_Constraint.UpObject; set => m_Constraint.UpObject = value; }
        public WorldUpType worldUpType { get => m_Constraint.UpType; set => m_Constraint.UpType = value; }

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

        internal override void OnAttached() => m_Constraint.Attach(this);
        internal override void OnEngineEnable() => m_Constraint.Enable();
        internal override void OnEngineDisable() => ConstraintSystem.Remove(m_Constraint);
        internal override void OnEngineDestroy() { ConstraintSystem.Remove(m_Constraint); base.OnEngineDestroy(); }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => m_Constraint.Read(body, resolver);
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            m_Enabled = ((AimConstraint)source).m_Enabled;
            m_Constraint.Copy(((AimConstraint)source).m_Constraint, remap);
        }
    }
}
