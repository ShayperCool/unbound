using System;
using System.IO;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The USS importer's check of values against the release's property syntaxes, read
    /// from the installed 6000.0.71f1 UI Toolkit module (skipped without it): the messages of
    /// tests/projects/ussvalidation and controls outside it.</summary>
    public sealed class UssValidationTests : IDisposable
    {
        readonly string m_DataPath = UnityEditor.UnityInstallInfo.DataPath;

        public void Dispose()
        {
            UnityEditor.UnityInstallInfo.DataPath = m_DataPath;
            UssValidation.Reset();
        }

        static bool Installed()
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Unity/Hub/Editor/6000.0.71f1/Editor/Data");
            if (!File.Exists(Path.Combine(data, "Managed/UnityEngine/UnityEngine.UIElementsModule.dll"))) return false;
            UnityEditor.UnityInstallInfo.DataPath = data;
            UssValidation.Reset();
            return true;
        }

        static string Message(string property, string value)
        {
            var r = UssValidation.Validate(property, value);
            Assert.NotNull(r);
            return r.Value.Ok ? null : r.Value.Message + (r.Value.Hint != null ? " -> " + r.Value.Hint : "");
        }

        [Fact]
        public void ValidValuesPass()
        {
            if (!Installed()) return;
            Assert.Null(Message("width", "30px"));
            Assert.Null(Message("width", "50%"));
            Assert.Null(Message("width", "auto"));
            Assert.Null(Message("width", "0"));
            Assert.Null(Message("width", "var(--w)"));
            Assert.Null(Message("color", "#ff0000"));
            Assert.Null(Message("color", "rgba(1, 2, 3, 0.5)"));
            Assert.Null(Message("color", "red"));
            Assert.Null(Message("border-color", "red green blue yellow"));
            Assert.Null(Message("transition", "width 1s ease-in, color 200ms"));
            Assert.Null(Message("transition-property", "all, width"));
            Assert.Null(Message("background-position", "left 10px top"));
            Assert.Null(Message("--anything", "12 px !"));
            Assert.Null(Message("width", "initial"));
        }

        [Fact]
        public void InvalidValuesNameTheSyntaxTheValueAndAHint()
        {
            if (!Installed()) return;
            Assert.Equal("Expected (<length-percentage> | auto) but found '10' -> Property expects a unit. Did you forget to add px or %?", Message("width", "10"));
            Assert.Equal("Expected end of value but found '20px'", Message("width", "10px 20px"));
            Assert.Equal("Expected (<color>) but found 'notacolor' -> Unsupported color 'notacolor'.", Message("color", "notacolor"));
            Assert.Equal("Expected (<time>#) but found '2' -> Property expects a unit. Did you forget to add s or ms?", Message("transition-duration", "2"));
            Assert.Equal("Expected end of value but found 'wobble'", Message("transition", "width 1s wobble"));
            Assert.Equal("Expected end of value but found 'black'", Message("border-color", "red green blue yellow black"));
            Assert.Equal("Unknown property 'colr' (did you mean 'color'?)", Message("colr", "red"));
            Assert.Equal("Expected (<integer>) but found '1.5'", Message("-unity-slice-left", "1.5"));
        }

        [Fact]
        public void EveryPropertySyntaxOfTheReleaseIsRead()
        {
            if (!Installed()) return;
            Assert.Empty(UssValidation.UnreadSyntaxes());
            Assert.Null(Message("flex", "1 0 auto"));
            Assert.Null(Message("flex-basis", "10px"));
            Assert.Equal("Expected (<length-percentage> | auto) but found 'inherit'", Message("width", "inherit"));
        }

        [Fact]
        public void ResolvedValuesAreCheckedByTheirTypes()
        {
            if (!Installed()) return;
            Assert.True(UssValidation.Fits("--custom", new Parts()));
            Assert.False(UssValidation.Fits("width", new Parts()));
            // Values as a sheet holds them (as a var() substitution gives them to the style pass).
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            try
            {
                new Unbound.UIElements.UssImporter("test.uss").Import(sheet, ".a { width: 0; height: 12px; color: red; border-color: #ff0000; opacity: 0.5;"
                    + " transition-duration: 1s; rotate: 2deg; flex-direction: row; left: 3; top: 20%; display: wide; -unity-slice-left: 4; }");
                bool Fits(string property, string as_)
                {
                    foreach (var p in sheet.rules[0].properties)
                        if (p.name == as_)
                        {
                            var values = new System.Collections.Generic.List<Unbound.UIElements.UssValue>();
                            foreach (var h in p.values) values.Add(new Unbound.UIElements.UssValue { sheet = sheet, handle = h });
                            return UssValidation.Fits(property, new Unbound.UIElements.UssReader.ResolvedParts(values));
                        }
                    throw new InvalidOperationException(as_);
                }
                Assert.True(Fits("width", "width"));               // a float 0 is a length
                Assert.True(Fits("width", "height"));
                Assert.False(Fits("width", "left"));               // another float is not
                Assert.True(Fits("width", "top"));                 // a percentage
                Assert.True(Fits("color", "color"));               // a color name
                Assert.True(Fits("color", "border-color"));
                Assert.False(Fits("width", "color"));
                Assert.False(Fits("border-left-width", "border-color"));
                Assert.True(Fits("opacity", "opacity"));
                Assert.False(Fits("opacity", "height"));
                Assert.True(Fits("transition-duration", "transition-duration"));
                Assert.False(Fits("transition-duration", "height"));
                Assert.True(Fits("rotate", "rotate"));
                Assert.True(Fits("flex-direction", "flex-direction"));
                Assert.False(Fits("flex-direction", "display"));   // an unknown keyword
                Assert.True(Fits("-unity-slice-left", "-unity-slice-left"));
                Assert.True(Fits("transition-property", "flex-direction")); // a custom ident
            }
            finally
            {
                Object.DestroyImmediate(sheet);
            }
        }

        sealed class Parts : UssValidation.IParts
        {
            public int Count => 0;
            public string Text(int index) => null;
            public bool IsVariable(int index) => false;
            public bool IsComma(int index) => false;
            public bool Keyword(int index, string keyword) => false;
            public bool Data(int index, string type) => false;
        }
    }
}
