using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A light's cookie whose imported texels Unbound cannot read is said and not applied by the
    /// bake (as a cube cookie's already was), instead of baking as an all-black cookie; a texture
    /// made by script without texels keeps its zeros.
    /// </summary>
    public class CookieUnreadableTests
    {
        static readonly MethodInfo Cookie = typeof(BakedAtlas).GetMethod("Cookie", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>An imported texture as it is when its texels could not be read: none kept,
        /// its asset's GUID known (the fetch from the import fails without an engine).</summary>
        static Texture2D Unreadable()
        {
            var texture = new Texture2D(4, 4) { m_AssetGuid = "0123456789abcdef0123456789abcdef" };
            texture.m_Pixels = null;
            texture.m_Hdr = null;
            typeof(Texture2D).GetField("m_Store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(texture, null);
            return texture;
        }

        [Fact]
        public void AnImportedTextureWithoutReadableTexelsGivesNoEngineTexels()
        {
            var texture = Unreadable();
            Assert.Null(texture.EnginePixels32OrNull());
            // The engine's other users keep reading zeros.
            Assert.Equal(16, Unreadable().EnginePixels32().Length);
            Assert.NotNull(new Texture2D(4, 4).EnginePixels32OrNull());
        }

        [Fact]
        public void TheBakeSaysAndDropsSuchACookie()
        {
            var go = new GameObject("Sun");
            try
            {
                var light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cookie = Unreadable();
                var said = new List<string>();
                var cookie = Cookie.Invoke(null, new object[] { light, 0, "Assets/Lit.unity", 1L, (Action<string, string>)((key, text) => said.Add(text)) });
                Assert.Null(cookie);
                Assert.Contains(said, s => s.Contains("cannot read"));
                // A readable cookie is still applied.
                light.cookie = new Texture2D(4, 4);
                said.Clear();
                Assert.NotNull(Cookie.Invoke(null, new object[] { light, 0, "Assets/Lit.unity", 1L, (Action<string, string>)((key, text) => said.Add(text)) }));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
