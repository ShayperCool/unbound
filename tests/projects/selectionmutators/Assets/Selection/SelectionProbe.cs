using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SelectionProbe
{
    [Serializable] struct Row { public string k; public string v; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var output = Environment.GetEnvironmentVariable("SELECTION_MUTATORS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var rows = new List<string>();
        var a = new GameObject("A");
        var b = new GameObject("B");
        var c = new GameObject("C");
        int aId = a.GetInstanceID(), bId = b.GetInstanceID(), cId = c.GetInstanceID();
        var destroyed = new GameObject("Destroyed");
        int dead = destroyed.GetInstanceID();
        Object.DestroyImmediate(destroyed);
        int notices = 0;
        void Changed() => notices++;
        Selection.selectionChanged += Changed;

        void Invoke(string op, object target, Type type) => typeof(Selection)
            .GetMethod(op, BindingFlags.Static | BindingFlags.NonPublic, null, new[] { type }, null)
            .Invoke(null, new[] { target });
        void RecordCase(string key, Action change)
        {
            var before = notices;
            string result = "ok";
            try { change(); }
            catch (TargetInvocationException e) { result = e.InnerException.GetType().Name; }
            catch (ArgumentNullException e) { result = e.GetType().Name + ":" + e.ParamName; }
            var ids = Selection.instanceIDs.Select(id => id == aId ? "A" : id == bId ? "B" : id == cId ? "C" : id == 0 ? "zero" : "unknown");
            var objects = Selection.objects.Select(o => o == null ? "null" : o.name);
            string value = result + "|ids=" + string.Join(",", ids) + "|objects=" + string.Join(",", objects)
                + "|active=" + (Selection.activeObject == null ? "null" : Selection.activeObject.name)
                + "|context=" + (Selection.activeContext == null ? "null" : Selection.activeContext.name)
                + "|count=" + Selection.count + "|activeDeadId=" + (Selection.activeInstanceID == dead)
                + "|containsDead=" + Selection.Contains(dead) + "|containsZero=" + Selection.Contains(0)
                + "|immediateChanges=" + (notices - before);
            rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));
        }

        try
        {
            Selection.SetActiveObjectWithContext(a, c);
            RecordCase("add.object", () => Invoke("Add", b, typeof(Object)));
            RecordCase("add.id", () => Invoke("Add", c.GetInstanceID(), typeof(int)));
            RecordCase("add.duplicate", () => Invoke("Add", b.GetInstanceID(), typeof(int)));
            RecordCase("add.null", () => Invoke("Add", null, typeof(Object)));
            RecordCase("add.destroyed", () => Invoke("Add", destroyed, typeof(Object)));
            RecordCase("add.deadId", () => Invoke("Add", dead, typeof(int)));
            RecordCase("add.zero", () => Invoke("Add", 0, typeof(int)));
            RecordCase("remove.missingId", () => Invoke("Remove", int.MaxValue, typeof(int)));
            RecordCase("remove.active", () => Invoke("Remove", a, typeof(Object)));
            RecordCase("remove.id", () => Invoke("Remove", c.GetInstanceID(), typeof(int)));
            RecordCase("remove.null", () => Invoke("Remove", null, typeof(Object)));
            RecordCase("remove.destroyed", () => Invoke("Remove", destroyed, typeof(Object)));
            RecordCase("remove.last", () => Invoke("Remove", b, typeof(Object)));
            RecordCase("remove.empty", () => Invoke("Remove", b, typeof(Object)));
            RecordCase("setter.raw", () => Selection.instanceIDs = new[] { dead, b.GetInstanceID(), 0, b.GetInstanceID(), a.GetInstanceID(), dead });
            RecordCase("setter.copy", () => Selection.instanceIDs[0] = c.GetInstanceID());
            RecordCase("setter.unknownActive", () => Selection.activeInstanceID = dead);
            RecordCase("setter.activateMember", () => Selection.activeInstanceID = aId);
            RecordCase("setter.activateNew", () => Selection.activeInstanceID = cId);
            RecordCase("setter.zeroActive", () => Selection.activeInstanceID = 0);
            RecordCase("setter.rawZeroFirst", () => Selection.instanceIDs = new[] { 0, aId, bId });
            RecordCase("setter.objectDuplicates", () => Selection.objects = new Object[] { b, null, a, b });
            RecordCase("setter.context", () => Selection.SetActiveObjectWithContext(b, c));
            RecordCase("add.duplicateKeepsContext", () => Invoke("Add", b, typeof(Object)));
            RecordCase("remove.missingResetsContext", () => Invoke("Remove", cId, typeof(int)));
            RecordCase("add.restoreContext", () => Selection.SetActiveObjectWithContext(b, c));
            RecordCase("add.contextReset", () => Invoke("Add", a, typeof(Object)));
            RecordCase("remove.destroyActive", () => Object.DestroyImmediate(b));
            RecordCase("setter.zeroWithNoActive", () => Selection.activeInstanceID = 0);
            RecordCase("setter.sameIdDifferentContext", () => {
                Selection.objects = new Object[] { a, c };
                Selection.SetActiveObjectWithContext(a, c);
            });
            RecordCase("setter.sameObjectWithContext", () => Selection.activeObject = a);
            RecordCase("setter.sameIdWithContext", () => {
                Selection.SetActiveObjectWithContext(a, c);
                Selection.activeInstanceID = aId;
            });
            RecordCase("setter.repeatExplicitContext", () => {
                Selection.SetActiveObjectWithContext(a, c);
                Selection.SetActiveObjectWithContext(a, c);
            });
            RecordCase("setter.objectsResetContext", () => {
                Selection.SetActiveObjectWithContext(a, c);
                Selection.objects = new Object[] { a, c };
            });
            RecordCase("setter.sameActiveObject", () => Selection.activeObject = a);
            RecordCase("setter.destroyContext", () => {
                Selection.SetActiveObjectWithContext(a, c);
                Object.DestroyImmediate(c);
            });
            RecordCase("setter.nullArray", () => Selection.instanceIDs = null);
            RecordCase("setter.nullObjects", () => Selection.objects = null);
            RecordCase("setter.emptyArray", () => Selection.instanceIDs = Array.Empty<int>());

            const string assetPath = "Assets/Selection Asset.asset";
            var asset = ScriptableObject.CreateInstance<SelectionProbeAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            var assetId = asset.GetInstanceID();
            Selection.objects = new Object[] { asset, a };
            void AddRow(string k, string v) => rows.Add(JsonUtility.ToJson(new Row { k = k, v = v }));
            string RawAssetSelection() =>
                "ids=" + string.Join(",", Selection.instanceIDs.Select(id => id == assetId ? "asset" : id == aId ? "A" : "other"))
                + "|activeAssetId=" + (Selection.activeInstanceID == assetId) + "|containsAsset=" + Selection.Contains(assetId);
            AddRow("asset.selected", RawAssetSelection());
            Resources.UnloadAsset(asset);
            var loadedBefore = AssetDatabase.IsMainAssetAtPathLoaded(assetPath);
            var raw = RawAssetSelection();
            AddRow("asset.unloaded.raw", "wrapperDestroyed=" + (asset == null) + "|before=" + loadedBefore
                + "|after=" + AssetDatabase.IsMainAssetAtPathLoaded(assetPath) + "|" + raw);
            var activeAsset = Selection.activeObject;
            AddRow("asset.unloaded.activeObject", "sameId=" + (activeAsset != null && activeAsset.GetInstanceID() == assetId)
                + "|loaded=" + AssetDatabase.IsMainAssetAtPathLoaded(assetPath) + "|count=" + Selection.count);
            var reloaded = AssetDatabase.LoadMainAssetAtPath(assetPath);
            AddRow("asset.reloaded", "sameId=" + (reloaded.GetInstanceID() == assetId) + "|active=" + (Selection.activeObject == reloaded)
                + "|" + RawAssetSelection());
        }
        catch { Selection.selectionChanged -= Changed; throw; }

        int ticks = 0;
        void Finish()
        {
            if (++ticks < 3) return;
            EditorApplication.update -= Finish;
            rows.Add(JsonUtility.ToJson(new Row { k = "update.selectionChanged", v = notices.ToString() }));
            Selection.selectionChanged -= Changed;
            SelectionStateProbe.Record((key, value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = value })), Write);
        }
        void Write()
        {
            Directory.CreateDirectory(output);
            rows.Add("{\"k\":\"complete\",\"v\":true}");
            File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
            EditorApplication.Exit(0);
        }
        EditorApplication.update += Finish;
    }
}
