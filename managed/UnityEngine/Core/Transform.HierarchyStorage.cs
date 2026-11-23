using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial class Transform
    {
        // Size of this subtree. Only the root owns the packed storage.
        int m_SubtreeCount = 1;
        int m_HierarchyCapacity = 1;
        int m_LegacyHierarchyCapacity = 1;
        bool m_AllocatedHierarchy;
        int m_ReservedChildSlots;

        static bool IsLiveTransform(Transform transform) =>
            !transform.m_Destroyed && !transform.IsPersistentAsset
            && transform.m_GameObject is { m_Destroyed: false, IsPersistentAsset: false, m_IsPrefabAsset: false };

        Transform HierarchyStorageOwner
        {
            get
            {
                if (!Compat.TransformHierarchyStorageMeasured || !IsLiveTransform(this)) return null;
                var owner = root;
                return IsLiveTransform(owner) ? owner : null;
            }
        }

        internal bool MeasuresHierarchyStorage => HierarchyStorageOwner != null;

        public int hierarchyCapacity
        {
            get => HierarchyStorageOwner?.m_HierarchyCapacity ?? m_LegacyHierarchyCapacity;
            set
            {
                var owner = HierarchyStorageOwner;
                if (owner == null)
                {
                    m_LegacyHierarchyCapacity = value;
                    return;
                }
                int count = owner.m_SubtreeCount;
                if (count == 0) return; // The branch's hierarchy was released during destruction.
                if (value < count)
                {
                    Log.Engine(LogType.Warning,
                        $"Changing desired transform hierarchy capacity ({value}) to the current transform count ({count})",
                        this, respectStackTraceSetting: true);
                    // The log callback can change the tree. Keep the original owner and use
                    // its current size so the reservation still holds every transform.
                    if (!ReferenceEquals(owner, HierarchyStorageOwner)) return;
                    value = Math.Max(value, owner.m_SubtreeCount);
                }
                if (value != owner.m_HierarchyCapacity) owner.ResizeHierarchy(value);
            }
        }

        public int hierarchyCount => HierarchyStorageOwner?.m_SubtreeCount ?? CountSubtree();

        int CountSubtree()
        {
            int count = 1;
            foreach (var child in m_Children) count += child.CountSubtree();
            return count;
        }

        internal override void OnAttached()
        {
            base.OnAttached();
            if (Compat.TransformHierarchyStorageMeasured && !ComponentFactory.IsDeserializing)
                m_AllocatedHierarchy = this is not RectTransform;
        }

        internal int ProfilerHierarchyCapacity => m_Parent == null && m_AllocatedHierarchy ? m_HierarchyCapacity : 0;
        internal int ProfilerReservedChildSlots => m_ReservedChildSlots;

        /// <summary>Initialize the finished scene or clone once. A clone reserves its current
        /// child slots immediately and defers its root allocation while its topology fits.</summary>
        internal void InitializeHierarchyStorage(bool allocated)
        {
            if (!Compat.TransformHierarchyStorageMeasured) return;
            m_HierarchyCapacity = InitializeHierarchyBranch();
            m_AllocatedHierarchy = allocated;
        }

        int InitializeHierarchyBranch()
        {
            m_ReservedChildSlots = m_Children.Count;
            int count = 1;
            foreach (var child in m_Children) count += child.InitializeHierarchyBranch();
            return m_SubtreeCount = count;
        }

        void ResizeHierarchy(int capacity)
        {
            m_HierarchyCapacity = capacity;
            m_AllocatedHierarchy = true;
        }

        internal void RecordHierarchyParentChange(Transform oldParent, Transform parent, bool destroying = false)
        {
            if (!Compat.TransformHierarchyStorageMeasured || ReferenceEquals(oldParent, parent)) return;
            for (var ancestor = oldParent; ancestor != null; ancestor = ancestor.m_Parent)
                ancestor.m_SubtreeCount -= m_SubtreeCount;

            if (parent == null)
            {
                if (destroying)
                {
                    // The surviving parent can still observe these transforms during its
                    // children-changed message, after their shared storage has been released.
                    m_SubtreeCount = 0;
                    m_HierarchyCapacity = 0;
                    m_AllocatedHierarchy = false;
                }
                else ResizeHierarchy(m_SubtreeCount);
                return;
            }

            if (parent.m_Children.Count > parent.m_ReservedChildSlots)
                parent.m_ReservedChildSlots = Math.Max(parent.m_Children.Count, 2 * parent.m_ReservedChildSlots);
            Transform owner = parent;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.m_Parent)
            {
                ancestor.m_SubtreeCount += m_SubtreeCount;
                owner = ancestor;
            }
            if (owner.m_SubtreeCount > owner.m_HierarchyCapacity)
                owner.ResizeHierarchy(2 * owner.m_SubtreeCount);
        }

        internal void AdoptReplacedHierarchy(Transform previous)
        {
            if (!Compat.TransformHierarchyStorageMeasured) return;
            m_HierarchyCapacity = previous.m_HierarchyCapacity;
            m_SubtreeCount = previous.m_SubtreeCount;
            m_ReservedChildSlots = previous.m_ReservedChildSlots;
            m_AllocatedHierarchy = false;
        }
    }
}
