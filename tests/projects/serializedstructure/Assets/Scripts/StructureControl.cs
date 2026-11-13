using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEditor;
public sealed class StructureControl : MonoBehaviour
{
    static readonly List<string> Rows = new List<string>();
    static void Put(string k, object v) => Rows.Add("{\"k\":\"" + k + "\",\"v\":\"" + Convert.ToString(v, CultureInfo.InvariantCulture) + "\"}");
    static void Vector(string k, Vector3 v) { Put(k+".x",v.x); Put(k+".y",v.y); Put(k+".z",v.z); }
    static void Property(SerializedObject so, string key, string name)
    {
        var p=so.FindProperty(name); Put(key+"/found",p!=null);
        if(p==null)return;
        Put(key+"/type",p.type); Put(key+"/kind",p.propertyType); Put(key+"/editable",p.editable); Put(key+"/array",p.isArray);
        if(p.isArray) { Put(key+"/size",p.arraySize); if(p.arraySize>0) { var e=p.GetArrayElementAtIndex(0); Put(key+"/elementType",e.type); Put(key+"/elementKind",e.propertyType); var c=e.FindPropertyRelative("component"); if(c!=null)Put(key+"/componentType",c.type); } }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin() { if(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT")!=null)new GameObject("Structure driver").AddComponent<StructureControl>(); }
    void Start()
    {
        try
        {
            var go=new GameObject("Parent"); var box=go.AddComponent<BoxCollider>(); var sphere=go.AddComponent<SphereCollider>();
            var a=new GameObject("A"); var b=new GameObject("B"); a.transform.SetParent(go.transform,false); b.transform.SetParent(go.transform,false);
            using(var so=new SerializedObject(go))
            {
                foreach(var n in new[]{"m_Component","m_Icon","m_NavMeshLayer","m_StaticEditorFlags"})Property(so,"go/"+n,n);
                foreach(var n in new[]{"m_NavMeshLayer","m_StaticEditorFlags"})
                {
                    var p=so.FindProperty(n); if(p==null)continue; Put(n+"/initial",p.intValue); p.intValue=9; Put(n+"/apply",so.ApplyModifiedProperties()); so.Update(); Put(n+"/read",so.FindProperty(n).intValue);
                }
                Put("go/staticAfterFlags",go.isStatic); go.isStatic=false; so.Update(); Put("go/flagsAfterFalse",so.FindProperty("m_StaticEditorFlags")?.intValue); go.isStatic=true; so.Update(); Put("go/flagsAfterTrue",so.FindProperty("m_StaticEditorFlags")?.intValue);
                var icon=new Texture2D(1,1); var picon=so.FindProperty("m_Icon"); if(picon!=null) { picon.objectReferenceValue=icon; Put("icon/apply",so.ApplyModifiedProperties()); so.Update(); Put("icon/retained",so.FindProperty("m_Icon").objectReferenceValue==icon); }
                var pc=so.FindProperty("m_Component"); if(pc!=null) { Put("components/move",pc.MoveArrayElement(2,1)); Put("components/before",go.GetComponents<Component>()[1].GetType().Name); Put("components/apply",so.ApplyModifiedProperties()); Put("components/after",go.GetComponents<Component>()[1].GetType().Name); pc=so.FindProperty("m_Component"); pc.arraySize=1; Put("components/resizeApply",so.ApplyModifiedProperties()); so.Update(); Put("components/sizeAfterResize",so.FindProperty("m_Component").arraySize); Put("components/liveSize",go.GetComponents<Component>().Length); pc=so.FindProperty("m_Component"); var cp=pc.GetArrayElementAtIndex(1).FindPropertyRelative("component"); Put("components/elementEditable",cp.editable); cp.objectReferenceValue=sphere; Put("components/replaceApply",so.ApplyModifiedProperties()); Put("components/replaceAfter",go.GetComponents<Component>()[1].GetType().Name); }
                DestroyImmediate(icon);
            }
            using(var so=new SerializedObject(go.transform))
            {
                foreach(var n in new[]{"m_ConstrainProportionsScale","m_Children","m_LocalEulerAnglesHint"})Property(so,"transform/"+n,n);
                var hint=so.FindProperty("m_LocalEulerAnglesHint"); if(hint!=null) { Vector("hint/initial",hint.vector3Value); hint.vector3Value=new Vector3(13,371,-26); Put("hint/apply",so.ApplyModifiedProperties()); Vector("hint/rotation",go.transform.localEulerAngles); so.Update(); Vector("hint/read",so.FindProperty("m_LocalEulerAnglesHint").vector3Value); go.transform.localEulerAngles=new Vector3(30,50,70); so.Update(); Vector("hint/afterEuler",so.FindProperty("m_LocalEulerAnglesHint").vector3Value); }
                var constrain=so.FindProperty("m_ConstrainProportionsScale"); if(constrain!=null) { constrain.boolValue=true; Put("constrain/apply",so.ApplyModifiedProperties()); go.transform.localScale=new Vector3(2,3,4); so.Update(); Put("constrain/read",so.FindProperty("m_ConstrainProportionsScale").boolValue); Vector("constrain/scale",go.transform.localScale); }
                var children=so.FindProperty("m_Children"); if(children!=null) { Put("children/first",children.GetArrayElementAtIndex(0).objectReferenceValue.name); Put("children/move",children.MoveArrayElement(1,0)); Put("children/before",go.transform.GetChild(0).name); Put("children/apply",so.ApplyModifiedProperties()); Put("children/after",go.transform.GetChild(0).name); Put("children/parent",b.transform.parent==go.transform); so.Update(); Put("children/read",so.FindProperty("m_Children").GetArrayElementAtIndex(0).objectReferenceValue.name); }
            }
            var copy=Instantiate(go); using(var so=new SerializedObject(copy))Put("clone/staticFlags",so.FindProperty("m_StaticEditorFlags")?.intValue); using(var so=new SerializedObject(copy.transform)) { Put("clone/constrain",so.FindProperty("m_ConstrainProportionsScale")?.boolValue); var p=so.FindProperty("m_LocalEulerAnglesHint"); if(p!=null)Vector("clone/hint",p.vector3Value); }
            DestroyImmediate(copy); DestroyImmediate(go); Put("components/removedDestroyedWithOwner",box==null);
            var notify=new GameObject("Notify"); var witness=notify.AddComponent<StructureWitness>(); var c1=new GameObject("C1"); var c2=new GameObject("C2"); c1.transform.SetParent(notify.transform,false); c2.transform.SetParent(notify.transform,false); witness.changes=0;
            using(var ns=new SerializedObject(notify.transform)) { var children=ns.FindProperty("m_Children"); if(children!=null) { children.MoveArrayElement(1,0); ns.ApplyModifiedProperties(); Put("children/changeCallbacks",witness.changes); } }
            DestroyImmediate(notify); Put("complete",1);
        }
        catch(Exception e) { Put("error",e.GetType().Name); Debug.LogException(e); }
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"),Rows); EditorApplication.Exit(0);
    }
}

public sealed class StructureWitness : MonoBehaviour { public int changes; void OnTransformChildrenChanged() { changes++; } }
