using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using TypeDefinition = Mono.Cecil.TypeDefinition;
using TypeReference = Mono.Cecil.TypeReference;

namespace Unbound.Host;

/// <summary>
/// Unity implements most of its math structs (Mathf, Vector2/3/4, Quaternion, Matrix4x4, Color,
/// Plane, ...) in C#, so in the Editor their arithmetic runs on Mono under the same rule as script
/// code: a game's call to <c>Vector3.Dot</c> sums double products. Unbound's engine evaluates these
/// methods in float32, as its own callers (which stand for Unity's native code) need. A game
/// assembly's calls to such a method go instead to a copy of the method's body, converted as script
/// code is and added to the game assembly; the copy's own calls to other such methods go to their
/// copies. Methods Unity binds to native code (<see cref="NativeMath"/>) keep the engine's float32
/// evaluation, as do the methods whose converted body would not change.
/// </summary>
static partial class WideFloats
{
    /// <summary>The UnityEngine structs whose methods Unity implements in C#, apart from
    /// <see cref="NativeMath"/>.</summary>
    internal static readonly HashSet<string> s_ScriptMathTypes = new(StringComparer.Ordinal)
    {
        "Mathf", "Vector2", "Vector3", "Vector4", "Vector2Int", "Vector3Int", "Quaternion", "Matrix4x4", "Color", "Color32",
        "Bounds", "BoundsInt", "Rect", "RectInt", "Plane", "Ray", "Ray2D", "Pose", "GeometryUtility",
    };

    /// <summary>
    /// Methods of <see cref="s_ScriptMathTypes"/> whose arithmetic Unity 6000.0.71f1 runs in native
    /// code (the C# method calls into the engine, after at most one rounded operation on its
    /// arguments), as "Type.Name" (IL names, every overload). The overloads taking engine classes
    /// (Camera, Transform) are left out of the math sources' build altogether. The test
    /// NativeMathFollowsTheReleaseBindings checks the list against the release's assemblies.
    /// </summary>
    internal static readonly HashSet<string> NativeMath = new(StringComparer.Ordinal)
    {
        "Bounds.ClosestPoint", "Bounds.Contains", "Bounds.IntersectRay", "Bounds.IntersectRayAABB", "Bounds.SqrDistance",
        "Color.get_gamma", "Color.get_linear",
        "GeometryUtility.CalculateBounds", "GeometryUtility.CalculateFrustumPlanes", "GeometryUtility.TestPlanesAABB",
        "Mathf.CorrelatedColorTemperatureToRGB", "Mathf.FloatToHalf", "Mathf.HalfToFloat", "Mathf.GammaToLinearSpace",
        "Mathf.LinearToGammaSpace", "Mathf.PerlinNoise", "Mathf.PerlinNoise1D",
        "Matrix4x4.Determinant", "Matrix4x4.get_determinant", "Matrix4x4.Frustum", "Matrix4x4.Inverse", "Matrix4x4.get_inverse",
        "Matrix4x4.Inverse3DAffine", "Matrix4x4.LookAt", "Matrix4x4.Ortho", "Matrix4x4.Perspective", "Matrix4x4.SetTRS",
        "Matrix4x4.TRS", "Matrix4x4.Transpose", "Matrix4x4.get_transpose", "Matrix4x4.ValidTRS", "Matrix4x4.get_decomposeProjection",
        "Matrix4x4.get_isIdentity", "Matrix4x4.get_lossyScale", "Matrix4x4.get_rotation",
        "Quaternion.AngleAxis", "Quaternion.AxisAngle", "Quaternion.Euler", "Quaternion.EulerAngles", "Quaternion.EulerRotation",
        "Quaternion.FromToRotation", "Quaternion.Inverse", "Quaternion.Lerp", "Quaternion.LerpUnclamped", "Quaternion.LookRotation",
        "Quaternion.SetAxisAngle", "Quaternion.SetEulerAngles", "Quaternion.SetEulerRotation", "Quaternion.SetFromToRotation",
        "Quaternion.SetLookRotation", "Quaternion.Slerp", "Quaternion.SlerpUnclamped", "Quaternion.ToAngleAxis",
        "Quaternion.ToAxisAngle", "Quaternion.ToEuler", "Quaternion.ToEulerAngles", "Quaternion.get_eulerAngles",
        "Quaternion.set_eulerAngles",
        "Vector3.OrthoNormalize", "Vector3.RotateTowards", "Vector3.Slerp", "Vector3.SlerpUnclamped",
    };

