using System;
using System.Diagnostics;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit ColorField for Color and Color32 SerializedProperty binding.
// Scoped to field state, visual hierarchy, UXML serialization/traits, mixed-value display, and
// BindProperty/Bind/PropertyField integration without standalone ColorPicker or EyeDropper windows.
// Documented in Unity 6000.0 Scripting API ("ColorField", "ColorUsageAttribute"); matches the
// Unity 6000.0.71f1 Editor, tests/projects/colorbinding.

namespace UnityEditor.UIElements
{
    public partial class ColorField : BaseField<Color>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<Color>.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField] bool showEyeDropper;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags showEyeDropper_UxmlAttributeFlags;
            [SerializeField] bool showAlpha;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags showAlpha_UxmlAttributeFlags;
            [SerializeField] bool hdr;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags hdr_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<Color>.UxmlSerializedData.Register();
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(showEyeDropper), "show-eye-dropper", null),
                    new UxmlAttributeNames(nameof(showAlpha), "show-alpha", null),
                    new UxmlAttributeNames(nameof(hdr), "hdr", null),
                });
            }

            public override object CreateInstance() => new ColorField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                var field = (ColorField)obj;
                if (ShouldWriteAttributeValue(showEyeDropper_UxmlAttributeFlags)) field.showEyeDropper = showEyeDropper;
                if (ShouldWriteAttributeValue(showAlpha_UxmlAttributeFlags)) field.showAlpha = showAlpha;
                if (ShouldWriteAttributeValue(hdr_UxmlAttributeFlags)) field.hdr = hdr;
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ColorField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseFieldTraits<Color, UxmlColorAttributeDescription>
        {
            readonly UxmlBoolAttributeDescription m_ShowEyeDropper = new UxmlBoolAttributeDescription
            {
                name = "show-eye-dropper",
                defaultValue = true
            };

            readonly UxmlBoolAttributeDescription m_ShowAlpha = new UxmlBoolAttributeDescription
            {
                name = "show-alpha",
                defaultValue = true
            };

            readonly UxmlBoolAttributeDescription m_Hdr = new UxmlBoolAttributeDescription
            {
                name = "hdr"
            };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var field = (ColorField)ve;
                field.showEyeDropper = m_ShowEyeDropper.GetValueFromBag(bag, cc);
                field.showAlpha = m_ShowAlpha.GetValueFromBag(bag, cc);
                field.hdr = m_Hdr.GetValueFromBag(bag, cc);
            }
        }

        internal static readonly BindingId showEyeDropperProperty = nameof(showEyeDropper);
        internal static readonly BindingId showAlphaProperty = nameof(showAlpha);
        internal static readonly BindingId hdrProperty = nameof(hdr);

        internal bool setAlphaIfTransparentWhenPicked;

        bool m_ShowAlpha;
        bool m_ShowEyeDropper;
        bool m_HDR;

        Label m_HDRLabel;
        VisualElement m_ColorContainer;
        VisualElement m_GradientContainer;
        VisualElement m_LeftGradient;
        VisualElement m_RightGradient;
        VisualElement m_AlphaGradientContainer;
        VisualElement m_LeftAlphaGradient;
        VisualElement m_RightAlphaGradient;
        VisualElement m_ColorElement;
        ProgressBar m_AlphaElement;
        VisualElement m_EyeDropperElement;

        public new static readonly string ussClassName = "unity-color-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string colorContainerUssClassName = ussClassName + "__color-container";
        public static readonly string colorUssClassName = ussClassName + "__color";
        public static readonly string mixedValueColorUssClassName = colorUssClassName + "--mixed-value";
        public static readonly string eyeDropperUssClassName = ussClassName + "__eyedropper";
        public static readonly string hdrLabelUssClassName = ussClassName + "__hdr";
        public static readonly string gradientContainerUssClassName = ussClassName + "__gradient-container";
        internal static readonly string internalColorFieldName = "unity-internal-color-field";

        [CreateProperty]
        public bool showEyeDropper
        {
            get => m_ShowEyeDropper;
            set
            {
                bool previous = m_ShowEyeDropper;
                m_ShowEyeDropper = value;
                if (m_EyeDropperElement != null)
                    m_EyeDropperElement.style.display = m_ShowEyeDropper ? DisplayStyle.Flex : DisplayStyle.None;
                if (previous != m_ShowEyeDropper)
                    NotifyPropertyChanged(in showEyeDropperProperty);
            }
        }

        [CreateProperty]
        public bool showAlpha
        {
            get => m_ShowAlpha;
            set
            {
                bool previous = m_ShowAlpha;
                m_ShowAlpha = value;
                if (m_AlphaElement != null)
                    m_AlphaElement.style.display = m_ShowAlpha ? DisplayStyle.Flex : DisplayStyle.None;
                if (previous != m_ShowAlpha)
                    NotifyPropertyChanged(in showAlphaProperty);
            }
        }

        [CreateProperty]
        public bool hdr
        {
            get => m_HDR;
            set
            {
                bool previous = m_HDR;
                m_HDR = value;
                var display = m_HDR ? DisplayStyle.Flex : DisplayStyle.None;
                if (m_HDRLabel != null) m_HDRLabel.style.display = display;
                if (m_GradientContainer != null) m_GradientContainer.style.display = display;
                if (m_AlphaGradientContainer != null) m_AlphaGradientContainer.style.display = display;
                UpdateColorProperties(this.value);
                if (previous != m_HDR)
                    NotifyPropertyChanged(in hdrProperty);
            }
        }

        public ColorField() : this(null) { }

        public ColorField(string label) : base(label, null)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);

            m_ColorContainer = new VisualElement { name = "unity-color-container" };
            m_ColorElement = new VisualElement { name = internalColorFieldName };
            m_AlphaElement = new ProgressBar
            {
                name = "unity-alpha-progress",
                title = string.Empty,
                lowValue = 0f,
                highValue = 100f
            };
            m_HDRLabel = new Label("HDR") { name = "unity-hdr-label" };
            m_EyeDropperElement = new VisualElement { name = "unity-eyedropper" };
            m_GradientContainer = new VisualElement { name = "unity-gradient-container" };
            m_LeftGradient = new VisualElement { name = "unity-gradient-left" };
            m_RightGradient = new VisualElement { name = "unity-gradient-right" };
            m_AlphaGradientContainer = new VisualElement { name = "unity-alpha-gradient-container" };
            m_LeftAlphaGradient = new VisualElement { name = "unity-alpha-gradient-left" };
            m_RightAlphaGradient = new VisualElement { name = "unity-alpha-gradient-right" };
            mixedValueLabel.name = "unity-mixed-value-label";

            visualInput.AddToClassList(inputUssClassName);
            m_ColorContainer.AddToClassList(colorContainerUssClassName);
            m_ColorElement.AddToClassList(colorUssClassName);
            m_HDRLabel.AddToClassList(hdrLabelUssClassName);
            m_EyeDropperElement.AddToClassList(eyeDropperUssClassName);
            m_GradientContainer.AddToClassList(gradientContainerUssClassName);
            m_AlphaGradientContainer.AddToClassList(gradientContainerUssClassName);

            m_GradientContainer.Add(m_LeftGradient);
            m_GradientContainer.Add(new VisualElement());
            m_GradientContainer.Add(m_RightGradient);
            m_AlphaGradientContainer.Add(m_LeftAlphaGradient);
            m_AlphaGradientContainer.Add(new VisualElement());
            m_AlphaGradientContainer.Add(m_RightAlphaGradient);

            m_ColorContainer.Add(m_ColorElement);
            m_ColorContainer.Add(m_GradientContainer);
            m_ColorContainer.Add(m_AlphaGradientContainer);
            m_ColorContainer.Add(m_HDRLabel);
            m_ColorContainer.Add(mixedValueLabel);
            m_ColorContainer.Add(m_AlphaElement);
            visualInput.Add(m_ColorContainer);
            visualInput.Add(m_EyeDropperElement);

            m_ColorContainer.RegisterCallback<AttachToPanelEvent>(OnAttach);

            labelElement.focusable = false;
            showAlpha = true;
            hdr = false;
            mixedValueLabel.style.display = showMixedValue ? DisplayStyle.Flex : DisplayStyle.None;
            mixedValueLabel.tabIndex = 0;
            mixedValueLabel.focusable = false;
            rawValue = default;
        }

        public override void SetValueWithoutNotify(Color newValue)
        {
            rawValue = newValue;
            UpdateColorProperties(rawValue);
            IncrementVersion(VersionChangeType.Repaint);
        }

        void OnAttach(AttachToPanelEvent evt) => UpdateColorProperties(value);

        void UpdateColorProperties(Color color)
        {
            if (panel == null || showMixedValue) return;
            if (m_AlphaElement != null) m_AlphaElement.value = color.a * 100f;
            var opaque = new Color(color.r, color.g, color.b, 1f);
            if (hdr) opaque = opaque.gamma;
            if (m_ColorElement != null) m_ColorElement.style.backgroundColor = opaque;
        }

        protected override void UpdateMixedValueContent()
        {
            m_ColorElement.EnableInClassList(mixedValueColorUssClassName, showMixedValue);
            if (showMixedValue)
            {
                mixedValueLabel.style.display = DisplayStyle.Flex;
                m_GradientContainer.style.display = DisplayStyle.None;
                m_AlphaGradientContainer.style.display = DisplayStyle.None;
                m_AlphaElement.style.display = DisplayStyle.None;
                m_HDRLabel.style.display = DisplayStyle.None;
            }
            else
            {
                mixedValueLabel.style.display = DisplayStyle.None;
                var hdrDisplay = hdr ? DisplayStyle.Flex : DisplayStyle.None;
                m_GradientContainer.style.display = hdrDisplay;
                m_AlphaGradientContainer.style.display = hdrDisplay;
                m_AlphaElement.style.display = showAlpha ? DisplayStyle.Flex : DisplayStyle.None;
                m_HDRLabel.style.display = hdrDisplay;
                UpdateColorProperties(value);
            }
        }
    }
}
