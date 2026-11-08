using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Controls recorded after the original sections:
/// <list type="bullet">
/// <item><c>birthstart/*</c>: when a sub-emitter child starts playing and when a Birth child's
/// first particles come, per frame (state and sorted particle ages): a played parent at two frame
/// times, a child outside the parent's hierarchy, a delayed parent, a parent without particles of
/// its own (then emitted into), child bursts, a stopped parent, a Play On Awake and a prewarmed
/// parent, a parent driven by Simulate, and Death/Collision/Trigger/Manual children.</item>
/// <item><c>freenorm/*</c>: one freeform stretched particle per system: the particle, and the
/// positions, normals and tangents BakeMesh gives from the front and side cameras, for stretch
/// lengths, directions, rotations, sizes, a pivot and transformed emitters.</item>
/// <item><c>meshscale/*</c>: mesh particles of a rotated, non-uniformly scaled emitter under a
/// scaled parent for each scaling mode, simulation space and render alignment.</item>
/// <item><c>meshindex/*</c>: BakeMesh and BakeTexture of mesh particles whose meshes or bakes are
/// below and above 65535 vertices, with 16- and 32-bit index buffers.</item>
/// </list>
/// </summary>
public partial class ParticleRenderProbe
{
    // ------------------------------------------------------------------ Birth sub-emitter start

    static readonly List<float> s_Ages = new List<float>();

    /// <summary>count, time, isPlaying, isEmitting, isPaused, then the particles' ages (start
    /// lifetime less remaining lifetime), sorted.</summary>
    static void RecordAges(string key, ParticleSystem ps)
    {
        if (s_Buffer.Length < ps.particleCount) s_Buffer = new ParticleSystem.Particle[ps.particleCount * 2];
        int n = ps.GetParticles(s_Buffer);
        s_Ages.Clear();
        for (int i = 0; i < n; i++) s_Ages.Add(s_Buffer[i].startLifetime - s_Buffer[i].remainingLifetime);
        s_Ages.Sort();
        var v = new List<float> { ps.particleCount, ps.time, ps.isPlaying ? 1 : 0, ps.isEmitting ? 1 : 0, ps.isPaused ? 1 : 0 };
        v.AddRange(s_Ages);
        Record(key, v);
    }

    /// <summary>A looping parent: a burst of two at once, then 3/s, 0.7 s lifetimes, always
    /// simulated.</summary>
    static ParticleSystem BirthParent(string key, Vector3 position, Action<ParticleSystem> configure = null)
    {
        var ps = Make(key + "/parent", s =>
        {
            var m = s.main; m.loop = true; m.duration = 1f; m.startLifetime = 0.7f; m.startSpeed = 0f;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var e = s.emission; e.enabled = true; e.rateOverTime = 3f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            configure?.Invoke(s);
        });
        ps.transform.position = position;
        return ps;
    }

    /// <summary>A world-space child at 7/s with 3 s lifetimes, linked to <paramref name="parent"/>
    /// as a sub-emitter of <paramref name="type"/>; under the parent's transform or a root.</summary>
    static ParticleSystem BirthChild(string key, ParticleSystem parent, bool underParent = true,
        Action<ParticleSystem> configure = null, ParticleSystemSubEmitterType type = ParticleSystemSubEmitterType.Birth)
    {
        var child = Make(key, s =>
        {
            var m = s.main; m.startLifetime = 3f; m.startSpeed = 0f; m.maxParticles = 256;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = s.emission; e.enabled = true; e.rateOverTime = 7f;
            configure?.Invoke(s);
        });
        if (underParent) child.transform.SetParent(parent.transform, false);
        else child.transform.position = parent.transform.position;
        var sub = parent.subEmitters;
        sub.enabled = true;
        sub.AddSubEmitter(child, type, ParticleSystemSubEmitterProperties.InheritNothing);
        return child;
    }

    /// <summary>Records every system of <paramref name="systems"/> under its key, with a suffix.</summary>
    static void RecordAll(List<(string, ParticleSystem)> systems, string suffix)
    {
        foreach (var (key, ps) in systems) RecordAges(key + "/" + suffix, ps);
    }

