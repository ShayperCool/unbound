using System;
using System.Collections;
using System.Collections.Generic;

// Drag and drop of collection view items (reordering a ListView or a tree view by dragging rows) on runtime
// panels, where the drag runs on pointer events: a left press on a row arms it, a move of 10 points
// (squared distance 100) starts the drag with the pressed row (selected first) or the selection,
// moves show the drop position (a hover bar between rows, a hover class over a row) and scroll near
// the view's edges, and the release drops the items there.
// Documented (Unity 6000.0 Scripting API): BaseVerticalCollectionView.canStartDrag,
// setupDragAndDrop, dragAndDropUpdate and handleDrop with their argument types, DragVisualMode,
// DragAndDropPosition, StartDragArgs; reorderable "allows the user to reorder items by dragging".
// Matches the Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (reorder section: dragged
// rows, moved items, itemIndexChanged per moved item, the selection after a drop, a short move that
// stays a click; tree-drag sections: drops onto and between tree rows). Editor-context panels use
// the same pointer path (the Editor's own drag events are not used).
namespace UnityEngine.UIElements
{
    public enum DragVisualMode
    {
        None,
        Copy,
        Move,
        Rejected,
    }

    public enum DragAndDropPosition
    {
        OverItem,
        BetweenItems,
        OutsideItems,
    }

    internal partial interface IDragAndDropData
    {
        object userData { get; }
        IEnumerable<Object> unityObjectReferences { get; }
        string[] paths { get; set; }
        object GetGenericData(string key);
    }

    public abstract partial class DragAndDropData : IDragAndDropData
    {
        internal const string dragSourceKey = "__unity-drag-and-drop__source-view";

        object IDragAndDropData.userData => GetGenericData(dragSourceKey);

        public abstract object source { get; }

        public abstract DragVisualMode visualMode { get; }

        public abstract IEnumerable<Object> unityObjectReferences { get; }

        public virtual string[] paths { get; set; }

        public abstract object GetGenericData(string key);

        public abstract void SetGenericData(string key, object data);
    }

    public partial struct StartDragArgs
    {
        public string title { get; }

        public DragVisualMode visualMode { get; }

        internal Hashtable genericData { get; private set; }

        internal IEnumerable<Object> unityObjectReferences { get; private set; }

        internal string[] assetPaths { get; private set; }

        public StartDragArgs(string title, DragVisualMode visualMode)
        {
            this.title = title;
            this.visualMode = visualMode;
            genericData = null;
            assetPaths = null;
            unityObjectReferences = null;
        }

        public void SetGenericData(string key, object data)
        {
            genericData ??= new Hashtable();
            genericData[key] = data;
        }

        public void SetUnityObjectReferences(IEnumerable<Object> references) => unityObjectReferences = references;

        public void SetPaths(string[] paths) => assetPaths = paths;
    }

    public readonly partial struct CanStartDragArgs
    {
        public readonly VisualElement draggedElement;
        public readonly int id;
        public readonly IEnumerable<int> selectedIds;

        internal CanStartDragArgs(VisualElement draggedElement, int id, IEnumerable<int> selectedIds)
        {
            this.draggedElement = draggedElement;
            this.id = id;
            this.selectedIds = selectedIds;
        }
    }

    public readonly partial struct SetupDragAndDropArgs
    {
        public readonly VisualElement draggedElement;
        public readonly IEnumerable<int> selectedIds;
        public readonly StartDragArgs startDragArgs;

        internal SetupDragAndDropArgs(VisualElement draggedElement, IEnumerable<int> selectedIds, StartDragArgs startDragArgs)
        {
            this.draggedElement = draggedElement;
            this.selectedIds = selectedIds;
            this.startDragArgs = startDragArgs;
        }
    }

    public readonly partial struct HandleDragAndDropArgs
    {
        readonly DragAndDropArgs m_DragAndDropArgs;

        public Vector2 position { get; }

        public object target => m_DragAndDropArgs.target;

        public int insertAtIndex => m_DragAndDropArgs.insertAtIndex;

        public int parentId => m_DragAndDropArgs.parentId;

        public int childIndex => m_DragAndDropArgs.childIndex;

        public DragAndDropPosition dropPosition => m_DragAndDropArgs.dragAndDropPosition;

        public DragAndDropData dragAndDropData => m_DragAndDropArgs.dragAndDropData;

        internal HandleDragAndDropArgs(Vector2 position, DragAndDropArgs dragAndDropArgs)
        {
            this.position = position;
            m_DragAndDropArgs = dragAndDropArgs;
        }
    }

    internal partial interface IListDragAndDropArgs
    {
        object target { get; }
        int insertAtIndex { get; }
        int parentId { get; }
        int childIndex { get; }
        DragAndDropData dragAndDropData { get; }
        DragAndDropPosition dragAndDropPosition { get; }
    }

    internal partial struct DragAndDropArgs : IListDragAndDropArgs
    {
        public object target { get; set; }
        public int insertAtIndex { get; set; }
        public int parentId { get; set; }
        public int childIndex { get; set; }
        public DragAndDropPosition dragAndDropPosition { get; set; }
        public DragAndDropData dragAndDropData { get; set; }
    }

    internal partial interface IReorderable
    {
        bool enableReordering { get; set; }
    }

    internal partial interface IDragAndDropController<in TArgs>
    {
        bool CanStartDrag(IEnumerable<int> itemIds);

        bool CanDrop() => true;

        StartDragArgs SetupDragAndDrop(IEnumerable<int> itemIds, bool skipText = false);

        DragVisualMode HandleDragAndDrop(TArgs args);

        void OnDrop(TArgs args);

        void DragCleanup() { }

        void HandleAutoExpand(ReusableCollectionItem item, Vector2 pointerPosition) { }

        IEnumerable<int> GetSortedSelectedIds() => Array.Empty<int>();
    }

    internal partial interface ICollectionDragAndDropController : IDragAndDropController<IListDragAndDropArgs>, IReorderable
    {
    }

    /// <summary>Remembers the dragged ids (sorted) and the drag title: the first dragged row's label,
    /// "Item {id}" without one, "&lt;Multiple&gt;" for several.</summary>
    internal abstract partial class BaseReorderableDragAndDropController : ICollectionDragAndDropController
    {
        protected readonly BaseVerticalCollectionView m_View;
        protected List<int> m_SortedSelectedIds = new List<int>();

        public virtual bool enableReordering { get; set; } = true;

        public IEnumerable<int> GetSortedSelectedIds() => m_SortedSelectedIds;

        protected BaseReorderableDragAndDropController(BaseVerticalCollectionView view) => m_View = view;

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual bool CanStartDrag(IEnumerable<int> itemIds) => true;

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual bool CanDrop() => true;

