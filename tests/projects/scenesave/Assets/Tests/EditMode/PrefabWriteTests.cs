using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Edit mode: the prefab writes one override at a time (ApplyPropertyOverride and
/// RevertPropertyOverride, added and removed components and GameObjects applied and reverted,
/// ObjectOverride.Apply and Revert, RevertPrefabInstance), SavePrefabAsset of an edited asset, the
/// writes that make new prefabs (SaveAsPrefabAsset of an instance and of a prefab's contents to a
/// new path, SaveAsPrefabAssetAndConnect), argument errors, an undo and redo of an apply, and
/// EditorSceneManager.ReloadScene. Each step logs its result, the instance's modifications, the
/// prefabInstanceUpdated calls, the ObjectChangeEvents published in the next Editor update and
/// the log messages it raised; snapshots keep the written prefabs and scenes.
/// </summary>
public class PrefabWriteTests
{
    const string ThingPath = "Assets/Prefabs/Thing.prefab";

    static string s_Step = "setup";

    static string Name(Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    static string NameOf(int instanceId) => Name(EditorUtility.InstanceIDToObject(instanceId));

    static string Mods(GameObject instance)
    {
        var mods = PrefabUtility.GetPropertyModifications(instance);
        if (mods == null) return "null";
        return string.Join("; ", mods.Select(m => $"{Name(m.target)}|{m.propertyPath}|{m.value}|{Name(m.objectReference)}"));
    }

    static void Overrides(string key, GameObject instance)
    {
        ProbeLog.Row(key + ".mods", Mods(instance));
        ProbeLog.Row(key + ".objects", string.Join(",", PrefabUtility.GetObjectOverrides(instance).Select(o => Name(o.instanceObject))));
        ProbeLog.Row(key + ".added", string.Join(",", PrefabUtility.GetAddedComponents(instance).Select(o => Name(o.instanceComponent))));
        ProbeLog.Row(key + ".removed", string.Join(",", PrefabUtility.GetRemovedComponents(instance).Select(o => Name(o.assetComponent))));
        ProbeLog.Row(key + ".addedObjects", string.Join(",", PrefabUtility.GetAddedGameObjects(instance).Select(o => Name(o.instanceGameObject))));
    }

    static void OnUpdated(GameObject instance) => ProbeLog.Row(s_Step + ".updated", Name(instance));

    static void OnLog(string message, string stack, LogType type) => ProbeLog.Row(s_Step + ".log", type + ": " + message);

    static void OnChanges(ref ObjectChangeEventStream stream)
    {
        for (int i = 0; i < stream.length; i++)
        {
            var kind = stream.GetEventType(i);
            string text;
            switch (kind)
            {
                case ObjectChangeKind.ChangeScene: stream.GetChangeSceneEvent(i, out var scene); text = scene.scene.name; break;
                case ObjectChangeKind.CreateGameObjectHierarchy: stream.GetCreateGameObjectHierarchyEvent(i, out var created); text = NameOf(created.instanceId); break;
                case ObjectChangeKind.ChangeGameObjectStructureHierarchy: stream.GetChangeGameObjectStructureHierarchyEvent(i, out var hierarchy); text = NameOf(hierarchy.instanceId); break;
                case ObjectChangeKind.ChangeGameObjectStructure: stream.GetChangeGameObjectStructureEvent(i, out var structure); text = NameOf(structure.instanceId); break;
                case ObjectChangeKind.ChangeGameObjectParent: stream.GetChangeGameObjectParentEvent(i, out var parent); text = NameOf(parent.instanceId) + " " + NameOf(parent.previousParentInstanceId) + ">" + NameOf(parent.newParentInstanceId); break;
                case ObjectChangeKind.ChangeGameObjectOrComponentProperties: stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var properties); text = NameOf(properties.instanceId); break;
                case ObjectChangeKind.DestroyGameObjectHierarchy: stream.GetDestroyGameObjectHierarchyEvent(i, out var destroyed); text = NameOf(destroyed.instanceId) + " parent=" + NameOf(destroyed.parentInstanceId); break;
                case ObjectChangeKind.CreateAssetObject: stream.GetCreateAssetObjectEvent(i, out var asset); text = NameOf(asset.instanceId) + " " + AssetDatabase.GUIDToAssetPath(asset.guid); break;
                case ObjectChangeKind.DestroyAssetObject: stream.GetDestroyAssetObjectEvent(i, out var gone); text = AssetDatabase.GUIDToAssetPath(gone.guid); break;
                case ObjectChangeKind.ChangeAssetObjectProperties: stream.GetChangeAssetObjectPropertiesEvent(i, out var changed); text = NameOf(changed.instanceId) + " " + AssetDatabase.GUIDToAssetPath(changed.guid); break;
                case ObjectChangeKind.UpdatePrefabInstances:
                    stream.GetUpdatePrefabInstancesEvent(i, out var updated);
                    text = updated.scene.name + " " + string.Join(",", updated.instanceIds.Select(NameOf));
                    break;
                case ObjectChangeKind.ChangeChildrenOrder: stream.GetChangeChildrenOrderEvent(i, out var children); text = NameOf(children.instanceId); break;
                case ObjectChangeKind.ChangeRootOrder: stream.GetChangeRootOrderEvent(i, out var roots); text = NameOf(roots.instanceId); break;
                default: text = ""; break;
            }
            ProbeLog.Row(s_Step + ".event", kind + " " + text);
        }
    }

    static void OnUndoRedo(in UndoRedoInfo info) => ProbeLog.Row(s_Step + ".undone", info.undoName);

    static void OnSceneOpening(string path, OpenSceneMode mode) => ProbeLog.Row(s_Step + ".scene", "opening " + path + " " + mode);
    static void OnSceneOpened(Scene scene, OpenSceneMode mode) => ProbeLog.Row(s_Step + ".scene", "opened " + scene.name + " " + mode);
    static void OnSceneClosing(Scene scene, bool removing) => ProbeLog.Row(s_Step + ".scene", "closing " + scene.name + " " + removing);
    static void OnSceneClosed(Scene scene) => ProbeLog.Row(s_Step + ".scene", "closed " + scene.name);
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ProbeLog.Row(s_Step + ".scene", "loaded " + scene.name + " " + mode);
    static void OnSceneUnloaded(Scene scene) => ProbeLog.Row(s_Step + ".scene", "unloaded " + scene.name);
    static void OnActiveChanged(Scene from, Scene to) => ProbeLog.Row(s_Step + ".scene", "active " + from.name + ">" + to.name);

    static GameObject Asset(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    /// <summary>ReloadScene is internal: called by reflection, its own exception unwrapped.</summary>
    static object Reload(MethodInfo reload, Scene scene)
    {
        try { return reload.Invoke(null, new object[] { scene }); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }

    static SerializedProperty Property(Object target, string path) => new SerializedObject(target).FindProperty(path);

    [UnityTest]
    public IEnumerator PrefabWrites()
    {
        LogAssert.ignoreFailingMessages = true;
        PrefabUtility.prefabInstanceUpdated += OnUpdated;
        ObjectChangeEvents.changesPublished += OnChanges;
        Application.logMessageReceived += OnLog;
        EditorSceneManager.sceneOpening += OnSceneOpening;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorSceneManager.sceneClosing += OnSceneClosing;
        EditorSceneManager.sceneClosed += OnSceneClosed;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveChanged;
        try
        {
            var settings = new ConvertToPrefabInstanceSettings();
            ProbeLog.Row("settings.defaults", $"{settings.objectMatchMode}|{settings.componentsNotMatchedBecomesOverride}|{settings.gameObjectsNotMatchedBecomesOverride}|{settings.recordPropertyOverridesOfMatches}|{settings.changeRootNameToAssetName}|{settings.logInfo}");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Base.unity", OpenSceneMode.Single);
            yield return null;
            var instance = GameObject.Find("Thing");
            var holder = instance.GetComponent<Holder>();
            var part = instance.transform.Find("Part").gameObject;
            var asset = Asset(ThingPath);

            s_Step = "propApply";
            holder.count = 40;
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
            yield return null;
            ProbeLog.Try("propApply.result", () => { PrefabUtility.ApplyPropertyOverride(Property(holder, "count"), ThingPath, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("propApply.asset", asset.GetComponent<Holder>().count);
            ProbeLog.Row("propApply.isDirty", scene.isDirty);
            ProbeLog.Snapshot("propApply.prefab", ThingPath);
            Overrides("propApply", instance);
            yield return null;

            s_Step = "undoApply";
            Undo.PerformUndo();
            ProbeLog.Row("undoApply.instance", holder.count);
            ProbeLog.Row("undoApply.asset", asset.GetComponent<Holder>().count);
            ProbeLog.Row("undoApply.prefab", ProbeLog.Hash(ThingPath));
            Overrides("undoApply", instance);
            yield return null;

            s_Step = "redoApply";
            Undo.PerformRedo();
            ProbeLog.Row("redoApply.instance", holder.count);
            ProbeLog.Row("redoApply.asset", asset.GetComponent<Holder>().count);
            ProbeLog.Row("redoApply.prefab", ProbeLog.Hash(ThingPath));
            Overrides("redoApply", instance);
            yield return null;

            s_Step = "propRevert";
            var label = new SerializedObject(holder);
            label.FindProperty("label").stringValue = "temporary";
            label.ApplyModifiedProperties();
            yield return null;
            Overrides("propRevert.before", instance);
            ProbeLog.Try("propRevert.result", () => { PrefabUtility.RevertPropertyOverride(Property(holder, "label"), InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("propRevert.label", holder.label);
            Overrides("propRevert", instance);
            yield return null;

            s_Step = "compApply";
            var sphere = part.AddComponent<SphereCollider>();
            sphere.radius = 3f;
            yield return null;
            ProbeLog.Row("compApply.isAdded", PrefabUtility.IsAddedComponentOverride(sphere));
            ProbeLog.Try("compApply.result", () => { PrefabUtility.ApplyAddedComponent(sphere, ThingPath, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("compApply.isInstance", PrefabUtility.IsPartOfPrefabInstance(sphere));
            ProbeLog.Row("compApply.source", Name(PrefabUtility.GetCorrespondingObjectFromSource(sphere)));
            ProbeLog.Snapshot("compApply.prefab", ThingPath);
            Overrides("compApply", instance);
            yield return null;

            s_Step = "compRevert";
            var capsule = part.AddComponent<CapsuleCollider>();
            yield return null;
            ProbeLog.Try("compRevert.result", () => { PrefabUtility.RevertAddedComponent(capsule, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("compRevert.gone", capsule == null);
            Overrides("compRevert", instance);
            yield return null;

            s_Step = "removeRevert";
            var assetBox = asset.transform.Find("Part").GetComponent<BoxCollider>();
            ProbeLog.Try("removeRevert.destroy", () => { Undo.DestroyObjectImmediate(part.GetComponent<BoxCollider>()); return "done"; });
            ProbeLog.Row("removeRevert.hasBox", part.GetComponent<BoxCollider>() != null);
            Overrides("removeRevert.before", instance);
            yield return null;
            ProbeLog.Try("removeRevert.result", () => { PrefabUtility.RevertRemovedComponent(part, assetBox, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("removeRevert.restored", Name(part.GetComponent<BoxCollider>()));
            ProbeLog.Row("removeRevert.size", part.GetComponent<BoxCollider>() == null ? "none" : part.GetComponent<BoxCollider>().size.ToString());
            Overrides("removeRevert", instance);
            yield return null;

            s_Step = "removeApply";
            ProbeLog.Try("removeApply.destroy", () => { Undo.DestroyObjectImmediate(part.GetComponent<BoxCollider>()); return "done"; });
            yield return null;
            ProbeLog.Try("removeApply.result", () => { PrefabUtility.ApplyRemovedComponent(part, assetBox, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("removeApply.assetBox", assetBox == null);
            ProbeLog.Snapshot("removeApply.prefab", ThingPath);
            Overrides("removeApply", instance);
            yield return null;

            s_Step = "goApply";
            var extra = new GameObject("Extra");
            extra.transform.SetParent(part.transform, false);
            extra.transform.localPosition = new Vector3(0f, 0f, 2f);
            extra.AddComponent<Holder>().label = "extra";
            yield return null;
            ProbeLog.Row("goApply.isAdded", PrefabUtility.IsAddedGameObjectOverride(extra));
            ProbeLog.Try("goApply.result", () => { PrefabUtility.ApplyAddedGameObject(extra, ThingPath, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("goApply.isInstance", PrefabUtility.IsPartOfPrefabInstance(extra));
            ProbeLog.Snapshot("goApply.prefab", ThingPath);
            Overrides("goApply", instance);
            yield return null;

            s_Step = "goRevert";
            var other = new GameObject("Other");
            other.transform.SetParent(instance.transform, false);
            yield return null;
            ProbeLog.Try("goRevert.result", () => { PrefabUtility.RevertAddedGameObject(other, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("goRevert.gone", other == null);
            Overrides("goRevert", instance);
            yield return null;

            s_Step = "objApply";
            part.transform.localPosition = new Vector3(0f, 2f, 0f);
            holder.ratio = 0.75f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
            yield return null;
            Overrides("objApply.before", instance);
            ProbeLog.Try("objApply.result", () =>
            {
                var o = PrefabUtility.GetObjectOverrides(instance).First(x => x.instanceObject == part.transform);
                o.Apply(ThingPath, InteractionMode.UserAction);
                return Name(o.GetAssetObject());
            });
            ProbeLog.Snapshot("objApply.prefab", ThingPath);
            Overrides("objApply", instance);
            yield return null;

            s_Step = "objRevert";
            part.transform.localScale = new Vector3(2f, 2f, 2f);
            yield return null;
            ProbeLog.Try("objRevert.result", () =>
            {
                var o = PrefabUtility.GetObjectOverrides(instance).First(x => x.instanceObject == part.transform);
                o.Revert(InteractionMode.UserAction);
                return part.transform.localScale.ToString();
            });
            ProbeLog.Row("objRevert.ratio", holder.ratio.ToString("R"));
            Overrides("objRevert", instance);
            yield return null;

            s_Step = "instRevert";
            holder.count = 55;
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
            instance.transform.position = new Vector3(8f, 0f, 0f);
            yield return null;
            ProbeLog.Try("instRevert.result", () => { PrefabUtility.RevertPrefabInstance(instance, InteractionMode.UserAction); return "done"; });
            ProbeLog.Row("instRevert.count", holder.count);
            ProbeLog.Row("instRevert.ratio", holder.ratio.ToString("R"));
            ProbeLog.Row("instRevert.position", instance.transform.position.ToString());
            Overrides("instRevert", instance);
            yield return null;

            s_Step = "assetSave";
            asset.GetComponent<Holder>().precise = 0.125;
            ProbeLog.Row("assetSave.isDirty", EditorUtility.IsDirty(asset.GetComponent<Holder>()));
            ProbeLog.Try("assetSave.result", () => { var saved = PrefabUtility.SavePrefabAsset(asset, out var ok); return Name(saved) + "|" + ok; });
            ProbeLog.Row("assetSave.instance", holder.precise.ToString("R"));
            ProbeLog.Snapshot("assetSave.prefab", ThingPath);
            yield return null;

            s_Step = "sceneSave";
            ProbeLog.Try("sceneSave.result", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/WritesApplied.unity", true));
            ProbeLog.Snapshot("sceneSave.scene", "Assets/Scenes/WritesApplied.unity");
            yield return null;

            s_Step = "variant";
            ProbeLog.Try("variant.result", () => { var made = PrefabUtility.SaveAsPrefabAsset(instance, "Assets/Prefabs/ThingVariant.prefab", out var ok); return Name(made) + "|" + ok; });
            ProbeLog.Row("variant.type", PrefabUtility.GetPrefabAssetType(Asset("Assets/Prefabs/ThingVariant.prefab")).ToString());
            ProbeLog.Row("variant.instancePath", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance));
            ProbeLog.Snapshot("variant.prefab", "Assets/Prefabs/ThingVariant.prefab");
            yield return null;

            s_Step = "connect";
            var connected = new GameObject("Connected");
            connected.AddComponent<Holder>().label = "connected";
            var connectedChild = new GameObject("Connected Child");
            connectedChild.transform.SetParent(connected.transform, false);
            yield return null;
            ProbeLog.Try("connect.result", () => { var made = PrefabUtility.SaveAsPrefabAssetAndConnect(connected, "Assets/Prefabs/Connected.prefab", InteractionMode.UserAction, out var ok); return Name(made) + "|" + ok; });
            ProbeLog.Row("connect.isInstance", PrefabUtility.IsPartOfPrefabInstance(connected));
            ProbeLog.Row("connect.isRoot", PrefabUtility.IsOutermostPrefabInstanceRoot(connected));
            ProbeLog.Row("connect.name", connected.name);
            // In the order of the new prefab's random file ids: compared as a set.
            ProbeLog.Row("connect.mods", string.Join("; ", Mods(connected).Split("; ").OrderBy(m => m, StringComparer.Ordinal)));
            ProbeLog.Snapshot("connect.prefab", "Assets/Prefabs/Connected.prefab");
            yield return null;

            s_Step = "contentsCopy";
            var contents = PrefabUtility.LoadPrefabContents(ThingPath);
            ProbeLog.Row("contentsCopy.name", contents.name);
            ProbeLog.Row("contentsCopy.preview", EditorSceneManager.IsPreviewScene(contents.scene));
            ProbeLog.Row("contentsCopy.isAsset", PrefabUtility.IsPartOfPrefabAsset(contents));
            contents.GetComponent<Holder>().count = 66;
            ProbeLog.Try("contentsCopy.result", () => Name(PrefabUtility.SaveAsPrefabAsset(contents, "Assets/Prefabs/ThingCopy.prefab")));
            PrefabUtility.UnloadPrefabContents(contents);
            ProbeLog.Row("contentsCopy.unloaded", contents == null);
            ProbeLog.Snapshot("contentsCopy.prefab", "Assets/Prefabs/ThingCopy.prefab");
            ProbeLog.Row("contentsCopy.thing", ProbeLog.Hash(ThingPath));
            yield return null;

            s_Step = "contentsSave";
            contents = PrefabUtility.LoadPrefabContents(ThingPath);
            contents.GetComponent<Holder>().label = "from contents";
            ProbeLog.Try("contentsSave.result", () => Name(PrefabUtility.SaveAsPrefabAsset(contents, ThingPath)));
            PrefabUtility.UnloadPrefabContents(contents);
            ProbeLog.Row("contentsSave.instance", holder.label);
            ProbeLog.Snapshot("contentsSave.prefab", ThingPath);
            yield return null;

            s_Step = "errors";
            var plain = new GameObject("Plain");
            ProbeLog.Try("errors.extension", () => Name(PrefabUtility.SaveAsPrefabAsset(plain, "Assets/Prefabs/Bad.asset")));
            ProbeLog.Try("errors.nullRoot", () => Name(PrefabUtility.SaveAsPrefabAsset(null, "Assets/Prefabs/Null.prefab")));
            ProbeLog.Try("errors.folder", () => Name(PrefabUtility.SaveAsPrefabAsset(plain, "Assets/Missing/Plain.prefab")));
            ProbeLog.Try("errors.load", () => Name(PrefabUtility.LoadPrefabContents("Assets/Prefabs/Missing.prefab")));
            ProbeLog.Try("errors.unload", () => { PrefabUtility.UnloadPrefabContents(plain); return "done"; });
            ProbeLog.Try("errors.applyPlain", () => { PrefabUtility.ApplyPrefabInstance(plain, InteractionMode.AutomatedAction); return "done"; });
            ProbeLog.Try("errors.revertPlain", () => { PrefabUtility.RevertPrefabInstance(plain, InteractionMode.AutomatedAction); return "done"; });
            ProbeLog.Row("errors.files", string.Join(",", new[] { "Bad.asset", "Null.prefab", "Plain.prefab" }.Where(f => System.IO.File.Exists("Assets/Prefabs/" + f))));
            Object.DestroyImmediate(plain);
            yield return null;

            s_Step = "sceneFinal";
            ProbeLog.Try("sceneFinal.result", () => EditorSceneManager.SaveScene(scene, "Assets/Scenes/WritesFinal.unity", true));
            ProbeLog.Snapshot("sceneFinal.scene", "Assets/Scenes/WritesFinal.unity");
            yield return null;

            // A record that changes nothing, in its own group: whether the undo history outlives
            // the reload.
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(asset.GetComponent<Holder>(), "Probe Record");
            Undo.IncrementCurrentGroup();
            var reload = typeof(EditorSceneManager).GetMethod("ReloadScene", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Scene) }, null);
            ProbeLog.Row("reload.method", reload != null);
            s_Step = "reload";
            EditorSceneManager.MarkSceneDirty(scene);
            ProbeLog.Row("reload.before", scene.isDirty + "|" + scene.rootCount);
            ProbeLog.Try("reload.result", () => Reload(reload, scene));
            ProbeLog.Row("reload.after", scene.isDirty + "|" + scene.rootCount + "|" + scene.path + "|" + scene.isLoaded);
            ProbeLog.Row("reload.active", SceneManager.GetActiveScene().path);
            ProbeLog.Row("reload.oldInstance", instance == null);
            ProbeLog.Row("reload.roots", string.Join(",", SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.name)));
            ProbeLog.Row("reload.thing", GameObject.Find("Thing")?.GetComponent<Holder>().label ?? "none");
            yield return null;

            s_Step = "reloadUndo";
            Undo.undoRedoEvent += OnUndoRedo;
            Undo.PerformUndo();
            Undo.undoRedoEvent -= OnUndoRedo;
            ProbeLog.Row("reloadUndo.done", true);
            yield return null;

            s_Step = "reloadClean";
            var current = SceneManager.GetActiveScene();
            ProbeLog.Try("reloadClean.result", () => Reload(reload, current));
            ProbeLog.Row("reloadClean.after", current.isDirty + "|" + current.rootCount);
            yield return null;

            s_Step = "reloadNew";
            var fresh = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(new GameObject("In Fresh"), fresh);
            ProbeLog.Try("reloadNew.result", () => Reload(reload, fresh));
            ProbeLog.Row("reloadNew.after", fresh.isDirty + "|" + fresh.rootCount + "|" + fresh.isLoaded);
            ProbeLog.Try("reloadInvalid.result", () => Reload(reload, default(Scene)));
            yield return null;
            s_Step = "end";
        }
        finally
        {
            PrefabUtility.prefabInstanceUpdated -= OnUpdated;
            ObjectChangeEvents.changesPublished -= OnChanges;
            Application.logMessageReceived -= OnLog;
            EditorSceneManager.sceneOpening -= OnSceneOpening;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveChanged;
        }
    }
}
