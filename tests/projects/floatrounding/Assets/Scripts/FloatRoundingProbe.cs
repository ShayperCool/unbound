using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Where game code's float arithmetic keeps a wider intermediate and where it rounds to float.
/// Every control takes its operands as arguments (so no compiler folds them) and returns the
/// recorded result. Most use operands whose exact intermediate is not a float: with
/// a = 16777216, b = 1, c = -16777216, "(a + b) + c" is 1 when a + b stays wider than float
/// through the construct under test and 0 when it is rounded there.
/// </summary>
public sealed class FloatRoundingProbe : MonoBehaviour
{
    const string Variable = "FLOAT_ROUNDING_OUT";
    readonly StringBuilder rows = new StringBuilder();
    string output;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrEmpty(path)) new GameObject("Float rounding probe").AddComponent<FloatRoundingProbe>().output = path;
    }

    void Value(string key, string value) => rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Bits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0).ToString("X8");
    static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    // Operand sets.
    static readonly float[] Wide = { 16777216f, 1f, -16777216f };
    static readonly float[] Sub = { 16777216f, -1f, -16777216f };
    static readonly float[] Mul = { 4097f, 4097f, -16785408f };
    static readonly float[] Div = { 20f, 37f, 2f };
    static readonly float[] Rem = { 16777216f, 1f, 3f };
    static readonly float[] Overflow = { 1e30f, 1e30f, 1e-30f };
    static readonly float[] Underflow = { 1e-30f, 1e-30f, 1e30f };
    static readonly float[] Root = { 33554432f, 2f, -5792.61865234375f };
    static readonly float[] Square = { 4097f, 1f, -16785408f };

    void Control(string key, Func<float, float, float, float> control, float[] operands) =>
        Value(key, Bits(control(operands[0], operands[1], operands[2])));

    void Start()
    {
        try
        {
            Record();
        }
        catch (Exception e)
        {
            Value("error", e.GetType().Name + ": " + e.Message.Replace("\"", "'").Replace("\\", "/"));
        }
        rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    void Record()
    {
        // Operators and chains.
        Control("op/add", Controls.Add, Wide);
        Control("op/sub", Controls.Subtract, Sub);
        Control("op/mul", Controls.Multiply, Mul);
        Control("op/div", Controls.Divide, Div);
        Control("op/rem", Controls.Remainder, Rem);
        Control("op/neg", Controls.Negate, Wide);
        Control("op/overflow", Controls.Product3, Overflow);
        Control("op/underflow", Controls.Product3, Underflow);

        // Conversions.
        Value("convert/toDouble", Bits(Controls.ToDouble(16777216f, 1f)));
        Value("convert/toInt", Controls.ToInt(16777216f, 1f).ToString(CultureInfo.InvariantCulture));
        Value("convert/checkedInt", Controls.CheckedInt(16777216f, 1f).ToString(CultureInfo.InvariantCulture));
        Value("convert/toLong", Controls.ToLong(16777216f, 1f).ToString(CultureInfo.InvariantCulture));
        Value("convert/mixedDouble", Bits(Controls.MixedDouble(16777216f, 1f, -16777216f)));
        Value("convert/intToFloat", Bits(Controls.IntToFloat(16777217, -16777216f)));
        Value("convert/longToFloat", Bits(Controls.LongToFloat(16777217L, -16777216f)));
        Value("convert/uintToFloat", Bits(Controls.UIntToFloat(16777217u, -16777216f)));
        Value("convert/doubleToFloat", Bits(Controls.DoubleToFloat(16777217.0, -16777216f)));
        Control("convert/cast", Controls.Cast, Wide);

        // Comparisons.
        Value("compare/equal", Controls.Equal(16777216f, 1f).ToString());
        Value("compare/greater", Controls.Greater(16777216f, 1f).ToString());
        Value("compare/castEqual", Controls.CastEqual(16777216f, 1f).ToString());
        Value("compare/localEqual", Controls.LocalEqual(16777216f, 1f).ToString());
        Control("compare/branch", Controls.Branch, Wide);
        Control("ternary/value", Controls.TernaryValue, Wide);
        Control("ternary/local", Controls.TernaryLocal, Wide);

        // Locals and arguments.
        Control("local/single", Controls.LocalSingle, Wide);
        Control("local/twice", Controls.LocalTwice, Wide);
        Control("local/call", Controls.LocalCall, Wide);
        Control("local/inlinedCall", Controls.LocalInlinedCall, Wide);
        Control("local/branch", Controls.LocalBranch, Wide);
        Control("local/compound", Controls.LocalCompound, Wide);
        Control("local/increment", Controls.LocalIncrement, Wide);
        Control("local/loopBody", Controls.LocalLoopBody, Wide);
        Control("local/loopExit", Controls.LocalLoopExit, Wide);
        Control("local/addressTaken", Controls.LocalAddressTaken, Wide);
        Control("local/copied", Controls.LocalCopied, Wide);
        Control("arg/store", Controls.ArgumentStore, Wide);
        Control("arg/storeCall", Controls.ArgumentStoreCall, Wide);

        // Memory.
        Control("store/staticField", Controls.StaticField, Wide);
        Control("store/volatileField", Controls.VolatileField, Wide);
        Control("store/instanceField", Controls.InstanceField, Wide);
        Control("store/array", Controls.ArrayElement, Wide);
        Control("store/refLocal", Controls.RefLocal, Wide);
        Control("store/span", Controls.SpanElement, Wide);
        Control("store/stackalloc", Controls.StackSpan, Wide);
        Control("store/box", Controls.Box, Wide);
        Control("store/boxField", Controls.BoxField, Wide);
        Control("store/boxApart", Controls.BoxApart, Wide);
        Control("store/property", Controls.PropertyStore, Wide);
        Control("store/fieldCompound", Controls.FieldCompound, Wide);
        Control("store/arrayCompound", Controls.ArrayCompound, Wide);
        Control("store/structLocal", Controls.StructLocal, Wide);
        Control("store/structConstructor", Controls.StructConstructor, Wide);
        Control("store/structInitializer", Controls.StructInitializer, Wide);

        // Calls.
        Control("call/noInliningArgument", Controls.NoInliningArgument, Wide);
        Control("call/noInliningReturn", Controls.NoInliningReturn, Wide);
        Control("call/argument", Controls.SmallArgument, Wide);
        Control("call/return", Controls.SmallReturn, Wide);
        Control("call/aggressiveArgument", Controls.AggressiveArgument, Wide);
        Control("call/aggressiveReturn", Controls.AggressiveReturn, Wide);
        Control("call/localReturn", Controls.LocalReturn, Wide);
        Control("call/body", Controls.Body, Wide);
        Control("call/instance", Controls.Instance, Wide);
        Control("call/virtual", Controls.Virtual, Wide);
        Control("call/interface", Controls.Interface, Wide);
        Control("call/delegate", Controls.DelegateCall, Wide);
        Control("generic/noInliningMethod", Controls.GenericNoInlining, Wide);
        Control("generic/method", Controls.GenericMethod, Wide);
        Control("generic/classField", Controls.GenericField, Wide);
        Control("generic/classMethod", Controls.GenericClassMethod, Wide);
        Control("generic/nullable", Controls.Nullable, Wide);

        // Compiler-generated state.
        Control("state/closure", Controls.Closure, Wide);
        Control("state/lambdaBody", Controls.LambdaBody, Wide);
        Control("state/iterator", Controls.Iterator, Wide);
        Control("state/async", Controls.Async, Wide);
        Control("state/tryFinally", Controls.TryFinally, Wide);

        // Library calls.
        Control("math/mathfAbs", Controls.MathfAbs, Wide);
        Control("math/mathfMax", Controls.MathfMax, Wide);
        Control("math/mathfMin", Controls.MathfMin, Wide);
        Control("math/mathfClamp", Controls.MathfClamp, Wide);
        Control("math/mathfFloor", Controls.MathfFloor, Wide);
        Control("math/mathfSqrt", Controls.MathfSqrt, Root);
        Control("math/mathSqrt", Controls.MathSqrt, Root);
        Control("math/mathAbs", Controls.MathAbs, Wide);
        Control("math/mathMax", Controls.MathMax, Wide);
        Control("math/mathFAbs", Controls.MathFAbs, Wide);
        Control("math/mathFSqrt", Controls.MathFSqrt, Root);
        Control("api/vector3Dot", Controls.Vector3Dot, Square);
        Control("api/vector3SqrMagnitude", Controls.Vector3SqrMagnitude, Square);
        Control("api/vector3Magnitude", Controls.Vector3Magnitude, Square);
        Control("api/vector3AddChain", Controls.Vector3AddChain, Wide);
        Control("api/vector3Components", Controls.Vector3Components, Wide);

        Fuzz();
        ApiRows();

        foreach (var method in typeof(Controls).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var body = method.GetMethodBody();
            if (body != null) Value("il/" + method.Name, InstructionText(method, body.GetILAsByteArray()));
        }
        Value("codeOptimization", UnityEditor.Compilation.CompilationPipeline.codeOptimization.ToString());
    }

    static Dictionary<short, System.Reflection.Emit.OpCode> s_OpCodes;

    /// <summary>A method body as opcodes with their operands, metadata tokens replaced by the
    /// names they resolve to (token values depend on the compilation, the instructions do not).</summary>
    static string InstructionText(MethodBase method, byte[] il)
    {
        if (s_OpCodes == null)
        {
            s_OpCodes = new Dictionary<short, System.Reflection.Emit.OpCode>();
            foreach (var field in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var code = (System.Reflection.Emit.OpCode)field.GetValue(null);
                s_OpCodes[code.Value] = code;
            }
        }
        var text = new StringBuilder();
        for (int at = 0; at < il.Length;)
        {
            short value = il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at];
            at += il[at] == 0xFE ? 2 : 1;
            var code = s_OpCodes[value];
            if (text.Length > 0) text.Append(' ');
            text.Append(code.Name);
            switch (code.OperandType)
            {
                case System.Reflection.Emit.OperandType.InlineNone:
                    break;
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                    text.Append(':').Append(at + 1 + (sbyte)il[at]); at += 1;
                    break;
                case System.Reflection.Emit.OperandType.ShortInlineI:
                case System.Reflection.Emit.OperandType.ShortInlineVar:
                    text.Append(':').Append(il[at]); at += 1;
                    break;
                case System.Reflection.Emit.OperandType.InlineVar:
                    text.Append(':').Append(BitConverter.ToUInt16(il, at)); at += 2;
                    break;
                case System.Reflection.Emit.OperandType.InlineBrTarget:
                    text.Append(':').Append(at + 4 + BitConverter.ToInt32(il, at)); at += 4;
                    break;
                case System.Reflection.Emit.OperandType.InlineI:
                    text.Append(':').Append(BitConverter.ToInt32(il, at)); at += 4;
                    break;
                case System.Reflection.Emit.OperandType.ShortInlineR:
                    text.Append(':').Append(Bits(BitConverter.ToSingle(il, at))); at += 4;
                    break;
                case System.Reflection.Emit.OperandType.InlineI8:
                    text.Append(':').Append(BitConverter.ToInt64(il, at)); at += 8;
                    break;
                case System.Reflection.Emit.OperandType.InlineR:
                    text.Append(':').Append(Bits(BitConverter.ToDouble(il, at))); at += 8;
                    break;
                case System.Reflection.Emit.OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, at), end = at + 4 + 4 * count;
                    for (int i = 0; i < count; i++) text.Append(':').Append(end + BitConverter.ToInt32(il, at + 4 + 4 * i));
                    at = end;
                    break;
                }
                case System.Reflection.Emit.OperandType.InlineString:
                    text.Append(":string"); at += 4;
                    break;
                default:
                {
                    var member = method.Module.ResolveMember(BitConverter.ToInt32(il, at), method.DeclaringType.GetGenericArguments(),
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                    text.Append(':').Append(member is Type type ? type.Name : member.DeclaringType.Name + "::" + member.Name);
                    at += 4;
                    break;
                }
            }
        }
        return text.ToString();
    }

    // ------------------------------------------------------------------ the math API

    /// <summary>24 results of <paramref name="function"/>, each on <paramref name="arity"/> fresh
    /// random floats (exponents in [-8, 8]); each result's floats are joined with commas.</summary>
    void Api(string key, int arity, Func<float[], float[]> function)
    {
        var values = new List<string>();
        var input = new float[arity];
        for (int i = 0; i < Samples; i++)
        {
            for (int j = 0; j < arity; j++) input[j] = RandomFloat();
            var result = function(input);
            var text = new StringBuilder();
            foreach (var value in result) text.Append(text.Length > 0 ? "," : "").Append(Bits(value));
            values.Add(text.ToString());
        }
        Value(key, string.Join(" ", values));
    }

    static Vector2 V2(float[] f, int at) => new Vector2(f[at], f[at + 1]);
    static Vector3 V3(float[] f, int at) => new Vector3(f[at], f[at + 1], f[at + 2]);
    static Vector4 V4(float[] f, int at) => new Vector4(f[at], f[at + 1], f[at + 2], f[at + 3]);
    static Quaternion Q(float[] f, int at) => new Quaternion(f[at], f[at + 1], f[at + 2], f[at + 3]);
    static Color C(float[] f, int at) => new Color(f[at], f[at + 1], f[at + 2], f[at + 3]);
    static Matrix4x4 M(float[] f, int at) => new Matrix4x4(V4(f, at), V4(f, at + 4), V4(f, at + 8), V4(f, at + 12));
    static float[] F(Vector2 v) => new[] { v.x, v.y };
    static float[] F(Vector3 v) => new[] { v.x, v.y, v.z };
    static float[] F(Vector4 v) => new[] { v.x, v.y, v.z, v.w };
    static float[] F(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
    static float[] F(Color c) => new[] { c.r, c.g, c.b, c.a };
    static float[] F(Plane p) => new[] { p.normal.x, p.normal.y, p.normal.z, p.distance };
    static float[] F(Matrix4x4 m)
    {
        var result = new float[16];
        for (int i = 0; i < 16; i++) result[i] = m[i];
        return result;
    }
    static float Unit(float v) => Mathf.Abs(v) % 1f;

    /// <summary>
    /// Unity's math API on random operands. The `api/` rows call methods Unity implements in C#
    /// (they run on the Editor's Mono like script code); the `native/` rows call methods bound to
    /// Unity's native code.
    /// </summary>
    void ApiRows()
    {
        Api("api/mathfLerpAngle", 3, f => new[] { Mathf.LerpAngle(f[0] * 40f, f[1] * 40f, Unit(f[2])) });
        Api("api/mathfDeltaAngle", 2, f => new[] { Mathf.DeltaAngle(f[0] * 40f, f[1] * 40f) });
        Api("api/mathfMoveTowardsAngle", 3, f => new[] { Mathf.MoveTowardsAngle(f[0] * 40f, f[1] * 40f, Mathf.Abs(f[2])) });
        Api("api/mathfSmoothDampAngle", 4, f =>
        {
            float velocity = f[3];
            float value = Mathf.SmoothDampAngle(f[0] * 40f, f[1] * 40f, ref velocity, Mathf.Abs(f[2]) + 0.01f, Mathf.Infinity, 0.02f);
            return new[] { value, velocity };
        });
        Api("api/mathfLerpUnclamped", 3, f => new[] { Mathf.LerpUnclamped(f[0], f[1], f[2]) });
        Api("api/mathfGamma", 3, f => new[] { Mathf.Gamma(f[0], f[1], Mathf.Abs(f[2])) });
        Api("api/mathfApproximately", 2, f => new[] { Mathf.Approximately(f[0], f[0] + f[1] * 1e-6f) ? 1f : 0f });
        Api("api/vector2", 5, f =>
        {
            Vector2 u = V2(f, 0), v = V2(f, 2);
            return new[] { Vector2.Dot(u, v), u.magnitude, u.sqrMagnitude, Vector2.Distance(u, v), Vector2.Angle(u, v), Vector2.SignedAngle(u, v),
                Vector2.Lerp(u, v, Unit(f[4])).x, Vector2.Reflect(u, v).y, u.normalized.x, Vector2.ClampMagnitude(u, Mathf.Abs(f[4])).y,
                Vector2.MoveTowards(u, v, Mathf.Abs(f[4])).x };
        });
        Api("api/vector3", 7, f =>
        {
            Vector3 u = V3(f, 0), v = V3(f, 3);
            return new[] { Vector3.Angle(u, v), Vector3.SignedAngle(u, v, Vector3.Cross(u, v) + Vector3.up), Vector3.ClampMagnitude(u, Mathf.Abs(f[6])).x,
                Vector3.MoveTowards(u, v, Mathf.Abs(f[6])).y, Vector3.ProjectOnPlane(u, v).z, Vector3.LerpUnclamped(u, v, f[6]).x,
                Vector3.Distance(u, v), Vector3.SqrMagnitude(u - v), Vector3.Magnitude(u), (u == v * (1f + f[6] * 1e-7f)) ? 1f : 0f };
        });
        Api("api/vector3SmoothDamp", 10, f =>
        {
            var velocity = V3(f, 6);
            var value = Vector3.SmoothDamp(V3(f, 0), V3(f, 3), ref velocity, Mathf.Abs(f[9]) + 0.01f, Mathf.Infinity, 0.02f);
            return new[] { value.x, value.y, value.z, velocity.x, velocity.y, velocity.z };
        });
        Api("api/vector4", 9, f =>
        {
            Vector4 u = V4(f, 0), v = V4(f, 4);
            return new[] { Vector4.Dot(u, v), u.magnitude, u.sqrMagnitude, Vector4.Distance(u, v), u.normalized.w, Vector4.Lerp(u, v, Unit(f[8])).x,
                Vector4.Project(u, v).y, Vector4.MoveTowards(u, v, Mathf.Abs(f[8])).z };
        });
        Api("api/quaternionMultiply", 8, f => F(Q(f, 0) * Q(f, 4)));
        Api("api/quaternionScalars", 8, f =>
        {
            Quaternion p = Q(f, 0), q = Q(f, 4);
            return new[] { Quaternion.Dot(p, q), Quaternion.Angle(p.normalized, q.normalized), (p.normalized == q.normalized) ? 1f : 0f };
        });
        Api("api/quaternionNormalize", 4, f => F(Quaternion.Normalize(Q(f, 0))));
        Api("api/matrixMultiply", 32, f => F(M(f, 0) * M(f, 16)));
        Api("api/matrixVector", 20, f =>
        {
            var m = M(f, 0);
            var point = V3(f, 16);
            var result = new List<float>();
            result.AddRange(F(m.MultiplyPoint(point)));
            result.AddRange(F(m.MultiplyPoint3x4(point)));
            result.AddRange(F(m.MultiplyVector(point)));
            result.AddRange(F(m * V4(f, 16)));
            result.AddRange(F(m.TransformPlane(new Plane(V3(f, 16), f[19]))));
            return result.ToArray();
        });
        Api("api/color", 9, f =>
        {
            Color a = C(f, 0), b = C(f, 4);
            Color.RGBToHSV(new Color(Mathf.Abs(f[0]) % 1f, Mathf.Abs(f[1]) % 1f, Mathf.Abs(f[2]) % 1f), out float h, out float s, out float v);
            var rgb = Color.HSVToRGB(Unit(f[4]), Unit(f[5]), Unit(f[6]));
            var c32 = Color32.Lerp(new Color32(10, 200, 30, 255), new Color32(250, 3, 128, 0), Unit(f[8]));
            return new[] { Color.Lerp(a, b, Unit(f[8])).r, Color.LerpUnclamped(a, b, f[8]).g, a.grayscale, h, s, v, rgb.r, rgb.g, rgb.b,
                c32.r, c32.g, c32.b, c32.a };
        });
        Api("api/plane", 10, f =>
        {
            var plane = new Plane(V3(f, 0), V3(f, 3));
            var three = new Plane(V3(f, 0), V3(f, 3), V3(f, 6));
            var point = V3(f, 6);
            bool hit = plane.Raycast(new Ray(point, V3(f, 3) + Vector3.one), out float enter);
            var result = new List<float>();
            result.AddRange(F(plane));
            result.AddRange(F(three));
            result.Add(plane.GetDistanceToPoint(point));
            result.AddRange(F(plane.ClosestPointOnPlane(point)));
            result.Add(hit ? enter : -enter);
            return result.ToArray();
        });
        Api("api/rect", 6, f =>
        {
            var rect = new Rect(f[0], f[1], Mathf.Abs(f[2]), Mathf.Abs(f[3]));
            var point = new Vector2(f[4], f[5]);
            return new[] { Rect.NormalizedToPoint(rect, point).x, Rect.PointToNormalized(rect, point).y, rect.center.x, rect.xMax };
        });

        Api("api/matrixRotate", 4, f => F(Matrix4x4.Rotate(Q(f, 0).normalized)));
        Api("api/quaternionRotateTowards", 9, f => F(Quaternion.RotateTowards(Q(f, 0).normalized, Q(f, 4).normalized, Mathf.Abs(f[8]) * 40f)));
        Api("api/pose", 14, f =>
        {
            var pose = new Pose(V3(f, 0), Q(f, 3).normalized).GetTransformedBy(new Pose(V3(f, 7), Q(f, 10).normalized));
            var result = new List<float>();
            result.AddRange(F(pose.position));
            result.AddRange(F(pose.rotation));
            result.AddRange(F(pose.forward));
            return result.ToArray();
        });
        Api("api/color32", 4, f =>
        {
            Color32 c = C(f, 0) * 0.25f + new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Color back = c;
            return new[] { (float)c.r, c.g, c.b, c.a, back.r, back.a };
        });
        Api("api/polygonPlane", 9, f =>
        {
            bool made = GeometryUtility.TryCreatePlaneFromPolygon(new[] { V3(f, 0), V3(f, 3), V3(f, 6) }, out var plane);
            return made ? F(plane) : new[] { -1f };
        });
        Api("native/quaternionSlerp", 9, f => F(Quaternion.Slerp(Q(f, 0).normalized, Q(f, 4).normalized, Unit(f[8]))));
        Api("native/quaternionLerp", 9, f => F(Quaternion.Lerp(Q(f, 0), Q(f, 4), Unit(f[8]))));
        Api("native/quaternionEuler", 3, f => F(Quaternion.Euler(f[0] * 40f, f[1] * 40f, f[2] * 40f)));
        Api("native/quaternionEulerAngles", 4, f => F(Q(f, 0).normalized.eulerAngles));
        Api("native/quaternionLookRotation", 6, f => F(Quaternion.LookRotation(V3(f, 0), V3(f, 3))));
        Api("native/quaternionAngleAxis", 4, f => F(Quaternion.AngleAxis(f[0] * 40f, V3(f, 1))));
        Api("native/quaternionFromToRotation", 6, f => F(Quaternion.FromToRotation(V3(f, 0), V3(f, 3))));
        Api("native/quaternionInverse", 4, f => F(Quaternion.Inverse(Q(f, 0))));
        Api("native/vector3Slerp", 7, f => F(Vector3.Slerp(V3(f, 0), V3(f, 3), Unit(f[6]))));
        Api("native/vector3RotateTowards", 8, f => F(Vector3.RotateTowards(V3(f, 0), V3(f, 3), Mathf.Abs(f[6]), Mathf.Abs(f[7]))));
        Api("native/vector3OrthoNormalize", 6, f =>
        {
            Vector3 u = V3(f, 0), v = V3(f, 3);
            Vector3.OrthoNormalize(ref u, ref v);
            return new[] { u.x, u.y, u.z, v.x, v.y, v.z };
        });
        Api("native/matrixTRS", 10, f => F(Matrix4x4.TRS(V3(f, 0), Q(f, 3).normalized, V3(f, 7))));
        Api("native/matrixInverse", 16, f => F(Matrix4x4.Inverse(M(f, 0))));
        Api("native/matrixDeterminant", 16, f => new[] { M(f, 0).determinant });
        Api("native/matrixDecompose", 10, f =>
        {
            var m = Matrix4x4.TRS(V3(f, 0), Q(f, 3).normalized, V3(f, 7));
            var result = new List<float>();
            result.AddRange(F(m.rotation));
            result.AddRange(F(m.lossyScale));
            return result.ToArray();
        });
        Api("native/boundsPoint", 9, f =>
        {
            var bounds = new Bounds(V3(f, 0), V3(f, 3));
            var point = V3(f, 6);
            var result = new List<float> { bounds.SqrDistance(point), bounds.Contains(point) ? 1f : 0f };
            result.AddRange(F(bounds.ClosestPoint(point)));
            return result.ToArray();
        });

        // Added after the native rows, so theirs keep the inputs they were recorded with.
        Api("api/polygonPlaneQuad", 15, f =>
        {
            bool made = GeometryUtility.TryCreatePlaneFromPolygon(new[] { V3(f, 0), V3(f, 3), V3(f, 6), V3(f, 9), V3(f, 12) }, out var plane);
            var result = new List<float> { made ? 1f : 0f };
            result.AddRange(F(plane));
            return result.ToArray();
        });
        Api("api/polygonPlaneDegenerate", 6, f =>
        {
            // Two vertices, and four on one line: an exactly zero Newell normal, or a small one where
            // rounding a + d * k moves a point off the line.
            bool two = GeometryUtility.TryCreatePlaneFromPolygon(new[] { V3(f, 0), V3(f, 3) }, out var first);
            Vector3 a = V3(f, 0), d = V3(f, 3);
            bool line = GeometryUtility.TryCreatePlaneFromPolygon(new[] { a, a + d, a + d * 2f, a + d * 3f }, out var second);
            var result = new List<float> { two ? 1f : 0f, line ? 1f : 0f };
            result.AddRange(F(first));
            result.AddRange(F(second));
            return result.ToArray();
        });

        // The branches random operands rarely reach: nearly equal and opposite rotations and
        // directions, degenerate lengths, gimbal lock, singular and pivoting matrices, and points
        // on a box's faces.
        Api("native/quaternionSlerpNear", 10, f =>
        {
            Quaternion a = Q(f, 0).normalized;
            Quaternion near = new Quaternion(a.x + f[4] * 1e-3f, a.y + f[5] * 1e-3f, a.z + f[6] * 1e-3f, a.w + f[7] * 1e-3f).normalized;
            Quaternion b = f[8] < 0f ? new Quaternion(-near.x, -near.y, -near.z, -near.w) : near;
            var result = new List<float>();
            result.AddRange(F(Quaternion.Slerp(a, b, Unit(f[9]))));
            result.AddRange(F(Quaternion.SlerpUnclamped(a, Q(f, 4).normalized, f[9])));
            result.AddRange(F(Quaternion.LerpUnclamped(a, new Quaternion(-a.x, -a.y, -a.z, -a.w), f[9])));
            result.AddRange(F(Quaternion.Lerp(Q(f, 0), Q(f, 4), f[9])));
            return result.ToArray();
        });
        Api("native/quaternionEdge", 8, f =>
        {
            Vector3 v = V3(f, 0), w = V3(f, 3);
            Vector3 perpendicular = Vector3.Cross(v, w) * 1e-4f;
            var result = new List<float>();
            result.AddRange(F(Quaternion.LookRotation(v, v * f[6])));
            result.AddRange(F(Quaternion.LookRotation(v * 1e-6f, w)));
            result.AddRange(F(Quaternion.LookRotation(Vector3.zero, w)));
            result.AddRange(F(Quaternion.FromToRotation(v, v * -Mathf.Abs(f[6]))));
            result.AddRange(F(Quaternion.FromToRotation(v, v * Mathf.Abs(f[6]))));
            result.AddRange(F(Quaternion.FromToRotation(v, -v + perpendicular)));
            result.AddRange(F(Quaternion.FromToRotation(v * 1e-6f, w)));
            result.AddRange(F(Quaternion.AngleAxis(f[7] * 40f, v * 1e-7f)));
            result.AddRange(F(Quaternion.AngleAxis(f[7] * 40f, Vector3.zero)));
            result.AddRange(F(Quaternion.Euler(90f + f[7] * 1e-3f, f[0] * 40f, f[1] * 40f).eulerAngles));
            result.AddRange(F(Quaternion.Euler(-90f + f[7] * 1e-5f, f[2] * 40f, f[3] * 40f).eulerAngles));
            result.AddRange(F(new Quaternion(f[0] * 1e-7f, f[1] * 1e-7f, f[2] * 1e-7f, f[3] * 1e-7f).eulerAngles));
            return result.ToArray();
        });
        Api("native/vector3Edge", 8, f =>
        {
            Vector3 v = V3(f, 0), w = V3(f, 3);
            float k = Mathf.Abs(f[6]), t = Unit(f[7]);
            var result = new List<float>();
            result.AddRange(F(Vector3.Slerp(v, v * -k, t)));
            result.AddRange(F(Vector3.Slerp(v, v * k, t)));
            result.AddRange(F(Vector3.Slerp(v * 1e-6f, w, t)));
            result.AddRange(F(Vector3.SlerpUnclamped(v, w, f[7])));
            result.AddRange(F(Vector3.RotateTowards(v, v * -k, t, k)));
            result.AddRange(F(Vector3.RotateTowards(v, v * k, t, k)));
            result.AddRange(F(Vector3.RotateTowards(v, w, 10f, k)));
            result.AddRange(F(Vector3.RotateTowards(v * 1e-6f, w, t, k)));
            result.AddRange(F(Vector3.RotateTowards(v, w * 1e-3f, t, k * 1e-3f)));
            return result.ToArray();
        });
        Api("native/matrixEdge", 16, f =>
        {
            Matrix4x4 m = M(f, 0);
            Matrix4x4 singular = m;
            singular.SetRow(2, Vector4.zero);
            Matrix4x4 pivoting = m;
            pivoting.m00 = 0f;
            pivoting.m11 = 0f;
            Quaternion q = Q(f, 4).normalized;
            Vector3 t = V3(f, 8);
            float s = Mathf.Abs(f[11]);
            var result = new List<float>();
            result.AddRange(F(singular.inverse));
            result.AddRange(F(pivoting.inverse));
            result.AddRange(F(Matrix4x4.TRS(t, q, V3(f, 12)).inverse));
            result.AddRange(F(Matrix4x4.Perspective(f[15] * 10f + 60f, 1.5f, 0.3f, 1000f).inverse));
            result.AddRange(F(Matrix4x4.TRS(t, q, Vector3.one).rotation));
            result.AddRange(F(Matrix4x4.TRS(t, q, new Vector3(s, s, s)).rotation));
            result.AddRange(F(Matrix4x4.TRS(t, q, new Vector3(s, 0f, s)).rotation));
            result.AddRange(F(Matrix4x4.TRS(t, q, new Vector3(-s, -s, -s)).rotation));
            result.AddRange(F(m.rotation));
            result.AddRange(F(m.lossyScale));
            result.AddRange(F(singular.rotation));
            return result.ToArray();
        });
        Api("native/boundsEdge", 9, f =>
        {
            Vector3 center = V3(f, 0), extents = V3(f, 3), offset = V3(f, 6);
            var box = new Bounds(center, extents * 2f);
            var flat = new Bounds(center, new Vector3(extents.x * 2f, 0f, extents.z * 2f));
            var result = new List<float>();
            foreach (var point in new[] { box.max, box.min, center + extents * 0.5f, center + new Vector3(offset.x, extents.y, offset.z) })
            {
                result.Add(box.SqrDistance(point));
                result.Add(box.Contains(point) ? 1f : 0f);
                result.AddRange(F(box.ClosestPoint(point)));
            }
            result.Add(flat.SqrDistance(center + offset));
            result.AddRange(F(flat.ClosestPoint(center + offset)));
            result.Add(flat.Contains(new Vector3(center.x, center.y, center.z)) ? 1f : 0f);
            return result.ToArray();
        });
    }

    // ------------------------------------------------------------------ randomized operands

    const int Samples = 24;
    static uint s_State = 2463534242u;

    static uint Next()
    {
        s_State ^= s_State << 13;
        s_State ^= s_State >> 17;
        s_State ^= s_State << 5;
        return s_State;
    }

    /// <summary>A float with a random sign, mantissa and an exponent in [-8, 8], built from its bits.</summary>
    static float RandomFloat()
    {
        uint bits = Next();
        uint exponent = 127u - 8u + (bits >> 8) % 17u;
        uint value = (bits & 0x80000000u) | (exponent << 23) | (Next() & 0x7FFFFFu);
        return BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
    }

    delegate float Shape(float a, float b, float c, float d);

    void Fuzz()
    {
        var operands = new float[Samples * 16];
        for (int i = 0; i < operands.Length; i++) operands[i] = RandomFloat();
        var shapes = new (string, Shape)[]
        {
            ("fuzz/products", Controls.Products), ("fuzz/sum4", Controls.Sum4), ("fuzz/mulDiv", Controls.MulDiv),
            ("fuzz/mixed", Controls.Mixed), ("fuzz/horner", Controls.Horner), ("fuzz/lerp", Controls.Lerp),
            ("fuzz/localChain", Controls.LocalChain), ("fuzz/distance", Controls.Distance),
            ("fuzz/greater", Controls.ProductGreater),
            ("fuzz/mathfLerp", Controls.MathfLerp), ("fuzz/mathfInverseLerp", Controls.MathfInverseLerp),
            ("fuzz/mathfSmoothStep", Controls.MathfSmoothStep), ("fuzz/mathfMoveTowards", Controls.MathfMoveTowards),
            ("fuzz/mathfRepeat", Controls.MathfRepeat), ("fuzz/mathfPingPong", Controls.MathfPingPong),
            ("fuzz/mathfSmoothDamp", Controls.MathfSmoothDamp),
        };
        foreach (var (key, shape) in shapes)
        {
            var values = new List<string>();
            for (int i = 0; i < Samples; i++)
                values.Add(Bits(shape(operands[i * 4], operands[i * 4 + 1], operands[i * 4 + 2], operands[i * 4 + 3])));
            Value(key, string.Join(" ", values));
        }
        var vectors = new (string, Func<Vector3, Vector3, Vector3>)[]
        {
            ("fuzz/vector3Cross", Vector3.Cross), ("fuzz/vector3Normalize", (u, v) => Vector3.Normalize(u)),
            ("fuzz/vector3Lerp", (u, v) => Vector3.Lerp(u, v, 0.375f)), ("fuzz/vector3Project", Vector3.Project),
            ("fuzz/vector3Reflect", Vector3.Reflect),
            ("fuzz/vector3Scalars", (u, v) => new Vector3(Vector3.Dot(u, v), u.magnitude, Vector3.Distance(u, v))),
            ("fuzz/quaternionRotate", (u, v) => new Quaternion(u.x, u.y, u.z, v.x) * v),
            ("fuzz/matrixPoint", (u, v) => Matrix4x4.TRS(u, Quaternion.identity, v).MultiplyPoint3x4(u + v)),
        };
        foreach (var (key, function) in vectors)
        {
            var values = new List<string>();
            for (int i = 0; i < Samples; i++)
            {
                int at = Samples * 4 + i * 6;
                var result = function(new Vector3(operands[at], operands[at + 1], operands[at + 2]),
                    new Vector3(operands[at + 3], operands[at + 4], operands[at + 5]));
                values.Add(Bits(result.x) + "," + Bits(result.y) + "," + Bits(result.z));
            }
            Value(key, string.Join(" ", values));
        }
    }
}

