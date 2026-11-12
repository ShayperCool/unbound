using System;
using System.ComponentModel;

namespace UnityEngine
{
    public partial class Rigidbody
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Please use Rigidbody.solverIterations instead. (UnityUpgradable) -> solverIterations", true)]
        public int solverIterationCount { get => solverIterations; set => solverIterations = value; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Please use Rigidbody.solverVelocityIterations instead. (UnityUpgradable) -> solverVelocityIterations", true)]
        public int solverVelocityIterationCount { get => solverVelocityIterations; set => solverVelocityIterations = value; }
    }
}
