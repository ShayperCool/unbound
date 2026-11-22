using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AssetDatabase.RemoveObjectFromAsset as the Unity 6000.0.71f1 Editor does it
    /// (tests/projects/projectwrites 14.remove), on assets of engine objects here: the object is
    /// out of its asset at once and out of the file with the next save, an object added back is
    /// dirty and no sub-asset until that save, and an asset whose main object went has none until
    /// the save gives it the object left.
    /// </summary>
    [Collection("EditorState")]
    public sealed class RemoveObjectFromAssetTests : IDisposable
    {
        const string Guid = "0b000000000000000000000000000001";
        const string OtherGuid = "0b000000000000000000000000000002";
        const string AssetPath = "Assets/Made.physicMaterial";
        const string Table = """
            [
              {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"0b000000000000000000000000000001","path":"Assets/Made.physicMaterial","kind":"yaml","importer":"NativeFormatImporter","labels":[]},
              {"guid":"0b000000000000000000000000000002","path":"Assets/Loaded.physicMaterial","kind":"yaml","importer":"NativeFormatImporter","labels":[]}
            ]
            """;

        readonly string m_Project, m_PreviousProject;
        readonly List<Object> m_Objects = new List<Object>();
        readonly List<string> m_Logs = new List<string>();
        readonly Application.LogCallback m_OnLog;
        readonly PhysicsMaterial m_Main, m_Part;

        public RemoveObjectFromAssetTests()
        {
            m_Project = Path.Combine(Path.GetTempPath(), "unbound-removeobject-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_Project, "Assets"));
            m_PreviousProject = ProjectInfo.ProjectPath;
            ProjectInfo.ProjectPath = m_Project;
            AssetDatabase.List(Table);
            m_Main = Keep(new PhysicsMaterial { name = "Made", bounciness = 0.25f });
            m_Part = Keep(new PhysicsMaterial { name = "Part", bounciness = 0.5f });
            File.WriteAllText(FilePath, "");
            AssetDatabase.TrackCreated(Guid, AssetPath, m_Main);
            AssetDatabase.AddObjectToAsset(m_Part, m_Main);
            AssetDatabase.SaveCreated();
            m_OnLog = (condition, _, type) => m_Logs.Add(type + ":" + condition);
            Application.logMessageReceived += m_OnLog;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= m_OnLog;
            foreach (var o in m_Objects) if (o) Object.DestroyImmediate(o, true);
            AssetDatabase.SaveCreated();
            AssetDatabaseRuntime.Invalidate(Guid);
            AssetDatabaseRuntime.Invalidate(OtherGuid);
            AssetDatabase.Invalidate();
            ProjectInfo.ProjectPath = m_PreviousProject;
            Directory.Delete(m_Project, true);
        }

        string FilePath => Path.Combine(m_Project, AssetPath);

        T Keep<T>(T o) where T : Object
        {
            m_Objects.Add(o);
            return o;
        }

        string[] Names() => AssetDatabase.LoadAllAssetsAtPath(AssetPath).Select(o => o.name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        int Documents() => File.ReadAllText(FilePath).Split("\n--- ").Length - 1;

        [Fact]
        public void ARemovedObjectIsOfNoAssetAtOnceAndLeavesTheFileWithTheSave()
        {
            Assert.True(AssetDatabase.IsSubAsset(m_Part));
            Assert.Equal(2, Documents());

            AssetDatabase.RemoveObjectFromAsset(m_Part);

            Assert.True(m_Part);
            Assert.Equal(0.5f, m_Part.bounciness);
            Assert.Equal("", AssetDatabase.GetAssetPath(m_Part));
            Assert.False(AssetDatabase.Contains(m_Part));
            Assert.False(AssetDatabase.IsSubAsset(m_Part));
            Assert.False(EditorUtility.IsPersistent(m_Part));
            Assert.Equal(new[] { "Made" }, Names());
            Assert.True(AssetDatabase.IsMainAsset(m_Main));
            Assert.Equal(2, Documents());

            AssetDatabase.SaveCreated();

            Assert.Equal(1, Documents());
            Assert.DoesNotContain("m_Name: Part", File.ReadAllText(FilePath));
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void RemovingTwiceOrAnObjectOfNoAssetDoesNothingAndNullThrows()
        {
            AssetDatabase.RemoveObjectFromAsset(m_Part);
            AssetDatabase.RemoveObjectFromAsset(m_Part);
            var loose = Keep(new PhysicsMaterial { name = "Loose" });
            AssetDatabase.RemoveObjectFromAsset(loose);

            Assert.True(loose);
            Assert.Equal(new[] { "Made" }, Names());
            Assert.Empty(m_Logs);
            Assert.Throws<ArgumentNullException>(() => AssetDatabase.RemoveObjectFromAsset(null));
        }

        [Fact]
        public void AnObjectAddedBackIsDirtyAndNoSubAssetUntilTheSave()
        {
            AssetDatabase.RemoveObjectFromAsset(m_Part);
            AssetDatabase.SaveCreated();
            Assert.False(EditorUtility.IsDirty(m_Part));

            AssetDatabase.AddObjectToAsset(m_Part, m_Main);

            Assert.Equal(AssetPath, AssetDatabase.GetAssetPath(m_Part));
            Assert.True(AssetDatabase.Contains(m_Part));
            Assert.True(EditorUtility.IsDirty(m_Part));
            Assert.False(AssetDatabase.IsSubAsset(m_Part));
            Assert.Equal(new[] { "Made", "Part" }, Names());
            Assert.Equal(1, Documents());

            AssetDatabase.SaveCreated();

            Assert.True(AssetDatabase.IsSubAsset(m_Part));
            Assert.False(EditorUtility.IsDirty(m_Part));
            Assert.Equal(2, Documents());
        }

        [Fact]
        public void AnAddedObjectRemovedBeforeTheSaveNeverReachesTheFile()
        {
            var late = Keep(new PhysicsMaterial { name = "Late" });
            AssetDatabase.AddObjectToAsset(late, m_Main);
            AssetDatabase.RemoveObjectFromAsset(late);
            AssetDatabase.SaveCreated();

            Assert.False(AssetDatabase.Contains(late));
            Assert.False(AssetDatabase.IsSubAsset(late));
            Assert.Equal(2, Documents());
            Assert.DoesNotContain("m_Name: Late", File.ReadAllText(FilePath));
        }

        [Fact]
        public void WithoutItsMainObjectAnAssetHasNoneUntilTheSaveMakesTheObjectLeftTheMainOne()
        {
            AssetDatabase.RemoveObjectFromAsset(m_Main);

            Assert.True(m_Main);
            Assert.Equal("", AssetDatabase.GetAssetPath(m_Main));
            Assert.False(AssetDatabase.IsMainAsset(m_Main));
            Assert.Null(AssetDatabase.LoadMainAssetAtPath(AssetPath));
            Assert.False(AssetDatabase.IsMainAsset(m_Part));
            Assert.True(AssetDatabase.IsSubAsset(m_Part));
            Assert.Equal(new[] { "Part" }, Names());
            Assert.Equal(2, Documents());
            Assert.Empty(m_Logs);

            AssetDatabase.SaveCreated();

            Assert.Same(m_Part, AssetDatabase.LoadMainAssetAtPath(AssetPath));
            Assert.True(AssetDatabase.IsMainAsset(m_Part));
            Assert.False(AssetDatabase.IsSubAsset(m_Part));
            Assert.Equal("Part", m_Part.name);
            Assert.Equal(1, Documents());
            Assert.Contains("m_Name: Part", File.ReadAllText(FilePath));
            Assert.Equal(new[] { "Warning:Main Object Name 'Part' does not match filename 'Made'" }, m_Logs);
        }

        [Fact]
        public void AnAssetLeftWithoutObjectsKeepsItsFile()
        {
            AssetDatabase.RemoveObjectFromAsset(m_Part);
            AssetDatabase.RemoveObjectFromAsset(m_Main);
            AssetDatabase.SaveCreated();

            // Unbound's own diagnostic says so; it is no log of the game's.
            Assert.Equal(2, Documents());
            Assert.Empty(Names());
            Assert.Null(AssetDatabase.LoadMainAssetAtPath(AssetPath));
        }

        [Fact]
        public void AnObjectOfAnAssetNotMadeInThisSessionStaysWithAnError()
        {
            var loaded = Keep(new PhysicsMaterial { name = "Loaded" });
            AssetDatabaseRuntime.Adopt(OtherGuid, "Assets/Loaded.physicMaterial", loaded, 13400000);

            AssetDatabase.RemoveObjectFromAsset(loaded);

            Assert.True(AssetDatabase.Contains(loaded));
            Assert.Contains(m_Logs, log => log.StartsWith("Error:", StringComparison.Ordinal) && log.Contains("Assets/Loaded.physicMaterial"));
        }
    }
}
