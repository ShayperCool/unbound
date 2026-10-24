using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Creates the URP asset and the probe scene: a camera, a directional light, a lit cube,
/// an unlit sphere and a lit plane; makes the scene the build's first.</summary>
public static class ProbeSetup
{
    public static void Create()
    {
        AssetDatabase.CreateFolder("Assets", "Settings");
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, "Assets/Settings/ProbeRenderer.asset");
        var asset = UniversalRenderPipelineAsset.Create(rendererData);
        asset.msaaSampleCount = 1;
        asset.renderScale = 1f;
        AssetDatabase.CreateAsset(asset, "Assets/Settings/ProbeURP.asset");
        GraphicsSettings.defaultRenderPipeline = asset;
        QualitySettings.renderPipeline = asset;

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
        l.color = Color.white;
        l.intensity = 1f;
        l.shadows = LightShadows.None;

        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lit.SetColor("_BaseColor", new Color(0.8f, 0.2f, 0.1f, 1f));
        AssetDatabase.CreateAsset(lit, "Assets/Settings/Lit.mat");
        var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        unlit.SetColor("_BaseColor", new Color(0.1f, 0.8f, 0.2f, 1f));
        AssetDatabase.CreateAsset(unlit, "Assets/Settings/Unlit.mat");
        var ground = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        ground.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.6f, 1f));
        AssetDatabase.CreateAsset(ground, "Assets/Settings/Ground.mat");

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(-1.2f, 0.5f, 0f);
        cube.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
        cube.GetComponent<MeshRenderer>().sharedMaterial = lit;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = new Vector3(1.2f, 0.5f, 0f);
        sphere.GetComponent<MeshRenderer>().sharedMaterial = unlit;
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.GetComponent<MeshRenderer>().sharedMaterial = ground;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
