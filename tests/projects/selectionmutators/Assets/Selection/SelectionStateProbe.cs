using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

static class SelectionStateProbe
{
    internal static void Record(Action<string, string> add, Action finish)
    {
        var a = new GameObject("A2");
        var b = new GameObject("B2");
        RecordActiveComponent(a, b, add);
        RecordAssetSelection(b, add);
        RecordNotifications(a, b, add, finish);
    }

    static void RecordActiveComponent(GameObject a, GameObject b, Action<string, string> add)
    {
        var collider = a.AddComponent<BoxCollider>();
        string Identity(int value) => value == a.GetInstanceID() ? "owner" : value == b.GetInstanceID() ? "B2"
            : value == a.transform.GetInstanceID() ? "transform" : value == collider.GetInstanceID() ? "collider"
            : value == 0 ? "zero" : "other";
        string Name(Object obj) => obj == null ? "null" : Identity(obj.GetInstanceID());
        string Ids() => string.Join(",", Selection.instanceIDs.Select(Identity));
        void State(string key) => add(key, "ids=" + Ids() + "|activeId=" + Identity(Selection.activeInstanceID)
            + "|active=" + Name(Selection.activeObject) + "|transform=" + Name(Selection.activeTransform)
            + "|context=" + Name(Selection.activeContext) + "|objects=" + string.Join(",", Selection.objects.Select(Name)));

        Selection.objects = new Object[] { a, b };
        Selection.activeTransform = a.transform;
        State("component.activeTransform");
        Selection.objects = new Object[] { a, b };
        Selection.activeObject = collider;
        State("component.activeObject");
        Selection.activeInstanceID = a.transform.GetInstanceID();
        State("component.activeId");
        Selection.SetActiveObjectWithContext(collider, b);
        State("component.context");
        var colliderId = collider.GetInstanceID();
        Object.DestroyImmediate(collider);
        add("component.destroyActive", "ids=" + Ids() + "|activeId=" + Identity(Selection.activeInstanceID)
            + "|activeIsDestroyed=" + (Selection.activeInstanceID == colliderId) + "|active=" + Name(Selection.activeObject));
        Selection.objects = new Object[] { a.transform, b };
        State("component.objects");
        Selection.instanceIDs = new[] { a.transform.GetInstanceID(), b.GetInstanceID() };
        State("component.ids");
    }

    static void RecordAssetSelection(GameObject b, Action<string, string> add)
    {
        const string assetPath = "Assets/Selection Reload.asset";
        const string materialPath = "Assets/Selection Material.mat";
        var asset = ScriptableObject.CreateInstance<SelectionProbeAsset>();
        var material = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.CreateAsset(material, materialPath);
        AssetDatabase.SaveAssets();
        int assetId = asset.GetInstanceID(), materialId = material.GetInstanceID(), bId = b.GetInstanceID();
        string Name(int id) => id == assetId ? "asset" : id == materialId ? "material" : id == bId ? "B2" : id == 0 ? "zero" : "other";
        string Ids() => string.Join(",", Selection.instanceIDs.Select(Name));
        bool Loaded() => AssetDatabase.IsMainAssetAtPathLoaded(assetPath);
        string Raw() => "loaded=" + Loaded() + "|ids=" + Ids() + "|active=" + Name(Selection.activeInstanceID)
            + "|contains=" + Selection.Contains(assetId);
        void Resolved(string key) => add(key, "active=" + (Selection.activeObject == null ? "null" : Name(Selection.activeObject.GetInstanceID()))
            + "|objects=" + string.Join(",", Selection.objects.Select(o => Name(o.GetInstanceID()))) + "|loaded=" + Loaded());
        void PrepareUnloaded()
        {
            Selection.activeObject = b;
            Resources.UnloadAsset(AssetDatabase.LoadMainAssetAtPath(assetPath));
        }

        Selection.objects = new Object[] { material, b };
        Resources.UnloadAsset(material);
        add("material.unload", "wrapperNull=" + (material == null) + "|ids=" + Ids()
            + "|active=" + Name(Selection.activeInstanceID) + "|contains=" + Selection.Contains(materialId));
        Resolved("material.afterUnload");

        PrepareUnloaded();
        Selection.instanceIDs = new[] { assetId, bId };
        add("reload.first.raw", Raw());
        Resolved("reload.first.resolved");
        PrepareUnloaded();
        Selection.activeInstanceID = assetId;
        add("reload.activeId.raw", Raw());
        Resolved("reload.activeId.resolved");

        PrepareUnloaded();
        Selection.instanceIDs = new[] { bId, assetId };
        add("reload.second.raw", Raw());
        add("reload.second.count", "count=" + Selection.count + "|loaded=" + Loaded());
        Resolved("reload.second.resolved");

        PrepareUnloaded();
        var addById = typeof(Selection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(int) }, null);
        addById.Invoke(null, new object[] { assetId });
        add("reload.add.raw", Raw());
        Resolved("reload.add.resolved");

