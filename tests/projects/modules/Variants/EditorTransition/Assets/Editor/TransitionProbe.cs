using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Engine integration control using the existing Native Play reference. Runs without
/// the test-framework package so its dependencies cannot enable the Physics package.</summary>
public static class TransitionProbe
{
    static Component s_UserComponent;
    static bool s_EditMode, s_EngineBefore, s_UserBefore;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MODULES_TRANSITION_OUT")))
            EditorApplication.delayCall += Open;
    }

    static void Open()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        var body = GameObject.Find("Body");
        s_EditMode = !Application.isPlaying;
        s_EngineBefore = body != null && body.GetComponent<BoxCollider>() != null && body.GetComponent<Rigidbody>() != null;
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ModuleUserIdentity"), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("User").DefineType("UnityEngine.BoxCollider", TypeAttributes.Public, typeof(MonoBehaviour));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var type = builder.CreateType();
        s_UserComponent = new GameObject("User component identity").AddComponent(type);
        s_UserBefore = s_UserComponent != null && type.Assembly != typeof(Component).Assembly;
        EditorApplication.playModeStateChanged += Entered;
        EditorApplication.EnterPlaymode();
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= Entered;
        var body = GameObject.Find("Body");
        bool pruned = body != null && body.GetComponent<BoxCollider>() == null && body.GetComponent<Rigidbody>() == null;
        bool retained = s_UserComponent != null && s_UserComponent.gameObject != null;
        File.WriteAllText(Environment.GetEnvironmentVariable("MODULES_TRANSITION_OUT"),
            "{\"editMode\":" + B(s_EditMode) + ",\"enginePresentInEditMode\":" + B(s_EngineBefore)
            + ",\"userAssemblyPresentInEditMode\":" + B(s_UserBefore) + ",\"enginePrunedInPlayMode\":" + B(pruned)
            + ",\"userAssemblyRetainedInPlayMode\":" + B(retained) + "}\n");
        if (!(s_EditMode && s_EngineBefore && s_UserBefore && pruned && retained)) EditorApplication.Exit(9);
    }

    static string B(bool value) => value ? "true" : "false";
}
