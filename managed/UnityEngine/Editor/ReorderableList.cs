using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;

namespace UnityEditorInternal
{
    public partial class ReorderableList
    {
        const float MinimumHeaderHeight = 2f;
        const float CompactHeaderThreshold = 5f;
        const float CompactTopPadding = 1f;
        const float StandardTopPadding = 4f;
        const float BottomPadding = 4f;
        const float ElementPadding = 2f;
        const int MaximumHeightRefreshes = 8;

        internal List<int> m_Selection = new List<int>();
        internal bool m_PropertyCacheValid;
        SerializedProperty m_SerializedProperty;
        readonly bool m_DisplayHeader;
        float[] m_ElementEnds = Array.Empty<float>();
        int m_CachedElementCount;
        float m_LastElementAreaHeight = -1f;
        bool m_RefreshingHeights;

        public delegate float ElementHeightCallbackDelegate(int index);
        public ElementHeightCallbackDelegate elementHeightCallback;
        public bool displayAdd;
        public bool displayRemove;
        public float elementHeight;
        public float headerHeight;
        public float footerHeight;
        public bool showDefaultBackground;

        public ReorderableList(IList elements, Type elementType)
            : this(null, elements, true, true, true, true) { }

        public ReorderableList(IList elements, Type elementType, bool draggable, bool displayHeader,
            bool displayAddButton, bool displayRemoveButton)
            : this(null, elements, draggable, displayHeader, displayAddButton, displayRemoveButton) { }

        public ReorderableList(SerializedObject serializedObject, SerializedProperty elements)
            : this(elements, null, true, true, true, true) { }

        public ReorderableList(SerializedObject serializedObject, SerializedProperty elements, bool draggable,
            bool displayHeader, bool displayAddButton, bool displayRemoveButton)
            : this(elements, null, draggable, displayHeader, displayAddButton, displayRemoveButton) { }

        ReorderableList(SerializedProperty property, IList elements, bool draggable, bool displayHeader,
            bool displayAddButton, bool displayRemoveButton)
        {
            list = elements;
            m_SerializedProperty = property;
            m_DisplayHeader = displayHeader;
            this.draggable = draggable;
            displayAdd = displayAddButton;
            displayRemove = displayRemoveButton;
            selectedIndices = m_Selection.AsReadOnly();

            if (Compat.EditorReorderableListLayoutMeasured)
            {
                elementHeight = 21f;
                headerHeight = 20f;
                footerHeight = 20f;
                showDefaultBackground = true;
            }

            if (property != null)
            {
                if (!property.editable) this.draggable = false;
                if (!property.isArray) Debug.LogError("Input elements should be an Array SerializedProperty");
            }
        }

        public IList list { get; set; }

        public SerializedProperty serializedProperty
        {
            get => m_SerializedProperty;
            set
            {
                m_SerializedProperty = value;
                if (value == null) throw new NullReferenceException("Object reference not set to an instance of an object");
            }
        }

        public bool draggable { get; set; }
        public bool multiSelect { get; set; }
        public ReadOnlyCollection<int> selectedIndices { get; }

        public int count => GetCount(out _);

        int GetCount(out bool overMultiEditLimit)
        {
            overMultiEditLimit = false;
            var property = m_SerializedProperty;
            if (property == null) return list?.Count ?? 0;
            if (!property.isArray)
            {
                Debug.LogError("Retrieving array size but no array was provided");
                return 0;
            }
            int size = property.minArraySize;
            var owner = property.serializedObject;
            overMultiEditLimit = owner.isEditingMultipleObjects && size > owner.maxArraySizeForMultiEditing;
            return overMultiEditLimit ? 0 : size;
        }

        public float GetHeight()
        {
            if (!Compat.EditorReorderableListLayoutMeasured)
            {
                Stubs.NotImplemented("UnityEditorInternal.ReorderableList.GetHeight");
                return 0f;
            }

            float header = Mathf.Max(m_DisplayHeader ? headerHeight : 0f, MinimumHeaderHeight);
            return header + GetElementAreaHeight() + footerHeight;
        }

        internal void InvalidateCache() => m_PropertyCacheValid = false;

        float GetElementAreaHeight()
        {
            if (m_RefreshingHeights)
                throw new InvalidOperationException("A ReorderableList element height callback cannot measure the same list again.");

            // A change in the reported area invalidates the cached row heights, even on the
            // first measurement. Bound the refreshes for a callback that never settles.
            for (int attempt = 0; attempt < MaximumHeightRefreshes; attempt++)
            {
                float padding = BottomPadding + (headerHeight > CompactHeaderThreshold ? StandardTopPadding : CompactTopPadding);
                int elementCount = GetCount(out bool overMultiEditLimit);
                if (!m_PropertyCacheValid && !overMultiEditLimit) RefreshElementHeights(elementCount);

                float height = padding;
                if (elementCount == 0) height += elementHeight * (overMultiEditLimit ? 2 : 1);
                else if (elementCount <= m_CachedElementCount) height += m_ElementEnds[elementCount - 1];

                if (height.Equals(m_LastElementAreaHeight)) return height;
                m_LastElementAreaHeight = height;
                InvalidateCache();
            }
            throw new InvalidOperationException("ReorderableList element heights did not stabilize.");
        }

        void RefreshElementHeights(int elementCount)
        {
            m_RefreshingHeights = true;
            try
            {
                if (elementCount > m_ElementEnds.Length)
                    Array.Resize(ref m_ElementEnds, Math.Max(elementCount, m_ElementEnds.Length * 2));

                float end = 0f;
                for (int index = 0; index < elementCount; index++)
                {
                    float height = elementHeightCallback?.Invoke(index) ?? elementHeight;
                    if (height != 0f) height += ElementPadding;
                    end += height;
                    m_ElementEnds[index] = end;
                }
                m_CachedElementCount = elementCount;
                m_PropertyCacheValid = true;
            }
            finally { m_RefreshingHeights = false; }
        }

        public int index
        {
            get => m_Selection.Count == 0 ? count - 1 : m_Selection[0];
            set => Select(value);
        }

        public void ClearSelection() => m_Selection.Clear();

        public void Select(int index, bool append = false)
        {
            if (!append)
            {
                m_Selection.Clear();
                m_Selection.Add(index);
                return;
            }

            int position = m_Selection.BinarySearch(index);
            if (position < 0) m_Selection.Insert(~position, index);
        }

        public void SelectRange(int indexFrom, int indexTo)
        {
            if (!multiSelect) throw new InvalidOperationException("Cannot select range when not in multi select mode.");
            int first = Math.Min(indexFrom, indexTo);
            int last = Math.Max(indexFrom, indexTo);
            m_Selection.Clear();
            for (int index = first; index < last; index++) m_Selection.Add(index);
            m_Selection.Add(last);
        }

        public bool IsSelected(int index) => m_Selection.BinarySearch(index) >= 0;

        public void Deselect(int index)
        {
            int position = m_Selection.BinarySearch(index);
            if (position >= 0) m_Selection.RemoveAt(position);
        }
    }
}
