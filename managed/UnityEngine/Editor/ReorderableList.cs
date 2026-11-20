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
        internal List<int> m_Selection = new List<int>();
        SerializedProperty m_SerializedProperty;

        public bool displayAdd;
        public bool displayRemove;
        public float elementHeight;
        public float headerHeight;
        public float footerHeight;
        public bool showDefaultBackground;

        public ReorderableList(IList elements, Type elementType)
            : this(elements, null, true, true, true) { }

        public ReorderableList(IList elements, Type elementType, bool draggable, bool displayHeader,
            bool displayAddButton, bool displayRemoveButton)
            : this(elements, null, draggable, displayAddButton, displayRemoveButton) { }

        public ReorderableList(SerializedObject serializedObject, SerializedProperty elements)
            : this(null, elements, true, true, true) { }

        public ReorderableList(SerializedObject serializedObject, SerializedProperty elements, bool draggable,
            bool displayHeader, bool displayAddButton, bool displayRemoveButton)
            : this(null, elements, draggable, displayAddButton, displayRemoveButton) { }

        ReorderableList(IList elements, SerializedProperty property, bool draggable, bool displayAddButton,
            bool displayRemoveButton)
        {
            list = elements;
            m_SerializedProperty = property;
            this.draggable = draggable;
            displayAdd = displayAddButton;
            displayRemove = displayRemoveButton;
            selectedIndices = m_Selection.AsReadOnly();

            if (Compat.EditorReorderableListDefaultsMeasured)
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

        public int count
        {
            get
            {
                var property = m_SerializedProperty;
                if (property == null) return list?.Count ?? 0;
                if (!property.isArray)
                {
                    Debug.LogError("Retrieving array size but no array was provided");
                    return 0;
                }
                int size = property.minArraySize;
                var owner = property.serializedObject;
                return owner.isEditingMultipleObjects && size > owner.maxArraySizeForMultiEditing ? 0 : size;
            }
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
