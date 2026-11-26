using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// Style sheets ending in text no rule takes (tests/projects/ussvalidation Trailing*, measured in
// Unity 6000.0.71f1: nothing logged, the rules before kept, a block the end closes kept).
namespace UnityEngine.Tests
{
    public class UssTrailingTextTests
    {
        [Theory]
        [InlineData(".a { width: 31px; }\n\n›", 1)]
        [InlineData(".a { width: 32px; }\nstray", 1)]
        [InlineData(".a { width: 33px; }\n.b { width: 1px;", 2)]
        [InlineData(".a { width: 33px; }\n.b { width: 1px", 2)]
        [InlineData(".a { width: 33px; }\n.b {", 2)]
        public void TrailingTextMakesNoErrorAndKeepsTheRules(string text, int rules)
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var importer = new UssImporter("trailing.uss");
            importer.Import(sheet, text);
            Assert.Empty(importer.errors);
            Assert.Empty(importer.warnings);
            Assert.Equal(rules, sheet.rules.Length);
            Assert.Equal("width", sheet.rules[0].properties[0].name);
            if (rules > 1 && text.Contains("1px")) Assert.Equal("width", sheet.rules[1].properties[0].name);
            Object.DestroyImmediate(sheet);
        }
    }
}