public interface IFloatAdder
{
    float Add(float x, float y);
}

public class FloatHolder : IFloatAdder
{
    public static readonly FloatHolder Instance = new FloatHolder();
    public float Field;
    public float AddInstance(float x, float y) => x + y;
    public virtual float Echo(float x) => x;
    public float Add(float x, float y) => x + y;
}

public struct FloatPair
{
    public float X;
    public float Y;
}

public static class FloatBox<T>
{
    public static T Value;
    public static T Echo(T value) => value;
}

public static class Controls
{
    public static bool Flag = true;
    public static int Count;
    public static int Iterations = 1;
    public static float Static;
    public static volatile float Volatile;
    public static readonly float[] Array = new float[1];
    public static float Property { get; set; }
    static object s_Object;
    static readonly Func<float, float, float> s_Add = (x, y) => x + y;

    [MethodImpl(MethodImplOptions.NoInlining)] static void Call() => Count++;
    static void SmallCall() => Count++;
    [MethodImpl(MethodImplOptions.NoInlining)] static float EchoNoInlining(float x) => x;
    [MethodImpl(MethodImplOptions.NoInlining)] static float AddNoInlining(float x, float y) => x + y;
    static float Echo(float x) => x;
    static float AddSmall(float x, float y) => x + y;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static float EchoAggressive(float x) => x;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static float AddAggressive(float x, float y) => x + y;
    static float AddThroughLocal(float x, float y) { float t = x + y; return t; }
    static float Add3(float x, float y, float z) => x + y + z;
    [MethodImpl(MethodImplOptions.NoInlining)] static T PassNoInlining<T>(T value) => value;
    static T Pass<T>(T value) => value;

