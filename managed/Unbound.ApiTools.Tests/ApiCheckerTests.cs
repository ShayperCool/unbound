using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class ApiCheckerTests
{
    const string Unity = """
        namespace UnityEngine
        {
            public static class SystemInfo { public static int supportsTextureWrapMirrorOnce => 0; }
            public class Camera { public enum MonoOrStereoscopicEye { Left, Right, Mono } }
            public enum Mode { A = 1, B = 2 }
            public struct Pair { public int a; public float b; }
            public class Thing
            {
                public int Count { get; set; }
                public void Do(int x) {}
                public Thing() {}
            }
        }
        namespace UnityEngine.Rendering { public struct ReflectionProbeBlendInfo { public float weight; } }
        """;

    const string HandWritten = """
        namespace UnityEngine
        {
            public static partial class SystemInfo { public static bool supportsTextureWrapMirrorOnce => false; }
            public partial class Camera { }
            public enum MonoOrStereoscopicEye { Left, Right, Mono }
            public enum Mode { A = 1, B = 3, C = 4 }
            public partial struct Pair { public float b; public int a; }
            internal partial class Thing
            {
                public int Count { get; }
                public void Do(ref int x) {}
                internal void Do(string helper) {}
            }
            public struct ReflectionProbeBlendInfo { public float weight; }
            public class Invented {}
        }
        namespace Unbound { public class OwnPublicHelper {} }
        """;

    [Fact]
    public void ReportsHandWrittenApiThatDiffersFromUnity()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", Unity);
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", HandWritten);

            var report = ApiChecker.Check(unity, impl);
            string Messages(string category, string type) => string.Join("\n",
                report.Errors.Where(e => e.Category == category && e.Type == type).Select(e => e.Message));

            Assert.Contains("static bool supportsTextureWrapMirrorOnce { get; } matches none of Unity's: static int supportsTextureWrapMirrorOnce { get; }",
                Messages("signature", "UnityEngine.SystemInfo"));
            Assert.Contains("Unity's is UnityEngine.Camera+MonoOrStereoscopicEye", Messages("type", "UnityEngine.MonoOrStereoscopicEye"));
            Assert.Contains("Unity's is UnityEngine.Rendering.ReflectionProbeBlendInfo", Messages("type", "UnityEngine.ReflectionProbeBlendInfo"));
            Assert.Contains("does not exist in Unity", Messages("type", "UnityEngine.Invented"));
            Assert.DoesNotContain(report.Errors, e => e.Type.StartsWith("Unbound", StringComparison.Ordinal));

            var mode = Messages("enum", "UnityEngine.Mode");
            Assert.Contains("B is 3, Unity's is 2", mode);
            Assert.Contains("has C = 4, which Unity's does not", mode);
            Assert.Contains("public fields are (float b, int a), Unity's are (int a, float b)", Messages("layout", "UnityEngine.Pair"));

            Assert.Contains("is internal, Unity's is public", Messages("access", "UnityEngine.Thing"));
            var thing = Messages("signature", "UnityEngine.Thing");
            Assert.Contains("int Count { get; } has no setter, Unity's has one", thing);
            Assert.Contains("void Do(ref int) matches none of Unity's: void Do(int)", thing);
            Assert.DoesNotContain("Do(string)", thing); // an internal helper overload is Unbound's own
            Assert.Contains(report.Errors, e => e.Category == "accessors" && e.Message.Contains("setter is absent"));
            Assert.Contains(report.Errors, e => e.Category == "enum" && e.Message.Contains("B is 3"));
            Assert.Equal(11, report.Errors.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void ComparesDeclarationMetadataAtEveryTarget()
    {
        const string declarations = """
            using System;
            namespace UnityEngine {
                [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
                public class Mark : Attribute { public Mark(Type t, params int[] values) {} public string Text; }
                [Mark(typeof(System.Collections.Generic.List<>), 1, 2, Text = "expected")]
                [Serializable] public class Thing<T> where T : class, new() {
                    [NonSerialized] public int transient;
                    public readonly int value;
                    public const float Constant = 3.5f;
                    [return: Mark(typeof(int))] public virtual int Do([Mark(typeof(string))] int v = 2) => v;
                    public void Many(params int[] values) {}
                    public void Interop([System.Runtime.InteropServices.In] int value) {}
                    public event Action Changed;
                    public int Prop { [Mark(typeof(int))] get; protected set; }
                }
            }
            """;
        var changed = declarations.Replace("Inherited = true", "Inherited = false")
            .Replace("AllowMultiple = true", "AllowMultiple = false")
            .Replace("Text = \"expected\"", "Text = \"wrong\"")
            .Replace("[Serializable]", "")
            .Replace("where T : class, new()", "where T : class")
            .Replace("[NonSerialized]", "")
            .Replace("[System.Runtime.InteropServices.In]", "")
            .Replace("readonly int", "int")
            .Replace("3.5f", "4.5f")
            .Replace("[return: Mark(typeof(int))]", "")
            .Replace("[Mark(typeof(string))]", "")
            .Replace("virtual int Do", "int Do")
            .Replace("v = 2", "v = 3")
            .Replace("Many(params int[]", "Many(int[]")
            .Replace("[Mark(typeof(int))] get", "get")
            .Replace("protected set", "set");
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", declarations);
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", changed);
            var report = ApiChecker.Check(unity, impl);
            foreach (var category in new[] { "attributes", "parameters", "constraints", "modifiers", "constants", "accessors" })
                Assert.Contains(report.Errors, e => e.Category == category);
            Assert.Contains(report.Errors, e => e.Message.Contains("Inherited=System.Boolean:False"));
            Assert.Contains(report.Errors, e => e.Message.Contains("return attributes"));
            Assert.Contains(report.Errors, e => e.Message.Contains("Interop") && e.Message.Contains("in/out"));
            Assert.Contains(report.Errors, e => e.Message.Contains("transient") && e.Category == "attributes");
            Assert.Contains(report.Errors, e => e.Message.Contains("get_Prop") && e.Category == "attributes");
            var same = ApiChecker.Check(unity, TestAssemblies.Compile(Path.Combine(root, "same"), "UnityEngine.CoreModule", declarations));
            Assert.Empty(same.Errors);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExceptionsAreExactReleaseScopedAndNeedReasons()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var path = Path.Combine(root, "exceptions.json");
            var entry = new ApiCheckException { Release = "6000.0.71f1", Category = "attributes", Type = "UnityEngine.Thing", Message = "mismatch", Reason = "Tracked declaration migration for Thing" };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { entry }));
            ApiCheckReport Report(string message = "mismatch") => new() { Errors = { new() { Category = "attributes", Type = "UnityEngine.Thing", Message = message } } };
            var report = Report();
            ApiCheckAllowList.Apply(report, path, entry.Release);
            Assert.Empty(report.Errors);
            Assert.Single(report.Allowed);
            report = Report();
            ApiCheckAllowList.Apply(report, path, "other-release");
            Assert.Single(report.Errors);
            report = Report("new mismatch");
            ApiCheckAllowList.Apply(report, path, entry.Release);
            Assert.Equal(2, report.Errors.Count);
            entry.Reason = " ";
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { entry }));
            Assert.Throws<InvalidDataException>(() => ApiCheckAllowList.Apply(Report(), path, entry.Release));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("new int[0]", "new string[0]")]
    [InlineData("(int[])null", "(string[])null")]
    public void AttributeArraysRetainTheirTypesAndConstructorOverloads(string first, string second)
    {
        const string declarations = """
            using System;
            namespace UnityEngine {
                public class Mark : Attribute { public Mark(int[] values) {} public Mark(string[] values) {} }
                [Mark(ARRAY)] public class Thing {}
            }
            """;
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", declarations.Replace("ARRAY", first));
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", declarations.Replace("ARRAY", second));
            Assert.Contains(ApiChecker.Check(unity, impl).Errors, e => e.Category == "attributes" && e.Type == "UnityEngine.Thing");
            impl = TestAssemblies.Compile(Path.Combine(root, "same"), "UnityEngine.CoreModule", declarations.Replace("ARRAY", first));
            Assert.Empty(ApiChecker.Check(unity, impl).Errors);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SharedPrivateFieldsKeepTheirSerializationAnnotations()
    {
        const string declarations = """
            using System;
            namespace UnityEngine {
                public class SerializeField : Attribute {}
                public class SerializeReference : Attribute {}
                namespace Serialization { public class FormerlySerializedAsAttribute : Attribute { public FormerlySerializedAsAttribute(string name) {} } }
                public class Thing {
                    [SerializeField] private int value;
                    [SerializeReference] private object reference;
                    [Serialization.FormerlySerializedAs("old")] private int renamed;
                }
            }
            """;
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", declarations);
            var changed = declarations.Replace("[SerializeField]", "").Replace("[SerializeReference]", "")
                .Replace("[Serialization.FormerlySerializedAs(\"old\")]", "");
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", changed);
            var errors = ApiChecker.Check(unity, impl).Errors;
            foreach (var name in new[] { "value", "reference", "renamed" })
                Assert.Contains(errors, e => e.Category == "attributes" && e.Message.StartsWith("field " + name + " attributes"));
            Assert.Equal(3, errors.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("this int", "int")]
    [InlineData("int", "this int")]
    public void ExtensionMethodStatusIsCheckedInBothDirections(string first, string second)
    {
        const string declarations = "namespace UnityEngine { public static class Extensions { public static void Foo(PARAM value) {} } }";
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", declarations.Replace("PARAM", first));
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", declarations.Replace("PARAM", second));
            Assert.Contains(ApiChecker.Check(unity, impl).Errors, e => e.Category == "modifiers" && e.Message.Contains("extension/vararg"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitLookupArgumentsAreCheckedAgainstReferenceUsage()
    {
        const string declarations = """
            using System;
            namespace UnityEngine {
                public class Inherited : Attribute {}
                [AttributeUsage(AttributeTargets.Class, Inherited = false)] public class Direct : Attribute {}
                [AttributeUsage(AttributeTargets.Field)] public class FieldOnly : Attribute {}
            }
            """;
        const string client = """
            namespace Unbound {
                using System;
                using System.Reflection;
                public static class Client {
                    public static bool Wrong(Type t) => t.IsDefined(typeof(UnityEngine.Inherited), inherit: false);
                    public static object WrongExtension(MemberInfo m) => m.GetCustomAttribute<UnityEngine.Inherited>(false);
                    public static bool WrongDirect(Type t) => t.IsDefined(typeof(UnityEngine.Direct), true);
                    public static bool Computed(Type t, bool inherit) => t.IsDefined(typeof(UnityEngine.Inherited), inherit);
                    public static bool Generic<T>(MemberInfo m) where T : Attribute => m.IsDefined(typeof(T), false);
                    public static object All(Type t) => t.GetCustomAttributes(false);
                    public static bool Good(Type t) => t.IsDefined(typeof(UnityEngine.Inherited), true);
                    public static bool Direct(Type t) => t.IsDefined(typeof(UnityEngine.Direct), false);
                    public static bool Field(FieldInfo f) => f.IsDefined(typeof(UnityEngine.Inherited), false);
                    public static object FieldExtension(FieldInfo f) => f.GetCustomAttribute<UnityEngine.Inherited>(false);
                    public static bool FieldTarget(MemberInfo f) => f.IsDefined(typeof(UnityEngine.FieldOnly), false);
                    public static object Default(MemberInfo m) => m.GetCustomAttribute<UnityEngine.Inherited>();
                }
            }
            """;
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", declarations);
            var source = Path.Combine(root, "source", "UnityEngine");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Client.cs"), declarations + client);
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", declarations + client);
            File.WriteAllText(Path.Combine(source, "Conditional.cs"), "#if NEVER_SELECTED\nnamespace Unbound { static class Conditional { static bool Hidden(System.Type t) => t.IsDefined(typeof(UnityEngine.Inherited), false); } }\n#endif\n");
            var errors = ApiChecker.Check(unity, impl, implSourceDir: source).Errors;
            Assert.Equal(7, errors.Count);
            Assert.Single(errors.Where(e => e.Type == "<conditional lookup>"));
            Assert.All(errors, e => Assert.Equal("lookup", e.Category));
            foreach (var name in new[] { "Wrong", "WrongExtension", "WrongDirect", "Computed", "Generic", "All" })
                Assert.Contains(errors, e => e.Message.StartsWith("UnityEngine/Client.cs " + name + ":"));
            Assert.Contains(errors, e => e.Message.Contains("inherit is computed"));
            Assert.Contains(errors, e => e.Type == "T" && e.Message.Contains("not statically known"));
        }
        finally { Directory.Delete(root, true); }
    }

}
