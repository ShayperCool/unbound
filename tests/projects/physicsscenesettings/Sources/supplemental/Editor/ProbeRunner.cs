using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        foreach (bool active in new[] { true,false })
        {
            var go = new GameObject(active ? "SceneActive" : "SceneInactive"); go.SetActive(active);
            go.transform.position = new Vector3(100,100,100);
            go.AddComponent<BoxCollider>(); go.AddComponent<Rigidbody>();
        }
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true) };
        AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
    }
    public static void Record()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
        var s = new System.Text.StringBuilder(); var property = settings.GetIterator();
        while (property.NextVisible(true))
            s.Append(property.propertyPath).Append(" ").Append(property.propertyType).Append(" ").Append(property.propertyType == SerializedPropertyType.Integer || property.propertyType == SerializedPropertyType.Enum ? property.intValue.ToString() : property.propertyType == SerializedPropertyType.Boolean ? property.boolValue.ToString() : "").Append(property.propertyType == SerializedPropertyType.Enum ? " names=" + string.Join(",", property.enumNames) : "").Append('\n');
        File.WriteAllText(Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT") + ".settings",s.ToString());
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity"); EditorApplication.EnterPlaymode();
    }
}