    public static float Add(float a, float b, float c) => a + b + c;
    public static float Subtract(float a, float b, float c) => a - b + c;
    public static float Multiply(float a, float b, float c) => a * b + c;
    public static float Divide(float a, float b, float c) => a / b - c;
    public static float Remainder(float a, float b, float c) => (a + b) % c;
    public static float Negate(float a, float b, float c) => -(a + b) - c;
    public static float Product3(float a, float b, float c) => a * b * c;

    public static double ToDouble(float a, float b) => (double)(a + b) - 16777216.0;
    public static int ToInt(float a, float b) => (int)(a + b) - 16777216;
    public static int CheckedInt(float a, float b) => checked((int)(a + b) - 16777216);
    public static long ToLong(float a, float b) => (long)(a + b) - 16777216L;
    public static double MixedDouble(float a, float b, float c) => (a + b) + (double)c;
    public static float IntToFloat(int i, float c) => i + c;
    public static float LongToFloat(long i, float c) => i + c;
    public static float UIntToFloat(uint i, float c) => i + c;
    public static float DoubleToFloat(double d, float c) => (float)d + c;
    public static float Cast(float a, float b, float c) => (float)(a + b) + c;

    public static bool Equal(float a, float b) => a + b == a;
    public static bool Greater(float a, float b) => a + b > a;
    public static bool CastEqual(float a, float b) => (float)(a + b) == a;
    public static bool LocalEqual(float a, float b) { float t = a + b; return t == a; }
    public static float Branch(float a, float b, float c) { if (a + b > a) return 1f; return 0f; }
    public static float TernaryValue(float a, float b, float c) => (Flag ? a + b : a - b) + c;
    public static float TernaryLocal(float a, float b, float c) { float t = Flag ? a + b : a - b; return t + c; }

