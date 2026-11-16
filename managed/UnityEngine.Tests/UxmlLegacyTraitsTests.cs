#pragma warning disable CS0618 // the deprecated UxmlFactory/UxmlTraits path is what is tested
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using Xunit;

// The deprecated UxmlFactory/UxmlTraits path, measured on the Unity 6000.0.71f1 Editor in
// tests/projects/uxmllegacy (a traits class's Init sets each described property, from its default
// when the attribute is absent, in the Editor's order; descriptions listed base classes first).
// These tests apply the same rules to cases the capture does not take.
namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UxmlLegacyTraitsTests
    {
        sealed class Bag : IUxmlAttributes
        {
            readonly Dictionary<string, string> m_Values = new Dictionary<string, string>();
            public Bag(params string[] pairs)
            {
                for (int i = 0; i < pairs.Length; i += 2) m_Values[pairs[i]] = pairs[i + 1];
            }
            public bool TryGetAttributeValue(string attributeName, out string value) => m_Values.TryGetValue(attributeName, out value);
        }

        static T Create<T, TTraits>(T element, params string[] attributes) where T : VisualElement where TTraits : UxmlTraits, new()
        {
            new TTraits().Init(element, new Bag(attributes), CreationContext.Default);
            return element;
        }

        // The former focus attribute sets the tab index and focusability, which the later
        // `tabindex` and `focusable` (here from their defaults) set again.
        [Fact]
        public void LaterFocusAttributesOverrideTheFormerOne()
        {
            var element = Create<VisualElement, VisualElement.UxmlTraits>(new VisualElement { tabIndex = 4 }, "focus-index", "5", "focusable", "true");
            Assert.Equal(0, element.tabIndex);
            Assert.True(element.focusable);
            var negative = Create<VisualElement, VisualElement.UxmlTraits>(new VisualElement(), "focus-index", "-3", "tabindex", "2");
            Assert.Equal(2, negative.tabIndex);
            Assert.False(negative.focusable);
        }

        // An empty binding path leaves the element's own; a non-empty one replaces it.
        [Fact]
        public void AnEmptyBindingPathKeepsTheElementsOwn()
        {
            var kept = Create<BindableElement, BindableElement.UxmlTraits>(new BindableElement { bindingPath = "own" }, "binding-path", "");
            Assert.Equal("own", kept.bindingPath);
            var replaced = Create<BindableElement, BindableElement.UxmlTraits>(new BindableElement { bindingPath = "own" }, "binding-path", "other");
            Assert.Equal("other", replaced.bindingPath);
        }

        sealed class GameTraits : Button.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_First = new UxmlIntAttributeDescription { name = "first" };
            UxmlBoolAttributeDescription second { get; } = new UxmlBoolAttributeDescription { name = "second" };
            readonly UxmlStringAttributeDescription m_Third = new UxmlStringAttributeDescription { name = "third" };
        }

        // A game traits class lists the built-in descriptions first, then its own fields in
        // declaration order (an auto-property's among them).
        [Fact]
        public void GameDescriptionsFollowTheBuiltInOnesInDeclarationOrder()
        {
            var names = new GameTraits().uxmlAttributesDescription.Select(d => d.name).ToList();
            Assert.Equal(new[] { "first", "second", "third" }, names.Skip(names.Count - 3));
            Assert.Equal("name", names[0]);
            Assert.Equal("icon-image", names[names.Count - 4]);
        }

        // A slider's range is set before its value: a value outside the slider's own range but
        // inside the default one is kept.
        [Fact]
        public void ASliderTakesItsRangeBeforeItsValue()
        {
            var slider = Create<Slider, Slider.UxmlTraits>(new Slider(50, 60), "value", "5");
            Assert.Equal(0, slider.lowValue);
            Assert.Equal(10, slider.highValue);
            Assert.Equal(5, slider.value);
        }

        // The numeric fields' traits read the placeholder and read-only state, defaults included.
        [Fact]
        public void NumericFieldTraitsSetThePlaceholder()
        {
            var field = new IntegerField { isReadOnly = true };
            field.textEdition.placeholder = "own";
            Create<IntegerField, IntegerField.UxmlTraits>(field, "placeholder-text", "Count");
            Assert.Equal("Count", field.textEdition.placeholder);
            Assert.False(field.isReadOnly);
        }

        public enum Fruit { Apple, Pear }

        // EnumField's traits read the value as Enum.TryParse does, a number included (enum* rows: a
        // type alone gives the zero value, another case is not a value).
        [Fact]
        public void EnumFieldTraitsReadNumericValues()
        {
            var type = typeof(Fruit).AssemblyQualifiedName;
            var numeric = Create<EnumField, EnumField.UxmlTraits>(new EnumField(), "type", type, "value", "1");
            Assert.Equal(Fruit.Pear, numeric.value);
            var none = Create<EnumField, EnumField.UxmlTraits>(new EnumField(KeyCode.Space), "type", type);
            Assert.Equal(Fruit.Apple, none.value);
        }

        // Every text input reads the placeholder and selection attributes (builtin* rows: a text
        // field and an integer field).
        [Fact]
        public void TextInputsReadThePlaceholderAndSelectionNames()
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "LegacyPlaceholder";
            Unbound.UIElements.UxmlImporter.Import("Assets/UI/LegacyPlaceholder.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:LongField placeholder-text=\"Big\" hide-placeholder-on-focus=\"true\" select-word-by-double-click=\"false\" /></ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            var field = root.Q<LongField>();
            Assert.Equal("Big", field.textEdition.placeholder);
            Assert.True(field.textEdition.hidePlaceholderOnFocus);
            Assert.False(field.doubleClickSelectsWord);
        }

        // A button is a text element: its selection options are read under the text element's
        // names, the former ones included.
        [Fact]
        public void AButtonReadsTheTextElementSelectionNames()
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "LegacySelection";
            Unbound.UIElements.UxmlImporter.Import("Assets/UI/LegacySelection.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Button selectable=\"true\" selectWordByDoubleClick=\"false\" triple-click-selects-line=\"false\" /></ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            var button = root.Q<Button>();
            Assert.True(button.selection.isSelectable);
            Assert.False(button.selection.doubleClickSelectsWord);
            Assert.False(button.selection.tripleClickSelectsLine);
        }
    }
}
