using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Records what CullingResults computes for point (every cube face) and spot light
/// shadows (matrices, culling spheres and planes) over light positions, ranges, angles and field of
/// view biases, from inside a render (where a cull is possible).</summary>
public static class LocalShadowMatrixProbe
{
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string M(Matrix4x4 m) => "[" + string.Join(",", Enumerable.Range(0, 16).Select(i => F(m[i]))) + "]";
    static string V(Vector4 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)},{F(v.w)}]";

    static string Split(ShadowSplitData sd)
    {
        var planes = string.Join(",", Enumerable.Range(0, sd.cullingPlaneCount).Select(k => { var pl = sd.GetCullingPlane(k); return $"[{F(pl.normal.x)},{F(pl.normal.y)},{F(pl.normal.z)},{F(pl.distance)}]"; }));
        return $"\"sphere\":{V(sd.cullingSphere)},\"nearPlane\":{F(sd.cullingNearPlane)},\"planes\":[{planes}]";
    }

    public static void Record(string path)
    {
        var sb = new StringBuilder();
        var go = new GameObject("Local Shadow Probe Light");
        var light = go.AddComponent<Light>();
        light.shadows = LightShadows.Hard;
        bool done = false;
        void Handler(ScriptableRenderContext context, Camera camera)
        {
            if (done) return;
            done = true;
            foreach (var position in new[] { new Vector3(0f, 1f, -1.2f), new Vector3(2f, 3f, 1f) })
                foreach (var range in new[] { 6f, 20f })
                {
                    go.transform.SetPositionAndRotation(position, Quaternion.Euler(50f, 30f, 0f));
                    light.range = range;
                    foreach (var near in new[] { 0.1f, 0.5f })
                    {
                        light.shadowNearPlane = near;
                        light.type = LightType.Point;
                        int index = Cull(context, camera, light, out var cull);
                        foreach (var fovBias in new[] { 0f, 4f })
                            for (int face = 0; face < 6; face++)
                            {
                                bool ok = cull.ComputePointShadowMatricesAndCullingPrimitives(index, (CubemapFace)face, fovBias, out var view, out var proj, out var sd);
                                sb.Append($"{{\"point\":{V(position)},\"range\":{F(range)},\"near\":{F(near)},\"fovBias\":{F(fovBias)},\"face\":{face},\"ok\":{(ok ? "true" : "false")},\"view\":{M(view)},\"proj\":{M(proj)},{Split(sd)}}}\n");
                            }
                        light.type = LightType.Spot;
                        foreach (var angle in new[] { 40f, 80f, 150f })
                        {
                            light.spotAngle = angle;
                            index = Cull(context, camera, light, out cull);
                            bool ok = cull.ComputeSpotShadowMatricesAndCullingPrimitives(index, out var view, out var proj, out var sd);
                            sb.Append($"{{\"spot\":{V(position)},\"range\":{F(range)},\"near\":{F(near)},\"angle\":{F(angle)},\"ok\":{(ok ? "true" : "false")},\"view\":{M(view)},\"proj\":{M(proj)},{Split(sd)}}}\n");
                        }
                    }
                }
        }
        RenderPipelineManager.beginCameraRendering += Handler;
        var cam = Camera.main;
        var target = new RenderTexture(320, 180, 24);
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = null;
        target.Release();
        RenderPipelineManager.beginCameraRendering -= Handler;
        Object.DestroyImmediate(go);
        File.WriteAllText(path, sb.ToString());
    }

    static int Cull(ScriptableRenderContext context, Camera camera, Light light, out CullingResults cull)
    {
        camera.TryGetCullingParameters(out var p);
        p.shadowDistance = 50f;
        cull = context.Cull(ref p);
        for (int i = 0; i < cull.visibleLights.Length; i++) if (cull.visibleLights[i].light == light) return i;
        return -1;
    }
}
