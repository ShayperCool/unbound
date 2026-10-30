using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProbeRunner
{
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Resources");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var data = new TerrainData { heightmapResolution = 33, size = new Vector3(4, 1, 4), alphamapResolution = 16, baseMapResolution = 16 };
        AssetDatabase.CreateAsset(data, "Assets/Resources/Terrain.asset");
        var white = new Texture2D(4,4,TextureFormat.RGBA32,false,true);
        var pixels = new Color[16]; for(int i=0;i<16;i++) pixels[i]=new Color(.7f,.55f,.4f,1);
        white.SetPixels(pixels); white.Apply();
        AssetDatabase.CreateAsset(white,"Assets/Resources/Albedo.asset");
        var layer = new TerrainLayer { diffuseTexture = white, tileSize = new Vector2(4,4) };
        AssetDatabase.CreateAsset(layer,"Assets/Resources/Layer.terrainlayer");
        data.terrainLayers = new[]{layer};
        var alpha=new float[16,16,1]; for(int y=0;y<16;y++) for(int x=0;x<16;x++) alpha[y,x,0]=1;
        data.SetAlphamaps(0,0,alpha);
        var terrain=Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>(); terrain.name="Baked Terrain"; terrain.drawInstanced=false;
        GameObjectUtility.SetStaticEditorFlags(terrain.gameObject,StaticEditorFlags.ContributeGI);
        var sun=new GameObject("Baked Sun",typeof(Light)).GetComponent<Light>();
        sun.type=LightType.Directional; sun.transform.rotation=Quaternion.Euler(50,20,0); sun.lightmapBakeType=LightmapBakeType.Baked;
        sun.color=new Color(.6f,.8f,1); sun.intensity=.7f;
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.black; RenderSettings.reflectionIntensity=0;
        var camera=new GameObject("Main Camera",typeof(Camera)).GetComponent<Camera>(); camera.tag="MainCamera";
        camera.transform.position=new Vector3(4,10,2); camera.transform.rotation=Quaternion.Euler(90,0,0);
        camera.orthographic=true; camera.orthographicSize=2; camera.aspect=2; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        var settings=new LightingSettings { bakedGI=true, realtimeGI=false, lightmapper=LightingSettings.Lightmapper.ProgressiveCPU,
          lightmapResolution=4, lightmapMaxSize=64, lightmapCompression=LightmapCompression.None, directionalityMode=LightmapsMode.NonDirectional,
          directSampleCount=16, indirectSampleCount=16, environmentSampleCount=16, maxBounces=1, filteringMode=LightingSettings.FilterMode.None };
        AssetDatabase.CreateAsset(settings,"Assets/Scenes/Bake.lighting"); Lightmapping.lightingSettings=settings;
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        if(!Lightmapping.Bake()) throw new System.Exception("Terrain bake failed");
        EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        // Native-authored prefab with explicit fields: Unity may deliberately omit baked bindings.
        terrain.lightmapIndex=1; terrain.lightmapScaleOffset=new Vector4(.5f,.25f,.125f,.375f);
        terrain.realtimeLightmapIndex=3; terrain.realtimeLightmapScaleOffset=new Vector4(.4f,.3f,.2f,.1f);
        PrefabUtility.SaveAsPrefabAsset(terrain.gameObject,"Assets/Resources/BoundTerrain.prefab");
        EditorApplication.Exit(0);
    }
    public static void Record() { EditorSettings.asyncShaderCompilation=false; EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity"); EditorApplication.EnterPlaymode(); }
    public static void RecordGamma() { PlayerSettings.colorSpace=ColorSpace.Gamma; AssetDatabase.SaveAssets(); Record(); }
    public static void RecordLinear() { PlayerSettings.colorSpace=ColorSpace.Linear; AssetDatabase.SaveAssets(); Record(); }
}