    public static float LocalSingle(float a, float b, float c) { float t = a + b; return t + c; }
    public static float LocalTwice(float a, float b, float c) { float t = a + b; return (t + c) + (t - t); }
    public static float LocalCall(float a, float b, float c) { float t = a + b; Call(); return t + c; }
    public static float LocalInlinedCall(float a, float b, float c) { float t = a + b; SmallCall(); return t + c; }
    public static float LocalBranch(float a, float b, float c) { float t = a + b; if (Flag) Count++; return t + c; }
    public static float LocalCompound(float a, float b, float c) { float t = a; t += b; return t + c; }
    public static float LocalIncrement(float a, float b, float c) { float t = a; t++; return t + c; }
    public static float LocalLoopBody(float a, float b, float c) { float t = a; for (int i = 0; i < Iterations; i++) { t = t + b; t = t + c; } return t; }
    public static float LocalLoopExit(float a, float b, float c) { float t = a; for (int i = 0; i < Iterations; i++) t = t + b; return t + c; }
    public static float LocalAddressTaken(float a, float b, float c) { float t = a + b; ref float r = ref t; return r + c; }
    public static float LocalCopied(float a, float b, float c) { float t = a + b; float u = t; return u + c + (t - u); }
    public static float ArgumentStore(float a, float b, float c) { a = a + b; return a + c; }
    public static float ArgumentStoreCall(float a, float b, float c) { a = a + b; Call(); return a + c; }

