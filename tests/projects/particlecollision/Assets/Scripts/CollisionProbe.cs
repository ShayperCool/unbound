using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// Play mode cases: what particle collision events report (colliderComponent, the safe event
/// size) and how a force field's vector field moves particles. Writes JSON lines {"k", "v"}.
public class CollisionProbe : MonoBehaviour
{
    public static bool Finished;
    static string Out => Environment.GetEnvironmentVariable("PARTICLE_COLLISION_OUT");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (Out == null) return;
        new GameObject("CollisionProbe").AddComponent<CollisionProbe>();
    }

    static void Put(string key, string json) => File.AppendAllText(Out, "{\"k\": \"" + key + "\", \"v\": " + json + "}\n");
    static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string Num(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string Vec(Vector3 v) => "[" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + "]";

    static ParticleSystem Emitter(string name, Vector3 position, bool twoD)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(90, 0, 0); // shoots down (-y)
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startSpeed = 10f;
        main.startLifetime = 2f;
        main.startSize = 0.1f;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 20f;
        var shape = ps.shape;
        shape.enabled = false;
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = twoD ? ParticleSystemCollisionMode.Collision2D : ParticleSystemCollisionMode.Collision3D;
        collision.sendCollisionMessages = true;
        collision.bounce = 0f;
        collision.lifetimeLoss = 1f;
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 7;
        go.AddComponent<HitRecorder>();
        return ps;
    }

    static T Target<T>(string name, Vector3 position, Transform parent = null) where T : Component
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = new Vector3(4, 0.5f, 4);
        go.AddComponent<HitRecorder>();
        return go.AddComponent<T>();
    }

    IEnumerator Collisions()
    {
        // 3D: a static box, a box with a Rigidbody, a box child of a Rigidbody parent, far apart.
        Target<BoxCollider>("Static", new Vector3(0, -3, 0));
        Emitter("ToStatic", new Vector3(0, 0, 0), false);
        var body = Target<BoxCollider>("Body", new Vector3(20, -3, 0));
        var rb = body.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        Emitter("ToBody", new Vector3(20, 0, 0), false);
        var parent = new GameObject("Parent");
        parent.transform.position = new Vector3(40, -3, 0);
        parent.AddComponent<HitRecorder>();
        var parentBody = parent.AddComponent<Rigidbody>();
        parentBody.isKinematic = true;
        Target<BoxCollider>("Child", new Vector3(40, -3, 0), parent.transform);
        Emitter("ToChild", new Vector3(40, 0, 0), false);
        // Two targets hit by one system: the safe size against each target's own events.
        Target<BoxCollider>("Left", new Vector3(59, -3, 0));
        Target<BoxCollider>("Right", new Vector3(61, -3, 0));
        var wide = Emitter("ToBoth", new Vector3(60, 0, 0), false);
        var wideShape = wide.shape;
        wideShape.enabled = true;
        wideShape.shapeType = ParticleSystemShapeType.Box;
        wideShape.scale = new Vector3(4, 0.1f, 0.1f);
        wideShape.randomDirectionAmount = 0f;
        var wideEmission = wide.emission;
        wideEmission.rateOverTime = 1000f;
        // 2D.
        Target<BoxCollider2D>("Static2D", new Vector3(80, -3, 0));
        Emitter("ToStatic2D", new Vector3(80, 0, 0), true);
        // Planes.
        var plane = new GameObject("Plane");
        plane.transform.position = new Vector3(100, -3, 0);
        plane.AddComponent<HitRecorder>();
        var planes = Emitter("ToPlane", new Vector3(100, 0, 0), false);
        var pc = planes.collision;
        pc.type = ParticleSystemCollisionType.Planes;
        pc.SetPlane(0, plane.transform);
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Play();
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var hit in FindObjectsByType<HitRecorder>(FindObjectsSortMode.InstanceID))
            Put("collide/" + hit.name, Str((hit.calls > 0 ? "called from " + string.Join(",", hit.from) : "not called") + (hit.first == null ? "" : "; " + hit.first)));
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    static Texture3D Field()
    {
        const int n = 4;
        var texture = new Texture3D(n, n, n, TextureFormat.RGBAFloat, false);
        var colors = new Color[n * n * n];
        for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    colors[x + n * (y + n * z)] = new Color(x / 3f, 1f - y / 3f, 0.5f * z / 3f, 1f);
        texture.SetPixels(colors);
        texture.Apply();
        return texture;
    }

    void VectorField(string name, float attraction, Texture3D texture, Vector3 at, float dt = 0.05f)
    {
        var fieldGo = new GameObject(name + "Field");
        fieldGo.transform.position = at;
        var field = fieldGo.AddComponent<ParticleSystemForceField>();
        field.shape = ParticleSystemForceFieldShape.Box;
        field.endRange = 2f;
        field.directionX = field.directionY = field.directionZ = 0f;
        field.gravity = 0f;
        field.rotationSpeed = 0f;
        field.drag = 0f;
        field.vectorField = texture;
        field.vectorFieldSpeed = 3f;
        field.vectorFieldAttraction = attraction;
        var go = new GameObject(name);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startSpeed = 0f;
        main.startLifetime = 10f;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var forces = ps.externalForces;
        forces.enabled = true;
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 3;
        var starts = new[] { new Vector3(-1.5f, -1.5f, -1.5f), Vector3.zero, new Vector3(0.7f, -0.3f, 1.2f), new Vector3(3f, 0f, 0f) };
        foreach (var start in starts)
            ps.Emit(new ParticleSystem.EmitParams { position = at + start, velocity = new Vector3(0.5f, 0f, 0f), startLifetime = 10f, startSize = 0.1f, applyShapeToPosition = false }, 1);
        var particles = new ParticleSystem.Particle[8];
        for (int step = 1; step <= 5; step++)
        {
            ps.Simulate(dt, true, false, false);
            int count = ps.GetParticles(particles);
            for (int i = 0; i < count; i++)
                Put("field/" + name + "/" + step + "/" + i, "[" + Vec(particles[i].position - at) + ", " + Vec(particles[i].velocity) + "]");
        }
    }

    IEnumerator Start()
    {
        Time.captureFramerate = 50;
        for (int i = 0; i < 3; i++) yield return null;
        yield return Collisions();
        var texture = Field();
        VectorField("speed", 0f, texture, new Vector3(0, 50, 0));
        VectorField("attract", 0.5f, texture, new Vector3(20, 50, 0));
        VectorField("pull", 1f, texture, new Vector3(40, 50, 0));
        VectorField("pull2", 2f, texture, new Vector3(60, 50, 0));
        VectorField("pullstep", 1f, texture, new Vector3(80, 50, 0), 0.02f);
        Put("complete", "\"1\"");
        Finished = true;
    }
}
