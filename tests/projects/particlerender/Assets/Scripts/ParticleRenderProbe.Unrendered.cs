using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Systems the culling cannot see and members that change what a system draws or does:
/// <list type="bullet">
/// <item><c>norender/*</c>: systems without a ParticleSystemRenderer (removed) or with a disabled
/// one, for every culling mode, played over frames while a camera looks at their place, away
/// and back (as <see cref="Culling"/>).</item>
/// <item><c>streams/*</c>: the active vertex streams after the obsolete EnableVertexStreams and
/// DisableVertexStreams, from the default streams and from custom ones.</item>
/// <item><c>instancing/*</c>: supportsMeshInstancing for render modes and materials.</item>
/// <item><c>force/*</c>: a free Rigidbody hit by particles with a collider force and each
/// multiplier, head-on, at a slant and off its centre: its velocity and angular velocity over
/// frames.</item>
/// </list>
/// </summary>
public partial class ParticleRenderProbe
{
    static void RecordUnrendered(string key, ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Buffer);
        float life = 0f; var pos = Vector3.zero;
        for (int i = 0; i < n; i++) { life += s_Buffer[i].remainingLifetime; pos += s_Buffer[i].position; }
        Record(key, new List<float>
        {
            ps.particleCount, ps.time, ps.isPlaying ? 1 : 0, ps.isPaused ? 1 : 0, ps.isEmitting ? 1 : 0,
            ps.IsAlive(true) ? 1 : 0, life, pos.x, pos.y, pos.z,
        });
    }

    IEnumerator UnrenderedCulling()
    {
        Time.captureDeltaTime = 1f / 20f;
        var cameraObject = new GameObject("UnrenderedCamera");
        cameraObject.transform.position = new Vector3(0f, -40f, -12f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 60f;
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        var modes = new List<(string, ParticleSystemCullingMode, bool)>
        {
            ("automatic_loop", ParticleSystemCullingMode.Automatic, true),
            ("automatic_once", ParticleSystemCullingMode.Automatic, false),
            ("pause_loop", ParticleSystemCullingMode.Pause, true),
            ("catchup_loop", ParticleSystemCullingMode.PauseAndCatchup, true),
            ("always_loop", ParticleSystemCullingMode.AlwaysSimulate, true),
        };
        var systems = new List<(string, ParticleSystem)>();
        int index = 0;
        foreach (var variant in new[] { "none", "disabled" })
            foreach (var (name, mode, loop) in modes)
            {
                var position = new Vector3(-4f + index % 5 * 2f, index < 5 ? -38.5f : -41.5f, 0f);
                var ps = MakeCulled("norender/" + variant + "_" + name, mode, loop, position, 0,
                    loop ? null : (Action<ParticleSystem>)(s => { var m = s.main; m.duration = 4f; }));
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (variant == "none") DestroyImmediate(renderer);
                else renderer.enabled = false;
                systems.Add(("norender/" + variant + "_" + name, ps));
                index++;
            }
        yield return null;
        foreach (var (_, ps) in systems) ps.Play();
        for (int frame = 0; frame < 40; frame++)
        {
            yield return null;
            foreach (var (key, ps) in systems) RecordUnrendered(key + "/" + frame, ps);
            // No camera renders before frame 5; then it looks at the systems' place until frame
            // 14, away for 15-29, back from 30.
            bool away = frame >= 15 && frame < 30;
            cameraObject.transform.rotation = away ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            if (frame >= 5) camera.Render();
        }
        foreach (var (_, ps) in systems) Done(ps);
        camera.targetTexture = null;
        DestroyImmediate(target);
        DestroyImmediate(cameraObject);
        Time.captureDeltaTime = 0f;
    }

    static readonly List<ParticleSystemVertexStream> s_Streams = new List<ParticleSystemVertexStream>();

    static void RecordStreams(string key, ParticleSystemRenderer r)
    {
        r.GetActiveVertexStreams(s_Streams);
        var values = new List<float>();
        foreach (var s in s_Streams) values.Add((int)s);
        Record(key, values);
    }

#pragma warning disable CS0618
    static void VertexStreams()
    {
        var all = ParticleSystemVertexStreams.Position | ParticleSystemVertexStreams.Normal | ParticleSystemVertexStreams.Tangent
            | ParticleSystemVertexStreams.Color | ParticleSystemVertexStreams.UV | ParticleSystemVertexStreams.UV2BlendAndFrame
            | ParticleSystemVertexStreams.CenterAndVertexID | ParticleSystemVertexStreams.Size | ParticleSystemVertexStreams.Rotation
            | ParticleSystemVertexStreams.Velocity | ParticleSystemVertexStreams.Lifetime | ParticleSystemVertexStreams.Custom1
            | ParticleSystemVertexStreams.Custom2 | ParticleSystemVertexStreams.Random;
        var cases = new List<(string, Action<ParticleSystemRenderer>)>
        {
            ("default", r => { }),
            ("enable_normal_tangent", r => r.EnableVertexStreams(ParticleSystemVertexStreams.Normal | ParticleSystemVertexStreams.Tangent)),
            ("enable_custom", r => r.EnableVertexStreams(ParticleSystemVertexStreams.Custom1 | ParticleSystemVertexStreams.Custom2)),
            ("enable_lifetime_random", r => r.EnableVertexStreams(ParticleSystemVertexStreams.Lifetime | ParticleSystemVertexStreams.Random)),
            ("enable_all", r => r.EnableVertexStreams(all)),
            ("disable_color", r => r.DisableVertexStreams(ParticleSystemVertexStreams.Color)),
            ("disable_uv", r => r.DisableVertexStreams(ParticleSystemVertexStreams.UV)),
            ("disable_position", r => r.DisableVertexStreams(ParticleSystemVertexStreams.Position)),
            ("enable_then_disable", r =>
            {
                r.EnableVertexStreams(ParticleSystemVertexStreams.Normal | ParticleSystemVertexStreams.Velocity);
                r.DisableVertexStreams(ParticleSystemVertexStreams.Normal);
            }),
            ("custom_then_enable", r =>
            {
                r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                    { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
                r.EnableVertexStreams(ParticleSystemVertexStreams.Size | ParticleSystemVertexStreams.Normal);
            }),
            ("custom_then_disable", r =>
            {
                r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                    { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
                r.DisableVertexStreams(ParticleSystemVertexStreams.Normal | ParticleSystemVertexStreams.UV);
            }),
        };
        foreach (var (name, act) in cases)
        {
            var ps = Make("streams/" + name);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            act(r);
            RecordStreams("streams/" + name, r);
            Record("streams/" + name + "/enabled", new List<float>
            {
                r.AreVertexStreamsEnabled(ParticleSystemVertexStreams.Normal) ? 1 : 0,
                r.AreVertexStreamsEnabled(ParticleSystemVertexStreams.Color) ? 1 : 0,
                (int)r.GetEnabledVertexStreams(all),
            });
            Done(ps);
        }
    }
#pragma warning restore CS0618

    static void MeshInstancing()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(cube);
        var materials = new List<(string, Material)>
        {
            ("legacy_alpha", s_ParticleMaterial),
            ("standard_surface", new Material(Shader.Find("Particles/Standard Surface"))),
            ("standard_unlit", new Material(Shader.Find("Particles/Standard Unlit"))),
            ("standard", new Material(Shader.Find("Standard"))),
            ("none", null),
        };
        foreach (var (name, material) in materials)
            foreach (var mode in new[] { ParticleSystemRenderMode.Mesh, ParticleSystemRenderMode.Billboard })
            {
                var ps = Make("instancing/" + name);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = mode;
                r.mesh = mesh;
                r.sharedMaterial = material;
                Record("instancing/" + name + "/" + mode, new List<float> { r.supportsMeshInstancing ? 1 : 0 });
                Done(ps);
            }
    }

    IEnumerator ColliderForce()
    {
        Time.captureDeltaTime = 1f / 20f;
        // (name, force, by angle, by speed, by size, the body's yaw in degrees, its sideways offset)
        var cases = new List<(string, float, bool, bool, bool, float, float)>
        {
            ("plain", 2f, false, false, false, 0f, 0f),
            ("angle", 2f, true, false, false, 0f, 0f),
            ("speed", 2f, false, true, false, 0f, 0f),
            ("size", 2f, false, false, true, 0f, 0f),
            ("zero", 0f, false, false, false, 0f, 0f),
            ("plain_tilted", 2f, false, false, false, 60f, 0f),
            ("angle_tilted", 2f, true, false, false, 60f, 0f),
            ("plain_offset", 2f, false, false, false, 0f, 0.7f),
        };
        var bodies = new List<(string, Rigidbody, ParticleSystem)>();
        int slot = 0;
        foreach (var (name, force, angle, speed, size, yaw, offset) in cases)
        {
            var origin = new Vector3(30f + 6f * slot++, 0f, 0f);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "force/" + name + "/body";
            box.transform.position = origin + new Vector3(offset, 0f, 3f);
            box.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            box.transform.localScale = new Vector3(2f, 2f, 0.5f);
            var body = box.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = 1f;
            body.angularDamping = 0f;
            var ps = Make("force/" + name, s =>
            {
                var m = s.main; m.startSpeed = 6f; m.startLifetime = 2f; m.startSize = 0.4f; m.loop = false; m.duration = 1f;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var e = s.emission; e.enabled = true; e.rateOverTime = 0f;
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });
                var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
                c.bounce = 0f; c.lifetimeLoss = 1f; c.dampen = 0f; c.radiusScale = 1f; c.quality = ParticleSystemCollisionQuality.High;
                c.colliderForce = force;
                c.multiplyColliderForceByCollisionAngle = angle;
                c.multiplyColliderForceByParticleSpeed = speed;
                c.multiplyColliderForceByParticleSize = size;
            });
            ps.transform.position = origin;
            bodies.Add(("force/" + name, body, ps));
        }
        yield return null;
        foreach (var (_, _, ps) in bodies) ps.Play();
        for (int frame = 0; frame < 20; frame++)
        {
            yield return null;
            foreach (var (key, body, ps) in bodies)
            {
                // Not the body's position: how many physics steps ran since a hit changes between
                // runs (the body has no damping, so its velocities do not).
                var v = body.linearVelocity; var w = body.angularVelocity;
                Record(key + "/" + frame, new List<float> { ps.particleCount, v.x, v.y, v.z, w.x, w.y, w.z });
            }
        }
        foreach (var (_, body, ps) in bodies) { Done(ps); DestroyImmediate(body.gameObject); }
        Time.captureDeltaTime = 0f;
    }
}
