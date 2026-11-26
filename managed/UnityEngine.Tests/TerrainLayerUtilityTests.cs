using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The terrain layer checks and tiling fields as Unity's own C# writes them: each
    /// check draws one warning box per problem in a layout pass; the tiling fields keep the
    /// layer's values on a pass without input.</summary>
    [Collection("Serial")]
    public sealed class TerrainLayerUtilityTests
    {
        /// <summary>Runs with a stand-in warning icon: the Editor's icons are the installation's,
        /// which the unit tests do not load.</summary>
        static void WithWarningIcon(Action body)
        {
            var caches = new[] { "s_LightHelpIcons", "s_DarkHelpIcons" };
            var icon = new Texture2D(2, 2);
            var saved = new Texture2D[caches.Length];
            for (int i = 0; i < caches.Length; i++)
            {
                var cache = (Texture2D[])typeof(EditorGUIUtility).GetField(caches[i], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                saved[i] = cache[1];
                cache[1] ??= icon;
            }
            try { body(); }
            finally
            {
                for (int i = 0; i < caches.Length; i++)
                    ((Texture2D[])typeof(EditorGUIUtility).GetField(caches[i], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null))[1] = saved[i];
                Object.DestroyImmediate(icon);
            }
        }

        static int LayoutEntries(Action body)
        {
            var previous = Event.current;
            Event.current = new Event { type = EventType.Layout };
            GUIUtility.BeginGUI(0, 702, 1);
            GUI.ResetState();
            try
            {
                body();
                return GUILayoutUtility.topLevel.entries.Count;
            }
            finally
            {
                GUILayoutUtility.End();
                Event.current = previous;
            }
        }

        [Fact]
        public void ATextureThatIsNoAssetCountsAsANormalMap()
        {
            var texture = new Texture2D(4, 4);
            try
            {
                Assert.True(TerrainLayerUtility.CheckNormalMapTextureType(texture));
                Assert.True(TerrainLayerUtility.CheckNormalMapTextureType(null));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        static string Warnings(Action<Action<GUIContent>> check)
        {
            var shown = new System.Collections.Generic.List<string>();
            WithWarningIcon(() => check(content => shown.Add(content.text)));
            return string.Join(" | ", shown);
        }

        [Fact]
        public void EachProblemShowsOneWarning()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var good = new Texture2D(4, 4, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var bad = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            const string Repeat = "Texture wrap mode must be set to Repeat", Mips = "Texture must have mip maps";
            try
            {
                Assert.Equal("Assign a tiling texture", Warnings(show => TerrainLayerUtility.DiffuseTextureWarnings(null, show)));
                Assert.Equal("", Warnings(show => TerrainLayerUtility.DiffuseTextureWarnings(good, show)));
                Assert.Equal(Repeat + " | " + Mips, Warnings(show => TerrainLayerUtility.DiffuseTextureWarnings(bad, show)));
                Assert.Equal("", Warnings(show => TerrainLayerUtility.NormalMapTextureWarnings(null, true, show)));
                Assert.Equal("Normal texture should be imported as Normal Map", Warnings(show => TerrainLayerUtility.NormalMapTextureWarnings(good, false, show)));
                Assert.Equal(Repeat + " | " + Mips + " | Normal texture should be imported as Normal Map", Warnings(show => TerrainLayerUtility.NormalMapTextureWarnings(bad, false, show)));
                Assert.Equal("", Warnings(show => TerrainLayerUtility.MaskMapTextureWarnings(null, show)));
                Assert.Equal(Repeat + " | " + Mips, Warnings(show => TerrainLayerUtility.MaskMapTextureWarnings(bad, show)));
            }
            finally
            {
                Object.DestroyImmediate(good);
                Object.DestroyImmediate(bad);
            }
        }

        [Fact]
        public void TilingFieldsKeepTheLayersValuesWithoutInput()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var layer = new TerrainLayer { tileSize = new Vector2(3f, 4f), tileOffset = new Vector2(0.5f, 0.25f) };
            try
            {
                // A label and two vector fields.
                Assert.Equal(3, LayoutEntries(() => TerrainLayerUtility.TilingSettingsUI(layer)));
                Assert.Equal(new Vector2(3f, 4f), layer.tileSize);
                Assert.Equal(new Vector2(0.5f, 0.25f), layer.tileOffset);
            }
            finally { Object.DestroyImmediate(layer); }
        }
    }
}
