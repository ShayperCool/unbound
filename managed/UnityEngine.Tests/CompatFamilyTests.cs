using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Releases are grouped by family (MAJOR.MINOR): every release of the 6000.0 family
    /// gets the answers measured on 6000.0.71f1; another family keeps the unmeasured ones.</summary>
    [Collection("Project release")]
    public class CompatFamilyTests
    {
        static readonly string[] s_FamilyReleases =
            { "6000.0.0f1", "6000.0.0b16", "6000.0.62f1", "6000.0.67f1", "6000.0.71f2", "6000.0.71p1", "6000.0.99f1" };

        static readonly string[] s_OtherFamilies = { "6000.1.0f1", "6000.3.0f1", "2022.3.71f1", "6001.0.0f1" };

        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.0f1", true)]
        [InlineData("6000.0.67f1", true)]
        [InlineData("6000.0.0b16", true)]
        [InlineData("6000.1.0f1", false)]
        [InlineData("6000.3.0f1", false)]
        [InlineData("2022.3.71f1", false)]
        [InlineData("6000.0", false)]
        [InlineData(null, false)]
        public void AReleaseBelongsToItsMajorMinorFamily(string release, bool inFamily)
        {
            var family = inFamily ? ReleaseFamily.Unity6000_0 : ReleaseFamily.Unmeasured;
            using (UnityEngine.Tests.TestRelease.Pin(release))
                Assert.Equal(family, Compat.Family);
            Assert.Equal(family, Compat.FamilyOf(release));
        }

        [Fact]
        public void TheMeasuredFamilyNamesItsMeasuredRelease()
        {
            Assert.Equal("6000.0.71f1", Compat.MeasuredRelease(ReleaseFamily.Unity6000_0));
            Assert.Null(Compat.MeasuredRelease(ReleaseFamily.Unmeasured));
            Assert.Contains("6000.0", Compat.MeasuredFamilies);
        }

        /// <summary>Every release gate of Compat and GuiCompat gives each release of the family
        /// the measured release's answer, and a release of another family the unmeasured one.</summary>
        [Fact]
        public void EveryGateAnswersAFamilyReleaseAsTheMeasuredRelease()
        {
            var measured = Answers(UnityEngine.Tests.TestRelease.Measured);
            foreach (var release in s_FamilyReleases)
            {
                var answers = Answers(release);
                var differing = measured.Keys.Where(k => !Same(measured[k], answers[k])).ToList();
                Assert.True(differing.Count == 0, release + " differs from 6000.0.71f1 in " + string.Join(", ", differing));
            }
            // Control: another family is another version; most gates answer otherwise there.
            foreach (var release in s_OtherFamilies)
            {
                var answers = Answers(release);
                int differing = measured.Keys.Count(k => !Same(measured[k], answers[k]));
                Assert.True(differing > 100, release + " differs from 6000.0.71f1 in only " + differing + " answers");
            }
        }

        [Fact]
        public void ReleaseArgumentGatesAnswerByFamily()
        {
            foreach (var release in s_FamilyReleases.Append(UnityEngine.Tests.TestRelease.Measured))
            {
                Assert.True(Compat.RemovesLegacyGuiLayer(release), release);
                Assert.True(Compat.FlareCameraRepairMeasuredFor(release), release);
                Assert.Equal(Compat.EditorClassTable("6000.0.71f1").Keys, Compat.EditorClassTable(release).Keys);
            }
            foreach (var release in s_OtherFamilies)
            {
                Assert.False(Compat.RemovesLegacyGuiLayer(release), release);
                Assert.False(Compat.FlareCameraRepairMeasuredFor(release), release);
                Assert.Contains("6000.0", Assert.Throws<NotSupportedException>(() => Compat.EditorClassTable(release)).Message);
            }
        }

        /// <summary>The value (or the exception) of every parameterless static property.</summary>
        static Dictionary<string, object> Answers(string release)
        {
            var answers = new Dictionary<string, object>();
            using (UnityEngine.Tests.TestRelease.Pin(release))
            {
                foreach (var type in new[] { typeof(Compat), typeof(GuiCompat) })
                foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.GetIndexParameters().Length != 0 || property.GetMethod == null) continue;
                    if (property.Name is nameof(Compat.Release) or nameof(Compat.Family) or "Current") continue;
                    object value;
                    try { value = property.GetValue(null); }
                    catch (TargetInvocationException e) { value = e.InnerException?.GetType(); }
                    answers[type.Name + "." + property.Name] = value;
                }
            }
            return answers;
        }

        static bool Same(object a, object b)
        {
            if (a is IEnumerable x && b is IEnumerable y && a is not string)
                return x.Cast<object>().SequenceEqual(y.Cast<object>());
            return Equals(a, b);
        }
    }
}
