using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// World collision in Collision2D mode (<c>collide/world2d/*</c>), as <see cref="CollideControls"/>
/// in 3D: systems shooting down at 2D colliders at the capture delta 0.02 s, each particle's
/// position, velocity and remaining lifetime per frame; a sloped BoxCollider2D (size over lifetime,
/// radius scale, bounce, dampen, lifetime loss, gravity), a CircleCollider2D, particles starting
/// inside a BoxCollider2D (near its side faces and near the face they move through, also with other
/// Physics2D.baumgarteScale and maxLinearCorrection), a BoxCollider2D away from the particles in
/// depth, two boxes in the path, a CapsuleCollider2D, a PolygonCollider2D, an EdgeCollider2D with
/// and without a radius, a Kinematic body, colliders the particles pass (a trigger, another layer,
/// a Dynamic body without enableDynamicColliders, maxCollisionShapes 0, a radius below 0.0001),
/// colliderForce on Dynamic bodies with each multiplier (the bodies' velocities), and the
/// collision events (count, intersection, normal, velocity, the collider's type) of one control
/// and of particles moving in depth.
/// </summary>
public partial class ParticleRenderProbe
{
    static GameObject Collide2D<T>(Vector3 at, Quaternion rotation) where T : Collider2D
    {
        var go = new GameObject("collide 2d");
        go.transform.position = at;
        go.transform.rotation = rotation;
        go.AddComponent<T>();
        return go;
    }

    static void RecordCollisionEvents2D(string key, ParticleSystem ps, GameObject target)
    {
        var v = new List<float>();
        int n = ps.GetCollisionEvents(target, s_CollisionEvents);
        v.Add(n);
        s_CollisionEvents.Sort((a, b) => a.intersection.x.CompareTo(b.intersection.x));
        for (int i = 0; i < n; i++)
        {
            var e = s_CollisionEvents[i];
            var at = e.intersection - ps.transform.position;
            v.Add(at.x); v.Add(at.y); v.Add(at.z);
            v.Add(e.normal.x); v.Add(e.normal.y); v.Add(e.normal.z);
            v.Add(e.velocity.x); v.Add(e.velocity.y); v.Add(e.velocity.z);
            v.Add(e.colliderComponent is BoxCollider2D ? 1 : e.colliderComponent is Collider2D ? 2 : 0);
        }
        Record(key, v);
    }

