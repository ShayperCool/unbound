using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Animations
{
    [ClassId(1773428102)]
    public sealed partial class ParentConstraint : Behaviour, IConstraint, IConstraintInternal
    {
        internal readonly ConstraintData m_Constraint = new ConstraintData(ConstraintKind.Parent);
        public float weight { get => m_Constraint.Weight; set => m_Constraint.Weight = value; }
        public bool constraintActive { get => m_Constraint.Active; set => m_Constraint.Active = value; }
        public bool locked { get => m_Constraint.Locked; set => m_Constraint.Locked = value; }
        public int sourceCount => m_Constraint.Sources.Count;
        public Vector3 translationAtRest { get => m_Constraint.TranslationRest; set => m_Constraint.TranslationRest = value; }
        public Vector3 rotationAtRest { get => m_Constraint.RotationRest; set => m_Constraint.RotationRest = value; }
        public Axis translationAxis { get => m_Constraint.TranslationAxis; set => m_Constraint.TranslationAxis = value; }
        public Axis rotationAxis { get => m_Constraint.RotationAxis; set => m_Constraint.RotationAxis = value; }

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
            m_Enabled = ((ParentConstraint)source).m_Enabled;
            m_Constraint.Copy(((ParentConstraint)source).m_Constraint, remap);
        }

        public Vector3[] translationOffsets { get => m_Constraint.TranslationOffsets.ToArray(); set => ConstraintData.SetOffsets(m_Constraint.TranslationOffsets, value); }
        public Vector3[] rotationOffsets { get => m_Constraint.RotationOffsets.ToArray(); set => ConstraintData.SetOffsets(m_Constraint.RotationOffsets, value); }
        public Vector3 GetTranslationOffset(int index) => m_Constraint.GetOffset(m_Constraint.TranslationOffsets, index);
        public void SetTranslationOffset(int index, Vector3 value) => m_Constraint.SetOffset(m_Constraint.TranslationOffsets, index, value);
        public Vector3 GetRotationOffset(int index) => m_Constraint.GetOffset(m_Constraint.RotationOffsets, index);
        public void SetRotationOffset(int index, Vector3 value) => m_Constraint.SetOffset(m_Constraint.RotationOffsets, index, value);
    }
}
