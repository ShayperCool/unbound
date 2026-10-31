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
        var v = rb.linearVelocity; var w = rb.angularVelocity;
        float[] values = { v.x,v.y,v.z,w.x,w.y,w.z,rb.IsSleeping() ? 1 : 0,rb.isKinematic ? 1 : 0 };
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
        string output = Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics.simulationMode = SimulationMode.Script;
        foreach (var source in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            source.gameObject.SetActive(false);
        foreach (string life in new[] { "fresh", "activate", "clone" })
        {
            var go = new GameObject("body"); go.SetActive(life != "activate");
            go.AddComponent<BoxCollider>(); var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            if (life == "activate") go.SetActive(true);
            GameObject original = null;
            if (life == "clone")
            {
                original = go; go = UnityEngine.Object.Instantiate(go);
                original.SetActive(false); rb = go.GetComponent<Rigidbody>();
            }
            Physics.SyncTransforms();
            rb.linearVelocity = new Vector3(1,2,3); rb.angularVelocity = new Vector3(.4f,.5f,.6f);
            State(life + "/initial",rb);
            rb.isKinematic = true; State(life + "/kinematic",rb);
            rb.isKinematic = false; State(life + "/dynamic",rb);
            rb.linearVelocity = new Vector3(3,2,1); rb.angularVelocity = new Vector3(.6f,.5f,.4f);
            rb.isKinematic = true; rb.isKinematic = false;
            State(life + "/consecutive",rb);
            Physics.SyncTransforms(); State(life + "/flushed",rb);
            UnityEngine.Object.DestroyImmediate(go);
            if (original != null) UnityEngine.Object.DestroyImmediate(original);
        }
        File.WriteAllText(output,rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
