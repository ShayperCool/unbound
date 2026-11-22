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

        static List<string> LoadEditorDomainForPlayMode()
        {
            var seen = new List<string>();
            AssemblyReloadEvents.AssemblyReloadCallback after = () => seen.Add("afterAssemblyReload " + PlayState());
            AssemblyReloadEvents.AssemblyReloadCallback beforeReload = () => seen.Add("beforeAssemblyReload");
            Action<PlayModeStateChange> state = s => seen.Add(s + " " + PlayState());
            AssemblyReloadEvents.afterAssemblyReload += after;
            AssemblyReloadEvents.beforeAssemblyReload += beforeReload;
            EditorApplication.playModeStateChanged += state;
            bool wasPlaying = Unbound.Runtime.EditorSession.IsPlaying;
            // As `unbound run` starts: the session counts as playing until the load says otherwise.
            Unbound.Runtime.EditorSession.IsPlaying = true;
            try
            {
                EditorInitialization.BeforePlayMode();
            }
            finally
            {
                Unbound.Runtime.EditorSession.IsPlaying = wasPlaying;
                AssemblyReloadEvents.afterAssemblyReload -= after;
                AssemblyReloadEvents.beforeAssemblyReload -= beforeReload;
                EditorApplication.playModeStateChanged -= state;
            }
            return seen;
        }

        [Fact]
        public void AnEnterRequestFromTheKeptDomainsLoadIsAnsweredByEnteringPlayMode()
        {
            // An editor script that enters Play mode from its load (as probe runners do) while the
            // options keep the domain: entering Play mode answers it, nothing stays pending.
            bool enabled = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            bool wasPlaying = Unbound.Runtime.EditorSession.IsPlaying;
            AssemblyReloadEvents.AssemblyReloadCallback enter = () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.EnterPlaymode();
            };
            AssemblyReloadEvents.afterAssemblyReload += enter;
            try
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                Unbound.Runtime.EditorSession.IsPlaying = true;
                EditorInitialization.BeforePlayMode();
                Assert.Equal("True/True", PlayState());
            }
            finally
            {
                AssemblyReloadEvents.afterAssemblyReload -= enter;
                Unbound.Runtime.EditorSession.IsPlaying = wasPlaying;
                EditorSettings.enterPlayModeOptionsEnabled = enabled;
                EditorSettings.enterPlayModeOptions = options;
            }
        }

        [Fact]
        public void AnEnterRequestMadeWhileEnteringPlayModeIsIgnored()
        {
            // An editor script that asks to enter Play mode from the load of the domain reloaded for
            // it (Play mode entry has begun): the request is ignored, so Play mode does not report a
            // change still to come (toolapis playRequest.seen).
            bool wasPlaying = Unbound.Runtime.EditorSession.IsPlaying;
            string during = null;
            AssemblyReloadEvents.AssemblyReloadCallback enter = () =>
            {
                EditorApplication.EnterPlaymode();
                during = PlayState();
            };
            AssemblyReloadEvents.afterAssemblyReload += enter;
            try
            {
                Unbound.Runtime.EditorSession.IsPlaying = true;
                EditorInitialization.BeforePlayMode();
                Assert.Equal("False/True", during);
                Assert.Equal("True/True", PlayState());
                // Control: once in Play mode, a request to leave it stays pending until applied.
                Unbound.Runtime.EditorSession.StartedInEditMode = true;
                EditorApplication.ExitPlaymode();
                Assert.Equal("True/False", PlayState());
            }
            finally
            {
                AssemblyReloadEvents.afterAssemblyReload -= enter;
                Unbound.Runtime.EditorSession.Request(true);
                Unbound.Runtime.EditorSession.StartedInEditMode = false;
                Unbound.Runtime.EditorSession.IsPlaying = wasPlaying;
            }
        }

        /// <summary>isPlaying and isPlayingOrWillChangePlaymode, as "playing/willChange" (Unbound's
        /// session states, not values an Editor capture records).</summary>
        static string PlayState() => EditorApplication.isPlaying + "/" + EditorApplication.isPlayingOrWillChangePlaymode;

        [Fact]
        public void EnteringPlayModeReloadsTheDomainUnlessItsOptionsDisableIt()
        {
            // The domain load ends with afterAssemblyReload, after Play mode entry's ExitingEditMode
            // and, when entering Play mode reloads the domain, beforeAssemblyReload (compileevents
            // start.load).
            bool enabled = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            try
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
                Assert.Equal(new[] { "ExitingEditMode False/True", "beforeAssemblyReload", "afterAssemblyReload False/True" }, LoadEditorDomainForPlayMode());
                // Options that keep the domain: the domain loads as the Editor's start does, in Edit mode
                // (ending with afterAssemblyReload), then Play mode is entered without a reload.
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                // The load in Edit mode (not playing, not changing), then Play mode entry (changing).
                Assert.Equal(new[] { "afterAssemblyReload False/False", "ExitingEditMode False/True" }, LoadEditorDomainForPlayMode());
                // Control: the same options while the options are off reload the domain.
                EditorSettings.enterPlayModeOptionsEnabled = false;
                Assert.Equal(new[] { "ExitingEditMode False/True", "beforeAssemblyReload", "afterAssemblyReload False/True" }, LoadEditorDomainForPlayMode());
            }
            finally
            {
                EditorSettings.enterPlayModeOptionsEnabled = enabled;
                EditorSettings.enterPlayModeOptions = options;
            }
        }

        [Fact]
        public void HandlersTheEditorCallsBeforeOrAfterItsReloadImportAtOnce()
        {
            // ExitingEditMode and beforeAssemblyReload come before the Editor's reload, and
            // afterAssemblyReload after the load: imports asked for there are not deferred
            // (Unbound's choice; tests/projects/initializeorder measures the load callbacks' only).
            bool enabled = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            var seen = new List<string>();
            Action<PlayModeStateChange> state = s => seen.Add(s + " " + EditorInitialization.DomainLoading);
            AssemblyReloadEvents.AssemblyReloadCallback before = () => seen.Add("before " + EditorInitialization.DomainLoading);
            AssemblyReloadEvents.AssemblyReloadCallback after = () => seen.Add("after " + EditorInitialization.DomainLoading);
            EditorApplication.playModeStateChanged += state;
            AssemblyReloadEvents.beforeAssemblyReload += before;
            AssemblyReloadEvents.afterAssemblyReload += after;
            bool wasPlaying = Unbound.Runtime.EditorSession.IsPlaying;
            try
            {
                EditorSettings.enterPlayModeOptionsEnabled = false;
                Unbound.Runtime.EditorSession.IsPlaying = true;
                EditorInitialization.BeforePlayMode();
                Assert.Equal(new[] { "ExitingEditMode False", "before False", "after False" }, seen);
            }
            finally
            {
                Unbound.Runtime.EditorSession.IsPlaying = wasPlaying;
                EditorApplication.playModeStateChanged -= state;
                AssemblyReloadEvents.beforeAssemblyReload -= before;
                AssemblyReloadEvents.afterAssemblyReload -= after;
                EditorSettings.enterPlayModeOptionsEnabled = enabled;
                EditorSettings.enterPlayModeOptions = options;
            }
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
