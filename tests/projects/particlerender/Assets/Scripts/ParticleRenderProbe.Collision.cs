using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Collision module contacts and bounces (<c>collide/*</c>), 0.02 s frames, every particle by seed
/// each frame: position relative to the control's origin, stored and total velocity, remaining
/// lifetime.
/// <list type="bullet">
/// <item><c>world/*</c>: World collisions with a sloped box (size over lifetime, radius scale,
/// bounce, dampen, lifetime loss, gravity), a sphere, a plane under particles with an animated
/// velocity and a speed modifier, particles starting inside a box, and the sloped box at Medium and
/// Low quality, uniformly and non-uniformly scaled Local systems, kill speeds with a speed modifier, and
/// the collision events of particles starting inside a box.</item>
/// <item><c>planes/*</c>: Planes collisions of particles starting behind a plane and moving towards or
/// away from it, two planes reached in one step (list order), a sloped plane, and scaled Local
/// systems.</item>
/// <item><c>trigger/size</c>: a Trigger module killing fast-shrinking particles on Enter.</item>
/// </list>
/// </summary>
public partial class ParticleRenderProbe
{
    static readonly Vector3 s_CollideOrigin = new Vector3(0f, 0f, 60f);
    static ParticleSystem s_EventSystem;
    static readonly List<ParticleCollisionEvent> s_CollisionEvents = new List<ParticleCollisionEvent>();

    /// <summary>The collision events of the frame on every box hit: count, then per event the
    /// intersection relative to the control's origin, the normal and the velocity.</summary>
    static void RecordCollisionEvents(string key, ParticleSystem ps)
    {
        var v = new List<float>();
        foreach (var box in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            if (box.gameObject.name != "collide box" || (box.transform.position - ps.transform.position).sqrMagnitude > 1e-6f) continue;
            int n = ps.GetCollisionEvents(box.gameObject, s_CollisionEvents);
            v.Add(n);
            for (int i = 0; i < n; i++)
            {
                var e = s_CollisionEvents[i];
                var at = e.intersection - ps.transform.position;
                v.Add(at.x); v.Add(at.y); v.Add(at.z);
                v.Add(e.normal.x); v.Add(e.normal.y); v.Add(e.normal.z);
                v.Add(e.velocity.x); v.Add(e.velocity.y); v.Add(e.velocity.z);
            }
        }
        Record(key, v);
    }

    /// <summary>count, then per particle (by seed): seed low half, position relative to
    /// <see cref="s_CollideOrigin"/>, velocity, total velocity, remaining lifetime.</summary>
    static void RecordCollide(string key, ParticleSystem ps, Vector3 origin)
    {
        if (s_Buffer.Length < ps.particleCount) s_Buffer = new ParticleSystem.Particle[ps.particleCount * 2];
        int n = ps.GetParticles(s_Buffer);
        s_Rows.Clear();
        bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            var position = local ? p.position : p.position - origin;
            var total = p.totalVelocity;
            s_Rows.Add(new[] { p.randomSeed & 0xffff, position.x, position.y, position.z, p.velocity.x, p.velocity.y, p.velocity.z,
                total.x, total.y, total.z, p.remainingLifetime });
        }
        s_Rows.Sort((a, b) => a[0].CompareTo(b[0]));
        var v = new List<float> { n };
        foreach (var r in s_Rows) v.AddRange(r);
        Record(key, v);
    }

    /// <summary>A world-space emitter at <paramref name="at"/> shooting a burst of particles down
    /// (-Y) from a small box, 2 s lifetime.</summary>
    static ParticleSystem CollideEmitter(string key, Vector3 at, int count, float speed, Action<ParticleSystem> configure)
    {
        var ps = Make(key, s =>
        {
            var m = s.main; m.startLifetime = 2f; m.startSpeed = speed; m.startSize = 0.3f; m.maxParticles = 64;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var shape = s.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.8f, 0.8f, 0f);
            var e = s.emission; e.enabled = true; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D; c.quality = ParticleSystemCollisionQuality.High;
            c.bounce = 0.6f; c.dampen = 0f; c.lifetimeLoss = 0f; c.sendCollisionMessages = false;
            configure?.Invoke(s);
        });
        ps.transform.position = at;
        ps.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        return ps;
    }

    static GameObject CollideBox(Vector3 at, Vector3 size, Quaternion rotation)
    {
        var box = new GameObject("collide box").AddComponent<BoxCollider>();
        box.size = size;
        box.transform.position = at;
        box.transform.rotation = rotation;
        return box.gameObject;
    }

    static Transform CollidePlane(Vector3 at, Quaternion rotation)
    {
        var plane = new GameObject("collide plane").transform;
        plane.position = at;
        plane.rotation = rotation;
        return plane;
    }

    /// <summary>The sloped box control: size over lifetime (the collision radius follows the size),
    /// radius scale, bounce, dampen, lifetime loss and gravity.</summary>
    static void SlopeSettings(ParticleSystem s, ParticleSystemCollisionQuality quality)
    {
        var m = s.main; m.gravityModifier = 0.5f;
        var sz = s.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
        var c = s.collision; c.radiusScale = 0.8f; c.bounce = 0.6f; c.dampen = 0.2f; c.lifetimeLoss = 0.05f; c.quality = quality;
    }

    IEnumerator CollideControls()
    {
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var controls = new List<(string key, ParticleSystem ps, Vector3 origin)>();
        var extra = new List<GameObject>();
        int slot = 0;
        Vector3 Next() => s_CollideOrigin + new Vector3(8f * slot++, 0f, 0f);

        foreach (var (name, quality) in new[] { ("slope", ParticleSystemCollisionQuality.High), ("slope_medium", ParticleSystemCollisionQuality.Medium),
                     ("slope_low", ParticleSystemCollisionQuality.Low) })
        {
            var at = Next();
            controls.Add(("collide/world/" + name, CollideEmitter("collide/world/" + name, at + Vector3.up, 6, 3f, s => SlopeSettings(s, quality)), at));
            extra.Add(CollideBox(at + new Vector3(0f, -0.5f, 0f), new Vector3(4f, 0.2f, 4f), Quaternion.Euler(0f, 0f, 25f)));
        }
        {
            var at = Next();
            controls.Add(("collide/world/sphere", CollideEmitter("collide/world/sphere", at + Vector3.up, 6, 3f, s =>
            {
                var m = s.main; m.startSize = 0.4f;
                var c = s.collision; c.bounce = 0.9f;
            }), at));
            var sphere = new GameObject("collide sphere").AddComponent<SphereCollider>();
            sphere.radius = 0.6f;
            sphere.transform.position = at + new Vector3(0.1f, -0.4f, 0.05f);
            extra.Add(sphere.gameObject);
        }
        {
            var at = Next();
            controls.Add(("collide/world/animated", CollideEmitter("collide/world/animated", at + Vector3.up, 4, 2.5f, s =>
            {
                var vel = s.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = 1.5f; vel.y = 0f; vel.z = 0f; vel.speedModifier = 0.6f;
                var c = s.collision; c.bounce = 0.5f; c.dampen = 0.1f;
            }), at));
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = at + new Vector3(0f, -0.3f, 0f);
            extra.Add(plane);
        }
        {
            var at = Next();
            controls.Add(("collide/world/inside", CollideEmitter("collide/world/inside", at, 3, 1f, s =>
            {
                var m = s.main; m.startSize = 0.2f;
                var c = s.collision; c.bounce = 0.5f;
            }), at));
            extra.Add(CollideBox(at, new Vector3(2f, 2f, 2f), Quaternion.identity));
        }
        foreach (var (name, speed) in new[] { ("behind_up", -2f), ("behind_down", 2f) })
        {
            var at = Next();
            var plane = CollidePlane(at, Quaternion.identity);
            extra.Add(plane.gameObject);
            controls.Add(("collide/planes/" + name, CollideEmitter("collide/planes/" + name, at + new Vector3(0f, -1f, 0f), 3, speed, s =>
            {
                var c = s.collision; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane);
            }), at));
        }
        {
            var at = Next();
            var first = CollidePlane(at + new Vector3(0f, -0.6f, 0f), Quaternion.identity);
            var second = CollidePlane(at + new Vector3(0f, -0.55f, 0f), Quaternion.Euler(0f, 0f, 20f));
            extra.Add(first.gameObject); extra.Add(second.gameObject);
            controls.Add(("collide/planes/order", CollideEmitter("collide/planes/order", at + Vector3.up, 3, 10f, s =>
            {
                var m = s.main; m.startSize = 0.1f;
                var c = s.collision; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(first); c.AddPlane(second);
            }), at));
        }
        {
            var at = Next();
            var plane = CollidePlane(at + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(30f, 0f, 0f));
            extra.Add(plane.gameObject);
            controls.Add(("collide/planes/slope", CollideEmitter("collide/planes/slope", at + Vector3.up, 6, 3f, s =>
            {
                SlopeSettings(s, ParticleSystemCollisionQuality.High);
                var c = s.collision; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane); c.radiusScale = 1.2f; c.dampen = 0.3f; c.bounce = 0.4f;
            }), at));
        }
        foreach (var (name, scale) in new[] { ("scaled", new Vector3(2f, 2f, 2f)), ("scaled_y", new Vector3(1f, 2.5f, 1f)) })
        {
            {
                var at = Next();
                var plane = CollidePlane(at + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(20f, 0f, 0f));
                extra.Add(plane.gameObject);
                var ps = CollideEmitter("collide/planes/" + name, at + Vector3.up, 6, 3f, s =>
                {
                    var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var c = s.collision; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane);
                });
                ps.transform.localScale = scale;
                controls.Add(("collide/planes/" + name, ps, at));
            }
            {
                var at = Next();
                var ps = CollideEmitter("collide/world/" + name, at + Vector3.up, 6, 3f, s =>
                {
                    var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.Local;
                });
                ps.transform.localScale = scale;
                controls.Add(("collide/world/" + name, ps, at));
                extra.Add(CollideBox(at + new Vector3(0f, -0.5f, 0f), new Vector3(4f, 0.2f, 4f), Quaternion.Euler(0f, 0f, 25f)));
            }
        }
        {
            // The kill speeds against the speed modifier: arriving at 4 with a modifier of 0.5.
            var at = Next();
            controls.Add(("collide/world/killspeed", CollideEmitter("collide/world/killspeed", at + Vector3.up, 4, 4f, s =>
            {
                var vel = s.velocityOverLifetime; vel.enabled = true; vel.speedModifier = 0.5f;
                var c = s.collision; c.minKillSpeed = 3f;
            }), at));
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = at + new Vector3(0f, -0.3f, 0f);
            extra.Add(plane);
        }
        {
            // A trigger killing on Enter, particles shrinking fast: when they are killed shows
            // which size the trigger sees.
            var at = Next();
            var sphere = new GameObject("collide trigger").AddComponent<SphereCollider>();
            sphere.isTrigger = true; sphere.radius = 0.3f;
            sphere.transform.position = at + new Vector3(0f, -0.6f, 0f);
            extra.Add(sphere.gameObject);
            controls.Add(("collide/trigger/size", CollideEmitter("collide/trigger/size", at + Vector3.up, 6, 3f, s =>
            {
                var m = s.main; m.startSize = 1f; m.startLifetime = 0.8f;
                var sz = s.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                var c = s.collision; c.enabled = false;
                var t = s.trigger; t.enabled = true; t.AddCollider(sphere);
                t.enter = ParticleSystemOverlapAction.Kill; t.inside = ParticleSystemOverlapAction.Ignore;
                t.exit = ParticleSystemOverlapAction.Ignore; t.outside = ParticleSystemOverlapAction.Ignore;
            }), at));
        }
        {
            // A particle starting inside a collider, with collision events: the event's normal.
            var at = Next();
            var ps = CollideEmitter("collide/events/inside", at, 3, 1f, s =>
            {
                var m = s.main; m.startSize = 0.2f;
                var c = s.collision; c.bounce = 0.5f; c.sendCollisionMessages = true;
            });
            controls.Add(("collide/events/inside", ps, at));
            extra.Add(CollideBox(at, new Vector3(2f, 2f, 2f), Quaternion.identity));
            s_EventSystem = ps;
        }
        foreach (var (_, ps, _) in controls) ps.Play();
        for (int frame = 0; frame < 40; frame++)
        {
            yield return null;
            foreach (var (key, ps, origin) in controls) RecordCollide(key + "/" + frame.ToString(CultureInfo.InvariantCulture), ps, origin);
            RecordCollisionEvents("collide/events/inside/events/" + frame.ToString(CultureInfo.InvariantCulture), s_EventSystem);
        }
        foreach (var (_, ps, _) in controls) Done(ps);
        foreach (var go in extra) DestroyImmediate(go);
        Time.captureDeltaTime = 0f;
    }
}