        public virtual StartDragArgs SetupDragAndDrop(IEnumerable<int> itemIds, bool skipText = false)
        {
            m_SortedSelectedIds.Clear();
            string title = string.Empty;
            if (itemIds != null)
            {
                foreach (int id in itemIds)
                {
                    m_SortedSelectedIds.Add(id);
                    if (skipText) continue;
                    if (string.IsNullOrEmpty(title))
                    {
                        var label = m_View.GetRecycledItemFromId(id)?.rootElement.Q<Label>();
                        title = label != null ? label.text : $"Item {id}";
                    }
                    else
                    {
                        title = "<Multiple>";
                        skipText = true;
                    }
                }
            }
            m_SortedSelectedIds.Sort(CompareId);
            return m_View.reorderable ? new StartDragArgs(title, DragVisualMode.Move) : new StartDragArgs(string.Empty, DragVisualMode.Rejected);
        }

        protected virtual int CompareId(int id1, int id2) => id1.CompareTo(id2);

        public abstract DragVisualMode HandleDragAndDrop(IListDragAndDropArgs args);

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual void OnDrop(IListDragAndDropArgs args) { }

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual void DragCleanup() { }

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual void HandleAutoExpand(ReusableCollectionItem item, Vector2 pointerPosition) { }
    }

    /// <summary>Drops a list view's own items between rows: each dragged item moves (last to first)
    /// so the dragged block lands at the insert index in its sorted order; the moved items are then
    /// selected without notification (or the selection is cleared without a selection type).</summary>
    internal partial class ListViewReorderableDragAndDropController : BaseReorderableDragAndDropController
    {
        protected readonly BaseListView m_ListView;

        public ListViewReorderableDragAndDropController(BaseListView view) : base(view) => m_ListView = view;

        public override DragVisualMode HandleDragAndDrop(IListDragAndDropArgs args)
        {
            if (args.dragAndDropPosition == DragAndDropPosition.OverItem || !CanDrop()) return DragVisualMode.Rejected;
            return args.dragAndDropData.source == m_ListView ? DragVisualMode.Move : DragVisualMode.Rejected;
        }

        public override void OnDrop(IListDragAndDropArgs args)
        {
            base.OnDrop(args);
            if (!m_ListView.reorderable) return;
            int insertAt = args.insertAtIndex;
            int movedFromBefore = 0;
            int movedFromAfter = 0;
            for (int i = m_SortedSelectedIds.Count - 1; i >= 0; i--)
            {
                int index = m_View.viewController.GetIndexForId(m_SortedSelectedIds[i]);
                if (index < 0) continue;
                int destination = insertAt - movedFromBefore;
                if (index >= insertAt)
                {
                    // Items after the insert point shift down by the ones already moved before them.
                    index += movedFromAfter;
                    movedFromAfter++;
                }
                else if (index < destination)
                {
                    movedFromBefore++;
                    destination--;
                }
                m_ListView.viewController.Move(index, destination);
            }
            if (m_ListView.selectionType != SelectionType.None)
            {
                var selection = new List<int>(m_SortedSelectedIds.Count);
                for (int i = 0; i < m_SortedSelectedIds.Count; i++) selection.Add(insertAt - movedFromBefore + i);
                m_ListView.SetSelectionWithoutNotify(selection);
            }
            else
            {
                m_ListView.ClearSelection();
            }
        }
    }

    /// <summary>Drops a tree view's own items onto a row (as its last children, the row expanded) or
    /// between rows (under the parent and at the child index the dragger worked out; without one,
    /// last at the root). The dragged items move in tree order and keep that order where they
    /// land; the view refreshes once, then reports each item whose parent or child index changed.
    /// A row hovered for <see cref="k_DropExpandTimeoutMs"/> with the pointer still (within 10
    /// points, off the row's top and bottom 4 points) expands; when the drag ends, every item that
    /// was not expanded before the first such expansion is collapsed again. Matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/uitkcollections (tree-drag sections).</summary>
    internal partial class TreeViewReorderableDragAndDropController : BaseReorderableDragAndDropController
    {
        protected partial class DropData
        {
            public int[] expandedIdsBeforeDrag;
            public int[] draggedIds;
            public int lastItemId = -1;
            public float expandItemBeginTimerMs;
            public Vector2 expandItemBeginPosition;
        }

        // How often a hovered row is checked for expansion.
        const long k_ExpandUpdateIntervalMs = 10L;
        // Hover time that expands a collapsed row under a drag.
        const float k_DropExpandTimeoutMs = 700f;
        // Squared pointer travel that restarts the hover time (10 points).
        const float k_DropDeltaPosition = 100f;
        // A row's top and bottom margin that does not count as hovering it.
        const float k_HalfDropBetweenHeight = 4f;

        protected DropData m_DropData = new DropData();
        protected readonly BaseTreeView m_TreeView;
        IVisualElementScheduledItem m_ExpandDropItemScheduledItem;
        readonly Action m_ExpandDropItemCallback;
        readonly List<int> m_Path1 = new List<int>();
        readonly List<int> m_Path2 = new List<int>();
        readonly List<(int parentId, int childIndex)> m_Before = new List<(int, int)>();

        public TreeViewReorderableDragAndDropController(BaseTreeView view) : base(view)
        {
            m_TreeView = view;
            m_ExpandDropItemCallback = ExpandDropItem;
        }

        /// <summary>Tree order: below the nearest common ancestor (the root included), the branch
        /// with the lower child index comes first; an item comes before its own descendants.</summary>
        protected override int CompareId(int id1, int id2)
        {
            if (id1 == id2) return 0;
            var controller = m_TreeView.viewController;
            m_Path1.Clear();
            m_Path2.Clear();
            for (int id = id1; id != BaseTreeView.invalidId; id = controller.GetParentId(id)) m_Path1.Add(id);
            for (int id = id2; id != BaseTreeView.invalidId; id = controller.GetParentId(id)) m_Path2.Add(id);
            m_Path1.Add(BaseTreeView.invalidId);
            m_Path2.Add(BaseTreeView.invalidId);
            for (int i = 0; i < m_Path1.Count; i++)
            {
                int j = m_Path2.IndexOf(m_Path1[i]);
                if (j < 0) continue;
                if (i == 0) return -1;
                // When the second item is the common ancestor itself, its own child index stands
                // for its branch.
                int branch1 = m_Path1[i - 1];
                int branch2 = j > 0 ? m_Path2[j - 1] : id2;
                return controller.GetChildIndexForId(branch1).CompareTo(controller.GetChildIndexForId(branch2));
            }
            throw new ArgumentOutOfRangeException("[UI Toolkit] Trying to reorder ids that are not in the same tree.");
        }

        public override StartDragArgs SetupDragAndDrop(IEnumerable<int> itemIds, bool skipText = false)
        {
            var args = base.SetupDragAndDrop(itemIds, skipText);
            m_DropData.draggedIds = m_SortedSelectedIds.ToArray();
            return m_TreeView.reorderable ? args : new StartDragArgs(string.Empty, DragVisualMode.Rejected);
        }

        public override DragVisualMode HandleDragAndDrop(IListDragAndDropArgs args) =>
            args.dragAndDropData.source == m_TreeView && CanDrop() ? DragVisualMode.Move : DragVisualMode.Rejected;

        public override bool CanDrop() => base.CanDrop() || m_DropData?.draggedIds != null;

