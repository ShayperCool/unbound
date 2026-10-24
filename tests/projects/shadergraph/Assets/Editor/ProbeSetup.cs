using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/shadergraph -executeMethod ProbeSetup.Create -quit`:
/// the probe scene, lit like the URP probe's, with a sphere drawn by a Shader Graph shader (URP's
/// Autodesk Interactive graph, imported by the Shader Graph package) next to one drawn by URP Lit.
/// `ProbeSetup.Record` enters Play mode, where <see cref="ShaderGraphProbe"/> writes the image.
/// </summary>
public static class ProbeSetup
{
    const string Graph = "Packages/com.unity.render-pipelines.universal/Shaders/AutodeskInteractive/AutodeskInteractive.shadergraph";

    public static void Create()
    {
        var graphShader = AssetDatabase.LoadAssetAtPath<Shader>(Graph);
        var graphMaterial = new Material(graphShader);
        graphMaterial.SetColor("_Color", new Color(0.2f, 0.5f, 0.9f, 1f));
        AssetDatabase.CreateAsset(graphMaterial, "Assets/Settings/Graph.mat");
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lit.SetColor("_BaseColor", new Color(0.8f, 0.2f, 0.1f, 1f));
        AssetDatabase.CreateAsset(lit, "Assets/Settings/Lit.mat");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 1.5f, -6f);
        camera.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        var cam = camera.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.3f, 0.5f, 1f);
        var light = new GameObject("Directional Light", typeof(Light));
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var l = light.GetComponent<Light>();
        l.type = LightType.Directional;
        l.shadows = LightShadows.None;

        var graphSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        graphSphere.name = "Graph Sphere";
        graphSphere.transform.position = new Vector3(1.2f, 0.5f, 0f);
        graphSphere.GetComponent<MeshRenderer>().sharedMaterial = graphMaterial;
        var litSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        litSphere.name = "Lit Sphere";
        litSphere.transform.position = new Vector3(-1.2f, 0.5f, 0f);
        litSphere.GetComponent<MeshRenderer>().sharedMaterial = lit;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
