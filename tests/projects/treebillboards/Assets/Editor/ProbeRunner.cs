using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -quit -projectPath tests/projects/treebillboards -executeMethod ProbeRunner.Create`
/// makes the probe's assets once: two tree prefabs drawn with the Soft Occlusion shaders (each one
/// mesh, a trunk submesh with the bark material and a crown submesh with the leaves material, whose
/// texture has holes: a round tree and a tall, narrow one), a flat 400 × 400 terrain with one layer
/// and the two trees as its prototypes, and
/// the scene with the terrain and a directional light. `-executeMethod ProbeRunner.Record` with
/// <c>TREE_BILLBOARD_PROBE_OUT</c> set plays the scene, where <see cref="TreeBillboardProbe"/>
/// places the trees and records.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var path in new[] { "Assets/Tree", "Assets/Terrain", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        foreach (var path in new[] { "Assets/Tree", "Assets/Terrain", "Assets/Scenes" })
            Directory.CreateDirectory(path);

        var bark = Texture("Assets/Tree/Bark.png", 32, (x, y) => new Color(0.42f + 0.06f * ((x / 4 + y / 8) & 1), 0.30f, 0.18f, 1f));
        // Leaves: green with round holes (alpha 0) on a 16-texel grid.
        var leaves = Texture("Assets/Tree/Leaves.png", 64, (x, y) =>
        {
            float dx = (x & 15) - 7.5f, dy = (y & 15) - 7.5f;
            bool hole = dx * dx + dy * dy < 12f;
            return new Color(0.20f + 0.10f * ((x / 16) & 1), 0.55f - 0.10f * ((y / 16) & 1), 0.15f, hole ? 0f : 1f);
        });

        var trunk = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        var crown = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        var mesh = new Mesh { name = "Tree" };
        mesh.CombineMeshes(new[]
        {
            new CombineInstance { mesh = trunk, transform = Matrix4x4.TRS(new Vector3(0f, 2f, 0f), Quaternion.identity, new Vector3(0.5f, 2f, 0.5f)) },
            new CombineInstance { mesh = crown, transform = Matrix4x4.TRS(new Vector3(0.3f, 5f, 0f), Quaternion.identity, new Vector3(4f, 3.5f, 3f)) },
        }, false, true);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, "Assets/Tree/Tree.asset");

        var barkMaterial = new Material(Shader.Find("Nature/Tree Soft Occlusion Bark")) { mainTexture = bark };
        AssetDatabase.CreateAsset(barkMaterial, "Assets/Tree/Bark.mat");
        var leavesMaterial = new Material(Shader.Find("Nature/Tree Soft Occlusion Leaves")) { mainTexture = leaves };
        leavesMaterial.SetFloat("_Cutoff", 0.4f);
        AssetDatabase.CreateAsset(leavesMaterial, "Assets/Tree/Leaves.mat");

        var prefab = Prefab("Tree", mesh, barkMaterial, leavesMaterial);
        // A second, narrow prototype: a tall trunk under a capsule crown.
        var tallMesh = new Mesh { name = "Tall" };
        tallMesh.CombineMeshes(new[]
        {
            new CombineInstance { mesh = trunk, transform = Matrix4x4.TRS(new Vector3(0f, 3f, 0f), Quaternion.identity, new Vector3(0.35f, 3f, 0.35f)) },
            new CombineInstance { mesh = Resources.GetBuiltinResource<Mesh>("Capsule.fbx"), transform = Matrix4x4.TRS(new Vector3(0f, 8f, 0f), Quaternion.identity, new Vector3(1.6f, 2.5f, 1.6f)) },
        }, false, true);
        tallMesh.RecalculateBounds();
        AssetDatabase.CreateAsset(tallMesh, "Assets/Tree/Tall.asset");
        var tall = Prefab("Tall", tallMesh, barkMaterial, leavesMaterial);

        var ground = Texture("Assets/Terrain/Ground.png", 32, (x, y) => new Color(0.36f, 0.42f + 0.05f * ((x / 8 + y / 8) & 1), 0.28f, 1f));
        var layer = new TerrainLayer { diffuseTexture = ground, tileSize = new Vector2(10f, 10f) };
        AssetDatabase.CreateAsset(layer, "Assets/Terrain/Ground.terrainlayer");
        var data = new TerrainData();
        AssetDatabase.CreateAsset(data, "Assets/Terrain/Flat.asset");
        data.heightmapResolution = 129;
        data.size = new Vector3(400f, 50f, 400f);
        data.alphamapResolution = 32;
        data.terrainLayers = new[] { layer };
        data.treePrototypes = new[] { new TreePrototype { prefab = prefab }, new TreePrototype { prefab = tall } };

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.name = "Flat";
        terrainObject.transform.position = new Vector3(-200f, 0f, 0f);
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static GameObject Prefab(string name, Mesh mesh, Material bark, Material leaves)
    {
        var tree = new GameObject(name);
        tree.AddComponent<MeshFilter>().sharedMesh = mesh;
        tree.AddComponent<MeshRenderer>().sharedMaterials = new[] { bark, leaves };
        var prefab = PrefabUtility.SaveAsPrefabAsset(tree, $"Assets/Tree/{name}.prefab");
        Object.DestroyImmediate(tree);
        return prefab;
    }

    static Texture2D Texture(string path, int size, System.Func<int, int, Color> color)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, color(x, y));
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
