using UnityEditor.ShaderKeywordFilter;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The keyword filter attributes as the Unity 6000.0.71f1 Editor answers them
    /// (tests/projects/keywordfilter).</summary>
    public sealed class ShaderKeywordFilterTests
    {
        class Settings
        {
            [RemoveIfNot(3, overridePriority: true, keywordNames: "K")]
            public int quality;
        }

        [Fact]
        public void ADeclaredRuleKeepsItsFileNameLineAndKeywords()
        {
            var rule = (FilterAttribute)typeof(Settings).GetField("quality").GetCustomAttributes(typeof(FilterAttribute), false)[0];
            Assert.Equal(FilterAction.Remove, rule.Action);
            Assert.Equal(FilterAttribute.Precedence.Override, rule.RulePrecedence);
            Assert.Equal(new[] { "K" }, rule.KeywordNames);
            Assert.Equal("Applying Remove attribute declared at ShaderKeywordFilterTests.cs:13 because condition was '4'", rule.GetFormattedResolutionMessageActive(4));
            // Negated: the rule applies when the value differs.
            Assert.True(rule.GetActiveFilterAction(4, out var action));
            Assert.Equal(FilterAction.Remove, action);
            Assert.False(rule.GetActiveFilterAction(3, out _));
        }

        [Fact]
        public void SelectOrRemoveAlwaysAppliesAndConstraintsMatchPairsAndApis()
        {
            var either = new RemoveOrSelectAttribute("x", false, "", 0);
            Assert.True(either.GetActiveFilterAction("x", out var onMatch));
            Assert.Equal(FilterAction.Remove, onMatch);
            Assert.True(either.GetActiveFilterAction("y", out var onOther));
            Assert.Equal(FilterAction.Select, onOther);
            Assert.Throws<System.NullReferenceException>(() => new RemoveIfAttribute(null).GetActiveFilterAction(1, out _));

            var tags = new ApplyRulesIfTagsEqualAttribute("A", "1", "B");
            Assert.True(tags.ShouldApplyRules("A", "1"));
            Assert.False(tags.ShouldApplyRules("B", "1"));
            Assert.True(new ApplyRulesIfTagsNotEqualAttribute("A", "1").ShouldApplyRules());
            Assert.True(new ApplyRulesIfNotGraphicsAPIAttribute(GraphicsDeviceType.Vulkan).ShouldApplyRules(GraphicsDeviceType.Metal));
            Assert.False(new ApplyRulesIfGraphicsAPIAttribute(GraphicsDeviceType.Vulkan).ShouldApplyRules(GraphicsDeviceType.Metal));
        }
    }
}
