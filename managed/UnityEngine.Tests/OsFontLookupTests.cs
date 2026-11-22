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

        static Unbound.Runtime.NativeGlyphSource Source(bool glyphData)
        {
            var source = (Unbound.Runtime.NativeGlyphSource)Activator.CreateInstance(typeof(Unbound.Runtime.NativeGlyphSource),
                BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { 0UL }, null);
            source.HasGlyphData = glyphData;
            return source;
        }

        // The Editor's LegacyRuntime has no font data: on macOS and Linux its TextCore asset is
        // made of the installed OS fallback families; for a font with data, the font's own face.
        [Fact]
        public void OnlyAFontWithoutDataOnMeasuredOsIsBuiltFromTheOsFallbacks()
        {
            var legacy = new Font { m_Source = Source(false) };
            var withData = new Font { m_Source = Source(true) };
            try
            {
                Assert.Equal(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux(),
                    UnityEngine.TextCore.Text.FontAsset.BuildsFromOsFallbacks(legacy));
                Assert.False(UnityEngine.TextCore.Text.FontAsset.BuildsFromOsFallbacks(withData));
                Assert.False(UnityEngine.TextCore.Text.FontAsset.BuildsFromOsFallbacks(new Font()));
                // With none of the families installed there is no OS asset, and the built-in face is kept.
                Assert.Null(WithFaces(() => UnityEngine.TextCore.Text.FontAsset.CreateFontAssetWithOSFallbackList(Font.GetOSFallbacks()),
                    Face("Unlisted Test Family", "Regular", "/fonts/UnlistedTest-Regular.ttf")));
            }
            finally
            {
                Object.DestroyImmediate(legacy);
                Object.DestroyImmediate(withData);
            }
        }

        // Unity 6000.0.71f1: macOS Editor (tests/projects/osfontfallback), Linux Editor (tests/projects/imguiskin).
        [Fact]
        public void TheOsFallbackListIsTheEditorsOfThisOs()
        {
            var names = Font.GetOSFallbacks();
            if (OperatingSystem.IsMacOS())
                Assert.Equal(new[] { "Arial", "Arial Unicode MS", "Helvetica Neue", "Microsoft Sans Serif", "Microsoft YaHei",
                    "Malgun Gothic", "Gulim", "MS Gothic", "Nirmala UI", "Apple Color Emoji", "LastResort" }, names);
            else if (OperatingSystem.IsLinux())
                Assert.Equal(new[] { "Arial", "Arial Unicode MS", "Helvetica Neue", "Microsoft Sans Serif", "Microsoft YaHei",
                    "Malgun Gothic", "Gulim", "MS Gothic", "Nirmala UI", "FreeSans", "Droid Sans", "DejaVu Sans",
                    "Liberation Sans", "WenQuanYi Micro Hei", "HYSong", "NanumGothic", "TakaoGothic",
                    "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans CJK SC", "Noto Sans CJK TC",
                    "Droid Sans Fallback", "Noto Color Emoji", "LastResort" }, names);
            else
                Assert.DoesNotContain("Apple Color Emoji", names);
            // A copy: callers cannot change the list.
            names[0] = "Changed";
            Assert.NotEqual("Changed", Font.GetOSFallbacks()[0]);
        }
    }
}
