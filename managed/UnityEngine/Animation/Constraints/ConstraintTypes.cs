using System;
using System.Collections.Generic;

namespace UnityEngine.Animations
{
    [Flags]
    public enum Axis { None = 0, X = 1, Y = 2, Z = 4 }

    public partial interface IConstraint
    {
        int AddSource(ConstraintSource source);
        void RemoveSource(int index);
        ConstraintSource GetSource(int index);
        void SetSource(int index, ConstraintSource source);
        void GetSources(List<ConstraintSource> sources);
        void SetSources(List<ConstraintSource> sources);
        float weight { get; set; }
        bool constraintActive { get; set; }
        bool locked { get; set; }
        int sourceCount { get; }
    }

    internal partial interface IConstraintInternal
    {
        void ActivateAndPreserveOffset();
        void ActivateWithZeroOffset();
        void UserUpdateOffset();
        Transform transform { get; }
    }

    public sealed partial class AimConstraint
    {
        public enum WorldUpType { SceneUp = 0, ObjectUp = 1, ObjectRotationUp = 2, Vector = 3, None = 4 }
    }
}
