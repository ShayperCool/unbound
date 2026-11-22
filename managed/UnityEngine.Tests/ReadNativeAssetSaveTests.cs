using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;
using Xunit.Abstractions;

namespace UnityEngine.Tests
{
    /// <summary>Native assets read from their files and saved: a document written again unchanged
    /// is the file byte for byte (every Unity-written mesh, texture and physics material document
    /// of the probe projects and their references), and an edit writes only the keys it changes
    /// (tests/projects/scenesave native-saves, Unity 6000.0.71f1).</summary>
    public class ReadNativeAssetSaveTests
    {
        readonly ITestOutputHelper m_Output;

        public ReadNativeAssetSaveTests(ITestOutputHelper output) => m_Output = output;

        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", ".."));

        /// <summary>The probe projects' files, without probe runs' copies and caches (Library,
        /// Temp: they can hold links back into the project).</summary>
        static IEnumerable<string> ProjectFiles(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
            foreach (var sub in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(sub);
                if (name is "Library" or "Temp" or "Logs" or "obj" || new DirectoryInfo(sub).LinkTarget != null) continue;
                foreach (var file in ProjectFiles(sub)) yield return file;
            }
        }

        static Object Make(int classId) => classId switch
        {
            43 => new Mesh(),
            28 => new Texture2D(1, 1),
            134 => new PhysicsMaterial(),
            _ => null,
        };

        static void Read(Object o, string documentText)
        {
            var body = TestYaml.DocumentBody(documentText);
            switch (o)
            {
                case Mesh m: m.ReadSerialized(body, new NullResolver()); break;
                case Texture2D t: t.ReadSerialized(body, new NullResolver()); break;
                case PhysicsMaterial p: p.ReadSerialized(body, new NullResolver()); break;
            }
        }

        /// <summary>The file written again from the objects read from it, the documents of the
        /// given classes overlaid; null when a document is kept as read.</summary>
        internal static string Rewrite(string text, Func<Object, Object> edit = null)
        {
            var docs = UnityYamlText.Parse(text);
            var objects = new List<Object>();
            try
            {
                foreach (var doc in docs)
                {
                    var o = Make(doc.ClassId);
                    if (o == null) continue;
                    objects.Add(o);
                    var single = UnityYamlText.Write(new List<YTextDocument> { doc });
                    Read(o, single);
                    edit?.Invoke(o);
                    // Saved as an edited asset is.
                    EditorUtility.SetDirty(o);
                    int unlocated = 0;
                    var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [o] = doc.FileId };
                    if (!NativeDocuments.Write(doc, o, ids, new AssetDatabaseRuntime.Locations(), ref unlocated)) return null;
                }
                return UnityYamlText.Write(docs);
            }
            finally
            {
                foreach (var o in objects) Object.DestroyImmediate(o);
            }
        }

        /// <summary>The one residual: a mesh read from its file holds its vertices as floats, so an
        /// SNorm16 component stored as -32768 is written as -32767 (both are -1).</summary>
        const string SNormMinimumMesh = "vfx/Authored/unity-6000.0.71f1/default/Assets/Objects/MeshMixed.asset";
        const string SNormMinimum = "000080bf0000003f0000003f0000003f000080bf00806400c8000180";
        const string SNormMinimumWritten = "000080bf0000003f0000003f0000003f000080bf01806400c8000180";

        static string Reference(string path) => File.ReadAllText(Path.Combine(Repo(), "tests", "projects", "scenesave", path));

        /// <summary>The probe's edits of a mesh, a texture and a physics material read from their
        /// files give the files the Editor saved (tests/projects/scenesave native-saves).</summary>
        [Fact]
        public void EditsOfReadAssetsAreWrittenAsTheEditorWroteThem()
        {
            const string saved = "Reference/unity-6000.0.71f1/native-saves/snapshots/";
            Assert.Equal(Reference(saved + "mesh.file"), Rewrite(Reference("Assets/Native/Tri.asset"), o =>
            {
                var mesh = (Mesh)o;
                mesh.vertices = new[] { Vector3.zero, Vector3.right * 2f, Vector3.up * 2f };
                mesh.RecalculateBounds();
                return o;
            }));
            Assert.Equal(Reference(saved + "texture.file"), Rewrite(Reference("Assets/Native/Alpha.asset"), o =>
            {
                var texture = (Texture2D)o;
                texture.SetPixel(1, 1, new Color(0f, 0f, 0f, 0.5f));
                texture.Apply();
                return o;
            }));
            Assert.Equal(Reference(saved + "physic.file"), Rewrite(Reference("Assets/Native/Bouncy.physicMaterial"), o =>
            {
                ((PhysicsMaterial)o).bounciness = 0.9f;
                return o;
            }));
        }

