using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes the probe's scenes (`environment_bake.py create`, <c>-executeMethod
/// EnvironmentSetup.Create</c>): each a plane under a directional sun named as the scene's sun,
/// with Lighting Settings that bake neither baked nor realtime global illumination (a bake then
/// makes the environment lighting alone), the default procedural skybox and the skybox as the
/// ambient source and the default reflection's, unless the scene says otherwise.
/// </summary>
public static class EnvironmentSetup
{
    const string Folder = "Assets/Scenes";

    static void Make(string name, Action setup)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var settings = new LightingSettings { name = name, bakedGI = false, realtimeGI = false };
        Lightmapping.lightingSettings = settings;
        GameObject.CreatePrimitive(PrimitiveType.Plane).name = "Ground";
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(30f, 40f, 0f);
        RenderSettings.sun = sun;
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        setup();
        AssetDatabase.CreateAsset(settings, $"{Folder}/{name}.lighting");
        EditorSceneManager.SaveScene(scene, $"{Folder}/{name}.unity");
    }

    /// <summary>Makes the scenes <c>ENVIRONMENT_SCENES</c> names (comma-separated), else all.</summary>
    public static void Create()
    {
        try
        {
            var all = new (string Name, Action Setup)[]
            {
                // The default skybox, ambient intensity 1, reflections of 128 texels.
                ("EnvSky", () => { }),
                // Intensities that are not 1 and reflections of 64 texels.
                ("EnvSkyBright", () =>
                {
                    RenderSettings.ambientIntensity = 2.5f;
                    RenderSettings.reflectionIntensity = 0.5f;
                    RenderSettings.defaultReflectionResolution = 64;
                }),
                // The sun higher and turned, its colour and intensity other than the default.
                ("EnvSkySun", () =>
                {
                    var sun = RenderSettings.sun;
                    sun.transform.rotation = Quaternion.Euler(65f, -120f, 0f);
                    sun.color = new Color(1f, 0.7f, 0.4f);
                    sun.intensity = 2f;
                    RenderSettings.defaultReflectionResolution = 32;
                }),
                // An ambient colour, and a gradient, with the skybox still there.
                ("EnvFlat", () =>
                {
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.3f, 0.5f, 0.8f);
                }),
                ("EnvTrilight", () =>
                {
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = new Color(0.3f, 0.5f, 1f);
                    RenderSettings.ambientEquatorColor = new Color(0.6f, 0.5f, 0.4f);
                    RenderSettings.ambientGroundColor = new Color(0.1f, 0.2f, 0.05f);
                }),
                // No skybox material.
                ("EnvNoSky", () => { RenderSettings.skybox = null; }),
                // Baked with the skybox and then without it (the recorder takes it away).
                ("EnvSkyThenNone", () => { }),
                // A render pipeline that bakes neither part (the recorder says so for the bake),
                // under a sky that is not the default's (intensity 1.7).
                ("EnvNoBaking", () => { RenderSettings.ambientIntensity = 1.7f; }),
            };
            var only = Environment.GetEnvironmentVariable("ENVIRONMENT_SCENES");
            var wanted = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            foreach (var (name, setup) in all)
                if (wanted == null || wanted.Contains(name)) Make(name, setup);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
