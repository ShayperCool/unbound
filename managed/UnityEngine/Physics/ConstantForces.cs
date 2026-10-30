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
        static bool s_Registered;
        static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate), Update);
        }
        internal static void Add(ConstantForce force) { EnsureRegistered(); if (!s_Three.Contains(force)) s_Three.Add(force); }
        internal static void Add(ConstantForce2D force) { EnsureRegistered(); if (!s_Two.Contains(force)) s_Two.Add(force); }
        internal static void Remove(ConstantForce force) => s_Three.Remove(force);
        internal static void Remove(ConstantForce2D force) => s_Two.Remove(force);
        static void Update()
        {
            foreach (var force in s_Three) if (force != null && force.isActiveAndEnabled) force.Apply();
            foreach (var force in s_Two) if (force != null && force.isActiveAndEnabled) force.Apply();
        }
    }
}
