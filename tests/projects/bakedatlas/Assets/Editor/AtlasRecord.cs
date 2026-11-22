using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The capture (`bake_atlas.py native|engine`, <c>BAKED_ATLAS_OUT</c> set): opens each scene of
/// <c>Assets/Scenes</c> in Edit mode, bakes it with <see cref="Lightmapping.Bake"/> and records
/// each MeshRenderer's lightmap index and lightmap scale and offset (the floats' bits) and each
/// lightmap's size. The Editor runs it with <c>-executeMethod AtlasRecord.Record</c>; with
/// <c>BAKED_ATLAS_AUTO</c> set (Unbound's <c>unbound test</c>) it starts itself once Edit mode has
/// run a few updates.
/// </summary>
[InitializeOnLoad]
public static class AtlasRecord
{
    static readonly List<string> s_Lines = new List<string>();

    static AtlasRecord()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKED_ATLAS_OUT"))) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKED_ATLAS_AUTO"))) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int updates = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++updates < 30) return;
            EditorApplication.update -= wait;
            Record();
        };
        EditorApplication.update += wait;
    }

    static void Write(string key, object value) =>
        s_Lines.Add("{\"key\":\"" + key + "\",\"value\":\"" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture).Replace("\"", "'") + "\"}");

    static string Bits(float v) => BitConverter.SingleToInt32Bits(v).ToString("x8");

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static void Record()
    {
        try
        {
            EditorSettings.asyncShaderCompilation = false;
            var scenes = new List<string>(Directory.GetFiles("Assets/Scenes", "*.unity"));
            scenes.Sort(StringComparer.Ordinal);
            foreach (var path in scenes)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                EditorSceneManager.OpenScene(path.Replace('\\', '/'));
                // The Editor's LightBaker process sometimes ends with a crash (exit code 11) on
                // this machine, whatever the scene, and Bake still answers true: the layout is the
                // Editor's own work before it, so a bake that made no lightmaps (every scene here
                // has lightmapped renderers) is tried again.
                bool baked = false;
                for (int attempt = 0; attempt < 5 && !(baked && LightmapSettings.lightmaps.Length > 0); attempt++)
                    baked = Lightmapping.Bake();
                Write(name + ".bake", baked);
                var sizes = new List<string>();
                foreach (var data in LightmapSettings.lightmaps)
                    sizes.Add(data.lightmapColor == null ? "none" : data.lightmapColor.width + "x" + data.lightmapColor.height);
                Write(name + ".lightmaps", string.Join(" ", sizes));
                var renderers = new List<string>();
                foreach (var r in Resources.FindObjectsOfTypeAll<MeshRenderer>())
                {
                    if (EditorUtility.IsPersistent(r) || r.gameObject.scene.path != path.Replace('\\', '/')) continue;
                    var st = r.lightmapScaleOffset;
                    renderers.Add(PathOf(r.transform) + "\t" + r.lightmapIndex + " " + Bits(st.x) + " " + Bits(st.y) + " " + Bits(st.z) + " " + Bits(st.w));
                }
                renderers.Sort(StringComparer.Ordinal);
                foreach (var line in renderers)
                {
                    var tab = line.IndexOf('\t');
                    Write(name + "." + line.Substring(0, tab), line.Substring(tab + 1));
                }
            }
            Write("complete", 1);
            var output = Environment.GetEnvironmentVariable("BAKED_ATLAS_OUT");
            File.WriteAllLines(output + ".tmp", s_Lines);
            File.Move(output + ".tmp", output);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
