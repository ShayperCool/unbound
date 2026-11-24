using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Makes the probe's scene (batch mode, `apv_bake.py create`): a plane, three cubes and a
/// baked directional light under the procedural sky, a global Adaptive Probe Volume, and Lighting
/// Settings that bake baked global illumination with the Progressive CPU lightmapper.</summary>
public static class ApvSetup
{
    const string Folder = "Assets/Scenes";

    static GameObject Prim(PrimitiveType type, string name, Vector3 position, Vector3 euler, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = position;
        go.transform.eulerAngles = euler;
        go.transform.localScale = scale;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
        return go;
    }

    public static void Create()
    {
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var settings = new LightingSettings
            {
                name = "Apv",
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                bakedGI = true,
                realtimeGI = false,
                lightmapResolution = 4f,
                lightmapMaxSize = 256,
                lightmapPadding = 2,
                directionalityMode = LightmapsMode.NonDirectional,
                lightmapCompression = LightmapCompression.None,
                filteringMode = LightingSettings.FilterMode.None,
                mixedBakeMode = MixedLightingMode.IndirectOnly,
            };
            AssetDatabase.CreateAsset(settings, $"{Folder}/Apv.lighting");
            Lightmapping.lightingSettings = settings;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.name = "Sun";
            }
            // Geometry sits inside cell y = 0 with faces in the voxel-center safe bands of
            // VoxelizeScene's 64^3 cell grid (avoiding SubShader 0 Conservative True vs SubShader 1
            // fallback voxel-edge differences) and cubes embedded 5 cm into Ground (avoiding
            // coincident opposite-plane ties). Tall's vertical faces at x = 1 and x = 2 place
            // twelve probes directly on face interiors (t == 0), and Red/Flat enclose interior probes.
            Prim(PrimitiveType.Plane, "Ground", new Vector3(0f, 0.85f, 0f), Vector3.zero, Vector3.one);
            Prim(PrimitiveType.Cube, "Red", new Vector3(-2f, 1.5f, 1f), new Vector3(0f, 20f, 0f), new Vector3(1f, 1.4f, 1f));
            Prim(PrimitiveType.Cube, "Tall", new Vector3(1.5f, 2.2f, -1.4f), Vector3.zero, new Vector3(1f, 2.8f, 1.4f));
            Prim(PrimitiveType.Cube, "Flat", new Vector3(0f, 1.0f, 3f), new Vector3(0f, 45f, 0f), new Vector3(3f, 0.4f, 1f));
            var volume = new GameObject("Probe Volume").AddComponent<ProbeVolume>();
            volume.mode = ProbeVolume.Mode.Global;
            EditorSceneManager.SaveScene(scene, $"{Folder}/Apv.unity");
            var bakingSet = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            bakingSet.name = "Apv Baking Set";
            AssetDatabase.CreateAsset(bakingSet, $"{Folder}/Apv Baking Set.asset");
            bakingSet.TryAddScene(AssetDatabase.AssetPathToGUID($"{Folder}/Apv.unity"));
            EditorUtility.SetDirty(bakingSet);
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
