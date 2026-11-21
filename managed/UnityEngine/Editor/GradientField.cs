using System;
using System.Diagnostics;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit GradientField for Gradient SerializedProperty binding.
// Scoped to field state, visual hierarchy, independent gradient copy semantics, UXML
// serialization/traits, hdr/colorSpace properties, mixed-value display, and
// BindProperty/Bind/PropertyField integration without standalone GradientPicker popup UI.
// Documented in Unity 6000.0 Scripting API ("GradientField"); matches the Unity 6000.0.71f1
// Editor, tests/projects/gradientbinding.

namespace UnityEditor.UIElements
{
    public partial class GradientField : BaseField<Gradient>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<Gradient>.UxmlSerializedData
        {
            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<Gradient>.UxmlSerializedData.Register();
            }

            public override object CreateInstance() => new GradientField();
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<GradientField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Gradient>.UxmlTraits
        {
            public UxmlTraits() { }
        }

        internal static readonly BindingId colorSpaceProperty = nameof(colorSpace);
        internal static readonly BindingId hdrProperty = nameof(hdr);

        public new static readonly string ussClassName = "unity-gradient-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string contentUssClassName = ussClassName + "__content";
        public static readonly string backgroundUssClassName = ussClassName + "__background";
        [Obsolete("borderUssClass is not used anymore", true)]
        public static readonly string borderUssClassName = ussClassName + "__border";

        bool m_ValueNull;
        ColorSpace m_ColorSpace;
        bool m_Hdr;
        VisualElement m_GradientTextureImage;
        readonly Background m_DefaultBackground = default;

        public override Gradient value
        {
            get => m_ValueNull ? null : GradientCopy(rawValue);
            set
            {
                if (value != null || !m_ValueNull)
                    base.value = value;
            }
        }

        [CreateProperty]
        public ColorSpace colorSpace
        {
            get => m_ColorSpace;
            set
            {
                if (m_ColorSpace == value) return;
                m_ColorSpace = value;
                NotifyPropertyChanged(in colorSpaceProperty);
            }
        }

        [CreateProperty]
        public bool hdr
        {
            get => m_Hdr;
            set
            {
                if (m_Hdr == value) return;
                m_Hdr = value;
                NotifyPropertyChanged(in hdrProperty);
            }
        }

        internal static Gradient GradientCopy(Gradient other)
        {
            var copy = new Gradient();
            copy.CopyKeysAndModeFrom(other);
            return copy;
        }

        public GradientField() : this(null) { }

        public GradientField(string label) : base(label, null)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);

            var background = new VisualElement { pickingMode = PickingMode.Ignore };
            background.AddToClassList(backgroundUssClassName);
            visualInput.Add(background);

            m_GradientTextureImage = new VisualElement { pickingMode = PickingMode.Ignore };
            m_GradientTextureImage.AddToClassList(contentUssClassName);
            background.Add(m_GradientTextureImage);

            rawValue = new Gradient();
        }

        [EventInterest(new[]
        {
            typeof(KeyDownEvent),
            typeof(MouseDownEvent),
            typeof(DetachFromPanelEvent),
            typeof(AttachToPanelEvent)
        })]
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

        [Unbound.EditorFaithful("default event handling was migrated to HandleEventBubbleUp")]
        [EventInterest(EventInterestOptions.Inherit)]
        [Obsolete("ExecuteDefaultActionAtTarget override has been removed because default event handling was migrated to HandleEventBubbleUp. Please use HandleEventBubbleUp.", false)]
        protected override void ExecuteDefaultActionAtTarget(EventBase evt)
        {
        }

        public override void SetValueWithoutNotify(Gradient newValue)
        {
            m_ValueNull = newValue == null;
            if (newValue != null)
                rawValue.CopyKeysAndModeFrom(newValue);
            else
                rawValue.ResetToDefaultWhite();

            if (m_ValueNull || showMixedValue)
                visualInput.style.backgroundImage = m_DefaultBackground;
            else
                IncrementVersion(VersionChangeType.Repaint);
        }

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                visualInput.style.backgroundImage = m_DefaultBackground;
                visualInput.Add(mixedValueLabel);
            }
            else
            {
                IncrementVersion(VersionChangeType.Repaint);
                mixedValueLabel.RemoveFromHierarchy();
            }
        }

        internal override bool EqualsCurrentValue(Gradient v)
        {
            if (v == null && m_ValueNull) return true;
            if ((v == null) ^ m_ValueNull) return false;
            int colorCount = rawValue.ColorKeyCount;
            int alphaCount = rawValue.AlphaKeyCount;
            if (colorCount != v.ColorKeyCount || alphaCount != v.AlphaKeyCount || rawValue.mode != v.mode)
                return false;

            bool ignoreTime = Unbound.Runtime.Compat.GradientFieldIgnoresKeyTimeInEquality;
            for (int i = 0; i < colorCount; i++)
            {
                var currentKey = rawValue.ColorKeyAt(i);
                var otherKey = v.ColorKeyAt(i);
                if (currentKey.color != otherKey.color
                    || (!ignoreTime && !Mathf.Approximately(currentKey.time, otherKey.time)))
                {
                    return false;
                }
            }
            for (int j = 0; j < alphaCount; j++)
            {
                var currentKey = rawValue.AlphaKeyAt(j);
                var otherKey = v.AlphaKeyAt(j);
                if (!Mathf.Approximately(currentKey.alpha, otherKey.alpha)
                    || (!ignoreTime && !Mathf.Approximately(currentKey.time, otherKey.time)))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
