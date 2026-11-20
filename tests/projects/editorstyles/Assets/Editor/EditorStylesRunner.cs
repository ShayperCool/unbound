using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class EditorStylesRunner
{
    public static void Record()
    {
        int skin = Environment.GetEnvironmentVariable("EDITOR_STYLES_THEME") == "dark" ? 1 : 0;
        EditorPrefs.SetInt("UserSkin", skin);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(EditorGUIUtility).GetProperty("skinIndex", flags).SetValue(null, skin);
        typeof(EditorStyles).GetMethod("UpdateSkinCache", flags, null, Type.EmptyTypes, null).Invoke(null, null);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
