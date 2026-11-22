using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Which colliders a World collision meets (<c>collide/rules/*</c>), 3D and 2D, as
/// <see cref="CollideControls"/>: systems shooting down at boxes at the capture delta 0.02 s, each
/// particle's position, velocity and remaining lifetime per frame, and per target the number of
/// collision events naming it. Dynamic and Kinematic bodies at Medium quality and at High with
/// enableDynamicColliders off; a trigger (Physics.queriesHitTriggers as set by default); two boxes
/// under a system with maxCollisionShapes 1; two equal boxes at the same place; a system and its box
/// in a local physics scene, and a system there over a box of the main scene; 2D boxes merged by a
/// CompositeCollider2D (outlines and polygons). <c>force/rules/*</c>: colliderForce on free
/// Rigidbodies (as <c>force/*</c>) with a negative force, the size multiplier at another radius
/// scale and a non-uniform 3D start size, and the angle multiplier for particles starting inside the
/// body. Low quality over Dynamic and Kinematic bodies comes last.
/// </summary>
public partial class ParticleRenderProbe
{
    static GameObject RuleBox(Vector3 at, bool twoD, string name)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        if (twoD) go.AddComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
        else go.AddComponent<BoxCollider>().size = new Vector3(4f, 0.2f, 4f);
        return go;
    }

    IEnumerator CollideRuleControls()
    {
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var controls = new List<(string key, ParticleSystem ps, Vector3 origin)>();
        var targets = new List<(string key, ParticleSystem ps, GameObject target)>();
        var extra = new List<GameObject>();
        int slot = 0;
        Vector3 Next() => s_CollideOrigin + new Vector3(8f * slot++, 0f, 80f);

        ParticleSystem System(string key, Vector3 at, bool twoD, ParticleSystemCollisionQuality quality, System.Action<ParticleSystem.CollisionModule> configure = null)
        {
            var ps = CollideEmitter(key, at + Vector3.up, 4, 3f, s =>
            {
                var c = s.collision; c.sendCollisionMessages = true; c.quality = quality;
                if (twoD) c.mode = ParticleSystemCollisionMode.Collision2D;
                configure?.Invoke(c);
            });
            controls.Add((key, ps, at));
            return ps;
        }
        void Target(string key, ParticleSystem ps, GameObject go)
        {
            targets.Add((key, ps, go));
            extra.Add(go);
        }

        var high = ParticleSystemCollisionQuality.High;
        var medium = ParticleSystemCollisionQuality.Medium;
        foreach (bool twoD in new[] { false, true })
        {
            string d = twoD ? "2d" : "3d";
            // Bodies by quality and enableDynamicColliders.
            foreach (var (name, quality, dynamicColliders, kinematic) in new[]
            {
                ("medium_dynamic", medium, true, false), ("medium_kinematic", medium, true, true),
                ("nodyn_dynamic", high, false, false), ("nodyn_kinematic", high, false, true),
            })
            {
                var at = Next();
                var key = "collide/rules/" + d + "/" + name;
                var ps = System(key, at, twoD, quality, c => c.enableDynamicColliders = dynamicColliders);
                var box = RuleBox(at + new Vector3(0f, -0.5f, 0f), twoD, key + "/box");
                if (twoD)
                {
                    var body = box.AddComponent<Rigidbody2D>();
                    body.gravityScale = 0f;
                    body.bodyType = kinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
                }
                else
                {
                    var body = box.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = kinematic;
                }
                Target(key + "/box", ps, box);
            }
            {
                var at = Next();
                var key = "collide/rules/" + d + "/trigger";
                var ps = System(key, at, twoD, high);
                var box = RuleBox(at + new Vector3(0f, -0.5f, 0f), twoD, key + "/box");
                if (twoD) box.GetComponent<BoxCollider2D>().isTrigger = true;
                else box.GetComponent<BoxCollider>().isTrigger = true;
                Target(key + "/box", ps, box);
            }
            // maxCollisionShapes 1: the left box under the particles left of the center, the right
            // box under the others.
            {
                var at = Next();
                var key = "collide/rules/" + d + "/maxshapes";
                var ps = System(key, at, twoD, high, c => c.maxCollisionShapes = 1);
                var left = RuleBox(at + new Vector3(-1.05f, -0.5f, 0f), twoD, key + "/left");
                var right = RuleBox(at + new Vector3(1.05f, -0.8f, 0f), twoD, key + "/right");
                if (twoD)
                {
                    left.GetComponent<BoxCollider2D>().size = new Vector2(2f, 0.2f);
                    right.GetComponent<BoxCollider2D>().size = new Vector2(2f, 0.2f);
                }
                else
                {
                    left.GetComponent<BoxCollider>().size = new Vector3(2f, 0.2f, 4f);
                    right.GetComponent<BoxCollider>().size = new Vector3(2f, 0.2f, 4f);
                }
                Target(key + "/left", ps, left);
                Target(key + "/right", ps, right);
            }
            // Two equal boxes at the same place.
            {
                var at = Next();
                var key = "collide/rules/" + d + "/equal";
                var ps = System(key, at, twoD, high);
                Target(key + "/first", ps, RuleBox(at + new Vector3(0f, -0.5f, 0f), twoD, key + "/first"));
                Target(key + "/second", ps, RuleBox(at + new Vector3(0f, -0.5f, 0f), twoD, key + "/second"));
            }
        }
        // 2D boxes merged by a CompositeCollider2D: a slope of two overlapping boxes.
        foreach (var (name, geometry) in new[] { ("outlines", CompositeCollider2D.GeometryType.Outlines), ("polygons", CompositeCollider2D.GeometryType.Polygons) })
        {
            var at = Next();
            var key = "collide/rules/2d/composite_" + name;
            var ps = System(key, at, true, high);
            var parent = new GameObject(key + "/composite");
            parent.transform.position = at + new Vector3(0f, -0.5f, 0f);
            parent.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            var composite = parent.AddComponent<CompositeCollider2D>();
            composite.geometryType = geometry;
            foreach (var (x, angle) in new[] { (-0.8f, 20f), (0.8f, -10f) })
            {
                var child = new GameObject("part");
                child.transform.SetParent(parent.transform, false);
                child.transform.localPosition = new Vector3(x, 0f, 0f);
                child.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                var part = child.AddComponent<BoxCollider2D>();
                part.size = new Vector2(2.2f, 0.3f);
                part.compositeOperation = Collider2D.CompositeOperation.Merge;
            }
            composite.GenerateGeometry();
            Target(key + "/composite", ps, parent);
        }
        // Local physics scenes: the system and its box there, and a system there over a box of
        // the main scene.
        var locals = new List<Scene>();
        foreach (bool twoD in new[] { false, true })
        {
            string d = twoD ? "2d" : "3d";
            var scene = SceneManager.CreateScene("particle rules " + d,
                new CreateSceneParameters(twoD ? LocalPhysicsMode.Physics2D : LocalPhysicsMode.Physics3D));
            locals.Add(scene);
            {
                var at = Next();
                var key = "collide/rules/" + d + "/local";
                var ps = System(key, at, twoD, high);
                var box = RuleBox(at + new Vector3(0f, -0.5f, 0f), twoD, key + "/box");
                SceneManager.MoveGameObjectToScene(ps.gameObject, scene);
                SceneManager.MoveGameObjectToScene(box, scene);
                targets.Add((key + "/box", ps, box));
            }
            {
                var at = Next();
                var key = "collide/rules/" + d + "/local_split";
                var ps = System(key, at, twoD, high);
                var box = RuleBox(at + new Vector3(0f, -0.5f, 0f), twoD, key + "/box");
                SceneManager.MoveGameObjectToScene(ps.gameObject, scene);
                Target(key + "/box", ps, box);
            }
        }
        // Low quality (it queries every fifth frame) over Dynamic and Kinematic bodies.
        foreach (bool twoD in new[] { false, true })
            foreach (bool kinematic in new[] { false, true })
            {
                string d = twoD ? "2d" : "3d";
                var at = Next();
                var key = "collide/rules/" + d + "/low_" + (kinematic ? "kinematic" : "dynamic");
                var ps = System(key, at, twoD, ParticleSystemCollisionQuality.Low);
                var box = RuleBox(at + new Vector3(0f, -0.5f, 0f), twoD, key + "/box");
                if (twoD)
                {
                    var body = box.AddComponent<Rigidbody2D>();
                    body.gravityScale = 0f;
                    body.bodyType = kinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
                }
                else
                {
                    var body = box.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = kinematic;
                }
                Target(key + "/box", ps, box);
            }
        Physics.SyncTransforms();
        Physics2D.SyncTransforms();
        foreach (var (_, ps, _) in controls) ps.Play();
        for (int frame = 0; frame < 40; frame++)
        {
            yield return null;
            string f = frame.ToString(CultureInfo.InvariantCulture);
            foreach (var (key, ps, origin) in controls) RecordCollide(key + "/" + f, ps, origin);
            foreach (var (key, ps, target) in targets)
                Record(key + "/events/" + f, new List<float> { ps.GetCollisionEvents(target, s_CollisionEvents) });
        }
        foreach (var (_, ps, _) in controls) Done(ps);
        foreach (var go in extra) DestroyImmediate(go);
        foreach (var scene in locals) yield return SceneManager.UnloadSceneAsync(scene);
        yield return ColliderForceRules();
        Time.captureDeltaTime = 0f;
    }

    /// <summary>As <see cref="ColliderForce"/>: a negative force, and the size multiplier with a
    /// radius scale of 0.5 and with a 3D start size of (0.2, 0.4, 0.6).</summary>
    IEnumerator ColliderForceRules()
    {
        Time.captureDeltaTime = 1f / 20f;
        var cases = new[] { ("negative", -2f, false, 1f), ("size_radius", 2f, false, 0.5f), ("size_3d", 2f, true, 1f), ("angle_inside", 2f, false, 1f) };
        var bodies = new List<(string, Rigidbody, ParticleSystem)>();
        int slot = 0;
        foreach (var (name, force, size3D, radiusScale) in cases)
        {
            var origin = new Vector3(30f + 6f * slot++, 0f, 40f);
            bool inside = name == "angle_inside";
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "force/rules/" + name + "/body";
            // angle_inside: the particles start inside the body, nearer its front face (z -0.15)
            // than the back one they move to (z 0.35).
            box.transform.position = origin + new Vector3(0f, 0f, inside ? 0.1f : 3f);
            box.transform.localScale = new Vector3(2f, 2f, 0.5f);
            var body = box.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = 1f;
            body.angularDamping = 0f;
            var ps = Make("force/rules/" + name, s =>
            {
                var m = s.main; m.startSpeed = 6f; m.startLifetime = 2f; m.startSize = 0.4f; m.loop = false; m.duration = 1f;
                if (size3D)
                {
                    m.startSize3D = true;
                    m.startSizeX = 0.2f; m.startSizeY = 0.4f; m.startSizeZ = 0.6f;
                }
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var e = s.emission; e.enabled = true; e.rateOverTime = 0f;
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });
                var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
                c.bounce = 0f; c.lifetimeLoss = 1f; c.dampen = 0f; c.radiusScale = radiusScale; c.quality = ParticleSystemCollisionQuality.High;
                c.colliderForce = force;
                c.multiplyColliderForceByParticleSize = force > 0f && !inside;
                c.multiplyColliderForceByCollisionAngle = inside;
            });
            ps.transform.position = origin;
            bodies.Add(("force/rules/" + name, body, ps));
        }
        yield return null;
        foreach (var (_, _, ps) in bodies) ps.Play();
        for (int frame = 0; frame < 20; frame++)
        {
            yield return null;
            foreach (var (key, body, ps) in bodies)
            {
                var v = body.linearVelocity; var w = body.angularVelocity;
                Record(key + "/" + frame, new List<float> { ps.particleCount, v.x, v.y, v.z, w.x, w.y, w.z });
            }
        }
        foreach (var (_, body, ps) in bodies) { Done(ps); DestroyImmediate(body.gameObject); }
    }
}
