using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Reads of a mesh scripts cannot read in Play mode, made outside Play mode as the editor
/// domain loads (Mesh.isReadable, 6000.0: "Access is always allowed in the Unity Editor outside of
/// the game and rendering loop"); <see cref="MeshReadableProbe"/> writes them out with its own.</summary>
[InitializeOnLoad]
static class MeshReadableEditMode
{
    static MeshReadableEditMode()
    {
        var mesh = Resources.Load<Mesh>("LayersLocked");
        var readable = Resources.Load<Mesh>("Layers");
        var output = new StringBuilder();
        var logs = new List<string>();
        Application.LogCallback log = (condition, _, type) => logs.Add(type + ": " + condition);
        void Value(string key, string json) => output.Append("{\"k\":\"editMode.").Append(key).Append("\",\"v\":").Append(json).Append("}\n");
        void Read(string key, Func<string> read)
        {
            logs.Clear();
            string result;
            try { result = read(); }
            catch (Exception e) { result = "\"throws " + e.GetType().Name + "\""; }
            Value(key, result);
            if (logs.Count > 0) Value(key + ".log", "[" + string.Join(",", logs.Select(l => "\"" + l.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]");
        }
        Application.logMessageReceived += log;
        try
        {
            Value("isPlaying", Application.isPlaying ? "true" : "false");
            Value("found", mesh != null && readable != null ? "true" : "false");
            if (mesh == null || readable == null) return;
            Value("readable", mesh.isReadable ? "true" : "false");
            Read("vertices", () => mesh.vertices.Length.ToString(CultureInfo.InvariantCulture));
            Read("triangles", () => mesh.triangles.Length.ToString(CultureInfo.InvariantCulture));
            Read("GetUVs4", () => { var l = new List<Vector2>(); mesh.GetUVs(4, l); return l.Count.ToString(CultureInfo.InvariantCulture); });
            Read("colors32", () => mesh.colors32.Length.ToString(CultureInfo.InvariantCulture));
            Read("combine", () =>
            {
                var into = new Mesh { name = "EditCombined" };
                into.CombineMeshes(new[] { new CombineInstance { mesh = mesh, transform = Matrix4x4.identity }, new CombineInstance { mesh = readable, transform = Matrix4x4.identity } });
                string r = "[" + into.vertexCount + "," + into.GetIndexCount(0) + "]";
                UnityEngine.Object.DestroyImmediate(into);
                return r;
            });
        }
        finally
        {
            Application.logMessageReceived -= log;
            MeshReadableProbe.EditModeValues = output.ToString();
        }
    }
}
