using System;
using System.Collections.Generic;
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

            // ReadSerialized resets prior atlas secondary bindings cleanly.
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!213 &1\nSprite:\n  m_Name: Fresh\n  m_Rect:\n    x: 0\n    y: 0\n    width: 4\n    height: 4\n  m_Offset: {x: 0, y: 0}\n  m_Border: {x: 0, y: 0, z: 0, w: 0}\n  m_PixelsToUnits: 4\n  m_Pivot: {x: 0.5, y: 0.5}\n  m_Extrude: 0\n  m_IsPolygon: 0\n  m_RD:\n    serializedVersion: 3\n    texture: {fileID: 0}\n    alphaTexture: {fileID: 0}\n    secondaryTextures: []\n    m_SubMeshes: []\n    m_IndexBuffer: \n    m_VertexData:\n      serializedVersion: 3\n      m_VertexCount: 0\n      m_Channels: []\n      m_DataSize: 0\n      _typelessdata: \n    m_Bindpose: []\n    textureRect:\n      x: 0\n      y: 0\n      width: 4\n      height: 4\n    textureRectOffset: {x: 0, y: 0}\n    atlasRectOffset: {x: -1, y: -1}\n    settingsRaw: 0\n    uvTransform: {x: 4, y: 2, z: 4, w: 2}\n    downscaleMultiplier: 1\n  m_PhysicsShape: []\n  m_Bones: []\n";
            var doc = UnityYamlText.Parse(yaml)[0];
            sprite.ReadSerialized(doc.Body.ToNode(), ObjectResolver.None);
            Assert.Equal(0, sprite.GetSecondaryTextureCount());
            Assert.Empty(sprite.SecondaryProperties());
        }
    }
}
