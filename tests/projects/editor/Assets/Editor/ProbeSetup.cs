using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/editor -executeMethod ProbeSetup.Build -quit`:
/// makes the probe's assets (a ScriptableObject, a material) and its scene (a GameObject with a
/// <see cref="ProbeBehaviour"/> referencing them and a child), the build's first scene.
/// </summary>
public static class ProbeSetup
{
    public static void Build()
    {
        var material = new Material(Shader.Find("Standard")) { name = "Probe" };
        AssetDatabase.CreateAsset(material, "Assets/Probe/Probe.mat");
        var asset = ScriptableObject.CreateInstance<ProbeAsset>();
        asset.material = material;
        AssetDatabase.CreateAsset(asset, "Assets/Probe/ProbeAsset.asset");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Probe");
        var child = new GameObject("Child");
        child.transform.SetParent(go.transform, false);
        child.transform.localPosition = new Vector3(0, 1, 0);
        var behaviour = go.AddComponent<ProbeBehaviour>();
        behaviour.asset = asset;
        behaviour.material = material;
        behaviour.target = child.transform;
        behaviour.data.asset = asset;
        behaviour.data.sceneObject = child;
        behaviour.data.material = material;
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
