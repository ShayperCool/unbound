using System;
using Unbound.Runtime;
using UnityEditor;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary><c>AssetImporter.GetExternalObjectMap</c> answers a .meta's <c>externalObjects</c>
    /// for every importer, not only scripted ones; the remapped assets load when the map is asked
    /// for.</summary>
    public class ExternalObjectMapTests
    {
        const string MaterialGuid = "5a1e0f3c9b8d4e6fa1b2c3d4e5f60718";
        const long MaterialId = 2100000;

        static AssetImporter Model(string externalObjects) =>
            AssetImporter.FromMeta("Assets/Model.fbx", "8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e", "ModelImporter",
                TestYaml.DocumentBody("ModelImporter:\n  serializedVersion: 22200\n" + externalObjects + "  userData: \n"));

        [Fact]
        public void AModelImportersRemapsAreItsExternalObjectMap()
        {
            using var engine = MainThreadCheck.Engine();
            var material = new Material(Shader.Create("Remap/Target", 2000)) { name = "Remap", IsPersistentAsset = true };
            var file = new LoadedFile { Guid = MaterialGuid, Path = "Assets/Remap.mat", MainObject = material };
            file.Objects[MaterialId] = material;
            AssetDatabaseRuntime.AddLoaded(file);
            try
            {
                var importer = Model(
                    "  externalObjects:\n" +
                    "  - first:\n" +
                    "      type: UnityEngine:Material\n" +
                    "      assembly: UnityEngine.CoreModule\n" +
                    "      name: Bark\n" +
                    $"    second: {{fileID: {MaterialId}, guid: {MaterialGuid}, type: 2}}\n" +
                    "  - first:\n" +
                    "      type: UnityEngine:Material\n" +
                    "      assembly: UnityEngine.CoreModule\n" +
                    "      name: Cleared\n" +
                    "    second: {fileID: 0}\n" +
                    "  - first:\n" +
                    "      type: Missing.Namespace:NoSuchClass\n" +
                    "      assembly: Missing\n" +
                    "      name: Skipped\n" +
                    $"    second: {{fileID: {MaterialId}, guid: {MaterialGuid}, type: 2}}\n");
                Assert.IsType<ModelImporter>(importer);
                var map = importer.GetExternalObjectMap();
                Assert.Equal(2, map.Count);
                Assert.Same(material, map[new AssetImporter.SourceAssetIdentifier(typeof(Material), "Bark")]);
                Assert.Null(map[new AssetImporter.SourceAssetIdentifier(typeof(Material), "Cleared")]);
                // The answer is a copy, and AddRemap / RemoveRemap change the importer's map.
                map.Clear();
                Assert.Equal(2, importer.GetExternalObjectMap().Count);
                Assert.True(importer.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Bark")));
                Assert.False(importer.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Bark")));
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Leaf"), material);
                map = importer.GetExternalObjectMap();
                Assert.Equal(2, map.Count);
                Assert.Same(material, map[new AssetImporter.SourceAssetIdentifier(typeof(Material), "Leaf")]);
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(MaterialGuid);
                Object.DestroyImmediate(material, true);
            }
        }

        [Fact]
        public void AModelImporterWithoutRemapsHasAnEmptyMap()
        {
            using var engine = MainThreadCheck.Engine();
            Assert.Empty(Model("  externalObjects: {}\n").GetExternalObjectMap());
            Assert.Empty(Model("").GetExternalObjectMap());
        }

        /// <summary>A remap read from the .meta is loaded when the map is asked for, not when the
        /// importer is made.</summary>
        [Fact]
        public void RemapsLoadWhenTheMapIsAskedFor()
        {
            using var engine = MainThreadCheck.Engine();
            var importer = Model(
                "  externalObjects:\n" +
                "  - first:\n" +
                "      type: UnityEngine:Material\n" +
                "      assembly: UnityEngine.CoreModule\n" +
                "      name: Bark\n" +
                $"    second: {{fileID: {MaterialId}, guid: {MaterialGuid}, type: 2}}\n");
            // Registered after the importer read its .meta: the map still finds it.
            var material = new Material(Shader.Create("Remap/Target", 2000)) { name = "Remap", IsPersistentAsset = true };
            var file = new LoadedFile { Guid = MaterialGuid, Path = "Assets/Remap.mat", MainObject = material };
            file.Objects[MaterialId] = material;
            AssetDatabaseRuntime.AddLoaded(file);
            try
            {
                Assert.Same(material, importer.GetExternalObjectMap()[new AssetImporter.SourceAssetIdentifier(typeof(Material), "Bark")]);
                // AddRemap and RemoveRemap before the first map query replace the .meta's entry.
                var bark = new AssetImporter.SourceAssetIdentifier(typeof(Material), "Bark");
                var added = Model(
                    "  externalObjects:\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: Bark\n" +
                    $"    second: {{fileID: {MaterialId}, guid: {MaterialGuid}, type: 2}}\n");
                added.AddRemap(bark, null);
                Assert.Null(added.GetExternalObjectMap()[bark]);
                var removed = Model(
                    "  externalObjects:\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: Bark\n" +
                    $"    second: {{fileID: {MaterialId}, guid: {MaterialGuid}, type: 2}}\n");
                Assert.True(removed.RemoveRemap(bark));
                Assert.Empty(removed.GetExternalObjectMap());
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(MaterialGuid);
                Object.DestroyImmediate(material, true);
            }
        }
    }
}