        Undo.ClearAll();
        Selection.instanceIDs = new[] { assetId, bId };
        Undo.IncrementCurrentGroup();
        Selection.activeObject = b;
        Undo.IncrementCurrentGroup();
        Resources.UnloadAsset(AssetDatabase.LoadMainAssetAtPath(assetPath));
        Undo.PerformUndo();
        add("reload.undo.raw", Raw());
        Resolved("reload.undo.resolved");

        foreach (var (key, query) in new (string, Func<int>)[]
        {
            ("gameObjects", () => Selection.gameObjects.Length),
            ("transforms", () => Selection.transforms.Length),
            ("assetGUIDs", () => Selection.assetGUIDs.Length),
            ("filtered", () => Selection.GetFiltered<ScriptableObject>(SelectionMode.Assets).Length),
        })
        {
            PrepareUnloaded();
            Selection.instanceIDs = new[] { bId, assetId };
            add("reload.query." + key, "count=" + query() + "|loaded=" + Loaded());
        }
    }

    static void RecordNotifications(GameObject a, GameObject b, Action<string, string> add, Action finish)
    {
        var context = new GameObject("Context only");
        var emptyContext = new GameObject("Empty selection context");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Selection Material.mat");
        var materialId = material.GetInstanceID();
        const string scriptablePath = "Assets/Selection Reload.asset";
        SelectionProbeAsset scriptable = null;
        int scriptableId = 0;
        int tick = 0, notices = 0;
        void Changed() => notices++;
        void State(string key) => add(key, "notices=" + notices + "|count=" + Selection.count
            + "|context=" + (Selection.activeContext == null ? "null" : Selection.activeContext.name));
        void MaterialState(string key) => add(key, "notices=" + notices + "|count=" + Selection.count
            + "|activeA=" + (Selection.activeObject == a) + "|activeZero=" + (Selection.activeInstanceID == 0)
            + "|containsMaterial=" + Selection.Contains(materialId)
            + "|contextMaterial=" + (Selection.activeContext != null && Selection.activeContext.GetInstanceID() == materialId));
        void ScriptableState(string key) => add(key, "loaded=" + AssetDatabase.IsMainAssetAtPathLoaded(scriptablePath)
            + "|notices=" + notices + "|count=" + Selection.count + "|activeA=" + (Selection.activeObject == a)
            + "|activeZero=" + (Selection.activeInstanceID == 0) + "|contains=" + Selection.Contains(scriptableId)
            + "|contextMatches=" + (Selection.activeContext != null && Selection.activeContext.GetInstanceID() == scriptableId));
        void Step()
        {
            switch (++tick)
            {
                case 1: Selection.SetActiveObjectWithContext(a, context); break;
                case 4:
                    notices = 0;
                    Object.DestroyImmediate(context);
                    State("notify.destroyContext.now");
                    break;
                case 7:
                    State("notify.destroyContext.next");
                    Selection.SetActiveObjectWithContext(null, emptyContext);
                    break;
                case 10:
                    notices = 0;
                    Object.DestroyImmediate(emptyContext);
                    State("notify.emptyContext.now");
                    break;
                case 13:
                    State("notify.emptyContext.next");
                    Selection.objects = new Object[] { a, b };
                    break;
                case 16:
                    notices = 0;
                    Object.DestroyImmediate(b);
                    State("notify.destroySelected.now");
                    break;
                case 19:
                    State("notify.destroySelected.next");
                    Selection.SetActiveObjectWithContext(a, material);
                    break;
                case 22:
                    notices = 0;
                    Resources.UnloadAsset(material);
                    MaterialState("notify.materialContext.now");
                    break;
                case 25:
                    MaterialState("notify.materialContext.next");
                    Selection.SetActiveObjectWithContext(material, material);
                    break;
                case 28:
                    notices = 0;
                    Resources.UnloadAsset(material);
                    MaterialState("notify.materialActiveContext.now");
                    break;
                case 31:
                    MaterialState("notify.materialActiveContext.next");
                    scriptable = AssetDatabase.LoadAssetAtPath<SelectionProbeAsset>(scriptablePath);
                    scriptableId = scriptable.GetInstanceID();
                    Selection.SetActiveObjectWithContext(a, scriptable);
                    break;
                case 34:
                    notices = 0;
                    Resources.UnloadAsset(scriptable);
                    ScriptableState("notify.scriptableContext.now");
                    break;
                case 37:
                    ScriptableState("notify.scriptableContext.next");
                    scriptable = AssetDatabase.LoadAssetAtPath<SelectionProbeAsset>(scriptablePath);
                    Selection.SetActiveObjectWithContext(scriptable, scriptable);
                    break;
                case 40:
                    notices = 0;
                    Resources.UnloadAsset(scriptable);
                    ScriptableState("notify.scriptableActiveContext.now");
                    break;
                case 43:
                    ScriptableState("notify.scriptableActiveContext.next");
                    EditorApplication.update -= Step;
                    Selection.selectionChanged -= Changed;
                    finish();
                    break;
            }
        }
        Selection.selectionChanged += Changed;
        EditorApplication.update += Step;
    }
}
