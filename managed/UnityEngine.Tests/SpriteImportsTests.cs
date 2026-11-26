using System;
using System.Collections.Generic;
using UnityEditor;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A sprite's first import (Unity 6000.0.71f1 Editor, tests/projects/spritemeta): the sprite ID
    /// the Editor gives a sprite, and a sprite's NPOT scaling read as none whatever its .meta holds.
    /// </summary>
    public sealed class SpriteImportsTests
    {
        static TextureImporter Sprite(string npot) => (TextureImporter)AssetImporter.FromMeta("Assets/Npot.png", "6123456789abcdef0123456789abcdef", "TextureImporter",
            TestYaml.DocumentBody("TextureImporter:\n  textureType: 8\n  spriteMode: 1\n  nPOTScale: " + npot + "\n"));

        [Fact]
        public void SpriteIdsAreTheEditors()
        {
            // A single sprite's, whatever the texture (sprites.EmptyId), and a 2D project's
            // sliced sprite NewGaps_0 of file id -2708432719997058213 (open.NewGaps).
            Assert.Equal("5e97eb03825dee720800000000000000", SpriteImports.SingleSpriteId);
            Assert.Equal("b53c7828ac6b96ad0800000000000000", SpriteImports.SpriteId(-2708432719997058213));
            Assert.Equal("f7a13a38428dff560800000000000000", SpriteImports.SpriteId(7349830768230865535));
        }

        [Fact]
        public void OnlyASingleSpriteWithoutAnIdIsGivenOne()
        {
            YNode Body(string text) => TestYaml.DocumentBody("TextureImporter:\n" + text);
            Assert.True(SpriteImports.NeedsSpriteId(Body("  textureType: 8\n  spriteMode: 1\n  spriteSheet:\n    spriteID: \n")));
            Assert.True(SpriteImports.NeedsSpriteId(Body("  textureType: 8\n  spriteMode: 1\n")));
            Assert.False(SpriteImports.NeedsSpriteId(Body("  textureType: 8\n  spriteMode: 1\n  spriteSheet:\n    spriteID: 5e97eb03825dee720800000000000000\n")));
            Assert.False(SpriteImports.NeedsSpriteId(Body("  textureType: 8\n  spriteMode: 2\n")));
            Assert.False(SpriteImports.NeedsSpriteId(Body("  textureType: 0\n  spriteMode: 1\n")));
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Warnings = new List<string>();
            public void LogException(Exception exception, Object context) { }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Warnings.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }
        }

        const string SingleWithoutId = "TextureImporter:\n  textureType: 8\n  spriteMode: 1\n";

        static List<YDocument> Meta(string text) =>
            new List<YDocument> { new YDocument { TypeName = "TextureImporter", Body = TestYaml.DocumentBody(text) } };

        /// <summary>The start's pass over the paths with the given kinds, .meta texts (null: a .meta
        /// that does not read) and session importers: the importers written.</summary>
        static List<string> Pass(Dictionary<string, (string Kind, string Meta, TextureImporter Importer)> assets, Capture logs)
        {
            var written = new List<string>();
            var handler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                SpriteImports.Imported(assets.Keys, path => assets[path].Kind, path => "guid-" + path,
                    guid =>
                    {
                        var entry = assets[guid.Substring("guid-".Length)];
                        if (entry.Meta == null) throw new InvalidOperationException("loading meta: line 4: expected `key:`");
                        return Meta(entry.Meta);
                    },
                    path => assets[path].Importer, importer => { written.Add(importer.assetPath); return true; });
            }
            finally { Debug.unityLogger.logHandler = handler; }
            return written;
        }

        [Fact]
        public void TheStartGivesOnlySingleSpritesWithoutAnIdTheirsAndWarnsOfAnUnreadableMeta()
        {
            static TextureImporter Importer(string path, string text) =>
                (TextureImporter)AssetImporter.FromMeta(path, "8123456789abcdef0123456789abcdef", "TextureImporter", TestYaml.DocumentBody(text));
            var sprite = Importer("Assets/Sprite.png", SingleWithoutId + "  nPOTScale: 1\n");
            // A script made the session's importer a Default texture (not saved): it is not given an ID.
            var changed = Importer("Assets/Changed.png", "TextureImporter:\n  textureType: 0\n");
            var logs = new Capture();
            var written = Pass(new Dictionary<string, (string, string, TextureImporter)>
            {
                ["Assets/Sprite.png"] = ("texture", SingleWithoutId, sprite),
                ["Assets/Changed.png"] = ("texture", SingleWithoutId, changed),
                ["Assets/Broken.png"] = ("texture", null, null),
                // Not a texture: its .meta is not read (a texture .meta here would be).
                ["Assets/Data.asset"] = ("yaml", SingleWithoutId, sprite),
            }, logs);
            Assert.Equal(new[] { "Assets/Sprite.png" }, written);
            Assert.Equal("5e97eb03825dee720800000000000000", MetaSettings.Find(sprite.m_Settings, "spriteSheet.spriteID")?.AsString());
            Assert.Equal(0, MetaSettings.Int(sprite.m_Settings, "nPOTScale", -1));
            Assert.Equal(new[] { "Unbound: the .meta of Assets/Broken.png cannot be read, so it is not given a sprite ID: loading meta: line 4: expected `key:`" }, logs.Warnings);
            // Another release keeps the .meta (unmeasured).
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.Empty(Pass(new Dictionary<string, (string, string, TextureImporter)>
                {
                    ["Assets/Other.png"] = ("texture", SingleWithoutId, Importer("Assets/Other.png", SingleWithoutId)),
                }, new Capture()));
        }

        [Fact]
        public void ASpritesNpotScalingReadsAsNone()
        {
            var sprite = Sprite("1");
            Assert.Equal(TextureImporterNPOTScale.None, sprite.npotScale);
            // A Default texture reads what its .meta holds.
            var texture = (TextureImporter)AssetImporter.FromMeta("Assets/D.png", "7123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  textureType: 0\n  nPOTScale: 1\n"));
            Assert.Equal(TextureImporterNPOTScale.ToNearest, texture.npotScale);
            // Another release reads the sprite's too (unmeasured).
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.Equal(TextureImporterNPOTScale.ToNearest, Sprite("1").npotScale);
        }
    }
}
