using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
public sealed class MaterialAliasControl:MonoBehaviour
{
 static readonly List<string> Values=new List<string>();
 static string output;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
 static void Begin(){output=Environment.GetEnvironmentVariable("MATERIAL_ALIAS_OUT");if(output==null)return;new GameObject("Material aliases").AddComponent<MaterialAliasControl>();}
 static void Put(string key,object v){Values.Add("{\"k\":\""+key+"\",\"v\":\""+Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)+"\"}");File.WriteAllLines(output+".partial",Values);}
 void Start()
 {
  foreach(bool touch in new[]{false,true})
  {
   string k=touch?"touched":"cold";var first=Resources.Load<Material>(touch?"UnloadMatTouched":"UnloadMat");int id=first.GetInstanceID();first.color=Color.red;Resources.UnloadAsset(first);Put(k+".aliveAfterUnload",first!=null);
   if(touch)Put(k+".firstAccessSaved",first.color==Color.white);
   var second=Resources.Load<Material>(touch?"UnloadMatTouched":"UnloadMat");Put(k+".sameWrapper",ReferenceEquals(first,second));Put(k+".sameID",id==second.GetInstanceID());Put(k+".equal",first==second);Put(k+".secondSaved",second.color==Color.white);
   first.color=Color.green;Put(k+".oldWriteNewReads",second.color==Color.green);
#if UNITY_EDITOR
   var holderGo=new GameObject("Serialized alias control");var holder=holderGo.AddComponent<MaterialAliasHolder>();holder.material=first;
   using(var serialized=new UnityEditor.SerializedObject(holder))
   {
    var property=serialized.FindProperty("material");property.objectReferenceValue=first;Put(k+".serialized.sameModified",serialized.hasModifiedProperties);Put(k+".serialized.sameApplied",serialized.ApplyModifiedProperties());
    property.objectReferenceValue=second;Put(k+".serialized.aliasModified",serialized.hasModifiedProperties);Put(k+".serialized.aliasApplied",serialized.ApplyModifiedProperties());Put(k+".serialized.oldWrapperRetained",ReferenceEquals(holder.material,first));
    var different=new Material(second);property.objectReferenceValue=different;Put(k+".serialized.differentModified",serialized.hasModifiedProperties);Put(k+".serialized.differentApplied",serialized.ApplyModifiedProperties());DestroyImmediate(different);
   }
   DestroyImmediate(holderGo);
#endif
   second.renderQueue=2450;Put(k+".newWriteOldReads",first.renderQueue);
   first.CopyPropertiesFromMaterial(second);Put(k+".copyFromAliasPreserves",second.color==Color.green&&second.renderQueue==2450);
   second.CopyMatchingPropertiesFromMaterial(first);Put(k+".matchingCopyAliasPreserves",first.color==Color.green&&first.renderQueue==2450);
   first.name="Alias name";Put(k+".nameShared",second.name=="Alias name");
   second.hideFlags=HideFlags.NotEditable;Put(k+".flagsShared",first.hideFlags==HideFlags.NotEditable);
   int found=0;bool canonical=false;foreach(var m in Resources.FindObjectsOfTypeAll<Material>())if(m.GetInstanceID()==id){found++;canonical=ReferenceEquals(m,second);}Put(k+".findCount",found);Put(k+".findCanonical",canonical);
#if UNITY_EDITOR
   Put(k+".idLookupCanonical",ReferenceEquals(UnityEditor.EditorUtility.InstanceIDToObject(id),second));
#endif
   var clone=new Material(second);clone.color=Color.blue;Put(k+".cloneIndependent",first.color==Color.green&&clone.GetInstanceID()!=id);DestroyImmediate(clone);
   Resources.UnloadAsset(second);var third=Resources.Load<Material>(touch?"UnloadMatTouched":"UnloadMat");Put(k+".secondReloadNewWrapper",!ReferenceEquals(third,second)&&!ReferenceEquals(third,first));Put(k+".secondReloadSameID",third.GetInstanceID()==id);Put(k+".oldReloadSaved",first.color==Color.white);Put(k+".secondReloadSaved",second.color==Color.white);
   Put(k+".reloadNameSaved",first.name=="UnloadMat"&&second.name=="UnloadMat"&&third.name=="UnloadMat");Put(k+".reloadFlagsSaved",first.hideFlags==HideFlags.None&&second.hideFlags==HideFlags.None&&third.hideFlags==HideFlags.None);
   third.color=Color.red;Put(k+".thirdWriteBothRead",first.color==Color.red&&second.color==Color.red);
   DestroyImmediate(third,true);Put(k+".destroyAllNull",first==null&&second==null&&third==null);
  }
  Values.Sort(StringComparer.Ordinal);Put("complete",1);File.WriteAllLines(output,Values);
#if UNITY_EDITOR
  UnityEditor.EditorApplication.Exit(0);
#endif
 }
}

public sealed class MaterialAliasHolder:MonoBehaviour
{
 public Material material;
}
