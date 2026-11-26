using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// An asset CreateAsset made and changed since is offered to the processors' OnWillSaveAssets by
    /// AssetDatabase.SaveAssets and SaveAssetIfDirty (the Unity 6000.0.71f1 Editor offers it,
    /// tests/projects/vfxwrites material save assets, material save if dirty), and is written only
    /// when they return its path: a path they drop leaves the file as it was and the object dirty
    /// (the hook's contract; unmeasured for such an asset).
    /// </summary>
    [Collection("EditorState")]
    public sealed class CreatedAssetSaveHookTests : IDisposable
    {
        const string Guid = "0c000000000000000000000000000001";
        const string AssetPath = "Assets/Made.physicMaterial";
        const string Table = """
            [
              {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"0c000000000000000000000000000001","path":"Assets/Made.physicMaterial","kind":"yaml","importer":"NativeFormatImporter","labels":[]}
            ]
            """;

        static readonly List<string> s_Calls = new List<string>();
        readonly string m_Project, m_PreviousProject;
        readonly PhysicsMaterial m_Main;

        /// <summary>Returns no path: nothing is to be saved.</summary>
        public sealed class Dropping : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                s_Calls.Add("drop[" + string.Join(",", paths) + "]");
                return Array.Empty<string>();
            }
        }

        /// <summary>Returns the paths it is given.</summary>
        public sealed class Returning : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                s_Calls.Add("return[" + string.Join(",", paths) + "]");
                return paths;
            }
        }

        public CreatedAssetSaveHookTests()
        {
            s_Calls.Clear();
            m_Project = Path.Combine(Path.GetTempPath(), "unbound-createdhook-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_Project, "Assets"));
            m_PreviousProject = ProjectInfo.ProjectPath;
            ProjectInfo.ProjectPath = m_Project;
            AssetDatabase.List(Table);
            m_Main = new PhysicsMaterial { name = "Made", bounciness = 0.25f };
            File.WriteAllText(FilePath, "");
            AssetDatabase.TrackCreated(Guid, AssetPath, m_Main);
            AssetDatabase.SaveCreated();
            m_Main.bounciness = 0.75f;
            EditorUtility.SetDirty(m_Main);
        }

        public void Dispose()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = null;
            s_Calls.Clear();
            if (m_Main) Object.DestroyImmediate(m_Main, true);
            AssetDatabase.SaveCreated();
            AssetDatabaseRuntime.Invalidate(Guid);
            AssetDatabase.Invalidate();
            ProjectInfo.ProjectPath = m_PreviousProject;
            Directory.Delete(m_Project, true);
        }

        string FilePath => Path.Combine(m_Project, AssetPath);

        void Save(bool ifDirty)
        {
            if (ifDirty) AssetDatabase.SaveAssetIfDirty(new GUID(Guid));
            else AssetDatabase.SaveAssets();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ADroppedPathLeavesTheFileAndTheObjectDirty(bool ifDirty)
        {
            var before = File.ReadAllText(FilePath);
            Assert.DoesNotContain("0.75", before);
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(Dropping) };
            Save(ifDirty);
            Assert.Equal(new[] { "drop[" + AssetPath + "]" }, s_Calls);
            Assert.Equal(before, File.ReadAllText(FilePath));
            Assert.True(EditorUtility.IsDirty(m_Main));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AReturnedPathIsWritten(bool ifDirty)
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(Returning) };
            Save(ifDirty);
            Assert.Equal(new[] { "return[" + AssetPath + "]" }, s_Calls);
            Assert.Contains("0.75", File.ReadAllText(FilePath));
            Assert.False(EditorUtility.IsDirty(m_Main));
        }
    }
}
