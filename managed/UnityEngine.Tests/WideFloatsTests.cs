using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Mono.Cecil.Cil;
using Unbound.Host;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Game code's float arithmetic under the rewrite that evaluates it as the Editor's Mono does
    /// (tests/projects/floatrounding): float32 values on the stack are doubles, and IL stores into
    /// float32 locations round. Operands a = 16777216, b = 1, c = -16777216 give 1 when a + b keeps
    /// its exact value through the construct under test and 0 when it is rounded there.
    /// </summary>
    public class WideFloatsTests
    {
        const string Source = @"
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace Unity.Burst { public sealed class BurstCompileAttribute : Attribute { } }
public struct Pair { public float X; }
public static class Box<T> { public static T Value; }
public static class Probe
{
    public static bool Flag = true;
    public static bool Off;
    public static int Kind = 1;
    public static float Static;
    public static volatile float Volatile;
    public static readonly float[] Array = new float[1];
    static object s_Object;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Echo(float x) => x;
    [MethodImpl(MethodImplOptions.NoInlining)] static float First(float x, float y) => x;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Add(float x, float y) => x + y;
    [MethodImpl(MethodImplOptions.NoInlining)] static void Call() { }
    public static float Chain(float a, float b, float c) => a + b + c;
    public static float Divide(float a, float b, float c) => a / b - c;
    public static float LocalTwice(float a, float b, float c) { float t = a + b; return (t + c) + (t - t); }
    public static float Argument(float a, float b, float c) { a = a + b; return a + c; }
    public static float StaticField(float a, float b, float c) { Static = a + b; return Static + c; }
    public static float VolatileField(float a, float b, float c) { Volatile = a + b; return Volatile + c; }
    public static float ArrayElement(float a, float b, float c) { Array[0] = a + b; return Array[0] + c; }
    public static float RefLocal(float a, float b, float c) { ref float r = ref Array[0]; r = a + b; return r + c; }
    public static float StackSpan(float a, float b, float c) { Span<float> s = stackalloc float[1]; s[0] = a + b; return s[0] + c; }
    public static float StructField(float a, float b, float c) { var p = new Pair { X = a + b }; return p.X + c; }
    public static float BoxedBack(float a, float b, float c) { object o = a + b; return (float)o + c; }
    public static float BoxedField(float a, float b, float c) { s_Object = a + b; return (float)s_Object + c; }
    public static float Closure(float a, float b, float c) { float t = a + b; Func<float> f = () => t; return t + c + f() * 0f; }
    public static float CallArgument(float a, float b, float c) => Echo(a + b) + c;
    public static float FirstArgument(float a, float b, float c) => First(a + b, 0f) + c;
    public static float CallReturn(float a, float b, float c) => Add(a, b) + c;
    public static float AcrossCall(float a, float b, float c) { float t = a + b; Call(); return t + c; }
    public static float Ternary(float a, float b, float c) { float t = Flag ? a + b : a - b; return t + c; }
    public static float TernaryElse(float a, float b, float c) { float t = Off ? a - b : a + b; return t + c; }
    public static float BoxedTernary(float a, float b, float c) { object o = Off ? a - b : a + b; Kind++; Kind--; return (float)o + c; }
    public static float Switch(float a, float b, float c) { float t = Kind switch { 0 => a - b, 1 => a + b, _ => a }; return t + c; }
    public static float TryFinally(float a, float b, float c) { float r; try { r = a + b; } finally { Kind++; Kind--; } return r + c; }
    public static float GenericField(float a, float b, float c) { Box<float>.Value = a + b; return Box<float>.Value + c; }
    public static float ListItems(float a, float b, float c) { var l = new List<float> { a, b }; return l[0] + l[1] + c; }
    public static float Nullable(float a, float b, float c) { float? n = a + b; return n.Value + c; }
    public static float Cast(float a, float b, float c) => (float)(a + b) + c;
    public static double ToDouble(float a, float b, float c) => (double)(a + b) + c;
    public static int ToInt(float a, float b, float c) => (int)(a + b) + (int)c;
    public static bool Equal(float a, float b, float c) => a + b == a;
    public static float IntToFloat(float a, float b, float c) { int i = 16777217; return i + c; }
    public static float Overflow(float a, float b, float c) => a * a * c;
}
public static class Plain { public static int Sum(int x, int y) => x + y; }
namespace Unity.Jobs { public interface IJob { void Execute(); } }
[Unity.Burst.BurstCompile] public struct Job : Unity.Jobs.IJob
{
    public float A, B, C, Result;
    public void Execute() { Result = A + B + C; }
    public static float Chain(float a, float b, float c) => a + b + c;
    public struct Inner { public static float Chain(float a, float b, float c) => a + b + c; }
}
[Unity.Burst.BurstCompile] public class BurstOwner
{
    public static float Update(float a, float b, float c) => a + b + c;
    [Unity.Burst.BurstCompile] public static float Compiled(float a, float b, float c) => a + b + c;
}
";

        static readonly MetadataReference[] s_References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();

        static readonly MetadataReference[] s_EngineReferences = s_References.Append(MetadataReference.CreateFromFile(typeof(Vector3).Assembly.Location)).ToArray();

        /// <summary>The source compiled as game code; <paramref name="engine"/> references the engine.</summary>
        internal static CSharpCompilation Compilation(string source, OptimizationLevel optimization, string name = "WideFloatsProbe", bool engine = false) =>
            CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source, path: "Probe.cs", encoding: System.Text.Encoding.UTF8) },
                engine ? s_EngineReferences : s_References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimization, allowUnsafe: true));

        internal static (byte[] Image, byte[] Pdb) Emit(CSharpCompilation compilation)
        {
            using var image = new MemoryStream();
            using var pdb = new MemoryStream();
            var result = compilation.Emit(image, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            return (image.ToArray(), pdb.ToArray());
        }

        /// <summary>Loads a game image in its own context, which finds the generated converted math
        /// as the game's load context does.</summary>
        internal static Assembly Load(byte[] image)
        {
            var context = new AssemblyLoadContext("wide floats " + Guid.NewGuid(), isCollectible: true);
            context.Resolving += (c, name) => name.Name == WideFloats.EngineMathAssembly ? c.LoadFromStream(new MemoryStream(WideFloats.EngineMathImage())) : null;
            return context.LoadFromStream(new MemoryStream(image));
        }

        static object Run(Assembly assembly, string type, string method, float a = 16777216f, float b = 1f, float c = -16777216f) =>
            assembly.GetType(type)!.GetMethod(method)!.Invoke(null, new object[] { a, b, c })!;

        static Assembly Widened(OptimizationLevel optimization) => Load(WideFloats.Widen(Emit(Compilation(Source, optimization)).Image));

        public static IEnumerable<object[]> Modes => new[] { new object[] { OptimizationLevel.Release }, new object[] { OptimizationLevel.Debug } };

        [Theory]
        [MemberData(nameof(Modes))]
        public void ArithmeticKeepsDoubleIntermediates(OptimizationLevel optimization)
        {
            var probe = Widened(optimization);
            Assert.Equal(1f, Run(probe, "Probe", "Chain"));
            Assert.Equal(1.0, Run(probe, "Probe", "ToDouble"));
            Assert.Equal(1, Run(probe, "Probe", "ToInt"));
            Assert.Equal(false, Run(probe, "Probe", "Equal"));
            Assert.Equal(1f, Run(probe, "Probe", "ListItems"));
            // 1e30 * 1e30 overflows float32, not the double intermediate.
            Assert.Equal(1e30f, Run(probe, "Probe", "Overflow", 1e30f, 0f, 1e-30f));
            // 20 / 37 - 2: one float step from the float32 evaluation (BFBACF92).
            Assert.Equal(0xBFBACF91u, BitConverter.SingleToUInt32Bits((float)Run(probe, "Probe", "Divide", 20f, 37f, 2f)));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void StoresIntoFloatLocationsRound(OptimizationLevel optimization)
        {
            var probe = Widened(optimization);
            foreach (var control in new[] { "LocalTwice", "Argument", "StaticField", "VolatileField", "ArrayElement", "RefLocal",
                         "StackSpan", "StructField", "BoxedField", "Closure", "CallArgument", "FirstArgument", "CallReturn",
                         "Switch", "TryFinally", "GenericField", "Nullable", "Cast", "IntToFloat" })
                Assert.True(0f.Equals(Run(probe, "Probe", control)), control);
        }

        [Fact]
        public void BoxingStraightBackKeepsTheValue()
        {
            // Release IL boxes and unboxes in a row; Debug stores the box in a local first.
            Assert.Equal(1f, Run(Widened(OptimizationLevel.Release), "Probe", "BoxedBack"));
            Assert.Equal(0f, Run(Widened(OptimizationLevel.Debug), "Probe", "BoxedBack"));
        }

        [Fact]
        public void ValuesOnTheStackAcrossCallsStayWide()
        {
            // Release keeps t on the evaluation stack across the call; Debug stores it in a local.
            Assert.Equal(1f, Run(Widened(OptimizationLevel.Release), "Probe", "AcrossCall"));
            Assert.Equal(0f, Run(Widened(OptimizationLevel.Debug), "Probe", "AcrossCall"));
        }

        [Fact]
        public void MergedBranchValuesRoundWhereTheyAreStored()
        {
            // Debug stores the conditional's value in a local: the first branch falls through to the
            // store, the second jumps to it, and both round there. Release keeps it on the stack.
            foreach (var control in new[] { "Ternary", "TernaryElse" })
            {
                Assert.True(0f.Equals(Run(Widened(OptimizationLevel.Debug), "Probe", control)), control);
                Assert.True(1f.Equals(Run(Widened(OptimizationLevel.Release), "Probe", control)), control);
            }
            // A box at the join (not unboxed straight away) rounds on both paths.
            foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
                Assert.True(0f.Equals(Run(Widened(optimization), "Probe", "BoxedTernary")), optimization.ToString());
        }

        [Fact]
        public void BurstCompiledCodeKeepsFloatArithmetic()
        {
            var probe = Widened(OptimizationLevel.Release);
            // What Burst compiles: a [BurstCompile] job's Execute and [BurstCompile] methods.
            var job = Activator.CreateInstance(probe.GetType("Job")!)!;
            foreach (var (field, value) in new[] { ("A", 16777216f), ("B", 1f), ("C", -16777216f) })
                job.GetType().GetField(field)!.SetValue(job, value);
            job.GetType().GetMethod("Execute")!.Invoke(job, null);
            Assert.Equal(0f, job.GetType().GetField("Result")!.GetValue(job));
            Assert.Equal(0f, Run(probe, "BurstOwner", "Compiled"));
            // The rest of those types is script code.
            Assert.Equal(1f, Run(probe, "Job", "Chain"));
            Assert.Equal(1f, Run(probe, "Job+Inner", "Chain"));
            Assert.Equal(1f, Run(probe, "BurstOwner", "Update"));
        }

        [Fact]
        public void WithoutFloatsTheImageStaysAsItIs()
        {
            var image = Emit(Compilation("public static class Plain { public static int Sum(int x, int y) => x + y; public static double Half(double x) => x / 2; }", OptimizationLevel.Release)).Image;
            Assert.Same(image, WideFloats.Widen(image));
        }

        [Fact]
        public void UnwidenedRunsRoundEveryOperation()
        {
            // The control: the same IL without the rewrite evaluates in float32 on this runtime.
            var probe = Load(Emit(Compilation(Source, OptimizationLevel.Release)).Image);
            Assert.Equal(0f, Run(probe, "Probe", "Chain"));
            Assert.Equal(true, Run(probe, "Probe", "Equal"));
        }

        [Fact]
        public void TailCallsStayTailCalls()
        {
            foreach (var whole in new[] { false, true })
            {
                using var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(new Mono.Cecil.AssemblyNameDefinition("Tail", new Version(1, 0)), "Tail", Mono.Cecil.ModuleKind.Dll);
                var module = assembly.MainModule;
                var type = new Mono.Cecil.TypeDefinition("", "Tail", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
                module.Types.Add(type);
                // Callee(x) => x + x; Caller(x) => tail. Callee(x * x / x).
                var callee = new Mono.Cecil.MethodDefinition("Callee", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Single);
                callee.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Single));
                var il = callee.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ret);
                type.Methods.Add(callee);
                var caller = new Mono.Cecil.MethodDefinition("Caller", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Single);
                caller.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Single));
                il = caller.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Mul);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Div);
                il.Emit(OpCodes.Tail);
                il.Emit(OpCodes.Call, callee);
                il.Emit(OpCodes.Ret);
                type.Methods.Add(caller);
                Assert.True(WideFloats.WidenBody(caller, whole));
                Assert.Equal(new[] { Code.Ldarg_0, Code.Conv_R8, Code.Ldarg_0, Code.Conv_R8, Code.Mul, Code.Ldarg_0, Code.Conv_R8, Code.Div, Code.Tail, Code.Call, Code.Ret },
                    caller.Body.Instructions.Select(i => i.OpCode.Code));
                // One operation rounded straight away is the same in float32: left alone, unless whole.
                Assert.Equal(whole, WideFloats.WidenBody(callee, whole));
                using var output = new MemoryStream();
                assembly.Write(output);
                // 1e30 * 1e30 stays finite in double: (1e30 * 1e30 / 1e30) + the same.
                Assert.Equal(2e30f, Load(output.ToArray()).GetType("Tail")!.GetMethod("Caller")!.Invoke(null, new object[] { 1e30f }));
            }
        }

        [Fact]
        public void SingleOperationsRoundedAtOnceStayAsTheyAre()
        {
            const string source = @"
public struct V { public float X, Y; public V(float x, float y) { X = x; Y = y; }
  public static V operator +(V a, V b) => new V(a.X + b.X, a.Y + b.Y);
  public static float Dot(V a, V b) => a.X * b.X + a.Y * b.Y; }
public static class S { public static float Scale(float a, float t) { float r = a * t; return r; } public static bool Less(float a, float b) => a * b < a; }";
            var image = Emit(Compilation(source, OptimizationLevel.Release)).Image;
            using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(image));
            var methods = assembly.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).ToDictionary(m => m.DeclaringType.Name + "." + m.Name);
            Assert.False(WideFloats.WidenBody(methods["V.op_Addition"]));
            Assert.False(WideFloats.WidenBody(methods["S.Scale"]));
            Assert.False(WideFloats.WidenBody(methods["V..ctor"]));
            // A sum of products, and a product that is compared, are evaluated in double.
            Assert.True(WideFloats.WidenBody(methods["V.Dot"]));
            Assert.Equal(4, methods["V.Dot"].Body.Instructions.Count(i => i.OpCode.Code == Code.Conv_R8));
            Assert.True(WideFloats.WidenBody(methods["S.Less"]));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void WideningWholeMethodsGivesTheSameResults(OptimizationLevel optimization)
        {
            var image = Emit(Compilation(Source, optimization)).Image;
            var expressions = Load(WideFloats.Widen(image));
            var whole = Load(WideFloats.Widen(image, whole: true));
            foreach (var method in expressions.GetType("Probe")!.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetParameters().Length == 3))
                Assert.True(Equals(Run(expressions, "Probe", method.Name), Run(whole, "Probe", method.Name)), method.Name);
        }

        [Fact]
        public void OnlyTheMeasuredReleaseEvaluatesWide()
        {
            var release = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Assert.True(Unbound.Runtime.Compat.ScriptFloatsWide);
                ProjectInfo.UnityVersion = "6000.0.72f1";
                Assert.False(Unbound.Runtime.Compat.ScriptFloatsWide);
            }
            finally
            {
                ProjectInfo.UnityVersion = release;
            }
        }

        const string Edited = @"