        public override void OnDrop(IListDragAndDropArgs args)
        {
            base.OnDrop(args);
            var dragged = m_DropData?.draggedIds;
            if (!m_TreeView.reorderable || dragged == null) return;
            var controller = m_TreeView.viewController;
            int parentId = args.parentId;
            int childIndex = args.childIndex;
            bool overItem = args.dragAndDropPosition == DragAndDropPosition.OverItem;
            bool toEnd = overItem || (parentId == BaseTreeView.invalidId && childIndex == -1);
            // Items that come from elsewhere, or from after the drop index, each take one place.
            int placed = 0;
            m_Before.Clear();
            foreach (int id in dragged)
            {
                int oldParentId = controller.GetParentId(id);
                int oldChildIndex = controller.GetChildIndexForId(id);
                m_Before.Add((oldParentId, oldChildIndex));
                if (toEnd)
                {
                    controller.Move(id, parentId, -1, false);
                    continue;
                }
                int index = childIndex + placed;
                if (oldParentId != parentId || oldChildIndex >= childIndex) placed++;
                controller.Move(id, parentId, index, false);
            }
            if (overItem) controller.ExpandItem(parentId, false, false);
            m_ExpandDropItemScheduledItem?.Pause();
            m_TreeView.RefreshItems();
            for (int i = 0; i < dragged.Length; i++)
            {
                int id = dragged[i];
                if (m_Before[i].parentId != controller.GetParentId(id) || m_Before[i].childIndex != controller.GetChildIndexForId(id))
                    controller.RaiseItemParentChanged(id, parentId);
            }
        }

        public override void DragCleanup()
        {
            base.DragCleanup();
            if (m_DropData != null)
            {
                if (m_DropData.expandedIdsBeforeDrag != null) RestoreExpanded(m_DropData.expandedIdsBeforeDrag);
                m_DropData = new DropData();
            }
            m_ExpandDropItemScheduledItem?.Pause();
        }

        /// <summary>Collapses every item that is not in the list (each one with children reports
        /// it, expanded or not), then refreshes the rows once.</summary>
        void RestoreExpanded(int[] expandedIds)
        {
            bool any = false;
            foreach (int id in m_TreeView.viewController.GetAllItemIds())
            {
                if (Array.IndexOf(expandedIds, id) >= 0) continue;
                m_TreeView.CollapseItem(id, false, false);
                any = true;
            }
            if (any) m_TreeView.RefreshItems();
        }

        public override void HandleAutoExpand(ReusableCollectionItem item, Vector2 pointerPosition)
        {
            int id = item.id;
            var bounds = item.bindableElement.worldBound;
            bool inside = new Rect(bounds.x, bounds.y + k_HalfDropBetweenHeight, bounds.width, bounds.height - 2f * k_HalfDropBetweenHeight).Contains(pointerPosition);
            var travel = m_DropData.expandItemBeginPosition - pointerPosition;
            if (id == m_DropData.lastItemId && inside && travel.sqrMagnitude < k_DropDeltaPosition) return;
            m_DropData.lastItemId = id;
            m_DropData.expandItemBeginTimerMs = Panel.TimeSinceStartupMs();
            m_DropData.expandItemBeginPosition = pointerPosition;
            if (m_ExpandDropItemScheduledItem == null)
            {
                m_ExpandDropItemScheduledItem = m_TreeView.schedule.Execute(m_ExpandDropItemCallback).Every(k_ExpandUpdateIntervalMs);
                return;
            }
            m_ExpandDropItemScheduledItem.Pause();
            m_ExpandDropItemScheduledItem.Resume();
        }

        void ExpandDropItem()
        {
            int id = m_DropData.lastItemId;
            if (Panel.TimeSinceStartupMs() - m_DropData.expandItemBeginTimerMs <= k_DropExpandTimeoutMs || !m_TreeView.viewController.Exists(id)) return;
            if (!m_TreeView.viewController.HasChildren(id) || m_TreeView.IsExpanded(id)) return;
            if (m_DropData.expandedIdsBeforeDrag == null)
            {
                var expanded = new List<int>();
                m_TreeView.viewController.GetExpandedItemIds(expanded);
                m_DropData.expandedIdsBeforeDrag = expanded.ToArray();
            }
            m_DropData.expandItemBeginTimerMs = Panel.TimeSinceStartupMs();
            // Not the invalid id: Unity names item 0 here.
            m_DropData.lastItemId = 0;
            m_TreeView.ExpandItem(id);
        }
    }

    internal partial interface IDragAndDrop
    {
        DragAndDropData data { get; }
        void StartDrag(StartDragArgs args, Vector3 pointerPosition);
        void UpdateDrag(Vector3 pointerPosition);
        void AcceptDrag();
        void DragCleanup();
        void SetVisualMode(DragVisualMode visualMode);
    }

    internal static partial class DragAndDropUtility
    {
        static IDragAndDrop s_DragAndDropEditor;
        static IDragAndDrop s_DragAndDropPlayMode;

        /// <summary>One drag-and-drop client for runtime panels and one for Editor panels.</summary>
        internal static IDragAndDrop GetDragAndDrop(IPanel panel) =>
            panel?.contextType == ContextType.Player
                ? s_DragAndDropPlayMode ??= new DefaultDragAndDropClient()
                : s_DragAndDropEditor ??= new DefaultDragAndDropClient();
    }

    /// <summary>The drag state of a panel: generic data (the source view), the visual mode and a
    /// label with the drag title that follows the pointer at the panel's root.</summary>
    internal partial class DefaultDragAndDropClient : DragAndDropData, IDragAndDrop
    {
        readonly Hashtable m_GenericData = new Hashtable();
        Label m_DraggedInfoLabel;
        DragVisualMode m_VisualMode;
        IEnumerable<Object> m_UnityObjectReferences;

        public override DragVisualMode visualMode => m_VisualMode;

        public override object source => GetGenericData(dragSourceKey);

        public override IEnumerable<Object> unityObjectReferences => m_UnityObjectReferences;

        public DragAndDropData data => this;

        public override object GetGenericData(string key) => m_GenericData.ContainsKey(key) ? m_GenericData[key] : null;

        public override void SetGenericData(string key, object value) => m_GenericData[key] = value;

        public void StartDrag(StartDragArgs args, Vector3 pointerPosition)
        {
            if (args.unityObjectReferences != null) m_UnityObjectReferences = new List<Object>(args.unityObjectReferences).ToArray();
            paths = args.assetPaths;
            m_VisualMode = args.visualMode;
            if (args.genericData != null)
                foreach (DictionaryEntry entry in args.genericData) m_GenericData[(string)entry.Key] = entry.Value;
            if (string.IsNullOrWhiteSpace(args.title)) return;
            var root = (source as VisualElement)?.panel?.visualTree;
            if (root == null) return;
            if (m_DraggedInfoLabel == null)
            {
                m_DraggedInfoLabel = new Label { pickingMode = PickingMode.Ignore };
                m_DraggedInfoLabel.style.position = Position.Absolute;
            }
            m_DraggedInfoLabel.text = args.title;
            m_DraggedInfoLabel.style.top = pointerPosition.y;
            m_DraggedInfoLabel.style.left = pointerPosition.x;
            root.Add(m_DraggedInfoLabel);
        }

