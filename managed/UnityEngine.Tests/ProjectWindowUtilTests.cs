using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ProjectWindowUtil without a Project window, as the Unity 6000.0.71f1 Editor runs it
    /// in Play mode (tests/projects/projectwindowutil).</summary>
    public sealed class ProjectWindowUtilTests
    {
        sealed class Recording : EndNameEditAction
        {
            internal readonly List<string> Calls = new List<string>();
            public override void Action(int instanceId, string pathName, string resourceFile) => Calls.Add(instanceId + ":" + pathName + ":" + resourceFile);
        }

        [Fact]
        public void AnEndActionIsHiddenAndItsCleanUpDestroysIt()
        {
            // The path a name edit ends on comes from the project's asset database (the probe
            // tests/projects/projectwindowutil checks it); here the action's own part.
            var action = ScriptableObject.CreateInstance<Recording>();
            Assert.Equal(HideFlags.HideAndDontSave, action.hideFlags);
            action.Cancelled(1, "Assets/X.asset", null);
            Assert.Empty(action.Calls);
            action.CleanUp();
            Assert.True(action == null);
        }

        [Fact]
        public void WithoutAProjectWindowTheActiveFolderIsAssets()
        {
            Assert.False(ProjectWindowUtil.TryGetActiveFolderPath(out var path));
            Assert.Equal("", path);
            Assert.Equal("Assets", ProjectWindowUtil.GetActiveFolderPath());
        }

        // The probe's template (tests/projects/projectwindowutil Assets/Data/ScriptTemplate.txt).
        const string Template = "#NOTRIM#// #NAME# (#SCRIPTNAME#, #SCRIPTNAME_LOWER#)\n#ROOTNAMESPACEBEGIN#\nclass #SCRIPTNAME#\n{\n    int value;\n}\n#ROOTNAMESPACEEND#\nend\n";

        [Fact]
        public void ATemplateTakesTheFileNameAndLosesItsNamespaceMarkersWithoutANamespace()
        {
            // create.template, create.templateLower: a text file has no root namespace.
            Assert.Equal("// My Note (MyNote, myNote)\nclass MyNote\n{\n    int value;\n}\nend\n",
                ProjectWindowUtil.PreprocessScriptAssetTemplate("Assets/Data/My Note.txt", Template));
            Assert.Equal("// note two (notetwo, myNotetwo)\nclass notetwo\n{\n    int value;\n}\nend\n",
                ProjectWindowUtil.PreprocessScriptAssetTemplate("Assets/Data/note two.txt", Template));
            // Without both markers the text stays; with Windows line breaks the break before each
            // marker goes too.
            Assert.Equal("a\n#ROOTNAMESPACEBEGIN#\nb", ProjectWindowUtil.RemoveOrInsertNamespace("a\n#ROOTNAMESPACEBEGIN#\nb", null));
            Assert.Equal("a\r\nb\r\nc", ProjectWindowUtil.RemoveOrInsertNamespace("a\r\n  #ROOTNAMESPACEBEGIN#\r\nb\r\n#ROOTNAMESPACEEND#  \r\nc", ""));
        }

        [Fact]
        public void NewTextTakesTheProjectsLineEndings()
        {
            // create.withContent: Unix turns \r\n and \r into \n.
            Assert.Equal("one\ntwo\nthree\nfour", ProjectWindowUtil.SetLineEndings("one\r\ntwo\rthree\nfour", LineEndingsMode.Unix));
            Assert.Equal("a\r\nb\r\nc", ProjectWindowUtil.SetLineEndings("a\nb\rc", LineEndingsMode.Windows));
            string native = Application.platform == RuntimePlatform.WindowsEditor ? "\r\n" : "\n";
            Assert.Equal("a" + native + "b", ProjectWindowUtil.SetLineEndings("a\r\nb", LineEndingsMode.OSNative));
        }

        [Fact]
        public void CreatingFromATemplateOrANullAssetThrowsAsTheEditor()
        {
            // create.templateNull, create.templateMissing, create.assetNull.
            var nullTemplate = Assert.Throws<ArgumentNullException>(() => ProjectWindowUtil.CreateScriptAssetFromTemplateFile(null, "X.txt"));
            Assert.Equal("templatePath", nullTemplate.ParamName);
            var missing = Assert.Throws<FileNotFoundException>(() => ProjectWindowUtil.CreateScriptAssetFromTemplateFile("Assets/Data/Missing.txt", "Y.txt"));
            Assert.Equal("The template file \"Assets/Data/Missing.txt\" could not be found.", missing.Message);
            var nullAsset = Assert.Throws<NullReferenceException>(() => ProjectWindowUtil.CreateAsset(null, "Null.anim"));
            Assert.Equal("Object reference not set to an instance of an object", nullAsset.Message);
        }

        /// <summary>Hears OnWillCreateAsset while a test listens (a private static method, as
        /// scripts declare it).</summary>
        public sealed class CreationListener : AssetModificationProcessor
        {
            internal static List<string> Heard;

            static void OnWillCreateAsset(string path) => Heard?.Add(path);
        }

        [Fact]
        public void AProcessorsOnWillCreateAssetHearsThePath()
        {
            CreationListener.Heard = new List<string>();
            try
            {
                // The test assembly is not a game assembly, so its class is passed as TypeCache
                // would find it in a project.
                AssetModificationProcessorInternal.OnWillCreateAsset("Assets/Data/Clip.anim", new[] { typeof(CreationListener) });
                AssetModificationProcessorInternal.OnWillCreateAsset("Assets/Data/Clip.anim.meta", new[] { typeof(CreationListener) });
                Assert.Equal(new[] { "Assets/Data/Clip.anim", "Assets/Data/Clip.anim.meta" }, CreationListener.Heard);
            }
            finally
            {
                CreationListener.Heard = null;
            }
        }

        [Fact]
        public void ThePackagesFolderIsTheLastAncestorOfAPackageAsset()
        {
            // As the Project window answers it: Packages has no folder asset.
            Assert.Equal(int.MaxValue, ProjectWindowUtil.FolderInstanceID("Packages"));
        }

        [Fact]
        public void PathHelpersTrimAndKeepTheOutermostFolders()
        {
            Assert.Equal("Assets/A", ProjectWindowUtil.GetContainingFolder("/Assets/A/b/"));
            Assert.Null(ProjectWindowUtil.GetContainingFolder("Assets"));
            Assert.Null(ProjectWindowUtil.GetContainingFolder(null));
            Assert.Equal("a/", ProjectWindowUtil.GetContainingFolder("a//b"));
            Assert.Equal(new[] { "Assets/A", "Assets/AB", "Assets/B" }, ProjectWindowUtil.GetBaseFolders(new[] { "Assets/B", "Assets/A", "Assets/A/Sub", "Assets/AB" }));
            Assert.Equal(new[] { "Only/" }, ProjectWindowUtil.GetBaseFolders(new[] { "Only/" }));
            // Sorted before the '/' is added, "Assets/A" comes first; a folder is checked against
            // the last base folder only, so "Assets/A/Sub" after "Assets/A 1" stays.
            Assert.Equal(new[] { "Assets/A", "Assets/A 1", "Assets/A/Sub" }, ProjectWindowUtil.GetBaseFolders(new[] { "Assets/A 1", "Assets/A", "Assets/A/Sub" }));
        }
    }
}