    public static float StaticField(float a, float b, float c) { Static = a + b; return Static + c; }
    public static float VolatileField(float a, float b, float c) { Volatile = a + b; return Volatile + c; }
    public static float InstanceField(float a, float b, float c) { var h = FloatHolder.Instance; h.Field = a + b; return h.Field + c; }
    public static float ArrayElement(float a, float b, float c) { Array[0] = a + b; return Array[0] + c; }
    public static float RefLocal(float a, float b, float c) { ref float r = ref Array[0]; r = a + b; return r + c; }
    public static float SpanElement(float a, float b, float c) { Span<float> s = Array; s[0] = a + b; return s[0] + c; }
    public static float StackSpan(float a, float b, float c) { Span<float> s = stackalloc float[1]; s[0] = a + b; return s[0] + c; }
    public static float Box(float a, float b, float c) { object o = a + b; return (float)o + c; }
    public static float BoxField(float a, float b, float c) { s_Object = a + b; return (float)s_Object + c; }
    public static float BoxApart(float a, float b, float c) { object o = a + b; Count++; return (float)o + c; }
    public static float FieldCompound(float a, float b, float c) { Static = a; Static += b; return Static + c; }
    public static float ArrayCompound(float a, float b, float c) { Array[0] = a; Array[0] += b; return Array[0] + c; }
    public static float PropertyStore(float a, float b, float c) { Property = a + b; return Property + c; }
    public static float StructLocal(float a, float b, float c) { Vector3 v = default; v.x = a + b; return v.x + c; }
    public static float StructConstructor(float a, float b, float c) { var v = new Vector3(a + b, 0f, 0f); return v.x + c; }
    public static float StructInitializer(float a, float b, float c) { var p = new FloatPair { X = a + b }; return p.X + c; }

