using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class StubGeneratorTests
{
    /// <summary>A stand-in for Unity's core module: one of each construct the generator handles.</summary>
    const string UnityCore = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        namespace UnityEngine
        {
            public class Object
            {
                public string name { get; set; }
                public static void Destroy(Object obj, float t = 0.0f) {}
                public override string ToString() => "";
            }
            public class Component : Object
            {
                public T GetComponent<T>() where T : class => null;
                protected internal virtual void OnHit(ref int damage, out bool killed) { killed = false; }
            }
            public struct Vector2 { public float x, y; }
            public struct Vector3
            {
                public const float kEpsilon = 1E-05F;
                public float x, y, z;
                public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
                public static Vector3 operator +(Vector3 a, Vector3 b) => a;
                public static implicit operator Vector2(Vector3 v) => default;
                public float this[int index] { get => 0; set {} }
            }
            [Flags] public enum Mode : byte { None = 0, A = 1, B = 2 }
            public delegate void Callback<in T>(T value, int count = -1);
            public interface ISized { int Count { get; } void Resize(ref int width, out float scale); }
            public abstract class Shape : ISized
            {
                protected Shape(int sides) {}
                public abstract float Area { get; }
                public int Count => 0;
                public void Resize(ref int width, out float scale) { scale = 1; }
            }
            public sealed class Square : Shape
            {
                internal Square() : base(4) {}
                public override float Area => 1;
            }
            public class Pool<T> where T : class, new()
            {
                public T Get() => null;
                public void Release(T item) {}
                public event Action Changed;
                public ref T Peek() => throw null;
            }
            public static class Extensions { public static int Twice(this int x) => x * 2; }
            public class Numbers : IEnumerable<int>
            {
                IEnumerator<int> IEnumerable<int>.GetEnumerator() => null;
                IEnumerator IEnumerable.GetEnumerator() => null;
            }
            internal class Hidden { internal static int Secret(int a = 5, string s = "x\n", Mode m = Mode.B) => 0; }
            [Obsolete("use Vector3", true)] public class Old {}
            public class Outer { public class Inner { public int v; } protected class Guarded {} private class Private {} }
            public unsafe struct Buffer { public fixed byte data[4]; public int* pointer; }
            public ref struct Span2 { public int length; }
        }
        """;

    const string UnityPhysics = """
        namespace UnityEngine
        {
            public enum ForceMode { Force = 0, Impulse = 1 }
            [RequireComponentLike]
            public class Rigidbody : Component
            {
                public Vector3 velocity { get; set; }
                public void AddForce(Vector3 force, ForceMode mode = ForceMode.Impulse) {}
            }
            public class RequireComponentLike : System.Attribute {}
        }
        """;

    /// <summary>The hand-written implementation so far: partial types with a few members.</summary>
    const string HandWritten = """
        namespace UnityEngine
        {
            public partial class Object { public string name { get; set; } }
            public partial class Component : Object { }
            public partial struct Vector3 { public float x, y; }
        }
        """;

    /// <summary>What the bodies of stubs call (the real one lives in Runtime/Stubs.cs).</summary>
    const string StubSupport = """
        namespace Unbound { [System.AttributeUsage(System.AttributeTargets.All)] internal sealed class StubAttribute : System.Attribute {} }
        namespace Unbound.Runtime
        {
            internal static class Stubs
            {
                public static int Calls;
                public static void NotImplemented(string member) => Calls++;
                static class Box<T> { public static T Value; }
                public static ref T Ref<T>() => ref Box<T>.Value;
            }
        }
        """;

    static readonly MetadataReference[] Framework =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll" or "System.Collections.dll")
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    static (byte[] Image, List<string> Errors) Compile(string name, IEnumerable<string> sources, params string[] references)
    {
        var compilation = CSharpCompilation.Create(
            name,
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            Framework.Concat(references.Select(r => MetadataReference.CreateFromFile(r))),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        return (stream.ToArray(), result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList());
    }

    static string Write(string dir, string name, byte[] image)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".dll");
        File.WriteAllBytes(path, image);
        return path;
    }

    [Fact]
    public void GeneratedStubsCompileWithTheHandWrittenImplementation()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            var (coreImage, coreErrors) = Compile("UnityEngine.CoreModule", new[] { UnityCore });
            Assert.Empty(coreErrors);
            var core = Write(unity, "UnityEngine.CoreModule", coreImage);
            var (physicsImage, physicsErrors) = Compile("UnityEngine.PhysicsModule", new[] { UnityPhysics }, core);
            Assert.Empty(physicsErrors);
            Write(unity, "UnityEngine.PhysicsModule", physicsImage);

            var sourceDir = Path.Combine(root, "impl");
            Directory.CreateDirectory(Path.Combine(sourceDir, "Core"));
            File.WriteAllText(Path.Combine(sourceDir, "Core", "Impl.cs"), HandWritten);
            var (implImage, implErrors) = Compile("UnityEngine.CoreModule", new[] { HandWritten, StubSupport });
            Assert.Empty(implErrors);
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule", implImage);

            var output = Path.Combine(sourceDir, "Generated", "6000.0");
            var report = StubGenerator.Generate(unity, impl, output, sourceDir);
            Assert.Empty(report.Conflicts);
            Assert.Contains(report.Modules, m => m.Name == "UnityEngine.PhysicsModule" && m.NewTypes == 3);
            Assert.Equal(3, report.ExtendedTypes); // Object, Component and Vector3 got members
            var files = Directory.GetFiles(output, "*.g.cs").OrderBy(f => f, StringComparer.Ordinal).ToList();

            // The stubs and the hand-written sources compile together.
            var (image, errors) = Compile("UnityEngine.CoreModule",
                files.Select(File.ReadAllText).Append(HandWritten).Append(StubSupport));
            Assert.True(errors.Count == 0, string.Join("\n", errors));

            // A second run with the same inputs changes nothing.
            Assert.Equal(0, StubGenerator.Generate(unity, impl, output, sourceDir).ChangedFiles);

            var context = new AssemblyLoadContext("stubs", isCollectible: true);
            try
            {
                var assembly = context.LoadFromStream(new MemoryStream(image));
                var vector3 = assembly.GetType("UnityEngine.Vector3")!;
                Assert.NotNull(vector3.GetField("z"));
                Assert.Equal(1E-05f, vector3.GetField("kEpsilon")!.GetValue(null));
                Assert.NotNull(vector3.GetMethod("op_Implicit"));
                Assert.NotNull(vector3.GetProperty("Item"));
                var mode = assembly.GetType("UnityEngine.Mode")!;
                Assert.Equal(typeof(byte), Enum.GetUnderlyingType(mode));
                Assert.NotNull(mode.GetCustomAttribute<FlagsAttribute>());

                var rigidbody = assembly.GetType("UnityEngine.Rigidbody")!;
                Assert.Equal("UnityEngine.Component", rigidbody.BaseType!.FullName);
                Assert.Contains(rigidbody.GetCustomAttributesData(), a => a.AttributeType.Name == "StubAttribute");
                var addForce = rigidbody.GetMethod("AddForce")!;
                Assert.Equal(1, Convert.ToInt32(addForce.GetParameters()[1].DefaultValue));

                var obsolete = assembly.GetType("UnityEngine.Old")!.GetCustomAttribute<ObsoleteAttribute>()!;
                Assert.False(obsolete.IsError);
                Assert.NotNull(assembly.GetType("UnityEngine.Hidden"));
                Assert.NotNull(assembly.GetType("UnityEngine.Outer+Guarded"));
                Assert.Null(assembly.GetType("UnityEngine.Outer+Private"));
                Assert.True(assembly.GetType("UnityEngine.Span2")!.IsByRefLike);

                // A stub body records the call and returns a default.
                var pool = assembly.GetType("UnityEngine.Pool`1")!.MakeGenericType(typeof(object));
                var instance = Activator.CreateInstance(pool)!; // the constructor is a stub too
                var calls = assembly.GetType("Unbound.Runtime.Stubs")!.GetField("Calls")!;
                var before = (int)calls.GetValue(null)!;
                Assert.Null(pool.GetMethod("Get")!.Invoke(instance, null));
                Assert.Equal(before + 1, calls.GetValue(null));
                var shape = assembly.GetType("UnityEngine.Square")!;
                Assert.True(shape.GetProperty("Area")!.GetMethod!.IsVirtual);
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
    public void NonPartialHandWrittenTypesAreReportedNotExtended()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            var (coreImage, _) = Compile("UnityEngine.CoreModule", new[] { "namespace UnityEngine { public class Object { public int a; public int b; } }" });
            Write(unity, "UnityEngine.CoreModule", coreImage);
            const string handWritten = "namespace UnityEngine { public class Object { public int a; } }";
            var sourceDir = Path.Combine(root, "impl");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "Object.cs"), handWritten);
            var (implImage, _) = Compile("UnityEngine.CoreModule", new[] { handWritten });
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule", implImage);

            var report = StubGenerator.Generate(unity, impl, Path.Combine(sourceDir, "Generated", "6000.0"), sourceDir);
            Assert.Contains(report.Conflicts, c => c.Contains("not partial"));
            Assert.Equal(0, report.Members);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
