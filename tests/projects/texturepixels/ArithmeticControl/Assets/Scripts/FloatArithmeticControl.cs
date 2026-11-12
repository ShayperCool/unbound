using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

public sealed class FloatArithmeticControl : MonoBehaviour
{
    static volatile float stored;
    static string output;
    static readonly List<string> Values = new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        output = Environment.GetEnvironmentVariable("FLOAT_ARITHMETIC_OUT");
        if (output != null) new GameObject("Float arithmetic control").AddComponent<FloatArithmeticControl>();
    }
    static void Put(string key, string value) => Values.Add("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}");
    static string Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("X8");
    [MethodImpl(MethodImplOptions.NoInlining)] static float Red(int i) => i / 37f - 2f;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Green(int i) => 1.5f - i / 91f;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Local(int i) { float x = i / 37f; return x - 2f; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Cast(int i) => (float)(i / 37f) - 2f;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Field(int i) { stored = i / 37f; return stored - 2f; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Divide(int i) => i / 37f;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Subtract(float value) => value - 2f;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Call(int i) => Subtract(Divide(i));
    [MethodImpl(MethodImplOptions.NoInlining)] static float ExplicitDouble(int i) => (float)((double)i / 37d - 2d);

    void Start()
    {
        foreach (int i in new[] { 20, 73, 92, 97, 12345, 16777217, 16777219, -20 })
        {
            string key = "i" + i + ".";
            Put(key + "red", Bits(Red(i)));
            Put(key + "green", Bits(Green(i)));
            Put(key + "local", Bits(Local(i)));
            Put(key + "cast", Bits(Cast(i)));
            Put(key + "field", Bits(Field(i)));
            Put(key + "call", Bits(Call(i)));
            Put(key + "explicitDouble", Bits(ExplicitDouble(i)));
        }
        // This is the probe's own game method, including its numeric conversions.
        foreach (string name in new[] { "Red", "Green", "Local", "Cast" })
        {
            var method = typeof(FloatArithmeticControl).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Put("gameIL." + name.ToLowerInvariant(), Convert.ToBase64String(method.GetMethodBody().GetILAsByteArray()));
        }
#if UNITY_EDITOR
        Put("codeOptimization", UnityEditor.Compilation.CompilationPipeline.codeOptimization.ToString());
#endif
        Values.Sort(StringComparer.Ordinal);
        Put("complete", "1");
        File.WriteAllLines(output, Values);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
