#pragma warning disable CS0618 // the deprecated traits are what this file implements
using System;
using System.Collections.Generic;
using UnityEngine;

// The traits of the built-in controls, used by the deprecated UxmlFactory/UxmlTraits path (a game's
// factory for its own control over a built-in one). As in the Unity 6000.0.71f1 Editor
// (tests/projects/uxmllegacy): each traits class describes its attributes (names, defaults and
// former names as the Editor lists them, base classes first), and Init sets each described property
// from its attribute or, when the attribute is absent, from the description's default, in the order
// below; the few marked "when present" keep the element's value instead. The serialized-data path
// that UXML uses for built-in elements is not involved.
namespace UnityEngine.UIElements
{
    public partial class BaseField<TValueType>
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public partial class UxmlTraits : BindableElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_Label = new UxmlStringAttributeDescription { name = "label" };

            public UxmlTraits()
            {
                focusIndex.defaultValue = 0;
                focusable.defaultValue = true;
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((BaseField<TValueType>)ve).label = m_Label.GetValueFromBag(bag, cc);
            }
        }
    }

    [Obsolete("BaseFieldTraits<TValueType, TValueUxmlAttributeType> is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
    public partial class BaseFieldTraits<TValueType, TValueUxmlAttributeType> : BaseField<TValueType>.UxmlTraits
        where TValueUxmlAttributeType : TypedUxmlAttributeDescription<TValueType>, new()
    {
        readonly TValueUxmlAttributeType m_Value = new TValueUxmlAttributeType { name = "value" };

        public BaseFieldTraits() { }

        public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
        {
            base.Init(ve, bag, cc);
            ((INotifyValueChanged<TValueType>)ve).SetValueWithoutNotify(m_Value.GetValueFromBag(bag, cc));
        }
    }

    [Obsolete("TextValueFieldTraits<TValueType, TValueUxmlAttributeType> is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
    public partial class TextValueFieldTraits<TValueType, TValueUxmlAttributeType> : BaseFieldTraits<TValueType, TValueUxmlAttributeType>
        where TValueUxmlAttributeType : TypedUxmlAttributeDescription<TValueType>, new()
    {
        readonly UxmlStringAttributeDescription m_PlaceholderText = new UxmlStringAttributeDescription { name = "placeholder-text" };
        readonly UxmlBoolAttributeDescription m_HidePlaceholderOnFocus = new UxmlBoolAttributeDescription { name = "hide-placeholder-on-focus" };
        readonly UxmlBoolAttributeDescription m_IsReadOnly = new UxmlBoolAttributeDescription { name = "readonly" };
        readonly UxmlBoolAttributeDescription m_IsDelayed = new UxmlBoolAttributeDescription { name = "is-delayed" };

        public TextValueFieldTraits() { }

        public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
        {
            base.Init(ve, bag, cc);
            if (!(ve is TextInputBaseField<TValueType> field)) return;
            field.textEdition.placeholder = m_PlaceholderText.GetValueFromBag(bag, cc);
            field.textEdition.hidePlaceholderOnFocus = m_HidePlaceholderOnFocus.GetValueFromBag(bag, cc);
            field.isReadOnly = m_IsReadOnly.GetValueFromBag(bag, cc);
            field.isDelayed = m_IsDelayed.GetValueFromBag(bag, cc);
        }
    }

    public partial class TextInputBaseField<TValueType>
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public partial class UxmlTraits : BaseFieldTraits<string, UxmlStringAttributeDescription>
        {
            readonly UxmlIntAttributeDescription m_MaxLength = new UxmlIntAttributeDescription { name = "max-length", obsoleteNames = new[] { "maxLength" }, defaultValue = -1 };
            readonly UxmlBoolAttributeDescription m_Password = new UxmlBoolAttributeDescription { name = "password" };
            readonly UxmlStringAttributeDescription m_MaskCharacter = new UxmlStringAttributeDescription { name = "mask-character", obsoleteNames = new[] { "maskCharacter" }, defaultValue = "*" };
            readonly UxmlStringAttributeDescription m_PlaceholderText = new UxmlStringAttributeDescription { name = "placeholder-text" };
            readonly UxmlBoolAttributeDescription m_HidePlaceholderOnFocus = new UxmlBoolAttributeDescription { name = "hide-placeholder-on-focus" };
            readonly UxmlBoolAttributeDescription m_IsReadOnly = new UxmlBoolAttributeDescription { name = "readonly" };
            readonly UxmlBoolAttributeDescription m_IsDelayed = new UxmlBoolAttributeDescription { name = "is-delayed" };
            readonly UxmlEnumAttributeDescription<ScrollerVisibility> m_VerticalScrollerVisibility = new UxmlEnumAttributeDescription<ScrollerVisibility> { name = "vertical-scroller-visibility", defaultValue = ScrollerVisibility.Hidden };
            readonly UxmlBoolAttributeDescription m_SelectAllOnMouseUp = new UxmlBoolAttributeDescription { name = "select-all-on-mouse-up", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_SelectAllOnFocus = new UxmlBoolAttributeDescription { name = "select-all-on-focus", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_SelectWordByDoubleClick = new UxmlBoolAttributeDescription { name = "select-word-by-double-click", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_SelectLineByTripleClick = new UxmlBoolAttributeDescription { name = "select-line-by-triple-click", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_EmojiFallbackSupport = new UxmlBoolAttributeDescription { name = "emoji-fallback-support", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_HideMobileInput = new UxmlBoolAttributeDescription { name = "hide-mobile-input" };
            readonly UxmlEnumAttributeDescription<TouchScreenKeyboardType> m_KeyboardType = new UxmlEnumAttributeDescription<TouchScreenKeyboardType> { name = "keyboard-type" };
            readonly UxmlBoolAttributeDescription m_AutoCorrection = new UxmlBoolAttributeDescription { name = "auto-correction" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var field = (TextInputBaseField<TValueType>)ve;
                field.maxLength = m_MaxLength.GetValueFromBag(bag, cc);
                field.password = m_Password.GetValueFromBag(bag, cc);
                field.readOnly = m_IsReadOnly.GetValueFromBag(bag, cc);
                field.isDelayed = m_IsDelayed.GetValueFromBag(bag, cc);
                field.textSelection.selectAllOnFocus = m_SelectAllOnFocus.GetValueFromBag(bag, cc);
                field.textSelection.selectAllOnMouseUp = m_SelectAllOnMouseUp.GetValueFromBag(bag, cc);
                field.doubleClickSelectsWord = m_SelectWordByDoubleClick.GetValueFromBag(bag, cc);
                field.tripleClickSelectsLine = m_SelectLineByTripleClick.GetValueFromBag(bag, cc);
                field.emojiFallbackSupport = m_EmojiFallbackSupport.GetValueFromBag(bag, cc);
                // Set also when the attribute is absent (to its default), unlike the other conditional reads.
                var visibility = m_VerticalScrollerVisibility.defaultValue;
                m_VerticalScrollerVisibility.TryGetValueFromBag(bag, cc, ref visibility);
                field.verticalScrollerVisibility = visibility;
                field.hideMobileInput = m_HideMobileInput.GetValueFromBag(bag, cc);
                field.keyboardType = m_KeyboardType.GetValueFromBag(bag, cc);
                field.autoCorrection = m_AutoCorrection.GetValueFromBag(bag, cc);
                var mask = m_MaskCharacter.GetValueFromBag(bag, cc);
                field.maskChar = string.IsNullOrEmpty(mask) ? '*' : mask[0];
                field.placeholderText = m_PlaceholderText.GetValueFromBag(bag, cc);
                field.hidePlaceholderOnFocus = m_HidePlaceholderOnFocus.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class TextField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public partial class UxmlTraits : TextInputBaseField<string>.UxmlTraits
        {
            // Read again by TextField when present (not listed: static).
            static readonly UxmlStringAttributeDescription k_Value = new UxmlStringAttributeDescription { name = "value" };

            readonly UxmlBoolAttributeDescription m_Multiline = new UxmlBoolAttributeDescription { name = "multiline" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                var field = (TextField)ve;
                base.Init(ve, bag, cc);
                string value = string.Empty;
                if (k_Value.TryGetValueFromBag(bag, cc, ref value)) field.SetValueWithoutNotify(value);
                field.multiline = m_Multiline.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class TextElement
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public partial class UxmlTraits : BindableElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text" };
            readonly UxmlBoolAttributeDescription m_EnableRichText = new UxmlBoolAttributeDescription { name = "enable-rich-text", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_EmojiFallbackSupport = new UxmlBoolAttributeDescription { name = "emoji-fallback-support", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_ParseEscapeSequences = new UxmlBoolAttributeDescription { name = "parse-escape-sequences" };
            readonly UxmlBoolAttributeDescription m_Selectable = new UxmlBoolAttributeDescription { name = "selectable" };
            readonly UxmlBoolAttributeDescription m_SelectWordByDoubleClick = new UxmlBoolAttributeDescription { name = "select-word-by-double-click" };
            readonly UxmlBoolAttributeDescription m_SelectLineByTripleClick = new UxmlBoolAttributeDescription { name = "select-line-by-triple-click" };
            readonly UxmlBoolAttributeDescription m_DisplayTooltipWhenElided = new UxmlBoolAttributeDescription { name = "display-tooltip-when-elided" };

            public UxmlTraits() { }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var element = (TextElement)ve;
                element.text = m_Text.GetValueFromBag(bag, cc);
                element.enableRichText = m_EnableRichText.GetValueFromBag(bag, cc);
                element.emojiFallbackSupport = m_EmojiFallbackSupport.GetValueFromBag(bag, cc);
                element.selection.isSelectable = m_Selectable.GetValueFromBag(bag, cc);
                element.parseEscapeSequences = m_ParseEscapeSequences.GetValueFromBag(bag, cc);
                element.selection.doubleClickSelectsWord = m_SelectWordByDoubleClick.GetValueFromBag(bag, cc);
                element.selection.tripleClickSelectsLine = m_SelectLineByTripleClick.GetValueFromBag(bag, cc);
                element.displayTooltipWhenElided = m_DisplayTooltipWhenElided.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class Button
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextElement.UxmlTraits
        {
            readonly UxmlImageAttributeDescription m_IconImage = new UxmlImageAttributeDescription { name = "icon-image" };

            public UxmlTraits() => focusable.defaultValue = true;

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((Button)ve).iconImage = m_IconImage.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class RepeatButton
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextElement.UxmlTraits
        {
            readonly UxmlLongAttributeDescription m_Delay = new UxmlLongAttributeDescription { name = "delay" };
            readonly UxmlLongAttributeDescription m_Interval = new UxmlLongAttributeDescription { name = "interval" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((RepeatButton)ve).SetAction(null, m_Delay.GetValueFromBag(bag, cc), m_Interval.GetValueFromBag(bag, cc));
            }
        }
    }

    public partial class Toggle
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseFieldTraits<bool, UxmlBoolAttributeDescription>
        {
            readonly UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((Toggle)ve).text = m_Text.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class RadioButton
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseFieldTraits<bool, UxmlBoolAttributeDescription>
        {
            readonly UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((RadioButton)ve).text = m_Text.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class RadioButtonGroup
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseFieldTraits<int, UxmlIntAttributeDescription>
        {
            readonly UxmlStringAttributeDescription m_Choices = new UxmlStringAttributeDescription { name = "choices" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((RadioButtonGroup)ve).choicesList = UxmlBuiltInTraits.ParseChoices(m_Choices.GetValueFromBag(bag, cc));
            }
        }
    }

    public partial class DropdownField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<string>.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_Index = new UxmlIntAttributeDescription { name = "index" };
            readonly UxmlStringAttributeDescription m_Choices = new UxmlStringAttributeDescription { name = "choices" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var field = (DropdownField)ve;
                var choices = UxmlBuiltInTraits.ParseChoices(m_Choices.GetValueFromBag(bag, cc));
                if (choices != null) field.choices = choices;
                field.index = m_Index.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class EnumField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Enum>.UxmlTraits
        {
            readonly UxmlTypeAttributeDescription<Enum> m_Type = new UxmlTypeAttributeDescription<Enum> { name = "type" };
            readonly UxmlStringAttributeDescription m_Value = new UxmlStringAttributeDescription { name = "value" };
            readonly UxmlBoolAttributeDescription m_IncludeObsoleteValues = new UxmlBoolAttributeDescription { name = "include-obsolete-values" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var field = (EnumField)ve;
                // A type initializes the field with the value named (exactly, as Enum.TryParse reads
                // it) or, without one, the type's zero value; a value the type does not define is
                // reported and leaves the field typed without a value; no type leaves it untyped.
                var type = m_Type.GetValueFromBag(bag, cc);
                if (type == null)
                {
                    field.SetTypeWithoutValue(null);
                    return;
                }
                string text = null;
                object parsed = null;
                if (m_Value.TryGetValueFromBag(bag, cc, ref text) && !Enum.TryParse(type, text, false, out parsed))
                {
                    Debug.LogErrorFormat("EnumField: Could not parse value of '{0}', because it isn't defined in the {1} enum.", text, type.FullName);
                    field.SetTypeWithoutValue(type);
                    return;
                }
                var value = (Enum)(text != null && parsed != null ? parsed : Enum.ToObject(type, 0));
                field.Init(value, m_IncludeObsoleteValues.GetValueFromBag(bag, cc));
            }
        }
    }

    public partial class Foldout
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BindableElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text" };
            readonly UxmlBoolAttributeDescription m_Value = new UxmlBoolAttributeDescription { name = "value", defaultValue = true };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (!(ve is Foldout foldout)) return;
                foldout.text = m_Text.GetValueFromBag(bag, cc);
                foldout.SetValueWithoutNotify(m_Value.GetValueFromBag(bag, cc));
            }
        }
    }

    public partial class GroupBox
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BindableElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((GroupBox)ve).text = m_Text.GetValueFromBag(bag, cc);
            }
        }
    }


    public partial class AbstractProgressBar
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BindableElement.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_LowValue = new UxmlFloatAttributeDescription { name = "low-value", defaultValue = 0 };
            readonly UxmlFloatAttributeDescription m_HighValue = new UxmlFloatAttributeDescription { name = "high-value", defaultValue = 100 };
            readonly UxmlFloatAttributeDescription m_Value = new UxmlFloatAttributeDescription { name = "value", defaultValue = 0 };
            readonly UxmlStringAttributeDescription m_Title = new UxmlStringAttributeDescription { name = "title" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (!(ve is AbstractProgressBar bar)) return;
                bar.lowValue = m_LowValue.GetValueFromBag(bag, cc);
                bar.highValue = m_HighValue.GetValueFromBag(bag, cc);
                bar.value = m_Value.GetValueFromBag(bag, cc);
                bar.title = m_Title.GetValueFromBag(bag, cc);
            }
        }
    }

    public abstract partial class BaseSlider<TValueType>
    {
        [Obsolete("UxmlTraits<TValueUxmlAttributeType> is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public partial class UxmlTraits<TValueUxmlAttributeType> : BaseFieldTraits<TValueType, TValueUxmlAttributeType>
            where TValueUxmlAttributeType : TypedUxmlAttributeDescription<TValueType>, new()
        {
            public UxmlTraits() => m_PickingMode.defaultValue = PickingMode.Ignore;
        }
    }

    /// <summary>An image attribute (Button and Tab `icon-image`): the referenced Texture, Sprite,
    /// RenderTexture or VectorImage as a Background, read as the builder reads the attribute.</summary>
    internal partial class UxmlImageAttributeDescription : UxmlAttributeDescription, IUxmlAssetAttributeDescription
    {
        public UxmlImageAttributeDescription()
        {
            type = "string";
            typeNamespace = xmlSchemaNamespace;
        }

        public Background defaultValue { get; set; }

        [Unbound.EditorFaithful("An image description's default is shown as null; Unity 6000.0.71f1, tests/projects/uxmllegacy (Button and Tab icon-image)")]
        public override string defaultValueAsString => "null";

        Type IUxmlAssetAttributeDescription.assetType => typeof(Object);

        public Background GetValueFromBag(IUxmlAttributes bag, CreationContext cc)
        {
            if (!TryGetValueFromBagAsString(bag, cc, out var text, out var source)) return defaultValue;
            return Background.FromObject(Unbound.UIElements.UxmlAttributeValues.ReadAsset(this, text, source, typeof(Object)));
        }
    }

    public partial class Slider
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseSlider<float>.UxmlTraits<UxmlFloatAttributeDescription>
        {
            readonly UxmlFloatAttributeDescription m_LowValue = new UxmlFloatAttributeDescription { name = "low-value" };
            readonly UxmlFloatAttributeDescription m_HighValue = new UxmlFloatAttributeDescription { name = "high-value", defaultValue = kDefaultHighValue };
            readonly UxmlFloatAttributeDescription m_PageSize = new UxmlFloatAttributeDescription { name = "page-size", defaultValue = Slider.kDefaultPageSize };
            readonly UxmlBoolAttributeDescription m_ShowInputField = new UxmlBoolAttributeDescription { name = "show-input-field", defaultValue = Slider.kDefaultShowInputField };
            readonly UxmlEnumAttributeDescription<SliderDirection> m_Direction = new UxmlEnumAttributeDescription<SliderDirection> { name = "direction", defaultValue = SliderDirection.Horizontal };
            readonly UxmlBoolAttributeDescription m_Inverted = new UxmlBoolAttributeDescription { name = "inverted", defaultValue = Slider.kDefaultInverted };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                var slider = (Slider)ve;
                slider.lowValue = m_LowValue.GetValueFromBag(bag, cc);
                slider.highValue = m_HighValue.GetValueFromBag(bag, cc);
                slider.direction = m_Direction.GetValueFromBag(bag, cc);
                slider.pageSize = m_PageSize.GetValueFromBag(bag, cc);
                slider.showInputField = m_ShowInputField.GetValueFromBag(bag, cc);
                slider.inverted = m_Inverted.GetValueFromBag(bag, cc);
                base.Init(ve, bag, cc);
            }
        }
    }

    public partial class SliderInt
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseSlider<int>.UxmlTraits<UxmlIntAttributeDescription>
        {
            readonly UxmlIntAttributeDescription m_LowValue = new UxmlIntAttributeDescription { name = "low-value" };
            readonly UxmlIntAttributeDescription m_HighValue = new UxmlIntAttributeDescription { name = "high-value", defaultValue = kDefaultHighValue };
            readonly UxmlIntAttributeDescription m_PageSize = new UxmlIntAttributeDescription { name = "page-size", defaultValue = (int)Slider.kDefaultPageSize };
            readonly UxmlBoolAttributeDescription m_ShowInputField = new UxmlBoolAttributeDescription { name = "show-input-field", defaultValue = Slider.kDefaultShowInputField };
            readonly UxmlEnumAttributeDescription<SliderDirection> m_Direction = new UxmlEnumAttributeDescription<SliderDirection> { name = "direction", defaultValue = SliderDirection.Horizontal };
            readonly UxmlBoolAttributeDescription m_Inverted = new UxmlBoolAttributeDescription { name = "inverted", defaultValue = Slider.kDefaultInverted };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                var slider = (SliderInt)ve;
                slider.lowValue = m_LowValue.GetValueFromBag(bag, cc);
                slider.highValue = m_HighValue.GetValueFromBag(bag, cc);
                slider.direction = m_Direction.GetValueFromBag(bag, cc);
                slider.pageSize = m_PageSize.GetValueFromBag(bag, cc);
                slider.showInputField = m_ShowInputField.GetValueFromBag(bag, cc);
                slider.inverted = m_Inverted.GetValueFromBag(bag, cc);
                base.Init(ve, bag, cc);
            }
        }
    }

    public partial class MinMaxSlider
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Vector2>.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_MinValue = new UxmlFloatAttributeDescription { name = "min-value", defaultValue = 0 };
            readonly UxmlFloatAttributeDescription m_MaxValue = new UxmlFloatAttributeDescription { name = "max-value", defaultValue = Slider.kDefaultHighValue };
            readonly UxmlFloatAttributeDescription m_LowLimit = new UxmlFloatAttributeDescription { name = "low-limit", defaultValue = float.MinValue };
            readonly UxmlFloatAttributeDescription m_HighLimit = new UxmlFloatAttributeDescription { name = "high-limit", defaultValue = float.MaxValue };

            public UxmlTraits() => m_PickingMode.defaultValue = PickingMode.Ignore;

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var slider = (MinMaxSlider)ve;
                slider.lowLimit = m_LowLimit.GetValueFromBag(bag, cc);
                slider.highLimit = m_HighLimit.GetValueFromBag(bag, cc);
                slider.value = new Vector2(m_MinValue.GetValueFromBag(bag, cc), m_MaxValue.GetValueFromBag(bag, cc));
            }
        }
    }

    public partial class Scroller
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_LowValue = new UxmlFloatAttributeDescription { name = "low-value", obsoleteNames = new[] { "lowValue" } };
            readonly UxmlFloatAttributeDescription m_HighValue = new UxmlFloatAttributeDescription { name = "high-value", obsoleteNames = new[] { "highValue" } };
            readonly UxmlEnumAttributeDescription<SliderDirection> m_Direction = new UxmlEnumAttributeDescription<SliderDirection> { name = "direction", defaultValue = SliderDirection.Vertical };
            readonly UxmlFloatAttributeDescription m_Value = new UxmlFloatAttributeDescription { name = "value" };

            public UxmlTraits() { }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var scroller = (Scroller)ve;
                scroller.lowValue = m_LowValue.GetValueFromBag(bag, cc);
                scroller.highValue = m_HighValue.GetValueFromBag(bag, cc);
                scroller.direction = m_Direction.GetValueFromBag(bag, cc);
                scroller.value = m_Value.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class ScrollView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlEnumAttributeDescription<ScrollViewMode> m_ScrollViewMode = new UxmlEnumAttributeDescription<ScrollViewMode> { name = "mode", defaultValue = ScrollViewMode.Vertical };
            readonly UxmlEnumAttributeDescription<NestedInteractionKind> m_NestedInteractionKind = new UxmlEnumAttributeDescription<NestedInteractionKind> { name = "nested-interaction-kind", defaultValue = NestedInteractionKind.Default };
            readonly UxmlBoolAttributeDescription m_ShowHorizontal = new UxmlBoolAttributeDescription { name = "show-horizontal-scroller" };
            readonly UxmlBoolAttributeDescription m_ShowVertical = new UxmlBoolAttributeDescription { name = "show-vertical-scroller" };
            readonly UxmlEnumAttributeDescription<ScrollerVisibility> m_HorizontalScrollerVisibility = new UxmlEnumAttributeDescription<ScrollerVisibility> { name = "horizontal-scroller-visibility" };
            readonly UxmlEnumAttributeDescription<ScrollerVisibility> m_VerticalScrollerVisibility = new UxmlEnumAttributeDescription<ScrollerVisibility> { name = "vertical-scroller-visibility" };
            readonly UxmlFloatAttributeDescription m_HorizontalPageSize = new UxmlFloatAttributeDescription { name = "horizontal-page-size", defaultValue = -1 };
            readonly UxmlFloatAttributeDescription m_VerticalPageSize = new UxmlFloatAttributeDescription { name = "vertical-page-size", defaultValue = -1 };
            readonly UxmlFloatAttributeDescription m_MouseWheelScrollSize = new UxmlFloatAttributeDescription { name = "mouse-wheel-scroll-size", defaultValue = 18 };
            readonly UxmlEnumAttributeDescription<TouchScrollBehavior> m_TouchScrollBehavior = new UxmlEnumAttributeDescription<TouchScrollBehavior> { name = "touch-scroll-type", defaultValue = TouchScrollBehavior.Clamped };
            readonly UxmlFloatAttributeDescription m_ScrollDecelerationRate = new UxmlFloatAttributeDescription { name = "scroll-deceleration-rate", defaultValue = 0.135f };
            readonly UxmlFloatAttributeDescription m_Elasticity = new UxmlFloatAttributeDescription { name = "elasticity", defaultValue = 0.1f };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = (ScrollView)ve;
                view.mode = m_ScrollViewMode.GetValueFromBag(bag, cc);
                var visibility = ScrollerVisibility.Auto;
                if (m_HorizontalScrollerVisibility.TryGetValueFromBag(bag, cc, ref visibility)) view.horizontalScrollerVisibility = visibility;
                else view.showHorizontal = m_ShowHorizontal.GetValueFromBag(bag, cc);
                visibility = ScrollerVisibility.Auto;
                if (m_VerticalScrollerVisibility.TryGetValueFromBag(bag, cc, ref visibility)) view.verticalScrollerVisibility = visibility;
                else view.showVertical = m_ShowVertical.GetValueFromBag(bag, cc);
                view.nestedInteractionKind = m_NestedInteractionKind.GetValueFromBag(bag, cc);
                view.horizontalPageSize = m_HorizontalPageSize.GetValueFromBag(bag, cc);
                view.verticalPageSize = m_VerticalPageSize.GetValueFromBag(bag, cc);
                view.mouseWheelScrollSize = m_MouseWheelScrollSize.GetValueFromBag(bag, cc);
                view.scrollDecelerationRate = m_ScrollDecelerationRate.GetValueFromBag(bag, cc);
                view.touchScrollBehavior = m_TouchScrollBehavior.GetValueFromBag(bag, cc);
                view.elasticity = m_Elasticity.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class BaseVerticalCollectionView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BindableElement.UxmlTraits
        {
            readonly UxmlEnumAttributeDescription<CollectionVirtualizationMethod> m_VirtualizationMethod = new UxmlEnumAttributeDescription<CollectionVirtualizationMethod> { name = "virtualization-method", defaultValue = CollectionVirtualizationMethod.FixedHeight };
            readonly UxmlIntAttributeDescription m_FixedItemHeight = new UxmlIntAttributeDescription { name = "fixed-item-height", obsoleteNames = new[] { "itemHeight", "item-height" }, defaultValue = s_DefaultItemHeight };
            readonly UxmlBoolAttributeDescription m_ShowBorder = new UxmlBoolAttributeDescription { name = "show-border" };
            readonly UxmlEnumAttributeDescription<SelectionType> m_SelectionType = new UxmlEnumAttributeDescription<SelectionType> { name = "selection-type", defaultValue = SelectionType.Single };
            readonly UxmlEnumAttributeDescription<AlternatingRowBackground> m_ShowAlternatingRowBackgrounds = new UxmlEnumAttributeDescription<AlternatingRowBackground> { name = "show-alternating-row-backgrounds", defaultValue = AlternatingRowBackground.None };
            readonly UxmlBoolAttributeDescription m_Reorderable = new UxmlBoolAttributeDescription { name = "reorderable" };
            readonly UxmlBoolAttributeDescription m_HorizontalScrollingEnabled = new UxmlBoolAttributeDescription { name = "horizontal-scrolling" };

            public UxmlTraits() => focusable.defaultValue = true;

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = (BaseVerticalCollectionView)ve;
                view.reorderable = m_Reorderable.GetValueFromBag(bag, cc);
                int height = 0;
                if (m_FixedItemHeight.TryGetValueFromBag(bag, cc, ref height)) view.fixedItemHeight = height;
                view.virtualizationMethod = m_VirtualizationMethod.GetValueFromBag(bag, cc);
                view.showBorder = m_ShowBorder.GetValueFromBag(bag, cc);
                view.selectionType = m_SelectionType.GetValueFromBag(bag, cc);
                view.showAlternatingRowBackgrounds = m_ShowAlternatingRowBackgrounds.GetValueFromBag(bag, cc);
                view.horizontalScrollingEnabled = m_HorizontalScrollingEnabled.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class BaseListView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseVerticalCollectionView.UxmlTraits
        {
            readonly UxmlBoolAttributeDescription m_ShowFoldoutHeader = new UxmlBoolAttributeDescription { name = "show-foldout-header" };
            readonly UxmlStringAttributeDescription m_HeaderTitle = new UxmlStringAttributeDescription { name = "header-title" };
            readonly UxmlBoolAttributeDescription m_ShowAddRemoveFooter = new UxmlBoolAttributeDescription { name = "show-add-remove-footer" };
            readonly UxmlBoolAttributeDescription m_AllowAdd = new UxmlBoolAttributeDescription { name = "allow-add", defaultValue = true };
            readonly UxmlBoolAttributeDescription m_AllowRemove = new UxmlBoolAttributeDescription { name = "allow-remove", defaultValue = true };
            readonly UxmlEnumAttributeDescription<ListViewReorderMode> m_ReorderMode = new UxmlEnumAttributeDescription<ListViewReorderMode> { name = "reorder-mode", defaultValue = ListViewReorderMode.Simple };
            readonly UxmlBoolAttributeDescription m_ShowBoundCollectionSize = new UxmlBoolAttributeDescription { name = "show-bound-collection-size", defaultValue = true };
            readonly UxmlEnumAttributeDescription<BindingSourceSelectionMode> m_BindingSourceSelectionMode = new UxmlEnumAttributeDescription<BindingSourceSelectionMode> { name = "binding-source-selection-mode", defaultValue = BindingSourceSelectionMode.Manual };

            protected UxmlTraits() => m_PickingMode.defaultValue = PickingMode.Ignore;

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = (BaseListView)ve;
                view.reorderMode = m_ReorderMode.GetValueFromBag(bag, cc);
                view.showFoldoutHeader = m_ShowFoldoutHeader.GetValueFromBag(bag, cc);
                view.headerTitle = m_HeaderTitle.GetValueFromBag(bag, cc);
                view.showAddRemoveFooter = m_ShowAddRemoveFooter.GetValueFromBag(bag, cc);
                view.allowAdd = m_AllowAdd.GetValueFromBag(bag, cc);
                view.allowRemove = m_AllowRemove.GetValueFromBag(bag, cc);
                view.showBoundCollectionSize = m_ShowBoundCollectionSize.GetValueFromBag(bag, cc);
                view.bindingSourceSelectionMode = m_BindingSourceSelectionMode.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class ListView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseListView.UxmlTraits
        {
            readonly UxmlAssetAttributeDescription<VisualTreeAsset> m_ItemTemplate = new UxmlAssetAttributeDescription<VisualTreeAsset> { name = "item-template" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is ListView view && m_ItemTemplate.TryGetValueFromBag(bag, cc, out var template)) view.itemTemplate = template;
            }
        }
    }

    public partial class BaseTreeView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseVerticalCollectionView.UxmlTraits
        {
            readonly UxmlBoolAttributeDescription m_AutoExpand = new UxmlBoolAttributeDescription { name = "auto-expand" };

            public UxmlTraits() { }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((BaseTreeView)ve).autoExpand = m_AutoExpand.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class TreeView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseTreeView.UxmlTraits
        {
            readonly UxmlAssetAttributeDescription<VisualTreeAsset> m_ItemTemplate = new UxmlAssetAttributeDescription<VisualTreeAsset> { name = "item-template" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is TreeView view && m_ItemTemplate.TryGetValueFromBag(bag, cc, out var template)) view.itemTemplate = template;
            }
        }
    }

    public partial class MultiColumnListView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseListView.UxmlTraits
        {
            readonly UxmlEnumAttributeDescription<ColumnSortingMode> m_SortingMode = UxmlBuiltInTraits.SortingMode();

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = (MultiColumnListView)ve;
                if (UxmlBuiltInTraits.TryReadSortingMode(m_SortingMode, bag, cc, out var mode)) view.sortingMode = mode;
                // Columns and sort descriptions declared as UXML objects are not read (unsupported, as for
                // built-in UXML): both are set to none, which clears them, as for a document declaring none.
                view.sortColumnDescriptions = null;
                view.columns = null;
            }
        }
    }

    public partial class MultiColumnTreeView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseTreeView.UxmlTraits
        {
            readonly UxmlEnumAttributeDescription<ColumnSortingMode> m_SortingMode = UxmlBuiltInTraits.SortingMode();

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = (MultiColumnTreeView)ve;
                if (UxmlBuiltInTraits.TryReadSortingMode(m_SortingMode, bag, cc, out var mode)) view.sortingMode = mode;
                view.sortColumnDescriptions = null;
                view.columns = null;
            }
        }
    }

    public partial class Tab
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_Label = new UxmlStringAttributeDescription { name = "label" };
            readonly UxmlImageAttributeDescription m_IconImage = new UxmlImageAttributeDescription { name = "icon-image" };
            readonly UxmlBoolAttributeDescription m_Closeable = new UxmlBoolAttributeDescription { name = "closeable" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var tab = (Tab)ve;
                tab.label = m_Label.GetValueFromBag(bag, cc);
                tab.iconImage = m_IconImage.GetValueFromBag(bag, cc);
                tab.closeable = m_Closeable.GetValueFromBag(bag, cc);
            }
        }
    }

    public partial class TabView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlBoolAttributeDescription m_Reorderable = new UxmlBoolAttributeDescription { name = "reorderable" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((TabView)ve).reorderable = m_Reorderable.GetValueFromBag(bag, cc);
            }
        }
    }



    // Composite fields: the components named by the traits make the whole value (missing ones 0).
    public partial class Vector2Field
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Vector2>.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_XValue = new UxmlFloatAttributeDescription { name = "x" };
            readonly UxmlFloatAttributeDescription m_YValue = new UxmlFloatAttributeDescription { name = "y" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((Vector2Field)ve).SetValueWithoutNotify(new Vector2(m_XValue.GetValueFromBag(bag, cc), m_YValue.GetValueFromBag(bag, cc)));
            }
        }
    }

    public partial class Vector3Field
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Vector3>.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_XValue = new UxmlFloatAttributeDescription { name = "x" };
            readonly UxmlFloatAttributeDescription m_YValue = new UxmlFloatAttributeDescription { name = "y" };
            readonly UxmlFloatAttributeDescription m_ZValue = new UxmlFloatAttributeDescription { name = "z" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((Vector3Field)ve).SetValueWithoutNotify(new Vector3(m_XValue.GetValueFromBag(bag, cc), m_YValue.GetValueFromBag(bag, cc), m_ZValue.GetValueFromBag(bag, cc)));
            }
        }
    }

    public partial class Vector4Field
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Vector4>.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_XValue = new UxmlFloatAttributeDescription { name = "x" };
            readonly UxmlFloatAttributeDescription m_YValue = new UxmlFloatAttributeDescription { name = "y" };
            readonly UxmlFloatAttributeDescription m_ZValue = new UxmlFloatAttributeDescription { name = "z" };
            readonly UxmlFloatAttributeDescription m_WValue = new UxmlFloatAttributeDescription { name = "w" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((Vector4Field)ve).SetValueWithoutNotify(new Vector4(m_XValue.GetValueFromBag(bag, cc), m_YValue.GetValueFromBag(bag, cc), m_ZValue.GetValueFromBag(bag, cc), m_WValue.GetValueFromBag(bag, cc)));
            }
        }
    }

    public partial class Vector2IntField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Vector2Int>.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_XValue = new UxmlIntAttributeDescription { name = "x" };
            readonly UxmlIntAttributeDescription m_YValue = new UxmlIntAttributeDescription { name = "y" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((Vector2IntField)ve).SetValueWithoutNotify(new Vector2Int(m_XValue.GetValueFromBag(bag, cc), m_YValue.GetValueFromBag(bag, cc)));
            }
        }
    }

    public partial class Vector3IntField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Vector3Int>.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_XValue = new UxmlIntAttributeDescription { name = "x" };
            readonly UxmlIntAttributeDescription m_YValue = new UxmlIntAttributeDescription { name = "y" };
            readonly UxmlIntAttributeDescription m_ZValue = new UxmlIntAttributeDescription { name = "z" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((Vector3IntField)ve).SetValueWithoutNotify(new Vector3Int(m_XValue.GetValueFromBag(bag, cc), m_YValue.GetValueFromBag(bag, cc), m_ZValue.GetValueFromBag(bag, cc)));
            }
        }
    }

    public partial class RectField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Rect>.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_XValue = new UxmlFloatAttributeDescription { name = "x" };
            readonly UxmlFloatAttributeDescription m_YValue = new UxmlFloatAttributeDescription { name = "y" };
            readonly UxmlFloatAttributeDescription m_WValue = new UxmlFloatAttributeDescription { name = "w" };
            readonly UxmlFloatAttributeDescription m_HValue = new UxmlFloatAttributeDescription { name = "h" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((RectField)ve).SetValueWithoutNotify(new Rect(m_XValue.GetValueFromBag(bag, cc), m_YValue.GetValueFromBag(bag, cc), m_WValue.GetValueFromBag(bag, cc), m_HValue.GetValueFromBag(bag, cc)));
            }
        }
    }

    public partial class RectIntField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<RectInt>.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_XValue = new UxmlIntAttributeDescription { name = "x" };
            readonly UxmlIntAttributeDescription m_YValue = new UxmlIntAttributeDescription { name = "y" };
            readonly UxmlIntAttributeDescription m_WValue = new UxmlIntAttributeDescription { name = "w" };
            readonly UxmlIntAttributeDescription m_HValue = new UxmlIntAttributeDescription { name = "h" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((RectIntField)ve).SetValueWithoutNotify(new RectInt(m_XValue.GetValueFromBag(bag, cc), m_YValue.GetValueFromBag(bag, cc), m_WValue.GetValueFromBag(bag, cc), m_HValue.GetValueFromBag(bag, cc)));
            }
        }
    }

    public partial class BoundsField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Bounds>.UxmlTraits
        {
            readonly UxmlFloatAttributeDescription m_CenterXValue = new UxmlFloatAttributeDescription { name = "cx" };
            readonly UxmlFloatAttributeDescription m_CenterYValue = new UxmlFloatAttributeDescription { name = "cy" };
            readonly UxmlFloatAttributeDescription m_CenterZValue = new UxmlFloatAttributeDescription { name = "cz" };
            readonly UxmlFloatAttributeDescription m_ExtentsXValue = new UxmlFloatAttributeDescription { name = "ex" };
            readonly UxmlFloatAttributeDescription m_ExtentsYValue = new UxmlFloatAttributeDescription { name = "ey" };
            readonly UxmlFloatAttributeDescription m_ExtentsZValue = new UxmlFloatAttributeDescription { name = "ez" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var center = new Vector3(m_CenterXValue.GetValueFromBag(bag, cc), m_CenterYValue.GetValueFromBag(bag, cc), m_CenterZValue.GetValueFromBag(bag, cc));
                var extents = new Vector3(m_ExtentsXValue.GetValueFromBag(bag, cc), m_ExtentsYValue.GetValueFromBag(bag, cc), m_ExtentsZValue.GetValueFromBag(bag, cc));
                ((BoundsField)ve).SetValueWithoutNotify(new Bounds(center, extents));
            }
        }
    }

    public partial class BoundsIntField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<BoundsInt>.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_PositionXValue = new UxmlIntAttributeDescription { name = "px" };
            readonly UxmlIntAttributeDescription m_PositionYValue = new UxmlIntAttributeDescription { name = "py" };
            readonly UxmlIntAttributeDescription m_PositionZValue = new UxmlIntAttributeDescription { name = "pz" };
            readonly UxmlIntAttributeDescription m_SizeXValue = new UxmlIntAttributeDescription { name = "sx" };
            readonly UxmlIntAttributeDescription m_SizeYValue = new UxmlIntAttributeDescription { name = "sy" };
            readonly UxmlIntAttributeDescription m_SizeZValue = new UxmlIntAttributeDescription { name = "sz" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var position = new Vector3Int(m_PositionXValue.GetValueFromBag(bag, cc), m_PositionYValue.GetValueFromBag(bag, cc), m_PositionZValue.GetValueFromBag(bag, cc));
                var size = new Vector3Int(m_SizeXValue.GetValueFromBag(bag, cc), m_SizeYValue.GetValueFromBag(bag, cc), m_SizeZValue.GetValueFromBag(bag, cc));
                ((BoundsIntField)ve).SetValueWithoutNotify(new BoundsInt(position, size));
            }
        }
    }

    public partial class TemplateContainer
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BindableElement.UxmlTraits
        {
            internal const string k_TemplateAttributeName = "template";

            readonly UxmlStringAttributeDescription m_Template = new UxmlStringAttributeDescription { name = k_TemplateAttributeName, use = UxmlAttributeDescription.Use.Required };

            public UxmlTraits() { }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var container = (TemplateContainer)ve;
                container.SetTemplateId(m_Template.GetValueFromBag(bag, cc));
                var template = cc.visualTreeAsset != null ? cc.visualTreeAsset.ResolveTemplate(container.templateId) : null;
                if (template == null)
                {
                    container.Add(new Label($"Unknown Template: '{container.templateId}'"));
                    Debug.LogErrorFormat("Could not resolve template with name '{0}'", container.templateId);
                    return;
                }
                container.templateSource = template;
                // The template gets the enclosing instances' overrides and slots, in a clone that cannot
                // hold every element (UxmlCloneScope.FromDeprecatedTraits).
                var scope = cc.uaScope ?? Unbound.UIElements.UxmlCloneScope.Root();
                template.CloneTree(container, scope.ForDeprecatedTraits(), out _, out _);
            }
        }
    }

    public partial class Label
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextElement.UxmlTraits
        {
            public UxmlTraits() { }
        }
    }

    public partial class Image
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            public UxmlTraits() { }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }
        }
    }

    public partial class IMGUIContainer
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            public UxmlTraits()
            {
                focusIndex.defaultValue = 0;
                focusable.defaultValue = true;
            }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }
        }
    }

    public partial class IntegerField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextValueFieldTraits<int, UxmlIntAttributeDescription>
        {
            public UxmlTraits() { }
        }
    }

    public partial class LongField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextValueFieldTraits<long, UxmlLongAttributeDescription>
        {
            public UxmlTraits() { }
        }
    }

    public partial class FloatField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextValueFieldTraits<float, UxmlFloatAttributeDescription>
        {
            public UxmlTraits() { }
        }
    }

    public partial class DoubleField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextValueFieldTraits<double, UxmlDoubleAttributeDescription>
        {
            public UxmlTraits() { }
        }
    }

    public partial class UnsignedIntegerField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextValueFieldTraits<uint, UxmlUnsignedIntAttributeDescription>
        {
            public UxmlTraits() { }
        }
    }

    public partial class UnsignedLongField
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextValueFieldTraits<ulong, UxmlUnsignedLongAttributeDescription>
        {
            public UxmlTraits() { }
        }
    }

    public partial class Hash128Field
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextValueFieldTraits<Hash128, UxmlHash128AttributeDescription>
        {
            public UxmlTraits() { }
        }
    }

    // The traits of a document's own elements (root, style, template, attribute overrides): their
    // descriptions as the Editor lists them; Unbound's builder reads those elements itself.
    [Obsolete("UxmlRootElementTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
    public partial class UxmlRootElementTraits : UxmlTraits
    {
        protected UxmlStringAttributeDescription m_Name = new UxmlStringAttributeDescription { name = "name" };
        readonly UxmlStringAttributeDescription m_Class = new UxmlStringAttributeDescription { name = "class" };

        public UxmlRootElementTraits() { }

        public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
        {
            get { yield return new UxmlChildElementDescription(typeof(VisualElement)); }
        }
    }

    [Obsolete("UxmlStyleTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
    public partial class UxmlStyleTraits : UxmlTraits
    {
        readonly UxmlStringAttributeDescription m_Name = new UxmlStringAttributeDescription { name = "name" };
        readonly UxmlStringAttributeDescription m_Path = new UxmlStringAttributeDescription { name = "path" };
        readonly UxmlStringAttributeDescription m_Src = new UxmlStringAttributeDescription { name = "src" };

        public UxmlStyleTraits() { }

        public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }
    }

    [Obsolete("UxmlTemplateTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
    public partial class UxmlTemplateTraits : UxmlTraits
    {
        readonly UxmlStringAttributeDescription m_Name = new UxmlStringAttributeDescription { name = "name", use = UxmlAttributeDescription.Use.Required };
        readonly UxmlStringAttributeDescription m_Path = new UxmlStringAttributeDescription { name = "path" };
        readonly UxmlStringAttributeDescription m_Src = new UxmlStringAttributeDescription { name = "src" };

        public UxmlTemplateTraits() { }

        public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }
    }

    [Obsolete("UxmlAttributeOverridesTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
    public partial class UxmlAttributeOverridesTraits : UxmlTraits
    {
        internal const string k_ElementNameAttributeName = "element-name";

        readonly UxmlStringAttributeDescription m_ElementName = new UxmlStringAttributeDescription { name = k_ElementNameAttributeName, use = UxmlAttributeDescription.Use.Required };

        public UxmlAttributeOverridesTraits() { }

        public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get { yield break; } }
    }


    // The built-in factories (their constructors), as game factories may derive from them.
    public partial class BoundsField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<BoundsField, BoundsField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class BoundsIntField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<BoundsIntField, BoundsIntField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Box
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Box>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Button
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Button, Button.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class DoubleField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<DoubleField, DoubleField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class DropdownField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<DropdownField, DropdownField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class EnumField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<EnumField, EnumField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class FloatField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<FloatField, FloatField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Foldout
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Foldout, Foldout.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class GroupBox
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<GroupBox, GroupBox.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Hash128Field
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Hash128Field, Hash128Field.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class IMGUIContainer
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<IMGUIContainer, IMGUIContainer.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Image
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Image, Image.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class IntegerField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<IntegerField, IntegerField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Label
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Label, Label.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class ListView
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ListView, ListView.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class LongField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<LongField, LongField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class MinMaxSlider
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<MinMaxSlider, MinMaxSlider.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class MultiColumnListView
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<MultiColumnListView, MultiColumnListView.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class MultiColumnTreeView
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<MultiColumnTreeView, MultiColumnTreeView.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class ProgressBar
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ProgressBar, AbstractProgressBar.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class RadioButton
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<RadioButton, RadioButton.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class RadioButtonGroup
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<RadioButtonGroup, RadioButtonGroup.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class RectField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<RectField, RectField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class RectIntField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<RectIntField, RectIntField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class RepeatButton
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<RepeatButton, RepeatButton.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class ScrollView
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ScrollView, ScrollView.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Scroller
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Scroller, Scroller.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Slider
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Slider, Slider.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class SliderInt
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<SliderInt, SliderInt.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Tab
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Tab, Tab.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class TabView
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<TabView, TabView.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class TemplateContainer
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<TemplateContainer, TemplateContainer.UxmlTraits>
        {
            public UxmlFactory() { }
            internal const string k_ElementName = "Instance";

            public override string uxmlName => k_ElementName;

            public override string uxmlQualifiedName => typeof(TemplateContainer).Namespace + "." + uxmlName;

        }
    }

    public partial class TextElement
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<TextElement, TextElement.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class TextField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<TextField, TextField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Toggle
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Toggle, Toggle.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class TreeView
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<TreeView, TreeView.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class UnsignedIntegerField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<UnsignedIntegerField, UnsignedIntegerField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class UnsignedLongField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<UnsignedLongField, UnsignedLongField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Vector2Field
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Vector2Field, Vector2Field.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Vector2IntField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Vector2IntField, Vector2IntField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Vector3Field
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Vector3Field, Vector3Field.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Vector3IntField
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Vector3IntField, Vector3IntField.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class Vector4Field
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Vector4Field, Vector4Field.UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class HelpBox
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text" };
            readonly UxmlEnumAttributeDescription<HelpBoxMessageType> m_MessageType = new UxmlEnumAttributeDescription<HelpBoxMessageType> { name = "message-type", defaultValue = HelpBoxMessageType.None };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var helpBox = ve as HelpBox;
                helpBox.text = m_Text.GetValueFromBag(bag, cc);
                helpBox.messageType = m_MessageType.GetValueFromBag(bag, cc);
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<HelpBox, UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class PopupWindow
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextElement.UxmlTraits
        {
            public UxmlTraits() { }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield return new UxmlChildElementDescription(typeof(VisualElement)); }
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<PopupWindow, UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class ToggleButtonGroup
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<ToggleButtonGroupState>.UxmlTraits
        {
            readonly UxmlBoolAttributeDescription m_IsMultipleSelection = new UxmlBoolAttributeDescription { name = "is-multiple-selection" };
            readonly UxmlBoolAttributeDescription m_AllowEmptySelection = new UxmlBoolAttributeDescription { name = "allow-empty-selection" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var group = (ToggleButtonGroup)ve;
                group.isMultipleSelection = m_IsMultipleSelection.GetValueFromBag(bag, cc);
                group.allowEmptySelection = m_AllowEmptySelection.GetValueFromBag(bag, cc);
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToggleButtonGroup, UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    public partial class TwoPaneSplitView
    {
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_FixedPaneIndex = new UxmlIntAttributeDescription { name = "fixed-pane-index", defaultValue = 0 };
            readonly UxmlIntAttributeDescription m_FixedPaneInitialDimension = new UxmlIntAttributeDescription { name = "fixed-pane-initial-dimension", defaultValue = 100 };
            readonly UxmlEnumAttributeDescription<TwoPaneSplitViewOrientation> m_Orientation = new UxmlEnumAttributeDescription<TwoPaneSplitViewOrientation> { name = "orientation", defaultValue = TwoPaneSplitViewOrientation.Horizontal };

            public UxmlTraits() { }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            // The initial dimension is read as an integer here, unlike the float property.
            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                int fixedPaneIndex = m_FixedPaneIndex.GetValueFromBag(bag, cc);
                int fixedPaneInitialDimension = m_FixedPaneInitialDimension.GetValueFromBag(bag, cc);
                var orientation = m_Orientation.GetValueFromBag(bag, cc);
                ((TwoPaneSplitView)ve).Init(fixedPaneIndex, fixedPaneInitialDimension, orientation);
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<TwoPaneSplitView, UxmlTraits>
        {
            public UxmlFactory() { }
        }
    }

    /// <summary>Readers the built-in traits share.</summary>
    static class UxmlBuiltInTraits
    {
        /// <summary>A comma-separated list of choices, each trimmed; null for an empty or blank text.</summary>
        internal static List<string> ParseChoices(string text)
        {
            if (string.IsNullOrEmpty(text?.Trim())) return null;
            var choices = new List<string>();
            foreach (var choice in text.Split(',')) choices.Add(choice.Trim());
            return choices;
        }

        internal static UxmlEnumAttributeDescription<ColumnSortingMode> SortingMode() =>
            new UxmlEnumAttributeDescription<ColumnSortingMode> { name = "sorting-mode", obsoleteNames = new[] { "sorting-enabled" }, defaultValue = ColumnSortingMode.None };

        /// <summary>The sorting mode when the attribute is present (an absent one leaves the view's);
        /// a boolean, the former attribute's form, reads true as Custom and false as None.</summary>
        internal static bool TryReadSortingMode(UxmlEnumAttributeDescription<ColumnSortingMode> description, IUxmlAttributes bag, CreationContext cc, out ColumnSortingMode mode)
        {
            mode = ColumnSortingMode.None;
            if (!description.TryGetValueFromBagAsString(bag, cc, out var text)) return false;
            mode = bool.TryParse(text, out var enabled) ? enabled ? ColumnSortingMode.Custom : ColumnSortingMode.None : description.GetValueFromBag(bag, cc);
            return true;
        }
    }
}
