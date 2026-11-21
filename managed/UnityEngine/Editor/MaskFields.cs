using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit MaskField, LayerMaskField, and RenderingLayerMaskField for LayerMask and
// RenderingLayerMask SerializedProperty binding.
// Documented in Unity 6000.0 Scripting API ("MaskField", "LayerMaskField", "RenderingLayerMaskField");
// matches the Unity 6000.0.71f1 Editor, tests/projects/maskbinding.

namespace UnityEditor.UIElements
{
    public partial class MaskField : BaseMaskField<int>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<int>.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField] List<string> choices;
            [HideInInspector] [SerializeField] [UxmlIgnore] UxmlAttributeFlags choices_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<int>.UxmlSerializedData.Register();
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames("choices", "choices", null),
                });
            }

            public override object CreateInstance() => new MaskField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                if (ShouldWriteAttributeValue(choices_UxmlAttributeFlags) && choices != null)
                    ((MaskField)obj).choices = choices;
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<MaskField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<int>.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_MaskChoices = new UxmlStringAttributeDescription { name = "choices" };
            readonly UxmlIntAttributeDescription m_MaskValue = new UxmlIntAttributeDescription { name = "value" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                var maskField = (MaskField)ve;
                var list = UxmlUtility.ParseStringListAttribute(m_MaskChoices.GetValueFromBag(bag, cc));
                if (list != null && list.Count > 0) maskField.choices = list;
                maskField.SetValueWithoutNotify(m_MaskValue.GetValueFromBag(bag, cc));
                base.Init(ve, bag, cc);
            }
        }

        public new static readonly string ussClassName = "unity-mask-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        public virtual Func<string, string> formatSelectedValueCallback
        {
            get => m_FormatSelectedValueCallback;
            set
            {
                m_FormatSelectedValueCallback = value;
                textElement.text = GetValueToDisplay();
            }
        }

        public virtual Func<string, string> formatListItemCallback
        {
            get => m_FormatListItemCallback;
            set => m_FormatListItemCallback = value;
        }

        internal override int MaskToValue(int newMask) => newMask;

        internal override int ValueToMask(int value) => value;

        internal override string GetListItemToDisplay(int itemIndex)
        {
            string text = GetDisplayedValue(itemIndex);
            if (ShouldFormatListItem(itemIndex)) text = m_FormatListItemCallback(text);
            return text;
        }

        internal override string GetValueToDisplay()
        {
            string text = showMixedValue ? mixedValueString : GetDisplayedValue(rawValue);
            if (ShouldFormatSelectedValue()) text = m_FormatSelectedValueCallback(text);
            return text;
        }

        internal bool ShouldFormatListItem(int itemIndex) =>
            itemIndex != 0 && itemIndex != -1 && m_FormatListItemCallback != null;

        internal bool ShouldFormatSelectedValue() =>
            rawValue != 0 && rawValue != -1 && m_FormatSelectedValueCallback != null && IsPowerOf2(rawValue);

        public MaskField(List<string> choices, int defaultMask, Func<string, string> formatSelectedValueCallback = null, Func<string, string> formatListItemCallback = null)
            : this(null, choices, defaultMask, formatSelectedValueCallback, formatListItemCallback) { }

        public MaskField(string label, List<string> choices, int defaultMask, Func<string, string> formatSelectedValueCallback = null, Func<string, string> formatListItemCallback = null)
            : this(label)
        {
            this.choices = choices;
            m_FormatListItemCallback = formatListItemCallback;
            m_FormatSelectedValueCallback = formatSelectedValueCallback;
            SetValueWithoutNotify(defaultMask);
            this.formatListItemCallback = formatListItemCallback;
            this.formatSelectedValueCallback = formatSelectedValueCallback;
        }

        public MaskField() : this(null) { }

        public MaskField(string label) : base(label)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    public partial class LayerMaskField : MaskField
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : MaskField.UxmlSerializedData
        {
#pragma warning disable 649
            [UxmlAttribute("value")] [SerializeField] LayerMask layerMask;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags layerMask_UxmlAttributeFlags;
            [UxmlAttribute("choices")] [SerializeField] [HideInInspector] List<string> layerChoices;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags layerChoices_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames("layerMask", "value", null),
                    new UxmlAttributeNames("layerChoices", "choices", null),
                });
            }

            public override object CreateInstance() => new LayerMaskField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                var field = (LayerMaskField)obj;
                if (ShouldWriteAttributeValue(layerMask_UxmlAttributeFlags))
                    field.SetValueWithoutNotify(layerMask.value);
                if (ShouldWriteAttributeValue(layerChoices_UxmlAttributeFlags))
                    field.layerChoices = layerChoices;
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<LayerMaskField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<int>.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_MaskValue = new UxmlIntAttributeDescription { name = "value" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                ((LayerMaskField)ve).SetValueWithoutNotify(m_MaskValue.GetValueFromBag(bag, cc));
                base.Init(ve, bag, cc);
            }
        }

        public new static readonly string ussClassName = "unity-layer-mask-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        internal List<string> layerChoices
        {
            get => choices;
            set
            {
                if (value != null) choices = value;
            }
        }

        internal LayerMask layerMask
        {
            get => value;
            set => this.value = value.value;
        }

        public override Func<string, string> formatSelectedValueCallback
        {
            get => null;
            set
            {
                if (value != null)
                    UnityEngine.Debug.LogWarning(L10n.Tr("LayerMaskField doesn't support the formatting of the selected value."));
                m_FormatSelectedValueCallback = null;
            }
        }

        public override Func<string, string> formatListItemCallback
        {
            get => null;
            set
            {
                if (value != null)
                    UnityEngine.Debug.LogWarning(L10n.Tr("LayerMaskField doesn't support the formatting of the list items."));
                m_FormatListItemCallback = null;
            }
        }

        void UpdateLayersInfo()
        {
            string[] layerNames = null;
            int[] layerValues = null;
            TagManager.GetDefinedLayers(ref layerNames, ref layerValues);
            choices = new List<string>(layerNames);
            choicesMasks = new List<int>(layerValues);
        }

        public LayerMaskField(int defaultMask) : this(null, defaultMask) { }

        public LayerMaskField(string label, int defaultMask) : this(label) => SetValueWithoutNotify(defaultMask);

        public LayerMaskField() : this(null) { }

        public LayerMaskField(string label) : base(label)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            UpdateLayersInfo();
        }

        internal override void AddMenuItems(IGenericMenu menu)
        {
            UpdateLayersInfo();
            base.AddMenuItems(menu);
        }
    }

    public partial class RenderingLayerMaskField : BaseMaskField<uint>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<uint>.UxmlSerializedData
        {
#pragma warning disable 649
            [UxmlAttribute("value")] [SerializeField] RenderingLayerMask layerMask;
            [UxmlIgnore] [SerializeField] [HideInInspector] UxmlAttributeFlags layerMask_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [RegisterUxmlCache]
            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<uint>.UxmlSerializedData.Register();
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames("layerMask", "value", null),
                });
            }

            public override object CreateInstance() => new RenderingLayerMaskField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                if (ShouldWriteAttributeValue(layerMask_UxmlAttributeFlags))
                    ((RenderingLayerMaskField)obj).layerMask = layerMask;
            }
        }

        public new static readonly string ussClassName = "unity-rendering-layer-mask-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        readonly HelpBox m_HelpBox;
        readonly List<string> m_RenderingLayersChoices = new List<string>();
        readonly List<int> m_RenderingLayersChoicesMasks = new List<int>();

        internal RenderingLayerMask layerMask
        {
            get => value;
            set => this.value = value;
        }

        public override uint value
        {
            get => rawValue;
            set
            {
                if (CheckIfOnlyOneLayerWasDeselectedFromEverything(value))
                {
                    uint mask = ~value;
                    if (BitOperationUtils.CountBits(mask) == 1)
                    {
                        int activeMax = RenderPipelineEditorUtility.GetActiveMaxRenderingLayers();
                        int[] definedValues = RenderingLayerMask.GetDefinedRenderingLayerValues();
                        value = BitOperationUtils.ModifyMaskByValuesArrayAndBitCount(value, definedValues, activeMax);
                    }
                }
                UpdateChoices(value);
                base.value = value;
            }
        }

        public RenderingLayerMaskField(uint defaultMask) : this(null, defaultMask) { }

        public RenderingLayerMaskField(string label) : this(label, RenderingLayerMask.defaultRenderingLayerMask) { }

        public RenderingLayerMaskField() : this(null) { }

        public RenderingLayerMaskField(string label, uint defaultMask) : base(label)
        {
            style.flexWrap = Wrap.Wrap;
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);

            m_HelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            Add(m_HelpBox);

            UpdateChoices(defaultMask);
            SetValueWithoutNotify(defaultMask);

            RegisterCallback<AttachToPanelEvent>(_ => TagManager.onRenderingLayersChanged += OnRenderingLayersChanged);
            RegisterCallback<DetachFromPanelEvent>(_ => TagManager.onRenderingLayersChanged -= OnRenderingLayersChanged);
            RegisterCallback<GeometryChangedEvent>(RecalculateHelpBoxSize);
        }

        internal override uint MaskToValue(int newMask) => (uint)newMask;

        internal override int ValueToMask(uint value) => (int)value;

        private protected override int UpdateMaskIfEverything(int currentMask)
        {
            int activeMax = RenderPipelineEditorUtility.GetActiveMaxRenderingLayers();
            return RenderPipelineEditorUtility.DoesMaskContainRenderingLayersOutsideOfMaxBitCount((uint)currentMask, activeMax)
                ? currentMask
                : base.UpdateMaskIfEverything(currentMask);
        }

        internal override string GetValueToDisplay() =>
            showMixedValue ? mixedValueString : GetDisplayedValue(ValueToMask(value));

        protected void UpdateLayersInfo() => UpdateChoices(value);

        bool CheckIfOnlyOneLayerWasDeselectedFromEverything(uint nextValue) =>
            rawValue == uint.MaxValue && nextValue != uint.MaxValue;

        void UpdateChoices(uint mask)
        {
            var (names, values) = RenderPipelineEditorUtility.GetRenderingLayerNamesAndValuesForMask(mask);
            m_RenderingLayersChoices.Clear();
            m_RenderingLayersChoices.AddRange(names);
            choices = m_RenderingLayersChoices;
            m_RenderingLayersChoicesMasks.Clear();
            m_RenderingLayersChoicesMasks.AddRange(values);
            choicesMasks = m_RenderingLayersChoicesMasks;
            UpdateHelpBoxVisibility(mask);
        }

        void OnRenderingLayersChanged() => value = rawValue;

        void RecalculateHelpBoxSize(GeometryChangedEvent evt) => m_HelpBox.style.width = evt.newRect.width;

        void UpdateHelpBoxVisibility(uint mask)
        {
            int activeMax = RenderPipelineEditorUtility.GetActiveMaxRenderingLayers();
            if (RenderPipelineEditorUtility.DoesMaskContainRenderingLayersOutsideOfMaxBitCount(mask, activeMax))
            {
                m_HelpBox.style.display = DisplayStyle.Flex;
                m_HelpBox.text = RenderPipelineEditorUtility.GetOutsideOfMaxBitCountWarningMessage(activeMax);
            }
            else
            {
                m_HelpBox.style.display = DisplayStyle.None;
            }
        }
    }
}
