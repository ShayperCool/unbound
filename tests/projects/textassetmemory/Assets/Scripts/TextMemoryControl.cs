using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEditor;
public sealed class TextMemoryControl : MonoBehaviour
{
    static readonly List<string> Rows = new List<string>();
    static void Put(string key, long value) => Rows.Add("{\"k\":\""+key+"\",\"v\":\""+value.ToString(CultureInfo.InvariantCulture)+"\"}");
    static void Size(string key, UnityEngine.Object value) => Put(key, Profiler.GetRuntimeMemorySizeLong(value));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT")!=null)
            new GameObject("Text memory control").AddComponent<TextMemoryControl>();
    }
    void Start()
    {
        foreach(int length in new[]{0,1,3,15,16,30,31,32,33,1000})
        {
            var ascii=new TextAsset(new string('x',length));
            Size("ascii/"+length,ascii);
            DestroyImmediate(ascii);
            var unicode=new TextAsset(new string('\u03a9',length));
            Size("unicode/"+length,unicode);
            DestroyImmediate(unicode);
        }
        foreach(int length in new[]{0,31,32,33,1000})
        {
            var binary=new TextAsset(new byte[length]);
            Size("binary/"+length,binary);
            var copy=Instantiate(binary);
            Size("clone/"+length,copy);
            DestroyImmediate(binary);
            Size("cloneAfterDestroy/"+length,copy);
            DestroyImmediate(copy);
        }
        var named=new TextAsset(new string('x',32));
        Size("name/initial",named);
        named.name=new string('n',1000);
        Size("name/long",named);
        named.name="short";
        Size("name/shortAfterLong",named);
        var textValue=named.text;
        var byteValue=named.bytes;
        Size("name/afterReads",named);
        DestroyImmediate(named);
        Put("complete",1);
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"),Rows);
        EditorApplication.Exit(0);
    }
}
