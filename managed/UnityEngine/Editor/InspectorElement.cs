using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.UIElements
{
    /// <summary>
    /// The inspector of a SerializedObject: its Editor's UI Toolkit GUI (CreateInspectorGUI), else
    /// the Editor's IMGUI in an IMGUIContainer; a generic inspector builds one PropertyField per
    /// visible property. Built when the element is bound, again when it is bound to another object.
    /// An Editor the element made is destroyed when it leaves its panel. As the Unity 6000.0.71f1
    /// Editor (tests/projects/inspectorelement).
    /// </summary>
    public partial class InspectorElement : BindableElement
    {
        public static readonly string ussClassName = "unity-inspector-element";
        public static readonly string customInspectorUssClassName = ussClassName + "__custom-inspector-container";
        public static readonly string iMGUIContainerUssClassName = ussClassName + "__imgui-container";
        public static readonly string iMGUIInspectorVariantUssClassName = ussClassName + "--imgui";
        public static readonly string uIEInspectorVariantUssClassName = ussClassName + "--uie";
        public static readonly string noInspectorFoundVariantUssClassName = ussClassName + "--no-inspector-found";
        public static readonly string uIECustomVariantUssClassName = ussClassName + "--uie-custom";
        public static readonly string iMGUICustomVariantUssClassName = ussClassName + "--imgui-custom";
        public static readonly string iMGUIDefaultVariantUssClassName = ussClassName + "--imgui-default";
        public static readonly string uIEDefaultVariantUssClassName = ussClassName + "--uie-default";
        public static readonly string debugVariantUssClassName = ussClassName + "--debug";
        public static readonly string debugInternalVariantUssClassName = ussClassName + "--debug-internal";
        internal static readonly string noScriptErrorContainerName = "unity-inspector-no-script-error-container";

        internal enum DefaultInspectorFramework
        {
            UIToolkit = 0,
            IMGUI = 1,
        }

        Editor m_Editor;
        bool m_OwnsEditor;
        SerializedObject m_BoundObject;
        Type m_BoundObjectType;
        VisualElement m_InspectorElement;
        DefaultInspectorFramework m_DefaultInspectorFramework;
        bool m_Rebind;

        internal Editor editor => m_Editor;
        internal bool ownsEditor => m_OwnsEditor;
        internal SerializedObject boundObject => m_BoundObject;
        internal VisualElement prefabOverrideBlueBarsContainer { get; private set; }
        internal VisualElement livePropertyYellowBarsContainer { get; private set; }

        internal DefaultInspectorFramework defaultInspectorFramework
        {
            get => m_DefaultInspectorFramework;
            set
            {
                m_Rebind = true;
                m_DefaultInspectorFramework = value;
            }
        }

        // The project's choice for generic inspectors (Project Settings > Editor > Use IMGUI
        // Default Inspector).
        static DefaultInspectorFramework GetDefaultInspectorFramework() =>
            EditorSettings.inspectorUseIMGUIDefaultInspector ? DefaultInspectorFramework.IMGUI : DefaultInspectorFramework.UIToolkit;

        static SerializedObject CreateSerializedObjectForTarget(Object target) => target == null ? null : new SerializedObject(target);

        public InspectorElement() : this((Object)null) { }

        public InspectorElement(Object obj) : this(CreateSerializedObjectForTarget(obj), GetDefaultInspectorFramework()) { }

        public InspectorElement(SerializedObject obj) : this(obj, GetDefaultInspectorFramework()) { }

        public InspectorElement(Editor editor) : this(editor, GetDefaultInspectorFramework()) { }

        internal InspectorElement(Object obj, DefaultInspectorFramework defaultInspectorFramework)
            : this(new SerializedObject(obj), null, defaultInspectorFramework) { }

        internal InspectorElement(SerializedObject serializedObject, DefaultInspectorFramework defaultInspectorFramework)
            : this(serializedObject, null, defaultInspectorFramework) { }

        internal InspectorElement(Editor editor, DefaultInspectorFramework defaultInspectorFramework)
            : this(editor.serializedObject, editor, defaultInspectorFramework) { }

        InspectorElement(SerializedObject obj, Editor editor, DefaultInspectorFramework defaultInspectorFramework)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList(ussClassName);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            prefabOverrideBlueBarsContainer = new VisualElement { name = BindingExtensions.prefabOverrideBarContainerName };
            prefabOverrideBlueBarsContainer.style.position = Position.Absolute;
            livePropertyYellowBarsContainer = new VisualElement { name = BindingExtensions.livePropertyBarContainerName };
            livePropertyYellowBarsContainer.style.position = Position.Absolute;
            Add(prefabOverrideBlueBarsContainer);
            Add(livePropertyYellowBarsContainer);
            m_DefaultInspectorFramework = defaultInspectorFramework;
            if (editor == null)
            {
                if (obj == null) return;
                m_Editor = Editor.CreateEditor(obj.targetObjects);
                m_OwnsEditor = true;
            }
            else
            {
                m_Editor = editor;
                if (m_Editor.targets.Length == 0) return;
            }
            this.Bind(obj);
        }

        // Back on a panel after its Editor was destroyed: built again for the object it shows.
        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (boundObject != null && boundObject.isValid && m_Editor == null)
            {
                m_Rebind = true;
                this.Bind(boundObject);
            }
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt) => DestroyOwnedEditor();

        void DestroyOwnedEditor()
        {
            if (!m_OwnsEditor || m_Editor == null) return;
            Object.DestroyImmediate(m_Editor);
            m_Editor = null;
            m_OwnsEditor = false;
        }

        // A bind of another object rebuilds the inspector; an element bound to its own object keeps
        // it when an ancestor binds another one.
        [EventInterest(new[] { typeof(SerializedObjectBindEvent) })]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (evt is not SerializedObjectBindEvent bindEvent) return;
            var dataSource = GetProperty(BindingExtensions.s_DataSourceProperty);
            if (dataSource != null && dataSource != bindEvent.bindObject)
                evt.StopPropagation();
            else if (bindEvent.bindObject != null && bindEvent.bindObject.isValid && (m_Rebind || m_BoundObject != bindEvent.bindObject))
            {
                m_Rebind = false;
                CreateInspectorElementFromSerializedObject(bindEvent.bindObject);
            }
        }

        [EventInterest(EventInterestOptions.Inherit)]
        [Obsolete("ExecuteDefaultActionAtTarget override has been removed because default event handling was migrated to HandleEventBubbleUp. Please use HandleEventBubbleUp.", false)]
        [Unbound.EditorFaithful("Unity keeps an empty obsolete override after migrating handling to HandleEventBubbleUp")]
        protected override void ExecuteDefaultActionAtTarget(EventBase evt) { }

        internal void SetEditor(Editor value)
        {
            bool sameType = value.target != null && value.target.GetType() == m_BoundObjectType;
            if (sameType && m_Editor == value) return;
            DestroyOwnedEditor();
            m_Editor = value;
            m_Rebind = true;
            this.Bind(m_Editor.serializedObject);
        }

        void ClearInspectorElement()
        {
            m_InspectorElement?.RemoveFromHierarchy();
            RemoveFromClassList(iMGUIInspectorVariantUssClassName);
            RemoveFromClassList(uIEInspectorVariantUssClassName);
            RemoveFromClassList(noInspectorFoundVariantUssClassName);
            RemoveFromClassList(uIECustomVariantUssClassName);
            RemoveFromClassList(iMGUICustomVariantUssClassName);
            RemoveFromClassList(iMGUIDefaultVariantUssClassName);
            RemoveFromClassList(uIEDefaultVariantUssClassName);
            RemoveFromClassList(debugVariantUssClassName);
            RemoveFromClassList(debugInternalVariantUssClassName);
        }

        // A generic inspector of the same type keeps its UI Toolkit GUI (its PropertyFields bind to
        // the new object); any other Editor builds its GUI again.
        void CreateInspectorElementFromSerializedObject(SerializedObject bindObject)
        {
            var target = bindObject?.targetObject;
            var type = target != null ? target.GetType() : null;
            bool sameType = m_BoundObjectType == type;
            m_BoundObject = bindObject;
            m_BoundObjectType = type;
            if (m_BoundObject == null)
            {
                ClearInspectorElement();
                return;
            }
            m_Editor = GetOrCreateEditor(bindObject);
            if (m_Editor == null) return;
            if (m_Editor is GenericInspector)
            {
                if (!sameType || m_InspectorElement == null || m_DefaultInspectorFramework == DefaultInspectorFramework.IMGUI)
                {
                    ClearInspectorElement();
                    m_InspectorElement = m_DefaultInspectorFramework == DefaultInspectorFramework.IMGUI
                        ? CreateInspectorElementUsingIMGUI(m_Editor)
                        : CreateInspectorElementUsingUIToolkit(m_Editor);
                }
            }
            else
            {
                ClearInspectorElement();
                m_InspectorElement = CreateInspectorElementUsingUIToolkit(m_Editor) ?? CreateInspectorElementUsingIMGUI(m_Editor);
                m_InspectorElement.AddToClassList(customInspectorUssClassName);
            }
            if (m_InspectorElement != null && m_InspectorElement.parent != this)
                hierarchy.Add(m_InspectorElement);
        }

        Editor GetOrCreateEditor(SerializedObject serializedObject)
        {
            var targets = serializedObject != null && serializedObject.isValid ? serializedObject.targetObjects : null;
            if (m_Editor != null)
            {
                if (m_Editor.serializedObject == serializedObject || (targets != null && m_Editor.targets.SequenceEqual(targets, ReferenceEqualityComparer.Instance)))
                    return m_Editor;
                DestroyOwnedEditor();
            }
            m_Editor = Editor.CreateEditor(targets);
            m_OwnsEditor = true;
            return m_Editor;
        }

        /// <summary>One PropertyField per visible property, named "PropertyField:" + its path; the
        /// script field is disabled (Unbound has no debug inspector mode, where it is editable).</summary>
        public static void FillDefaultInspector(VisualElement container, SerializedObject serializedObject, Editor editor)
        {
            if (serializedObject == null) return;
            var iterator = serializedObject.GetIterator();
            if (iterator.NextVisible(true))
            {
                do
                {
                    var field = new PropertyField(iterator) { name = "PropertyField:" + iterator.propertyPath };
                    if (iterator.propertyPath == "m_Script" && (serializedObject.targetObject != null || iterator.objectReferenceValue != null))
                        field.SetEnabled(false);
                    container.Add(field);
                }
                while (iterator.NextVisible(false));
            }
            // The Editor adds an IMGUI warning for a script that cannot be loaded; Unbound has no
            // objects whose script is missing, so it is reported if one is ever inspected.
            if (serializedObject.targetObject == null && serializedObject.FindProperty("m_Script") != null)
                Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.InspectorElement.FillDefaultInspector (missing script warning)");
        }

        VisualElement CreateInspectorElementUsingUIToolkit(Editor targetEditor)
        {
            var element = targetEditor.CreateInspectorGUI();
            if (element == null) return null;
            if (targetEditor is GenericInspector)
            {
                AddToClassList(uIEDefaultVariantUssClassName);
                AddToClassList(uIEInspectorVariantUssClassName);
            }
            else
            {
                AddToClassList(uIECustomVariantUssClassName);
                if (editor.UseDefaultMargins()) AddToClassList(uIEInspectorVariantUssClassName);
            }
            return element;
        }

        // The container the Editor draws the editor's OnInspectorGUI in. Unbound does not run IMGUI
        // containers in a panel, so the editor's IMGUI is reported instead of drawn.
        VisualElement CreateInspectorElementUsingIMGUI(Editor targetEditor)
        {
            AddToClassList(targetEditor is GenericInspector ? iMGUIDefaultVariantUssClassName : iMGUICustomVariantUssClassName);
            Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.UIElements.InspectorElement (IMGUI of {targetEditor.GetType().Name})");
            var inspector = m_InspectorElement as IMGUIContainer ?? new IMGUIContainer();
            inspector.onGUIHandler = () =>
            {
                if (targetEditor.target == null || !targetEditor.serializedObject.isValid) return;
                try
                {
                    targetEditor.OnInspectorGUI();
                }
                catch (Exception exception)
                {
                    if (GUIUtility.ShouldRethrowException(exception)) throw;
                    Debug.LogException(exception);
                }
            };
            inspector.style.overflow = Overflow.Visible;
            inspector.AddToClassList(iMGUIContainerUssClassName);
            AddToClassList(iMGUIInspectorVariantUssClassName);
            return inspector;
        }
    }
}
