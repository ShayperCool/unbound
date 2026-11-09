using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ResetDeliveryThreadRunner
{
    public static void Record()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("delivery"); go.AddComponent<ResetDeliveryProbe>(); go.AddComponent<ResetDeliveryThreads>();
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/ResetDeliveryThreads.unity");
        EditorApplication.EnterPlaymode();
    }
}
