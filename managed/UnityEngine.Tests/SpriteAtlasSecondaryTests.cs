using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A packed sprite's secondary textures: the packed properties bind its atlas page's
    /// secondary pages, the others stay its own, and each bind starts again from its own (measured
    /// in the Unity 6000.0.71f1 Editor, tests/projects/spriteatlassecondary).</summary>
    public class SpriteAtlasSecondaryTests : IDisposable
    {
        readonly List<Object> m_Made = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Made) if (o) Object.DestroyImmediate(o);
        }

        Texture2D Texture(string name)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name };
            m_Made.Add(t);
            return t;
        }

        static Dictionary<string, Texture2D> Secondary(Sprite sprite)
        {
            var list = new SecondarySpriteTexture[sprite.GetSecondaryTextureCount()];
            sprite.GetSecondaryTextures(list);
            var result = new Dictionary<string, Texture2D>();
            foreach (var s in list) result[s.name] = s.texture;
            return result;
        }

        [Fact]
        public void PackedPropertiesBindThePagesAndEachBindStartsFromTheSpritesOwn()
        {
            var normals = Texture("normals");
            var mask = Texture("mask");
            var sprite = Sprite.Create(Texture("main"), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect,
                Vector4.zero, false, new[]
                {
                    new SecondarySpriteTexture { name = "_NormalMap", texture = normals },
                    new SecondarySpriteTexture { name = "_MaskTex", texture = mask },
                });
            m_Made.Add(sprite);
            var normalsPage = Texture("page?_NormalMap");
            var maskPage = Texture("page?_MaskTex");
            Texture2D PageOf(string property) => property == "_NormalMap" ? normalsPage : property == "_MaskTex" ? maskPage : null;

            // Only the packed property takes the page's; the other stays the sprite's own.
            sprite.BindAtlasSecondary(new[] { "_NormalMap" }, PageOf);
            var bound = Secondary(sprite);
            Assert.Same(normalsPage, bound["_NormalMap"]);
            Assert.Same(mask, bound["_MaskTex"]);
            Assert.Same(normalsPage, sprite.SecondaryProperties()[Shader.PropertyToID("_NormalMap")]);

            // A second bind starts again from the sprite's own textures, not from the first bind's.
            sprite.BindAtlasSecondary(new[] { "_MaskTex" }, PageOf);
            bound = Secondary(sprite);
            Assert.Same(normals, bound["_NormalMap"]);
            Assert.Same(maskPage, bound["_MaskTex"]);

            // A packed property without a page keeps the sprite's own.
            sprite.BindAtlasSecondary(new[] { "_NormalMap", "_MaskTex" }, p => p == "_MaskTex" ? maskPage : null);
            bound = Secondary(sprite);
            Assert.Same(normals, bound["_NormalMap"]);
            Assert.Same(maskPage, bound["_MaskTex"]);

            // None packed (a page without secondary pages) restores them.
            sprite.BindAtlasSecondary(Array.Empty<string>(), PageOf);
            bound = Secondary(sprite);
            Assert.Same(normals, bound["_NormalMap"]);
            Assert.Same(mask, bound["_MaskTex"]);
            Assert.Same(normals, sprite.SecondaryProperties()[Shader.PropertyToID("_NormalMap")]);
        }

        [Fact]
        public void ASpriteNeverBoundIsLeftAsItIs()
        {
            var normals = Texture("normals");
            var sprite = Sprite.Create(Texture("main"), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect,
                Vector4.zero, false, new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normals } });
            m_Made.Add(sprite);
            var before = sprite.SecondaryProperties();
            sprite.BindAtlasSecondary(Array.Empty<string>(), _ => throw new InvalidOperationException("no page is asked for"));
            Assert.Same(before, sprite.SecondaryProperties());
        }

        sealed class SingleTextureResolver : IReferenceResolver
        {
            readonly Texture2D m_Texture;
            public SingleTextureResolver(Texture2D texture) => m_Texture = texture;
            public Object Resolve(ObjectRef reference, Type expected) => expected == typeof(Texture2D) ? m_Texture : null;
        }

        [Fact]
        public void ImportedSecondaryGuidsBindPagesWithoutLoadingReplacedTexturesAndResetOnDeserialize()
        {
            var main = Texture("main");
            var sprite = Sprite.Create(main, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            m_Made.Add(sprite);
            sprite.SetImportedSecondaryTextures(new[]
            {
                ("_NormalMap", "11111111111111111111111111111111"),
                ("_MaskTex", "22222222222222222222222222222222"),
            });
            var normalsPage = Texture("page?_NormalMap");
            var maskPage = Texture("page?_MaskTex");
            Texture2D PageOf(string property) => property == "_NormalMap" ? normalsPage : property == "_MaskTex" ? maskPage : null;

            sprite.BindAtlasSecondary(new[] { "_NormalMap", "_MaskTex" }, PageOf);
            var bound = Secondary(sprite);
            Assert.Same(normalsPage, bound["_NormalMap"]);
            Assert.Same(maskPage, bound["_MaskTex"]);
            Assert.Same(normalsPage, sprite.SecondaryProperties()[Shader.PropertyToID("_NormalMap")]);

            // Rebinding to a subset starts from the original GUID list; an unresolvable own GUID resolves to null.
            sprite.BindAtlasSecondary(new[] { "_MaskTex" }, PageOf);
            bound = Secondary(sprite);
            Assert.Null(bound["_NormalMap"]);
            Assert.Same(maskPage, bound["_MaskTex"]);

            // Restoring with empty packed list restores the lazy GUID table cleanly.
            sprite.BindAtlasSecondary(Array.Empty<string>(), PageOf);
            Assert.Equal(2, sprite.GetSecondaryTextureCount());

            // ReadSerialized resets prior atlas secondary bindings cleanly (including m_OwnSecondaryGuids
            // and m_SecondaryBound so a subsequent empty BindAtlasSecondary cannot restore old entries).
            var imported = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Imported" };
            m_Made.Add(imported);
            var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SpriteFixtures", "Rectangle.asset"));
            var doc = UnityYamlText.Parse(yaml)[0];
            sprite.ReadSerialized(doc.Body.ToNode(), new SingleTextureResolver(imported));
            Assert.Equal(0, sprite.GetSecondaryTextureCount());
            Assert.Null(sprite.SecondaryProperties());
            sprite.BindAtlasSecondary(Array.Empty<string>(), PageOf);
            Assert.Equal(0, sprite.GetSecondaryTextureCount());
            Assert.Null(sprite.SecondaryProperties());
        }
    }
}
