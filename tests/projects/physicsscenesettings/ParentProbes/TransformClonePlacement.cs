using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class SceneSettingsProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var rows = new StringBuilder();
        foreach (bool active in new[] { false, true })
        foreach (int placement in new[] { 0,1,2,3,4,5 })
        {
            var parent = new GameObject("source-parent");
            parent.transform.SetPositionAndRotation(new Vector3(10,20,30), new Quaternion(0,1,0,0));
            var target = new GameObject("target-parent");
            target.transform.SetPositionAndRotation(new Vector3(-7,8,9), new Quaternion(0,0,1,0));
            var source = new GameObject("source"); source.SetActive(active);
            source.transform.SetParent(parent.transform, false);
            source.transform.localPosition = new Vector3(3,4,5);
            source.transform.localRotation = new Quaternion(1,0,0,0);
            GameObject clone;
            if (placement == 0) clone = UnityEngine.Object.Instantiate(source);
            else if (placement == 1) clone = UnityEngine.Object.Instantiate(source, (Transform)null, false);
            else if (placement == 2) clone = UnityEngine.Object.Instantiate(source, (Transform)null, true);
            else if (placement == 3) clone = UnityEngine.Object.Instantiate(source, new Vector3(21,22,23), new Quaternion(0,0,1,0));
            else clone = UnityEngine.Object.Instantiate(source, target.transform, placement == 4);
            var p = clone.transform.position; var q = clone.transform.rotation;
            float[] values = {p.x,p.y,p.z,q.x,q.y,q.z,q.w};
            rows.Append("{\"key\":\"").Append(active).Append('/').Append(placement).Append("\",\"value\":[");
            for (int i=0; i<values.Length; i++)
            {
                if (i!=0) rows.Append(',');
                rows.Append(values[i].ToString("R",CultureInfo.InvariantCulture));
            }
            rows.Append("]}\n");
            UnityEngine.Object.DestroyImmediate(clone);
            UnityEngine.Object.DestroyImmediate(parent);
            UnityEngine.Object.DestroyImmediate(target);
        }
        File.WriteAllText(output, rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
