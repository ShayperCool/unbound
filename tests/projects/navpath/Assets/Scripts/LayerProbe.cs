using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Stacked runtime data with point and wide links, including connection lifetime controls.
/// Run separately with run.py --profile layers; every discrepancy is reported strictly.
/// </summary>
public class LayerProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Environment.GetEnvironmentVariable("NAVPATH_PROBE_PROFILE") != "layers" ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVPATH_PROBE_OUT"))) return;
        new GameObject("LayerProbe").AddComponent<LayerProbe>();
    }
    IEnumerator Start()
    {
        NavMesh.RemoveAllNavMeshData();
        // Vary layer count and duplicate connections independently.
        foreach (int layers in new[] { 1, 2, 4, 8 })
        foreach (float width in new[] { 0f, 4f })
        foreach (int copies in new[] { 1, 3 })
        {
            ProbeLog.Begin("link.layers." + layers + ".w" + width + ".copies" + copies);
            var left = Floor(-4f);
            var right = Floor(4f);
            var instances = new List<NavMeshDataInstance>();
            var connections = new List<NavMeshLinkInstance>();
            var starts = new Vector3[layers];
            var ends = new Vector3[layers];
            try
            {
                for (int i = 0; i < layers; i++)
                {
                    var offset = new Vector3(0f, i * 0.125f, 0f);
                    instances.Add(NavMesh.AddNavMeshData(left, offset, Quaternion.identity));
                    instances.Add(NavMesh.AddNavMeshData(right, offset, Quaternion.identity));
                }
                for (int i = 0; i < layers; i++)
                {
                    float y = i * 0.125f + 0.09f;
                    bool a = NavMesh.SamplePosition(new Vector3(-2.7f, y, 0f), out var first, 0.05f, NavMesh.AllAreas);
                    bool b = NavMesh.SamplePosition(new Vector3(2.7f, y, 0f), out var last, 0.05f, NavMesh.AllAreas);
                    ProbeLog.Value("layer" + i + ".start.found", a);
                    ProbeLog.Value("layer" + i + ".end.found", b);
                    ProbeLog.Value("layer" + i + ".start", first.position);
                    ProbeLog.Value("layer" + i + ".end", last.position);
                    starts[i] = first.position + Vector3.left;
                    ends[i] = last.position + Vector3.right;
                    for (int j = 0; j < copies; j++)
                        connections.Add(NavMesh.AddLink(new NavMeshLinkData { startPosition = first.position,
                            endPosition = last.position, width = width, bidirectional = true, costModifier = -1f }));
                }
                yield return null;
                var path = new NavMeshPath();
                for (int i = 0; i < layers; i++)
                for (int j = 0; j < layers; j++)
                {
                    string key = "p" + i + "." + j;
                    ProbeLog.Value(key + ".ok", NavMesh.CalculatePath(starts[i], ends[j], NavMesh.AllAreas, path));
                    ProbeLog.Value(key + ".status", path.status.ToString());
                    var corners = path.corners;
                    ProbeLog.Value(key + ".count", corners.Length);
                    for (int k = 0; k < corners.Length; k++) ProbeLog.Value(key + ".c" + k, corners[k]);
                }
            }
            finally
            {
                foreach (var connection in connections) NavMesh.RemoveLink(connection);
                foreach (var instance in instances) NavMesh.RemoveNavMeshData(instance);
                DestroyImmediate(left);
                DestroyImmediate(right);
            }
        }
        yield return Rebinding();
        yield return AddedCloser();
        var output = Environment.GetEnvironmentVariable("NAVPATH_PROBE_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
    IEnumerator Rebinding()
    {
        // The selected lower layer is removed, restored, then the link is recreated.
        var left = Floor(-4f);
        var right = Floor(4f);
        var lowLeft = NavMesh.AddNavMeshData(left);
        var lowRight = NavMesh.AddNavMeshData(right);
        var highLeft = NavMesh.AddNavMeshData(left, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
        var highRight = NavMesh.AddNavMeshData(right, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
        NavMesh.SamplePosition(new Vector3(-2.7f, 0.09f, 0f), out var start, 0.05f, NavMesh.AllAreas);
        NavMesh.SamplePosition(new Vector3(2.7f, 0.09f, 0f), out var end, 0.05f, NavMesh.AllAreas);
        var connection = NavMesh.AddLink(new NavMeshLinkData { startPosition = start.position,
            endPosition = end.position, bidirectional = true, costModifier = -1f });
        try
        {
            foreach (string phase in new[] { "both", "removed", "restored", "relinked" })
            {
                if (phase == "removed") { NavMesh.RemoveNavMeshData(lowLeft); NavMesh.RemoveNavMeshData(lowRight); }
                if (phase == "restored") { lowLeft = NavMesh.AddNavMeshData(left); lowRight = NavMesh.AddNavMeshData(right); }
                if (phase == "relinked")
                {
                    NavMesh.RemoveLink(connection);
                    connection = NavMesh.AddLink(new NavMeshLinkData { startPosition = start.position,
                        endPosition = end.position, bidirectional = true, costModifier = -1f });
                }
                yield return null;
                ProbeLog.Begin("link.layers.rebind." + phase);
                var path = new NavMeshPath();
                ProbeLog.Value("ok", NavMesh.CalculatePath(start.position + new Vector3(-1f, 0.3f, 0f),
                    end.position + new Vector3(1f, 0.3f, 0f), NavMesh.AllAreas, path));
                ProbeLog.Value("status", path.status.ToString());
                var corners = path.corners;
                ProbeLog.Value("count", corners.Length);
                for (int i = 0; i < corners.Length; i++) ProbeLog.Value("c" + i, corners[i]);
                ProbeLog.Begin("link.layers.rebind.low." + phase);
                ProbeLog.Value("ok", NavMesh.CalculatePath(start.position + Vector3.left, end.position + Vector3.right, NavMesh.AllAreas, path));
                ProbeLog.Value("status", path.status.ToString());
                corners = path.corners;
                ProbeLog.Value("count", corners.Length);
                for (int i = 0; i < corners.Length; i++) ProbeLog.Value("c" + i, corners[i]);
            }
        }
        finally
        {
            NavMesh.RemoveLink(connection);
            NavMesh.RemoveNavMeshData(lowLeft); NavMesh.RemoveNavMeshData(lowRight);
            NavMesh.RemoveNavMeshData(highLeft); NavMesh.RemoveNavMeshData(highRight);
            DestroyImmediate(left); DestroyImmediate(right);
        }
        foreach (float climb in new[] { 0.1f, 1.2f })
        foreach (float above in new[] { 0.25f, 1f })
        {
            ProbeLog.Begin("link.layers.climb" + climb + ".above" + above);
            left = Floor(-4f, climb);
            right = Floor(4f, climb);
            lowLeft = NavMesh.AddNavMeshData(left);
            lowRight = NavMesh.AddNavMeshData(right);
            NavMesh.SamplePosition(new Vector3(-2.7f, 0.09f, 0f), out start, 0.05f, NavMesh.AllAreas);
            NavMesh.SamplePosition(new Vector3(2.7f, 0.09f, 0f), out end, 0.05f, NavMesh.AllAreas);
            connection = NavMesh.AddLink(new NavMeshLinkData { startPosition = start.position + Vector3.up * above,
                endPosition = end.position + Vector3.up * above, bidirectional = true, costModifier = -1f });
            try
            {
                yield return null;
                var path = new NavMeshPath();
                ProbeLog.Value("ok", NavMesh.CalculatePath(start.position + Vector3.left, end.position + Vector3.right, NavMesh.AllAreas, path));
                ProbeLog.Value("status", path.status.ToString());
                var corners = path.corners;
                ProbeLog.Value("count", corners.Length);
                for (int i = 0; i < corners.Length; i++) ProbeLog.Value("c" + i, corners[i]);
            }
            finally
            {
                NavMesh.RemoveLink(connection);
                NavMesh.RemoveNavMeshData(lowLeft); NavMesh.RemoveNavMeshData(lowRight);
                DestroyImmediate(left); DestroyImmediate(right);
            }
        }
    }
    IEnumerator AddedCloser()
    {
        // A valid upper binding exists before a closer layer is added.
        var left = Floor(-4f);
        var right = Floor(4f);
        var highLeft = NavMesh.AddNavMeshData(left, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
        var highRight = NavMesh.AddNavMeshData(right, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
        NavMesh.SamplePosition(new Vector3(-2.7f, 0.39f, 0f), out var start, 0.05f, NavMesh.AllAreas);
        NavMesh.SamplePosition(new Vector3(2.7f, 0.39f, 0f), out var end, 0.05f, NavMesh.AllAreas);
        var first = start.position - Vector3.up * 0.3f;
        var last = end.position - Vector3.up * 0.3f;
        var connection = NavMesh.AddLink(new NavMeshLinkData { startPosition = first, endPosition = last,
            bidirectional = true, costModifier = -1f });
        var lowLeft = default(NavMeshDataInstance);
        var lowRight = default(NavMeshDataInstance);
        try
        {
            foreach (string phase in new[] { "before", "after" })
            {
                if (phase == "after") { lowLeft = NavMesh.AddNavMeshData(left); lowRight = NavMesh.AddNavMeshData(right); }
                yield return null;
                ProbeLog.Begin("link.layers.addCloser." + phase);
                var path = new NavMeshPath();
                for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    var a = i == 0 ? first : start.position;
                    var b = j == 0 ? last : end.position;
                    string key = "p" + i + "." + j;
                    ProbeLog.Value(key + ".ok", NavMesh.CalculatePath(a + Vector3.left, b + Vector3.right, NavMesh.AllAreas, path));
                    ProbeLog.Value(key + ".status", path.status.ToString());
                    var corners = path.corners;
                    ProbeLog.Value(key + ".count", corners.Length);
                    for (int k = 0; k < corners.Length; k++) ProbeLog.Value(key + ".c" + k, corners[k]);
                }
            }
        }
        finally
        {
            NavMesh.RemoveLink(connection);
            NavMesh.RemoveNavMeshData(lowLeft); NavMesh.RemoveNavMeshData(lowRight);
            NavMesh.RemoveNavMeshData(highLeft); NavMesh.RemoveNavMeshData(highRight);
            DestroyImmediate(left); DestroyImmediate(right);
        }
    }

    static NavMeshData Floor(float x, float climb = -1f)
    {
        var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(4f, 1f, 8f), transform = Matrix4x4.Translate(new Vector3(x, -0.5f, 0f)) };
        var settings = NavMesh.GetSettingsByID(0);
        if (climb >= 0f) settings.agentClimb = climb;
        return NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { source },
            new Bounds(Vector3.zero, new Vector3(16f, 4f, 12f)), Vector3.zero, Quaternion.identity);
    }
}
