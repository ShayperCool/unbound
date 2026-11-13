using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEditor;
public sealed class MemoryScopeControl : MonoBehaviour
{
    static readonly List<string> Rows = new List<string>();
    static void Put(string key, object value) => Rows.Add("{\"k\":\""+key+"\",\"v\":\""+Convert.ToString(value,CultureInfo.InvariantCulture)+"\"}");
    static void Size(string key, UnityEngine.Object value) => Put(key, Profiler.GetRuntimeMemorySizeLong(value));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin() { if(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT")!=null)new GameObject("Memory scope").AddComponent<MemoryScopeControl>(); }
    void Start()
    {
        try
        {
            var root=new GameObject("Root");
            foreach(int capacity in new[]{1,32,128}) { root.transform.hierarchyCapacity=capacity; Size("transform/capacity"+capacity,root.transform); Put("capacity/read"+capacity,root.transform.hierarchyCapacity); }
            var child=new GameObject("Child"); Size("transform/childDetached",child.transform); child.transform.SetParent(root.transform,false); Size("transform/childParented",child.transform); Size("transform/parentWithChild",root.transform);
            child.transform.SetParent(null,false); Size("transform/childDetachedAgain",child.transform); Size("transform/parentAfterDetach",root.transform);
            foreach(int length in new[]{0,1,3,15,16,30,31,32,33,1000}) { var text=new TextAsset(new string('x',length)); Size("text/ascii"+length,text); DestroyImmediate(text); var wide=new TextAsset(new string('\u03a9',length)); Size("text/unicode"+length,wide); DestroyImmediate(wide); }
            foreach(string shaderName in new[]{"Unlit/Color","Standard"})
            {
                var shader=Shader.Find(shaderName); var material=new Material(shader); Size("material/"+shaderName+"/new",material); material.SetFloat("_ProbeExtra",2); Size("material/"+shaderName+"/extraFloat",material); var copy=new Material(material); Size("material/"+shaderName+"/copy",copy); copy.SetFloat("_ProbeExtra",3); Size("material/"+shaderName+"/copyEdited",copy); Size("material/"+shaderName+"/originalAfterEdit",material); DestroyImmediate(copy); DestroyImmediate(material);
            }
            var named=new TextAsset(new string('x',32)); Size("text/name/initial",named); named.name=new string('n',1000); Size("text/name/long",named); named.name="short"; Size("text/name/shortAfterLong",named); var textValue=named.text; var byteValue=named.bytes; Size("text/name/afterReads",named); DestroyImmediate(named);
            foreach(int length in new[]{0,31,32,33,1000}) { var binary=new TextAsset(new byte[length]); Size("text/binary"+length,binary); var copy=Instantiate(binary); Size("text/binaryClone"+length,copy); DestroyImmediate(binary); Size("text/cloneAfterDestroy"+length,copy); DestroyImmediate(copy); }
            DestroyImmediate(child); DestroyImmediate(root); Put("complete",1);
        }
        catch(Exception e) { Put("error",e.GetType().Name); Debug.LogException(e); }
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"),Rows); EditorApplication.Exit(0);
    }
}
