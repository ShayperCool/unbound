using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class FacadeGeneratorTests
{
    /// <summary>A load context like the game's: <c>UnityEngine.CoreModule</c> is the implementation,
    /// every other assembly comes from the facade directory or the given extra directories.</summary>
    sealed class GameContext(string implementation, params string[] dirs) : AssemblyLoadContext("unbound-test", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == UnityModules.ImplAssemblyName)
                return LoadFromStream(new MemoryStream(File.ReadAllBytes(implementation)));
            foreach (var dir in dirs)
            {
                var path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path))
                    return LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
            }
            return null;
        }
    }

    const string ImplSource = """
        namespace UnityEngine
        {
            public class Object {}
            public class Rigidbody : Object
            {
                public class Nested { public int Value = 42; }
                public float force;
                public void AddForce(float x) { force += x; }
            }
            public class GUIText {}
        }
        """;

    /// <summary>A stand-in Unity release: a core module, a physics module with a nested type, the
    /// monolithic UnityEngine.dll forwarding to them and defining a legacy type of its own, and a
    /// library that is not a module.</summary>
    static string FakeUnity(string root)
    {
        var unity = Path.Combine(root, "Managed", "UnityEngine");
        var core = TestAssemblies.Compile(unity, "UnityEngine.CoreModule", "namespace UnityEngine { public class Object {} }");
        var physics = TestAssemblies.Compile(unity, "UnityEngine.PhysicsModule", """
            namespace UnityEngine
            {
                public class Rigidbody : Object
                {
                    public class Nested { public int Value; }
                    public void AddForce(float x) {}
                }
            }
            """, core);
        TestAssemblies.Compile(unity, "UnityEngine", """
            [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(UnityEngine.Object))]
            [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(UnityEngine.Rigidbody))]
            namespace UnityEngine { public class GUIText {} }
            """, core, physics);
        TestAssemblies.Compile(unity, "Unity.Cecil", "public class NotAModule {}");
        return unity;
    }

    [Fact]
    public void FacadesForwardModuleTypesToTheImplementation()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = FakeUnity(root);
            var facades = Path.Combine(root, "facades");
            var report = FacadeGenerator.Generate(unity, facades);

            Assert.False(report.Reused);
            Assert.Equal(new[] { "UnityEngine", "UnityEngine.PhysicsModule" }, report.Facades.Select(f => f.Name).Order(StringComparer.Ordinal));
            Assert.False(File.Exists(Path.Combine(facades, "UnityEngine.CoreModule.dll")));
            Assert.False(File.Exists(Path.Combine(facades, "Unity.Cecil.dll")));
            // UnityEngine.dll: GUIText (defined), Object, Rigidbody and Rigidbody/Nested (forwarded).
            Assert.Equal(4, report.Facades.Single(f => f.Name == "UnityEngine").Types);

            // Game code compiled against Unity's modules, and an old plugin compiled against a
            // monolithic UnityEngine.dll that defined everything itself.
            var game = TestAssemblies.Compile(Path.Combine(root, "game"), "Game", """
                public static class Game
                {
                    public static string Where() => typeof(UnityEngine.Rigidbody).Assembly.GetName().Name;
                    public static void Push() => new UnityEngine.Rigidbody().AddForce(2);
                    public static int Nested() => new UnityEngine.Rigidbody.Nested().Value;
                }
                """, Path.Combine(unity, "UnityEngine.CoreModule.dll"), Path.Combine(unity, "UnityEngine.PhysicsModule.dll"));
            var oldEngine = TestAssemblies.Compile(Path.Combine(root, "old"), "UnityEngine", """
                namespace UnityEngine
                {
                    public class Object {}
                    public class Rigidbody : Object { public class Nested { public int Value; } }
                    public class GUIText {}
                }
                """);
            var plugin = TestAssemblies.Compile(Path.Combine(root, "plugin"), "Plugin", """
                public static class Plugin
                {
                    public static int Nested() => new UnityEngine.Rigidbody.Nested().Value;
                    public static object Text() => new UnityEngine.GUIText();
                }
                """, oldEngine);
            var implementation = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", ImplSource);

            var context = new GameContext(implementation, facades, Path.GetDirectoryName(game)!, Path.GetDirectoryName(plugin)!);
            try
            {
                var gameType = context.LoadFromAssemblyName(new AssemblyName("Game")).GetType("Game")!;
                Assert.Equal("UnityEngine.CoreModule", gameType.GetMethod("Where")!.Invoke(null, null));
                Assert.Equal(42, gameType.GetMethod("Nested")!.Invoke(null, null));
                gameType.GetMethod("Push")!.Invoke(null, null); // binds a member through the facade
                var pluginType = context.LoadFromAssemblyName(new AssemblyName("Plugin")).GetType("Plugin")!;
                Assert.Equal(42, pluginType.GetMethod("Nested")!.Invoke(null, null));
                Assert.Equal("UnityEngine.CoreModule", pluginType.GetMethod("Text")!.Invoke(null, null)!.GetType().Assembly.GetName().Name);
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FacadesKeepTheModuleIdentityAndAreDeterministic()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = FakeUnity(root);
            var first = Path.Combine(root, "a");
            var second = Path.Combine(root, "b");
            FacadeGenerator.Generate(unity, first);
            FacadeGenerator.Generate(unity, second);
            foreach (var file in Directory.GetFiles(first, "*.dll"))
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(second, Path.GetFileName(file))));

            using var pe = new PEReader(File.OpenRead(Path.Combine(first, "UnityEngine.PhysicsModule.dll")));
            var md = pe.GetMetadataReader();
            Assert.Equal("UnityEngine.PhysicsModule", md.GetString(md.GetAssemblyDefinition().Name));
            Assert.Single(md.TypeDefinitions); // <Module> only
            var target = md.GetAssemblyReference(md.AssemblyReferences.Single());
            Assert.Equal("UnityEngine.CoreModule", md.GetString(target.Name));
            Assert.Equal(new Version(0, 0, 0, 0), target.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UpToDateFacadesAreReused()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = FakeUnity(root);
            var facades = Path.Combine(root, "facades");
            Assert.False(FacadeGenerator.Generate(unity, facades).Reused);
            var again = FacadeGenerator.Generate(unity, facades);
            Assert.True(again.Reused);
            Assert.Equal(2, again.Assemblies);

            // A changed input regenerates.
            File.SetLastWriteTimeUtc(Path.Combine(unity, "UnityEngine.PhysicsModule.dll"), DateTime.UtcNow.AddMinutes(1));
            Assert.False(FacadeGenerator.Generate(unity, facades).Reused);
            // So does a deleted output.
            File.Delete(Path.Combine(facades, "UnityEngine.dll"));
            Assert.False(FacadeGenerator.Generate(unity, facades).Reused);
            Assert.True(File.Exists(Path.Combine(facades, "UnityEngine.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The installed Unity release the integration tests use, if any.</summary>
    static string? UnityManagedEngineDir()
    {
        var dir = Environment.GetEnvironmentVariable("UNBOUND_TEST_UNITY_MANAGED_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Unity/Hub/Editor/6000.0.71f1/Editor/Data/Managed/UnityEngine");
        return File.Exists(Path.Combine(dir, "UnityEngine.PhysicsModule.dll")) ? dir : null;
    }

    [Fact]
    public void RealUnityPhysicsModuleForwardsRigidbody()
    {
        if (UnityManagedEngineDir() is not { } unity)
            return; // no Unity install on this machine
        var root = TestAssemblies.TempDir();
        try
        {
            var facades = Path.Combine(root, "facades");
            var report = FacadeGenerator.Generate(unity, facades);
            Assert.True(report.Assemblies > 100);
            Assert.Contains(report.Facades, f => f.Name == "UnityEngine" && f.Types > 1000);
            Assert.Contains(report.Facades, f => f.Name == "UnityEditor.CoreModule");

            var implementation = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule",
                "namespace UnityEngine { public class Rigidbody {} }");
            var context = new GameContext(implementation, facades);
            try
            {
                using (context.EnterContextualReflection())
                {
                    var type = Type.GetType("UnityEngine.Rigidbody, UnityEngine.PhysicsModule", throwOnError: true)!;
                    Assert.Equal("UnityEngine.CoreModule", type.Assembly.GetName().Name);
                    Assert.Same(type, Type.GetType("UnityEngine.Rigidbody, UnityEngine", throwOnError: true));
                }
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
