using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Editor APIs around script compilation and the Scene view's gizmo table: a response file read
    /// by CompilationPipeline.ParseResponseFile, the defines BuildDefines delegates add, and the
    /// AnnotationUtility table (Unity 6000.0.71f1, tests/projects/gameinterop rsp.parse.*,
    /// builddefines.*, annotation.*).
    /// </summary>
    public class EditorCompilationApiTests
    {
        [Fact]
        public void ParseResponseFileReadsTheEditorsOptions()
        {
            var project = Path.Combine(Path.GetTempPath(), "unbound-rsp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(project, "Assets/Libs"));
            try
            {
                File.WriteAllBytes(Path.Combine(project, "Assets/Libs/Ref.dll"), Array.Empty<byte>());
                File.WriteAllText(Path.Combine(project, "Assets/csc.rsp"),
                    "-define:A;B\n-d:C,D # E is a comment\n-D:F\n-unsafe\n-r:Assets/Libs/Ref.dll\n-r:Missing.dll\n-nowarn:CS0219\n'-langversion:10'\n-warnaserror+:\"CS 1\"\n");
                var data = CompilationPipeline.ParseResponseFile("Assets/csc.rsp", project, new string[0]);
                Assert.Equal(new[] { "A", "B", "C", "D" }, data.Defines);
                Assert.Equal(new[] { project.Replace('\\', '/') + "/Assets/Libs/Ref.dll" }, data.FullPathReferences);
                Assert.True(data.Unsafe);
                Assert.Equal(new[] { "/D:F", "/nowarn:CS0219", "/langversion:10", "/warnaserror+:\"CS 1\"" }, data.OtherArguments);
                Assert.Contains("Missing.dll could not be found as a system library", Assert.Single(data.Errors));

                // Control: a file that does not exist reads as nothing.
                var none = CompilationPipeline.ParseResponseFile("Assets/None/csc.rsp", project, new string[0]);
                Assert.Empty(none.Defines);
                Assert.Empty(none.OtherArguments);
                Assert.False(none.Unsafe);
            }
            finally { Directory.Delete(project, recursive: true); }
        }

        static void AddDefine(BuildTarget target, HashSet<string> defines) => defines.Add("FROM_DELEGATE");

        [Fact]
        public void BuildDefinesDelegatesAddToTheDefinesTheEditorReports()
        {
            var before = EditorUserBuildSettings.activeScriptCompilationDefines;
            Assert.DoesNotContain("FROM_DELEGATE", before);
            GetScriptCompilationDefinesDelegate handler = AddDefine;
            BuildDefines.getScriptCompilationDefinesDelegates += handler;
            try
            {
                Assert.Equal(new[] { "INPUT", "FROM_DELEGATE" }, BuildDefines.GetScriptCompilationDefines(BuildTarget.StandaloneOSX, new[] { "INPUT" }));
                Assert.Contains("FROM_DELEGATE", EditorUserBuildSettings.activeScriptCompilationDefines);
            }
            finally
            {
                BuildDefines.getScriptCompilationDefinesDelegates -= handler;
            }
            Assert.Equal(new[] { "INPUT" }, BuildDefines.GetScriptCompilationDefines(BuildTarget.StandaloneOSX, new[] { "INPUT" }));
            Assert.DoesNotContain("FROM_DELEGATE", EditorUserBuildSettings.activeScriptCompilationDefines);
        }

        [Fact]
        public void TheAnnotationTableListsTheEngineClassesAndSetsOnlyTheirOwnFlags()
        {
            var all = AnnotationUtility.GetAnnotations();
            Assert.Equal(Compat.BuiltinAnnotations.Length, all.Count(a => a.scriptClass == ""));
            var camera = AnnotationUtility.GetAnnotation(20, "");
            Assert.Equal((3, 1, 1), (camera.flags, camera.iconEnabled, camera.gizmoEnabled));
            var collider = AnnotationUtility.GetAnnotation(65, "");
            Assert.Equal(2, collider.flags);
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) => logs.Add(type + ": " + message);
            Application.logMessageReceived += capture;
            try
            {
                AnnotationUtility.SetIconEnabled(20, "", 0);
                AnnotationUtility.SetGizmoEnabled(20, "", 0, false);
                Assert.Equal((0, 0), (AnnotationUtility.GetAnnotation(20, "").iconEnabled, AnnotationUtility.GetAnnotation(20, "").gizmoEnabled));
                // A box collider has a gizmo and no icon: its icon setting does not change.
                AnnotationUtility.SetIconEnabled(65, "", 0);
                Assert.Equal(1, AnnotationUtility.GetAnnotation(65, "").iconEnabled);
                Assert.Empty(logs);
                AnnotationUtility.SetIconEnabled(114, "NoSuchScript", 0);
                Assert.Equal(new[] { "Log: Warning: Annotation not found!" }, logs);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                AnnotationUtility.SetIconEnabled(20, "", 1);
                AnnotationUtility.SetGizmoEnabled(20, "", 1, false);
            }
        }
    }
}
