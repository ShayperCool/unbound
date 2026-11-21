using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public sealed class SpriteAtlasUtilityTests
    {
        static List<string> CaptureLogs(Action action)
        {
            var logs = new List<string>();
            Application.LogCallback handler = (condition, _, type) => logs.Add(type + ":" + condition);
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return logs;
        }

        [Fact]
        public void DefaultConstructorCreatesUtilityInstance()
        {
            var util = new SpriteAtlasUtility();
            Assert.NotNull(util);
            Assert.Equal("UnityEditor.U2D.SpriteAtlasUtility", util.GetType().FullName);
        }

        [Fact]
        public void PackAtlasesValidatesNullEmptyAndDestroyedInputsBeforePacking()
        {
            var exNull = Assert.Throws<ArgumentNullException>(() =>
                SpriteAtlasUtility.PackAtlases(null, BuildTarget.StandaloneLinux64));
            Assert.Equal("atlases", exNull.ParamName);
            Assert.StartsWith("Value for parameter atlases is null", exNull.Message);

            var exElem = Assert.Throws<ArgumentNullException>(() =>
                SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { null }, BuildTarget.StandaloneLinux64));
            Assert.Equal("atlases", exElem.ParamName);
            Assert.StartsWith("One of the elements in atlases is null. Please check your Inputs.", exElem.Message);

            var nonPersistent = new SpriteAtlas { name = "Temp" };
            try
            {
                // Validation scans the entire array first, so a mixed null element throws before
                // logging the non-persistent warning for earlier entries.
                var mixedLogs = CaptureLogs(() =>
                {
                    var exMixed = Assert.Throws<ArgumentNullException>(() =>
                        SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { nonPersistent, null }, BuildTarget.StandaloneLinux64));
                    Assert.Equal("atlases", exMixed.ParamName);
                    Assert.StartsWith("One of the elements in atlases is null. Please check your Inputs.", exMixed.Message);
                });
                Assert.Empty(mixedLogs);
            }
            finally
            {
                Object.DestroyImmediate(nonPersistent);
            }

            var destroyed = new SpriteAtlas();
            Object.DestroyImmediate(destroyed);
            var exDestroyed = Assert.Throws<ArgumentNullException>(() =>
                SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { destroyed }, BuildTarget.StandaloneLinux64));
            Assert.Equal("atlases", exDestroyed.ParamName);
            Assert.StartsWith("One of the elements in atlases is null. Please check your Inputs.", exDestroyed.Message);

            var emptyLogs = CaptureLogs(() =>
                SpriteAtlasUtility.PackAtlases(Array.Empty<SpriteAtlas>(), BuildTarget.StandaloneLinux64));
            Assert.Empty(emptyLogs);
        }

        [Fact]
        public void PackAtlasesSkipsNonPersistentWithExactWarningAndSkipsV2Silently()
        {
            var temp = new SpriteAtlas { name = "InMemoryAtlas" };
            var v2 = new SpriteAtlas();
            v2.ReadSerialized(TestYaml.DocumentBody(
                "SpriteAtlasAsset:\n  m_Name: V2\n  m_ImporterData:\n    packables: []\n  m_IsVariant: 0\n"), null);
            EditorUtility.SetDirty(v2);
            try
            {
                var nonPersistentLogs = CaptureLogs(() =>
                    SpriteAtlasUtility.PackAtlases(new[] { temp }, BuildTarget.StandaloneLinux64));
                Assert.Equal(
                    new[] { "Warning:Packing non-persistent SpriteAtlases are not supported and were skipped." },
                    nonPersistentLogs);
                Assert.Equal(0, temp.spriteCount);

                // V2 imported atlases are skipped silently without clearing dirty.
                var v2Logs = CaptureLogs(() =>
                    SpriteAtlasUtility.PackAtlases(new[] { v2 }, BuildTarget.StandaloneLinux64));
                Assert.Empty(v2Logs);
                Assert.True(EditorUtility.IsDirty(v2));
            }
            finally
            {
                Object.DestroyImmediate(temp);
                Object.DestroyImmediate(v2);
            }
        }

        [Fact]
        public void CleanupAtlasPackingAndEnableV2ImportManageFlagCleanly()
        {
            var logs = CaptureLogs(() =>
            {
                SpriteAtlasUtility.EnableV2Import(true);
                Assert.True(SpriteAtlasUtility.V2ImportEnabled);
                SpriteAtlasUtility.CleanupAtlasPacking();
                Assert.False(SpriteAtlasUtility.V2ImportEnabled);
            });
            Assert.Empty(logs);
        }

        [Fact]
        public void PackAllAtlasesIsSilentNoOpWhenSpritePackerModeIsDisabledOrLegacy()
        {
            var original = EditorSettings.SpritePackerModeValue;
            try
            {
                foreach (var mode in new[] { EditorSettings.SpritePackerModeDisabled, EditorSettings.SpritePackerModeBuildTimeOnly, EditorSettings.SpritePackerModeAlwaysOn })
                {
                    EditorSettings.SpritePackerModeValue = mode;
                    var logs = CaptureLogs(() => SpriteAtlasUtility.PackAllAtlases(BuildTarget.StandaloneLinux64));
                    Assert.Empty(logs);
                }
            }
            finally
            {
                EditorSettings.SpritePackerModeValue = original;
            }
        }

        [Fact]
        public void V1PackedSpriteMetadataRoundTripsThroughNativeDocumentWrite()
        {
            const string initialYaml =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_ObjectHideFlags: 0\n" +
                "  m_Name: Main\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "    packedSpriteRenderDataKeys: []\n" +
                "  m_MasterAtlas: {fileID: 0}\n" +
                "  m_PackedSprites: []\n" +
                "  m_PackedSpriteNamesToIndex: []\n" +
                "  m_Tag: Main\n" +
                "  m_IsVariant: 0\n";

            var docs = UnityYamlText.Parse(initialYaml);
            var atlas = new SpriteAtlas();
            try
            {
                atlas.ReadSerialized(TestYaml.DocumentBody(initialYaml), null);
                Assert.Empty(atlas.SerializedPackedSprites);

                var spriteRef = new ObjectRef(21300000, "b828055b6a1d50b968e77fc6406e2b5c", 3);
                atlas.ApplyPackedSprites(new List<ObjectRef> { spriteRef }, new List<string> { "Single" }, markModified: true);
                Assert.True(atlas.PackedDataModified);

                int unlocated = 0;
                Assert.True(NativeDocuments.Write(docs[0], atlas, new Dictionary<Object, long>(), new AssetDatabaseRuntime.Locations(), ref unlocated));
                Assert.False(atlas.PackedDataModified);

                string written = UnityYamlText.Write(docs);
                Assert.Contains("packedSpriteRenderDataKeys:\n    - b828055b6a1d50b968e77fc6406e2b5c: 21300000", written);
                Assert.Contains("m_PackedSprites:\n  - {fileID: 21300000, guid: b828055b6a1d50b968e77fc6406e2b5c, type: 3}", written);
                Assert.Contains("m_PackedSpriteNamesToIndex:\n  - Single", written);

                var reread = new SpriteAtlas();
                try
                {
                    reread.ReadSerialized(TestYaml.DocumentBody(written), null);
                    Assert.Single(reread.SerializedPackedSprites);
                    Assert.Equal(21300000, reread.SerializedPackedSprites[0].FileId);
                    Assert.Equal("b828055b6a1d50b968e77fc6406e2b5c", reread.SerializedPackedSprites[0].Guid);
                }
                finally
                {
                    Object.DestroyImmediate(reread);
                }
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }

        [Fact]
        public void SpriteBindAtlasRespectsReportAsPackedFlag()
        {
            var sourceTex = new Texture2D(16, 16);
            var pageTex = new Texture2D(32, 32);
            var packedSprite = Sprite.Create(sourceTex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f);
            var unpackedModeSprite = Sprite.Create(sourceTex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f);
            try
            {
                packedSprite.BindAtlas(pageTex, new Rect(0, 0, 16, 16), new Vector4(100, 8, 100, 8),
                    SpritePackingMode.Rectangle, SpritePackingRotation.None, new Vector2(32, 32), 0.5f, reportAsPacked: true);
                Assert.Same(pageTex, packedSprite.texture);
                Assert.True(packedSprite.packed);
                Assert.Equal(0.5f, packedSprite.spriteAtlasTextureScale);

                unpackedModeSprite.BindAtlas(pageTex, new Rect(0, 0, 16, 16), new Vector4(100, 8, 100, 8),
                    SpritePackingMode.Rectangle, SpritePackingRotation.None, new Vector2(32, 32), 0.5f, reportAsPacked: false);
                Assert.Same(pageTex, unpackedModeSprite.texture);
                Assert.False(unpackedModeSprite.packed);
                Assert.Equal(1f, unpackedModeSprite.spriteAtlasTextureScale);
            }
            finally
            {
                Object.DestroyImmediate(packedSprite);
                Object.DestroyImmediate(unpackedModeSprite);
                Object.DestroyImmediate(sourceTex);
                Object.DestroyImmediate(pageTex);
            }
        }

        [Fact]
        public void PackAtlasesControlWorksUnderUnmeasuredRelease()
        {
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var util = new SpriteAtlasUtility();
                Assert.NotNull(util);
                Assert.Throws<ArgumentNullException>(() =>
                    SpriteAtlasUtility.PackAtlases(null, BuildTarget.StandaloneLinux64));
                SpriteAtlasUtility.PackAtlases(Array.Empty<SpriteAtlas>(), BuildTarget.StandaloneLinux64);
                SpriteAtlasUtility.CleanupAtlasPacking();
            }
        }
    }
}
