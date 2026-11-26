using System;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Unbound.Host;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Assembly.Location and CodeBase of the game's assemblies as the Editor answers them
    /// (tests/projects/compilesetup location.*): game code's calls are redirected to
    /// Unbound.AssemblyFiles, which names the file the game sees (a plugin's own, a script
    /// assembly's Library/ScriptAssemblies link) instead of the image Unbound loaded.</summary>
    [Collection("EditorState")]
    public sealed class AssemblyLocationTests
    {
        const string Source = @"
using System.Reflection;
public static class Probe
{
    public static string Location() => typeof(Probe).Assembly.Location;
#pragma warning disable SYSLIB0012
    public static string CodeBase() => typeof(Probe).Assembly.CodeBase;
#pragma warning restore SYSLIB0012
    public static string NullLocation() => ((Assembly)null).Location;
}";

        static Assembly Loaded(bool redirected, string directory)
        {
            var image = WideFloatsTests.Emit(WideFloatsTests.Compilation(Source, OptimizationLevel.Release, "AssemblyLocationProbe")).Image;
            var file = Path.Combine(directory, "AssemblyLocationProbe.dll");
            File.WriteAllBytes(file, redirected ? FrameworkShims.Redirect(image) : image);
            return new System.Runtime.Loader.AssemblyLoadContext("assembly locations " + Guid.NewGuid()).LoadFromAssemblyPath(file);
        }

        static object Call(Assembly assembly, string method)
        {
            try { return assembly.GetType("Probe")!.GetMethod(method)!.Invoke(null, null)!; }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }

        static string TempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "unbound-locations-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        [Fact]
        public void AnAssemblyShownElsewhereNamesThatFileAndOthersTheirOwn()
        {
            var directory = TempDirectory();
            try
            {
                var redirected = Loaded(redirected: true, Directory.CreateDirectory(Path.Combine(directory, "a")).FullName);
                var loadedFrom = redirected.Location;
                // Not shown elsewhere: the file it was loaded from.
                Assert.Equal(loadedFrom, Call(redirected, "Location"));
                Assert.Equal("file://" + loadedFrom, Call(redirected, "CodeBase"));
                GameDomain.Show(redirected, "/project/Assets/Plugins/AssemblyLocationProbe.dll");
                Assert.Equal("/project/Assets/Plugins/AssemblyLocationProbe.dll", Call(redirected, "Location"));
                Assert.Equal("file:///project/Assets/Plugins/AssemblyLocationProbe.dll", Call(redirected, "CodeBase"));
                // Without the redirect .NET names the loaded image.
                var plain = Loaded(redirected: false, Directory.CreateDirectory(Path.Combine(directory, "b")).FullName);
                GameDomain.Show(plain, "/project/Assets/Plugins/AssemblyLocationProbe.dll");
                Assert.Equal(plain.Location, Call(plain, "Location"));
                // A null assembly throws as the property's call does.
                Assert.Throws<NullReferenceException>(() => Call(redirected, "NullLocation"));
                Assert.Throws<NullReferenceException>(() => Call(plain, "NullLocation"));
            }
            finally { Directory.Delete(directory, true); }
        }

        /// <summary>A project root with its view in Unbound's directory, whose Library/ links to
        /// Unbound's own (as the host builds it, crates/unbound-runtime/src/view.rs).</summary>
        static (string Root, string View, string OwnLibrary) Project(string directory)
        {
            var root = Path.Combine(directory, "project");
            var view = Path.Combine(root, ProjectInfo.UnboundFolder, ProjectInfo.ViewFolder);
            var own = Path.Combine(root, ProjectInfo.UnboundFolder, "Library");
            Directory.CreateDirectory(view);
            Directory.CreateDirectory(own);
            Directory.CreateSymbolicLink(Path.Combine(view, "Library"), own);
            return (root, view, own);
        }

        [Fact]
        public void AScriptAssemblyIsLinkedUnderTheViewsLibraryAndRelinkedToANewImage()
        {
            var directory = TempDirectory();
            var before = ProjectInfo.ProjectPath;
            try
            {
                var (_, view, own) = Project(directory);
                ProjectInfo.ProjectPath = view;
                var first = Path.Combine(directory, "first.dll");
                var second = Path.Combine(directory, "second.dll");
                File.WriteAllText(first, "first");
                File.WriteAllText(second, "second");
                var shown = GameDomain.ShowScriptAssembly("Assembly-CSharp", first);
                Assert.Equal(Path.Combine(view, "Library", "ScriptAssemblies", "Assembly-CSharp.dll"), shown);
                Assert.Equal("first", File.ReadAllText(shown));
                // The link lives in Unbound's own Library/.
                Assert.True(File.Exists(Path.Combine(own, "ScriptAssemblies", "Assembly-CSharp.dll")));
                Assert.Equal(shown, GameDomain.ShowScriptAssembly("Assembly-CSharp", first));
                // A reload's new image: the link follows it.
                Assert.Equal(shown, GameDomain.ShowScriptAssembly("Assembly-CSharp", second));
                Assert.Equal("second", File.ReadAllText(shown));
                // No project (the engine's own tests): nothing is linked.
                ProjectInfo.ProjectPath = "";
                Assert.Null(GameDomain.ShowScriptAssembly("Assembly-CSharp", first));
            }
            finally
            {
                ProjectInfo.ProjectPath = before;
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void AProjectsOwnLibraryIsNeverWritten()
        {
            var directory = TempDirectory();
            var before = ProjectInfo.ProjectPath;
            try
            {
                // A doctor run loads the game's code with the project's own root as its path.
                var (root, _, _) = Project(directory);
                var unity = Path.Combine(root, "Library", "ScriptAssemblies");
                Directory.CreateDirectory(unity);
                File.WriteAllText(Path.Combine(unity, "Assembly-CSharp.dll"), "unity-built");
                var image = Path.Combine(directory, "image.dll");
                File.WriteAllText(image, "image");
                ProjectInfo.ProjectPath = root;
                Assert.Null(GameDomain.ShowScriptAssembly("Assembly-CSharp", image));
                Assert.Null(GameDomain.ShowScriptAssembly("Other", image));
                Assert.Equal("unity-built", File.ReadAllText(Path.Combine(unity, "Assembly-CSharp.dll")));
                Assert.Equal(new[] { Path.Combine(unity, "Assembly-CSharp.dll") }, Directory.GetFiles(unity));
                // A view whose Library/ is a folder of its own (not Unbound's): nothing either.
                var other = Path.Combine(directory, "other");
                Directory.CreateDirectory(Path.Combine(other, "Library"));
                ProjectInfo.ProjectPath = other;
                Assert.Null(GameDomain.ShowScriptAssembly("Assembly-CSharp", image));
                Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(other, "Library")));
            }
            finally
            {
                ProjectInfo.ProjectPath = before;
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void APluginWithOrWithoutAMetaNamesItsOwnFileAndAScriptAssemblyItsLink()
        {
            var directory = TempDirectory();
            var before = ProjectInfo.ProjectPath;
            try
            {
                var (_, view, _) = Project(directory);
                ProjectInfo.ProjectPath = view;
                var dll = Path.Combine(view, "Assets", "Plugins", "Foo.dll");
                var adapted = Path.Combine(directory, "adapted", "Foo.dll");
                var withMeta = new GameAssembly { Name = "Foo", Dll = dll, PluginGuid = "0123456789abcdef0123456789abcdef" };
                var withoutMeta = new GameAssembly { Name = "Foo", Dll = dll };
                // Loaded from its own file: nothing to show; from an adapted image: its own file.
                Assert.Null(GameDomain.ShownFileOf(withMeta, dll));
                Assert.Equal(dll, GameDomain.ShownFileOf(withMeta, adapted));
                Assert.Null(GameDomain.ShownFileOf(withoutMeta, dll));
                Assert.Equal(dll, GameDomain.ShownFileOf(withoutMeta, adapted));
                // A compiled script assembly: Library/ScriptAssemblies of the view.
                var compiled = Path.Combine(directory, "Assembly-CSharp.dll");
                File.WriteAllText(compiled, "compiled");
                var scripts = new GameAssembly { Name = "Assembly-CSharp", Dll = compiled, Sources = new[] { "Assets/Probe.cs" } };
                Assert.Equal(Path.Combine(view, "Library", "ScriptAssemblies", "Assembly-CSharp.dll"), GameDomain.ShownFileOf(scripts, compiled));
            }
            finally
            {
                ProjectInfo.ProjectPath = before;
                Directory.Delete(directory, true);
            }
        }
    }
}
