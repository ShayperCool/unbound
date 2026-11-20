using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UxmlSerializedDataProbes
{
    [Serializable]
    public class CustomPayload
    {
        public string label;
        public int value;

        public override string ToString() => $"{label ?? "<null>"}:{value}";
    }

#if UNITY_EDITOR
    public class CustomPayloadConverter : UnityEditor.UIElements.UxmlAttributeConverter<CustomPayload>
    {
        public override CustomPayload FromString(string value)
        {
            if (string.IsNullOrEmpty(value)) return new CustomPayload { label = "", value = 0 };
            var parts = value.Split(':');
            return new CustomPayload
            {
                label = parts[0],
                value = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0,
            };
        }

        public override string ToString(CustomPayload value) => value == null ? "" : $"{value.label}:{value.value}";
    }
#endif

    [UxmlElement]
    public partial class CustomCounter : VisualElement
    {
        [UxmlAttribute] public int count { get; set; } = 42;
        [UxmlAttribute("display-title", "old-title")] public string title { get; set; } = "default-title";
        [UxmlAttribute] public bool active { get; set; } = true;
        [UxmlAttribute] public List<int> numbers { get; set; } = new List<int> { 7, 8 };
        [UxmlAttribute] public CustomPayload payload { get; set; } = new CustomPayload { label = "init", value = 99 };
    }

    [UxmlElement]
    public partial class DerivedBindable : BindableElement
    {
        [UxmlAttribute] public string tag { get; set; } = "base-tag";

        public DerivedBindable()
        {
            bindingPath = "preset.path";
        }
    }

    [UxmlElement]
    public partial class CustomLabel : TextElement
    {
        [UxmlAttribute] public int level { get; set; } = 5;

        public CustomLabel()
        {
            text = "preset-text";
        }
    }

    [UxmlObject]
    public partial class ProbeCustomBinding : CustomBinding
    {
        [UxmlAttribute] public string prefix { get; set; } = "pre:";
    }

    /// <summary>Manual UxmlSerializedData subclass that throws inside Deserialize to verify flag restoration.</summary>
    public class ThrowingSerializedData : UxmlSerializedData
    {
        public bool sawDefaultDuringDeserialize;
        public bool sawOverrideDuringDeserialize;

        public override object CreateInstance() => new VisualElement();

        public override void Deserialize(object obj)
        {
            sawDefaultDuringDeserialize = ShouldWriteAttributeValue(UxmlAttributeFlags.DefaultValue);
            sawOverrideDuringDeserialize = ShouldWriteAttributeValue(UxmlAttributeFlags.OverriddenInUxml);
            throw new InvalidOperationException("deserialize-control");
        }
    }
}
