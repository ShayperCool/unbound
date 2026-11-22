using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Scripting.ScriptCompilation;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The internal Editor hooks package editor code (Burst's loader) calls, as the Unity
    /// 6000.0.71f1 Editor answers them (tests/projects/editorhooks, tests/projects/compileevents):
    /// the update's tick, update and delayCall order with CallDelayed, PathToAbsolutePath, the
    /// default assembly search paths and the Editor compilation's own assemblyCompilationFinished.</summary>
    [Collection("Frames")]
    public sealed class EditorHooksTests
    {
        [Fact]
        public void AnUpdateRunsTickThenUpdateThenDelayCall()
        {
            var seen = new List<string>();
            EditorApplication.CallbackFunction onUpdate = null;
            onUpdate = () =>
            {
                seen.Add("update");
                EditorApplication.update -= onUpdate;
                // Registered during the update: a delayCall still runs in it, a delayed call in
                // the next one's tick.
                EditorApplication.delayCall += () => seen.Add("delayCallFromUpdate");
                EditorApplication.CallDelayed(() => seen.Add("delayedFromUpdate"));
            };
            EditorApplication.update += onUpdate;
            EditorApplication.delayCall += () => seen.Add("delayCall");
            EditorApplication.CallDelayed(() => seen.Add("delayed0"));
            var cancel = EditorApplication.CallDelayed(() => seen.Add("cancelled"));
            cancel();
            var cancelLong = EditorApplication.CallDelayed(() => seen.Add("delayedLong"), 3600);
            Task.Run(() => EditorApplication.CallDelayed(() => seen.Add("fromThread"))).Wait();
            try
            {
                EditorApplication.RunEditorCallbacksNow();
                Assert.Equal(new[] { "delayed0", "fromThread", "update", "delayCall", "delayCallFromUpdate" }, seen);
                seen.Clear();
                EditorApplication.RunEditorCallbacksNow();
                Assert.Equal(new[] { "delayedFromUpdate" }, seen);
            }
            finally
            {
                EditorApplication.update -= onUpdate;
                EditorApplication.delayCall = null;
                cancelLong();
            }
        }

        [Fact]
        public void ADelayedCallWaitsForItsDelayAndRunsOnce()
        {
            int runs = 0;
            // Long enough that a slow first update cannot reach it.
            var cancel = EditorApplication.CallDelayed(() => runs++, 1.0);
            try
            {
                EditorApplication.RunEditorCallbacksNow();
                Assert.Equal(0, runs);
                System.Threading.Thread.Sleep(1100);
                EditorApplication.RunEditorCallbacksNow();
                EditorApplication.RunEditorCallbacksNow();
                Assert.Equal(1, runs);
            }
            finally { cancel(); }
        }

        [Fact]
        public void PathToAbsolutePathJoinsProjectPathsAsGiven()
        {
            var project = ProjectInfo.ProjectPath;
            ProjectInfo.ProjectPath = "/work/Game/";
            try
            {
                Assert.Equal("/work/Game/Assets/Scripts/A.cs", FileUtil.PathToAbsolutePath("Assets/Scripts/A.cs"));
                Assert.Equal("/work/Game/assets/../Other\\x", FileUtil.PathToAbsolutePath("assets/../Other\\x"));
                Assert.Equal("/work/Game", FileUtil.PathToAbsolutePath(""));
                Assert.Equal("/work/Game/Packages/com.missing.package/F.txt", FileUtil.PathToAbsolutePath("Packages/com.missing.package/F.txt"));
                Assert.Equal("/tmp/Absolute/F.txt", FileUtil.PathToAbsolutePath("/tmp/Absolute/F.txt"));
                Assert.Throws<ArgumentNullException>(() => FileUtil.PathToAbsolutePath(null));
            }
            finally { ProjectInfo.ProjectPath = project; }
        }

        [Fact]
        public void TheSearchPathsAreThePrecompiledFoldersSortedThenTheEngineAndScriptAssemblies()
        {
            var data = UnityInstallInfo.DataPath;
            var root = Path.Combine(Path.GetTempPath(), "unbound-searchpaths-" + Guid.NewGuid().ToString("N"));
            var contents = Path.Combine(root, "Unity.app", "Contents");
            void Touch(string path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "");
            }
            Touch(Path.Combine(contents, "Managed", "UnityEditor.Graphs.dll"));
            Touch(Path.Combine(contents, "PlaybackEngines", "ZStandaloneSupport", "UnityEditor.ZStandalone.Extensions.dll"));
            Touch(Path.Combine(contents, "PlaybackEngines", "AStandaloneSupport", "UnityEditor.A.Extensions.Xcode.dll"));
            Touch(Path.Combine(contents, "PlaybackEngines", "BPlayer", "UnityEditor.B.Extensions.dll"));
            UnityInstallInfo.DataPath = contents;
            try
            {
                var c = contents.Replace('\\', '/');
                Assert.Equal(new[]
                {
                    c + "/Managed", c + "/PlaybackEngines/BPlayer", c + "/PlaybackEngines/ZStandaloneSupport",
                    c + "/Managed/UnityEngine", "Library/ScriptAssemblies",
                }, AssemblyHelper.GetDefaultAssemblySearchPaths());
            }
            finally
            {
                UnityInstallInfo.DataPath = data;
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TheEditorCompilationsFinishedEventFollowsThePipelinesWithTheBuildsErrors()
        {
            var seen = new List<string>();
            Action<string, CompilerMessage[]> onPublic = (path, messages) => seen.Add("public " + path);
            Action<ScriptAssembly, CompilerMessage[]> onInternal = (assembly, messages) =>
                seen.Add("internal " + assembly.FullPath + " errors=" + assembly.HasCompileErrors + " defines=" + string.Join(";", assembly.Defines) + " messages=" + messages.Length);
            var compilation = EditorCompilationInterface.Instance;
            CompilationPipeline.assemblyCompilationFinished += onPublic;
            compilation.assemblyCompilationFinished += onInternal;
            try
            {
                var error = CompilationPipeline.Message(true, "CS1525", "Invalid expression term ';'", null, 0, 0);
                CompilationPipeline.RaiseCompilation(new[]
                {
                    new CompilationPipeline.CompiledAssembly("First", true, null, new[] { "PROBE_DEFINE" }),
                    new CompilationPipeline.CompiledAssembly("Skipped", false, null),
                    new CompilationPipeline.CompiledAssembly("Failing", true, new[] { error }),
                });
                Assert.Equal(new[]
                {
                    "public Library/ScriptAssemblies/First.dll",
                    "internal Library/ScriptAssemblies/First.dll errors=True defines=PROBE_DEFINE messages=0",
                    "public Library/ScriptAssemblies/Failing.dll",
                    "internal Library/ScriptAssemblies/Failing.dll errors=True defines= messages=1",
                }, seen);
                seen.Clear();
                CompilationPipeline.RaiseCompilation(new[] { new CompilationPipeline.CompiledAssembly("First", true, null) });
                Assert.Equal("internal Library/ScriptAssemblies/First.dll errors=False defines= messages=0", seen[1]);
            }
            finally
            {
                CompilationPipeline.assemblyCompilationFinished -= onPublic;
                compilation.assemblyCompilationFinished -= onInternal;
            }
        }
    }
}
