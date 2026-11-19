using System.Collections.Generic;
using NUnit.Framework;
using Probe.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Edit mode: a scene edited by script and saved in place, as a copy and to a new path; a second
/// scene saved with and without being marked dirty; a new scene with default objects saved; asset
/// edits saved by AssetDatabase.SaveAssets with and without SetDirty, and edits left unsaved when
/// the Editor exits. Each row records a return value, a flag or a file's hash at that point; run.py
/// compares the files on disk after the run.
/// </summary>
public class SaveEditTests
{
    [Test]
    public void SaveFlow()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Base.unity", OpenSceneMode.Single);
        ProbeLog.Row("open.isDirty", scene.isDirty);
        EditScene();
        ProbeLog.Row("edited.isDirty", scene.isDirty);

        ProbeLog.Try("saveAsCopy.result", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/BaseCopy.unity", true));
        ProbeLog.Row("saveAsCopy.path", scene.path);
        ProbeLog.Row("saveAsCopy.copy", ProbeLog.Hash("Assets/Scenes/BaseCopy.unity"));
        ProbeLog.Row("saveAsCopy.base", ProbeLog.Hash("Assets/Scenes/Base.unity"));

        ProbeLog.Try("save.result", () => EditorSceneManager.SaveScene(scene));
        ProbeLog.Row("save.isDirty", scene.isDirty);
        ProbeLog.Snapshot("save.base", "Assets/Scenes/Base.unity");

        ProbeLog.Try("saveTo.result", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/BaseMoved.unity"));
        ProbeLog.Row("saveTo.path", scene.path);
        ProbeLog.Row("saveTo.name", scene.name);
        ProbeLog.Row("saveTo.moved", ProbeLog.Hash("Assets/Scenes/BaseMoved.unity"));

        ProbeLog.Try("prefab.section", () => { Prefabs(scene); return "done"; });

        var second = EditorSceneManager.OpenScene("Assets/Scenes/Second.unity", OpenSceneMode.Single);
        var lone = second.GetRootGameObjects()[0];
        lone.transform.position = new Vector3(4f, 5f, 6f);
        ProbeLog.Row("second.edited.isDirty", second.isDirty);
        ProbeLog.Try("second.saveOpen.result", () => EditorSceneManager.SaveOpenScenes());
        ProbeLog.Row("second.saveOpen.file", ProbeLog.Hash("Assets/Scenes/Second.unity"));
        ProbeLog.Try("second.markDirty.result", () => EditorSceneManager.MarkSceneDirty(second));
        ProbeLog.Row("second.markDirty.isDirty", second.isDirty);
        ProbeLog.Try("second.ifUserWants.result", () => EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo());
        ProbeLog.Row("second.ifUserWants.file", ProbeLog.Hash("Assets/Scenes/Second.unity"));
        ProbeLog.Row("second.ifUserWants.isDirty", second.isDirty);
        ProbeLog.Try("second.saveDirty.result", () => EditorSceneManager.SaveOpenScenes());
        ProbeLog.Row("second.saveDirty.file", ProbeLog.Hash("Assets/Scenes/Second.unity"));
        ProbeLog.Row("second.saveDirty.isDirty", second.isDirty);
        lone.transform.position = new Vector3(7f, 8f, 9f);
        ProbeLog.Try("second.saveScenes.result", () => EditorSceneManager.SaveScenes(new[] { second }));
        ProbeLog.Snapshot("second.saveScenes.file", "Assets/Scenes/Second.unity");

        var fresh = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        ProbeLog.Row("fresh.isDirty", fresh.isDirty);
        ProbeLog.Try("fresh.save.result", () => EditorSceneManager.SaveScene(fresh, "Assets/Scenes/Fresh.unity"));
        ProbeLog.Row("fresh.path", fresh.path);
        ProbeLog.Row("fresh.file", ProbeLog.Hash("Assets/Scenes/Fresh.unity"));

        ProbeLog.Try("asset.section", () => { EditAssets(); return "done"; });
    }

    static void EditScene()
    {
        var root = GameObject.Find("Root");
        root.transform.position = new Vector3(1.5f, -0.1f, 1e-7f);
        root.transform.localScale = new Vector3(-0f, 123456.79f, 2f);
        var holder = root.GetComponent<Holder>();
        holder.count = 7;
        holder.ratio = 0.1f;
        holder.precise = 0.1;
        holder.label = "a: b # c";
        holder.offset = new Vector3(0.333333343f, 1e10f, -2.5f);
        holder.tint = new Color(0.2f, 0.4f, 0.6f, 1f);
        holder.numbers = new List<int> { 1, 2, 3 };
        holder.words.Add("it's");
        holder.target = GameObject.Find("Graph");

        var child = root.transform.Find("Child").gameObject;
        child.name = "Child Renamed";
        child.GetComponent<BoxCollider>().size = new Vector3(2f, 2f, 2f);
        child.GetComponent<MeshRenderer>().enabled = false;
        child.SetActive(false);

        var light = Object.FindFirstObjectByType<Light>();
        light.intensity = 2f;

        var added = new GameObject("Added");
        added.transform.SetParent(root.transform, false);
        added.transform.localPosition = new Vector3(0f, 2f, 0f);
        var addedHolder = added.AddComponent<Holder>();
        addedHolder.target = root;
        addedHolder.label = "a label long enough to pass the eighty columns of a line, so that the writer has to continue it on the next line";
        added.AddComponent<BoxCollider>().isTrigger = true;

        var kinds = new GameObject("Kinds").AddComponent<Kinds>();
        kinds.floats = new List<float> { 0.5f, -1e-5f, 3.4028235e38f, 1e5f, 1e6f, 1e7f, 1e8f, 1e9f, 1e15f, 1e16f, 1e17f, 123456789f, 1234567.9f, 16777216f, 1e-4f, 1e-6f, 1e-8f, 1e-9f, 1e-10f, 1.5e-8f, 1e-15f, 1e-16f, 1e-30f, 1.17549435e-38f, 1e-45f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, float.MaxValue, float.Epsilon };
        kinds.flags = new[] { true, false, true };
        kinds.moods = new List<Mood> { Mood.Busy, Mood.Late };
        kinds.longs = new[] { -1L, 1L << 40 };
        kinds.bytes = new byte[] { 0, 15, 255 };
        kinds.doubles = new List<double> { 0.1, 1e-300, 1e5, 1e10, 1e15, 1e16, 1e17, 1e20, 1e21, 1e22, 123456789012345678, 1e-5, 1e-7, 1e-8, 1e-10, 1e-16, 1e-17, 0.1 + 0.2, double.MaxValue, double.Epsilon, double.NaN, double.NegativeInfinity };
        kinds.points = new List<Vector3> { new Vector3(1f, 2f, 3f) };
        kinds.pairs = new List<Pair> { new Pair { key = 1, value = 0.25f }, new Pair { key = 2, value = -4f } };
        kinds.nested = new Nested { title = "inner", pairs = new List<Pair> { new Pair { key = 3, value = 1f } } };
        kinds.nestedList = new List<Nested> { new Nested { title = "first" }, new Nested() };
        kinds.mood = Mood.Late;
        kinds.unsigned = 4000000000u;
        kinds.small = -7;
        kinds.letter = 'Z';
        kinds.matrix = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f), new Vector3(1f, 2f, 0.5f));
        kinds.curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1f), new Keyframe(1f, 2f, 0.5f, 0.5f) { weightedMode = WeightedMode.Both, inWeight = 0.2f, outWeight = 0.4f });
        kinds.curve.postWrapMode = WrapMode.Loop;
        kinds.gradient = new Gradient();
        kinds.gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 0.75f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
        kinds.padding = new RectOffset(1, 2, 3, 4);
        kinds.area = new Rect(0.5f, 1f, 10f, 20f);
        kinds.layers = 1 << 5 | 1;
        kinds.bounds = new Bounds(new Vector3(1f, 1f, 1f), new Vector3(2f, 4f, 6f));
        kinds.turn = Quaternion.Euler(0f, 90f, 0f);
        kinds.cell = new Vector2Int(-3, 4);
        kinds.hash = Hash128.Compute("scenesave");
        kinds.shade = new Color32(10, 20, 30, 40);
        kinds.multiline = "line one\nline two: \"quoted\"";
        kinds.texts = new List<string> { "tab\there", " lead", "trail ", "#hash", "- dash", "-dash", "{brace}", "[x]", "yes", "no", "null", "~", "123", "1.5", "true", "a:b", "a: b", "a #b", "a#b", "it's", "\"dq\"", "@at", "`tick", "%pct", "&amp", "*star", "!bang", "|pipe", ">gt", "?q", ",comma", "end:", "\u00e9t\u00e9", "\u4e2d", "cr\rlf", "two\n\nlines", "", "x\u0001y", "back\\slash", "trailing\n" };

        var graph = GameObject.Find("Graph").GetComponent<Graph>();
        var shared = new Circle { name = "shared", radius = 0.5f };
        graph.main = new Box { name = "box", size = new Vector2(1f, 2f), inner = shared };
        graph.shapes = new List<Shape> { shared, shared, null, new Box { name = "b2" } };

        var instance = GameObject.Find("Thing");
        instance.transform.position = new Vector3(3f, 0f, 5f);
        instance.GetComponent<Holder>().count = 11;
        instance.transform.Find("Part").gameObject.AddComponent<Holder>().label = "added to instance";
    }

    static void Prefabs(Scene scene)
    {
        var instance = GameObject.Find("Thing");
        var holder = instance.GetComponent<Holder>();
        holder.count = 12;
        PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
        ProbeLog.Row("prefab.recorded.isDirty", scene.isDirty);
        ProbeLog.Try("prefab.recorded.result", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/BaseRecorded.unity", true));
        ProbeLog.Snapshot("prefab.recorded.scene", "Assets/Scenes/BaseRecorded.unity");

        var made = new GameObject("Made");
        made.AddComponent<Holder>().label = "made";
        var part = new GameObject("Made Part");
        part.transform.SetParent(made.transform, false);
        part.AddComponent<SphereCollider>().radius = 2f;
        ProbeLog.Try("prefab.made.result", () => PrefabUtility.SaveAsPrefabAsset(made, "Assets/Prefabs/Made.prefab").name);
        ProbeLog.Snapshot("prefab.made.file", "Assets/Prefabs/Made.prefab");
        ProbeLog.Row("prefab.made.isInstance", PrefabUtility.IsPartOfPrefabInstance(made));
        Object.DestroyImmediate(made);

        var contents = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Thing.prefab");
        contents.GetComponent<Holder>().count = 99;
        contents.GetComponent<Holder>().label = "edited contents";
        ProbeLog.Try("prefab.contents.result", () => PrefabUtility.SaveAsPrefabAsset(contents, "Assets/Prefabs/Thing.prefab").name);
        PrefabUtility.UnloadPrefabContents(contents);
        ProbeLog.Snapshot("prefab.contents.file", "Assets/Prefabs/Thing.prefab");
        ProbeLog.Row("prefab.contents.instanceCount", GameObject.Find("Thing").GetComponent<Holder>().count);
        ProbeLog.Row("prefab.contents.instanceLabel", GameObject.Find("Thing").GetComponent<Holder>().label);

        instance = GameObject.Find("Thing");
        ProbeLog.Try("prefab.apply.result", () => { PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction); return "done"; });
        ProbeLog.Snapshot("prefab.apply.file", "Assets/Prefabs/Thing.prefab");
        ProbeLog.Row("prefab.apply.isDirty", scene.isDirty);
        ProbeLog.Try("prefab.apply.saveResult", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/BaseApplied.unity", true));
        ProbeLog.Snapshot("prefab.apply.scene", "Assets/Scenes/BaseApplied.unity");
    }

    static void EditAssets()
    {
        var unsaved = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataUnsaved.asset");
        var matEdit = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatEdit.mat");
        unsaved.count = 10;
        matEdit.color = Color.red;
        ProbeLog.Row("asset.unsaved.isDirty", EditorUtility.IsDirty(unsaved));
        ProbeLog.Row("asset.matEdit.isDirty", EditorUtility.IsDirty(matEdit));
        AssetDatabase.SaveAssets();
        ProbeLog.Row("asset.save1.unsaved", ProbeLog.Hash("Assets/Data/DataUnsaved.asset"));
        ProbeLog.Row("asset.save1.matEdit", ProbeLog.Hash("Assets/Materials/MatEdit.mat"));
        ProbeLog.Row("asset.save1.matEdit.isDirty", EditorUtility.IsDirty(matEdit));

        var saved = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataSaved.asset");
        saved.count = 20;
        saved.label = "saved: yes";
        EditorUtility.SetDirty(saved);
        ProbeLog.Row("asset.saved.isDirty", EditorUtility.IsDirty(saved));
        AssetDatabase.SaveAssets();
        ProbeLog.Row("asset.save2.saved", ProbeLog.Hash("Assets/Data/DataSaved.asset"));
        ProbeLog.Row("asset.save2.saved.isDirty", EditorUtility.IsDirty(saved));

        var quit = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataQuit.asset");
        quit.count = 30;
        EditorUtility.SetDirty(quit);
        var matQuit = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatQuit.mat");
        matQuit.SetFloat("_Glossiness", 0.75f);
        ProbeLog.Row("asset.quit.isDirty", EditorUtility.IsDirty(quit));
        ProbeLog.Row("asset.matQuit.isDirty", EditorUtility.IsDirty(matQuit));
    }
}
