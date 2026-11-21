using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Edit mode: assets made of objects created by script. Materials made by AssetDatabase.CreateAsset
/// (a shader's defaults, edited values, keywords, tags, passes, a texture, a copy of a material
/// asset, another shader, another extension), edited and saved by SaveAssets, given a material by
/// AddObjectToAsset, a material added to a ScriptableObject asset; a mesh, an animation clip, a
/// physic material and a texture made assets the same way; SaveAsPrefabAssetAndConnect of a
/// GameObject under another name than the file's and of one referencing a scene object outside
/// its hierarchy, with the scene they are in saved. Each row records a return value, a flag or a
/// file's hash; run.py compares the files on disk after the run.
/// </summary>
public class CreateAssetTests
{
    const string Folder = "Assets/Created";

    static string Name(Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    static void Create(string key, Object asset, string path)
    {
        ProbeLog.Try(key + ".result", () => { AssetDatabase.CreateAsset(asset, path); return Name(asset); });
        ProbeLog.Row(key + ".path", AssetDatabase.GetAssetPath(asset));
        ProbeLog.Row(key + ".dirty", EditorUtility.IsDirty(asset));
        ProbeLog.Row(key + ".main", Name(AssetDatabase.LoadMainAssetAtPath(path)));
        ProbeLog.Snapshot(key + ".file", path);
        ProbeLog.Snapshot(key + ".meta", path + ".meta");
    }

    [Test]
    public void CreateAssets()
    {
        ProbeLog.Row("folder", AssetDatabase.CreateFolder("Assets", "Created").Length == 32);
        var standard = Shader.Find("Standard");

        Create("plain", new Material(standard), Folder + "/Plain.mat");

        var edited = new Material(standard);
        edited.color = new Color(0.25f, 0.5f, 0.75f, 1f);
        edited.SetFloat("_Glossiness", 0.8f);
        edited.SetFloat("_NotInShader", 3f);
        edited.SetColor("_EmissionColor", Color.red);
        edited.SetVector("_NotInShaderVector", new Vector4(1f, 2f, 3f, 4f));
        edited.SetInt("_NotInShaderInt", 7);
        edited.EnableKeyword("_EMISSION");
        edited.EnableKeyword("NOT_DECLARED");
        edited.renderQueue = 3000;
        edited.enableInstancing = true;
        edited.doubleSidedGI = true;
        edited.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        edited.SetOverrideTag("RenderType", "Transparent");
        edited.SetShaderPassEnabled("ShadowCaster", false);
        edited.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Probe.png");
        edited.mainTextureScale = new Vector2(2f, 3f);
        edited.mainTextureOffset = new Vector2(0.5f, 0.25f);
        edited.name = "Ignored";
        Create("edited", edited, Folder + "/Edited.mat");

        Create("copy", new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatEdit.mat")), Folder + "/Copy.mat");
        Create("unlit", new Material(Shader.Find("Unlit/Color")), Folder + "/Unlit.mat");
        Create("asAsset", new Material(standard), Folder + "/AsAsset.asset");

        // Edited after it was made, then saved.
        var plain = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Plain.mat");
        plain.SetFloat("_Metallic", 0.5f);
        ProbeLog.Row("plainEdit.dirty", EditorUtility.IsDirty(plain));
        ProbeLog.Row("plainEdit.unsaved", ProbeLog.Hash(Folder + "/Plain.mat"));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("plainEdit.file", Folder + "/Plain.mat");

        // A material added to a material asset, then saved.
        var sub = new Material(Shader.Find("Unlit/Color")) { name = "Sub" };
        ProbeLog.Try("sub.result", () => { AssetDatabase.AddObjectToAsset(sub, plain); return Name(sub); });
        ProbeLog.Row("sub.path", AssetDatabase.GetAssetPath(sub));
        ProbeLog.Row("sub.unsaved", ProbeLog.Hash(Folder + "/Plain.mat"));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("sub.file", Folder + "/Plain.mat");
        ProbeLog.Row("sub.all", string.Join(",", AssetDatabase.LoadAllAssetsAtPath(Folder + "/Plain.mat").Select(Name).OrderBy(n => n, System.StringComparer.Ordinal)));

        // A material added to a ScriptableObject asset.
        var data = ScriptableObject.CreateInstance<ProbeData>();
        Create("holder", data, Folder + "/Holder.asset");
        var inner = new Material(standard) { name = "Inner" };
        ProbeLog.Try("inner.result", () => { AssetDatabase.AddObjectToAsset(inner, data); return Name(inner); });
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("inner.file", Folder + "/Holder.asset");

        // Other engine classes made assets.
        var mesh = new Mesh();
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Create("mesh", mesh, Folder + "/Tri.asset");

        var clip = new AnimationClip();
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
        Create("clip", clip, Folder + "/Move.anim");

        var physic = new PhysicsMaterial("Bouncy") { bounciness = 0.5f, dynamicFriction = 0.2f, staticFriction = 0.3f };
        Create("physic", physic, Folder + "/Bouncy.physicMaterial");

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 128) });
        texture.Apply();
        Create("texture", texture, Folder + "/Tex.asset");

        // SaveAsPrefabAssetAndConnect under another name and with a reference outside the hierarchy.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var named = new GameObject("Named");
        ProbeLog.Try("named.result", () => Name(PrefabUtility.SaveAsPrefabAssetAndConnect(named, Folder + "/Other.prefab", InteractionMode.AutomatedAction)));
        ProbeLog.Row("named.name", named.name);
        ProbeLog.Row("named.mods", Mods(named));
        ProbeLog.Snapshot("named.prefab", Folder + "/Other.prefab");

        var target = new GameObject("Target");
        var referencing = new GameObject("Referencing");
        var holder = referencing.AddComponent<Holder>();
        holder.target = target;
        holder.label = "referencing";
        ProbeLog.Try("outside.result", () => Name(PrefabUtility.SaveAsPrefabAssetAndConnect(referencing, Folder + "/Referencing.prefab", InteractionMode.AutomatedAction)));
        ProbeLog.Row("outside.target", Name(holder.target));
        ProbeLog.Row("outside.mods", Mods(referencing));
        ProbeLog.Snapshot("outside.prefab", Folder + "/Referencing.prefab");
        ProbeLog.Try("scene.result", () => EditorSceneManager.SaveScene(scene, Folder + "/Connect.unity"));
        ProbeLog.Snapshot("scene.file", Folder + "/Connect.unity");
    }

    static string Mods(GameObject instance)
    {
        var mods = PrefabUtility.GetPropertyModifications(instance);
        if (mods == null) return "null";
        return string.Join("; ", mods.Select(m => $"{Name(m.target)}|{m.propertyPath}|{m.value}|{Name(m.objectReference)}").OrderBy(m => m, System.StringComparer.Ordinal));
    }
}