    /// <summary>The name of the type that holds a game assembly's copies of engine math methods.</summary>
    internal const string EngineMathType = "<Unbound>EngineMath";

    /// <summary>Whether a method of the math sources is one of Unity's math methods that run in C#.</summary>
    internal static bool IsScriptMath(MethodDefinition method)
    {
        var type = method.DeclaringType;
        if (type.Namespace != "UnityEngine" || type.DeclaringType != null || !s_ScriptMathTypes.Contains(type.Name)) return false;
        if (!method.HasBody || method.HasGenericParameters || method.IsVirtual || method.IsStatic && method.IsConstructor) return false;
        return !NativeMath.Contains(type.Name + "." + method.Name);
    }

    /// <summary>
    /// The script math methods whose converted body differs from the engine's: it has expressions to
    /// convert or calls another such method, keyed by full name. In the Editor each local these
    /// methods declare rounds (tests/projects/floatrounding: Vector3.Distance, ClampMagnitude, ...);
    /// the engine's optimized build keeps some locals' values on the evaluation stack, so the bodies
    /// come from a second build of the same sources without IL optimization, where every declared
    /// local is a store, embedded as <c>Unbound.ScriptMath.dll</c>.
    /// </summary>
    sealed class EngineMath
    {
        internal readonly Mono.Cecil.ModuleDefinition Source;
        internal readonly Dictionary<string, MethodDefinition> Methods = new(StringComparer.Ordinal);

