using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
public static class ContactLayout
{
    static StreamWriter file;
    static void Type(Type t,string prefix)
    {
        file.WriteLine(prefix+" size="+Marshal.SizeOf(t));
        foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            file.WriteLine(prefix+"."+f.Name+" type="+f.FieldType.FullName+" offset="+Marshal.OffsetOf(t,f.Name));
            if(f.FieldType.IsValueType && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType!=typeof(IntPtr))Type(f.FieldType,prefix+"."+f.Name);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string path=Environment.GetEnvironmentVariable("CONTACT_LAYOUT_OUT");if(string.IsNullOrEmpty(path))return;
        file=new StreamWriter(path);Type(typeof(PhysicsScene),"PhysicsScene");file.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