        public void UpdateDrag(Vector3 pointerPosition)
        {
            if (m_DraggedInfoLabel == null) return;
            m_DraggedInfoLabel.style.top = pointerPosition.y;
            m_DraggedInfoLabel.style.left = pointerPosition.x;
        }

        [Unbound.EditorFaithful("Unity's runtime client accepts a drop without further work")]
        public void AcceptDrag() { }

        public void SetVisualMode(DragVisualMode mode) => m_VisualMode = mode;

        public void DragCleanup()
        {
            paths = null;
            m_UnityObjectReferences = null;
            m_GenericData.Clear();
            SetVisualMode(DragVisualMode.None);
            m_DraggedInfoLabel?.RemoveFromHierarchy();
        }
    }

    /// <summary>Turns pointer events on a target into a drag: press (armed when the subclass allows
    /// a drag from there), a move of more than <see cref="k_StartDragSqrDistance"/> (squared) or a
    /// pointer leaving the target starts it and captures the pointer, moves update it (on the view
    /// under the pointer when it is another collection view), the release drops.</summary>
    internal abstract partial class DragEventsProcessor
    {
        internal enum DragState
        {
            None,
            CanStartDrag,
            Dragging,
        }

        // Squared pointer travel that turns a press into a drag (10 points).
        const float k_StartDragSqrDistance = 100f;

        bool m_IsRegistered;
        DragState m_DragState;
        Vector3 m_Start;
        bool m_PendingPerformDrag;
        protected readonly VisualElement m_Target;

        internal bool isRegistered => m_IsRegistered;

        internal DragState dragState => m_DragState;

        [Unbound.EditorFaithful("Unity's base implementation")]
        protected virtual bool supportsDragEvents => true;

        protected IDragAndDrop dragAndDrop => DragAndDropUtility.GetDragAndDrop(m_Target.panel);

        readonly EventCallback<AttachToPanelEvent> m_OnAttach;
        readonly EventCallback<DetachFromPanelEvent> m_OnDetach;

        internal DragEventsProcessor(VisualElement target)
        {
            m_Target = target;
            m_OnAttach = _ => RegisterCallbacksFromTarget();
            m_OnDetach = _ => UnregisterCallbacksFromTarget();
            m_Target.RegisterCallback(m_OnAttach);
            m_Target.RegisterCallback(m_OnDetach);
            RegisterCallbacksFromTarget();
        }

