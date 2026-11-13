using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
            internal static class Compat { public static bool RemovedEditorSettingsMeasured = true; }
            internal static class Stubs
            {
                public static int Calls;
                public static void NotImplemented(string member) => Calls++;
                static class Box<T> { public static T Value; }
                public static ref T Ref<T>() => ref Box<T>.Value;
            }
        }
        """;

    /// <summary>No release table: the synthetic modules have none of its members.</summary>
    static readonly Dictionary<string, StubDefault> NoReturns = new();

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
            var report = StubGenerator.Generate(unity, impl, output, sourceDir, returns: NoReturns);
            Assert.Empty(report.Errors);
            Assert.Empty(report.Conflicts);
            Assert.Contains(report.Modules, m => m.Name == "UnityEngine.PhysicsModule" && m.NewTypes == 3);
            Assert.Equal(3, report.ExtendedTypes); // Object, Component and Vector3 got members
            var files = Directory.GetFiles(output, "*.g.cs").OrderBy(f => f, StringComparer.Ordinal).ToList();

            // The stubs and the hand-written sources compile together.
            var (image, errors) = Compile("UnityEngine.CoreModule",
                files.Select(File.ReadAllText).Append(HandWritten).Append(StubSupport));
            Assert.True(errors.Count == 0, string.Join("\n", errors));

            // A second run with the same inputs changes nothing.
            Assert.Equal(0, StubGenerator.Generate(unity, impl, output, sourceDir, returns: NoReturns).ChangedFiles);

            // The assembly file names every Unity assembly read with its module version id, so stubs
            // generated from other files (another OS's Editor build) differ visibly.
            static string Mvid(string path)
            {
                using var pe = new PEReader(File.OpenRead(path));
                var md = pe.GetMetadataReader();
                return md.GetGuid(md.GetModuleDefinition().Mvid).ToString("D");
            }
            var assemblyFile = File.ReadAllText(Path.Combine(output, "Unbound.Assembly.g.cs"));
            Assert.Contains("// Generated from these Unity assemblies (file, module version id):\n"
                + $"//   UnityEngine.CoreModule.dll {Mvid(core)}\n"
                + $"//   UnityEngine.PhysicsModule.dll {Mvid(Path.Combine(unity, "UnityEngine.PhysicsModule.dll"))}\n\n",
                assemblyFile);
            var (otherBuild, otherErrors) = Compile("UnityEngine.PhysicsModule", new[] { UnityPhysics + "\n// another build" }, core);
            Assert.Empty(otherErrors);
            Write(unity, "UnityEngine.PhysicsModule", otherBuild);
            Assert.Equal(1, StubGenerator.Generate(unity, impl, output, sourceDir, returns: NoReturns).ChangedFiles);
            Assert.NotEqual(assemblyFile, File.ReadAllText(Path.Combine(output, "Unbound.Assembly.g.cs")));

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
    public void ImporterAccessorsCheckBackingBeforeTheirStubBodies()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            const string reference = """
                namespace UnityEngine { public class Object {} }
                namespace UnityEditor
                {
                    public class AssetImporter : UnityEngine.Object
                    {
                        public int count { get; set; }
                        public static int staticCount => 0;
                        public int removed => throw new System.NotSupportedException("removed");
                        public bool fixedTrue { get { return true; } set {} }
                        public bool fixedFalse { get => false; set {} }
                        public string fixedString { get { return "removed"; } set {} }
                        public Channels fixedEnum { get { return Channels.Automatic; } set {} }
                        public int fixedNumber { get { return -7; } }
                        public float fixedFloat { get { return 3.5f; } }
                    }
                    public enum Channels { Automatic, Mono }
                    public class TextureImporter : AssetImporter { public bool isReadable { get; set; } }
                }
                """;
            const string implementation = """
                namespace UnityEngine { public partial class Object {} }
                namespace UnityEditor
                {
                    public partial class AssetImporter : UnityEngine.Object
                    {
                        public bool bound;
                        internal void CheckImporterState(string binding)
                        { if (!bound) throw new System.NullReferenceException(); }
                    }
                    public partial class TextureImporter : AssetImporter {}
                }
                """;
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            var (referenceImage, referenceErrors) = Compile("UnityEngine.CoreModule", new[] { reference });
            Assert.Empty(referenceErrors);
            Write(unity, "UnityEngine.CoreModule", referenceImage);
            var source = Path.Combine(root, "impl"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Importer.cs"), implementation);
            var (implImage, implErrors) = Compile("UnityEngine.CoreModule", new[] { implementation, StubSupport });
            Assert.Empty(implErrors);
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule", implImage);
            var output = Path.Combine(source, "Generated");
            var report = StubGenerator.Generate(unity, impl, output, source, returns: NoReturns);
            Assert.Empty(report.Errors); Assert.Empty(report.Conflicts);
            var (image, errors) = Compile("UnityEngine.CoreModule", Directory.GetFiles(output, "*.g.cs")
                .Select(File.ReadAllText).Append(implementation).Append(StubSupport));
            Assert.True(errors.Count == 0, string.Join("\n", errors));
            var context = new AssemblyLoadContext("importer-stubs", isCollectible: true);
            try
            {
                var assembly = context.LoadFromStream(new MemoryStream(image));
                var type = assembly.GetType("UnityEditor.TextureImporter")!;
                var instance = Activator.CreateInstance(type)!;
                foreach (var name in new[] { "count", "isReadable" })
                {
                    var property = type.GetProperty(name)!;
                    Assert.IsType<NullReferenceException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(instance)).InnerException);
                    Assert.IsType<NullReferenceException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(instance,
                        property.PropertyType == typeof(bool) ? (object)true : 7)).InnerException);
                }
                Assert.Equal(0, type.BaseType!.GetProperty("staticCount")!.GetValue(null));
                Assert.IsType<NotSupportedException>(Assert.Throws<TargetInvocationException>(() => type.GetProperty("removed")!.GetValue(instance)).InnerException);
                foreach (var name in new[] { "fixedTrue", "fixedFalse", "fixedString", "fixedEnum" })
                {
                    var property = type.GetProperty(name)!;
                    var expected = property.GetValue(instance);
                    object changed = property.PropertyType == typeof(bool) ? !(bool)expected!
                        : property.PropertyType == typeof(string) ? "changed" : Enum.ToObject(property.PropertyType, 77);
                    property.SetValue(instance, changed);
                    Assert.Equal(expected, property.GetValue(instance));
                }
                Assert.Equal(true, type.GetProperty("fixedTrue")!.GetValue(instance));
                Assert.Equal(false, type.GetProperty("fixedFalse")!.GetValue(instance));
                Assert.Equal("removed", type.GetProperty("fixedString")!.GetValue(instance));
                Assert.Equal(0, Convert.ToInt32(type.GetProperty("fixedEnum")!.GetValue(instance)));
                Assert.Equal(-7, type.GetProperty("fixedNumber")!.GetValue(instance));
                Assert.Equal(3.5f, type.GetProperty("fixedFloat")!.GetValue(instance));
                assembly.GetType("Unbound.Runtime.Compat")!.GetField("RemovedEditorSettingsMeasured")!.SetValue(null, false);
                Assert.Equal(false, type.GetProperty("fixedTrue")!.GetValue(instance));
                assembly.GetType("Unbound.Runtime.Compat")!.GetField("RemovedEditorSettingsMeasured")!.SetValue(null, true);
                type.GetField("bound")!.SetValue(instance, true);
                Assert.Equal(0, type.GetProperty("count")!.GetValue(instance));
                Assert.Equal(false, type.GetProperty("isReadable")!.GetValue(instance));
            }
            finally { context.Unload(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NonPartialHandWrittenTypesFailBeforeWritingOutput()
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

            var output = Path.Combine(sourceDir, "Generated", "6000.0");
            var error = StubGenerator.Generate(unity, impl, output, sourceDir);
            Assert.Contains(error.Errors, e => e.Contains("not partial"));
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void TableEntriesAreValidatedBeforeWritingOutput()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            var (coreImage, coreErrors) = Compile("UnityEngine.CoreModule", new[] { """
                namespace UnityEngine
                {
                    public static class Scale
                    {
                        public static float factor => 1;
                        public static float other => 1;
                        public static int Count(int a) => 1;
                    }
                }
                """ });
            Assert.Empty(coreErrors);
            Write(unity, "UnityEngine.CoreModule", coreImage);
            var sourceDir = Path.Combine(root, "impl");
            Directory.CreateDirectory(sourceDir);
            var (implImage, implErrors) = Compile("UnityEngine.CoreModule", new[] { StubSupport });
            Assert.Empty(implErrors);
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule", implImage);

            var output = Path.Combine(sourceDir, "Generated", "6000.0");
            var returns = new Dictionary<string, StubDefault>
            {
                ["UnityEngine.Scale.factor"] = "1f",
                ["UnityEngine.Scale.Count"] = "1",
                ["UnityEngine.Gone.value"] = "2",
            };
            var error = StubGenerator.Generate(unity, impl, output, sourceDir, returns: returns);
            Assert.Contains(error.Errors, e => e.Contains("UnityEngine.Gone.value"));
            Assert.False(Directory.Exists(output));
            returns.Remove("UnityEngine.Gone.value");
            returns["UnityEngine.Scale.factor"] = "1"; // Implicit numeric conversion must not hide a changed reference type.
            error = StubGenerator.Generate(unity, impl, output, sourceDir, returns: returns);
            Assert.Contains(error.Errors, e => e.Contains("invalid expression for float"));
            Assert.False(Directory.Exists(output));
            returns["UnityEngine.Scale.factor"] = "1f";
            returns["UnityEngine.Scale.Count"] = new("1", "value");
            error = StubGenerator.Generate(unity, impl, output, sourceDir, returns: returns);
            Assert.Contains(error.Errors, e => e.Contains("no generated property uses its measured setter"));
            Assert.False(Directory.Exists(output));
            returns["UnityEngine.Scale.Count"] = "1";
            var report = StubGenerator.Generate(unity, impl, output, sourceDir, returns: returns);
            var text = File.ReadAllText(Path.Combine(output, "UnityEngine.CoreModule.g.cs"));
            // Still a stub that warns, now returning the table's value.
            Assert.Contains("NotImplemented(\"UnityEngine.Scale.factor\"); return 1f; }", text);
            Assert.Contains("NotImplemented(\"UnityEngine.Scale.Count\"); return 1; }", text);
            Assert.Contains("NotImplemented(\"UnityEngine.Scale.other\"); return default; }", text);
            Assert.Empty(report.Errors);
            Assert.Empty(report.Conflicts);
            var (_, errors) = Compile("UnityEngine.CoreModule", Directory.GetFiles(output, "*.g.cs").Select(File.ReadAllText).Append(StubSupport));
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CheckedBootstrapStubSignaturesCannotDrift()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "UnityEngine");
            Write(unity, "UnityEngine.CoreModule", Compile("UnityEngine.CoreModule", new[] {
                "namespace UnityEngine { public class Controller { public virtual long Count(int id) => 0; } }"
            }).Image);
            var sourceDir = Path.Combine(root, "impl");
            Directory.CreateDirectory(sourceDir);
            const string hand = "namespace UnityEngine { public partial class Controller { [Unbound.Stub] public virtual int Count(int id) => 0; } }";
            File.WriteAllText(Path.Combine(sourceDir, "Controller.cs"), hand);
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule", Compile("UnityEngine.CoreModule", new[] { hand, StubSupport }).Image);
            var output = Path.Combine(root, "output");
            var report = StubGenerator.Generate(unity, impl, output, sourceDir);
            Assert.Contains(report.Errors, e => e.Contains("checked stub UnityEngine.Controller.Count"));
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MutableDefaultsKeepWritesClampAndRemainPerInstance()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "UnityEngine");
            Write(unity, "UnityEngine.CoreModule", Compile("UnityEngine.CoreModule", new[] { """
                namespace UnityEngine { public class Settings {
                    public float amount { get; set; }
                    public static int budget { get; set; }
                    public static bool ignored { get; set; }
                } }
                """ }).Image);
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule", Compile("UnityEngine.CoreModule", new[] { StubSupport }).Image);
            var output = Path.Combine(root, "arbitrary-output");
            var report = StubGenerator.Generate(unity, impl, output, returns: new Dictionary<string, StubDefault> {
                ["UnityEngine.Settings.amount"] = new("1f", "value"),
                ["UnityEngine.Settings.budget"] = new("8", "global::System.Math.Max(0, value)"),
                ["UnityEngine.Settings.ignored"] = "true",
            });
            Assert.Empty(report.Errors);
            var (image, errors) = Compile("UnityEngine.CoreModule", Directory.GetFiles(output, "*.g.cs").Select(File.ReadAllText).Append(StubSupport));
            Assert.Empty(errors);
            var context = new AssemblyLoadContext("mutable-defaults", isCollectible: true);
            try
            {
                var type = context.LoadFromStream(new MemoryStream(image)).GetType("UnityEngine.Settings")!;
                var first = Activator.CreateInstance(type);
                var second = Activator.CreateInstance(type);
                var amount = type.GetProperty("amount")!;
                Assert.Equal(1f, amount.GetValue(first));
                amount.SetValue(first, -2.5f);
                Assert.Equal(-2.5f, amount.GetValue(first));
                Assert.Equal(1f, amount.GetValue(second));
                var budget = type.GetProperty("budget")!;
                budget.SetValue(null, -4);
                Assert.Equal(0, budget.GetValue(null));
                budget.SetValue(null, 17);
                Assert.Equal(17, budget.GetValue(null));
                var ignored = type.GetProperty("ignored")!;
                ignored.SetValue(null, false);
                Assert.Equal(true, ignored.GetValue(null));
            }
            finally { context.Unload(); }
            var validFile = File.ReadAllText(Path.Combine(output, "UnityEngine.CoreModule.g.cs"));
            report = StubGenerator.Generate(unity, impl, output, returns: new Dictionary<string, StubDefault> {
                ["UnityEngine.Settings.amount"] = new("1f", "false"),
            });
            Assert.Contains(report.Errors, e => e.Contains("invalid setter"));
            Assert.Equal(validFile, File.ReadAllText(Path.Combine(output, "UnityEngine.CoreModule.g.cs")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ReleaseFamilyIsIndependentOfOutputDirectory()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "UnityEngine");
            Write(unity, "UnityEngine.CoreModule", Compile("UnityEngine.CoreModule", new[] { "namespace UnityEngine { public class Empty {} }" }).Image);
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule", Compile("UnityEngine.CoreModule", new[] { StubSupport }).Image);
            Assert.Empty(StubGenerator.Generate(unity, impl, Path.Combine(root, "6000.0"), releaseFamily: "unknown").Errors);
            var report = StubGenerator.Generate(unity, impl, Path.Combine(root, "elsewhere"), releaseFamily: "6000.0");
            Assert.Contains(report.Errors, e => e.Contains("UnityEngine.Scripting.GarbageCollector.GCMode"));
            Assert.False(Directory.Exists(Path.Combine(root, "elsewhere")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FixedRemovedGetterExceptionsFollowTheSelectedRelease()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "UnityEngine");
            var impl = Write(Path.Combine(root, "nostubs"), "UnityEngine.CoreModule",
                Compile("UnityEngine.CoreModule", new[] { StubSupport }).Image);
            var output = Path.Combine(root, "output");
            foreach (var message in new[] { "removed\n\"getter\"", "replacement release message" })
            {
                var source = """
                    namespace UnityEngine { public class Removed {
                        public object old => throw new System.NotSupportedException(MESSAGE);
                        public object ordinary => null;
                        public object conditional => System.DateTime.Now.Ticks > 0 ? null : throw new System.NotSupportedException("conditional");
                        public object computed => throw new System.NotSupportedException(System.DateTime.Now.ToString());
                    } }
                    """.Replace("MESSAGE", SymbolDisplay.FormatLiteral(message, true));
                var (reference, errors) = Compile("UnityEngine.CoreModule", new[] { source });
                Assert.Empty(errors);
                Write(unity, "UnityEngine.CoreModule", reference);
                var report = StubGenerator.Generate(unity, impl, output, returns: NoReturns);
                Assert.Empty(report.Errors);
                var (image, generatedErrors) = Compile("UnityEngine.CoreModule",
                    Directory.GetFiles(output, "*.g.cs").Select(File.ReadAllText).Append(StubSupport));
                Assert.Empty(generatedErrors);
                var context = new AssemblyLoadContext("removed-getters", isCollectible: true);
                try
                {
                    var assembly = context.LoadFromStream(new MemoryStream(image));
                    var type = assembly.GetType("UnityEngine.Removed")!;
                    var instance = Activator.CreateInstance(type);
                    var calls = assembly.GetType("Unbound.Runtime.Stubs")!.GetField("Calls")!;
                    var before = calls.GetValue(null);
                    var error = Assert.Throws<TargetInvocationException>(() => type.GetProperty("old")!.GetValue(instance));
                    Assert.Equal(message, Assert.IsType<NotSupportedException>(error.InnerException).Message);
                    Assert.Equal(before, calls.GetValue(null));
                    foreach (var name in new[] { "ordinary", "conditional", "computed" })
                        Assert.Null(type.GetProperty(name)!.GetValue(instance));
                }
                finally { context.Unload(); }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void EveryReleaseTableEntryIsAnExpression()
    {
        foreach (var (id, value) in StubDefaults.For("6000.0"))
        {
            Assert.StartsWith("UnityEngine.", id);
            Assert.Empty(SyntaxFactory.ParseExpression(value.Value).GetDiagnostics());
        }
        Assert.Empty(StubDefaults.For("no-such-family"));
    }
}
