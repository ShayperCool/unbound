using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Rendering
{
    /// <summary>Renderers under a sorting group sort together, as one unit, by the group's sorting
    /// layer, order and distance; inside the group they sort among themselves.</summary>
    [global::UnityEngine.@RequireComponent(typeof(global::UnityEngine.Transform))] [ClassId(210)]
    public sealed partial class SortingGroup : Behaviour
    {
        // Reserved lookup index recorded by tests/projects/sortinglookup on Unity 6000.0.71f1.
        internal const int InvalidIndex = (1 << 20) - 1;
        static readonly List<SortingGroup> s_ActiveGroups = new List<SortingGroup>();
        int m_Index = InvalidIndex;

        internal static int invalidSortingGroupID
        {
            get { MainThreadCheck.Check("get_invalidSortingGroupID"); return InvalidIndex; }
        }

        internal int index
        {
            get { MainThreadCheck.Check("get_index"); return m_Index; }
        }

        internal static SortingGroup GetSortingGroupByIndex(int index)
        {
            MainThreadCheck.Check("GetSortingGroupByIndex");
            return (uint)index < (uint)s_ActiveGroups.Count ? s_ActiveGroups[index] : null;
        }

        internal override void OnEngineEnable()
        {
            if (m_Index != InvalidIndex) return;
            // Registration follows the hierarchy from descendants to parents. The guard above
            // keeps later enable hooks for the registered descendants from walking them again.
            using var lease = UnityEngine.Pool.ListPool<(Transform Node, bool Visited)>.Get(out var pending);
            pending.Add((m_GameObject.m_Transform, false));
            while (pending.Count != 0)
            {
                int last = pending.Count - 1;
                var entry = pending[last];
                pending.RemoveAt(last);
                var node = entry.Node;
                var owner = node.m_GameObject;
                if (owner.m_Destroyed || !owner.m_IsActive) continue;
                if (!entry.Visited)
                {
                    pending.Add((node, true));
                    for (int i = node.m_Children.Count - 1; i >= 0; i--) pending.Add((node.m_Children[i], false));
                    continue;
                }
                foreach (var component in owner.m_Components)
                {
                    if (!(component is SortingGroup group) || group.m_Destroyed || !group.m_Enabled || group.m_Index != InvalidIndex) continue;
                    if (s_ActiveGroups.Count == InvalidIndex)
                        throw new NotSupportedException("SortingGroup lookup has exhausted its active-group index range.");
                    group.m_Index = s_ActiveGroups.Count;
                    s_ActiveGroups.Add(group);
                }
            }
        }

        internal override void OnEngineDisable() => Unregister();
        internal override void OnEngineDestroy() => Unregister();

        void Unregister()
        {
            if (m_Index == InvalidIndex) return;
            int last = s_ActiveGroups.Count - 1;
            var moved = s_ActiveGroups[last];
            s_ActiveGroups[m_Index] = moved;
            moved.m_Index = m_Index;
            s_ActiveGroups.RemoveAt(last);
            m_Index = InvalidIndex;
        }

        internal int m_SortingLayerID;
        internal int m_SortingOrder;
        internal bool m_SortAtRoot;

        public SortingGroup() { }

        public string sortingLayerName
        {
            get => SortingLayer.IDToName(m_SortingLayerID);
            set => m_SortingLayerID = SortingLayer.NameToID(value);
        }

        public int sortingLayerID { get => m_SortingLayerID; set => m_SortingLayerID = value; }
        public int sortingOrder { get => m_SortingOrder; set => m_SortingOrder = value; }

        /// <summary>Sort with the renderers at the root, ignoring enclosing groups.</summary>
        public bool sortAtRoot { get => m_SortAtRoot; set => m_SortAtRoot = value; }

        /// <summary>The nearest active and enabled sorting group on a renderer's GameObject or above
        /// it.</summary>
        internal static SortingGroup Enclosing(Renderer renderer) => NearestFrom(renderer.m_GameObject?.m_Transform);

        /// <summary>The nearest active and enabled sorting group on <paramref name="start"/>'s
        /// GameObject or above it; none from a null transform.</summary>
        internal static SortingGroup NearestFrom(Transform start)
        {
            for (var t = start; t != null; t = t.m_Parent)
            {
                var group = t.m_GameObject.GetComponent<SortingGroup>();
                if (group != null && group.isActiveAndEnabled) return group;
            }
            return null;
        }

        [Unbound.EditorFaithful("no-op is equivalent: Unbound reads sorting groups when it draws, so they are always up to date")]
        public static void UpdateAllSortingGroups() { }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_SortingLayerID = body.Int("m_SortingLayerID");
            m_SortingOrder = body.Int("m_SortingOrder");
            m_SortAtRoot = body.Bool("m_SortAtRoot");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SortingGroup)source;
            m_SortingLayerID = s.m_SortingLayerID;
            m_SortingOrder = s.m_SortingOrder;
            m_SortAtRoot = s.m_SortAtRoot;
        }
    }
}
