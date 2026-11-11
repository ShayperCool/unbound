using System.Collections.Generic;
using Unity.Properties;
using UnityEngine.UIElements;
using Unbound.UIElements;
using Xunit;

// Property paths into the values of style properties, and what resolvedStyle answers right after
// an inline value is set or unset. The probe (tests/projects/uitkbindings, nested section) records
// the Editor's behaviour; these tests check the same rules outside its setup: other members and
// values, a rule from a game style sheet instead of the theme, an inherited property.
namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkStyleValuePathTests
    {
        static Panel NewPanel()
        {
            var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            panel.visualTree.style.width = 400;
            panel.visualTree.style.height = 300;
            return panel;
        }

        static void Update(Panel panel)
        {
            panel.ApplyStyles();
            panel.ValidateLayout();
        }

        static T Get<T>(VisualElement e, string path)
        {
            Assert.True(PropertyContainer.TryGetValue(ref e, new PropertyPath(path), out T value, out VisitReturnCode code), path + ": " + code);
            return value;
        }

        static VisitReturnCode Set<T>(VisualElement e, string path, T value)
        {
            PropertyContainer.TrySetValue(ref e, new PropertyPath(path), value, out VisitReturnCode code);
            return code;
        }

        [Fact]
        public void PathsReachTheMembersOfStyleValues()
        {
            var e = new VisualElement();
            e.style.translate = new Translate(new Length(1, LengthUnit.Pixel), new Length(20, LengthUnit.Percent), 0);
            e.style.rotate = new Rotate(new Angle(0.5f, AngleUnit.Turn));
            e.style.textShadow = new TextShadow { offset = new Vector2(1, 2), blurRadius = 3, color = Color.green };
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            Assert.Equal(new Length(20, LengthUnit.Percent), Get<Length>(e, "style.translate.value.y"));
            Assert.Equal(LengthUnit.Percent, Get<LengthUnit>(e, "style.translate.value.y.unit"));
            Assert.Equal(AngleUnit.Turn, Get<AngleUnit>(e, "style.rotate.value.angle.unit"));
            Assert.Equal(Vector3.forward, Get<Vector3>(e, "style.rotate.value.axis"));
            Assert.Equal(1f, Get<float>(e, "style.text-shadow.value.color.g"));
            Assert.Equal(BackgroundSizeType.Cover, Get<BackgroundSizeType>(e, "style.backgroundSize.value.sizeType"));
            // A member the value does not have, and the value's member asked on the style value.
            Assert.False(PropertyContainer.TryGetValue(ref e, new PropertyPath("style.rotate.value.z"), out float _, out VisitReturnCode code));
            Assert.Equal(VisitReturnCode.InvalidPath, code);
            Assert.False(PropertyContainer.TryGetValue(ref e, new PropertyPath("style.rotate.angle"), out Angle _, out code));
            Assert.Equal(VisitReturnCode.InvalidPath, code);
        }

        [Fact]
        public void WritingAMemberSetsTheWholeStyleValue()
        {
            var e = new VisualElement();
            Assert.Equal(StyleKeyword.Null, e.style.scale.keyword);
            Assert.Equal(VisitReturnCode.Ok, Set(e, "style.scale.value.value.y", 5f));
            // The other members start from what the unset value reads as: a scale of one.
            Assert.Equal(StyleKeyword.Undefined, e.style.scale.keyword);
            Assert.Equal(new Vector3(1, 5, 1), e.style.scale.value.value);
            Assert.Equal(VisitReturnCode.Ok, Set(e, "style.transformOrigin.value.z", 4f));
            Assert.Equal(new Length(50, LengthUnit.Percent), e.style.transformOrigin.value.x);
            Assert.Equal(4f, e.style.transformOrigin.value.z);
            Assert.Equal(VisitReturnCode.Ok, Set(e, "style.backgroundPositionY.value.offset", new Length(3, LengthUnit.Pixel)));
            Assert.Equal(new Length(3, LengthUnit.Pixel), e.style.backgroundPositionY.value.offset);
            // No conversion from a number to a Length; the value stays.
            Assert.Equal(VisitReturnCode.InvalidCast, Set(e, "style.backgroundPositionY.value.offset", 9f));
            Assert.Equal(new Length(3, LengthUnit.Pixel), e.style.backgroundPositionY.value.offset);
        }

        [Fact]
        public void TransitionListsAreReachedByIndexAndPropertyNamesAreReadOnly()
        {
            var e = new VisualElement();
            Assert.Equal(VisitReturnCode.InvalidPath, Set(e, "style.transitionDelay.value[0].value", 1f));
            e.style.transitionDelay = new List<TimeValue> { new TimeValue(1), new TimeValue(250, TimeUnit.Millisecond) };
            e.style.transitionProperty = new List<StylePropertyName> { "opacity", "scale" };
            Assert.Equal(250f, Get<float>(e, "style.transitionDelay.value[1].value"));
            Assert.Equal(VisitReturnCode.Ok, Set(e, "style.transitionDelay.value[1].unit", TimeUnit.Second));
            Assert.Equal(new TimeValue(250, TimeUnit.Second), e.style.transitionDelay.value[1]);
            Assert.Equal(VisitReturnCode.InvalidPath, Set(e, "style.transitionDelay.value[2].value", 1f));
            Assert.Equal("scale", Get<string>(e, "style.transitionProperty.value[1].name"));
            Assert.Equal(VisitReturnCode.AccessViolation, Set(e, "style.transitionProperty.value[1].name", "width"));
            Assert.Equal("scale", e.style.transitionProperty.value[1].ToString());
        }

        [Fact]
        public void TransformValuesWithoutAValueOfTheirOwnReadAsTheirDefaults()
        {
            var e = new VisualElement();
            Assert.Equal(Vector3.one, e.style.scale.value.value);
            Assert.Equal(new Length(50, LengthUnit.Percent), e.style.transformOrigin.value.y);
            Assert.Equal(0f, e.style.rotate.value.angle.value);
            Assert.Equal(new Length(0, LengthUnit.Pixel), e.style.translate.value.x);
            Assert.Equal(Vector3.one, new StyleScale(StyleKeyword.Initial).value.value);
            Assert.Equal(new Length(50, LengthUnit.Percent), new StyleTransformOrigin(StyleKeyword.None).value.x);
        }

        [Fact]
        public void AnInlineValueShowsAtOnceAlsoBeforeAnyStylePass()
        {
            // Outside a panel, never styled.
            var e = new VisualElement();
            e.style.opacity = 0.3f;
            e.style.visibility = Visibility.Hidden;
            Assert.Equal(0.3f, e.resolvedStyle.opacity);
            Assert.Equal(Visibility.Hidden, e.resolvedStyle.visibility);
            e.style.opacity = 0.6f;
            Assert.Equal(0.6f, e.resolvedStyle.opacity);
            // Unset: the initial value again, the other inline value kept.
            e.style.opacity = StyleKeyword.Null;
            Assert.Equal(1f, e.resolvedStyle.opacity);
            Assert.Equal(Visibility.Hidden, e.resolvedStyle.visibility);
            // Another element's initial values are untouched.
            Assert.Equal(Visibility.Visible, new VisualElement().resolvedStyle.visibility);
            // In a panel before its first style pass, and through that pass.
            var panel = NewPanel();
            var inPanel = new VisualElement();
            panel.visualTree.Add(inPanel);
            inPanel.style.scale = new Scale(new Vector3(2, 2, 1));
            Assert.Equal(new Vector3(2, 2, 1), inPanel.resolvedStyle.scale.value);
            Update(panel);
            Assert.Equal(new Vector3(2, 2, 1), inPanel.resolvedStyle.scale.value);
        }

        [Fact]
        public void AnUnsetInlineValueFallsBackToTheRulesAtOnce()
        {
            var panel = NewPanel();
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            new UssImporter("rules.uss").Import(sheet, ".box { opacity: 0.4; color: rgb(0, 255, 0); } .parent { color: rgb(255, 0, 0); }");
            panel.visualTree.styleSheets.Add(sheet);
            var parent = new VisualElement();
            parent.AddToClassList("parent");
            var box = new VisualElement();
            box.AddToClassList("box");
            var plain = new VisualElement();
            parent.Add(box);
            parent.Add(plain);
            panel.visualTree.Add(parent);
            Update(panel);
            Assert.Equal(0.4f, box.resolvedStyle.opacity);
            box.style.opacity = 0.9f;
            box.style.color = Color.blue;
            Assert.Equal(0.9f, box.resolvedStyle.opacity);
            box.style.opacity = StyleKeyword.Null;
            // The rule's value, the other inline value kept; no style pass in between.
            Assert.Equal(0.4f, box.resolvedStyle.opacity);
            Assert.Equal(Color.blue, box.resolvedStyle.color);
            box.style.color = StyleKeyword.Null;
            Assert.Equal(Color.green, box.resolvedStyle.color);
            // An inherited value comes back from the parent.
            plain.style.color = Color.blue;
            plain.style.color = StyleKeyword.Null;
            Assert.Equal(Color.red, plain.resolvedStyle.color);
            Update(panel);
            Assert.Equal(0.4f, box.resolvedStyle.opacity);
            Assert.Equal(Color.green, box.resolvedStyle.color);
            Assert.Equal(Color.red, plain.resolvedStyle.color);
        }
    }
}
