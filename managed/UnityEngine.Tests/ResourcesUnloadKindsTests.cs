using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public sealed class UnloadKindsHolder : MonoBehaviour
    {
        public TextAsset text;
    }

    /// <summary>
    /// Resources.UnloadAsset of textures, meshes, audio clips and text assets, and
    /// Resources.UnloadUnusedAssets. Scripting API 6000.0, Resources.UnloadUnusedAssets: "An asset is
    /// deemed to be unused if it isn't reached after walking the whole game object hierarchy,
    /// including script components. Static variables are also examined. The script execution
    /// stack, however, is not examined [...] All assets other than ScriptableObjects are loaded back
    /// in the next time one of its properties or methods is used"; HideFlags.DontUnloadUnusedAsset:
    /// "The object will not be unloaded by Resources.UnloadUnusedAssets." The Editor's reload of a
    /// material in place is measured (tests/projects/droppedparams unload.material); the other
    /// kinds follow it unmeasured. These tests check that what scripts changed is gone and the
    /// saved data is back; they assert no wrapper identity beyond the object the loader keeps.
    /// </summary>
    public class ResourcesUnloadKindsTests
    {
        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        static List<string> Errors(System.Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error) errors.Add(message); };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return errors;
        }

        static LoadedFile Loaded(string guid, string path, long fileId, Object asset)
        {
            var file = new LoadedFile { Guid = guid, Path = path, MainObject = asset };
            file.Objects[fileId] = asset;
            AssetDatabaseRuntime.AddLoaded(file);
            return file;
        }

        static string TempFile(string contents)
        {
            var path = Path.Combine(Path.GetTempPath(), $"unbound-unload-{System.Guid.NewGuid():N}.txt");
            File.WriteAllText(path, contents);
            return path;
        }

        static TextAsset ImportedText(string path) =>
            new TextAsset(path, Path.GetFileNameWithoutExtension(path), new FileInfo(path).Length) { IsPersistentAsset = true };

        [Fact]
        public void AnImportedTextureIsImportedAgainIntoItsHandle()
        {
            const string guid = "c0de0000000000000000000000000001";
            var info = new ImportedTextureInfo
            {
                Width = 4, Height = 2, MipCount = 1, Srgb = 1, FilterMode = (int)FilterMode.Bilinear,
                AnisoLevel = 1, Readable = 1, Format = 0, Dimension = 2,
            };
            var calls = new List<(ulong, string)>();
            var previous = Texture2D.Reimport;
            Texture2D.Reimport = (handle, g) => { calls.Add((handle, g)); return (true, info); };
            var texture = Texture2D.FromImport(9001, guid, "Imported", info);
            Loaded(guid, "Assets/Resources/Imported.png", AssetImporters.Texture2DFileId, texture);
            const string member = "Resources.UnloadAsset(Texture2D)";
            try
            {
                texture.filterMode = FilterMode.Point;
                texture.Reinitialize(8, 8);
                texture.SetPixel(0, 0, Color.red);
                texture.Apply(false, true);
                Assert.False(texture.isReadable);
                int before = Hits(member);

                Assert.Empty(Errors(() => Resources.UnloadAsset(texture)));

                Assert.Equal(before, Hits(member));
                Assert.Equal(new[] { (9001UL, guid) }, calls);
                Assert.True(texture != null);
                Assert.Equal(4, texture.width);
                Assert.Equal(2, texture.height);
                Assert.True(texture.isReadable);
                Assert.Equal(FilterMode.Bilinear, texture.filterMode);
                Assert.Equal(9001UL, texture.m_NativeHandle);
                Assert.Null(texture.m_Pixels);
                Assert.Same(texture, AssetDatabaseRuntime.Load(guid, AssetImporters.Texture2DFileId, typeof(Texture2D)));
            }
            finally
            {
                Texture2D.Reimport = previous;
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(texture, true);
            }
        }

        // Three positions (0,0,0) (1,0,0) (0,1,0), one triangle of UInt16 indices.
        const string TriangleMesh = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!43 &4300000
