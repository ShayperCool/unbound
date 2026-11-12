using System;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]
public static class InputConfigRunner
{
 static InputConfigRunner(){if(Environment.GetEnvironmentVariable("INPUT_CONFIG_OUT")==null)return;EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredPlayMode)EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();};if(EditorApplication.isPlayingOrWillChangePlaymode)return;int warm=0;EditorApplication.CallbackFunction wait=null;wait=()=>{if(++warm<30)return;EditorApplication.update-=wait;EditorSceneManager.OpenScene("Assets/Scenes/Empty.unity");PlayModeWindow.SetPlayModeFocused(true);EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).maximized=true;EditorApplication.EnterPlaymode();};EditorApplication.update+=wait;}
}