        void RegisterCallbacksFromTarget()
        {
            if (m_IsRegistered) return;
            m_IsRegistered = true;
            m_Target.RegisterCallback<PointerDownEvent>(OnPointerDownEvent);
            m_Target.RegisterCallback<PointerUpEvent>(OnPointerUpEvent, TrickleDown.TrickleDown);
            m_Target.RegisterCallback<PointerLeaveEvent>(OnPointerLeaveEvent);
            m_Target.RegisterCallback<PointerMoveEvent>(OnPointerMoveEvent);
            m_Target.RegisterCallback<PointerCancelEvent>(OnPointerCancelEvent);
            m_Target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCapturedOut);
            m_Target.RegisterCallback<PointerOutEvent>(OnPointerOutEvent);
            m_Target.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        internal void UnregisterCallbacksFromTarget(bool unregisterPanelEvents = false)
        {
            m_IsRegistered = false;
            m_Target.UnregisterCallback<PointerDownEvent>(OnPointerDownEvent);
            m_Target.UnregisterCallback<PointerUpEvent>(OnPointerUpEvent, TrickleDown.TrickleDown);
            m_Target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeaveEvent);
            m_Target.UnregisterCallback<PointerMoveEvent>(OnPointerMoveEvent);
            m_Target.UnregisterCallback<PointerCancelEvent>(OnPointerCancelEvent);
            m_Target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCapturedOut);
            m_Target.UnregisterCallback<PointerOutEvent>(OnPointerOutEvent);
            m_Target.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            if (!unregisterPanelEvents) return;
            m_Target.UnregisterCallback(m_OnAttach);
            m_Target.UnregisterCallback(m_OnDetach);
        }

        protected abstract bool CanStartDrag(Vector3 pointerPosition);

        protected internal abstract StartDragArgs StartDrag(Vector3 pointerPosition);

        protected internal abstract void UpdateDrag(Vector3 pointerPosition);

        protected internal abstract void OnDrop(Vector3 pointerPosition);

        protected abstract void ClearDragAndDropUI(bool dragCancelled);

        void OnPointerDownEvent(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                m_DragState = DragState.None;
            }
            else if (CanStartDrag(evt.position))
            {
                m_DragState = DragState.CanStartDrag;
                m_Start = evt.position;
            }
        }

        void OnPointerOutEvent(PointerOutEvent evt)
        {
            if (m_DragState != DragState.CanStartDrag || (m_Start - evt.position).sqrMagnitude <= 0f) return;
            m_PendingPerformDrag = true;
            evt.StopPropagation();
        }

        internal void OnPointerUpEvent(PointerUpEvent evt)
        {
            if (m_DragState == DragState.Dragging)
            {
                var dropTarget = GetDropTarget(evt.position) ?? this;
                dropTarget.UpdateDrag(evt.position);
                dropTarget.OnDrop(evt.position);
                dropTarget.ClearDragAndDropUI(false);
                evt.StopPropagation();
            }
            m_Target.ReleasePointer(evt.pointerId);
            ClearDragAndDropUI(m_DragState == DragState.Dragging);
            dragAndDrop.DragCleanup();
            m_DragState = DragState.None;
            m_PendingPerformDrag = false;
        }

        void OnPointerLeaveEvent(PointerLeaveEvent evt) => ClearDragAndDropUI(false);

        void OnPointerCancelEvent(PointerCancelEvent evt) => CancelDragAndDrop(evt.pointerId);

        void OnPointerCapturedOut(PointerCaptureOutEvent evt) => CancelDragAndDrop();

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (m_Target.resolvedStyle.display == DisplayStyle.None) CancelDragAndDrop();
        }

        void CancelDragAndDrop(int releaseCapturePointerId = -1)
        {
            if (m_DragState == DragState.None && !m_PendingPerformDrag) return;
            ClearDragAndDropUI(true);
            if (releaseCapturePointerId != -1) m_Target.ReleasePointer(releaseCapturePointerId);
            ClearDragAndDropUI(m_DragState == DragState.Dragging);
            dragAndDrop.DragCleanup();
            m_DragState = DragState.None;
            m_PendingPerformDrag = false;
        }

        void OnPointerMoveEvent(PointerMoveEvent evt)
        {
            if (evt.isHandledByDraggable) return;
            if (m_DragState == DragState.Dragging)
            {
                (GetDropTarget(evt.position) ?? this).UpdateDrag(evt.position);
                m_PendingPerformDrag = false;
                return;
            }
            if (m_DragState != DragState.CanStartDrag) return;
            if ((m_Start - evt.position).sqrMagnitude < k_StartDragSqrDistance && !m_PendingPerformDrag) return;
            var args = StartDrag(m_Start);
            if (args.visualMode == DragVisualMode.Rejected)
            {
                m_DragState = DragState.None;
                return;
            }
            if (supportsDragEvents) dragAndDrop.StartDrag(args, evt.position);
            m_DragState = DragState.Dragging;
            m_Target.CapturePointer(evt.pointerId);
            evt.isHandledByDraggable = true;
            m_PendingPerformDrag = false;
            evt.StopPropagation();
        }

        /// <summary>This processor while the pointer is over its target, else the drag processor of
        /// the collection view under the pointer, if any.</summary>
        DragEventsProcessor GetDropTarget(Vector2 position)
        {
            if (m_Target.worldBound.Contains(position)) return this;
            if (!supportsDragEvents) return null;
            return m_Target.panel?.Pick(position)?.GetFirstOfType<BaseVerticalCollectionView>()?.dragger;
        }
    }

    internal static partial class ListViewDraggerExtension
    {
        public static ReusableCollectionItem GetRecycledItemFromId(this BaseVerticalCollectionView listView, int id)
        {
            foreach (var item in listView.activeItems)
                if (item.id == id) return item;
            return null;
        }

        public static ReusableCollectionItem GetRecycledItemFromIndex(this BaseVerticalCollectionView listView, int index)
        {
            foreach (var item in listView.activeItems)
                if (item.index == index) return item;
            return null;
        }
    }

    /// <summary>Drags the rows of a collection view: the drop position comes from the row under the
    /// pointer (between rows within 5 points of a row's top or bottom edge, over the row otherwise;
    /// after the last row when below the content), the hover bar marks it, and the view scrolls by 20
    /// points while the pointer is within 5 points of its top or bottom edge.</summary>
    internal partial class ListViewDragger : DragEventsProcessor
    {
        internal partial struct DragPosition : IEquatable<DragPosition>
        {
            public int insertAtIndex;
            public int parentId;
            public int childIndex;
            public ReusableCollectionItem recycledItem;
            public DragAndDropPosition dropPosition;

            public bool Equals(DragPosition other) =>
                insertAtIndex == other.insertAtIndex && parentId == other.parentId && childIndex == other.childIndex &&
                Equals(recycledItem, other.recycledItem) && dropPosition == other.dropPosition;

            public override bool Equals(object obj) => obj is DragPosition other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(insertAtIndex, parentId, childIndex, recycledItem, dropPosition);
        }

        // Pointer distance from the view's top or bottom edge that scrolls it while dragging.
        const int k_AutoScrollAreaSize = 5;
        // Pointer distance from a row's top or bottom edge that drops between rows.
        const int k_BetweenElementsAreaSize = 5;
        // Scroll step per drag update in the auto-scroll area.
        const int k_PanSpeed = 20;
        // Height of the hover bar kept inside the viewport.
        const int k_DragHoverBarHeight = 2;

        // Toggle and per-level indent width of a tree row that has not been laid out with a depth.
        const float k_DefaultTreeIndentWidth = 15f;

        DragPosition m_LastDragPosition;
        VisualElement m_DragHoverBar;
        // Tree views: a marker at the start of the hover bar, and one under the row of the item the
        // drop goes after when that is an ancestor's level.
        VisualElement m_DragHoverItemMarker;
        VisualElement m_DragHoverSiblingMarker;
        // Where the hover bar starts for a drop at a tree depth, and the bottom of the sibling
        // marker's row in the viewport; negative: none.
        float m_LeftIndentation = -1f;
        float m_SiblingBottom = -1f;
        bool m_Enabled = true;

        protected BaseVerticalCollectionView targetView => m_Target as BaseVerticalCollectionView;

        protected ScrollView targetScrollView => targetView.scrollView;

        public ICollectionDragAndDropController dragAndDropController { get; set; }

        internal bool enabled
        {
            get => m_Enabled;
            set
            {
                m_Enabled = value;
                if (!(targetView is BaseListView)) return;
                foreach (var item in targetView.activeItems)
                    (item as ReusableListViewItem)?.SetDragHandleEnabled(value);
            }
        }

        public ListViewDragger(BaseVerticalCollectionView listView) : base(listView) { }

        protected override bool CanStartDrag(Vector3 pointerPosition)
        {
            if (dragAndDropController == null) return false;
            if (!targetScrollView.contentContainer.worldBound.Contains(pointerPosition)) return false;
            var item = GetRecycledItem(pointerPosition);
            bool anySelected = HasAny(targetView.selectedIds);
            if (item != null && targetView.HasCanStartDrag())
                return targetView.RaiseCanStartDrag(item, anySelected ? targetView.selectedIds : new[] { item.id });
            if (anySelected) return dragAndDropController.CanStartDrag(targetView.selectedIds);
            return item != null && dragAndDropController.CanStartDrag(new[] { item.id });
        }

        static bool HasAny(IEnumerable<int> values)
        {
            foreach (var _ in values) return true;
            return false;
        }

        protected internal override StartDragArgs StartDrag(Vector3 pointerPosition)
        {
            var item = GetRecycledItem(pointerPosition);
            IEnumerable<int> ids;
            if (item != null)
            {
                if (targetView.selectionType == SelectionType.None)
                {
                    ids = new[] { item.index };
                }
                else
                {
                    if (!Contains(targetView.selectedIndices, item.index)) targetView.SetSelection(item.index);
                    ids = targetView.selectedIds;
                }
            }
            else
            {
                ids = HasAny(targetView.selectedIds) ? targetView.selectedIds : Array.Empty<int>();
            }
            var args = dragAndDropController.SetupDragAndDrop(ids);
            args = targetView.RaiseSetupDragAndDrop(item, dragAndDropController.GetSortedSelectedIds(), args);
            args.SetGenericData(DragAndDropData.dragSourceKey, targetView);
            return args;
        }

        static bool Contains(IEnumerable<int> values, int value)
        {
            foreach (int v in values)
                if (v == value) return true;
            return false;
        }

        protected internal override void UpdateDrag(Vector3 pointerPosition)
        {
            var position = default(DragPosition);
            var mode = GetVisualMode(pointerPosition, ref position);
            if (mode == DragVisualMode.Rejected)
            {
                ClearDragAndDropUI(false);
            }
            else
            {
                HandleDragAndScroll(pointerPosition);
                var item = GetRecycledItem(pointerPosition);
                if (item != null) dragAndDropController.HandleAutoExpand(item, pointerPosition);
                ApplyDragAndDropUI(position);
            }
            dragAndDrop.SetVisualMode(mode);
            dragAndDrop.UpdateDrag(pointerPosition);
        }

        DragVisualMode GetVisualMode(Vector3 pointerPosition, ref DragPosition position)
        {
            if (dragAndDropController == null) return DragVisualMode.Rejected;
            bool found = TryGetDragPosition(pointerPosition, ref position);
            var args = MakeDragAndDropArgs(position);
            var mode = targetView.RaiseHandleDragAndDrop(pointerPosition, args);
            if (mode != DragVisualMode.None) return mode;
            return found ? dragAndDropController.HandleDragAndDrop(args) : DragVisualMode.Rejected;
        }

        protected internal override void OnDrop(Vector3 pointerPosition)
        {
            var position = default(DragPosition);
            if (!TryGetDragPosition(pointerPosition, ref position)) return;
            var args = MakeDragAndDropArgs(position);
            switch (targetView.RaiseDrop(pointerPosition, args))
            {
                case DragVisualMode.Rejected:
                    dragAndDropController.DragCleanup();
                    break;
                case DragVisualMode.None:
                    if (IsDraggingDisabled()) break;
                    if (dragAndDropController.HandleDragAndDrop(args) != DragVisualMode.Rejected)
                    {
                        dragAndDropController.OnDrop(args);
                        dragAndDrop.AcceptDrag();
                    }
                    else
                    {
                        dragAndDropController.DragCleanup();
                    }
                    break;
                default:
                    dragAndDrop.AcceptDrag();
                    break;
            }
        }

        internal void HandleDragAndScroll(Vector2 pointerPosition)
        {
            var bounds = targetScrollView.worldBound;
            bool up = pointerPosition.y < bounds.yMin + k_AutoScrollAreaSize;
            bool down = pointerPosition.y > bounds.yMax - k_AutoScrollAreaSize;
            if (!up && !down) return;
            var offset = targetScrollView.scrollOffset + (up ? Vector2.down : Vector2.up) * k_PanSpeed;
            float max = Mathf.Max(0f, targetScrollView.contentContainer.worldBound.height - targetScrollView.contentViewport.worldBound.height);
            offset.y = Mathf.Clamp(offset.y, 0f, max);
            targetScrollView.scrollOffset = offset;
        }

        void ApplyDragAndDropUI(DragPosition position)
        {
            if (m_LastDragPosition.Equals(position) || IsDraggingDisabled()) return;
            if (m_DragHoverBar == null)
            {
                m_DragHoverBar = new VisualElement { pickingMode = PickingMode.Ignore };
                m_DragHoverBar.AddToClassList(BaseVerticalCollectionView.dragHoverBarUssClassName);
                m_DragHoverBar.style.width = targetView.localBound.width;
                m_DragHoverBar.style.visibility = Visibility.Hidden;
                targetView.RegisterCallback<GeometryChangedEvent>(_ => m_DragHoverBar.style.width = targetView.localBound.width);
                targetScrollView.contentViewport.Add(m_DragHoverBar);
            }
            if (m_DragHoverItemMarker == null && targetView is BaseTreeView)
            {
                m_DragHoverItemMarker = MakeHoverMarker();
                m_DragHoverBar.Add(m_DragHoverItemMarker);
                m_DragHoverSiblingMarker = MakeHoverMarker();
                targetScrollView.contentViewport.Add(m_DragHoverSiblingMarker);
            }
            ClearDragAndDropUI(false);
            m_LastDragPosition = position;
            switch (position.dropPosition)
            {
                case DragAndDropPosition.OverItem:
                    position.recycledItem.rootElement.AddToClassList(BaseVerticalCollectionView.itemDragHoverUssClassName);
                    break;
                case DragAndDropPosition.BetweenItems:
                    if (position.insertAtIndex == 0)
                    {
                        PlaceHoverBarAt(0f);
                        break;
                    }
                    PlaceHoverBarAtElement(targetView.GetRecycledItemFromIndex(position.insertAtIndex - 1) ?? targetView.GetRecycledItemFromIndex(position.insertAtIndex));
                    break;
                case DragAndDropPosition.OutsideItems:
                    var last = targetView.GetRecycledItemFromIndex(targetView.itemsSource.Count - 1);
                    if (last != null) PlaceHoverBarAtElement(last);
                    else PlaceHoverBarAt(0f);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(position.dropPosition), position.dropPosition, "Unsupported dropPosition value");
            }
        }

        static VisualElement MakeHoverMarker()
        {
            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.AddToClassList(BaseVerticalCollectionView.dragHoverMarkerUssClassName);
            marker.style.visibility = Visibility.Hidden;
            return marker;
        }

        protected virtual bool TryGetDragPosition(Vector2 pointerPosition, ref DragPosition position)
        {
            var item = GetRecycledItem(pointerPosition);
            if (item == null)
            {
                if (!targetView.worldBound.Contains(pointerPosition)) return false;
                position.dropPosition = DragAndDropPosition.OutsideItems;
                position.insertAtIndex = pointerPosition.y >= targetScrollView.contentContainer.worldBound.yMax ? targetView.itemsSource.Count : 0;
                SetTreePosition(pointerPosition, ref position);
                return true;
            }
            var bounds = item.rootElement.worldBound;
            if (bounds.yMax - pointerPosition.y < k_BetweenElementsAreaSize)
            {
                position.insertAtIndex = item.index + 1;
                position.dropPosition = DragAndDropPosition.BetweenItems;
            }
            else if (pointerPosition.y - bounds.yMin > k_BetweenElementsAreaSize)
            {
                // Over a row: bring it fully into view first, and look again if that scrolled.
                var before = targetScrollView.scrollOffset;
                targetView.ScrollToItem(item.index);
                var after = targetScrollView.scrollOffset;
                if (!Mathf.Approximately(before.x, after.x) || !Mathf.Approximately(before.y, after.y))
                    return TryGetDragPosition(pointerPosition, ref position);
                position.recycledItem = item;
                position.insertAtIndex = item.index;
                position.dropPosition = DragAndDropPosition.OverItem;
            }
            else
            {
                position.insertAtIndex = item.index;
                position.dropPosition = DragAndDropPosition.BetweenItems;
            }
            SetTreePosition(pointerPosition, ref position);
            return true;
        }

        /// <summary>The parent and child index of a drop in a tree view (-1 and -1 elsewhere): over
        /// a row, that item's last child; before the first row, first at the root; otherwise by the
        /// rows around the insert index.</summary>
        void SetTreePosition(Vector2 pointerPosition, ref DragPosition position)
        {
            position.parentId = BaseTreeView.invalidId;
            position.childIndex = -1;
            m_LeftIndentation = -1f;
            m_SiblingBottom = -1f;
            if (!(targetView is BaseTreeView tree) || position.insertAtIndex < 0) return;
            if (position.dropPosition == DragAndDropPosition.OverItem) position.parentId = tree.viewController.GetIdForIndex(position.insertAtIndex);
            else if (position.insertAtIndex <= 0) position.childIndex = 0;
            else SetTreeSiblingPosition(tree, pointerPosition, ref position);
        }

        /// <summary>A drop between two rows of a tree. The rows around it are the nearest ones that
        /// are not dragged. The drop can go at any depth from the next row's (0 after the last row)
        /// to the previous row's (one more when that row is expanded with children). With one depth
        /// possible it goes before the next row, or first into the open previous row. Otherwise the
        /// pointer's x picks the depth, in indent steps after the toggle: at the deepest, right after
        /// the previous row (or first into it), else right after the previous row's ancestor of that
        /// depth, whose row gets the sibling marker.</summary>
        void SetTreeSiblingPosition(BaseTreeView tree, Vector2 pointerPosition, ref DragPosition position)
        {
            var controller = tree.viewController;
            int insertId = controller.GetIdForIndex(position.insertAtIndex);
            int previousId = BaseTreeView.invalidId;
            int nextId = BaseTreeView.invalidId;
            for (int i = position.insertAtIndex - 1; i >= 0 && previousId == BaseTreeView.invalidId; i--)
                if (!IsDragged(controller.GetIdForIndex(i))) previousId = controller.GetIdForIndex(i);
            for (int i = position.insertAtIndex; i < targetView.itemsSource.Count && nextId == BaseTreeView.invalidId; i++)
                if (!IsDragged(controller.GetIdForIndex(i))) nextId = controller.GetIdForIndex(i);
            if (previousId == BaseTreeView.invalidId) return;

            bool previousOpen = controller.HasChildren(previousId) && tree.IsExpanded(previousId);
            int previousDepth = controller.GetIndentationDepth(previousId);
            int minDepth = nextId != BaseTreeView.invalidId ? controller.GetIndentationDepth(nextId) : 0;
            int maxDepth = previousDepth + (previousOpen ? 1 : 0);

            // The indent step is read from a laid-out row with a depth: the previous one, else the
            // insert row.
            float toggleWidth = k_DefaultTreeIndentWidth;
            float indentWidth = k_DefaultTreeIndentWidth;
            int measuredDepth = previousDepth;
            var measuredRow = tree.GetRootElementForId(previousId);
            if (measuredDepth <= 0 || measuredRow == null)
            {
                measuredDepth = controller.GetIndentationDepth(insertId);
                measuredRow = measuredDepth > 0 ? tree.GetRootElementForId(insertId) : null;
            }
            var indent = measuredRow?.Q(BaseTreeView.itemIndentUssClassName);
            var toggle = measuredRow?.Q(BaseTreeView.itemToggleUssClassName);
            if (indent != null && toggle != null)
            {
                toggleWidth = toggle.layout.width;
                indentWidth = indent.layout.width / measuredDepth;
            }

            if (maxDepth <= minDepth)
            {
                m_LeftIndentation = toggleWidth + indentWidth * minDepth;
                if (previousOpen)
                {
                    position.parentId = previousId;
                    position.childIndex = 0;
                    return;
                }
                position.parentId = controller.GetParentId(previousId);
                position.childIndex = controller.GetParentId(nextId) == insertId
                    ? controller.GetChildIndexForId(previousId) + 1
                    : controller.GetChildIndexForId(nextId);
                return;
            }

            int pointerDepth = Mathf.FloorToInt((tree.scrollView.contentContainer.WorldToLocal(pointerPosition).x - toggleWidth) / indentWidth);
            if (pointerDepth >= maxDepth)
            {
                m_LeftIndentation = toggleWidth + indentWidth * maxDepth;
                position.parentId = previousOpen ? previousId : controller.GetParentId(previousId);
                position.childIndex = previousOpen ? 0 : controller.GetChildIndexForId(previousId) + 1;
                return;
            }

            int siblingId = previousId;
            int depth = previousDepth;
            while (depth > minDepth && depth != pointerDepth)
            {
                siblingId = controller.GetParentId(siblingId);
                depth--;
            }
            var siblingRow = siblingId != insertId ? tree.GetRootElementForId(siblingId) : null;
            if (siblingRow != null)
            {
                var viewport = targetScrollView.contentViewport;
                float bottom = viewport.WorldToLocal(siblingRow.worldBound).yMax;
                if (viewport.localBound.yMin < bottom && bottom < viewport.localBound.yMax) m_SiblingBottom = bottom;
            }
            position.parentId = controller.GetParentId(siblingId);
            position.childIndex = controller.GetChildIndexForId(siblingId) + 1;
            m_LeftIndentation = toggleWidth + indentWidth * depth;
        }

        bool IsDragged(int id)
        {
            foreach (int dragged in dragAndDropController.GetSortedSelectedIds())
                if (dragged == id) return true;
            return false;
        }

        protected DragAndDropArgs MakeDragAndDropArgs(DragPosition position)
        {
            object target = null;
            if (position.recycledItem != null) target = targetView.viewController.GetItemForIndex(position.recycledItem.index);
            return new DragAndDropArgs
            {
                target = target,
                insertAtIndex = position.insertAtIndex,
                parentId = position.parentId,
                childIndex = position.childIndex,
                dragAndDropPosition = position.dropPosition,
                dragAndDropData = DragAndDropUtility.GetDragAndDrop(m_Target.panel).data,
            };
        }

        void PlaceHoverBarAtElement(ReusableCollectionItem item)
        {
            var viewport = targetScrollView.contentViewport;
            PlaceHoverBarAt(Mathf.Min(viewport.WorldToLocal(item.rootElement.worldBound).yMax, viewport.localBound.yMax - k_DragHoverBarHeight), m_LeftIndentation, m_SiblingBottom);
        }

        // Without an indentation the bar spans the view (also before a tree's first row).
        void PlaceHoverBarAt(float top, float indentation = -1f, float siblingBottom = -1f)
        {
            m_DragHoverBar.style.top = top;
            m_DragHoverBar.style.visibility = Visibility.Visible;
            if (m_DragHoverItemMarker != null) m_DragHoverItemMarker.style.visibility = Visibility.Visible;
            if (indentation < 0f)
            {
                m_DragHoverBar.style.marginLeft = 0f;
                m_DragHoverBar.style.width = targetView.localBound.width;
                return;
            }
            m_DragHoverBar.style.marginLeft = indentation;
            m_DragHoverBar.style.width = targetView.localBound.width - indentation;
            if (siblingBottom <= 0f || m_DragHoverSiblingMarker == null) return;
            m_DragHoverSiblingMarker.style.top = siblingBottom;
            m_DragHoverSiblingMarker.style.visibility = Visibility.Visible;
            m_DragHoverSiblingMarker.style.marginLeft = indentation;
        }

        protected override void ClearDragAndDropUI(bool dragCancelled)
        {
            if (dragCancelled) dragAndDropController.DragCleanup();
            m_LastDragPosition = default;
            foreach (var item in targetView.activeItems)
                item.rootElement.RemoveFromClassList(BaseVerticalCollectionView.itemDragHoverUssClassName);
            if (m_DragHoverBar != null) m_DragHoverBar.style.visibility = Visibility.Hidden;
            if (m_DragHoverItemMarker != null) m_DragHoverItemMarker.style.visibility = Visibility.Hidden;
            if (m_DragHoverSiblingMarker != null) m_DragHoverSiblingMarker.style.visibility = Visibility.Hidden;
        }

        protected ReusableCollectionItem GetRecycledItem(Vector3 pointerPosition)
        {
            foreach (var item in targetView.activeItems)
                if (item.rootElement.worldBound.Contains(pointerPosition)) return item;
            return null;
        }

        bool IsDraggingDisabled() => targetView == dragAndDrop.data.source && !enabled;
    }
}

