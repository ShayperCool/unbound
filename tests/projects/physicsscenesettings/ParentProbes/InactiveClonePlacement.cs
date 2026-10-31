using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class SceneSettingsProbe
{
    static readonly StringBuilder rows = new StringBuilder();
    static void State(string key, Rigidbody rb)
    {
        var p = rb.position; var q = rb.rotation;
        var t = rb.transform.position; var r = rb.transform.rotation;
        var v = rb.linearVelocity; var w = rb.angularVelocity;
        float[] values = { rb.IsSleeping() ? 1 : 0, p.x,p.y,p.z,q.x,q.y,q.z,q.w,
            t.x,t.y,t.z,r.x,r.y,r.z,r.w,v.x,v.y,v.z,w.x,w.y,w.z };
        rows.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i != 0) rows.Append(',');
            rows.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }
        rows.Append("]}\n");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            rb.gameObject.SetActive(false);
        Physics.simulationMode = SimulationMode.Script;
        Physics.gravity = Vector3.zero;
        foreach (bool active in new[] { false, true })
        foreach (bool parented in new[] { false, true })
        foreach (bool kinematic in new[] { false, true })
        foreach (int placement in new[] { 0, 1, 2, 3 })
        {
            var sourceParent = new GameObject("source-parent");
            sourceParent.transform.SetPositionAndRotation(new Vector3(10,20,30), new Quaternion(0,1,0,0));
            var targetParent = new GameObject("target-parent");
            targetParent.transform.SetPositionAndRotation(new Vector3(-7,8,9), new Quaternion(0,0,1,0));
            var source = new GameObject("source"); source.SetActive(false);
            if (parented) source.transform.SetParent(sourceParent.transform, false);
            source.transform.localPosition = new Vector3(3,4,5);
            source.transform.localRotation = new Quaternion(1,0,0,0);
            var body = source.AddComponent<Rigidbody>();
            body.useGravity = false; body.isKinematic = kinematic;
            body.linearDamping = body.angularDamping = 0;
            if (active) source.SetActive(true);
            if (!kinematic) { body.linearVelocity = Vector3.right; body.angularVelocity = Vector3.up; }
            string key = active + "/" + parented + "/" + kinematic + "/" + placement;
            State(key + "/source", body);
            GameObject clone;
            if (placement == 0) clone = UnityEngine.Object.Instantiate(source);
            else if (placement == 1) clone = UnityEngine.Object.Instantiate(source, new Vector3(21,22,23), new Quaternion(0,0,1,0));
            else clone = UnityEngine.Object.Instantiate(source, targetParent.transform, placement == 2);
            var clonedBody = clone.GetComponent<Rigidbody>();
            State(key + "/clone", clonedBody);
            Physics.SyncTransforms(); State(key + "/sync", clonedBody);
            if (!active) clone.SetActive(true);
            State(key + "/activated", clonedBody);
            Physics.Simulate(.02f); State(key + "/step", clonedBody);
            UnityEngine.Object.DestroyImmediate(clone);
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(sourceParent);
            UnityEngine.Object.DestroyImmediate(targetParent);
            Physics.SyncTransforms();
        }
        File.WriteAllText(output, rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
