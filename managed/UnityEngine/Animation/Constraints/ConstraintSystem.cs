using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>Constraints run after animation and before script LateUpdate. Source constraints
    /// evaluate first, even when their components were enabled after their dependents.</summary>
    internal static class ConstraintSystem
    {
        static readonly List<ConstraintData> s_Constraints = new List<ConstraintData>();
        static readonly Dictionary<Transform, List<ConstraintData>> s_ByTransform = new Dictionary<Transform, List<ConstraintData>>();
        static readonly Dictionary<ConstraintData, byte> s_Visited = new Dictionary<ConstraintData, byte>();
        static bool s_Registered;

        internal static void Add(ConstraintData data)
        {
            if (!s_Registered)
            {
                s_Registered = true;
                PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PreLateUpdate.ConstraintManagerUpdate), Update);
            }
            if (!s_Constraints.Contains(data)) s_Constraints.Add(data);
        }
        internal static void Remove(ConstraintData data) => s_Constraints.Remove(data);
        static void Update()
        {
            s_Visited.Clear(); s_ByTransform.Clear();
            foreach (var c in s_Constraints)
            {
                if (!c.Owner.isActiveAndEnabled || !c.Active) continue;
                if (!s_ByTransform.TryGetValue(c.Owner.transform, out var list))
                    s_ByTransform[c.Owner.transform] = list = new List<ConstraintData>();
                list.Add(c);
            }
            foreach (var c in s_Constraints) Visit(c);
        }
        static void Visit(ConstraintData c)
        {
            if (s_Visited.ContainsKey(c) || !c.Owner.isActiveAndEnabled || !c.Active) return;
            s_Visited[c] = 1;
            VisitTransform(c.Owner.transform.parent);
            foreach (var source in c.Sources) VisitTransform(source.sourceTransform);
            VisitTransform(c.UpObject);
            c.Evaluate();
            s_Visited[c] = 2;
        }
        static void VisitTransform(Transform transform)
        {
            for (var t = transform; t != null; t = t.parent)
                if (s_ByTransform.TryGetValue(t, out var constraints))
                    foreach (var c in constraints) Visit(c);
        }
    }
}