namespace UnityEngine.UIElements
{
    using UnityEngine.UIElements.Experimental;

    /// <summary>Animated reordering: the dragged row is lifted out of the list (absolutely placed
    /// under the pointer), the row it would drop before opens a gap of the dragged row's height
    /// (its top padding animated over 500 ms), and dropping moves the item to the gap. Matches the
    /// Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (animated section).</summary>
    internal partial class ListViewDraggerAnimated : ListViewDragger
    {
        // Duration of a row's gap opening or closing.
        const int k_GapAnimationMs = 500;

        int m_DragStartIndex;
        int m_CurrentIndex;
        float m_SelectionHeight;
        float m_LocalOffsetOnStart;
        Vector3 m_CurrentPointerPosition;
        ReusableCollectionItem m_Item;
        ReusableCollectionItem m_OffsetItem;

        public bool isDragging { get; private set; }

        public ReusableCollectionItem draggedItem => m_Item;

        [Unbound.EditorFaithful("Unity's: the animated drag follows pointer events only")]
        protected override bool supportsDragEvents => false;

        public ListViewDraggerAnimated(BaseVerticalCollectionView listView) : base(listView) { }

        protected internal override StartDragArgs StartDrag(Vector3 pointerPosition)
        {
            if (!enabled) return base.StartDrag(pointerPosition);
            targetView.ClearSelection();
            var item = GetRecycledItem(pointerPosition);
            if (item == null) return new StartDragArgs(string.Empty, DragVisualMode.Rejected);
            if (targetView.selectionType != SelectionType.None) targetView.SetSelection(item.index);
            isDragging = true;
            m_Item = item;
            targetView.virtualizationController.StartDragItem(m_Item);
            var root = m_Item.rootElement;
            float y = root.layout.y;
            m_SelectionHeight = root.layout.height;
            root.style.position = Position.Absolute;
            root.style.height = root.layout.height;
            root.style.width = root.layout.width;
            root.style.top = y;
            m_DragStartIndex = m_Item.index;
            m_CurrentIndex = m_DragStartIndex;
            m_CurrentPointerPosition = pointerPosition;
            m_LocalOffsetOnStart = targetScrollView.contentContainer.WorldToLocal(pointerPosition).y - y;
            var next = targetView.GetRecycledItemFromIndex(m_CurrentIndex + 1);
            if (next != null)
            {
                m_OffsetItem = next;
                Animate(m_OffsetItem, m_SelectionHeight);
                m_OffsetItem.rootElement.style.paddingTop = m_SelectionHeight;
                if (targetView.virtualizationMethod == CollectionVirtualizationMethod.FixedHeight)
                    m_OffsetItem.rootElement.style.height = targetView.fixedItemHeight + m_SelectionHeight;
            }
            return dragAndDropController.SetupDragAndDrop(new[] { m_Item.index }, true);
        }

