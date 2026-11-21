using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class SpriteAtlasAssetTests
    {
        sealed class DummyPacker : ScriptablePacker
        {
            public override bool Pack(
                SpriteAtlasPackingSettings config,
                SpriteAtlasTextureSettings setting,
                PackerData input) => false;
        }

        sealed class OtherScriptable : ScriptableObject { }

        static List<(LogType type, string msg)> CaptureLogs(Action action)
        {
            var logs = new List<(LogType, string)>();
            Application.LogCallback handler = (c, _, t) => logs.Add((t, c));
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return logs;
        }

        [Fact]
        public void ConstructionStartsDirtyWithDefaults()
        {
            var asset = new SpriteAtlasAsset();
            Assert.Equal("", asset.name);
            Assert.Equal(HideFlags.None, asset.hideFlags);
            Assert.False(asset.isVariant);
            Assert.Null(asset.GetMasterAtlas());
            Assert.True(EditorUtility.IsDirty(asset));
            Assert.Equal(1, EditorUtility.GetDirtyCount(asset));
        }

        [Fact]
        public void SetIsVariantAndSetMasterAtlasFollowMeasuredContract()
        {
            var asset = new SpriteAtlasAsset();
            int baseDirty = EditorUtility.GetDirtyCount(asset);

            asset.SetIsVariant(false);
            Assert.Equal(baseDirty, EditorUtility.GetDirtyCount(asset));

            asset.SetIsVariant(true);
            Assert.True(asset.isVariant);
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(asset));

            asset.SetIsVariant(true);
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(asset));

            var master = new SpriteAtlas { name = "Master" };
            var logs = CaptureLogs(() => asset.SetMasterAtlas(master));
            Assert.Empty(logs);
            Assert.Same(master, asset.GetMasterAtlas());
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(asset));

            // Unlike SpriteAtlasExtensions.SetIsVariant(false), SpriteAtlasAsset preserves m_MasterAtlas.
            asset.SetIsVariant(false);
            Assert.False(asset.isVariant);
            Assert.Same(master, asset.GetMasterAtlas());
            Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(asset));

            // Variant-as-master on a non-variant is stored verbatim without warning or dirtying.
            var variant = new SpriteAtlas { name = "Var" };
            variant.SetIsVariant(true);
            logs = CaptureLogs(() => asset.SetMasterAtlas(variant));
            Assert.Empty(logs);
            Assert.Same(variant, asset.GetMasterAtlas());
            Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(asset));

            Object.DestroyImmediate(variant);
            Assert.Null(asset.GetMasterAtlas());

            asset.SetMasterAtlas(null);
            Assert.Null(asset.GetMasterAtlas());
            Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(asset));
            Object.DestroyImmediate(master);
        }

        [Fact]
        public void AddRemoveAndRemoveAtFollowDirtyAndWarningContracts()
        {
            var asset = new SpriteAtlasAsset();
            int d = EditorUtility.GetDirtyCount(asset);

            Assert.Empty(CaptureLogs(() => asset.Add(null)));
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));
            Assert.Empty(CaptureLogs(() => asset.Add(Array.Empty<Object>())));
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));

            var tex1 = new Texture2D(2, 2) { name = "T1" };
            var tex2 = new Texture2D(2, 2) { name = "T2" };
            var bad = ScriptableObject.CreateInstance<OtherScriptable>();
            bad.name = "BadObj";

            var addLogs = CaptureLogs(() => asset.Add(new Object[] { tex1, tex2, tex1, null, bad }));
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));
            Assert.Equal(2, addLogs.Count);
            Assert.Equal(LogType.Warning, addLogs[0].type);
            Assert.Equal("Attempted to add null object into a Sprite Atlas Importer.", addLogs[0].msg);
            Assert.Equal(LogType.Warning, addLogs[1].type);
            Assert.Equal("Attempted to add invalid object into a Sprite Atlas Importer. BadObj was a OtherScriptable. Only Folder, Texture and Sprite are valid.", addLogs[1].msg);

            Assert.Empty(CaptureLogs(() => asset.Remove(null)));
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));
            Assert.Empty(CaptureLogs(() => asset.Remove(Array.Empty<Object>())));
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));

            Assert.Empty(CaptureLogs(() => asset.Remove(new Object[] { tex1, bad })));
            d += 3; // N + 1
            Assert.Equal(d, EditorUtility.GetDirtyCount(asset));

            // RemoveAt: out-of-range is a clean no-op; valid index dirties once.
            asset.RemoveAt(-1);
            asset.RemoveAt(5);
            Assert.Equal(d, EditorUtility.GetDirtyCount(asset));
            asset.RemoveAt(0);
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));

            Object.DestroyImmediate(tex1);
            Object.DestroyImmediate(tex2);
            Object.DestroyImmediate(bad);
        }

        [Fact]
        public void SetScriptablePackerAndSetPackerValidateType()
        {
            var asset = new SpriteAtlasAsset();
            int d = EditorUtility.GetDirtyCount(asset);

            var packer = ScriptableObject.CreateInstance<DummyPacker>();
            Assert.Empty(CaptureLogs(() => asset.SetScriptablePacker(packer)));
            Assert.Same(packer, asset.GetPacker());
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));

            var invalid = ScriptableObject.CreateInstance<OtherScriptable>();
            var logs = CaptureLogs(() => asset.SetPacker(invalid));
            Assert.Single(logs);
            Assert.Equal(LogType.Warning, logs[0].type);
            Assert.Equal("Attempted to set invalid scriptableobject as UnityEditor.U2D.ScriptablePacker.", logs[0].msg);
            Assert.Same(packer, asset.GetPacker());
            Assert.Equal(d, EditorUtility.GetDirtyCount(asset));

            Assert.Empty(CaptureLogs(() => asset.SetScriptablePacker(null)));
            Assert.Null(asset.GetPacker());
            Assert.Equal(++d, EditorUtility.GetDirtyCount(asset));

            Object.DestroyImmediate(packer);
            Object.DestroyImmediate(invalid);
        }

        [Fact]
        public void ObsoleteMethodsMatchMeasuredNoOpAndDefaultReturns()
        {
#pragma warning disable CS0618
            var asset = new SpriteAtlasAsset();
            int d = EditorUtility.GetDirtyCount(asset);

            var logs = CaptureLogs(() =>
            {
                asset.SetVariantScale(0.25f);
                asset.SetIncludeInBuild(false);
                asset.SetPackingSettings(new SpriteAtlasPackingSettings { padding = 8, blockOffset = 2, enableRotation = true });
                asset.SetTextureSettings(new SpriteAtlasTextureSettings { anisoLevel = 4, filterMode = FilterMode.Trilinear, sRGB = true });
                asset.SetPlatformSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = 512 });
                asset.SetPlatformSettings(null);
            });
            Assert.Empty(logs);
            Assert.Equal(d, EditorUtility.GetDirtyCount(asset));

            Assert.True(asset.IsIncludeInBuild());

            var packing = asset.GetPackingSettings();
            Assert.Equal(0, packing.blockOffset);
            Assert.Equal(0, packing.padding);
            Assert.False(packing.enableRotation);
            Assert.False(packing.enableTightPacking);
            Assert.False(packing.enableAlphaDilation);

            var texture = asset.GetTextureSettings();
            Assert.Equal(0, texture.maxTextureSize);
            Assert.Equal(0, texture.anisoLevel);
            Assert.Equal(FilterMode.Point, texture.filterMode);
            Assert.False(texture.generateMipMaps);
            Assert.False(texture.readable);
            Assert.False(texture.sRGB);

            var plat = asset.GetPlatformSettings("Android");
            Assert.NotNull(plat);
            Assert.Equal("DefaultTexturePlatform", plat.name);
            Assert.False(plat.overridden);
            Assert.Equal(2048, plat.maxTextureSize);
            Assert.Equal(TextureImporterFormat.Automatic, plat.format);

            var platNull = asset.GetPlatformSettings(null);
            Assert.NotNull(platNull);
            Assert.Equal("DefaultTexturePlatform", platNull.name);
            Assert.NotSame(plat, platNull);
            Assert.Equal(d, EditorUtility.GetDirtyCount(asset));