Mesh:
  m_ObjectHideFlags: 0
  m_Name: Triangle
  serializedVersion: 11
  m_SubMeshes:
  - serializedVersion: 2
    firstByte: 0
    indexCount: 3
    topology: 0
    baseVertex: 0
    firstVertex: 0
    vertexCount: 3
  m_MeshCompression: 0
  m_IsReadable: 1
  m_IndexFormat: 0
  m_IndexBuffer: 000001000200
  m_VertexData:
    serializedVersion: 3
    m_VertexCount: 3
    m_Channels:
    - stream: 0
      offset: 0
      format: 0
      dimension: 3
    m_DataSize: 36
    _typelessdata: 0000000000000000000000000000803f0000000000000000000000000000803f00000000
  m_LocalAABB:
    m_Center: {x: 0.5, y: 0.5, z: 0}
    m_Extent: {x: 0.5, y: 0.5, z: 0}
";

        [Fact]
        public void AMeshReadsItsDocumentAgain()
        {
            const string guid = "c0de0000000000000000000000000002";
            var mesh = new Mesh { name = "Triangle", IsPersistentAsset = true };
            var file = Loaded(guid, "Assets/Resources/Triangle.asset", 4300000, mesh);
            ((ISerializedAsset)mesh).ReadSerialized(UnityYaml.Parse(TriangleMesh)[0].Body, file);
            var reads = new List<string>();
            var previous = AssetDatabaseRuntime.ReloadDocuments;
            AssetDatabaseRuntime.ReloadDocuments = g => { reads.Add(g); return UnityYaml.Parse(TriangleMesh); };
            const string member = "Resources.UnloadAsset(Mesh)";
            try
            {
                mesh.vertices = new[] { new Vector3(5, 5, 5), new Vector3(6, 5, 5), new Vector3(5, 6, 5) };
                mesh.UploadMeshData(true);
                Assert.False(mesh.isReadable);
                int before = Hits(member);

                Assert.Empty(Errors(() => Resources.UnloadAsset(mesh)));

                Assert.Equal(before, Hits(member));
                Assert.Equal(new[] { guid }, reads);
                Assert.True(mesh != null);
                Assert.True(mesh.isReadable);
                Assert.Equal(new[] { Vector3.zero, new Vector3(1, 0, 0), new Vector3(0, 1, 0) }, mesh.vertices);
                Assert.Equal(new[] { 0, 1, 2 }, mesh.triangles);
                Assert.Same(mesh, AssetDatabaseRuntime.Load(guid, 4300000, typeof(Mesh)));
            }
            finally
            {
                AssetDatabaseRuntime.ReloadDocuments = previous;
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(mesh, true);
            }
        }

        [Fact]
        public void AnImportedAudioClipIsImportedAgainIntoItsHandle()
        {
            const string guid = "c0de0000000000000000000000000003";
            var previous = AudioBackend.Current;
            var backend = new VirtualAudioBackend();
            AudioBackend.Use(backend);
            var saved = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
            backend.Assets[guid] = (new AudioClipInfo { Frames = 4, Channels = 1, Frequency = 4, Preload = 1 }, saved);
            AudioClip clip = null;
            const string member = "Resources.UnloadAsset(AudioClip)";
            try
            {
                clip = AudioClip.FromImport(guid, "Clip");
                Loaded(guid, "Assets/Resources/Clip.wav", AudioClipImporter.AudioClipFileId, clip);
                ulong handle = clip.m_Handle;
                Assert.True(clip.SetData(new[] { 0.9f, 0.9f, 0.9f, 0.9f }, 0));
                int before = Hits(member);

                Assert.Empty(Errors(() => Resources.UnloadAsset(clip)));

                Assert.Equal(before, Hits(member));
                Assert.True(clip != null);
                Assert.Equal(handle, clip.m_Handle);
                var data = new float[4];
                Assert.True(clip.GetData(data, 0));
                Assert.Equal(saved, data);
                Assert.Same(clip, AssetDatabaseRuntime.Load(guid, AudioClipImporter.AudioClipFileId, typeof(AudioClip)));
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(guid);
                if (clip != null) Object.DestroyImmediate(clip, true);
                AudioBackend.Use(previous);
            }
        }

        [Fact]
        public void AnImportedTextAssetReadsItsFileAgain()
        {
            const string guid = "c0de0000000000000000000000000004";
            var path = TempFile("saved");
            var text = ImportedText(path);
            Loaded(guid, "Assets/Resources/Note.txt", AssetImporters.TextAssetFileId, text);
            const string member = "Resources.UnloadAsset(TextAsset)";
            try
            {
                Assert.Equal("saved", text.text);
                File.WriteAllText(path, "changed on disk");
                Assert.Equal("saved", text.text);
                int before = Hits(member);

                Assert.Empty(Errors(() => Resources.UnloadAsset(text)));

                Assert.Equal(before, Hits(member));
                Assert.True(text != null);
                Assert.Equal("changed on disk", text.text);
                Assert.Same(text, AssetDatabaseRuntime.Load(guid, AssetImporters.TextAssetFileId, typeof(TextAsset)));
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(text, true);
                File.Delete(path);
            }
        }

        [Fact]
        public void ATextureNoFileImportedStillWarns()
        {
            // A texture asset Unbound did not import (serialized in an asset file, made here by
            // code): there is no reload path, so it is refused loudly and left as it is.
            const string guid = "c0de0000000000000000000000000005";
            var texture = new Texture2D(2, 2) { name = "serialized", IsPersistentAsset = true };
            texture.SetPixel(0, 0, Color.red);
            Loaded(guid, "Assets/Resources/Atlas.asset", 2800000, texture);
            const string member = "Resources.UnloadAsset(Texture2D)";
            try
            {
                int before = Hits(member);
                Assert.Empty(Errors(() => Resources.UnloadAsset(texture)));
                Assert.Equal(before + 1, Hits(member));
                Assert.Equal(Color.red, texture.GetPixel(0, 0));
                Assert.Equal(2, texture.width);
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(texture, true);
            }
        }

        [Fact]
        public void UnloadUnusedAssetsUnloadsWhatNothingReferencesAndKeepsTheRest()
        {
            const string heldGuid = "c0de0000000000000000000000000006";
            const string unusedGuid = "c0de0000000000000000000000000007";
            const string flaggedGuid = "c0de0000000000000000000000000008";
            var heldPath = TempFile("held 0");
            var unusedPath = TempFile("unused 0");
            var flaggedPath = TempFile("flagged 0");
            var held = ImportedText(heldPath);
            var unused = ImportedText(unusedPath);
            var flagged = ImportedText(flaggedPath);
            flagged.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Loaded(heldGuid, "Assets/Resources/Held.txt", AssetImporters.TextAssetFileId, held);
            Loaded(unusedGuid, "Assets/Resources/Unused.txt", AssetImporters.TextAssetFileId, unused);
            Loaded(flaggedGuid, "Assets/Resources/Flagged.txt", AssetImporters.TextAssetFileId, flagged);
            var go = new GameObject("holder");
            go.AddComponent<UnloadKindsHolder>().text = held;
            try
            {
                Assert.Equal("held 0", held.text);
                Assert.Equal("unused 0", unused.text);
                Assert.Equal("flagged 0", flagged.text);
                File.WriteAllText(heldPath, "held 1");
                File.WriteAllText(unusedPath, "unused 1");
                File.WriteAllText(flaggedPath, "flagged 1");

                AsyncOperation operation = null;
                Assert.Empty(Errors(() => operation = Resources.UnloadUnusedAssets()));

                // Completion as before: done when the call returns.
                Assert.True(operation.isDone);
                // Referenced from a scene object's component: still loaded.
                Assert.Equal("held 0", held.text);
                // Referenced only from this method's stack: unloaded, read again at its next use.
                Assert.Equal("unused 1", unused.text);
                // DontUnloadUnusedAsset: kept.
                Assert.Equal("flagged 0", flagged.text);
                Assert.True(held != null && unused != null && flagged != null);
            }
            finally
            {
                Object.DestroyImmediate(go);
                foreach (var g in new[] { heldGuid, unusedGuid, flaggedGuid }) AssetDatabaseRuntime.Invalidate(g);
                foreach (var t in new[] { held, unused, flagged }) Object.DestroyImmediate(t, true);
                foreach (var p in new[] { heldPath, unusedPath, flaggedPath }) File.Delete(p);
            }
        }

        [Fact]
        public void UnloadUnusedAssetsKeepsScriptableObjects()
        {
            // Unbound's choice, not a Unity guarantee: Unity unloads unused ScriptableObjects for
            // good, but the engine cannot see every reference (the stack, statics of classes with a
            // class constructor), so it keeps them rather than destroy one still in use.
            const string guid = "c0de0000000000000000000000000009";
            var data = ScriptableObject.CreateInstance<ScriptableObject>();
            data.IsPersistentAsset = true;
            data.name = "Data";
            Loaded(guid, "Assets/Resources/Data.asset", 11400000, data);
            try
            {
                Resources.UnloadUnusedAssets();
                Assert.True(data != null);
                Assert.Same(data, AssetDatabaseRuntime.Load(guid, 11400000, typeof(ScriptableObject)));
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(data, true);
            }
        }
    }
}