    public static float NoInliningArgument(float a, float b, float c) => EchoNoInlining(a + b) + c;
    public static float NoInliningReturn(float a, float b, float c) => AddNoInlining(a, b) + c;
    public static float SmallArgument(float a, float b, float c) => Echo(a + b) + c;
    public static float SmallReturn(float a, float b, float c) => AddSmall(a, b) + c;
    public static float AggressiveArgument(float a, float b, float c) => EchoAggressive(a + b) + c;
    public static float AggressiveReturn(float a, float b, float c) => AddAggressive(a, b) + c;
    public static float LocalReturn(float a, float b, float c) => AddThroughLocal(a, b) + c;
    public static float Body(float a, float b, float c) => Add3(a, b, c);
    public static float Instance(float a, float b, float c) => FloatHolder.Instance.AddInstance(a, b) + c;
    public static float Virtual(float a, float b, float c) => FloatHolder.Instance.Echo(a + b) + c;
    public static float Interface(float a, float b, float c) => ((IFloatAdder)FloatHolder.Instance).Add(a, b) + c;
    public static float DelegateCall(float a, float b, float c) => s_Add(a, b) + c;
    public static float GenericNoInlining(float a, float b, float c) => PassNoInlining(a + b) + c;
    public static float GenericMethod(float a, float b, float c) => Pass(a + b) + c;
    public static float GenericField(float a, float b, float c) { FloatBox<float>.Value = a + b; return FloatBox<float>.Value + c; }
    public static float Nullable(float a, float b, float c) { float? n = a + b; return n.Value + c; }
    public static float GenericClassMethod(float a, float b, float c) => FloatBox<float>.Echo(a + b) + c;

