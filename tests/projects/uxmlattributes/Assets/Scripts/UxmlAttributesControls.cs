#pragma warning disable CS0618 // UxmlFactory and UxmlTraits are deprecated in Unity 6 and still supported
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UxmlAttributesProbes
{
    public enum ProbeEnum { First, Second, Third }

    /// <summary>One [UxmlAttribute] of each measured type, each with a non-default initial value
    /// so that an unset attribute is told apart from a reset one.</summary>
    [UxmlElement]
    public partial class TypedProbe : VisualElement
    {
        [UxmlAttribute] public int myInt { get; set; } = -1;
        [UxmlAttribute] public float myFloat { get; set; } = -1f;
        [UxmlAttribute] public bool myBool { get; set; }
        [UxmlAttribute] public bool myFlag { get; set; }
        [UxmlAttribute] public long myLong { get; set; } = -1;
        [UxmlAttribute] public double myDouble { get; set; } = -1;
        [UxmlAttribute] public ProbeEnum myEnum { get; set; } = ProbeEnum.Third;
        [UxmlAttribute] public Color myColor { get; set; } = new Color(0.25f, 0.5f, 0.75f, 1f);
        [UxmlAttribute] public Vector2 myVector2 { get; set; } = new Vector2(-1f, -1f);
        [UxmlAttribute] public Vector3 myVector3 { get; set; } = new Vector3(-1f, -1f, -1f);
        [UxmlAttribute] public Vector2Int myVector2Int { get; set; } = new Vector2Int(-1, -1);
        [UxmlAttribute] public Rect myRect { get; set; } = new Rect(-1f, -1f, -1f, -1f);
        [UxmlAttribute] public string myString { get; set; } = "initial";
        [UxmlAttribute] public char myChar { get; set; } = '?';
        [UxmlAttribute, UxmlTypeReference(typeof(VisualElement))] public Type myType { get; set; }
        [UxmlAttribute] public List<int> myInts { get; set; }
        [UxmlAttribute] public List<string> myStrings { get; set; }
        [UxmlAttribute] public Texture2D myTexture { get; set; }
        [UxmlAttribute] public Sprite mySprite { get; set; }
        [UxmlAttribute] public StyleSheet mySheet { get; set; }
        [UxmlAttribute] public VisualTreeAsset myTemplate { get; set; }
        [UxmlAttribute] public UnityEngine.Object myObject { get; set; }
        [UxmlAttribute("custom-name")] public int renamed { get; set; } = -1;
        [UxmlAttribute("new-name", "old-name")] public string aliased { get; set; } = "initial";
    }

    [UxmlElement]
    public partial class DerivedProbe : TypedProbe
    {
        [UxmlAttribute] public string extra { get; set; } = "initial";
    }

    [UxmlElement("custom-element-name")]
    public partial class RenamedElement : VisualElement
    {
        [UxmlAttribute] public int value { get; set; } = -1;
    }

    /// <summary>A control declared only through the legacy UxmlFactory/UxmlTraits path.</summary>
    public class LegacyProbe : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<LegacyProbe, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_Value = new UxmlIntAttributeDescription { name = "legacy-value", defaultValue = 11 };
            readonly UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "legacy-text", defaultValue = "default" };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var probe = (LegacyProbe)ve;
                probe.legacyValue = m_Value.GetValueFromBag(bag, cc);
                probe.legacyText = m_Text.GetValueFromBag(bag, cc);
                probe.initCalls++;
            }
        }

        public int legacyValue { get; set; } = -1;
        public string legacyText { get; set; } = "initial";
        public int initCalls { get; set; }
    }

    /// <summary>A [UxmlElement] control that also declares a legacy factory.</summary>
    [UxmlElement]
    public partial class BothProbe : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<BothProbe, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_Value = new UxmlIntAttributeDescription { name = "value", defaultValue = 100 };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((BothProbe)ve).value = m_Value.GetValueFromBag(bag, cc) + 1000;
                ((BothProbe)ve).traitsInit = true;
            }
        }

        [UxmlAttribute] public int value { get; set; } = -1;
        public bool traitsInit { get; set; }
    }

    /// <summary>Asset reference members of other types: a Background (a texture, sprite, render
    /// texture or vector image) and a Font.</summary>
    [UxmlElement]
    public partial class AssetRefProbe : VisualElement
    {
        [UxmlAttribute] public Background myBackground { get; set; }
        [UxmlAttribute] public Font myFont { get; set; }
    }

    /// <summary>Values the preset controls start with, to tell "kept" from "cleared".</summary>
    static class ProbePresets
    {
        static Texture2D s_Texture;
        static VisualTreeAsset s_Template;

        public static Texture2D Texture
        {
            get
            {
                if (s_Texture == null) s_Texture = new Texture2D(1, 1) { name = "Preset" };
                return s_Texture;
            }
        }

        public static VisualTreeAsset Template
        {
            get
            {
                if (s_Template == null)
                {
                    s_Template = ScriptableObject.CreateInstance<VisualTreeAsset>();
                    s_Template.name = "Preset";
                }
                return s_Template;
            }
        }
    }

    /// <summary>A Button whose icon is set before its UXML attributes are read.</summary>
    [UxmlElement]
    public partial class PresetIconButton : Button
    {
        public PresetIconButton() { iconImage = Background.FromTexture2D(ProbePresets.Texture); }
    }

    /// <summary>Asset reference members with initial values, and a list of Backgrounds.</summary>
    [UxmlElement]
    public partial class PresetRefProbe : VisualElement
    {
        [UxmlAttribute] public Background myBackground { get; set; } = Background.FromTexture2D(ProbePresets.Texture);
        [UxmlAttribute] public List<Background> myBackgrounds { get; set; }
        [UxmlAttribute] public Texture2D myTexture { get; set; } = ProbePresets.Texture;
    }

    /// <summary>A ListView whose item template is set before its UXML attributes are read.</summary>
    [UxmlElement]
    public partial class PresetTemplateList : ListView
    {
        public PresetTemplateList() { itemTemplate = ProbePresets.Template; }
    }
}
