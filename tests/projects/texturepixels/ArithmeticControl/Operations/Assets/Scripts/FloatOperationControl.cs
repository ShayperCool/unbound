using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
public sealed class FloatOperationControl : MonoBehaviour
{
    static readonly List<string> Rows = new List<string>();
    static volatile float stored;
    static readonly float[] values = new float[1];
    static void Put(string k, string v) => Rows.Add("{\"k\":\""+k+"\",\"v\":\""+v+"\"}");
    static string Bits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v),0).ToString("X8");
    [MethodImpl(MethodImplOptions.NoInlining)] static float Add(float a,float b,float c) => a+b+c;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Sub(float a,float b,float c) => a-b-c;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Mul(float a,float b,float c) => a*b+c;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Div(float a,float b,float c) => a/b-c;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Rem(float a,float b,float c) => a%b+c;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Cast(float a,float b,float c) => (float)(a/b)-c;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Local(float a,float b,float c) { float x=a/b; return x-c; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Field(float a,float b,float c) { stored=a/b; return stored-c; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Array(float a,float b,float c) { values[0]=a/b; return values[0]-c; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Divide(float a,float b) => a/b;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Call(float a,float b,float c) => Divide(a,b)-c;
    [MethodImpl(MethodImplOptions.NoInlining)] static bool Compare(float a,float b) => a+b==a;
    [MethodImpl(MethodImplOptions.NoInlining)] static bool CompareCast(float a,float b) => (float)(a+b)==a;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin() { if(Environment.GetEnvironmentVariable("FLOAT_ARITHMETIC_OUT")!=null)new GameObject("Float operations").AddComponent<FloatOperationControl>(); }
    void Start()
    {
        var cases=new[]{new[]{16777216f,1f,-16777216f},new[]{4097f,4097f,-16785408f},new[]{20f,37f,2f},new[]{1.25f,2f,0.5f}};
        for(int i=0;i<cases.Length;i++)
        {
            var v=cases[i]; float a=v[0],b=v[1],c=v[2]; string k="case"+i+"/";
            Put(k+"add",Bits(Add(a,b,c))); Put(k+"sub",Bits(Sub(a,b,c))); Put(k+"mul",Bits(Mul(a,b,c))); Put(k+"div",Bits(Div(a,b,c))); Put(k+"rem",Bits(Rem(a,b,c)));
            Put(k+"cast",Bits(Cast(a,b,c))); Put(k+"local",Bits(Local(a,b,c))); Put(k+"field",Bits(Field(a,b,c))); Put(k+"array",Bits(Array(a,b,c))); Put(k+"call",Bits(Call(a,b,c))); Put(k+"compare",Compare(a,b).ToString()); Put(k+"compareCast",CompareCast(a,b).ToString());
        }
        foreach(string name in new[]{"Add","Sub","Mul","Div","Local","Cast","Compare","CompareCast"})Put("gameIL/"+name,Convert.ToBase64String(typeof(FloatOperationControl).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).GetMethodBody().GetILAsByteArray()));
        Put("codeOptimization",UnityEditor.Compilation.CompilationPipeline.codeOptimization.ToString()); Put("complete","1");
        File.WriteAllLines(Environment.GetEnvironmentVariable("FLOAT_ARITHMETIC_OUT"),Rows); EditorApplication.Exit(0);
    }
}
