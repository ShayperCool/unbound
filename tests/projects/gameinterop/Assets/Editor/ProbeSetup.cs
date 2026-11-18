using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Timeline;

/// <summary>
/// Authors the probe's scene and assets with the Editor itself:
/// `Unity -batchmode -projectPath tests/projects/gameinterop -executeMethod ProbeSetup.Build -quit`.
/// CutsceneMissing.playable is then made from Cutscene.playable by hand: its ProbeMarker
/// documents name a script GUID that does not exist (see README.md).
/// </summary>
public static class ProbeSetup
{
    const string Res = "Assets/Resources/";

    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset("Assets/Resources");
        Directory.CreateDirectory(Res + "Hier");
        AssetDatabase.Refresh();

        // Scripts whose constructors all take arguments.
        var data = ScriptableObject.CreateInstance<ArgOnlyData>();
        Debug.Log("ProbeSetup: CreateInstance<ArgOnlyData> " + (data == null ? "null" : "made"));
        AssetDatabase.CreateAsset(data, Res + "ArgData.asset");
        Set(data, so => { so.FindProperty("number").intValue = 17; List(so, "list", 9); });

        var prefabRoot = new GameObject("ArgPrefab");
        ArgObjects(prefabRoot, 13, "prefab", data);
        Save(prefabRoot, Res + "ArgPrefab.prefab");

        // Hierarchy edits of loaded prefab assets.
        foreach (var name in new[] { "EditNone", "EditInside", "EditNull", "EditProperty", "EditScene", "EditLoose", "EditSibling",
                     "EditInstantiate", "EditInstantiateWorld", "EditDetach", "EditRect", "EditDestroy", "EditNewChild" })
            Save(Rig(name), Res + "Hier/" + name + ".prefab");
        var piece = new GameObject("Piece");
        Child(piece, "Tip");
        Save(piece, Res + "Hier/Piece.prefab");
        Save(RiggedValidator("ValidateReparent"), Res + "Hier/ValidateReparent.prefab");
        Save(RiggedValidator("ValidateAdd"), Res + "Hier/ValidateAdd.prefab");

        // A rig nested in a box, with an object added inside the instance; a variant of the box.
        var inner = PrefabUtility.SaveAsPrefabAsset(RiggedValidator("Inner"), Res + "Hier/Inner.prefab");
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)) Object.DestroyImmediate(go);
        foreach (var name in new[] { "Outer", "OuterValidate" })
        {
            var box = new GameObject("Box");
            Child(box, "Lid");
            var figure = (GameObject)PrefabUtility.InstantiatePrefab(inner, box.transform);
            figure.name = "Figure";
            figure.transform.localPosition = new Vector3(1, 2, 3);
            Child(figure.transform.Find("Body/Head").gameObject, "Hat");
            Save(box, Res + "Hier/" + name + ".prefab");
        }
        var outer = AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Hier/Outer.prefab");
        var variant = (GameObject)PrefabUtility.InstantiatePrefab(outer);
        Child(variant.transform.Find("Figure/Body").gameObject, "Extra");
        PrefabUtility.SaveAsPrefabAsset(variant, Res + "Hier/OuterVariant.prefab");
        Object.DestroyImmediate(variant);

        // Animator controllers.
        var controller = AnimatorController.CreateAnimatorControllerAtPath(Res + "Anim.controller");
        controller.AddParameter("speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("go", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("flag", AnimatorControllerParameterType.Bool);
        controller.layers[0].stateMachine.AddState("Idle");
        var overrideController = new AnimatorOverrideController(controller);
        AssetDatabase.CreateAsset(overrideController, Res + "Override.overrideController");
        var emptyOverride = new AnimatorOverrideController();
        AssetDatabase.CreateAsset(emptyOverride, Res + "OverrideEmpty.overrideController");

        // A timeline with clips, a group, signal and probe markers.
        var move = new AnimationClip();
        move.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
        AssetDatabase.CreateAsset(move, Res + "Move.anim");
        var ping = ScriptableObject.CreateInstance<SignalAsset>();
        AssetDatabase.CreateAsset(ping, Res + "Ping.signal");
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, Res + "Cutscene.playable");
        var moveTrack = timeline.CreateTrack<AnimationTrack>(null, "Move");
        moveTrack.CreateClip(move);
        moveTrack.CreateMarker<SignalEmitter>(0.5).asset = ping;
        timeline.CreateTrack<ActivationTrack>(null, "Show").CreateDefaultClip();
        var group = timeline.CreateTrack<GroupTrack>(null, "Group");
        var innerTrack = timeline.CreateTrack<AnimationTrack>(group, "Inner");
        innerTrack.CreateMarker<ProbeMarker>(1.0);
        timeline.CreateMarkerTrack();
        timeline.markerTrack.CreateMarker<ProbeMarker>(0.25);
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();

        // The scene.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Runner").AddComponent<GameInteropProbe>();
        ArgObjects(new GameObject("ArgScene"), 11, "scene", data);
        var anims = new GameObject("Anims");
        AddAnimator(anims, "Bare", null, true, true);
        AddAnimator(anims, "Inactive", controller, false, true);
        AddAnimator(anims, "Disabled", controller, true, false);
        AddAnimator(anims, "OverrideEmpty", emptyOverride, true, true);
        AddAnimator(anims, "Override", overrideController, true, true);
        AddAnimator(anims, "Controller", controller, true, true);
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static void ArgObjects(GameObject go, int number, string text, ArgOnlyData data)
    {
        var argOnly = go.AddComponent<ArgOnlyBehaviour>();
        var derived = go.AddComponent<ArgDerived>();
        var privateCtor = go.AddComponent<PrivateCtorBehaviour>();
        var holder = go.AddComponent<ArgHolder>();
        Debug.Log($"ProbeSetup: AddComponent {go.name}: {(argOnly == null ? "null" : "made")} {(derived == null ? "null" : "made")} {(privateCtor == null ? "null" : "made")}");
        Set(argOnly, so =>
        {
            so.FindProperty("number").intValue = number;
            so.FindProperty("text").stringValue = text;
            List(so, "list", 4);
            List(so, "array", 8);
            so.FindProperty("nested.a").intValue = 12;
        });
        Set(derived, so => { so.FindProperty("baseNumber").intValue = 20; so.FindProperty("derivedNumber").intValue = 30; });
        Set(holder, so =>
        {
            so.FindProperty("argOnly").objectReferenceValue = argOnly;
            so.FindProperty("derived").objectReferenceValue = derived;
            so.FindProperty("privateCtor").objectReferenceValue = privateCtor;
            so.FindProperty("data").objectReferenceValue = data;
        });
    }

    static void Set(Object target, System.Action<SerializedObject> edit)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void List(SerializedObject so, string name, int value)
    {
        var list = so.FindProperty(name);
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).intValue = value;
    }

    static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    static GameObject Rig(string name)
    {
        var root = new GameObject(name);
        var body = Child(root, "Body");
        Child(body, "Head");
        Child(body, "Arm");
        Child(Child(root, "Base"), "Foot");
        return root;
    }

    static GameObject RiggedValidator(string name)
    {
        var root = Rig(name);
        var rig = root.AddComponent<LimbRig>();
        rig.limb = root.transform.Find("Body/Head");
        rig.newParent = root.transform.Find("Base");
        return root;
    }

    static void AddAnimator(GameObject parent, string name, RuntimeAnimatorController controller, bool active, bool enabled)
    {
        var go = Child(parent, name);
        var animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.enabled = enabled;
        go.SetActive(active);
    }

    static void Save(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }
}
