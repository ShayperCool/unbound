using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A recompilation as the Unity 6000.0.71f1 Editor reports it (tests/projects/compileevents):
    /// started with the build's context, each compiled assembly's started and finished events with its
    /// messages, not-required assemblies, finished with the same context.</summary>
    public sealed class CompilationEventsTests
    {
        [Fact]
        public void ARecompilationRaisesItsEventsInOrderWithOneContext()
        {
            var seen = new List<string>();
            object started = null, finished = null;
            Action<object> onStarted = c => { started = c; seen.Add("started"); };
            Action<object> onFinished = c => { finished = c; seen.Add("finished"); };
#pragma warning disable 618
            Action<string> onAssemblyStarted = p => seen.Add("assemblyStarted " + p);
#pragma warning restore 618
            Action<string, CompilerMessage[]> onAssemblyFinished = (p, m) => seen.Add("assemblyFinished " + p + " " + m.Length + (m.Length > 0 ? " " + m[0].message : ""));
            Action<string> onNotRequired = p => seen.Add("notRequired " + p);
            Action<object> throwing = c => throw new InvalidOperationException("subscriber");
            CompilationPipeline.compilationStarted += throwing;
            CompilationPipeline.compilationStarted += onStarted;
            CompilationPipeline.compilationFinished += onFinished;
#pragma warning disable 618
            CompilationPipeline.assemblyCompilationStarted += onAssemblyStarted;
#pragma warning restore 618
            CompilationPipeline.assemblyCompilationFinished += onAssemblyFinished;
            CompilationPipeline.assemblyCompilationNotRequired += onNotRequired;
            try
            {
                using var logs = new RecordedLogs();
                var warning = CompilationPipeline.Message(false, "CS0219", "unused", null, 0, 0);
                CompilationPipeline.RaiseCompilation(new[]
                {
                    new CompilationPipeline.CompiledAssembly("Assembly-CSharp", true, new[] { warning }),
                    new CompilationPipeline.CompiledAssembly("Other", false, null),
                    new CompilationPipeline.CompiledAssembly("Assembly-CSharp-Editor", true, null),
                });
                Assert.Equal(new[]
                {
                    "started",
                    "assemblyStarted Library/ScriptAssemblies/Assembly-CSharp.dll",
                    "assemblyFinished Library/ScriptAssemblies/Assembly-CSharp.dll 1 warning CS0219: unused",
                    "notRequired Library/ScriptAssemblies/Other.dll",
                    "assemblyStarted Library/ScriptAssemblies/Assembly-CSharp-Editor.dll",
                    "assemblyFinished Library/ScriptAssemblies/Assembly-CSharp-Editor.dll 0",
                    "finished",
                }, seen);
                Assert.Same(started, finished);
                Assert.Equal("Bee.BeeDriver.ActiveBuild", started.GetType().FullName);
                // A throwing subscriber is logged and the others still run.
                Assert.Equal(new[] { "Exception: subscriber" }, logs.Logged);
            }
            finally
            {
                CompilationPipeline.compilationStarted -= throwing;
                CompilationPipeline.compilationStarted -= onStarted;
                CompilationPipeline.compilationFinished -= onFinished;
#pragma warning disable 618
                CompilationPipeline.assemblyCompilationStarted -= onAssemblyStarted;
#pragma warning restore 618
                CompilationPipeline.assemblyCompilationFinished -= onAssemblyFinished;
                CompilationPipeline.assemblyCompilationNotRequired -= onNotRequired;
            }
        }

        [Fact]
        public void TheHotReloadReportsAssembliesWithChangedInputsAndTheRestAsNotRequired()
        {
            var seen = new List<string>();
            Action<object> onStarted = c => seen.Add("started");
            Action<string, CompilerMessage[]> onAssemblyFinished = (p, m) => seen.Add("finished " + p + " " + string.Join(";", Array.ConvertAll(m, x => x.message)));
            Action<string> onNotRequired = p => seen.Add("notRequired " + p);
            CompilationPipeline.compilationStarted += onStarted;
            CompilationPipeline.assemblyCompilationFinished += onAssemblyFinished;
            CompilationPipeline.assemblyCompilationNotRequired += onNotRequired;
            try
            {
                static Unbound.Compiler.AssemblyResult Result(string name, string status) => new Unbound.Compiler.AssemblyResult { Name = name, Status = status };
                var failed = Result("A", "failed");
                failed.Errors.Add(new Unbound.Compiler.DiagnosticInfo { Id = "CS1525", Message = "bad" });
                failed.WarningList.Add(new Unbound.Compiler.DiagnosticInfo { Id = "CS0219", Message = "unused" });
                var response = new Unbound.Compiler.CompileResponse();
                response.Assemblies.AddRange(new[] { failed, Result("B", "skipped"), Result("C", "cached"), Result("D", "unchanged"), Result("E", "compiled") });
                Unbound.Host.HotReload.RaiseCompilationEvents(response);
                Assert.Equal(new[]
                {
                    "started",
                    "finished Library/ScriptAssemblies/A.dll error CS1525: bad;warning CS0219: unused",
                    "notRequired Library/ScriptAssemblies/B.dll",
                    "finished Library/ScriptAssemblies/C.dll ",
                    "notRequired Library/ScriptAssemblies/D.dll",
                    "finished Library/ScriptAssemblies/E.dll ",
                }, seen);
                // Control: a compile whose inputs did not change raises nothing.
                seen.Clear();
                var nothing = new Unbound.Compiler.CompileResponse();
                nothing.Assemblies.AddRange(new[] { Result("D", "unchanged"), Result("F", "skipped") });
                Unbound.Host.HotReload.RaiseCompilationEvents(nothing);
                Assert.Empty(seen);
            }
            finally
            {
                CompilationPipeline.compilationStarted -= onStarted;
                CompilationPipeline.assemblyCompilationFinished -= onAssemblyFinished;
                CompilationPipeline.assemblyCompilationNotRequired -= onNotRequired;
            }
        }

        [Fact]
        public void AnEditorDomainLoadEndsWithAfterAssemblyReload()
        {
            // Once, after ExitingEditMode on Play mode entry (the order after the load callbacks:
            // compileevents start.load).
            var seen = new List<string>();
            AssemblyReloadEvents.AssemblyReloadCallback after = () => seen.Add("afterAssemblyReload");
            Action<PlayModeStateChange> state = s => seen.Add(s.ToString());
            AssemblyReloadEvents.afterAssemblyReload += after;
            EditorApplication.playModeStateChanged += state;
            bool wasPlaying = Unbound.Runtime.EditorSession.IsPlaying;
            try
            {
                Unbound.Runtime.EditorSession.BeginEnteringPlayMode();
                EditorInitialization.LoadEditorDomain(enteringPlayMode: true);
            }
            finally
            {
                Unbound.Runtime.EditorSession.EndEnteringPlayMode();
                Unbound.Runtime.EditorSession.IsPlaying = wasPlaying;
                AssemblyReloadEvents.afterAssemblyReload -= after;
                EditorApplication.playModeStateChanged -= state;
            }
            Assert.Equal(new[] { "ExitingEditMode", "afterAssemblyReload" }, seen);
        }

        [Fact]
        public void AMessageNamesItsFileAsTheProjectDoes()
        {
            string previous = ProjectInfo.ProjectPath;
            string project = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unbound-compile-" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectInfo.ProjectPath = project;
                var error = CompilationPipeline.Message(true, "CS1525", "Invalid expression term ';'", project + "/Assets/Scripts/Changing.cs", 1, 59);
                Assert.Equal("Assets/Scripts/Changing.cs(1,59): error CS1525: Invalid expression term ';'", error.message);
                Assert.Equal("Assets/Scripts/Changing.cs", error.file);
                Assert.Equal(1, error.line);
                Assert.Equal(59, error.column);
                Assert.Equal(CompilerMessageType.Error, error.type);
                // Control: a diagnostic without a place has no file part.
                var placeless = CompilationPipeline.Message(false, "CS8021", "No value for RuntimeMetadataVersion", null, 0, 0);
                Assert.Equal("warning CS8021: No value for RuntimeMetadataVersion", placeless.message);
                Assert.Equal(CompilerMessageType.Warning, placeless.type);
            }
            finally { ProjectInfo.ProjectPath = previous; }
        }
    }
}
