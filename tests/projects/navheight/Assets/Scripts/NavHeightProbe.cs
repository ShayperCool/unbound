using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// NavMeshes built at run time with NavMeshBuildSettings.buildHeightMesh: a ramp, five 0.2 m stairs
// up to a landing, a 0.1 m bump, a 0.15 m step, and a table 2.5 m up over the ground, given as
// mesh sources and again as box sources. For each build: NavMesh.SamplePosition heights along lines
// across them, a raycast up the ramp, and agents walking up the ramp and over the bump. Without a height mesh, the main line again as the contrast.
public class NavHeightProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("NavHeightProbe").AddComponent<NavHeightProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    Mesh cube;

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    static string R(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string P(Vector3 v) => R(v.x) + "," + R(v.y) + "," + R(v.z);

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("NAV_HEIGHT_OUT"); if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        Time.captureDeltaTime = 0.02f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = go.GetComponent<MeshFilter>().sharedMesh;
        go.SetActive(false);
        StartCoroutine(Record());
    }

    NavMeshBuildSource Source(bool mesh, Vector3 center, Vector3 size, Quaternion rotation) => mesh
        ? new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = cube, transform = Matrix4x4.TRS(center, rotation, size), area = 0 }
        : new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = Matrix4x4.TRS(center, rotation, Vector3.one), area = 0 };

    List<NavMeshBuildSource> Sources(bool mesh)
    {
        var s = new List<NavMeshBuildSource>
        {
            Source(mesh, new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 14f), Quaternion.identity),
            Source(mesh, new Vector3(-6f, 0.7f, 0f), new Vector3(6f, 0.4f, 3f), Quaternion.Euler(0f, 0f, 15f)),
        };
        for (int k = 1; k <= 5; k++) s.Add(Source(mesh, new Vector3(1.6f + 0.8f * k - 0.4f, 0.1f * k, 0f), new Vector3(0.8f, 0.2f * k, 3f), Quaternion.identity));
        s.Add(Source(mesh, new Vector3(7.1f, 0.5f, 0f), new Vector3(3f, 1f, 3f), Quaternion.identity));
        s.Add(Source(mesh, new Vector3(-1f, 0.05f, 3f), new Vector3(1f, 0.1f, 1f), Quaternion.identity));
        s.Add(Source(mesh, new Vector3(-6f, 2.5f, -4.5f), new Vector3(3f, 0.1f, 2f), Quaternion.identity));
        s.Add(Source(mesh, new Vector3(-1f, 1.2f, -4.5f), new Vector3(2f, 0.1f, 2f), Quaternion.identity));
        s.Add(Source(mesh, new Vector3(5f, 0.5f, -4.5f), new Vector3(3f, 0.2f, 2f), Quaternion.Euler(0f, 0f, 40f)));
        s.Add(Source(mesh, new Vector3(-9f, 0.075f, 4.5f), new Vector3(2f, 0.15f, 2f), Quaternion.identity));
        return s;
    }

    // Heights along a line at z, sampled from yGuess + 0.3 within 0.6 m every 5 cm ("-": none).
    string Line(float z, Func<float, float> guess)
    {
        var values = new List<string>();
        for (int i = 0; i <= 440; i++)
        {
            float x = -11f + i * 0.05f;
            values.Add(NavMesh.SamplePosition(new Vector3(x, guess(x) + 0.3f, z), out var hit, 0.6f, NavMesh.AllAreas) ? R(hit.position.y) : "-");
        }
        return string.Join(" ", values);
    }

    static float Profile(float x) => x < -2.5f && x > -9f ? Mathf.Max(0f, Mathf.Tan(15f * Mathf.Deg2Rad) * (x + 9f) * 0.6f) : x > 2f ? Mathf.Min(1f, (x - 1.6f) * 0.25f) : 0f;

    IEnumerator Record()
    {
        foreach (var (tag, mesh, height) in new[] { ("mesh", true, true), ("box", false, true), ("mesh.none", true, false) })
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.buildHeightMesh = height;
            var data = NavMeshBuilder.BuildNavMeshData(settings, Sources(mesh), new Bounds(Vector3.zero, new Vector3(26f, 8f, 16f)), Vector3.zero, Quaternion.identity);
            var instance = NavMesh.AddNavMeshData(data);
            Put(tag + ".main", Line(0f, Profile));
            if (height)
            {
                Put(tag + ".bump", Line(3f, x => 0f));
                Put(tag + ".step", Line(4.5f, x => 0f));
                Put(tag + ".table", Line(-4.5f, x => 2.5f));
                Put(tag + ".under", Line(-4.5f, x => 0f));
                bool blocked = NavMesh.Raycast(new Vector3(-10f, 0f, -0.5f), new Vector3(7f, 1f, -0.5f), out var hit, NavMesh.AllAreas);
                Put(tag + ".ray", blocked + " " + P(hit.position));
                var agents = new List<(string, NavMeshAgent)>();
                foreach (var (name, from, to) in new[] { ("ramp", new Vector3(-10.5f, 0f, 0.2f), new Vector3(-3f, 1.4f, 0.2f)), ("bump", new Vector3(-3f, 0f, 3f), new Vector3(1f, 0f, 3f)) })
                {
                    var a = new GameObject(name);
                    a.transform.position = from;
                    var agent = a.AddComponent<NavMeshAgent>();
                    agent.speed = 2f;
                    agent.SetDestination(to);
                    agents.Add((name, agent));
                }
                for (int f = 1; f <= 260; f++)
                {
                    yield return null;
                    if (f % 5 != 0) continue;
                    foreach (var (name, agent) in agents)
                        Put($"{tag}.agent.{name}.f{f}", P(agent.nextPosition) + " vy" + R(agent.velocity.y));
                }
                foreach (var (_, agent) in agents) Destroy(agent.gameObject);
                yield return null;
            }
            NavMesh.RemoveNavMeshData(instance);
            yield return null;
        }
        // The documented limit: an update with preserveTilesOutsideBounds creates no height mesh
        // and removes an existing one. A first build with it, then an update of the left half.
        string At(float x) => NavMesh.SamplePosition(new Vector3(x, Profile(x) + 0.3f, 0f), out var h, 0.6f, NavMesh.AllAreas) ? R(h.position.y) : "-";
        // Inside the left half, by the 0.15 m step's edge: the height mesh's height differs from
        // the polygon's there; then the triangulation's size.
        string Triangles() => (NavMesh.CalculateTriangulation().indices.Length / 3).ToString(CultureInfo.InvariantCulture);
        string State(NavMeshData data)
        {
            var t = NavMesh.CalculateTriangulation();
            var bump = new List<string>();
            foreach (float x in new[] { -1.7f, -1.6f, -1.45f, -1.3f })
                bump.Add(NavMesh.SamplePosition(new Vector3(x, 0.3f, 3f), out var a, 0.6f, NavMesh.AllAreas) ? R(a.position.y) : "-");
            // The ramp's side edge (z = 1.5 less the agent radius, as built), from inside it.
            string edge = NavMesh.SamplePosition(new Vector3(-6f, 1.1f, 1.45f), out var e, 0.8f, NavMesh.AllAreas) ? R(e.position.z) : "-";
            var source = data != null ? " src " + P(data.sourceBounds.center) + " " + P(data.sourceBounds.size) : "";
            return At(-6f) + " " + At(3.1f) + " " + At(6.5f) + " bump " + string.Join(" ", bump) + " edge " + edge + source;
        }
        var bounds = new Bounds(Vector3.zero, new Vector3(26f, 8f, 16f));
        var left = new Bounds(new Vector3(-6.5f, 0f, 0f), new Vector3(13f, 8f, 16f));
        {
            var plain = NavMesh.GetSettingsByID(0);
            plain.buildHeightMesh = false;
            var instance = NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(plain, Sources(true), bounds, Vector3.zero, Quaternion.identity));
            Put("plain.build", State(null));
            Put("plain.build.tris", Triangles());
            NavMesh.RemoveNavMeshData(instance);
        }
        // Default tiles, then 32-voxel tiles (about 5.3 m); `changed` updates with another tile size
        // and agent radius, which a preserving update ignores (documented).
        foreach (var (tag, preserve, tiles, changed) in new[] { ("preserve", true, 0, false), ("drop", false, 0, false), ("preserve.small", true, 32, false), ("drop.small", false, 32, false), ("preserve.changed", true, 32, true) })
        {
            var settings = NavMesh.GetSettingsByID(0);
            if (tiles > 0) { settings.overrideTileSize = true; settings.tileSize = tiles; }
            settings.buildHeightMesh = true;
            settings.preserveTilesOutsideBounds = preserve;
            var built = NavMeshBuilder.BuildNavMeshData(settings, Sources(true), bounds, Vector3.zero, Quaternion.identity);
            var instance = NavMesh.AddNavMeshData(built);
            Put(tag + ".build", State(built));
            Put(tag + ".build.tris", Triangles());
            NavMesh.RemoveNavMeshData(instance);
            settings.preserveTilesOutsideBounds = false;
            var data = NavMeshBuilder.BuildNavMeshData(settings, Sources(true), bounds, Vector3.zero, Quaternion.identity);
            instance = NavMesh.AddNavMeshData(data);
            Put(tag + ".before", State(data));
            Put(tag + ".before.tris", Triangles());
            settings.preserveTilesOutsideBounds = preserve;
            if (changed)
            {
                settings.tileSize = 64;
                settings.agentRadius = 0.9f;
            }
            bool updated = NavMeshBuilder.UpdateNavMeshData(data, settings, Sources(true), left);
            yield return null;
            Put(tag + ".update", updated + " " + State(data));
            Put(tag + ".update.tris", Triangles());
            NavMesh.RemoveNavMeshData(instance);
            yield return null;
        }
        Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(0);
    }
}
