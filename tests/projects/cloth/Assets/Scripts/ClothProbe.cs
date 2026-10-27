using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// How Cloth moves: its defaults, then scenarios of a 1 m cloth of 9 x 9 vertices (hanging from
/// its top row, or lying flat and free) under each setting, colliders, transform motion and API
/// call, at a frame per fixed step; the cloth's vertices are recorded at chosen frames, its
/// normals at the last one. Writes <c>cloth.jsonl</c> into <c>CLOTH_PROBE_OUT</c>, then quits.
/// </summary>
public class ClothProbe : MonoBehaviour
{
    // Steps of 1/8: exact in floats however the runtime rounds intermediate results.
    const int N = 8;
    static readonly int[] s_Frames = { 1, 2, 3, 5, 10, 20, 21, 22, 25, 31, 32, 35, 40, 51, 52, 55, 60, 61, 62, 65, 90, 120 };
    static readonly StringBuilder s_Out = new StringBuilder();
    static Camera s_Camera;
    static Material s_Material;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ClothProbe").AddComponent<ClothProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string Vs(IEnumerable<Vector3> vs) => "[" + string.Join(",", vs.Select(V)) + "]";
    static void Record(string kind, string body) => s_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    /// <summary>A square grid: vertical (x right, y down from 0) or flat (x and z).</summary>
    static Mesh Grid(bool flat, bool seam = false)
    {
        var vertices = new List<Vector3>();
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
                vertices.Add(flat ? new Vector3(i / (float)N - 0.5f, 0f, j / (float)N - 0.5f) : new Vector3(i / (float)N - 0.5f, -j / (float)N, 0f));
        // A seam: the middle column again, for the right half's triangles (as a UV seam makes).
        int extra = vertices.Count;
        if (seam)
            for (int j = 0; j <= N; j++) vertices.Add(vertices[j * (N + 1) + N / 2]);
        var triangles = new List<int>();
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int a = j * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
                if (seam && i == N / 2)
                {
                    a = extra + j;
                    c = extra + j + 1;
                }
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        var mesh = new Mesh { name = flat ? "flat" : "hanging" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Cloth Make(string name, bool flat, Vector3 position, bool seam = false, bool offscreen = true)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = Grid(flat, seam);
        smr.sharedMaterial = s_Material;
        smr.updateWhenOffscreen = offscreen;
        var cloth = go.AddComponent<Cloth>();
        if (!flat)
        {
            var co = cloth.coefficients;
            for (int i = 0; i <= N; i++) co[i].maxDistance = 0f;
            cloth.coefficients = co;
        }
        return cloth;
    }

    static GameObject Sphere(string name, Vector3 position, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = Vector3.one * (radius * 2f);
        return go;
    }

    /// <summary>Runs a cloth for the sampled frames; <paramref name="frame"/> acts at the start of each frame.</summary>
    IEnumerator Scenario(string name, bool flat, Action<Cloth, List<GameObject>> setup, Action<Cloth, int> frame = null, Vector3? position = null,
        bool seam = false, bool render = true, bool offscreen = true)
    {
        var cloth = Make(name, flat, position ?? new Vector3(0f, 2f, 0f), seam, offscreen);
        var extras = new List<GameObject>();
        setup?.Invoke(cloth, extras);
        var samples = new List<string>();
        int last = s_Frames[s_Frames.Length - 1];
        for (int f = 1; f <= last; f++)
        {
            yield return null;
            // Cloth simulates while its renderer is visible: a camera renders each frame.
            if (render) s_Camera.Render();
            frame?.Invoke(cloth, f);
            if (Array.IndexOf(s_Frames, f) >= 0)
                samples.Add($"[{f},{V(cloth.transform.position)},{Vs(cloth.vertices)}]");
        }
        Record("scenario", $"\"name\":\"{name}\",\"count\":{cloth.vertices.Length},\"coefficients\":{cloth.coefficients.Length},\"samples\":[{string.Join(",", samples)}],\"normals\":{Vs(cloth.normals)}");
        Destroy(cloth.gameObject);
        foreach (var e in extras) Destroy(e);
        yield return null;
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("CLOTH_PROBE_OUT") ?? "/tmp/cloth";
        Directory.CreateDirectory(dir);
        // A frame per physics step (cloth steps by the fixed time step).
        Time.captureDeltaTime = Time.fixedDeltaTime;
        s_Material = new Material(Shader.Find("Standard"));
        var cameraObject = new GameObject("camera");
        cameraObject.transform.position = new Vector3(0f, 1.5f, -6f);
        s_Camera = cameraObject.AddComponent<Camera>();
        s_Camera.targetTexture = new RenderTexture(64, 64, 24);
        yield return null;

        {
            var cloth = Make("defaults", false, new Vector3(0f, 50f, 0f));
            var c0 = cloth.coefficients[0];
            var c1 = cloth.coefficients[N + 5];
            Record("defaults", $"\"sleepThreshold\":{F(cloth.sleepThreshold)},\"bendingStiffness\":{F(cloth.bendingStiffness)},\"stretchingStiffness\":{F(cloth.stretchingStiffness)},\"damping\":{F(cloth.damping)},\"externalAcceleration\":{V(cloth.externalAcceleration)},\"randomAcceleration\":{V(cloth.randomAcceleration)},\"useGravity\":{(cloth.useGravity ? "true" : "false")},\"enabled\":{(cloth.enabled ? "true" : "false")},\"friction\":{F(cloth.friction)},\"collisionMassScale\":{F(cloth.collisionMassScale)},\"enableContinuousCollision\":{(cloth.enableContinuousCollision ? "true" : "false")},\"useVirtualParticles\":{F(cloth.useVirtualParticles)},\"worldVelocityScale\":{F(cloth.worldVelocityScale)},\"worldAccelerationScale\":{F(cloth.worldAccelerationScale)},\"clothSolverFrequency\":{F(cloth.clothSolverFrequency)},\"useTethers\":{(cloth.useTethers ? "true" : "false")},\"stiffnessFrequency\":{F(cloth.stiffnessFrequency)},\"selfCollisionDistance\":{F(cloth.selfCollisionDistance)},\"selfCollisionStiffness\":{F(cloth.selfCollisionStiffness)},\"coefficients\":{cloth.coefficients.Length},\"pinned\":[{F(c0.maxDistance)},{F(c0.collisionSphereDistance)}],\"free\":[{F(c1.maxDistance)},{F(c1.collisionSphereDistance)}],\"vertices\":{cloth.vertices.Length},\"first\":{V(cloth.vertices[0])},\"last\":{V(cloth.vertices[cloth.vertices.Length - 1])}");
            Destroy(cloth.gameObject);
            yield return null;
        }
        {
            // The virtual particles and collision indices Unity makes, once simulated.
            var cloth = Make("particles", false, new Vector3(0f, 50f, 0f));
            yield return null;
            s_Camera.Render();
            yield return null;
            var indices = new List<uint>();
            var weights = new List<Vector3>();
            var self = new List<uint>();
            cloth.GetVirtualParticleIndices(indices);
            cloth.GetVirtualParticleWeights(weights);
            cloth.GetSelfAndInterCollisionIndices(self);
            Record("particles", $"\"indices\":[{string.Join(",", indices)}],\"weights\":{Vs(weights)},\"self\":[{string.Join(",", self)}]");
            Destroy(cloth.gameObject);
            yield return null;
        }

        yield return Scenario("hanging", false, null);
        yield return Scenario("stretching", false, (c, e) => c.stretchingStiffness = 0.3f);
        yield return Scenario("bending", false, (c, e) => { c.bendingStiffness = 0.5f; c.externalAcceleration = new Vector3(0f, 0f, 4f); });
        yield return Scenario("damping", false, (c, e) => { c.damping = 0.5f; c.externalAcceleration = new Vector3(3f, 0f, 0f); });
        yield return Scenario("external", false, (c, e) => c.externalAcceleration = new Vector3(3f, 0f, 1f));
        yield return Scenario("no gravity", false, (c, e) => { c.useGravity = false; c.externalAcceleration = new Vector3(0f, 0f, 2f); });
        yield return Scenario("frequency 30", false, (c, e) => { c.clothSolverFrequency = 30f; c.externalAcceleration = new Vector3(0f, 0f, 3f); });
        yield return Scenario("frequency 300", false, (c, e) => { c.clothSolverFrequency = 300f; c.externalAcceleration = new Vector3(0f, 0f, 3f); });
        yield return Scenario("stiffness frequency", false, (c, e) => { c.stiffnessFrequency = 3f; c.stretchingStiffness = 0.6f; c.externalAcceleration = new Vector3(0f, 0f, 3f); });
        yield return Scenario("no tethers", false, (c, e) => { c.useTethers = false; c.externalAcceleration = new Vector3(0f, 0f, 3f); });
        yield return Scenario("max distance", false, (c, e) =>
        {
            var co = c.coefficients;
            for (int i = N + 1; i < co.Length; i++) co[i].maxDistance = 0.2f;
            c.coefficients = co;
            c.externalAcceleration = new Vector3(0f, 0f, 5f);
        });
        yield return Scenario("sphere distance", false, (c, e) =>
        {
            var co = c.coefficients;
            for (int i = N + 1; i < co.Length; i++) co[i].collisionSphereDistance = 0.1f;
            c.coefficients = co;
            c.externalAcceleration = new Vector3(0f, 0f, -5f);
        });
        yield return Scenario("sphere", false, (c, e) =>
        {
            var s = Sphere("ball", new Vector3(0.1f, 1.4f, 0.15f), 0.3f);
            e.Add(s);
            c.sphereColliders = new[] { new ClothSphereColliderPair(s.GetComponent<SphereCollider>()) };
        });
        yield return Scenario("sphere pair", false, (c, e) =>
        {
            var a = Sphere("a", new Vector3(-0.4f, 1.5f, 0.1f), 0.1f);
            var b = Sphere("b", new Vector3(0.4f, 1.3f, 0.1f), 0.25f);
            e.Add(a);
            e.Add(b);
            c.sphereColliders = new[] { new ClothSphereColliderPair(a.GetComponent<SphereCollider>(), b.GetComponent<SphereCollider>()) };
        });
        yield return Scenario("capsule", false, (c, e) =>
        {
            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cap.name = "capsule";
            cap.transform.position = new Vector3(0f, 1.5f, 0.1f);
            cap.transform.rotation = Quaternion.Euler(0f, 0f, 70f);
            cap.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
            e.Add(cap);
            c.capsuleColliders = new[] { cap.GetComponent<CapsuleCollider>() };
        });
        yield return Scenario("drape", true, (c, e) =>
        {
            var s = Sphere("ball", new Vector3(0.05f, 1.2f, -0.03f), 0.35f);
            e.Add(s);
            c.sphereColliders = new[] { new ClothSphereColliderPair(s.GetComponent<SphereCollider>()) };
        });
        yield return Scenario("friction", true, (c, e) =>
        {
            var s = Sphere("ball", new Vector3(0.05f, 1.2f, -0.03f), 0.35f);
            e.Add(s);
            c.sphereColliders = new[] { new ClothSphereColliderPair(s.GetComponent<SphereCollider>()) };
            c.friction = 1f;
        });
        yield return Scenario("collision mass scale", true, (c, e) =>
        {
            var s = Sphere("ball", new Vector3(0.05f, 1.2f, -0.03f), 0.35f);
            e.Add(s);
            c.sphereColliders = new[] { new ClothSphereColliderPair(s.GetComponent<SphereCollider>()) };
            c.collisionMassScale = 2f;
        });
        yield return Scenario("virtual particles", true, (c, e) =>
        {
            var s = Sphere("ball", new Vector3(0.05f, 1.2f, -0.03f), 0.35f);
            e.Add(s);
            c.sphereColliders = new[] { new ClothSphereColliderPair(s.GetComponent<SphereCollider>()) };
            c.useVirtualParticles = 1f - c.useVirtualParticles;
        });
        yield return Scenario("self collision", true, (c, e) =>
        {
            var s = Sphere("ball", new Vector3(0.05f, 1.2f, -0.03f), 0.2f);
            e.Add(s);
            c.sphereColliders = new[] { new ClothSphereColliderPair(s.GetComponent<SphereCollider>()) };
            c.selfCollisionDistance = 0.05f;
            c.selfCollisionStiffness = 1f;
            var indices = new List<uint>();
            for (uint i = 0; i < c.vertices.Length; i++) indices.Add(i);
            c.SetSelfAndInterCollisionIndices(indices);
        });
        yield return Scenario("moving", false, null, (c, f) => c.transform.position = new Vector3(0.5f * Mathf.Sin(f * 0.1f), 2f, 0f));
        yield return Scenario("moving scaled", false, (c, e) => { c.worldVelocityScale = 1f; c.worldAccelerationScale = 0.2f; },
            (c, f) => c.transform.position = new Vector3(0.5f * Mathf.Sin(f * 0.1f), 2f, 0f));
        yield return Scenario("rotating", false, null, (c, f) => c.transform.rotation = Quaternion.Euler(0f, f * 3f, 0f));
        yield return Scenario("scaled", false, (c, e) => c.transform.localScale = new Vector3(2f, 1f, 1f));
        yield return Scenario("teleport", false, null, (c, f) =>
        {
            if (f == 30) c.transform.position = new Vector3(3f, 2f, 0f);
            if (f == 60)
            {
                c.transform.position = new Vector3(-3f, 2f, 0f);
                c.ClearTransformMotion();
            }
        });
        yield return Scenario("disabled", false, (c, e) => c.externalAcceleration = new Vector3(0f, 0f, 3f), (c, f) =>
        {
            if (f == 20) c.enabled = false;
            if (f == 50) c.enabled = true;
        });
        yield return Scenario("sleep", false, (c, e) => c.sleepThreshold = 0.5f);
        yield return Scenario("continuous", true, (c, e) =>
        {
            var s = Sphere("ball", new Vector3(0f, 0.5f, 0f), 0.2f);
            e.Add(s);
            c.sphereColliders = new[] { new ClothSphereColliderPair(s.GetComponent<SphereCollider>()) };
            c.enableContinuousCollision = true;
            c.useGravity = false;
        }, (c, f) => c.sphereColliders[0].first.transform.position = new Vector3(0f, f < 30 ? 0.5f + f * 0.08f : 2.9f, 0f));

        yield return Scenario("velocity scale 0", false, (c, e) => c.worldVelocityScale = 0f,
            (c, f) => c.transform.position = new Vector3(0.5f * Mathf.Sin(f * 0.1f), 2f, 0f));
        yield return Scenario("velocity scale 0.25", false, (c, e) => { c.worldVelocityScale = 0.25f; c.worldAccelerationScale = 0.5f; },
            (c, f) => c.transform.position = new Vector3(0.5f * Mathf.Sin(f * 0.1f), 2f, 0f));
        yield return Scenario("seam", false, (c, e) => c.externalAcceleration = new Vector3(0f, 0f, 3f), seam: true);
        yield return Scenario("hidden", false, (c, e) => c.externalAcceleration = new Vector3(0f, 0f, 3f), render: false, offscreen: false);
        yield return Scenario("offscreen", false, (c, e) => c.externalAcceleration = new Vector3(0f, 0f, 3f), render: false, offscreen: true);
        yield return Scenario("not offscreen", false, (c, e) => c.externalAcceleration = new Vector3(0f, 0f, 3f), render: true, offscreen: false);
        yield return Scenario("no material", false, (c, e) =>
        {
            c.externalAcceleration = new Vector3(0f, 0f, 3f);
            c.GetComponent<SkinnedMeshRenderer>().sharedMaterial = null;
        }, offscreen: false);
        yield return Scenario("renderer disabled", false, (c, e) =>
        {
            c.externalAcceleration = new Vector3(0f, 0f, 3f);
            c.GetComponent<SkinnedMeshRenderer>().enabled = false;
        });
        yield return Scenario("fading", false, (c, e) => c.externalAcceleration = new Vector3(0f, 0f, 3f), (c, f) =>
        {
            if (f == 20) c.SetEnabledFading(false, 0.5f);
            if (f == 50) c.SetEnabledFading(true, 0.5f);
        });
        {
            // Skinned by a bone that moves: the fixed vertices follow it.
            Transform bone = null;
            yield return Scenario("bones", false, (c, e) =>
            {
                var smr = c.GetComponent<SkinnedMeshRenderer>();
                var mesh = smr.sharedMesh;
                bone = new GameObject("bone").transform;
                bone.SetParent(c.transform, false);
                var weights = new BoneWeight[mesh.vertexCount];
                for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                mesh.boneWeights = weights;
                mesh.bindposes = new[] { Matrix4x4.identity };
                smr.bones = new[] { bone };
                smr.rootBone = bone;
            }, (c, f) => bone.localPosition = new Vector3(0.3f * Mathf.Sin(f * 0.15f), 0f, 0.2f * Mathf.Sin(f * 0.1f)));
        }

        File.WriteAllText(Path.Combine(dir, "cloth.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
