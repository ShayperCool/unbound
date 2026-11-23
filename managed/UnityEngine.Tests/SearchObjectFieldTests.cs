using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using Xunit;
using SearchObjectField = UnityEditor.Search.ObjectField;

namespace UnityEngine.Tests
{
    /// <summary>UnityEditor.Search.ObjectField as the Unity 6000.0.71f1 Editor builds it
    /// (tests/projects/searchobjectfield): its input's display and selector, its defaults, the
    /// display of no value and of values, and a GameObject read as the component of a component type.</summary>
    public sealed class SearchObjectFieldTests
    {
        [Fact]
        public void AFieldIsBuiltWithItsDisplayAndSelectorAndPicksObjects()
        {
            var field = new SearchObjectField();
            Assert.Equal(new[] { "unity-base-field", "unity-base-field--no-label", "unity-object-field" }, field.GetClasses().ToArray());
            var input = field.Q(className: "unity-object-field__input");
            Assert.Equal(new[] { "ObjectFieldDisplay", "ObjectFieldSelector" }, input.Children().Select(c => c.GetType().Name).ToArray());
            Assert.Equal(typeof(Object), field.objectType);
            Assert.Equal(UnityEngine.Search.SearchViewFlags.ObjectPicker, field.searchViewFlags);
            Assert.Null(field.searchContext);
            Assert.Equal(18f, SearchObjectField.singleLineHeight);
            Assert.True(field.Q(className: "unity-object-field__object").focusable);
            Assert.False(input.focusable);

            var label = field.Q(className: "unity-object-field-display").Q<Label>();
            Assert.Equal("None (Object)", label.text);
            field.objectType = typeof(Texture2D);
            Assert.Equal("None (Texture 2D)", label.text);

            var go = new GameObject("Probe Object");
            var texture = new Texture2D(2, 2) { name = "Probe Texture" };
            try
            {
                field.objectType = typeof(Transform);
                field.SetValueWithoutNotify(go);
                Assert.Same(go.transform, field.value);
                field.objectType = typeof(Rigidbody);
                field.SetValueWithoutNotify(go);
                Assert.Same(go, field.value);
                field.objectType = typeof(Object);
                field.value = texture;
                Assert.Equal("Probe Texture", label.text);
                // A texture shows itself.
                Assert.Same(texture, field.Q(className: "unity-object-field-display").Q<Image>().image);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void UxmlDataMakesAFieldAndReadsItsType()
        {
            var data = new SearchObjectField.UxmlSerializedData();
            Assert.IsType<SearchObjectField>(data.CreateInstance());
        }
    }
}