    IEnumerator Collide2DControls()
    {
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var controls = new List<(string key, ParticleSystem ps, Vector3 origin)>();
        var extra = new List<GameObject>();
        int slot = 0;
        Vector3 Next() => s_CollideOrigin + new Vector3(8f * slot++, 0f, 40f);
        void TwoD(ParticleSystem s) { var c = s.collision; c.mode = ParticleSystemCollisionMode.Collision2D; }

        foreach (var (name, quality) in new[] { ("slope", ParticleSystemCollisionQuality.High), ("slope_medium", ParticleSystemCollisionQuality.Medium) })
        {
            var at = Next();
            controls.Add(("collide/world2d/" + name, CollideEmitter("collide/world2d/" + name, at + Vector3.up, 6, 3f, s =>
            {
                SlopeSettings(s, quality);
                TwoD(s);
            }), at));
            var box = Collide2D<BoxCollider2D>(at + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(0f, 0f, 25f));
            box.GetComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            extra.Add(box);
        }
        {
            var at = Next();
            controls.Add(("collide/world2d/circle", CollideEmitter("collide/world2d/circle", at + Vector3.up, 6, 3f, s =>
            {
                var m = s.main; m.startSize = 0.4f;
                var c = s.collision; c.bounce = 0.9f;
                TwoD(s);
            }), at));
            var circle = Collide2D<CircleCollider2D>(at + new Vector3(0.1f, -0.4f, 0f), Quaternion.identity);
            circle.GetComponent<CircleCollider2D>().radius = 0.6f;
            extra.Add(circle);
        }
        {
            var at = Next();
            controls.Add(("collide/world2d/inside", CollideEmitter("collide/world2d/inside", at, 3, 1f, s =>
            {
                var m = s.main; m.startSize = 0.2f;
                var c = s.collision; c.bounce = 0.5f;
                TwoD(s);
            }), at));
            var box = Collide2D<BoxCollider2D>(at, Quaternion.identity);
            box.GetComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
            extra.Add(box);
        }
        {
            // A collider at another depth than the particles.
            var at = Next();
            controls.Add(("collide/world2d/depth", CollideEmitter("collide/world2d/depth", at + Vector3.up, 4, 3f, TwoD), at));
            var box = Collide2D<BoxCollider2D>(at + new Vector3(0f, -0.5f, 5f), Quaternion.identity);
            box.GetComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            extra.Add(box);
        }
        GameObject eventTarget;
        ParticleSystem eventSystem;
        {
            var at = Next();
            eventSystem = CollideEmitter("collide/world2d/events", at + Vector3.up, 4, 3f, s =>
            {
                var c = s.collision; c.bounce = 0.5f; c.dampen = 0.3f; c.lifetimeLoss = 0.5f; c.sendCollisionMessages = true;
                TwoD(s);
            });
            controls.Add(("collide/world2d/events", eventSystem, at));
            eventTarget = Collide2D<BoxCollider2D>(at + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(0f, 0f, -15f));
            eventTarget.GetComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            extra.Add(eventTarget);
        }
        // The other collider shapes, each under the emitter like the slope: a capsule, a
        // triangle, a sloped edge (plain and rounded) and a box of a Kinematic body.
        {
            var at = Next();
            controls.Add(("collide/world2d/capsule", CollideEmitter("collide/world2d/capsule", at + Vector3.up, 6, 3f, TwoD), at));
            var capsule = Collide2D<CapsuleCollider2D>(at + new Vector3(0.2f, -0.5f, 0f), Quaternion.Euler(0f, 0f, 70f));
            capsule.GetComponent<CapsuleCollider2D>().size = new Vector2(0.6f, 3f);
            extra.Add(capsule);
        }
        {
            var at = Next();
            controls.Add(("collide/world2d/polygon", CollideEmitter("collide/world2d/polygon", at + Vector3.up, 6, 3f, s =>
            {
                var c = s.collision; c.bounce = 0.8f;
                TwoD(s);
            }), at));
            var polygon = Collide2D<PolygonCollider2D>(at + new Vector3(0f, -0.6f, 0f), Quaternion.identity);
            polygon.GetComponent<PolygonCollider2D>().points = new[] { new Vector2(-1.5f, -0.3f), new Vector2(1.5f, -0.3f), new Vector2(0.3f, 0.4f) };
            extra.Add(polygon);
        }
        foreach (var (name, radius) in new[] { ("edge", 0f), ("edge_rounded", 0.1f) })
        {
            var at = Next();
            controls.Add(("collide/world2d/" + name, CollideEmitter("collide/world2d/" + name, at + Vector3.up, 6, 3f, TwoD), at));
            var edge = Collide2D<EdgeCollider2D>(at + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(0f, 0f, -20f));
            var e = edge.GetComponent<EdgeCollider2D>();
            e.points = new[] { new Vector2(-2f, 0f), new Vector2(2f, 0f) };
            e.edgeRadius = radius;
            extra.Add(edge);
        }
        // A box under a Kinematic body on its parent: the events of the body's and the box's
        // GameObjects.
        GameObject kinematicBody, kinematicBox;
        ParticleSystem kinematicSystem;
        {
            var at = Next();
            kinematicSystem = CollideEmitter("collide/world2d/kinematic", at + Vector3.up, 4, 3f, s =>
            {
                var c = s.collision; c.colliderForce = 2f; c.sendCollisionMessages = true;
                TwoD(s);
            });
            controls.Add(("collide/world2d/kinematic", kinematicSystem, at));
            kinematicBody = new GameObject("collide 2d body");
            kinematicBody.transform.position = at + new Vector3(0f, -0.5f, 0f);
            kinematicBody.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            kinematicBox = Collide2D<BoxCollider2D>(at + new Vector3(0f, -0.5f, 0f), Quaternion.identity);
            kinematicBox.GetComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            kinematicBox.transform.SetParent(kinematicBody.transform, true);
            extra.Add(kinematicBody);
        }
        // Colliders the particles pass: a trigger, a box on a layer collidesWith leaves out, a
        // Dynamic body with enableDynamicColliders off, maxCollisionShapes 0, and particles whose
        // radius (size 0.3 times a radius scale of 0.0005) is below 0.0001.
        foreach (var name in new[] { "trigger", "layer", "nodynamic", "noshapes", "tiny" })
        {
            var at = Next();
            controls.Add(("collide/world2d/" + name, CollideEmitter("collide/world2d/" + name, at + Vector3.up, 4, 3f, s =>
            {
                var c = s.collision;
                TwoD(s);
                if (name == "layer") c.collidesWith = ~(1 << 2);
                if (name == "nodynamic") c.enableDynamicColliders = false;
                if (name == "noshapes") c.maxCollisionShapes = 0;
                if (name == "tiny") c.radiusScale = 0.0005f;
            }), at));
            var box = Collide2D<BoxCollider2D>(at + new Vector3(0f, -0.5f, 0f), Quaternion.identity);
            box.GetComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            if (name == "trigger") box.GetComponent<BoxCollider2D>().isTrigger = true;
            if (name == "layer") box.layer = 2;
            if (name == "nodynamic")
            {
                var body = box.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
            }
            extra.Add(box);
        }
        // Two boxes in the particles' path, the far one first in the hierarchy; and particles
        // inside a box near its bottom face moving down (out through the face they are nearest).
        {
            var at = Next();
            controls.Add(("collide/world2d/two", CollideEmitter("collide/world2d/two", at + Vector3.up, 4, 3f, TwoD), at));
            var far = Collide2D<BoxCollider2D>(at + new Vector3(0f, -1.2f, 0f), Quaternion.Euler(0f, 0f, 10f));
            far.GetComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            var near = Collide2D<BoxCollider2D>(at + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(0f, 0f, -10f));
            near.GetComponent<BoxCollider2D>().size = new Vector2(1.2f, 0.2f);
            extra.Add(far);
            extra.Add(near);
        }
        {
            var at = Next();
            controls.Add(("collide/world2d/inside_exit", CollideEmitter("collide/world2d/inside_exit", at, 3, 1f, s =>
            {
                var m = s.main; m.startSize = 0.2f;
                var c = s.collision; c.bounce = 0.5f;
                TwoD(s);
            }), at));
            var box = Collide2D<BoxCollider2D>(at + new Vector3(0f, 0.95f, 0f), Quaternion.identity);
            box.GetComponent<BoxCollider2D>().size = new Vector2(4f, 2f);
            extra.Add(box);
        }
        // A step with depth: the particles move down and away (+z); the events' intersections.
        GameObject depthTarget;
        ParticleSystem depthSystem;
        {
            var at = Next();
            depthSystem = CollideEmitter("collide/world2d/events_z", at + Vector3.up, 4, 3f, s =>
            {
                var c = s.collision; c.sendCollisionMessages = true;
                TwoD(s);
            });
            depthSystem.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            controls.Add(("collide/world2d/events_z", depthSystem, at));
            depthTarget = Collide2D<BoxCollider2D>(at + new Vector3(0f, -0.5f, 0f), Quaternion.identity);
            depthTarget.GetComponent<BoxCollider2D>().size = new Vector2(4f, 0.2f);
            extra.Add(depthTarget);
        }
        // colliderForce on free Dynamic bodies (mass 1, no gravity or damping) with each
        // multiplier, head-on, at a slant and off the body's center; at Medium quality (with the
        // speed multiplier); and a negative force.
        var bodies = new List<(string key, Rigidbody2D body)>();
        void ForceControl(string name, float force, bool angle, bool speed, bool size, float tilt, float offset, ParticleSystemCollisionQuality quality)
        {
            var at = Next();
            var key = "collide/world2d/force_" + name;
            controls.Add((key, CollideEmitter(key, at + Vector3.up, 4, 3f, s =>
            {
                var c = s.collision; c.bounce = 0f; c.lifetimeLoss = 1f; c.colliderForce = force; c.quality = quality;
                c.multiplyColliderForceByCollisionAngle = angle;
                c.multiplyColliderForceByParticleSpeed = speed;
                c.multiplyColliderForceByParticleSize = size;
                TwoD(s);
            }), at));
            var box = Collide2D<BoxCollider2D>(at + new Vector3(offset, -0.8f, 0f), Quaternion.Euler(0f, 0f, tilt));
            box.GetComponent<BoxCollider2D>().size = new Vector2(2f, 0.5f);
            var body = box.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.mass = 1f;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            bodies.Add((key, body));
            extra.Add(box);
        }
        var high = ParticleSystemCollisionQuality.High;
        ForceControl("plain", 2f, false, false, false, 0f, 0f, high);
        ForceControl("angle", 2f, true, false, false, 0f, 0f, high);
        ForceControl("speed", 2f, false, true, false, 0f, 0f, high);
        ForceControl("size", 2f, false, false, true, 0f, 0f, high);
        ForceControl("angle_tilted", 2f, true, false, false, 40f, 0f, high);
        ForceControl("plain_offset", 2f, false, false, false, 0f, 0.9f, high);
        ForceControl("speed_medium", 2f, false, true, false, 0f, 0f, ParticleSystemCollisionQuality.Medium);
        ForceControl("negative", -2f, false, false, false, 0f, 0f, high);
        foreach (var (_, ps, _) in controls) ps.Play();
        for (int frame = 0; frame < 40; frame++)
        {
            yield return null;
            string f = frame.ToString(CultureInfo.InvariantCulture);
            foreach (var (key, ps, origin) in controls) RecordCollide(key + "/" + f, ps, origin);
            RecordCollisionEvents2D("collide/world2d/events/events/" + f, eventSystem, eventTarget);
            RecordCollisionEvents2D("collide/world2d/events_z/events/" + f, depthSystem, depthTarget);
            RecordCollisionEvents2D("collide/world2d/kinematic/events/body/" + f, kinematicSystem, kinematicBody);
            RecordCollisionEvents2D("collide/world2d/kinematic/events/box/" + f, kinematicSystem, kinematicBox);
            // The bodies' velocities, not their positions (how many physics steps ran since a hit
            // may differ between runs; without damping the velocities do not).
            foreach (var (key, body) in bodies)
            {
                var v = body.linearVelocity;
                Record(key + "/body/" + f, new List<float> { v.x, v.y, body.angularVelocity });
            }
        }
        foreach (var (_, ps, _) in controls) Done(ps);
        foreach (var go in extra) DestroyImmediate(go);
        yield return Collide2DBaumgarte();
        Time.captureDeltaTime = 0f;
    }