#pragma warning restore CS0618
        }

        [Fact]
        public void SaveValidatesAssetAndWritesExactYamlAndPreservesUnresolvedRefs()
        {
            var exNull = Assert.Throws<ArgumentNullException>(() => SpriteAtlasAsset.Save(null, "Assets/Never.spriteatlasv2"));
            Assert.Equal("Parameter asset is null", exNull.ParamName);

            var destroyed = new SpriteAtlasAsset();
            Object.DestroyImmediate(destroyed);
            var exDestroyed = Assert.Throws<ArgumentNullException>(() => SpriteAtlasAsset.Save(destroyed, "Assets/Never.spriteatlasv2"));
            Assert.Equal("Parameter asset is null", exDestroyed.ParamName);

            Assert.Null(SpriteAtlasAsset.Load(null));
            Assert.Null(SpriteAtlasAsset.Load(""));

            string tempDir = Path.Combine(Path.GetTempPath(), "unbound-spriteatlasasset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string file = Path.Combine(tempDir, "Empty.spriteatlasv2");
                var asset = new SpriteAtlasAsset { hideFlags = HideFlags.DontSaveInEditor };
                asset.name = "Sample";
                asset.SetIsVariant(true);
                Assert.True(EditorUtility.IsDirty(asset));

                SpriteAtlasAsset.Save(asset, file);
                Assert.False(EditorUtility.IsDirty(asset));
                Assert.True(File.Exists(file));
                Assert.False(File.Exists(file + ".meta"));

                string expected =
                    "%YAML 1.1\n" +
                    "%TAG !u! tag:unity3d.com,2011:\n" +
                    "--- !u!612988286 &1\n" +
                    "SpriteAtlasAsset:\n" +
                    "  m_ObjectHideFlags: 4\n" +
                    "  m_CorrespondingSourceObject: {fileID: 0}\n" +
                    "  m_PrefabInstance: {fileID: 0}\n" +
                    "  m_PrefabAsset: {fileID: 0}\n" +
                    "  m_Name: Sample\n" +
                    "  serializedVersion: 2\n" +
                    "  m_MasterAtlas: {fileID: 0}\n" +
                    "  m_ImporterData:\n" +
                    "    packables: []\n" +
                    "  m_IsVariant: 1\n" +
                    "  m_ScriptablePacker: {fileID: 0}\n";
                Assert.Equal(expected, File.ReadAllText(file));

                // Control: missing parent directory logs an error, clears dirty, does not throw or create the directory.
                string missingDirPath = Path.Combine(tempDir, "NoSuchSub", "Out.spriteatlasv2");
                var failAsset = new SpriteAtlasAsset();
                var errLogs = CaptureLogs(() => SpriteAtlasAsset.Save(failAsset, missingDirPath));
                Assert.Contains(errLogs, l => l.type == LogType.Error && l.msg.Contains(missingDirPath));
                Assert.False(File.Exists(missingDirPath));
                Assert.False(Directory.Exists(Path.Combine(tempDir, "NoSuchSub")));
                Assert.False(EditorUtility.IsDirty(failAsset));

                // Control: null/empty path logs an error, clears dirty, does not throw.
                var emptyPathAsset = new SpriteAtlasAsset();
                var emptyLogs = CaptureLogs(() => SpriteAtlasAsset.Save(emptyPathAsset, ""));
                Assert.Contains(emptyLogs, l => l.type == LogType.Error);
                Assert.False(EditorUtility.IsDirty(emptyPathAsset));

                // Control: ReadSerialized + WriteYaml preserves serialized references even without AssetDatabase.
                var parsed = new SpriteAtlasAsset(fromDeserialization: true);
                var body = TestYaml.DocumentBody(
                    "SpriteAtlasAsset:\n" +
                    "  m_ObjectHideFlags: 0\n" +
                    "  m_Name: Roundtrip\n" +
                    "  serializedVersion: 2\n" +
                    "  m_MasterAtlas: {fileID: 100100200, guid: 9b2c3d4e5f60718293a4b5c6d7e80002, type: 3}\n" +
                    "  m_ImporterData:\n" +
                    "    packables:\n" +
                    "    - {fileID: 2800000, guid: b828055b6a1d50b968e77fc6406e2b5c, type: 3}\n" +
                    "  m_IsVariant: 1\n" +
                    "  m_ScriptablePacker: {fileID: 11400000, guid: 9b2c3d4e5f60718293a4b5c6d7e80008, type: 2}\n");
                parsed.ReadSerialized(body);
                Assert.False(EditorUtility.IsDirty(parsed));
                Assert.Equal(0, EditorUtility.GetDirtyCount(parsed));
                Assert.Equal("Roundtrip", parsed.name);
                Assert.True(parsed.isVariant);

                string roundtripFile = Path.Combine(tempDir, "Roundtrip.spriteatlasv2");
                SpriteAtlasAsset.Save(parsed, roundtripFile);
                string roundtripText = File.ReadAllText(roundtripFile);
                Assert.Contains("m_MasterAtlas: {fileID: 100100200, guid: 9b2c3d4e5f60718293a4b5c6d7e80002, type: 3}\n", roundtripText);
                Assert.Contains("    - {fileID: 2800000, guid: b828055b6a1d50b968e77fc6406e2b5c, type: 3}\n", roundtripText);
                Assert.Contains("m_ScriptablePacker: {fileID: 11400000, guid: 9b2c3d4e5f60718293a4b5c6d7e80008, type: 2}\n", roundtripText);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UnmeasuredReleaseControlPreservesCoreBehavior()
        {
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var asset = new SpriteAtlasAsset();
                Assert.False(asset.isVariant);
                asset.SetIsVariant(true);
                Assert.True(asset.isVariant);
#pragma warning disable CS0618
                Assert.True(asset.IsIncludeInBuild());
#pragma warning restore CS0618
            }
        }
    }
}
