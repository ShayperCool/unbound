using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class SortingLookupProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("SortingLookupProbe").AddComponent<SortingLookupProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly PropertyInfo index = typeof(SortingGroup).GetProperty("index", All);
    static readonly MethodInfo lookup = typeof(SortingGroup).GetMethod("GetSortingGroupByIndex", All);
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key=key, value=Convert.ToString(value,CultureInfo.InvariantCulture) }));
    int Index(SortingGroup group) => (int)index.GetValue(group);
    SortingGroup Get(int id) => (SortingGroup)lookup.Invoke(null,new object[]{ id });
    string Name(int id) { var g=Get(id); return g == null ? "null" : g.name; }
    SortingGroup Group(string name, bool active=true)
    {
        var go=new GameObject(name);go.SetActive(active);return go.AddComponent<SortingGroup>();
    }
    void State(string key, params SortingGroup[] groups)
    {
        foreach(var g in groups) { Put(key+"."+g.name+".index",Index(g));Put(key+"."+g.name+".self",ReferenceEquals(Get(Index(g)),g)); }
        for(int i=0;i<8;i++)Put(key+".lookup."+i,Name(i));
    }
    void Start()
    {
        string path=Environment.GetEnvironmentVariable("SORTING_LOOKUP_OUT");if(string.IsNullOrEmpty(path))return;
        try
        {
            using(output=new StreamWriter(path)) {Record();Put("complete",1);}EditorApplication.Exit(0);
        }
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static string OnWorker(Func<object> action)
    {
        string result = null;
        var thread = new Thread(() => { try { result = Convert.ToString(action(), CultureInfo.InvariantCulture); }
            catch (Exception e) { while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException; result = e.GetType().Name+":"+e.Message; } });
        thread.Start(); thread.Join(); return result;
    }
    void Record()
    {
        int invalid=(int)typeof(SortingGroup).GetProperty("invalidSortingGroupID",All).GetValue(null);Put("invalid",invalid);
        foreach(int i in new[]{-1,int.MinValue,int.MaxValue,(1<<20)-1,(1<<20)-2,0,31})Put("initial.lookup."+i,Name(i));
        var a=Group("a");var b=Group("b");var c=Group("c",false);State("created",a,b,c);
        Put("thread.invalid",OnWorker(() => typeof(SortingGroup).GetProperty("invalidSortingGroupID",All).GetValue(null)));
        Put("thread.lookup",OnWorker(() => Get(0) == null));
        Put("thread.index",OnWorker(() => Index(a)));
        int originalA=Index(a),originalB=Index(b);
        a.enabled=false;State("disable-a",a,b,c);
        c.gameObject.SetActive(true);State("activate-c",a,b,c);
        a.enabled=true;State("enable-a",a,b,c);
        b.enabled=false;State("disable-b",a,b,c);
        int oldC=Index(c);DestroyImmediate(c.gameObject);Put("destroy-c.lookup",Name(oldC));
        var d=Group("d");State("reuse-c",a,b,d);
        b.gameObject.SetActive(false);b.enabled=true;State("inactive-enable-b",a,b,d);
        b.gameObject.SetActive(true);State("reactivate-b",a,b,d);
        d.transform.SetParent(a.transform,false);State("nested",a,b,d);
        a.gameObject.SetActive(false);State("inactive-ancestor",a,b,d);
        a.gameObject.SetActive(true);State("reactivate-ancestor",a,b,d);
        SortingGroup.UpdateAllSortingGroups();State("updated",a,b,d);
        int oldA=Index(a),oldD=Index(d);DestroyImmediate(a.gameObject);Put("destroy-tree.a",Name(oldA));Put("destroy-tree.d",Name(oldD));
        var e=Group("e");var f=Group("f");State("reuse-tree",b,e,f);
        var saved=new SortingGroup[70];for(int i=0;i<saved.Length;i++)saved[i]=Group("many-"+i);
        bool all=true;foreach(var g in saved)all &= ReferenceEquals(g,Get(Index(g)));Put("many.all-lookups",all);
        for(int i=0;i<saved.Length;i+=2) saved[i].enabled=false;
        bool retained=true;for(int i=1;i<saved.Length;i+=2)retained &= ReferenceEquals(saved[i],Get(Index(saved[i])));Put("many.retained-lookups",retained);
        foreach(var g in saved)DestroyImmediate(g.gameObject);DestroyImmediate(b.gameObject);DestroyImmediate(e.gameObject);DestroyImmediate(f.gameObject);
        for(int i=0;i<8;i++)Put("final.lookup."+i,Name(i));
        var unrelated = Group("unrelated");
        Put("renderer.control-active-group", ReferenceEquals(unrelated, Get(Index(unrelated))));
        foreach (var type in new[] { typeof(SpriteRenderer), typeof(MeshRenderer) })
        {
            var owner = new GameObject("renderer-"+type.Name); var renderer = (Renderer)owner.AddComponent(type);
            var id = typeof(Renderer).GetProperty("sortingGroupID", All);
            Put("renderer."+type.Name+".ungrouped", id.GetValue(renderer));
            owner.SetActive(false); Put("renderer."+type.Name+".inactive", id.GetValue(renderer));
            owner.SetActive(true); Put("renderer."+type.Name+".reactivated", id.GetValue(renderer));
            DestroyImmediate(owner);
        }
        DestroyImmediate(unrelated.gameObject);
        var tree=Group("tree",false);var left=Group("left",false);var right=Group("right",false);
        left.transform.SetParent(tree.transform,false);right.transform.SetParent(tree.transform,false);
        left.gameObject.SetActive(true);right.gameObject.SetActive(true);tree.gameObject.SetActive(true);State("siblings.enabled",tree,left,right);
        tree.gameObject.SetActive(false);tree.gameObject.SetActive(true);State("siblings.reenabled",tree,left,right);
        DestroyImmediate(tree.gameObject);
    }
}
