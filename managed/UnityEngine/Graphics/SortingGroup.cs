using System;
using Unbound.Runtime;

namespace UnityEngine.Rendering
{
    /// <summary>Renderers under a sorting group sort together, as one unit, by the group's sorting
    /// layer, order and distance; inside the group they sort among themselves.</summary>
    [ClassId(210)]
    public sealed partial class SortingGroup : Behaviour
    {
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