        /// <summary>The dragged row follows the pointer within the content; the first row whose top
        /// half it passes opens the gap (none: the end of the list).</summary>
        protected internal override void UpdateDrag(Vector3 pointerPosition)
        {
            if (!enabled)
            {
                base.UpdateDrag(pointerPosition);
                return;
            }
            if (m_Item == null) return;
            HandleDragAndScroll(pointerPosition);
            m_CurrentPointerPosition = pointerPosition;
            var local = targetScrollView.contentContainer.WorldToLocal(m_CurrentPointerPosition);
            var layout = m_Item.rootElement.layout;
            float contentHeight = targetScrollView.contentContainer.layout.height;
            layout.y = Mathf.Clamp(local.y - m_LocalOffsetOnStart, 0f, contentHeight - m_SelectionHeight);
            float top = targetScrollView.contentContainer.resolvedStyle.paddingTop;
            m_CurrentIndex = -1;
            foreach (var active in targetView.activeItems)
            {
                if (active.index < 0 || (active.rootElement.style.display == DisplayStyle.None && !active.isDragGhost)) continue;
                if (active.index == m_Item.index && active.index < targetView.itemsSource.Count - 1)
                {
                    float nextHeight = targetView.virtualizationController.GetExpectedItemHeight(active.index + 1);
                    if (!Mathf.Approximately(layout.y + nextHeight, contentHeight) && layout.y <= top + nextHeight * 0.5f)
                        m_CurrentIndex = active.index;
                    continue;
                }
                float height = targetView.virtualizationController.GetExpectedItemHeight(active.index);
                if (layout.y <= top + height * 0.5f)
                {
                    if (m_CurrentIndex == -1) m_CurrentIndex = active.index;
                    if (m_OffsetItem != active)
                    {
                        Animate(m_OffsetItem, 0f);
                        Animate(active, m_SelectionHeight);
                        m_OffsetItem = active;
                    }
                    break;
                }
                top += height;
            }
            if (m_CurrentIndex == -1)
            {
                m_CurrentIndex = targetView.itemsSource.Count;
                Animate(m_OffsetItem, 0f);
                m_OffsetItem = null;
            }
            m_Item.rootElement.layout = layout;
            m_Item.rootElement.BringToFront();
        }