        /// <summary>A document in an older serialization (its keys unprefixed, another version) is
        /// kept as read when its object is edited: nothing is mixed into it.</summary>
        [Fact]
        public void AnOlderSerializationIsKeptAsRead()
        {
            const string older = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!134 &13400000\nPhysicMaterial:\n"
                + "  m_ObjectHideFlags: 0\n  m_Name: Slippy\n  dynamicFriction: 0.1\n  staticFriction: 0.1\n  bounciness: 0\n"
                + "  frictionCombine: 0\n  bounceCombine: 0\n";
            Assert.Null(Rewrite(older, o => { ((PhysicsMaterial)o).bounciness = 0.9f; return o; }));
            var mesh = Reference("Assets/Native/Tri.asset");
            Assert.Null(Rewrite(mesh.Replace("  serializedVersion: 11\n", "  serializedVersion: 10\n"), o => o));
            Assert.Null(Rewrite(mesh.Replace("    serializedVersion: 3\n    m_VertexCount", "    serializedVersion: 2\n    m_VertexCount"), o => o));
        }

        /// <summary>A mesh read from its file holds its file's layout; bone weights set later add the
        /// skin channels to it, so they are listed and written.</summary>
        [Fact]
        public void BoneWeightsSetOnAReadMeshAreInItsLayout()
        {
            var mesh = new Mesh();
            try
            {
                mesh.ReadSerialized(TestYaml.DocumentBody(Reference("Assets/Native/Tri.asset")), new NullResolver());
                Assert.False(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendWeight));
                mesh.boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } };
                Assert.True(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendWeight));
                Assert.True(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendIndices));
                Assert.Equal(3, mesh.boneWeights.Length);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        /// <summary>The engine's own applies (a font atlas, a terrain's alphamaps, an import)
        /// do not dirty an asset texture; a script's Apply does. Under a release family without the
        /// measurement, edits of an asset this session did not make dirty nothing and its document
        /// is kept as read.</summary>
        [Fact]
        public void EngineAppliesAndUnmeasuredFamiliesDirtyNothing()
        {
            var texture = new Texture2D(2, 2);
            var physics = new PhysicsMaterial();
            try
            {
                texture.IsPersistentAsset = true;
                texture.ApplyQuietly();
                Assert.False(EditorUtility.IsDirty(texture));
                texture.Apply();
                Assert.True(EditorUtility.IsDirty(texture));
                EditorUtility.ClearDirty(texture);
                using (TestRelease.Pin(TestRelease.Unmeasured))
                {
                    texture.Apply();
                    Assert.False(EditorUtility.IsDirty(texture));
                    physics.IsPersistentAsset = true;
                    physics.bounciness = 0.9f;
                    Assert.False(EditorUtility.IsDirty(physics));
                    Assert.Null(Rewrite(Reference("Assets/Native/Bouncy.physicMaterial"), o => { ((PhysicsMaterial)o).bounciness = 0.9f; return o; }));
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(physics);
            }
        }

        [Fact]
        public void EveryUnityWrittenNativeDocumentIsWrittenAgainUnchanged()
        {
            var root = Path.Combine(Repo(), "tests", "projects");
            var checkedFiles = 0;
            var failures = new List<string>();
            foreach (var file in ProjectFiles(root)
                         .Where(f => f.EndsWith(".asset", StringComparison.Ordinal) || f.EndsWith(".physicMaterial", StringComparison.Ordinal)
                             || f.EndsWith(".file", StringComparison.Ordinal)))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var text = File.ReadAllText(file);
                if (!text.StartsWith(UnityYamlText.Preamble, StringComparison.Ordinal)) continue;
                if (!text.Contains("--- !u!43 &") && !text.Contains("--- !u!28 &") && !text.Contains("--- !u!134 &")) continue;
                string written;
                try { written = Rewrite(text); }
                catch (Exception e) { failures.Add(file + ": " + e.GetType().Name + " " + e.Message); continue; }
                checkedFiles++;
                if (written == null) { m_Output.WriteLine("kept as read: " + file); continue; }
                if (written != text.Replace("\r\n", "\n") && !(relative == SNormMinimumMesh && written == text.Replace(SNormMinimum, SNormMinimumWritten)))
                {
                    var a = text.Replace("\r\n", "\n").Split('\n');
                    var b = written.Split('\n');
                    int i = 0;
                    while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
                    failures.Add(file + ": line " + (i + 1) + ": " + (i < a.Length ? a[i] : "<end>") + " => " + (i < b.Length ? b[i] : "<end>"));
                }
            }
            foreach (var f in failures) m_Output.WriteLine(f);
            m_Output.WriteLine("checked " + checkedFiles);
            Assert.True(checkedFiles > 0);
            Assert.Empty(failures);
        }
    }
}
