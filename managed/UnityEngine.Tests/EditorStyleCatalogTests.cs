using Unbound.Runtime;
using Unbound.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The Editor's style catalog over style sheets written here: how sheets merge, what a block is
    /// named and holds, and what a GUI style takes from its block. The catalog of the Editor's own
    /// sheets is measured against the Editor in tests/projects/editorskin.
    /// </summary>
    public class EditorStyleCatalogTests
    {
        static StyleSheet Sheet(string name, string uss)
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            new UssImporter(name).Import(sheet, uss);
            return sheet;
        }

        static EditorStyleCatalog Catalog(params string[] sheets)
        {
            var list = new StyleSheet[sheets.Length];
            for (int i = 0; i < sheets.Length; i++) list[i] = Sheet("catalog" + i + ".uss", sheets[i]);
            return EditorStyleCatalog.Build(list);
        }

        [Fact]
        public void ALaterSheetReplacesAPropertyAndKeepsTheOthers()
        {
            var catalog = Catalog(
                ".thing { width: 10px; height: 20px; }",
                ".thing { height: 30px; }");
            var block = catalog.Find("thing");
            Assert.NotNull(block);
            Assert.Equal(10f, block.GetFloat("width", -1f));
            Assert.Equal(30f, block.GetFloat("height", -1f));
            Assert.Equal(1, catalog.Count);
        }

        [Fact]
        public void ABlockIsNamedByTheTypeClassAndIdPartsOfOneCompoundSelector()
        {
            var catalog = Catalog("Button { width: 1px; } .a.b { width: 2px; } #id { width: 3px; } .outer .inner { width: 4px; }");
            Assert.Equal(1f, catalog.Find("Button").GetFloat("width", 0f));
            Assert.Equal(2f, catalog.Find("a.b").GetFloat("width", 0f));
            Assert.Equal(3f, catalog.Find("id").GetFloat("width", 0f));
            // A selector with a descendant makes no block.
            Assert.Null(catalog.Find("inner"));
            Assert.Null(catalog.Find("outer.inner"));
            Assert.Equal(3, catalog.Count);
        }

        [Fact]
        public void PseudoStateRulesAreValuesOfTheSameBlockUnderTheirState()
        {
            var catalog = Catalog(".thing { width: 10px; } .thing:hover { width: 12px; } .thing:hover:checked { width: 14px; }");
            var block = catalog.Find("thing");
            Assert.Equal(10f, block.GetFloat("width", 0f));
            Assert.Equal(12f, block.In(EditorStyleCatalog.State.Hover).GetFloat("width", 0f));
            Assert.Equal(14f, block.In(EditorStyleCatalog.State.Hover | EditorStyleCatalog.State.Checked).GetFloat("width", 0f));
            // A state reads its own values only: nothing falls back to the plain rule.
            Assert.Equal(-1f, block.In(EditorStyleCatalog.State.Active).GetFloat("width", -1f));
            Assert.Equal(-1f, block.In(EditorStyleCatalog.State.Hover).GetFloat("height", -1f));
        }

        [Theory]
        [InlineData("margin: 4px;", 4, 4, 4, 4)]
        [InlineData("margin: 1px 2px;", 2, 2, 1, 1)]
        [InlineData("margin: 1px 2px 3px;", 2, 2, 1, 3)]
        [InlineData("margin: 1px 2px 3px 4px;", 4, 2, 1, 3)]
        [InlineData("margin: 4px; margin-left: 9px;", 9, 4, 4, 4)]
        // A single number fills only the sides not given before it; a rect replaces them all.
        [InlineData("margin-left: 9px; margin: 4px;", 9, 4, 4, 4)]
        [InlineData("margin-left: 9px; margin: 1px 2px;", 2, 2, 1, 1)]
        [InlineData("margin-top: 7px;", 0, 0, 7, 0)]
        public void ARectPropertyGivesItsSides(string declarations, int left, int right, int top, int bottom)
        {
            var catalog = Catalog(".thing { " + declarations + " }");
            var style = new GUIStyle();
            catalog.ApplyTo(style, "thing");
            Assert.Equal(left, style.margin.left);
            Assert.Equal(right, style.margin.right);
            Assert.Equal(top, style.margin.top);
            Assert.Equal(bottom, style.margin.bottom);
        }

        [Fact]
        public void SidesAStyleSheetLeavesOutKeepTheStylesOwn()
        {
            var catalog = Catalog(".thing { padding-left: 6px; }");
            var style = new GUIStyle { padding = new RectOffset(1, 2, 3, 4) };
            catalog.ApplyTo(style, "thing");
            Assert.Equal(6, style.padding.left);
            Assert.Equal(2, style.padding.right);
            Assert.Equal(3, style.padding.top);
            Assert.Equal(4, style.padding.bottom);
        }

        [Fact]
        public void ARectOfALaterSheetReplacesTheSidesOfAnEarlierOne()
        {
            var catalog = Catalog(".thing { padding: 1px 2px 3px 4px; }", ".thing { padding: 8px 9px; }");
            var style = new GUIStyle();
            catalog.ApplyTo(style, "thing");
            Assert.Equal(9, style.padding.left);
            Assert.Equal(9, style.padding.right);
            Assert.Equal(8, style.padding.top);
            Assert.Equal(8, style.padding.bottom);
        }

        [Fact]
        public void ExtendGivesTheParentsMissingPropertiesAndItsPseudoStateRules()
        {
            var catalog = Catalog(
                ".base { width: 10px; height: 20px; } .base:hover { width: 11px; } " +
                ".child { -unity-extend: \".base\"; height: 99px; }");
            var child = catalog.Find("child");
            Assert.Equal(10f, child.GetFloat("width", 0f));
            Assert.Equal(99f, child.GetFloat("height", 0f));
            Assert.Equal(11f, child.In(EditorStyleCatalog.State.Hover).GetFloat("width", 0f));
            Assert.False(child.Has("-unity-extend", EditorStyleCatalog.Kind.Text));
        }

        [Fact]
        public void ExtendGoesThroughAParentThatExtendsItself()
        {
            // The grandchild is written first: its parent takes the grandparent's width before it does.
            var catalog = Catalog(
                ".c { -unity-extend: \".b\"; } .b { -unity-extend: \".a\"; height: 2px; } .a { width: 1px; height: 1px; }");
            var c = catalog.Find("c");
            Assert.Equal(1f, c.GetFloat("width", 0f));
            Assert.Equal(2f, c.GetFloat("height", 0f));
        }

        [Fact]
        public void AStyleTakesWhatItsBlockSays()
        {
            var catalog = Catalog(
                ".My-Style { width: 30px; height: 18px; font-size: 14px; font-weight: bold; -unity-text-align: middle-center; " +
                "-unity-clipping: overflow; -unity-image-position: image-above; -unity-word-wrap: true; -unity-rich-text: false; " +
                "-unity-stretch-width: true; -unity-slice: 3px; -unity-overflow: 1px 2px; -unity-content-offset: 5px 6px; color: rgb(255, 0, 0); } " +
                ".My-Style:hover { color: rgb(0, 255, 0); } .My-Style:checked { color: rgb(0, 0, 255); }");
            var style = new GUIStyle { name = "My Style", richText = true };
            catalog.ApplyTo(style, EditorStyleCatalog.BlockNameOfStyle(style.name));
            Assert.Equal(30f, style.fixedWidth);
            Assert.Equal(18f, style.fixedHeight);
            Assert.Equal(14, style.fontSize);
            Assert.Equal(FontStyle.Bold, style.fontStyle);
            Assert.Equal(TextAnchor.MiddleCenter, style.alignment);
            Assert.Equal(TextClipping.Overflow, style.clipping);
            Assert.Equal(ImagePosition.ImageAbove, style.imagePosition);
            Assert.True(style.wordWrap);
            Assert.False(style.richText);
            Assert.True(style.stretchWidth);
            Assert.False(style.stretchHeight);
            Assert.Equal(3, style.border.left);
            Assert.Equal(3, style.border.bottom);
            Assert.Equal(2, style.overflow.left);
            Assert.Equal(1, style.overflow.top);
            Assert.Equal(new Vector2(5f, 6f), style.contentOffset);
            Assert.Equal(Color.red, style.normal.textColor);
            Assert.Equal(Color.green, style.hover.textColor);
            Assert.Equal(Color.blue, style.onNormal.textColor);
            // States the block has no colour for take the block's own colour; an on state takes
            // after its off state.
            Assert.Equal(Color.red, style.focused.textColor);
            Assert.Equal(Color.red, style.active.textColor);
            Assert.Equal(Color.green, style.onHover.textColor);
            Assert.Equal("My Style", style.name);
        }

        [Fact]
        public void TextWithoutABlockColourTakesTheThemesTextColour()
        {
            var catalog = Catalog(":root { --unity-text-color: rgb(0, 0, 255); } .thing { width: 1px; }");
            var style = new GUIStyle();
            style.normal.textColor = Color.red;
            catalog.ApplyTo(style, "thing");
            Assert.Equal(Color.blue, style.normal.textColor);
            Assert.Equal(Color.blue, style.hover.textColor);
        }

        [Fact]
        public void AFontSizeOfTheStyleStaysUnlessTheBlockGivesOne()
        {
            var catalog = Catalog(".plain { width: 1px; } .sized { font-size: 9px; }");
            var style = new GUIStyle { fontSize = 20 };
            catalog.ApplyTo(style, "plain");
            Assert.Equal(20, style.fontSize);
            catalog.ApplyTo(style, "sized");
            Assert.Equal(9, style.fontSize);
        }

        [Theory]
        [InlineData("button", "Button")]
        [InlineData("textfield", "TextField")]
        [InlineData("horizontalscrollbarthumb", "HorizontalScrollbarThumb")]
        [InlineData("AC BoldHeader", "AC-BoldHeader")]
        [InlineData("Foo.Bar baz", "Foo-Bar-baz")]
        [InlineData("Button", "Button")]
        [InlineData("", "")]
        public void AStylesBlockIsNamedAfterIt(string styleName, string blockName) =>
            Assert.Equal(blockName, EditorStyleCatalog.BlockNameOfStyle(styleName));

        [Fact]
        public void OnlyTheStylesOfASkinThatHaveABlockChangeAndLoseTheirBackgrounds()
        {
            var catalog = Catalog("Button { padding: 2px 6px; }");
            var skin = ScriptableObject.CreateInstance<GUISkin>();
            var texture = new Texture2D(2, 2);
            skin.button.normal.background = texture;
            skin.label.normal.background = texture;
            skin.label.padding = new RectOffset(7, 7, 7, 7);
            catalog.ApplyTo(skin);
            Assert.Equal(6, skin.button.padding.left);
            Assert.Equal(2, skin.button.padding.top);
            Assert.Null(skin.button.normal.background);
            Assert.Equal(7, skin.label.padding.left);
            Assert.Same(texture, skin.label.normal.background);
        }

        [Fact]
        public void TheEditorsTextSettingsAnswerForFallbacksAndSharpness()
        {
            var settings = UnityEditor.EditorTextSettings.defaultTextSettings;
            Assert.Same(settings.fallbackFontAssets, settings.GetFallbackFontAssets());
            Assert.Same(settings.fallbackFontAssets, settings.GetFallbackFontAssets(12));
            Assert.Equal(0f, settings.GetEditorTextSharpness());
        }
    }
}