        /// <summary>Animates a row's top padding (and, for fixed-height rows, its height) to open or
        /// close a gap, unless it is already going there.</summary>
        void Animate(ReusableCollectionItem element, float paddingTop)
        {
            if (element == null) return;
            var animator = element.animator;
            if (animator != null)
            {
                if (animator.isRunning && animator.to.paddingTop == paddingTop) return;
                if (!animator.isRunning && element.rootElement.style.paddingTop == paddingTop) return;
            }
            animator?.Stop();
            animator?.Recycle();
            var to = new StyleValues { paddingTop = paddingTop };
            if (targetView.virtualizationMethod == CollectionVirtualizationMethod.FixedHeight)
                to.height = targetView.ResolveItemHeight() + paddingTop;
            element.animator = element.rootElement.experimental.animation.Start(to, k_GapAnimationMs);
            element.animator.KeepAlive();
        }

        protected internal override void OnDrop(Vector3 pointerPosition)
        {
            if (!enabled)
            {
                base.OnDrop(pointerPosition);
                return;
            }
            if (m_Item == null) return;
            isDragging = false;
            m_Item.rootElement.ClearManualLayout();
            targetView.virtualizationController.EndDrag(m_CurrentIndex);
            if (m_OffsetItem != null)
            {
                m_OffsetItem.animator?.Stop();
                m_OffsetItem.animator?.Recycle();
                m_OffsetItem.animator = null;
                m_OffsetItem.rootElement.style.paddingTop = 0f;
                if (targetView.virtualizationMethod == CollectionVirtualizationMethod.FixedHeight)
                    m_OffsetItem.rootElement.style.height = targetView.ResolveItemHeight();
            }
            var position = new DragPosition { recycledItem = m_Item, insertAtIndex = m_CurrentIndex, dropPosition = DragAndDropPosition.BetweenItems };
            var args = MakeDragAndDropArgs(position);
            dragAndDropController.OnDrop(args);
            dragAndDrop.AcceptDrag();
            m_Item = null;
            m_OffsetItem = null;
        }

        [Unbound.EditorFaithful("Unity's: the lifted row is the drag feedback")]
        protected override void ClearDragAndDropUI(bool dragCancelled) { }

        protected override bool TryGetDragPosition(Vector2 pointerPosition, ref DragPosition dragPosition)
        {
            dragPosition.recycledItem = m_Item;
            dragPosition.insertAtIndex = m_CurrentIndex;
            dragPosition.dropPosition = DragAndDropPosition.BetweenItems;
            return true;
        }
    }
}