    public static float Closure(float a, float b, float c) { float t = a + b; Func<float> f = () => t; return t + c + f() * 0f; }
    public static float LambdaBody(float a, float b, float c) { Func<float, float, float, float> f = (x, y, z) => x + y + z; return f(a, b, c); }
    static IEnumerable<float> Sequence(float a, float b, float c) { float t = a + b; yield return 0f; yield return t + c; }
    public static float Iterator(float a, float b, float c) { var e = Sequence(a, b, c).GetEnumerator(); e.MoveNext(); e.MoveNext(); return e.Current; }
    static async Task<float> Later(float a, float b, float c) { float t = a + b; await Task.CompletedTask; return t + c; }
    public static float Async(float a, float b, float c) => Later(a, b, c).Result;
    public static float TryFinally(float a, float b, float c) { float r; try { r = a + b; } finally { Count++; } return r + c; }

    public static float MathfAbs(float a, float b, float c) => Mathf.Abs(a + b) + c;
    public static float MathfMax(float a, float b, float c) => Mathf.Max(a + b, 0f) + c;
    public static float MathfMin(float a, float b, float c) => Mathf.Min(a + b, 1e30f) + c;
    public static float MathfClamp(float a, float b, float c) => Mathf.Clamp(a + b, 0f, 1e30f) + c;
    public static float MathfFloor(float a, float b, float c) => Mathf.Floor(a + b) + c;
    public static float MathfSqrt(float a, float b, float c) => Mathf.Sqrt(a + b) + c;
    public static float MathSqrt(float a, float b, float c) => (float)Math.Sqrt(a + b) + c;
    public static float MathAbs(float a, float b, float c) => Math.Abs(a + b) + c;
    public static float MathMax(float a, float b, float c) => Math.Max(a + b, 0f) + c;
    public static float MathFAbs(float a, float b, float c) => MathF.Abs(a + b) + c;
    public static float MathFSqrt(float a, float b, float c) => MathF.Sqrt(a + b) + c;
    public static float Vector3Dot(float a, float b, float c) => Vector3.Dot(new Vector3(a, b, 0f), new Vector3(a, c, 0f));
    public static float Vector3SqrMagnitude(float a, float b, float c) => new Vector3(a, b, 0f).sqrMagnitude;
    public static float Vector3Magnitude(float a, float b, float c) => new Vector3(a, b, 0f).magnitude;
    public static float Vector3AddChain(float a, float b, float c) => (new Vector3(a, 0f, 0f) + new Vector3(b, 0f, 0f) + new Vector3(c, 0f, 0f)).x;
    public static float Vector3Components(float a, float b, float c) { var u = new Vector3(a, b, c); return u.x + u.y + u.z; }

