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

        [Fact]
        public void AScriptAssemblyIsLinkedUnderTheProjectsLibraryAndRelinkedToANewImage()
        {
            var directory = TempDirectory();
            var before = ProjectInfo.ProjectPath;
            try
            {
                ProjectInfo.ProjectPath = Path.Combine(directory, "view");
                var first = Path.Combine(directory, "first.dll");
                var second = Path.Combine(directory, "second.dll");
                File.WriteAllText(first, "first");
                File.WriteAllText(second, "second");
                var shown = GameDomain.ShowScriptAssembly("Assembly-CSharp", first);
                Assert.Equal(Path.Combine(directory, "view", "Library", "ScriptAssemblies", "Assembly-CSharp.dll"), shown);
                Assert.Equal("first", File.ReadAllText(shown));
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
    }
}