    /// <summary>Particles starting inside a BoxCollider2D (as <c>collide/world2d/inside</c>) while
    /// Physics2D.baumgarteScale is 0.4 (<c>inside_baumgarte</c>), then while
    /// Physics2D.maxLinearCorrection is 0.01 (<c>inside_correction</c>), then, with
    /// Physics2D.defaultContactOffset 0.05, inside a box made then (<c>inside_offset</c>) and over a
    /// sloped box (<c>slope_offset</c>, as <c>slope</c> without its other settings).</summary>
    IEnumerator Collide2DBaumgarte()
    {
        float baumgarte = Physics2D.baumgarteScale, correction = Physics2D.maxLinearCorrection;
        float offset = Physics2D.defaultContactOffset;
        foreach (var (name, scale, limit, contactOffset) in new[]
        {
            ("inside_baumgarte", 0.4f, correction, offset), ("inside_correction", baumgarte, 0.01f, offset),
            ("inside_offset", baumgarte, correction, 0.05f), ("slope_offset", baumgarte, correction, 0.05f),
        })
        {
            Physics2D.baumgarteScale = scale;
            Physics2D.maxLinearCorrection = limit;
            Physics2D.defaultContactOffset = contactOffset;
            var at = s_CollideOrigin + new Vector3(0f, 10f, 40f);
            var key = "collide/world2d/" + name;
            bool slope = name == "slope_offset";
            var ps = CollideEmitter(key, slope ? at + Vector3.up : at, slope ? 6 : 3, slope ? 3f : 1f, s =>
            {
                var m = s.main; m.startSize = slope ? 0.3f : 0.2f;
                var c = s.collision; c.bounce = 0.5f; c.mode = ParticleSystemCollisionMode.Collision2D;
            });
            var box = Collide2D<BoxCollider2D>(slope ? at + new Vector3(0f, -0.5f, 0f) : at, Quaternion.Euler(0f, 0f, slope ? 25f : 0f));
            box.GetComponent<BoxCollider2D>().size = slope ? new Vector2(4f, 0.2f) : new Vector2(2f, 2f);
            yield return null;
            ps.Play();
            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
                RecordCollide(key + "/" + frame.ToString(CultureInfo.InvariantCulture), ps, at);
            }
            Done(ps);
            DestroyImmediate(box);
        }
        Physics2D.baumgarteScale = baumgarte;
        Physics2D.maxLinearCorrection = correction;
        Physics2D.defaultContactOffset = offset;
    }
}
