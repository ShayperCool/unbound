using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>EditorUtility.NaturalCompare's signs as the Unity 6000.0.71f1 Editor gives them
    /// (tests/projects/packagefolders natural.*), and pairs outside the probe's.</summary>
    public sealed class NaturalCompareTests
    {
        [Theory]
        [InlineData("a", "B", -1)]
        [InlineData("_c", "A", -1)]
        [InlineData("C9", "C10", -1)]
        [InlineData("a02", "a2", -1)]
        [InlineData("a002", "a02", -1)]
        [InlineData("abc", "ABC", 0)]
        [InlineData("a b", "a_b", -1)]
        [InlineData("x1y2", "x1y10", -1)]
        [InlineData("", "a", -1)]
        [InlineData("a", "ab", -1)]
        [InlineData("1.5", "1.10", -1)]
        [InlineData("Z", "a", 1)]
        [InlineData("é", "f", 1)]
        [InlineData("a 1", "a1", -1)]
        [InlineData("0", "00", 1)]
        [InlineData("a01b", "a1a", 1)]
        [InlineData("99999999999999999999", "1", 1)]
        [InlineData("É", "é", -1)]
        [InlineData("Ä", "b", 1)]
        public void SignsMatchTheEditor(string a, string b, int sign) => AssertSign(a, b, sign);

        /// <summary>Pairs no capture recorded, ordered by the same rule: leading zeros of a later
        /// number, a number at the end, equal names.</summary>
        [Theory]
        [InlineData("x1-007", "x1-07", -1)]
        [InlineData("Level 10", "level 9", 1)]
        [InlineData("img12", "img12", 0)]
        public void OtherPairsFollowTheSameRule(string a, string b, int sign) => AssertSign(a, b, sign);

        static void AssertSign(string a, string b, int sign)
        {
            Assert.Equal(sign, Math.Sign(EditorUtility.NaturalCompare(a, b)));
            Assert.Equal(-sign, Math.Sign(EditorUtility.NaturalCompare(b, a)));
        }

        [Fact]
        public void SortsFoldersAsGetSubFoldersLists()
        {
            var names = new[] { "b", "C10", "a2", "_c", "C9", "A" };
            Array.Sort(names, EditorUtility.NaturalCompare);
            Assert.Equal(new[] { "_c", "A", "a2", "b", "C9", "C10" }, names);
        }
    }
}
