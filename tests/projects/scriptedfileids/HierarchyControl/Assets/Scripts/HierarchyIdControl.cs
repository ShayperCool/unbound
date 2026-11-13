using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEditor;
public sealed class HierarchyIdControl : MonoBehaviour
{
    static readonly List<string> Rows=new List<string>();
    static void Put(string k,object v) => Rows.Add("{\"k\":\""+k+"\",\"v\":\""+Convert.ToString(v,CultureInfo.InvariantCulture)+"\"}");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin() { if(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT")!=null)new GameObject("Hierarchy identity control").AddComponent<HierarchyIdControl>(); }
    void Start()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imports/Root.hierarchyid");
        Put("root/found",root!=null);
        if(root!=null) { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root,out string guid,out long id); Put("root/id",id); Put("root/name",root.name); }
        Put("complete",1);
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"),Rows); EditorApplication.Exit(0);
    }
}
