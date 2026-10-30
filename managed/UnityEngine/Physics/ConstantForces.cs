using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>Both dimensions queue forces once per fixed script update, rather than once
    /// per simulation call. Forces can accumulate across fixed updates in Script mode.</summary>
    internal static class ConstantForces
    {
        static readonly List<ConstantForce> s_Three = new List<ConstantForce>();
        static readonly List<ConstantForce2D> s_Two = new List<ConstantForce2D>();
        internal static void Add(ConstantForce force) { if (!s_Three.Contains(force)) s_Three.Add(force); }
        internal static void Add(ConstantForce2D force) { if (!s_Two.Contains(force)) s_Two.Add(force); }
        internal static void Remove(ConstantForce force) => s_Three.Remove(force);
        internal static void Remove(ConstantForce2D force) => s_Two.Remove(force);
        internal static void Update()
        {
            foreach (var force in s_Three) if (force != null && force.isActiveAndEnabled) force.Apply();
            foreach (var force in s_Two) if (force != null && force.isActiveAndEnabled) force.Apply();
        }
    }
}
