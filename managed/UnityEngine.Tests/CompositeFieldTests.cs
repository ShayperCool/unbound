using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The composite fields and Hash128Field (Unity 6000.0.71f1,
    /// tests/projects/compositefields). Controls outside the probe: a sub-field edit validated by
    /// the composite, the per-component UXML attributes of a bounds field (center and size) and a
    /// Vector4Field, and a single-digit hash.</summary>
    public sealed class CompositeFieldTests : System.IDisposable
    {
        // Sub-field changes reach the composite as change events, which need a panel.
        readonly Panel m_Panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        public void Dispose() => m_Panel.Dispose();

        T Attached<T>(T element) where T : VisualElement
        {
            m_Panel.visualTree.Add(element);
            return element;
        }

        [Fact]
        public void SubFieldsShowTheComponentsAndSetTheWhole()
        {
            var field = Attached(new Vector3Field("P") { value = new Vector3(1, 2, 3) });
            Assert.Equal(3, field.fields.Count);
            Assert.Equal(new[] { "1", "2", "3" }, field.fields.ConvertAll(f => f.text));
            Assert.Equal(new[] { "X", "Y", "Z" }, field.fields.ConvertAll(f => f.label));
            field.fields[1].value = 5;
            Assert.Equal(new Vector3(1, 5, 3), field.value);
            field.SetValueWithoutNotify(new Vector3(1f / 3, 0, 0));
            Assert.Equal("0.3333333", field.fields[0].text);
            var rect = new RectField();
            Assert.Contains(BaseCompositeField<Rect, FloatField, float>.multilineVariantUssClassName, rect.GetClasses());
            Assert.Equal(2, rect.visualInput.hierarchy.childCount);
        }

        [Fact]
        public void TheCompositeValidatesASubFieldsEdit()
        {
            var field = Attached(new Vector2IntField());
            field.onValidateValue += v => new Vector2Int(Mathf.Min(v.x, 10), v.y);
            field.fields[0].value = 50;
            Assert.Equal(new Vector2Int(10, 0), field.value);
            Assert.Equal(10, field.fields[0].value);
        }

        [Fact]
        public void BoundsFieldsEditCenterAndExtents()
        {
            var bounds = Attached(new BoundsField { value = new Bounds(Vector3.zero, new Vector3(2, 2, 2)) });
            var center = bounds.Q<Vector3Field>("unity-m_Center-input");
            center.value = new Vector3(1, 1, 1);
            Assert.Equal(new Vector3(1, 1, 1), bounds.value.center);
            Assert.Equal(new Vector3(1, 1, 1), bounds.value.extents);
            var cells = Attached(new BoundsIntField { value = new BoundsInt(1, 2, 3, 4, 5, 6) });
            cells.Q<Vector3IntField>("unity-m_Size-input").value = new Vector3Int(7, 7, 7);
            Assert.Equal(new BoundsInt(1, 2, 3, 7, 7, 7), cells.value);
        }

        static VisualElement Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "CompositeFieldTests";
            UxmlImporter.Import("Assets/CompositeFieldTests.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return root;
        }

        [Fact]
        public void ComponentAttributesMakeTheWholeValue()
        {
            var root = Clone("<ui:BoundsField name=\"b\" value=\"9,9,9,9,9,9\" ez=\"6\" cx=\"1\" />" +
                "<ui:Vector4Field name=\"v\" x=\"not a number\" w=\"0.5\" />" +
                "<ui:MinMaxSlider name=\"m\" low-limit=\"0\" high-limit=\"10\" x=\"3\" />");
            var bounds = root.Q<BoundsField>("b");
            Assert.Equal(new Vector3(1, 0, 0), bounds.value.center);
            Assert.Equal(new Vector3(0, 0, 6), bounds.value.size);
            Assert.Equal(new Vector4(0, 0, 0, 0.5f), root.Q<Vector4Field>("v").value);
            Assert.Equal(new Vector2(0, 10), root.Q<MinMaxSlider>("m").value);
        }

        [Fact]
        public void Hash128FieldsReadHexadecimalText()
        {
            var hash = new Hash128Field { value = Hash128.Parse("0123456789abcdef0123456789abcdef") };
            Assert.Equal("0123456789abcdef0123456789abcdef", hash.text);
            Assert.Contains("unity-hash128-field", hash.GetClasses());
            // One decimal digit reads as that number; longer text as hexadecimal.
            var single = new Hash128Field();
            single.text = "7";
            single.UpdateValueFromText();
            Assert.Equal(new Hash128(7ul, 0ul), single.value);
            single.text = "ab";
            single.UpdateValueFromText();
            Assert.Equal(Hash128.Parse("ab"), single.value);
        }
    }
}