public static class Game
{
    public static int Count(int a, int b) => a - b;
    public static float Speed(float a, float b) => a + b;
    public static string Name() => ""game"";
    public static class Inner { public static float Mix(float a, float b) => b + a; }
}";

        /// <summary>Whether hot-reload edits of <see cref="Edited"/> (one delta) need the conversions (a restart).</summary>
        static bool EditNeedsWidening(params (string Before, string After)[] edits) => EditNeedsWidening(Edited, false, edits);

        /// <summary>Whether hot-reload edits of <paramref name="edited"/> (one delta) need the conversions.</summary>
        internal static bool EditNeedsWidening(string edited, bool engine, params (string Before, string After)[] edits)
        {
            var original = Compilation(edited, OptimizationLevel.Debug, "EditedGame", engine);
            var (image, pdb) = Emit(original);
            var loaded = WideFloats.Widen(image);
            var session = new Unbound.Compiler.EditAndContinue();
            session.Begin("EditedGame", original, loaded, pdb);
            var source = edited;
            foreach (var (before, after) in edits) source = source.Replace(before, after);
            var current = Compilation(source, OptimizationLevel.Debug, "EditedGame", engine);
            var delta = session.Emit("EditedGame", current);
            Assert.NotNull(delta);
            return WideFloats.DeltaNeedsWidening(loaded, new[] { delta!.Metadata }, delta.IL, () => Emit(current).Image);
        }

        [Fact]
        public void HotReloadRestartsOnlyForEditsThatNeedConversions()
        {
            // A float edit whose single operation is rounded straight away is patched in place.
            Assert.False(EditNeedsWidening(("a + b", "a * b")));
            // One that chains operations, or compares a result, needs the conversions.
            Assert.True(EditNeedsWidening(("a + b", "a * b + a")));
            Assert.True(EditNeedsWidening(("a + b", "a + b > a ? 1f : 0f")));
            Assert.False(EditNeedsWidening(("a - b", "a * b")));
            Assert.False(EditNeedsWidening(("\"game\"", "\"edited\"")));
            // Several methods in one delta: each body is matched to its own method (the integer
            // method comes first, so a body matched to its neighbour would answer wrongly).
            Assert.True(EditNeedsWidening(("a - b", "a * b"), ("a + b", "a * b + a")));
            Assert.False(EditNeedsWidening(("a - b", "a * b"), ("a + b", "a * b")));
            // A nested type's method.
            Assert.True(EditNeedsWidening(("b + a", "b * a + a")));
            Assert.False(EditNeedsWidening(("b + a", "b * a")));
        }
    }
}
