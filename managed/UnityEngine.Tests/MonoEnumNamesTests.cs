using System;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Unbound;
using Unbound.Host;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The names enum values print with as the Unity 6000.0.71f1 Editor's Mono picks them among
    /// names that share a value (tests/projects/enumtext): the binary search of the sorted constants
    /// for the default format and Enum.GetName, the highest sorted constants first for [Flags] enums
    /// and "F", for game code's calls (redirected when its assembly loads).
    /// </summary>
    public sealed class MonoEnumNamesTests
    {
        const string Source = @"
using System;
using System.Text;
using UnityEngine;
public enum SortedAliases { A = 1, B = 2, C = 2, D = 3, E = 4 }
public enum ThreeUnsorted { M = 2, N = 2, L = 1 }
public enum ManyUnsorted
{
    V17 = 17, V3 = 3, V9 = 9, V3b = 3, V12 = 12, V1 = 1, V9b = 9, V20 = 20, V5 = 5, V3c = 3,
    V14 = 14, V0 = 0, V9c = 9, V7 = 7, V18 = 18, V2 = 2, V12b = 12, V11 = 11, V9d = 9, V6 = 6,
    V3d = 3, V15 = 15, V12c = 12, V4 = 4,
}
[Flags] public enum FlagAliases { None = 0, A = 1, Alias = 1, B = 2, AB = 3, C = 4, Both = 2 }
[Flags] public enum FlagsNoZero { One = 1, Two = 2, Uno = 1 }
public enum Signed { Neg = -1, Zero = 0, One = 1, MinusOne = -1, Min = int.MinValue }
public enum Longs : ulong { Small = 1, Big = ulong.MaxValue, Biggest = ulong.MaxValue, Mid = 1UL << 40 }
public struct Counter { public int Calls; public override string ToString() => ""calls "" + (++Calls); }
public static class Probe
{
    public static string Direct() => SortedAliases.C.ToString();
    public static string Field() => s_Field.ToString();
    static SortedAliases s_Field = SortedAliases.B;
    public static string Formats() { var c = SortedAliases.C; return c.ToString(""G"") + "" "" + c.ToString(""F"") + "" "" + c.ToString(""D"") + "" "" + c.ToString(""X""); }
    public static string Boxed() { object o = SortedAliases.C; return o.ToString(); }
    public static string AsEnum() { Enum e = SortedAliases.C; return e.ToString() + "" "" + e.ToString(""g""); }
    public static string Concat() => ""value "" + SortedAliases.C + "" "" + string.Concat((object)SortedAliases.C, ""|"");
    public static string Format() => string.Format(""{0} {0:D} {1:F} {2,3}|"", SortedAliases.C, FlagAliases.AB, SortedAliases.C);
    public static string Builder() => new StringBuilder().Append((object)SortedAliases.C).AppendFormat("" {0}"", SortedAliases.C).ToString();
    public static string Converted() => Convert.ToString((object)SortedAliases.C);
    public static string Lookups() => Enum.GetName(typeof(SortedAliases), 2) + "" "" + Enum.GetName(typeof(SortedAliases), (object)SortedAliases.C)
        + "" "" + typeof(SortedAliases).GetEnumName(2) + "" "" + Enum.Format(typeof(SortedAliases), 2, ""G"") + "" "" + Enum.Format(typeof(SortedAliases), 2, ""D"");
    public static string Generic<T>(T value) => value.ToString();
    public static string GenericEnum() => Generic(SortedAliases.C);
    public static string GenericClass() => Generic<string>(""text"");
    public static string GenericNull() => Generic<object>(null);
    public static string GenericFloat() => Generic(1849.9999f);
    public static string GenericStruct() { var counter = new Counter(); string first = Mutate(ref counter); return first + "" "" + counter.Calls; }
    static string Mutate<T>(ref T value) => value.ToString();
    public static string Joined() => string.Join("","", new[] { SortedAliases.B, SortedAliases.C }) + ""|"" + string.Join(';', new System.Collections.Generic.List<SortedAliases> { SortedAliases.C })
        + ""|"" + string.Join("","", new object[] { SortedAliases.C, 1849.9999f }) + ""|"" + string.Join("","", new[] { 1849.9999f }) + ""|"" + string.Join("","", new[] { ""a"", null, ""b"" })
        + ""|"" + string.Join("","", new System.Collections.Generic.List<int> { 1, 2 }) + ""|"" + string.Join("","", new SortedAliases?[] { SortedAliases.C, null });
    public static string NullableFloat() { float? some = 1849.9999f, none = null; return some.ToString() + ""|"" + none.ToString(); }
    public static string Nullable() { SortedAliases? some = SortedAliases.C, none = null; return some.ToString() + ""|"" + none.ToString() + ""|""; }
    public static string Others() => ThreeUnsorted.N.ToString() + "" "" + ManyUnsorted.V3.ToString() + "" "" + ManyUnsorted.V12.ToString()
        + "" "" + FlagAliases.A.ToString() + "" "" + (FlagAliases.A | FlagAliases.C).ToString() + "" "" + ((FlagAliases)0).ToString()
        + "" "" + ((FlagAliases)8).ToString() + "" "" + ((FlagsNoZero)0).ToString() + "" "" + ((FlagsNoZero)3).ToString()
        + "" "" + Signed.Neg.ToString() + "" "" + ((Signed)2).ToString() + "" "" + Longs.Big.ToString() + "" "" + ((SortedAliases)9).ToString();
    public static string Engine() => CameraClearFlags.SolidColor.ToString() + "" "" + KeyCode.LeftApple.ToString() + "" "" + ParticleSystemBakeTextureOptions.Default.ToString()
        + "" "" + Enum.GetName(typeof(ParticleSystemBakeTextureOptions), 4) + "" "" + CollisionFlags.CollidedAbove.ToString();
}";

        static Assembly Redirected(OptimizationLevel optimization) =>
            WideFloatsTests.Load(NumberFormatShims.Redirect(WideFloatsTests.Emit(WideFloatsTests.Compilation(Source, optimization, "MonoEnumProbe", engine: true)).Image));

        static string Run(Assembly assembly, string method)
        {
            try { return (string)assembly.GetType("Probe")!.GetMethod(method)!.Invoke(null, null)!; }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }

        [Theory]
        [MemberData(nameof(WideFloatsTests.Modes), MemberType = typeof(WideFloatsTests))]
        public void GameCodePrintsMonosNameAmongAliases(OptimizationLevel optimization)
        {
            var probe = Redirected(optimization);
            Assert.Equal("C", Run(probe, "Direct"));
            Assert.Equal("C", Run(probe, "Field"));
            Assert.Equal("C C 2 00000002", Run(probe, "Formats"));
            Assert.Equal("C", Run(probe, "Boxed"));
            Assert.Equal("C C", Run(probe, "AsEnum"));
            Assert.Equal("value C C|", Run(probe, "Concat"));
            // (Unity's class library has no interpolation handler: game code's interpolations are string.Format.)
            Assert.Equal("C 2 AB   C|", Run(probe, "Format"));
            Assert.Equal("C C", Run(probe, "Builder"));
            Assert.Equal("C", Run(probe, "Converted"));
            Assert.Equal("C C C C 2", Run(probe, "Lookups"));
            Assert.Equal("C", Run(probe, "GenericEnum"));
            Assert.Equal("C||", Run(probe, "Nullable"));
            Assert.Equal("C,C|C|C,1850|1850|a,,b|1,2|C,", Run(probe, "Joined"));
            Assert.Equal("N V3b V12 Alias Alias, C None 8 0 Uno, Two Neg 2 Big 9", Run(probe, "Others"));
            Assert.Equal("SolidColor LeftMeta Default PerVertex Above", Run(probe, "Engine"));
        }

        /// <summary>A ToString called on a generic parameter's value prints a float as Mono does
        /// and keeps every other type's own text and semantics: a class's (null throws) and a
        /// struct's own, which still runs on the caller's storage.</summary>
        [Theory]
        [MemberData(nameof(WideFloatsTests.Modes), MemberType = typeof(WideFloatsTests))]
        public void GenericToStringKeepsOtherTypes(OptimizationLevel optimization)
        {
            var probe = Redirected(optimization);
            Assert.Equal("text", Run(probe, "GenericClass"));
            Assert.Throws<NullReferenceException>(() => Run(probe, "GenericNull"));
            Assert.Equal("1850", Run(probe, "GenericFloat"));
            Assert.Equal("1850|", Run(probe, "NullableFloat"));
            Assert.Equal("calls 1 1", Run(probe, "GenericStruct"));
        }

        enum Unsorted16 { P9 = 9, P1 = 1, P5 = 5, P5b = 5, P3 = 3 }

        /// <summary>The engine's own texts (the log) use the same names.</summary>
        [Fact]
        public void EngineTextsUseMonosNames()
        {
            Assert.Equal("SolidColor", MonoNumberFormat.ObjectToString(CameraClearFlags.SolidColor));
            Assert.Equal("SolidColor", MonoNumberFormat.ToString((Enum)CameraClearFlags.Color));
            // Unsorted constants with one alias: insertion sort keeps declaration order (P5, P5b), the
            // search over five values lands on the middle one.
            Assert.Equal("P5", MonoNumberFormat.ToString((Enum)Unsorted16.P5b));
            Assert.Equal("P5", MonoNumberFormat.GetName(typeof(Unsorted16), 5));
            Assert.Null(MonoNumberFormat.GetName(typeof(Unsorted16), 4));
            Assert.Equal("4", MonoNumberFormat.ToString((Enum)(Unsorted16)4));
        }
    }
}
