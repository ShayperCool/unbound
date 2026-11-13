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
        Deep();
        Put("complete",1);
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"),Rows); EditorApplication.Exit(0);
    }

    // Every object of the deep asset by its hierarchy path and class: its local file id, whether
    // LoadAllAssetsAtPath lists it, and whether a GameObject is the main object.
    static void Deep()
    {
        const string path="Assets/Imports/Deep.hierarchyid";
        var main=AssetDatabase.LoadMainAssetAtPath(path);
        Put("deep/main",main==null?"null":main.GetType().Name+" "+main.name);
        var listed=new HashSet<UnityEngine.Object>(AssetDatabase.LoadAllAssetsAtPath(path));
        var seen=new SortedDictionary<string,string>(StringComparer.Ordinal);
        void Add(UnityEngine.Object o,string key)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long id);
            string k=key;
            for(int i=1;seen.ContainsKey(k);i++) k=key+"#"+i;
            seen[k]=id+" "+(listed.Contains(o)?"listed":"unlisted");
        }
        void Walk(Transform t,string prefix)
        {
            string p=prefix+"/"+t.name;
            Add(t.gameObject,"deep/"+p+"/GameObject");
            foreach(var c in t.GetComponents<Component>()) Add(c,"deep/"+p+"/"+c.GetType().Name);
            for(int i=0;i<t.childCount;i++) Walk(t.GetChild(i),p);
        }
        foreach(var o in listed)
        {
            if(o is GameObject g && g.transform.parent==null) Walk(g.transform,"");
            else if(!(o is GameObject) && !(o is Component)) Add(o,"deep/asset/"+o.GetType().Name+"/"+o.name);
        }
        foreach(var row in seen) Put(row.Key,row.Value);
        Put("deep/listed",listed.Count);
    }
}
