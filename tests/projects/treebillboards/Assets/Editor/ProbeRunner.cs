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

    /// <summary>
    /// `-executeMethod ProbeRunner.Extend` adds the later cases' prototypes, leaving the first
    /// assets as they are: in Assets/Resources, `Wide` (the round tree's mesh flattened to three
    /// times its width and 0.4 of its height, saved as an asset) and `Creator`, a Tree Creator
    /// tree (a trunk with branches and leaves, the probe's bark and leaf textures in Tree Creator
    /// materials, its optimized mesh and materials generated by the Tree Creator).
    /// </summary>
    public static void Extend()
    {
        AssetDatabase.DeleteAsset("Assets/Resources");
        AssetDatabase.CreateFolder("Assets", "Resources");
        var round = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Tree/Tree.prefab");
        var flat = Object.Instantiate(round.GetComponent<MeshFilter>().sharedMesh);
        flat.name = "Wide";
        var vertices = flat.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Scale(vertices[i], new Vector3(3f, 0.4f, 3f));
        flat.vertices = vertices;
        flat.RecalculateBounds();
        AssetDatabase.CreateAsset(flat, "Assets/Resources/Wide.asset");
        var materials = round.GetComponent<MeshRenderer>().sharedMaterials;
        var wide = new GameObject("Wide");
        wide.AddComponent<MeshFilter>().sharedMesh = flat;
        wide.AddComponent<MeshRenderer>().sharedMaterials = materials;
        PrefabUtility.SaveAsPrefabAsset(wide, "Assets/Resources/Wide.prefab");
        Object.DestroyImmediate(wide);

        var bark = new Material(Shader.Find("Nature/Tree Creator Bark")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Tree/Bark.png") };
        AssetDatabase.CreateAsset(bark, "Assets/Resources/CreatorBark.mat");
        var leaves = new Material(Shader.Find("Nature/Tree Creator Leaves")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Tree/Leaves.png") };
        leaves.SetFloat("_Cutoff", 0.4f);
        AssetDatabase.CreateAsset(leaves, "Assets/Resources/CreatorLeaves.mat");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!EditorApplication.ExecuteMenuItem("GameObject/3D Object/Tree"))
            throw new System.InvalidOperationException("the Tree Creator menu item did not run");
        var tree = Selection.activeGameObject.GetComponent<Tree>();
        var data = (TreeEditor.TreeData)tree.data;
        var trunk = data.branchGroups[0];
        trunk.materialBranch = bark;
        trunk.height = new Vector2(8f, 8f);
        var boughs = (TreeEditor.TreeGroupBranch)data.AddGroup(trunk, typeof(TreeEditor.TreeGroupBranch));
        boughs.materialBranch = bark;
        boughs.distributionFrequency = 6;
        boughs.height = new Vector2(3f, 4f);
        data.UpdateFrequency(boughs.uniqueID);
        var foliage = (TreeEditor.TreeGroupLeaf)data.AddGroup(boughs, typeof(TreeEditor.TreeGroupLeaf));
        foliage.materialLeaf = leaves;
        foliage.distributionFrequency = 12;
        foliage.size = new Vector2(1.2f, 1.6f);
        data.UpdateFrequency(foliage.uniqueID);
        data.UpdateMesh(tree.transform.worldToLocalMatrix, out var optimized);
        tree.GetComponent<MeshRenderer>().sharedMaterials = optimized;
        EditorUtility.SetDirty(data);
        PrefabUtility.ApplyPrefabInstance(tree.gameObject, InteractionMode.AutomatedAction);
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tree.gameObject);
        AssetDatabase.SaveAssets();
        Selection.activeObject = null;
        Object.DestroyImmediate(tree.gameObject);
        foreach (var (from, to) in new[] { (path, "Assets/Resources/Creator.prefab"), ("Assets/Tree_Textures", "Assets/Resources/Creator_Textures") })
        {
            string moved = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(moved)) throw new System.InvalidOperationException(moved);
        }
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