    IEnumerator BirthStart()
    {
        // Frames of 0.02 s (one internal step each).
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var systems = new List<(string, ParticleSystem)>();
        var played = new List<ParticleSystem>();
        var simulated = new List<ParticleSystem>();
        void Pair(string name, Vector3 at, Action<ParticleSystem> parentConfig = null, Action<ParticleSystem> childConfig = null,
            bool underParent = true, bool play = true)
        {
            var parent = BirthParent("birthstart/" + name, at, parentConfig);
            var child = BirthChild("birthstart/" + name + "/child", parent, underParent, childConfig);
            systems.Add(("birthstart/" + name + "/parent", parent));
            systems.Add(("birthstart/" + name + "/child", child));
            (play ? played : simulated).Add(parent);
        }
        var origin = new Vector3(0f, 100f, 0f);
        Pair("play02", origin);
        Pair("root02", origin + new Vector3(2f, 0f, 0f), underParent: false);
        Pair("delay02", origin + new Vector3(4f, 0f, 0f), s => { var m = s.main; m.startDelay = 0.1f; });
        Pair("noemit02", origin + new Vector3(6f, 0f, 0f), s => { var e = s.emission; e.enabled = false; });
        Pair("burstchild02", origin + new Vector3(8f, 0f, 0f), null, s =>
        {
            var e = s.emission; e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2), new ParticleSystem.Burst(0.1f, 1) });
        });
        Pair("stop02", origin + new Vector3(10f, 0f, 0f));
        Pair("simulate02", origin + new Vector3(12f, 0f, 0f), play: false);
        ParticleSystem noemit = systems[6].Item2, stop = systems[10].Item2;

        // Death, Collision, Trigger and Manual children of one parent whose two particles fall
        // through a trigger sphere onto a plane and die at 0.5 s; the Manual slot is triggered at
        // frame 15.
        var eventsAt = origin + new Vector3(30f, 0f, 0f);
        var events = Make("birthstart/events02/parent", s =>
        {
            var m = s.main; m.loop = true; m.duration = 1f; m.startLifetime = 0.5f; m.startSpeed = 2f;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var e = s.emission; e.enabled = true; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
        });
        events.transform.SetPositionAndRotation(eventsAt, Quaternion.Euler(90f, 0f, 0f));
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.transform.position = eventsAt + new Vector3(0f, -0.41f, 0f);
        var sphere = new GameObject("birthstart trigger").AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 0.05f;
        sphere.transform.position = eventsAt + new Vector3(0f, -0.21f, 0f);
        var collision = events.collision;
        collision.enabled = true; collision.type = ParticleSystemCollisionType.World; collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.bounce = 0.5f; collision.lifetimeLoss = 0f; collision.sendCollisionMessages = false;
        var trigger = events.trigger;
        trigger.enabled = true; trigger.AddCollider(sphere);
        trigger.enter = ParticleSystemOverlapAction.Callback; trigger.inside = ParticleSystemOverlapAction.Ignore;
        trigger.exit = ParticleSystemOverlapAction.Ignore; trigger.outside = ParticleSystemOverlapAction.Ignore;
        systems.Add(("birthstart/events02/parent", events));
        foreach (var type in new[] { ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterType.Collision,
                     ParticleSystemSubEmitterType.Trigger, ParticleSystemSubEmitterType.Manual })
        {
            var child = BirthChild("birthstart/events02/" + type, events, true, s =>
            {
                var e = s.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            }, type);
            systems.Add(("birthstart/events02/" + type, child));
        }
        played.Add(events);

        // One sub-emitter type per parent (no collision or trigger happens), and Death children
        // of parents with a Collision or Trigger module, a looping Death child, a Death child
        // beside a Birth one and a Collision child without a Collision module.
        int typeIndex = 0;
        ParticleSystem TypeParent(string name, Action<ParticleSystem> configure = null)
        {
            var parent = Make("birthstart/types02/" + name + "/parent", s =>
            {
                var m = s.main; m.loop = true; m.duration = 1f; m.startLifetime = 0.5f; m.startSpeed = 2f;
                m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var e = s.emission; e.enabled = true; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
                configure?.Invoke(s);
            });
            parent.transform.SetPositionAndRotation(origin + new Vector3(60f + 3f * typeIndex++, 0f, 0f), Quaternion.Euler(90f, 0f, 0f));
            systems.Add(("birthstart/types02/" + name + "/parent", parent));
            played.Add(parent);
            return parent;
        }
        void TypeChild(string name, ParticleSystem parent, ParticleSystemSubEmitterType type, bool loop = false)
        {
            var child = BirthChild("birthstart/types02/" + name + "/" + type, parent, true, s =>
            {
                var m = s.main; m.loop = loop; m.duration = loop ? 5f : 1f;
                if (type == ParticleSystemSubEmitterType.Birth) return;
                var e = s.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            }, type);
            systems.Add(("birthstart/types02/" + name + "/" + type, child));
        }
        var manualParents = new List<ParticleSystem>();
        foreach (var type in new[] { ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterType.Collision,
                     ParticleSystemSubEmitterType.Trigger, ParticleSystemSubEmitterType.Manual })
        {
            var parent = TypeParent(type.ToString());
            TypeChild(type.ToString(), parent, type);
            if (type == ParticleSystemSubEmitterType.Manual) manualParents.Add(parent);
        }
        TypeChild("Death_collision", TypeParent("Death_collision", s =>
        {
            var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
        }), ParticleSystemSubEmitterType.Death);
        TypeChild("Death_trigger", TypeParent("Death_trigger", s =>
        {
            var t = s.trigger; t.enabled = true; t.AddCollider(sphere);
            t.inside = ParticleSystemOverlapAction.Ignore; t.outside = ParticleSystemOverlapAction.Ignore;
        }), ParticleSystemSubEmitterType.Death);
        TypeChild("Death_loop", TypeParent("Death_loop"), ParticleSystemSubEmitterType.Death, loop: true);
        var both = TypeParent("Death_Birth");
        TypeChild("Death_Birth", both, ParticleSystemSubEmitterType.Death);
        TypeChild("Death_Birth", both, ParticleSystemSubEmitterType.Birth);
        TypeChild("Collision_nomodule", TypeParent("Collision_nomodule"), ParticleSystemSubEmitterType.Collision);
        // A Birth child made before its parent.
        {
            var child = Make("birthstart/order02/child", s =>
            {
                var m = s.main; m.startLifetime = 3f; m.startSpeed = 0f; m.maxParticles = 256;
                m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                var e = s.emission; e.enabled = true; e.rateOverTime = 7f;
            });
            var parent = BirthParent("birthstart/order02", origin + new Vector3(14f, 0f, 0f));
            child.transform.position = parent.transform.position;
            var sub = parent.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
            systems.Add(("birthstart/order02/parent", parent));
            systems.Add(("birthstart/order02/child", child));
            played.Add(parent);
        }

        RecordAll(systems, "created");
        foreach (var ps in played) ps.Play();
        RecordAll(systems, "start");
        var late = MakeLateCamera("BirthStartCamera", origin + new Vector3(0f, 0f, -500f));
        int frame = 0;
        late.Late = () => { RecordAll(systems, frame.ToString()); frame++; };
        for (int i = 0; i < 30; i++)
        {
            if (i == 10) noemit.Emit(1);
            if (i == 8) stop.Stop();
            if (i == 15)
            {
                events.TriggerSubEmitter(3);
                foreach (var ps in manualParents) ps.TriggerSubEmitter(0);
            }
            foreach (var ps in simulated) ps.Simulate(0.02f, true, i == 0, false);
            yield return null;
        }
        late.Late = null;
        foreach (var (key, ps) in systems) if (key.EndsWith("/parent")) Done(ps);
        foreach (var (key, ps) in systems) if (ps != null) Done(ps);
        DestroyImmediate(plane);
        DestroyImmediate(sphere.gameObject);

        // Frames of 0.05 s (two internal steps): a played parent, Play On Awake, prewarm, Simulate.
        Time.captureDeltaTime = 0.05f;
        yield return null;
        systems.Clear(); played.Clear(); simulated.Clear();
        Pair("play05", origin);
        Pair("prewarm05", origin + new Vector3(2f, 0f, 0f), s => { var m = s.main; m.prewarm = true; });
        Pair("simulate05", origin + new Vector3(4f, 0f, 0f), play: false);
        // Parents dying with a Birth due exactly at the end of their life (10/s, 0.5 s), inside
        // their last step (7/s, 0.574 s) and one step before it (7/s, 0.58 s).
        Pair("deathtie05", origin + new Vector3(8f, 0f, 0f), s => { var m = s.main; m.startLifetime = 0.5f; },
            s => { var e = s.emission; e.rateOverTime = 10f; });
        Pair("deathin05", origin + new Vector3(10f, 0f, 0f), s => { var m = s.main; m.startLifetime = 0.574f; });
        Pair("deathafter05", origin + new Vector3(12f, 0f, 0f), s => { var m = s.main; m.startLifetime = 0.58f; });
        // Play On Awake: the parent plays when its object becomes active, before the link is made.
        var awake = BirthParent("birthstart/awake05", origin + new Vector3(6f, 0f, 0f), s => { var m = s.main; m.playOnAwake = true; });
        var awakeChild = BirthChild("birthstart/awake05/child", awake);
        systems.Add(("birthstart/awake05/parent", awake));
        systems.Add(("birthstart/awake05/child", awakeChild));
        RecordAll(systems, "created");
        foreach (var ps in played) ps.Play();
        RecordAll(systems, "start");
        frame = 0;
        late.Late = () => { RecordAll(systems, frame.ToString()); frame++; };
        for (int i = 0; i < 24; i++)
        {
            foreach (var ps in simulated) ps.Simulate(0.05f, true, i == 0, false);
            yield return null;
        }
        late.Late = null;
        foreach (var (key, ps) in systems) if (key.EndsWith("/parent")) Done(ps);
        DestroyLateCamera(late);
        Time.captureDeltaTime = 0f;
    }

    // ------------------------------------------------------------------ freeform normals

    static readonly List<Vector4> s_Tangents = new List<Vector4>();

    /// <summary>The particles (position, velocity, rotation, size), then BakeMesh positions and
    /// normals with the default streams, and normals and tangents with a Tangent stream.</summary>
    static void RecordFreeform(string key, ParticleSystem ps, Camera camera)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        var streams = new List<ParticleSystemVertexStream>();
        r.GetActiveVertexStreams(streams);
        var mesh = new Mesh();
        r.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
        var v = new List<float> { mesh.vertexCount };
        foreach (var p in mesh.vertices) { v.Add(p.x); v.Add(p.y); v.Add(p.z); }
        Record(key + "/positions", v);
        var n = new List<float> { mesh.vertexCount };
        foreach (var p in mesh.normals) { n.Add(p.x); n.Add(p.y); n.Add(p.z); }
        Record(key + "/normals", n);
        r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Tangent,
            ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        });
        r.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
        var t = new List<float> { mesh.vertexCount };
        foreach (var p in mesh.normals) { t.Add(p.x); t.Add(p.y); t.Add(p.z); }
        s_Tangents.Clear();
        mesh.GetTangents(s_Tangents);
        t.Add(s_Tangents.Count);
        foreach (var p in s_Tangents) { t.Add(p.x); t.Add(p.y); t.Add(p.z); t.Add(p.w); }
        Record(key + "/tangents", t);
        r.SetActiveVertexStreams(streams);
        DestroyImmediate(mesh);
    }

    static void RecordParticles(string key, ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { n };
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            var s = p.startSize3D;
            v.Add(p.position.x); v.Add(p.position.y); v.Add(p.position.z);
            v.Add(p.velocity.x); v.Add(p.velocity.y); v.Add(p.velocity.z);
            v.Add(p.rotation); v.Add(s.x); v.Add(s.y); v.Add(s.z);
        }
        Record(key, v);
    }

    static readonly (string, Vector3, Vector3, float, float, bool, Vector3?)[] s_FreeNormCases =
    {
        // name, position, velocity, rotation, lengthScale, rotateWithStretchDirection, size3D
        ("base", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0f, 2f, true, null),
        ("length1", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0f, 1f, true, null),
        ("length1.5", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0f, 1.5f, true, null),
        ("length3", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0f, 3f, true, null),
        ("length0.5", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0f, 0.5f, true, null),
        ("rot30", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 30f, 2f, true, null),
        ("rot30_norot", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 30f, 2f, false, null),
        ("rot90_norot", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 90f, 2f, false, null),
        ("vy", new Vector3(0.3f, -0.2f, 0f), new Vector3(0f, 2f, 0f), 0f, 2f, true, null),
        ("vy_norot", new Vector3(0.3f, -0.2f, 0f), new Vector3(0f, 2f, 0f), 0f, 2f, false, null),
        ("vdiag", new Vector3(0.3f, -0.2f, 0f), new Vector3(1.5f, 1.5f, 0f), 0f, 2f, true, null),
        ("vdepth", new Vector3(0.3f, -0.2f, 0f), new Vector3(0.3f, 0.2f, 3f), 0f, 2f, true, null),
        ("vtoward", new Vector3(0.3f, -0.2f, 0f), new Vector3(0.5f, 0f, -2f), 0f, 2f, true, null),
        ("centre", new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), 0f, 2f, true, null),
        ("offaxis", new Vector3(3f, 2f, 0f), new Vector3(2f, 0f, 0f), 0f, 2f, true, null),
        ("size3d", new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0f, 2f, true, new Vector3(0.2f, 0.6f, 1f)),
        ("size3d_vy", new Vector3(0.3f, -0.2f, 0f), new Vector3(0f, 2f, 0f), 0f, 2f, true, new Vector3(0.2f, 0.6f, 1f)),
    };

    static readonly (string, Quaternion, Vector3, ParticleSystemSimulationSpace, ParticleSystemScalingMode)[] s_FreeNormTransforms =
    {
        ("rot", Quaternion.Euler(10f, 20f, 30f), Vector3.one, ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Local),
        ("uscale", Quaternion.identity, new Vector3(2f, 2f, 2f), ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Local),
        ("scalex", Quaternion.identity, new Vector3(2f, 1f, 1f), ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Local),
        ("scaley", Quaternion.identity, new Vector3(1f, 2f, 1f), ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Local),
        ("scalez", Quaternion.identity, new Vector3(1f, 1f, 2f), ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Local),
        ("both", Quaternion.Euler(10f, 20f, 30f), new Vector3(1.5f, 0.5f, 2f), ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Local),
        ("both_world", Quaternion.Euler(10f, 20f, 30f), new Vector3(1.5f, 0.5f, 2f), ParticleSystemSimulationSpace.World, ParticleSystemScalingMode.Local),
        ("both_shape", Quaternion.Euler(10f, 20f, 30f), new Vector3(1.5f, 0.5f, 2f), ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Shape),
        ("both_hierarchy", Quaternion.Euler(10f, 20f, 30f), new Vector3(1.5f, 0.5f, 2f), ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Hierarchy),
    };

    static ParticleSystem MakeFreeform(string key, float lengthScale, bool rotateWith, bool size3D, Action<ParticleSystem> system = null)
    {
        var ps = Make(key, s =>
        {
            if (size3D) { var m = s.main; m.startSize3D = true; }
            system?.Invoke(s);
        });
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.freeformStretching = true;
        r.rotateWithStretchDirection = rotateWith;
        r.lengthScale = lengthScale;
        r.velocityScale = 0f;
        r.cameraVelocityScale = 0f;
        return ps;
    }

    static void FreeformNormals()
    {
        foreach (var (name, position, velocity, rotation, length, rotateWith, size3D) in s_FreeNormCases)
        {
            string key = "freenorm/" + name;
            var ps = MakeFreeform(key, length, rotateWith, size3D.HasValue);
            Emit(ps, position, velocity, 0.4f, rotation, 5f, size3D);
            ps.Simulate(0.1f, true, false, false);
            RecordParticles(key + "/particles", ps);
            RecordFreeform(key + "/front", ps, s_BakeCamera);
            RecordFreeform(key + "/side", ps, s_SideCamera);
            // Without freeform stretching, as a control for the tangents.
            if (name == "base" || name == "vdepth" || name == "rot30")
            {
                ps.GetComponent<ParticleSystemRenderer>().freeformStretching = false;
                RecordFreeform(key + "/plain/front", ps, s_BakeCamera);
            }
            Done(ps);
        }
        // A pivot.
        {
            var ps = MakeFreeform("freenorm/pivot", 2f, true, false);
            ps.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0.5f, 0.25f, 0.3f);
            Emit(ps, new Vector3(0.3f, -0.2f, 0f), new Vector3(2f, 0f, 0f), 0.4f, 0f);
            ps.Simulate(0.1f, true, false, false);
            RecordParticles("freenorm/pivot/particles", ps);
            RecordFreeform("freenorm/pivot/front", ps, s_BakeCamera);
            Done(ps);
        }
        // Transformed emitters (under a parent scaled (1, 1.5, 1), which only Hierarchy sees),
        // two particles each.
        var holder = new GameObject("freenorm parent");
        holder.transform.localScale = new Vector3(1f, 1.5f, 1f);
        foreach (var (name, rotation, scale, space, scaling) in s_FreeNormTransforms)
        {
            string key = "freenorm/transform/" + name;
            var ps = MakeFreeform(key, 2f, true, false, s =>
            {
                var m = s.main; m.simulationSpace = space; m.scalingMode = scaling;
            });
            ps.transform.SetParent(holder.transform, false);
            ps.transform.localPosition = new Vector3(0.2f, 0.1f, 0.5f);
            ps.transform.localRotation = rotation;
            ps.transform.localScale = scale;
            Emit(ps, new Vector3(-0.5f, 0.3f, 0f), new Vector3(2f, 0f, 0f), 0.3f, 0f);
            Emit(ps, new Vector3(0.5f, -0.3f, 0f), new Vector3(0.3f, 1f, 0.5f), 0.4f, 30f);
            ps.Simulate(0.1f, true, false, false);
            RecordParticles(key + "/particles", ps);
            RecordFreeform(key + "/front", ps, s_BakeCamera);
            RecordFreeform(key + "/side", ps, s_SideCamera);
            BakeMesh(key + "/front/transform", ps, s_BakeCamera, ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale);
            Done(ps);
        }
        DestroyImmediate(holder);
    }

    // ------------------------------------------------------------------ scaled mesh particles

    /// <summary>A small asymmetric mesh: four triangles of a skewed tetrahedron, with normals and
    /// tangents.</summary>
    static Mesh Tetra()
    {
        var mesh = new Mesh { name = "tetra" };
        mesh.vertices = new[]
        {
            new Vector3(0.5f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1.5f), new Vector3(-0.3f, -0.2f, -0.1f),
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0 };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    static void MeshScale()
    {
        var tetra = Tetra();
        var holder = new GameObject("meshscale parent");
        holder.transform.localScale = new Vector3(1f, 1.5f, 1f);
        foreach (var scaling in new[] { ParticleSystemScalingMode.Hierarchy, ParticleSystemScalingMode.Local, ParticleSystemScalingMode.Shape })
            foreach (var space in new[] { ParticleSystemSimulationSpace.Local, ParticleSystemSimulationSpace.World })
                foreach (var alignment in new[] { ParticleSystemRenderSpace.View, ParticleSystemRenderSpace.World, ParticleSystemRenderSpace.Local })
                {
                    string key = "meshscale/" + scaling + "/" + space + "/" + alignment;
                    var ps = Make(key, s =>
                    {
                        var m = s.main; m.simulationSpace = space; m.scalingMode = scaling; m.startRotation3D = true;
                    });
                    ps.transform.SetParent(holder.transform, false);
                    ps.transform.localPosition = new Vector3(0.3f, 0.2f, 0.1f);
                    ps.transform.localRotation = Quaternion.Euler(20f, 40f, 10f);
                    ps.transform.localScale = new Vector3(2f, 1f, 0.5f);
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    r.renderMode = ParticleSystemRenderMode.Mesh;
                    r.mesh = tetra;
                    r.alignment = alignment;
                    var e = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(-0.4f, 0.2f, 0.1f), velocity = new Vector3(0.5f, 0f, 0f), startLifetime = 5f,
                        rotation3D = new Vector3(15f, 25f, 35f), startSize3D = new Vector3(0.3f, 0.5f, 0.7f),
                        startColor = new Color32(255, 200, 150, 255), randomSeed = 1234u,
                    };
                    ps.Emit(e, 1);
                    e.position = new Vector3(0.5f, -0.3f, 0.2f); e.rotation3D = Vector3.zero; e.startSize3D = new Vector3(0.4f, 0.4f, 0.4f);
                    ps.Emit(e, 1);
                    ps.Simulate(0.1f, true, false, false);
                    BakeMesh(key + "/front", ps, s_BakeCamera);
                    BakeMesh(key + "/front/transform", ps, s_BakeCamera, ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                    Done(ps);
                }
        DestroyImmediate(holder);
        DestroyImmediate(tetra);
    }

    // ------------------------------------------------------------------ index formats of mesh bakes

    /// <summary>A flat grid of side × side vertices, two triangles a cell.</summary>
    static Mesh Grid(int side, IndexFormat format)
    {
        var mesh = new Mesh { name = "grid" + side, indexFormat = format };
        var vertices = new Vector3[side * side];
        var uvs = new Vector2[side * side];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                vertices[y * side + x] = new Vector3(x / (side - 1f) - 0.5f, y / (side - 1f) - 0.5f, 0f);
                uvs[y * side + x] = new Vector2(x / (side - 1f), y / (side - 1f));
            }
        var triangles = new int[(side - 1) * (side - 1) * 6];
        int t = 0;
        for (int y = 0; y < side - 1; y++)
            for (int x = 0; x < side - 1; x++)
            {
                int i = y * side + x;
                triangles[t++] = i; triangles[t++] = i + side; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + side; triangles[t++] = i + side + 1;
            }
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>[count, sum mod 65536, floor(sum / 65536)] then the values at the start, from
    /// count/2 - 4 and at the end (eight each, those that exist).</summary>
    static List<float> Digest(IList<double> values, int count)
    {
        double sum = 0;
        for (int i = 0; i < count; i++) sum += values[i];
        var v = new List<float> { count, (float)(sum % 65536), (float)Math.Floor(sum / 65536) };
        for (int i = 0; i < 8 && i < count; i++) v.Add((float)values[i]);
        for (int i = Math.Max(0, count / 2 - 4); i < count / 2 + 4 && i < count; i++) v.Add((float)values[i]);
        for (int i = Math.Max(0, count - 8); i < count; i++) v.Add((float)values[i]);
        return v;
    }

    static void MeshIndexFormats()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var cube16 = Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        DestroyImmediate(cube);
        var cube32 = Instantiate(cube16);
        cube32.indexFormat = IndexFormat.UInt32;
        cube32.triangles = cube16.triangles;
        var cases = new (string, Mesh, int)[]
        {
            ("cube16", cube16, 4), ("cube32", cube32, 4),
            ("grid141x4", Grid(141, IndexFormat.UInt16), 4), ("grid257", Grid(257, IndexFormat.UInt32), 1),
        };
        var values = new List<double>();
        foreach (var (name, mesh, count) in cases)
        {
            string key = "meshindex/" + name;
            var ps = Make(key, s => { var m = s.main; m.maxParticles = 8; });
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.alignment = ParticleSystemRenderSpace.World;
            for (int i = 0; i < count; i++) Emit(ps, new Vector3(i * 1.2f - 1.8f, 0f, 0f), Vector3.zero, 0.5f);
            ps.Simulate(0.1f, true, false, false);
            Record(key + "/source", new List<float> { mesh.vertexCount, (int)mesh.indexFormat, mesh.GetIndexCount(0) });

            var baked = new Mesh();
            r.BakeMesh(baked, s_BakeCamera, ParticleSystemBakeMeshOptions.Default);
            var indices = baked.triangles;
            values.Clear();
            foreach (var i in indices) values.Add(i);
            var v = new List<float> { baked.vertexCount, (int)baked.indexFormat, baked.subMeshCount };
            v.AddRange(Digest(values, values.Count));
            Record(key + "/bakemesh", v);
            DestroyImmediate(baked);

            foreach (var (option, flags) in new[]
            {
                ("default", ParticleSystemBakeTextureOptions.Default), ("perparticle", ParticleSystemBakeTextureOptions.PerParticle),
            })
            {
                Texture2D vt = null, it = null;
                int n = r.BakeTexture(ref vt, ref it, s_BakeCamera, flags);
                var head = new List<float> { n };
                foreach (var t in new[] { vt, it })
                {
                    if (t == null) { head.Add(0); head.Add(0); head.Add(0); continue; }
                    head.Add(t.width); head.Add(t.height); head.Add((int)t.format);
                }
                values.Clear();
                if (it != null)
                {
                    var pixels = it.GetPixels();
                    for (int i = 0; i < pixels.Length; i++) values.Add(pixels[i].r);
                }
                head.AddRange(Digest(values, Math.Min(values.Count, n)));
                Record(key + "/baketexture/" + option, head);
                if (vt != null) DestroyImmediate(vt);
                if (it != null) DestroyImmediate(it);
            }
            Done(ps);
        }
        foreach (var (_, mesh, _) in cases) DestroyImmediate(mesh);
    }
}
