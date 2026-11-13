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
        Listing("deep","Assets/Imports/Deep.hierarchyid");
        Listing("script","Assets/Imports/Script.hierarchyid");
        Put("complete",1);
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"),Rows); EditorApplication.Exit(0);
    }

    // Every object of an asset by its hierarchy path and class: its local file id, whether
    // LoadAllAssetsAtPath lists it, and whether a GameObject is the main object.
    static void Listing(string prefix,string path)
    {
        var main=AssetDatabase.LoadMainAssetAtPath(path);
        Put(prefix+"/main",main==null?"null":main.GetType().Name+" "+main.name);
        var listed=new HashSet<UnityEngine.Object>(AssetDatabase.LoadAllAssetsAtPath(path));
        var seen=new SortedDictionary<string,string>(StringComparer.Ordinal);
        void Add(UnityEngine.Object o,string key)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long id);
            string k=key;
            for(int i=1;seen.ContainsKey(k);i++) k=key+"#"+i;
            seen[k]=id+" "+(listed.Contains(o)?"listed":"unlisted");
        }
        void Walk(Transform t,string parent)
        {
            string p=parent+"/"+t.name;
            Add(t.gameObject,prefix+"/"+p+"/GameObject");
            foreach(var c in t.GetComponents<Component>()) Add(c,prefix+"/"+p+"/"+c.GetType().Name);
            for(int i=0;i<t.childCount;i++) Walk(t.GetChild(i),p);
        }
        foreach(var o in listed)
        {
            if(o is GameObject g && g.transform.parent==null) Walk(g.transform,"");
            else if(!(o is GameObject) && !(o is Component)) Add(o,prefix+"/asset/"+o.GetType().Name+"/"+o.name);
        }
        foreach(var row in seen) Put(row.Key,row.Value);
        Put(prefix+"/listed",listed.Count);
    }
}
