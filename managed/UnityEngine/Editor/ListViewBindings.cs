using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Editor UI Toolkit ListView SerializedProperty binding (SerializedObjectList,
// EditorListViewController, BaseListViewSerializedObjectBinding, ListViewSerializedObjectBinding,
// SerializedObjectListReorderableDragAndDropController) and EditorGUIUtility.MoveArrayExpandedState.
// Documented in Unity 6000.0 Scripting API ("ListView", "PropertyField", "BindingExtensions");
// matches the Unity 6000.0.71f1 Editor, tests/projects/arraylistbinding.

namespace UnityEditor
{
    public sealed partial class EditorGUIUtility : GUIUtility
    {
        /// <summary>After an element moved from <paramref name="oldActiveElement"/> to
        /// <paramref name="newActiveElement"/>, moves the foldout states that stay with their
        /// paths along with it: the element's own and those of its nested foldouts, the elements
        /// between shifting by one.</summary>
        internal static void MoveArrayExpandedState(SerializedProperty elements, int oldActiveElement, int newActiveElement)
        {
            if (elements == null || oldActiveElement == newActiveElement) return;
            int count = elements.arraySize;
            if (oldActiveElement < 0 || oldActiveElement >= count || newActiveElement < 0 || newActiveElement >= count) return;
            var moved = elements.GetArrayElementAtIndex(oldActiveElement);
            var movedStates = new List<bool> { moved.isExpanded };
            int step = oldActiveElement < newActiveElement ? 1 : -1;
            var current = moved;
            for (int i = oldActiveElement + step; i != newActiveElement + step; i += step)
            {
                var next = elements.GetArrayElementAtIndex(i);
                foreach (var (to, from) in NestedFoldouts(current, next))
                {
                    movedStates.Add(to.isExpanded);
                    to.isExpanded = from.isExpanded;
                }
                current.isExpanded = next.isExpanded;
                current = next;
            }
            current.isExpanded = movedStates[0];
            int index = 1;
            foreach (var (to, _) in NestedFoldouts(current, elements.GetArrayElementAtIndex(oldActiveElement)))
            {
                if (index >= movedStates.Count) break;
                to.isExpanded = movedStates[index++];
            }
        }

        // The visible properties with visible children of two elements, side by side, as long as
        // both have them.
        static IEnumerable<(SerializedProperty, SerializedProperty)> NestedFoldouts(SerializedProperty a, SerializedProperty b)
        {
            a = a.Copy();
            b = b.Copy();
            int depth = Math.Min(a.depth, b.depth);
            while (a.NextVisible(true) && a.depth > depth && b.NextVisible(true) && b.depth > depth)
                if (a.hasVisibleChildren && b.hasVisibleChildren) yield return (a, b);
        }
    }
}

namespace UnityEditor.UIElements.Bindings
{
    internal partial class SerializedObjectList : ISerializedObjectList, IList, ICollection, IEnumerable
    {
        List<SerializedProperty> m_Properties;

        public SerializedProperty ArrayProperty { get; private set; }
        public SerializedProperty ArraySize { get; private set; }

        public object this[int index]
        {
            get => m_Properties[index];
            set => throw new NotImplementedException();
        }

        [Unbound.EditorFaithful("SerializedObjectList is a fixed read-only view over the bound array property (Unity 6000.0.71f1, tests/projects/arraylistbinding)")]
        public bool IsReadOnly => true;
        [Unbound.EditorFaithful("SerializedObjectList is a fixed read-only view over the bound array property (Unity 6000.0.71f1, tests/projects/arraylistbinding)")]
        public bool IsFixedSize => true;

        public int Count
        {
            get
            {
                if (ArrayProperty.serializedObject.isEditingMultipleObjects)
                {
                    if (IsOverMaxMultiEditLimit) return 0;
                    return ArrayProperty.minArraySize;
                }
                return m_Properties != null ? m_Properties.Count : 0;
            }
        }

        bool ICollection.IsSynchronized => ((ICollection)m_Properties).IsSynchronized;
        object ICollection.SyncRoot => ((ICollection)m_Properties).SyncRoot;

        internal bool IsOverMaxMultiEditLimit =>
            ArrayProperty.serializedObject.isEditingMultipleObjects
            && ArrayProperty.minArraySize > ArrayProperty.serializedObject.maxArraySizeForMultiEditing;

        public int minArraySize => ArrayProperty.minArraySize;

        public int arraySize
        {
            get => ArrayProperty.arraySize;
            set => ArrayProperty.arraySize = value;
        }

        public SerializedObjectList(SerializedProperty parentProperty)
        {
            ArrayProperty = parentProperty.Copy();
            RefreshProperties();
        }

        public void RefreshProperties()
        {
            var iter = ArrayProperty.Copy();
            var elements = ArrayProperty.Copy();
            var end = iter.GetEndProperty();
            iter = ArrayProperty.Copy();
            iter.Next(true);
            m_Properties ??= new List<SerializedProperty>();
            m_Properties.Clear();
            ArraySize = null;
            while (!SerializedProperty.EqualContents(iter, end))
            {
                if (iter.propertyType == SerializedPropertyType.ArraySize)
                {
                    ArraySize = iter.Copy();
                    foreach (SerializedProperty item in elements)
                        m_Properties.Add(item.Copy());
                    break;
                }
                if (!iter.Next(true)) break;
            }
            if (ArraySize == null)
                throw new ArgumentException("Can't find array size property!");
        }

        public int Add(object value) => throw new NotImplementedException();
        public void Clear() => throw new NotImplementedException();
        public bool Contains(object value) => IndexOf(value) >= 0;
        public void CopyTo(Array array, int index) => throw new NotImplementedException();
        public IEnumerator GetEnumerator() => m_Properties.GetEnumerator();

        public int IndexOf(object value)
        {
            if (value is SerializedProperty prop && m_Properties != null)
                return m_Properties.IndexOf(prop);
            return -1;
        }

        public void Move(int srcIndex, int destIndex)
        {
            if (srcIndex == destIndex) return;
            ArrayProperty.MoveArrayElement(srcIndex, destIndex);
            EditorGUIUtility.MoveArrayExpandedState(ArrayProperty, srcIndex, destIndex);
            RefreshProperties();
        }

        public void ApplyChanges()
        {
            ArrayProperty.serializedObject.ApplyModifiedProperties();
            RefreshProperties();
        }

        public void Insert(int index, object value) => throw new NotImplementedException();
        public void Remove(object value) => RemoveAt(IndexOf(value));
        public void RemoveAt(int index) => RemoveAt(index, Count);

        public void RemoveAt(int index, int listCount)
        {
            if (index < 0 || index >= listCount) return;
            int newCount = listCount - 1;
            ArrayProperty.DeleteArrayElementAtIndex(index);
            if (index >= newCount - 1) return;
            var prev = ArrayProperty.GetArrayElementAtIndex(index);
            for (int i = index + 1; i < newCount; i++)
            {
                var next = ArrayProperty.GetArrayElementAtIndex(i);
                if (next != null && prev != null)
                {
                    prev.isExpanded = next.isExpanded;
                    prev = next;
                }
            }
        }
    }

    internal partial class EditorListViewController : ListViewController
    {
        readonly SerializedObjectListControllerImpl m_SerializedObjectListControllerImpl;

        public EditorListViewController()
        {
            m_SerializedObjectListControllerImpl = new SerializedObjectListControllerImpl(this, () => itemsSource as SerializedObjectList);
        }

        public override int GetItemsCount() => m_SerializedObjectListControllerImpl.GetItemsCount();
        internal override int GetItemsMinCount() => m_SerializedObjectListControllerImpl.GetItemsMinCount();
        public override void AddItems(int itemCount) => m_SerializedObjectListControllerImpl.AddItems(itemCount);
        internal override void RemoveItems(int itemCount) => m_SerializedObjectListControllerImpl.RemoveItems(itemCount);
        public override void RemoveItems(List<int> indices) => m_SerializedObjectListControllerImpl.RemoveItems(indices);
        public override void RemoveItem(int index) => m_SerializedObjectListControllerImpl.RemoveItem(index);
        public override void ClearItems() => m_SerializedObjectListControllerImpl.ClearItems();
        public override void Move(int srcIndex, int destIndex) => m_SerializedObjectListControllerImpl.Move(srcIndex, destIndex);
    }

    internal partial class SerializedObjectListReorderableDragAndDropController : ListViewReorderableDragAndDropController
    {
        public SerializedObjectListReorderableDragAndDropController(BaseListView baseListView) : base(baseListView) { }

        public override void OnDrop(IListDragAndDropArgs args)
        {
            if (args.dragAndDropPosition != DragAndDropPosition.BetweenItems && args.dragAndDropPosition != DragAndDropPosition.OutsideItems)
                throw new ArgumentException($"{args.dragAndDropPosition} is not supported by {nameof(SerializedObjectListReorderableDragAndDropController)}.");
            base.OnDrop(args);
        }
    }

    internal abstract partial class BaseListViewSerializedObjectBinding : SerializedObjectBindingBase
    {
        protected SerializedObjectList m_DataList;
        protected EventCallback<DragUpdatedEvent> m_DragUpdatedCallback;
        protected EventCallback<DragPerformEvent> m_DragPerformCallback;
        protected EventCallback<SerializedObjectBindEvent> m_SerializedObjectBindEventCallback;
        protected Func<VisualElement> m_DefaultMakeItem;
        protected Action<VisualElement, int> m_DefaultBindItem;
        protected Action<VisualElement, int> m_DefaultUnbindItem;
        int m_ListViewArraySize;

        BaseListView baseListView
        {
            get => boundElement as BaseListView;
            set => boundElement = value;
        }

        protected override string bindingId => BindingExtensions.s_SerializedBindingId;

        protected BaseListViewSerializedObjectBinding()
        {
            m_DefaultMakeItem = MakeItem;
            m_DefaultUnbindItem = UnbindListViewItem;
            m_DragUpdatedCallback = OnDragObjects;
            m_DragPerformCallback = OnDragObjects;
            m_SerializedObjectBindEventCallback = SerializedObjectBindEventCallback;
        }

        static void SerializedObjectBindEventCallback(SerializedObjectBindEvent evt) => evt.StopPropagation();

        // Objects dragged onto the header of a list of object references are added as elements
        // after the Editor checks them against the elements' object type; that check is not
        // implemented, so such a drag is reported instead of being ignored. Other lists take no
        // objects. The drag is accepted either way.
        void OnDragObjects(EventBase evt)
        {
            if (DragAndDrop.objectReferences.Length > 0
                && m_DataList?.ArrayProperty.arrayElementType.StartsWith("PPtr<", StringComparison.Ordinal) == true)
                Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.BaseListViewSerializedObjectBinding (objects dragged onto a bound ListView)");
            DragAndDrop.AcceptDrag();
        }

        public override void OnRelease()
        {
            if (isReleased) return;
            isReleased = true;
            ResetContext();
            m_DataList = null;
            m_ListViewArraySize = -1;
            ClearView();
            ResetCachedValues();
            PoolRelease();
        }

        public override BindingResult OnUpdate(in BindingContext context)
        {
            if (isReleased) return new BindingResult(BindingStatus.Pending);
            try
            {
                ResetUpdate();
                if (!IsSynced()) return new BindingResult(BindingStatus.Pending);
                if (bindingContext == null || !bindingContext.IsValid() || m_DataList == null || !m_DataList.ArrayProperty.isValid)
                {
                    Unbind();
                    return new BindingResult(BindingStatus.Failure, "Failed to update ListView binding.");
                }
                // A mixed or hidden size is refreshed only by OnPropertyValueChanged.
                var sizeField = baseListView.arraySizeField;
                if (sizeField == null || sizeField.showMixedValue) return default;
                if (m_DataList.arraySize != m_ListViewArraySize) UpdateArraySize();
                return default;
            }
            catch (Exception) when (isContextDisposed)
            {
            }
            Unbind();
            return new BindingResult(BindingStatus.Failure, "Failed to update ListView binding.");
        }

        public override void OnPropertyValueChanged(SerializedProperty currentPropertyIterator)
        {
            if (isReleased) return;
            try
            {
                UpdateArraySize();
            }
            catch (Exception) when (isContextDisposed)
            {
            }
        }

        protected override void ResetCachedValues()
        {
            m_ListViewArraySize = -1;
            UpdateFieldIsAttached();
        }

        void UpdateArraySize()
        {
            if (m_DataList == null || baseListView == null) return;
            m_DataList.RefreshProperties();
            m_ListViewArraySize = m_DataList.arraySize;
            bool isOverLimit = m_DataList.IsOverMaxMultiEditLimit;
            baseListView.footer?.SetEnabled(!isOverLimit);
            baseListView.SetOverMaxMultiEditLimit(isOverLimit, m_DataList.ArrayProperty.serializedObject.maxArraySizeForMultiEditing);
            baseListView.RefreshItems();
            if (baseListView.arraySizeField != null && m_DataList.ArraySize != null)
                baseListView.arraySizeField.showMixedValue = m_DataList.ArraySize.hasMultipleDifferentValues;
        }

        public static bool CreateBind(BaseListView baseListView, SerializedObjectBindingContext context, SerializedProperty prop)
        {
            BaseListViewSerializedObjectBinding binding;
            if (baseListView is ListView)
            {
                binding = ListViewSerializedObjectBinding.GetFromPool();
            }
            else if (baseListView is MultiColumnListView)
            {
                Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.MultiColumnListViewSerializedObjectBinding");
                return false;
            }
            else
            {
                return false;
            }
            if (binding == null) return false;
            binding.isReleased = false;
            baseListView.SetBinding(BindingExtensions.s_SerializedBindingId, binding);
            binding.SetBinding(baseListView, context, prop);
            return true;
        }

        void SetBinding(BaseListView targetList, SerializedObjectBindingContext context, SerializedProperty prop)
        {
            m_DataList = new SerializedObjectList(prop);
            m_ListViewArraySize = m_DataList.arraySize;
            SetView(targetList);
            SetContext(context, m_DataList.ArraySize);
            bool isOverLimit = m_DataList.IsOverMaxMultiEditLimit;
            targetList.SetOverMaxMultiEditLimit(isOverLimit, m_DataList.ArrayProperty.serializedObject.maxArraySizeForMultiEditing);
            targetList.RefreshItems();
            if (targetList.arraySizeField != null && m_DataList.ArraySize != null)
                targetList.arraySizeField.showMixedValue = m_DataList.ArraySize.hasMultipleDifferentValues;
        }

        void SetView(BaseListView view)
        {
            if (baseListView != null)
            {
                Debug.LogError("[UI Toolkit] Internal ListViewBindings error.");
                return;
            }
            baseListView = view;
            baseListView.SetProperty(BaseListView.internalBindingKey, this);
            object boundPf = baseListView.GetProperty(PropertyField.listViewBoundFieldProperty);
            SetDefaultCallbacks();
            baseListView.scrollView.contentContainer.RegisterCallback(m_SerializedObjectBindEventCallback);
            bool reorderable = baseListView.reorderable;
            if (boundPf != null)
            {
                reorderable = IsArrayReorderable(m_DataList.ArrayProperty);
                baseListView.reorderMode = reorderable ? ListViewReorderMode.Animated : ListViewReorderMode.Simple;
            }
            SetEditorViewController();
            baseListView.SetDragAndDropController(new SerializedObjectListReorderableDragAndDropController(baseListView)
            {
                enableReordering = reorderable
            });
            baseListView.itemsSource = m_DataList;
            baseListView.SetupArraySizeField();
            var header = baseListView.headerFoldout?.toggle?.visualInput;
            if (header != null)
            {
                header.RegisterCallback(m_DragUpdatedCallback);
                header.RegisterCallback(m_DragPerformCallback);
            }
        }

        // A PropertyField's list can be reordered unless its field is [NonReorderable].
        static bool IsArrayReorderable(SerializedProperty property)
        {
            if (property?.serializedObject?.targetObject == null) return false;
            var field = property.UnboundDeclaringField;
            return field == null || !field.IsDefined(typeof(NonReorderableAttribute), false);
        }

        static IBindable FindFirstBindable(VisualElement root)
        {
            if (root is IBindable direct) return direct;
            int count = root.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var found = FindFirstBindable(root.hierarchy[i]);
                if (found != null) return found;
            }
            return null;
        }

        protected void BindListViewItem(VisualElement ve, int index)
        {
            if (m_DataList == null || index < 0 || index >= m_DataList.Count) return;
            if (m_DataList[index] is SerializedProperty prop)
                BindListViewItem(ve, prop.propertyPath);
        }

        protected void BindListViewItem(VisualElement ve, string propertyPath)
        {
            if (bindingContext == null || (m_ListViewArraySize != -1 && m_DataList != null && m_DataList.arraySize != m_ListViewArraySize))
                return;
            var bindable = FindFirstBindable(ve);
            if (bindable == null)
                throw new InvalidOperationException("Can't find BindableElement: please provide BindableVisualElements or provide your own Listview.bindItem callback");
            bindable.bindingPath = propertyPath;
            bindingContext.ContinueBinding(ve, null);
        }

        void UnbindListViewItem(VisualElement ve, int index)
        {
            var bindable = FindFirstBindable(ve);
            if (bindable == null)
                throw new InvalidOperationException("Can't find BindableElement: please provide BindableVisualElements or provide your own Listview.unbindItem callback");
            ve.Unbind();
            bindable.bindingPath = null;
        }

        void ClearView()
        {
            if (baseListView == null) return;
            if (baseListView.arraySizeField != null) baseListView.arraySizeField.showMixedValue = false;
            baseListView.SetProperty(BaseListView.internalBindingKey, null);
            baseListView.itemsSource = null;
            baseListView.SetupArraySizeField();
            baseListView.Rebuild();
            ResetCallbacks();
            baseListView.scrollView.contentContainer.UnregisterCallback(m_SerializedObjectBindEventCallback);
            baseListView.SetViewController(null);
            var header = baseListView.headerFoldout?.toggle?.visualInput;
            if (header != null)
            {
                header.UnregisterCallback(m_DragUpdatedCallback);
                header.UnregisterCallback(m_DragPerformCallback);
            }
            baseListView = null;
        }

        protected abstract void SetEditorViewController();
        protected abstract VisualElement MakeItem();
        protected abstract void SetDefaultCallbacks();
        protected abstract bool HasDefaultBindItem();
        protected abstract void ResetCallbacks();
        protected abstract void PoolRelease();
    }

    internal partial class ListViewSerializedObjectBinding : BaseListViewSerializedObjectBinding
    {
        ListView listView => boundElement as ListView;

        public ListViewSerializedObjectBinding()
        {
            m_DefaultBindItem = BindListViewItem;
        }

        public static BaseListViewSerializedObjectBinding GetFromPool() => new ListViewSerializedObjectBinding();

        protected override void SetEditorViewController() => listView.SetViewController(new EditorListViewController());

        protected override VisualElement MakeItem() => new PropertyField();

        protected override void SetDefaultCallbacks()
        {
            if (listView.makeItem == null) listView.SetMakeItemWithoutNotify(m_DefaultMakeItem);
            if (listView.bindItem == null) listView.SetBindItemWithoutNotify(m_DefaultBindItem);
            if (listView.unbindItem == null) listView.unbindItem = m_DefaultUnbindItem;
        }

        protected override bool HasDefaultBindItem() => listView.bindItem == m_DefaultBindItem;

        protected override void ResetCallbacks()
        {
            if (HasDefaultBindItem()) listView.SetBindItemWithoutNotify(null);
            if (listView.makeItem == m_DefaultMakeItem) listView.SetMakeItemWithoutNotify(null);
            if (listView.unbindItem == m_DefaultUnbindItem) listView.unbindItem = null;
        }

        [Unbound.EditorFaithful("ListViewSerializedObjectBinding does not pool instances (Unity 6000.0.71f1, tests/projects/arraylistbinding)")]
        protected override void PoolRelease() { }
    }
}
