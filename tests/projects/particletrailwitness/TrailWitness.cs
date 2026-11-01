using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

// Read-only inspection inserted immediately before the authored Capture's camera.Render.
public static class TrailWitness
{
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static StreamWriter output;
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static string J(object x)
    {
        if (x == null) return "null";
        if (x is string s) return Q(s);
        if (x is bool b) return b ? "true" : "false";
        if (x is float f) return "{\"r\":" + Q(f.ToString("R", CultureInfo.InvariantCulture)) + ",\"bits\":" + Q(BitConverter.ToUInt32(BitConverter.GetBytes(f), 0).ToString("x8")) + "}";
        if (x is Enum) return "{\"name\":" + Q(x.ToString()) + ",\"value\":" + Convert.ToInt64(x) + "}";
        if (x is Vector2 v2) return J(new[] { v2.x, v2.y });
        if (x is Vector3 v3) return J(new[] { v3.x, v3.y, v3.z });
        if (x is Vector4 v4) return J(new[] { v4.x, v4.y, v4.z, v4.w });
        if (x is Quaternion q) return J(new[] { q.x, q.y, q.z, q.w });
        if (x is Color c) return J(new[] { c.r, c.g, c.b, c.a });
        if (x is Color32 c32) return J(new[] { c32.r, c32.g, c32.b, c32.a });
        if (x is Bounds bounds) return "{\"center\":" + J(bounds.center) + ",\"extents\":" + J(bounds.extents) + "}";
        if (x is Matrix4x4 m) return J(Enumerable.Range(0, 16).Select(i => m[i]).ToArray());
        if (x is IEnumerable values) return "[" + string.Join(",", values.Cast<object>().Select(J)) + "]";
        if (x.GetType().IsPrimitive) return Convert.ToString(x, CultureInfo.InvariantCulture);
        throw new NotSupportedException("Unrecorded type " + x.GetType());
    }
    static void V(string key, object value) => output.WriteLine("{\"k\":" + Q(key) + ",\"v\":" + J(value) + "}");
    static void Properties(string key, object value)
    {
        foreach (var f in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) V(key + "." + f.Name, f.GetValue(value));
        foreach (var p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.GetIndexParameters().Length == 0) V(key + "." + p.Name, p.GetValue(value));
    }
    public static void Inspect(ParticleSystem ps, Camera camera, RenderTexture target)
    {
        string dir = Environment.GetEnvironmentVariable("PARTICLES_IMAGE_DIR");
        Directory.CreateDirectory(dir);
        using (output = new StreamWriter(Path.Combine(dir, "witness.jsonl")))
        {
            V("release", Application.unityVersion);
            V("system.seed", ps.randomSeed); V("system.time", ps.time);
            V("system.localToWorld", ps.transform.localToWorldMatrix);
            var particles = new ParticleSystem.Particle[ps.main.maxParticles];
            int count = ps.GetParticles(particles);
            V("particles.count", count); V("particles.bufferLength", particles.Length);
            for (int i = 0; i < count; i++)
            {
                var p = particles[i]; string k = "particles." + i;
                V(k + ".seed", p.randomSeed); V(k + ".position", p.position);
                V(k + ".velocity", p.velocity); V(k + ".totalVelocity", p.totalVelocity);
                V(k + ".startLifetime", p.startLifetime); V(k + ".remainingLifetime", p.remainingLifetime);
                V(k + ".startSize", p.startSize3D); V(k + ".size", p.GetCurrentSize3D(ps));
                V(k + ".startColor", p.startColor); V(k + ".color", p.GetCurrentColor(ps));
                V(k + ".rotation", p.rotation3D); V(k + ".angularVelocity", p.angularVelocity3D);
            }
            var trails = new ParticleSystem.Trails();
            V("trails.returnCount", ps.GetTrails(ref trails));
            var fields = typeof(ParticleSystem.Trails).GetFields(Fields);
            V("trails.fieldNames", fields.Select(f => f.Name).ToArray());
            foreach (var f in fields) V("trails." + f.Name, f.GetValue(trails));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var streams = new List<ParticleSystemVertexStream>(); renderer.GetActiveTrailVertexStreams(streams);
            V("mesh.activeTrailStreams", streams);
            var options = ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale;
            V("mesh.options", options);
            var mesh = new Mesh(); renderer.BakeTrailsMesh(mesh, camera, options);
            V("mesh.vertexCount", mesh.vertexCount); V("mesh.indexFormat", mesh.indexFormat);
            var attrs = mesh.GetVertexAttributes(); V("mesh.attributeCount", attrs.Length);
            for (int i = 0; i < attrs.Length; i++)
            {
                var a = attrs[i]; string k = "mesh.attribute." + i;
                V(k + ".semantic", a.attribute); V(k + ".format", a.format); V(k + ".dimension", a.dimension);
                V(k + ".stream", a.stream); V(k + ".offset", mesh.GetVertexAttributeOffset(a.attribute));
            }
            using (var raw = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var data = raw[0]; V("mesh.vertexBufferCount", data.vertexBufferCount);
                for (int i = 0; i < data.vertexBufferCount; i++)
                {
                    V("mesh.buffer." + i + ".stride", data.GetVertexBufferStride(i));
                    V("mesh.buffer." + i + ".bytes", Convert.ToBase64String(data.GetVertexData<byte>(i).ToArray()));
                }
                V("mesh.indexBytes", Convert.ToBase64String(data.GetIndexData<byte>().ToArray()));
            }
            V("mesh.vertices", mesh.vertices); V("mesh.normals", mesh.normals);
            V("mesh.tangents", mesh.tangents); V("mesh.colors32", mesh.colors32);
            for (int i = 0; i < 8; i++) { var uv = new List<Vector4>(); mesh.GetUVs(i, uv); V("mesh.uv" + i, uv); }
            V("mesh.subMeshCount", mesh.subMeshCount);
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                Properties("mesh.submesh." + i, mesh.GetSubMesh(i));
                V("mesh.submesh." + i + ".indicesWithoutBase", mesh.GetIndices(i, false));
                V("mesh.submesh." + i + ".indicesWithBase", mesh.GetIndices(i, true));
            }
            V("camera.worldToCamera", camera.worldToCameraMatrix); V("camera.projection", camera.projectionMatrix);
            V("camera.gpuProjection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            V("camera.allowHDR", camera.allowHDR); V("camera.allowMSAA", camera.allowMSAA);
            V("camera.fov", camera.fieldOfView); V("camera.aspect", camera.aspect);
            V("camera.near", camera.nearClipPlane); V("camera.far", camera.farClipPlane);
            Properties("target", target.descriptor);
            V("quality.level", QualitySettings.GetQualityLevel()); V("quality.AA", QualitySettings.antiAliasing);
            V("quality.colorSpace", QualitySettings.activeColorSpace); V("quality.maximumParticleDeltaTime", Time.maximumParticleDeltaTime);
            V("complete", true);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
