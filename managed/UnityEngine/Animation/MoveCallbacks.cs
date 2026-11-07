using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>The OnAnimatorMove callbacks of the scripts on an Animator's GameObject, shared by
    /// controller and playable-output root motion, without allocating per call.</summary>
    internal static class MoveCallbacks
    {
        static readonly ConcurrentDictionary<Type, MethodInfo> s_Methods = new ConcurrentDictionary<Type, MethodInfo>();
        // Callbacks may add or remove components or move another Animator reentrantly: each
        // Invoke walks its own pooled snapshot of the component list.
        static readonly Stack<List<Component>> s_Snapshots = new Stack<List<Component>>();

        static MethodInfo Method(MonoBehaviour behaviour) => s_Methods.GetOrAdd(behaviour.GetType(),
            static type => ScriptMessages.Find(type, "OnAnimatorMove", Type.EmptyTypes));

        /// <summary>A script on the Animator's GameObject implements OnAnimatorMove (enabled or
        /// not, as for the Animator's own root motion).</summary>
        internal static bool Any(Animator animator)
        {
            var components = animator.gameObject.m_Components;
            for (int i = 0; i < components.Count; i++)
                if (components[i] is MonoBehaviour mb && mb != null && Method(mb) != null) return true;
            return false;
        }

        /// <summary>Invokes OnAnimatorMove on the enabled scripts that implement it; whether any
        /// script (enabled or not) implements it.</summary>
        internal static bool Invoke(Animator animator)
        {
            bool any = false;
            var components = s_Snapshots.Count > 0 ? s_Snapshots.Pop() : new List<Component>();
            components.AddRange(animator.gameObject.m_Components);
            try
            {
                foreach (var component in components)
                {
                    if (component is not MonoBehaviour mb || mb == null) continue;
                    var method = Method(mb);
                    if (method == null) continue;
                    any = true;
                    if (!mb.isActiveAndEnabled) continue;
                    try { method.Invoke(mb, null); }
                    catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e, mb); }
                }
            }
            finally
            {
                components.Clear();
                s_Snapshots.Push(components);
            }
            return any;
        }
    }
}