    public static float Products(float a, float b, float c, float d) => a * b + c * d;
    public static float Sum4(float a, float b, float c, float d) => a + b + c + d;
    public static float MulDiv(float a, float b, float c, float d) => a * b / c;
    public static float Mixed(float a, float b, float c, float d) => (a - b) * (c + d);
    public static float Horner(float a, float b, float c, float d) => ((a * d + b) * d + c) * d;
    public static float Lerp(float a, float b, float c, float d) => a + (b - a) * c;
    public static float LocalChain(float a, float b, float c, float d) { float t = a * b; t += c * d; return t * a; }
    public static float Distance(float a, float b, float c, float d) => Mathf.Sqrt((a - c) * (a - c) + (b - d) * (b - d));
    public static float ProductGreater(float a, float b, float c, float d) => a * b + c > c * d + a ? 1f : 0f;
    public static float MathfLerp(float a, float b, float c, float d) => Mathf.Lerp(a, b, Mathf.Abs(c) % 1f);
    public static float MathfInverseLerp(float a, float b, float c, float d) => Mathf.InverseLerp(a, b, c);
    public static float MathfSmoothStep(float a, float b, float c, float d) => Mathf.SmoothStep(a, b, Mathf.Abs(c) % 1f);
    public static float MathfMoveTowards(float a, float b, float c, float d) => Mathf.MoveTowards(a, b, Mathf.Abs(c));
    public static float MathfRepeat(float a, float b, float c, float d) => Mathf.Repeat(a, Mathf.Abs(b));
    public static float MathfPingPong(float a, float b, float c, float d) => Mathf.PingPong(a, Mathf.Abs(b));
    public static float MathfSmoothDamp(float a, float b, float c, float d) { float v = d; return Mathf.SmoothDamp(a, b, ref v, Mathf.Abs(c) + 0.01f, Mathf.Infinity, 0.02f) + v; }
}