        internal EngineMath(Mono.Cecil.ModuleDefinition source)
        {
            Source = source;
            var candidates = new List<MethodDefinition>();
            foreach (var type in source.Types)
                foreach (var method in type.Methods)
                    if (IsScriptMath(method)) candidates.Add(method);
            var converted = new HashSet<MethodDefinition>();
            foreach (var method in candidates)
                if (NeedsConversion(method)) converted.Add(method);
            // A method calling a converted one runs the converted copy: it needs its own copy too.
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var method in candidates)
                    if (!converted.Contains(method) && method.Body.Instructions.Any(i => i.Operand is MethodDefinition callee && converted.Contains(callee)))
                        grew |= converted.Add(method);
            }
            foreach (var method in converted) Methods[method.FullName] = method;
        }
    }

    const string ScriptMathResource = "Unbound.ScriptMath.dll";
    static readonly object s_EngineMathLock = new();
    static EngineMath? s_EngineMath;

    /// <summary>Whether converting the method changes it: it has expressions the double evaluation
    /// can change (or a stack the analysis cannot follow).</summary>
    internal static bool NeedsConversion(MethodDefinition method) =>
        method.HasBody && (WideExpressions(method) is not var (loads, unboxed) || loads.Count > 0 || unboxed.Count > 0);

    /// <summary>The math sources' module (for tests).</summary>
    internal static Mono.Cecil.ModuleDefinition ScriptMathModule => Engine().Source;

    /// <summary>The script math, read when first needed.</summary>
    static EngineMath Engine()
    {
        lock (s_EngineMathLock)
        {
            if (s_EngineMath != null) return s_EngineMath;
            using var stream = typeof(WideFloats).Assembly.GetManifestResourceStream(ScriptMathResource)
                ?? throw new InvalidOperationException($"{ScriptMathResource} is not embedded in Unbound.Host");
            var image = new System.IO.MemoryStream();
            stream.CopyTo(image);
            image.Position = 0;
            var module = Mono.Cecil.ModuleDefinition.ReadModule(image, new ReaderParameters { ReadingMode = ReadingMode.Immediate });
            return s_EngineMath = new EngineMath(module);
        }
    }

    /// <summary>The script math method a call in game code names, when it runs converted.</summary>
    static MethodDefinition? ScriptMathCallee(MethodReference callee)
    {
        if (callee is GenericInstanceMethod || callee.DeclaringType is not { Namespace: "UnityEngine" } declaring
            || declaring is TypeSpecification || declaring.Scope is not AssemblyNameReference || !s_ScriptMathTypes.Contains(declaring.Name))
            return null;
        return Engine().Methods.TryGetValue(callee.FullName, out var method) ? method : null;
    }

    /// <summary>Whether a method calls engine math that runs converted in game code.</summary>
    internal static bool CallsScriptMath(MethodDefinition method) =>
        method.HasBody && method.Body.Instructions.Any(i => i.Operand is MethodReference callee && ScriptMathCallee(callee) != null);

    /// <summary>Imports members of the script math sources as the engine's: the types they name
    /// are the engine's types of the same names.</summary>
    sealed class EngineImporter : DefaultMetadataImporter
    {
        readonly Mono.Cecil.ModuleDefinition m_Source;
        readonly IMetadataScope m_Engine;

        internal EngineImporter(Mono.Cecil.ModuleDefinition module, Mono.Cecil.ModuleDefinition source) : base(module)
        {
            m_Source = source;
            m_Engine = module.ImportReference(typeof(UnityEngine.Object)).Scope;
        }

        protected override IMetadataScope ImportScope(TypeReference type) =>
            type.Scope == m_Source ? m_Engine : base.ImportScope(type);
    }

    /// <summary>Copies of engine math methods added to one game module.</summary>
    sealed class EngineMathCopies
    {
        readonly Mono.Cecil.ModuleDefinition m_Module;
        EngineImporter? m_Importer;
        TypeDefinition? m_Type;
        readonly Dictionary<MethodDefinition, MethodDefinition> m_Copies = new();
        readonly Dictionary<MethodDefinition, MethodDefinition> m_Constructions = new();

        internal EngineMathCopies(Mono.Cecil.ModuleDefinition module) => m_Module = module;

        EngineImporter Importer => m_Importer ??= new EngineImporter(m_Module, Engine().Source);

        TypeReference Import(TypeReference type) => Importer.ImportReference(type, null);

        /// <summary>Redirects the method's calls to converted engine math; false when it has none.</summary>
        internal bool Redirect(MethodDefinition method)
        {
            bool changed = false;
            foreach (var instruction in method.Body.Instructions)
            {
                var code = instruction.OpCode.Code;
                if (code is not (Code.Call or Code.Newobj or Code.Ldftn) || instruction.Operand is not MethodReference callee) continue;
                if (ScriptMathCallee(callee) is not { } engine) continue;
                if (code == Code.Ldftn && engine.HasThis) continue; // a delegate needs the method itself
                if (code == Code.Newobj)
                {
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = Construction(engine);
                }
                else
                {
                    instruction.Operand = Copy(engine);
                }
                changed = true;
            }
            return changed;
        }

        TypeDefinition Type()
        {
            if (m_Type != null) return m_Type;
            m_Type = new TypeDefinition("", EngineMathType,
                TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                m_Module.TypeSystem.Object);
            m_Module.Types.Add(m_Type);
            // The copies use the engine's internal members as the engine's own methods do.
            AccessChecks.IgnoreChecksTo(m_Module, typeof(UnityEngine.Object).Assembly.GetName().Name!);
            return m_Type;
        }

        /// <summary>A static method taking the instance (for instance methods and constructors) by
        /// reference first, then the method's parameters, with the method's body converted.</summary>
        MethodDefinition Copy(MethodDefinition engine)
        {
            if (m_Copies.TryGetValue(engine, out var copy)) return copy;
            var returnType = engine.IsConstructor ? m_Module.TypeSystem.Void : Import(engine.ReturnType);
            copy = new MethodDefinition(engine.DeclaringType.Name + "." + engine.Name,
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, returnType)
            {
                ImplAttributes = engine.ImplAttributes & ~Mono.Cecil.MethodImplAttributes.NoOptimization,
            };
            m_Copies[engine] = copy;
            Type().Methods.Add(copy);
            if (engine.HasThis)
                copy.Parameters.Add(new ParameterDefinition("this", ParameterAttributes.None,
                    new ByReferenceType(Import(engine.DeclaringType))));
            foreach (var parameter in engine.Parameters)
                copy.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, Import(parameter.ParameterType)));
            CopyBody(engine, copy);
            WidenBody(copy);
            return copy;
        }

        /// <summary>For <c>newobj</c>: a static method returning the value its constructor's copy builds.</summary>
        MethodDefinition Construction(MethodDefinition constructor)
        {
            if (m_Constructions.TryGetValue(constructor, out var construction)) return construction;
            var type = Import(constructor.DeclaringType);
            construction = new MethodDefinition(constructor.DeclaringType.Name + ".new",
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, type);
            foreach (var parameter in constructor.Parameters)
                construction.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, Import(parameter.ParameterType)));
            var value = new VariableDefinition(type);
            construction.Body.Variables.Add(value);
            construction.Body.InitLocals = true;
            var il = construction.Body.GetILProcessor();
            il.Emit(OpCodes.Ldloca, value);
            foreach (var parameter in construction.Parameters) il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, Copy(constructor));
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
            construction.Body.OptimizeMacros();
            m_Constructions[constructor] = construction;
            Type().Methods.Add(construction);
            return construction;
        }

        /// <summary>The engine method's body in <paramref name="copy"/>, its members imported and its
        /// calls to converted engine math going to their copies. The copy's parameters are the
        /// engine method's in the same positions (the instance first), so argument numbers hold.</summary>
        void CopyBody(MethodDefinition engine, MethodDefinition copy)
        {
            var source = engine.Body;
            var body = copy.Body;
            body.InitLocals = source.InitLocals;
            body.MaxStackSize = source.MaxStackSize;
            foreach (var variable in source.Variables)
                body.Variables.Add(new VariableDefinition(Import(variable.VariableType)));
            var instructions = new Dictionary<Instruction, Instruction>();
            foreach (var instruction in source.Instructions)
            {
                var clone = Instruction.Create(OpCodes.Nop);
                clone.OpCode = instruction.OpCode;
                clone.Operand = instruction.Operand;
                instructions[instruction] = clone;
                body.Instructions.Add(clone);
            }
            int shift = engine.HasThis ? 1 : 0;
            ParameterDefinition Parameter(ParameterDefinition parameter) =>
                parameter == source.ThisParameter ? copy.Parameters[0] : copy.Parameters[parameter.Index + shift];
            foreach (var clone in body.Instructions)
            {
                clone.Operand = clone.Operand switch
                {
                    Instruction target => instructions[target],
                    Instruction[] targets => targets.Select(t => instructions[t]).ToArray(),
                    ParameterDefinition parameter => Parameter(parameter),
                    VariableDefinition variable => body.Variables[variable.Index],
                    MethodDefinition callee when Engine().Methods.ContainsKey(callee.FullName) && clone.OpCode.Code is Code.Call or Code.Newobj =>
                        clone.OpCode.Code == Code.Newobj ? Construction(callee) : Copy(callee),
                    MethodReference callee => Importer.ImportReference(callee, copy),
                    FieldReference field => Importer.ImportReference(field, copy),
                    TypeReference type => Import(type),
                    CallSite => throw new NotSupportedException($"{engine.FullName}: calli"),
                    var operand => operand,
                };
                if (clone.OpCode.Code == Code.Newobj && clone.Operand is MethodDefinition { DeclaringType.Name: EngineMathType })
                    clone.OpCode = OpCodes.Call;
            }
            foreach (var handler in source.ExceptionHandlers)
                body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
                {
                    TryStart = Map(handler.TryStart),
                    TryEnd = Map(handler.TryEnd),
                    HandlerStart = Map(handler.HandlerStart),
                    HandlerEnd = Map(handler.HandlerEnd),
                    FilterStart = Map(handler.FilterStart),
                    CatchType = handler.CatchType == null ? null : Import(handler.CatchType),
                });
            Instruction? Map(Instruction? instruction) => instruction == null ? null : instructions[instruction];
            // Operand sizes can change (a short branch over a longer conversion): recompute the forms.
            body.SimplifyMacros();
            body.OptimizeMacros();
        }
    }
}
