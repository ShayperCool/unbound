using System;
using System.Runtime.CompilerServices;
public static class FloatStudy
{
    static volatile float stored;
    [MethodImpl(MethodImplOptions.NoInlining)] static float Plain(int i) { return i / 37f - 2f; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Cast(int i) { return (float)(i / 37f) - 2f; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Field(int i) { stored=i / 37f; return stored-2f; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Product(float x,float y,float z) { return x*y+z; }
    [MethodImpl(MethodImplOptions.NoInlining)] static float Sum(float x,float y,float z) { return x+y+z; }
    static string Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value),0).ToString("X8"); }
    public static void Main()
    {
        foreach(int i in new[]{20,73,16777219})Console.WriteLine(i+" plain="+Bits(Plain(i))+" cast="+Bits(Cast(i))+" field="+Bits(Field(i)));
        Console.WriteLine("sum="+Bits(Sum(16777216f,1f,-16777216f)));
        Console.WriteLine("product="+Bits(Product(4097f,4097f,-16785408f)));
    }
}
