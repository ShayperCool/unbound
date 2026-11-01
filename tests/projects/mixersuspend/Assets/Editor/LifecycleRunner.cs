using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
public static class LifecycleRunner {
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    public static void PrepareAsset(){
        const string path="Assets/Resources/lifecycle_muted.mixer";
        if(!AssetDatabase.CopyAsset("Assets/Resources/enabled.mixer",path))throw new Exception("Copy mixer failed");
        var mixer=AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
        foreach(var group in mixer.FindMatchingGroups(""))if(group.name=="Sibling"){
            group.GetType().GetProperty("mute",F).SetValue(group,true);EditorUtility.SetDirty(group);
        }
        EditorUtility.SetDirty(mixer);AssetDatabase.SaveAssets();
    }
    public static void Record(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("LifecycleProbe").AddComponent<LifecycleProbe>();Directory.CreateDirectory("Assets/Scenes");EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/Lifecycle.unity");EditorApplication.EnterPlaymode();}
}
