using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit CurveField for AnimationCurve SerializedProperty binding.
// Scoped to field state, visual hierarchy, independent curve copy semantics, UXML
// serialization/traits, mixed-value display, and BindProperty/Bind/PropertyField integration
// without standalone CurveEditorWindow popup UI.
// Documented in Unity 6000.0 Scripting API ("CurveField", "CurveField.RenderMode"); matches the
// Unity 6000.0.71f1 Editor, tests/projects/curvebinding.

namespace UnityEditor.UIElements
{
    public partial class CurveField : BaseField<AnimationCurve>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<AnimationCurve>.UxmlSerializedData
        {
            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<AnimationCurve>.UxmlSerializedData.Register();
            }

            public override object CreateInstance() => new CurveField();
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<CurveField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<AnimationCurve>.UxmlTraits
        {
            public UxmlTraits() { }
        }

        public enum RenderMode
        {
            Texture = 0,
            Mesh = 1,
            Default = Texture,
        }

        internal static readonly BindingId renderModeProperty = nameof(renderMode);

        public new static readonly string ussClassName = "unity-curve-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string contentUssClassName = ussClassName + "__content";
        [Obsolete("borderUssClass is not used anymore", false)]
        public static readonly string borderUssClassName = ussClassName + "__border";

        internal Action onShowPresets;

        bool m_ValueNull;
        RenderMode m_RenderMode = RenderMode.Texture;
        VisualElement m_Content;
        VisualElement m_ZeroIndicator;
        VisualElement m_ContentParent;

        public Rect ranges { get; set; }

        [CreateProperty]
        public RenderMode renderMode
        {
            get => m_RenderMode;
            set
            {
                if (m_RenderMode == value) return;
                m_RenderMode = value;
                if (m_RenderMode == RenderMode.Mesh)
                {
                    m_ContentParent = new VisualElement();
                    m_ContentParent.AddToClassList(contentUssClassName);
                    visualInput.Insert(0, m_ContentParent);
                    m_Content = new VisualElement { pickingMode = PickingMode.Ignore };
                    m_ZeroIndicator = new VisualElement();
                    m_ZeroIndicator.style.height = 1f;
                    m_ZeroIndicator.style.backgroundColor = Color.black;
                    m_ContentParent.Add(m_ZeroIndicator);
                    m_ContentParent.Add(m_Content);
                    m_Content.StretchToParentSize();
                    m_ZeroIndicator.StretchToParentWidth();
                }
                else
                {
                    m_ZeroIndicator?.RemoveFromHierarchy();
                    m_ZeroIndicator = null;
                    m_Content?.RemoveFromHierarchy();
                    m_Content = null;
                    m_ContentParent?.RemoveFromHierarchy();
                    m_ContentParent = null;
                }
                NotifyPropertyChanged(in renderModeProperty);
            }
        }

        public override AnimationCurve value
        {
            get => m_ValueNull ? null : CopyCurve(rawValue);
            set
            {
                if (value != null || !m_ValueNull)
                    base.value = value;
            }
        }

        internal static AnimationCurve CopyCurve(AnimationCurve other)
        {
            var copy = new AnimationCurve();
            copy.CopyFrom(other);
            return copy;
        }

        public CurveField() : this(null) { }

        public CurveField(string label) : base(label, null)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            ranges = Rect.zero;
            rawValue = new AnimationCurve(Array.Empty<Keyframe>());

            var border = new VisualElement
            {
                name = "unity-border",
                pickingMode = PickingMode.Ignore
            };
#pragma warning disable 618
            border.AddToClassList(borderUssClassName);
#pragma warning restore 618
            visualInput.Add(border);
        }

        public override void SetValueWithoutNotify(AnimationCurve newValue)
        {
            m_ValueNull = newValue == null;
            if (newValue != null)
            {
                rawValue.CopyFrom(newValue);
            }
            else
            {
                rawValue.ClearKeys();
                rawValue.preWrapMode = WrapMode.Once;
                rawValue.postWrapMode = WrapMode.Once;
            }
            visualInput.IncrementVersion(VersionChangeType.Repaint);
            m_Content?.IncrementVersion(VersionChangeType.Repaint);
        }

        [EventInterest(new[] { typeof(DetachFromPanelEvent), typeof(GeometryChangedEvent) })]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
        }

        [Unbound.EditorFaithful("default event handling was migrated to HandleEventBubbleUp")]
        [EventInterest(EventInterestOptions.Inherit)]
        [Obsolete("ExecuteDefaultAction override has been removed because default event handling was migrated to HandleEventBubbleUp. Please use HandleEventBubbleUp.", false)]
        protected override void ExecuteDefaultAction(EventBase evt)
        {
        }

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                visualInput.Add(mixedValueLabel);
                m_ContentParent?.RemoveFromHierarchy();
            }
            else
            {
                if (m_ContentParent != null) visualInput.Add(m_ContentParent);
                mixedValueLabel.RemoveFromHierarchy();
            }
        }

        internal override bool EqualsCurrentValue(AnimationCurve v)
        {
            if (v == null && m_ValueNull) return true;
            if ((v == null) ^ m_ValueNull) return false;
            int length = rawValue.length;
            if (rawValue.preWrapMode != v.preWrapMode || rawValue.postWrapMode != v.postWrapMode || length != v.length)
                return false;
            for (int i = 0; i < length; i++)
            {
                if (!EqualityComparer<Keyframe>.Default.Equals(rawValue[i], v[i]))
                    return false;
            }
            return true;
        }
    }
}
