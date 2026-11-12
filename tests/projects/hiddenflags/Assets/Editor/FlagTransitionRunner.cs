using System;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]
public static class FlagTransitionRunner
{
 static FlagTransitionRunner()
 {
  if(Environment.GetEnvironmentVariable("FLAG_TRANSITION_OUT")==null)return;
  EditorApplication.playModeStateChanged += state => { if(state==PlayModeStateChange.EnteredEditMode && FlagTransitionControl.Finished) { FlagTransitionControl.AfterPlay(); EditorApplication.Exit(0); } };
  if(EditorApplication.isPlayingOrWillChangePlaymode)return;
  int warm=0; EditorApplication.CallbackFunction wait=null;
  wait=()=>{if(++warm<30)return;EditorApplication.update-=wait;EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;EditorSceneManager.OpenScene("Assets/Scenes/Empty.unity");EditorApplication.EnterPlaymode();};
  EditorApplication.update+=wait;
 }
}
