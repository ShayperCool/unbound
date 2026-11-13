using System;
using System.Reflection;
using UnityEngine.TextCore.LowLevel;
using Xunit;

namespace UnityEngine.Tests
{
    public class OsFontLookupTests
    {
        static readonly FieldInfo s_List = typeof(Font).GetField("s_OsFonts", BindingFlags.NonPublic | BindingFlags.Static);

        static Font.OsFace Face(string family, string style, string path, int index = 0) =>
            new Font.OsFace { Family = family, Style = style, Path = path, Index = index };

        static T WithFaces<T>(Func<T> body, params Font.OsFace[] faces)
        {
            var previous = s_List.GetValue(null);
            s_List.SetValue(null, new Font.OsFontList { Faces = faces });
            try { return body(); }
            finally { s_List.SetValue(null, previous); }
        }

        static string Lookup(string family, string style)
        {
            bool found = FontEngine.TryGetSystemFontReference(family, style, out var reference);
            return found ? reference.filePath + "#" + reference.faceIndex : "none";
        }

        // Unity 6000.0.71f1 macOS Editor, tests/projects/osfontfallback: without a style a family
        // stands for its Regular face, though other faces are listed first; a style picks its face.
        [Fact]
        public void AFamilyWithoutAStyleIsItsRegularFace()
        {
            var faces = new[]
            {
                Face("Arial", "Bold Italic", "/fonts/Arial Bold Italic.ttf"),
                Face("Arial", "Bold", "/fonts/Arial Bold.ttf"),
                Face("Arial", "Regular", "/fonts/Arial.ttf"),
                Face("Helvetica Neue", "Italic", "/fonts/HelveticaNeue.ttc", 2),
                Face("Helvetica Neue", "Regular", "/fonts/HelveticaNeue.ttc", 0),
                Face("Avenir", "Roman", "/fonts/Avenir.ttc", 11),
                Face("Avenir", "Book", "/fonts/Avenir.ttc", 0),
            };
            Assert.Equal("/fonts/Arial.ttf#0", WithFaces(() => Lookup("Arial", null), faces));
            Assert.Equal("/fonts/Arial.ttf#0", WithFaces(() => Lookup("arial", ""), faces));
            Assert.Equal("/fonts/HelveticaNeue.ttc#0", WithFaces(() => Lookup("Helvetica Neue", null), faces));
            Assert.Equal("/fonts/Arial Bold.ttf#0", WithFaces(() => Lookup("Arial", "Bold"), faces));
            Assert.Equal("none", WithFaces(() => Lookup("Arial", "Light"), faces));
            // No Regular face (Book and Roman are not taken for it): the first one listed.
            Assert.Equal("/fonts/Avenir.ttc#11", WithFaces(() => Lookup("Avenir", null), faces));
            Assert.Equal("none", WithFaces(() => Lookup("Futura", null), faces));
        }

        // Unity 6000.0.71f1 macOS Editor, tests/projects/osfontfallback.
        [Fact]
        public void TheOsFallbackListIsTheEditorsOfThisOs()
        {
            var names = Font.GetOSFallbacks();
            if (OperatingSystem.IsMacOS())
                Assert.Equal(new[] { "Arial", "Arial Unicode MS", "Helvetica Neue", "Microsoft Sans Serif", "Microsoft YaHei",
                    "Malgun Gothic", "Gulim", "MS Gothic", "Nirmala UI", "Apple Color Emoji", "LastResort" }, names);
            else
                Assert.DoesNotContain("Apple Color Emoji", names);
            // A copy: callers cannot change the list.
            names[0] = "Changed";
            Assert.NotEqual("Changed", Font.GetOSFallbacks()[0]);
        }
    }
}
