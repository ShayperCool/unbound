using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes what AssetDatabase.IsMainAssetAtPathLoaded answers for an asset the scene references and
/// one nothing references, before and after loading and unloading them, and what the scene's
/// reference reads after its asset is unloaded, into values.jsonl in $ASSET_LOADED_OUT
/// (tests/value_probe.py).
/// </summary>
public class AssetLoadedProbe : MonoBehaviour
{
    const string Referenced = "Assets/Data/Referenced.asset";
    const string Unreferenced = "Assets/Data/Unreferenced.asset";
    static string s_Directory;
    static StreamWriter output;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        s_Directory = Environment.GetEnvironmentVariable("ASSET_LOADED_OUT");
        if (!string.IsNullOrEmpty(s_Directory)) new GameObject("probe").AddComponent<AssetLoadedProbe>();
    }

    static string Loaded(string path) => AssetDatabase.IsMainAssetAtPathLoaded(path).ToString();

    void Update()
    {
        if (Time.frameCount < 3) return;
        enabled = false;
        try
        {
            Directory.CreateDirectory(s_Directory);
            using (output = new StreamWriter(Path.Combine(s_Directory, "values.jsonl")))
            {
                var holder = FindAnyObjectByType<LoadedHolder>();
                Row("referenced.loaded", Loaded(Referenced));
                Row("unreferenced.loaded", Loaded(Unreferenced));
                Row("missing.loaded", Loaded("Assets/Data/Missing.asset"));
                var unreferenced = AssetDatabase.LoadAssetAtPath<LoadedData>(Unreferenced);
                int unreferencedId = unreferenced.GetInstanceID();
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(unreferenced, out string expectedGuid, out long expectedFileId);
                string expectedGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(unreferenced).ToString();
                bool expectedHasOid = UnityEditor.Build.Content.ObjectIdentifier.TryGetObjectIdentifier(unreferenced, out var expectedOid);
                Row("unreferenced.afterLoad", Loaded(Unreferenced));
                Resources.UnloadAsset(unreferenced);
                Row("unreferenced.afterUnload", Loaded(Unreferenced));
                Row("unreferenced.reference", unreferenced == null ? "null" : unreferenced.value.ToString());
                Resources.UnloadAsset(holder.data);
                Row("referenced.afterUnload", Loaded(Referenced));
                Row("referenced.sceneReference", holder.data == null ? "null" : holder.data.value.ToString());
                Row("referenced.afterAccess", Loaded(Referenced));

                // Weak-reference reload by instance ID (as ProbeVolumeBakingSetWeakReference uses).
                Row("unreferenced.idValidAfterUnload", Resources.InstanceIDIsValid(unreferencedId).ToString());
                var byResources = Resources.InstanceIDToObject(unreferencedId) as LoadedData;
                Row("unreferenced.byResources", byResources == null ? "null" : $"{byResources.value}|sameId={byResources.GetInstanceID() == unreferencedId}");
                Row("unreferenced.idValidAfterResources", Resources.InstanceIDIsValid(unreferencedId).ToString());
                Row("unreferenced.loadedAfterResources", Loaded(Unreferenced));

                Resources.UnloadAsset(byResources);
                Row("unreferenced.idValidAfterUnload2", Resources.InstanceIDIsValid(unreferencedId).ToString());
                var byEditor = EditorUtility.InstanceIDToObject(unreferencedId) as LoadedData;
                Row("unreferenced.byEditor", byEditor == null ? "null" : $"{byEditor.value}|sameId={byEditor.GetInstanceID() == unreferencedId}");
                Row("unreferenced.loadedAfterEditor", Loaded(Unreferenced));

                Resources.UnloadAsset(byEditor);
                // Sibling instance-ID metadata APIs while unloaded.
                Row("unreferenced.getAssetPathAfterUnload", AssetDatabase.GetAssetPath(unreferencedId));
                Row("unreferenced.containsAfterUnload", AssetDatabase.Contains(unreferencedId).ToString());
                bool gotGuid = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(unreferencedId, out string gotGuidVal, out long gotFileId);
                Row("unreferenced.tryGetGuidAfterUnload", $"{gotGuid}|{gotGuidVal == expectedGuid}|{gotFileId == expectedFileId}");
                var gotGlobal = GlobalObjectId.GetGlobalObjectIdSlow(unreferencedId);
                Row("unreferenced.globalIdAfterUnload", $"type={gotGlobal.identifierType}|match={gotGlobal.ToString() == expectedGlobalId}");
                bool gotOidOk = UnityEditor.Build.Content.ObjectIdentifier.TryGetObjectIdentifier(unreferencedId, out var gotOid);
                Row("unreferenced.objectIdAfterUnload", $"{gotOidOk}|{expectedHasOid && gotOid == expectedOid}");
                Row("unreferenced.loadedAfterMetadataQueries", Loaded(Unreferenced));

                var spGo = new GameObject("sp-holder");
                var spHolder = spGo.AddComponent<LoadedHolder>();
                var spSo = new SerializedObject(spHolder);
                var spProp = spSo.FindProperty("data");
                spProp.objectReferenceInstanceIDValue = unreferencedId;
                spSo.ApplyModifiedPropertiesWithoutUndo();
                var assignedData = spHolder.data;
                Row("unreferenced.serializedPropSetId", $"{assignedData != null}|sameId={(assignedData != null && assignedData.GetInstanceID() == unreferencedId)}|loaded={Loaded(Unreferenced)}");
                DestroyImmediate(spGo);
                if (assignedData != null) Resources.UnloadAsset(assignedData);

                var byAssetDb = AssetDatabase.LoadAssetAtPath<LoadedData>(Unreferenced);
                Row("unreferenced.byAssetDbSameId", byAssetDb == null ? "null" : $"{byAssetDb.value}|sameId={byAssetDb.GetInstanceID() == unreferencedId}");

                var runtime = ScriptableObject.CreateInstance<LoadedData>();
                int runtimeId = runtime.GetInstanceID();
                DestroyImmediate(runtime);
                Row("runtime.idValidAfterDestroy", Resources.InstanceIDIsValid(runtimeId).ToString());
                Row("runtime.byResourcesAfterDestroy", (Resources.InstanceIDToObject(runtimeId) != null).ToString());
                Row("runtime.byEditorAfterDestroy", (EditorUtility.InstanceIDToObject(runtimeId) != null).ToString());

                // Direct DestroyImmediate(asset, true) on persistent ScriptableObject and Material.
                int destroyedSoId = byAssetDb.GetInstanceID();
                DestroyImmediate(byAssetDb, true);
                Row("destroyAsset.so.idValid", Resources.InstanceIDIsValid(destroyedSoId).ToString());
                Row("destroyAsset.so.byResources", (Resources.InstanceIDToObject(destroyedSoId) != null).ToString());
                Row("destroyAsset.so.byEditor", (EditorUtility.InstanceIDToObject(destroyedSoId) != null).ToString());
                Row("destroyAsset.so.getAssetPath", AssetDatabase.GetAssetPath(destroyedSoId));
                Row("destroyAsset.so.contains", AssetDatabase.Contains(destroyedSoId).ToString());
                Row("destroyAsset.so.loaded", Loaded(Unreferenced));

                const string TestMatPath = "Assets/Data/TestMat.mat";
                var testMat = AssetDatabase.LoadAssetAtPath<Material>(TestMatPath);
                int destroyedMatId = testMat.GetInstanceID();
                DestroyImmediate(testMat, true);
                Row("destroyAsset.mat.idValid", Resources.InstanceIDIsValid(destroyedMatId).ToString());
                Row("destroyAsset.mat.byResources", (Resources.InstanceIDToObject(destroyedMatId) != null).ToString());
                Row("destroyAsset.mat.byEditor", (EditorUtility.InstanceIDToObject(destroyedMatId) != null).ToString());
                Row("destroyAsset.mat.getAssetPath", AssetDatabase.GetAssetPath(destroyedMatId));
                Row("destroyAsset.mat.contains", AssetDatabase.Contains(destroyedMatId).ToString());
                Row("destroyAsset.mat.loaded", Loaded(TestMatPath));
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
