using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Play mode after the Edit-mode script bake (<c>EditBake</c>, editor code): the records it
/// wrote, then how many of the scene's renderers that contribute to GI have a realtime lightmap
/// and which, and whether the scene names lighting data.
/// </summary>
public class EditBakeProbe : MonoBehaviour
{
    static string s_Out;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        s_Out = Environment.GetEnvironmentVariable("REALTIME_GI_EDIT_BAKE_OUT");
        if (!string.IsNullOrEmpty(s_Out)) new GameObject("EditBakeProbe").AddComponent<EditBakeProbe>();
    }

    IEnumerator Start()
    {
        var lines = new List<string>(File.ReadAllLines(s_Out + ".edit"));
        void Write(string key, object value) =>
            lines.Add("{\"key\":\"" + key + "\",\"value\":\"" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture).Replace("\"", "'") + "\"}");
        for (int i = 0; i < 3; i++) yield return null;
        int contributing = 0, lit = 0;
        var indices = new List<string>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
#if UNITY_EDITOR
            if ((UnityEditor.GameObjectUtility.GetStaticEditorFlags(r.gameObject) & UnityEditor.StaticEditorFlags.ContributeGI) == 0) continue;
#endif
            contributing++;
            indices.Add(r.name + ":" + r.realtimeLightmapIndex);
            if (r.realtimeLightmapIndex >= 0 && r.realtimeLightmapIndex < 65534) lit++;
        }
        indices.Sort(StringComparer.Ordinal);
        Write("play.contributing", contributing);
        Write("play.withRealtimeLightmap", lit);
        Write("play.indices", string.Join(" ", indices));
#if UNITY_EDITOR
        Write("play.lightingData", UnityEditor.Lightmapping.lightingDataAsset != null);
#endif
        Write("complete", 1);
        File.WriteAllLines(s_Out + ".tmp", lines);
        File.Move(s_Out + ".tmp", s_Out);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
