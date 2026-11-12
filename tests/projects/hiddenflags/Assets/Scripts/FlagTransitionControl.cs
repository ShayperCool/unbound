using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
public sealed class FlagTransitionWitness : MonoBehaviour
{
 public int enables, disables, enableOwnFlags, disableOwnFlags;
 public bool enableSceneValid, disableSceneValid;
 void OnEnable(){enables++;enableSceneValid=gameObject.scene.IsValid();enableOwnFlags=(int)hideFlags;}
 void OnDisable(){disables++;disableSceneValid=gameObject.scene.IsValid();disableOwnFlags=(int)hideFlags;}
}
public sealed class FlagDestroyCollider : MonoBehaviour
{
 public UnityEngine.Object victim;
 void OnDisable(){if(victim!=null)UnityEngine.Object.DestroyImmediate(victim);}
}
public sealed class FlagDeactivateAgain : MonoBehaviour
{
 public int calls;
 void OnDisable(){calls++;gameObject.SetActive(false);}
}
public sealed class FlagTransitionControl : MonoBehaviour
{
 static readonly List<string> Values=new List<string>();
 static readonly List<GameObject> Retained=new List<GameObject>();
 static string output;
 static GameObject ordinary;
 static HideFlags[] flags={HideFlags.None,HideFlags.HideInHierarchy,HideFlags.HideInInspector,HideFlags.DontSaveInEditor,HideFlags.NotEditable,HideFlags.DontSaveInBuild,HideFlags.DontUnloadUnusedAsset,HideFlags.DontSave,HideFlags.HideAndDontSave,(HideFlags)5,(HideFlags)17,(HideFlags)20,(HideFlags)21,(HideFlags)53};
 public static bool Finished;
 int frames;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
 static void Begin(){output=Environment.GetEnvironmentVariable("FLAG_TRANSITION_OUT");if(output==null)return;var d=new GameObject("Flag driver");DontDestroyOnLoad(d);d.AddComponent<FlagTransitionControl>();}
 static void Put(string key,object v){Values.Add("{\"k\":\""+key+"\",\"v\":\""+Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)+"\"}");}
 static void State(string key,GameObject root,FlagTransitionWitness witness){Put(key+".valid",root.scene.IsValid());Put(key+".scene",root.scene.name);Put(key+".enable",witness.enables);Put(key+".disable",witness.disables);Put(key+".componentFlags",(int)witness.hideFlags);Put(key+".enableSceneValid",witness.enableSceneValid);Put(key+".disableSceneValid",witness.disableSceneValid);Put(key+".enableOwnFlags",witness.enableOwnFlags);Put(key+".disableOwnFlags",witness.disableOwnFlags);}
 void Start()
 {
  var first=SceneManager.CreateScene("Flag scene");SceneManager.SetActiveScene(first);
  foreach(var flag in flags)
  {
   string key="flag."+(int)flag;var root=new GameObject(key);var w=root.AddComponent<FlagTransitionWitness>();var child=new GameObject("child");child.transform.SetParent(root.transform,false);var cw=child.AddComponent<FlagTransitionWitness>();
   State(key+".before",root,w);root.hideFlags=flag;State(key+".set",root,w);Put(key+".child.valid",child.scene.IsValid());Put(key+".child.flags",(int)child.hideFlags);Put(key+".child.enable",cw.enables);Put(key+".child.disable",cw.disables);
   root.hideFlags=HideFlags.None;State(key+".restored",root,w);DestroyImmediate(root);
   var retained=new GameObject("Retain "+(int)flag);retained.hideFlags=flag;Retained.Add(retained);
  }
  var restored=new GameObject("Restoration target control");var rw=restored.AddComponent<FlagTransitionWitness>();restored.hideFlags=HideFlags.HideAndDontSave;
  SceneManager.SetActiveScene(SceneManager.CreateScene("Restored target"));restored.hideFlags=HideFlags.None;State("restore.toActive",restored,rw);DestroyImmediate(restored);
  foreach(bool inactive in new[]{false,true}){var r=new GameObject("disabled control");var w=r.AddComponent<FlagTransitionWitness>();if(inactive)r.SetActive(false);else w.enabled=false;string k=inactive?"inactive":"disabled";State(k+".before",r,w);r.hideFlags=HideFlags.HideAndDontSave;State(k+".set",r,w);r.hideFlags=HideFlags.None;State(k+".restore",r,w);DestroyImmediate(r);}
  var removed=new GameObject("Callback collider");removed.transform.position=new Vector3(500,0,0);var removal=removed.AddComponent<FlagDestroyCollider>();var collider=removed.AddComponent<BoxCollider>();removal.victim=collider;removed.hideFlags=HideFlags.HideAndDontSave;Physics.SyncTransforms();Put("callback.colliderNull",collider==null);Put("callback.overlaps",Physics.OverlapBox(new Vector3(500,0,0),Vector3.one).Length);DestroyImmediate(removed);
  var inactiveControl=new GameObject("SetActive removal");var inactiveRemoval=inactiveControl.AddComponent<FlagDestroyCollider>();var inactiveCollider=inactiveControl.AddComponent<BoxCollider>();inactiveRemoval.victim=inactiveCollider;inactiveControl.SetActive(false);Put("callback.setactive.colliderNull",inactiveCollider==null);inactiveRemoval.victim=null;DestroyImmediate(inactiveControl);
  var enabledControl=new GameObject("Behaviour removal");var enabledRemoval=enabledControl.AddComponent<FlagDestroyCollider>();var enabledCollider=enabledControl.AddComponent<BoxCollider>();enabledRemoval.victim=enabledCollider;enabledRemoval.enabled=false;Put("callback.behaviour.colliderNull",enabledCollider==null);enabledRemoval.victim=null;DestroyImmediate(enabledControl);
  var childControl=new GameObject("Child removal");var childRemoval=childControl.AddComponent<FlagDestroyCollider>();var childTarget=new GameObject("child target");childTarget.transform.SetParent(childControl.transform,false);var childCollider=childTarget.AddComponent<BoxCollider>();childRemoval.victim=childCollider;childControl.hideFlags=HideFlags.HideAndDontSave;Put("callback.child.colliderNull",childCollider==null);childRemoval.victim=null;DestroyImmediate(childControl);
  var selfControl=new GameObject("Root removal");var selfRemoval=selfControl.AddComponent<FlagDestroyCollider>();selfRemoval.victim=selfControl;selfControl.hideFlags=HideFlags.HideAndDontSave;Put("callback.root.alive",selfControl!=null);selfRemoval.victim=null;DestroyImmediate(selfControl);
  var reentryControl=new GameObject("Activation reentry");var reentry=reentryControl.AddComponent<FlagDeactivateAgain>();reentryControl.hideFlags=HideFlags.HideAndDontSave;Put("callback.reentry.activeSelf",reentryControl.activeSelf);Put("callback.reentry.calls",reentry.calls);DestroyImmediate(reentryControl);
  ordinary=new GameObject("ordinary");
  SceneManager.LoadScene("Assets/Scenes/Empty.unity",LoadSceneMode.Single);
 }
 void Update()
 {
  if(++frames!=5)return;
  for(int i=0;i<Retained.Count;i++){var r=Retained[i];Put("load."+(int)flags[i]+".alive",r!=null);if(r!=null)Put("load."+(int)flags[i]+".valid",r.scene.IsValid());}
  Put("load.ordinary.alive",ordinary!=null);
  Finished=true;
#if UNITY_EDITOR
  UnityEditor.EditorApplication.ExitPlaymode();
#endif
 }
 public static void AfterPlay()
 {
  for(int i=0;i<Retained.Count;i++){var r=Retained[i];Put("stop."+(int)flags[i]+".alive",r!=null);if(r!=null){Put("stop."+(int)flags[i]+".valid",r.scene.IsValid());DestroyImmediate(r);}}
  Values.Sort(StringComparer.Ordinal);Put("complete",1);File.WriteAllLines(output,Values);
 }
}
