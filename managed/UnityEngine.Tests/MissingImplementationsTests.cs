using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unbound.Host;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A plugin built against an older version of a library, whose classes lack implementations of
    /// members the project's version has, loads with those classes listed and found; using such a
    /// class throws TypeLoadException (Unity 6000.0.71f1, tests/projects/gameinterop plugins.*).
    /// Complete classes, and classes that implement the members, are not changed.
    /// </summary>
    public class MissingImplementationsTests
    {
        const string CoreOld = @"
namespace Core
{
    public abstract class Shape { public abstract float Area(); public virtual string Describe() => GetType().Name; }
    public interface IMeasured { int Measure(); }
    public abstract class Plug<T> { public abstract T Apply(T value); }
}";

        const string CoreNew = @"
namespace Core
{
    public abstract class Shape { public abstract float Area(float scale); public virtual string Describe() => GetType().Name; }
    public interface IMeasured { int Measure(int scale); }
    public abstract class Plug<T> { public abstract T Apply(T value, float time); }
    public abstract class Figure : Shape { public override float Area(float scale) => scale; }
}";

        const string Extras = @"
namespace Extras
{
    public sealed class Circle : Core.Shape { public override float Area() => 3f; public static int Count() => 1; }
    public class Ruler : Core.IMeasured { public int Measure() => 30; }
    public class FloatPlug : Core.Plug<float> { public override float Apply(float value) => value; }
    public class Square { public float Area() => 4f; }
}";

        // Built against the new version: complete.
        const string Later = @"
namespace Later
{
    public sealed class Triangle : Core.Shape { public override float Area(float scale) => 0.5f * scale; }
    public sealed class Ring : Core.Figure { }
    public class Meter : Core.IMeasured { int Core.IMeasured.Measure(int scale) => scale; }
}";

        static byte[] Build(string name, string source, params byte[][] references)
        {
            var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .Concat(references.Select(r => MetadataReference.CreateFromImage(r)));
            var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) }, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var result = compilation.Emit(image);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            return image.ToArray();
        }

        static Assembly Load(byte[] image, byte[] core)
        {
            var context = new AssemblyLoadContext("missing implementations " + Guid.NewGuid(), isCollectible: true);
            Assembly loadedCore = null;
            context.Resolving += (c, name) => name.Name == "Core" ? loadedCore ??= c.LoadFromStream(new MemoryStream(core)) : null;
            return context.LoadFromStream(new MemoryStream(image));
        }

        static Exception Thrown(Action action)
        {
            try { action(); }
            catch (TargetInvocationException e) { return e.InnerException; }
            catch (Exception e) { return e; }
            return null;
        }

        [Fact]
        public void AClassMissingImplementationsLoadsAndFailsAtItsFirstUse()
        {
            var oldCore = Build("Core", CoreOld);
            var newCore = Build("Core", CoreNew);
            var extras = Build("Extras", Extras, oldCore);
            var later = Build("Later", Later, newCore);
            var dir = Path.Combine(Path.GetTempPath(), "unbound-missing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "Core.dll"), newCore);
                AccessChecks.SetSearchDirectories(new[] { dir }, new Dictionary<string, string> { ["Core"] = Path.Combine(dir, "Core.dll") });

                // As built, .NET refuses the incomplete classes when the plugin's types are listed.
                Assert.Throws<ReflectionTypeLoadException>(() => Load(extras, newCore).GetTypes());

                var repaired = Load(MissingImplementations.Repair(extras), newCore);
                Assert.Equal(new[] { "Extras.Circle", "Extras.FloatPlug", "Extras.Ruler", "Extras.Square" },
                    repaired.GetTypes().Select(t => t.FullName).Where(n => n.StartsWith("Extras.")).OrderBy(n => n));
                foreach (var name in new[] { "Extras.Circle", "Extras.Ruler", "Extras.FloatPlug" })
                {
                    var type = repaired.GetType(name, true);
                    var error = Assert.IsType<TypeLoadException>(Thrown(() => Activator.CreateInstance(type)));
                    Assert.Contains($"in type '{name}' from assembly 'Extras", error.Message);
                }
                Assert.Contains("Method 'Area' in type 'Extras.Circle'",
                    Assert.IsType<TypeLoadException>(Thrown(() => repaired.GetType("Extras.Circle").GetMethod("Count").Invoke(null, null))).Message);
                // Control: a complete class works as before.
                var square = Activator.CreateInstance(repaired.GetType("Extras.Square"));
                Assert.Equal(4f, square.GetType().GetMethod("Area").Invoke(square, null));

                // A plugin with nothing missing is returned as it is.
                Assert.Same(later, MissingImplementations.Repair(later));
                var complete = Load(later, newCore);
                foreach (var (name, area) in new[] { ("Later.Triangle", 1f), ("Later.Ring", 2f) })
                {
                    var shape = Activator.CreateInstance(complete.GetType(name, true));
                    Assert.Equal(area, shape.GetType().GetMethod("Area", new[] { typeof(float) }).Invoke(shape, new object[] { 2f }));
                }
            }
            finally
            {
                AccessChecks.SetSearchDirectories(Array.Empty<string>(), new Dictionary<string, string>());
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
