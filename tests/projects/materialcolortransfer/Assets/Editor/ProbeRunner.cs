using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner {
 public static void Create() {
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  new GameObject("Probe").AddComponent<MaterialColorTransferProbe>();
  System.IO.Directory.CreateDirectory("Assets/Scenes");
  EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
  EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
  var m=new Material(Shader.Find("Probe/MaterialColorTransfer"));
  foreach(var n in MaterialColorTransferProbe.Properties) {
   int i=System.Array.IndexOf(MaterialColorTransferProbe.Properties,n);
   if(i<6)m.SetColor(n,new Color(4,.25f,-.125f,2));else m.SetFloat(n,4);
  }
  AssetDatabase.CreateAsset(m,"Assets/Resources/Imported.mat");
  var v=new Material(m);v.parent=m;v.RevertAllPropertyOverrides();v.SetColor("_Hdr",new Color(8,.75f,-.5f,4));
  AssetDatabase.CreateAsset(v,"Assets/Resources/Variant.mat");AssetDatabase.SaveAssets();
 }
 public static void Record() {
  bool old=EditorSettings.asyncShaderCompilation;EditorSettings.asyncShaderCompilation=false;
  EditorApplication.quitting+=()=>{EditorSettings.asyncShaderCompilation=old;};
  EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");EditorApplication.EnterPlaymode();
 }
}
