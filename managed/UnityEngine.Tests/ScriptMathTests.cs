using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;
using Unbound.Host;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Game code's calls to the math methods Unity implements in C# run converted copies of them
    /// (tests/projects/floatrounding `api/` rows); the engine's own calls and the methods Unity binds
    /// to native code keep float32 arithmetic.
    /// </summary>
    public class ScriptMathTests
    {
        const string Source = @"
using UnityEngine;
public static class Game
{
    public static float Dot(float a, float b, float c) => Vector3.Dot(new Vector3(a, b, 0f), new Vector3(a, c, 0f));
    public static float Magnitude(float a, float b, float c) => new Vector3(a, b, 0f).sqrMagnitude + c;
    public static Vector3 Clamp(Vector3 v, float length) => Vector3.ClampMagnitude(v, length);
    public static Vector3 Normalized(Vector3 v) { v.Normalize(); return v; }
    public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => Quaternion.Slerp(a, b, t);
    public static float PlaneDistance(Vector3 normal, Vector3 point) => new Plane(normal, point).distance;
    public static System.Func<Vector3, Vector3, Vector3> Cross() => Vector3.Cross;
    public static int Count(int a, int b) => a - b;
}";

        static readonly Lazy<(byte[] Image, System.Reflection.Assembly Game)> s_Game = new(() =>
        {
            var image = WideFloats.Widen(WideFloatsTests.Emit(WideFloatsTests.Compilation(Source, OptimizationLevel.Release, "ScriptMathGame", engine: true)).Image);
            return (image, WideFloatsTests.Load(image));
        });

        static object Call(string method, params object[] arguments) => s_Game.Value.Game.GetType("Game")!.GetMethod(method)!.Invoke(null, arguments)!;

        static Mono.Cecil.MethodDefinition GameMethod(Mono.Cecil.ModuleDefinition module, string name) =>
            module.GetType("Game").Methods.Single(m => m.Name == name);

        static string[] Callees(Mono.Cecil.MethodDefinition method) =>
            method.Body.Instructions.Where(i => i.Operand is Mono.Cecil.MethodReference).Select(i => ((Mono.Cecil.MethodReference)i.Operand).DeclaringType.Name + "::" + ((Mono.Cecil.MethodReference)i.Operand).Name).ToArray();

        [Fact]
        public void GameCallsSumDoubleProducts()
        {
            // 4097 * 4097 + 1 * -16785408 is 1, but 16785409 is not a float32 (it rounds to 16785408).
            Assert.Equal(1f, Call("Dot", 4097f, 1f, -16785408f));
            // The engine's own call keeps float32 arithmetic.
            Assert.Equal(0f, Vector3.Dot(new Vector3(4097f, 1f, 0f), new Vector3(4097f, -16785408f, 0f)));
            // The control: the game assembly without the rewrite.
            var plain = WideFloatsTests.Load(WideFloatsTests.Emit(WideFloatsTests.Compilation(Source, OptimizationLevel.Release, "ScriptMathPlain", engine: true)).Image);
            Assert.Equal(0f, plain.GetType("Game")!.GetMethod("Dot")!.Invoke(null, new object[] { 4097f, 1f, -16785408f }));
        }

        [Fact]
        public void CopiesRoundWhereTheSourcesDeclareLocals()
        {
            // ClampMagnitude stores x / magnitude in a local before scaling it: 3 / 5 rounds to float32
            // there. Kept wide, (3 / 5) * 1.3 would give 0.7799999714 instead of 0.7800000310.
            var clamped = (Vector3)Call("Clamp", new Vector3(3f, 4f, 0f), 1.3f);
            float rounded = (float)((double)(3f / 5f) * 1.3f);
            Assert.NotEqual((float)(3.0 / 5.0 * 1.3f), rounded);
            Assert.Equal(rounded, clamped.x);
        }

        [Fact]
        public void NativeMethodsAndOperatorsStayTheEngines()
        {
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(s_Game.Value.Image));
            Assert.Contains("<Unbound>EngineMath::Vector3.Dot", Callees(GameMethod(module, "Dot")));
            Assert.Contains("<Unbound>EngineMath::Vector3.get_sqrMagnitude", Callees(GameMethod(module, "Magnitude")));
            // Instance methods take the value by reference, as the engine's call did.
            Assert.Contains("<Unbound>EngineMath::Vector3.Normalize", Callees(GameMethod(module, "Normalized")));
            // Quaternion.Slerp runs in Unity's native code; constructors without arithmetic stay too.
            Assert.Equal(new[] { "Quaternion::Slerp" }, Callees(GameMethod(module, "Slerp")));
            Assert.Contains("Vector3::.ctor", Callees(GameMethod(module, "Dot")));
            Assert.Equal(new Quaternion(0f, 0f, 0f, 1f), Call("Slerp", Quaternion.identity, Quaternion.identity, 0.5f));
            var normalized = (Vector3)Call("Normalized", new Vector3(0f, 3f, 4f));
            Assert.Equal(new Vector3(0f, 0.6f, 0.8f), normalized);
        }

        [Fact]
        public void ConstructorsAndDelegatesUseTheCopies()
        {
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(s_Game.Value.Image));
            Assert.Contains("<Unbound>EngineMath::Plane.new", Callees(GameMethod(module, "PlaneDistance")));
            Assert.Contains("<Unbound>EngineMath::Vector3.Cross", Callees(GameMethod(module, "Cross")));
            Assert.Equal(-2f, Call("PlaneDistance", new Vector3(0f, 2f, 0f), new Vector3(5f, 2f, 7f)));
            var cross = (Func<Vector3, Vector3, Vector3>)Call("Cross");
            Assert.Equal(new Vector3(0f, 0f, 1f), cross(Vector3.right, Vector3.up));
        }

        [Fact]
        public void HotReloadRestartsWhenAnEditCallsConvertedMath()
        {
            Assert.True(WideFloatsTests.EditNeedsWidening(Source, true, ("a - b;", "a - (int)Vector3.Dot(Vector3.one, Vector3.one);")));
            Assert.False(WideFloatsTests.EditNeedsWidening(Source, true, ("a - b;", "a * b;")));
            // Native math is called as it is.
            Assert.False(WideFloatsTests.EditNeedsWidening(Source, true, ("Quaternion.Slerp(a, b, t)", "Quaternion.Slerp(b, a, t)")));
        }

        /// <summary>The release's UnityEngine.CoreModule, when its Editor is installed here.</summary>
        static string? ReleaseCoreModule()
        {
            var overridden = Environment.GetEnvironmentVariable("UNBOUND_TEST_UNITY_MANAGED_DIR");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var dir in new[] { overridden, Path.Combine(home, "Unity/Hub/Editor/6000.0.71f1/Editor/Data/Managed/UnityEngine"),
                         "/Applications/Unity/Hub/Editor/6000.0.71f1/Unity.app/Contents/Managed/UnityEngine" })
                if (dir != null && File.Exists(Path.Combine(dir, "UnityEngine.CoreModule.dll"))) return Path.Combine(dir, "UnityEngine.CoreModule.dll");
            return null;
        }

        /// <summary>Whether a release method runs its arithmetic in native code: it is extern, or calls
        /// (through methods of the math types) a method that is.</summary>
        static bool BoundToNative(Mono.Cecil.MethodDefinition method, System.Collections.Generic.HashSet<Mono.Cecil.MethodDefinition> visiting)
        {
            if (!method.HasBody || method.Name.EndsWith("_Injected", StringComparison.Ordinal)) return true;
            if (!visiting.Add(method)) return false;
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is Mono.Cecil.MethodDefinition callee && callee.DeclaringType.Namespace == "UnityEngine"
                    && WideFloats.s_ScriptMathTypes.Contains(callee.DeclaringType.Name) && BoundToNative(callee, visiting))
                    return true;
            return false;
        }

        [Fact]
        public void NativeMathFollowsTheReleaseBindings()
        {
            if (ReleaseCoreModule() is not { } path) return; // no 6000.0.71f1 Editor on this machine
            using var release = Mono.Cecil.ModuleDefinition.ReadModule(path);
            var releaseMethods = release.Types.Where(t => t.Namespace == "UnityEngine" && WideFloats.s_ScriptMathTypes.Contains(t.Name))
                .SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
            var sources = WideFloats.ScriptMathModule.Types.Where(t => t.Namespace == "UnityEngine" && WideFloats.s_ScriptMathTypes.Contains(t.Name))
                .SelectMany(t => t.Methods).ToList();
            var problems = new System.Collections.Generic.List<string>();
            foreach (var entry in WideFloats.NativeMath)
                if (!releaseMethods.Values.Any(m => m.DeclaringType.Name + "." + m.Name == entry)) problems.Add("no such method: " + entry);
            foreach (var method in sources)
            {
                if (!releaseMethods.TryGetValue(method.FullName, out var unity) || unity.IsAbstract) continue; // the engine's own helpers
                // Native: the arithmetic happens in native code; what the C# method computes itself,
                // or through the math methods it calls, gives the same in float32 (RotateTowards and
                // TransformPlane combine a native call with arithmetic of their own: they are C#).
                bool native = BoundToNative(unity, new()) && !WideFloats.NeedsConversion(unity) && !(unity.HasBody && unity.Body.Instructions.Any(i =>
                    i.Operand is Mono.Cecil.MethodDefinition callee && WideFloats.s_ScriptMathTypes.Contains(callee.DeclaringType.Name)
                    && WideFloats.NeedsConversion(callee) && !BoundToNative(callee, new())));
                bool listed = WideFloats.NativeMath.Contains(method.DeclaringType.Name + "." + method.Name);
                // A listed name covers its overloads: each must be native in the release.
                if (listed && !native) problems.Add("listed as native but C# in the release: " + method.FullName);
                if (!listed && native && WideFloats.IsScriptMath(method) && WideFloats.NeedsConversion(method))
                    problems.Add("native in the release but converted: " + method.FullName);
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
