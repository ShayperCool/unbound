using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Records what CullingResults computes for directional shadows (matrices, culling
/// spheres and planes, caster bounds) for several lights, cascade splits, resolutions and shadow
/// distances, from inside a render (where a cull is possible).</summary>
public static class ShadowMatrixProbe
{
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string M(Matrix4x4 m) => "[" + string.Join(",", Enumerable.Range(0, 16).Select(i => F(m[i]))) + "]";
    static string V(Vector4 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)},{F(v.w)}]";

    public static void Record(string path)
    {
        var sb = new StringBuilder();
        var light = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
        var rotation = light.transform.rotation;
        light.shadows = LightShadows.Hard;
        bool done = false;
        void Handler(ScriptableRenderContext context, Camera camera)
        {
            if (done) return;
            done = true;
            sb.Append($"{{\"camera\":{{\"aspect\":{F(camera.aspect)},\"fov\":{F(camera.fieldOfView)},\"near\":{F(camera.nearClipPlane)},\"far\":{F(camera.farClipPlane)},\"pixelWidth\":{camera.pixelWidth},\"pixelHeight\":{camera.pixelHeight}}}}}\n");
            {
                var (fov0, aspect0, near0, rot0) = (camera.fieldOfView, camera.aspect, camera.nearClipPlane, camera.transform.rotation);
                foreach (var fov in new[] { 30f, 60f, 90f })
                    foreach (var aspect in new[] { 1f, 1.7777778f, 0.5f })
                        foreach (var near in new[] { 0.3f, 2f })
                            foreach (var pitch in new[] { 10f, 0f, 45f })
                            {
                                camera.fieldOfView = fov;
                                camera.aspect = aspect;
                                camera.nearClipPlane = near;
                                camera.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
                                camera.TryGetCullingParameters(out var gp);
                                gp.shadowDistance = 100f;
                                var gc = context.Cull(ref gp);
                                int gi = 0;
                                for (int i = 0; i < gc.visibleLights.Length; i++) if (gc.visibleLights[i].light == light) gi = i;
                                gc.ComputeDirectionalShadowMatricesAndCullingPrimitives(gi, 0, 1, new Vector3(1f, 0f, 0f), 2048, 3f, out _, out _, out var gsd);
                                var pos = camera.transform.position;
                                sb.Append($"{{\"grid\":[{F(fov)},{F(aspect)},{F(near)},{F(pitch)}],\"sphere\":{V(gsd.cullingSphere)},\"camera\":[{F(pos.x)},{F(pos.y)},{F(pos.z)}]}}\n");
                            }
                camera.fieldOfView = fov0;
                camera.nearClipPlane = near0;
                camera.transform.rotation = rot0;
                camera.ResetAspect();
            }
            foreach (var distance in new[] { 1f, 2f, 5f, 10f, 30f, 100f, 200f, 999f, 2000f })
            {
                camera.TryGetCullingParameters(out var p);
                p.shadowDistance = distance;
                var cull = context.Cull(ref p);
                int index = 0;
                for (int i = 0; i < cull.visibleLights.Length; i++) if (cull.visibleLights[i].light == light) index = i;
                cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(index, 0, 1, new Vector3(1f, 0f, 0f), 2048, 3f, out var view, out var proj, out var sd);
                sb.Append($"{{\"sweep\":{F(distance)},\"sphere\":{V(sd.cullingSphere)},\"proj\":{M(proj)},\"view\":{M(view)}}}\n");
            }
            foreach (var euler in new[] { rotation.eulerAngles, new Vector3(40f, 160f, 0f), new Vector3(70f, -30f, 0f), new Vector3(15f, 100f, 0f) })
            {
                light.transform.rotation = Quaternion.Euler(euler);
                foreach (var distance in new[] { 50f, 20f })
                {
                    camera.TryGetCullingParameters(out var p);
                    p.shadowDistance = distance;
                    var cull = context.Cull(ref p);
                    int index = -1;
                    for (int i = 0; i < cull.visibleLights.Length; i++) if (cull.visibleLights[i].light == light) index = i;
                    bool bounds = cull.GetShadowCasterBounds(index, out var b);
                    sb.Append($"{{\"light\":{V(euler)},\"distance\":{F(distance)},\"index\":{index},\"casterBounds\":{(bounds ? "true" : "false")},\"bounds\":[{F(b.center.x)},{F(b.center.y)},{F(b.center.z)},{F(b.extents.x)},{F(b.extents.y)},{F(b.extents.z)}]}}\n");
                    foreach (var (count, ratio) in new[] { (1, new Vector3(1f, 0f, 0f)), (2, new Vector3(0.25f, 0f, 0f)), (4, new Vector3(0.067f, 0.2f, 0.467f)) })
                        foreach (var resolution in new[] { 2048, 1024, 512 })
                            for (int split = 0; split < count; split++)
                            {
                                bool ok = cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(index, split, count, ratio, resolution, 3f, out var view, out var proj, out var sd);
                                var planes = string.Join(",", Enumerable.Range(0, sd.cullingPlaneCount).Select(k => { var pl = sd.GetCullingPlane(k); return $"[{F(pl.normal.x)},{F(pl.normal.y)},{F(pl.normal.z)},{F(pl.distance)}]"; }));
                                sb.Append($"{{\"light\":{V(euler)},\"distance\":{F(distance)},\"count\":{count},\"ratio\":{V(ratio)},\"resolution\":{resolution},\"split\":{split},\"ok\":{(ok ? "true" : "false")},\"view\":{M(view)},\"proj\":{M(proj)},\"sphere\":{V(sd.cullingSphere)},\"blend\":{F(sd.shadowCascadeBlendCullingFactor)},\"nearPlane\":{F(sd.cullingNearPlane)},\"planes\":[{planes}]}}\n");
                            }
                }
            }
            light.transform.rotation = rotation;
        }
        RenderPipelineManager.beginCameraRendering += Handler;
        var cam = Camera.main;
        var target = new RenderTexture(320, 180, 24);
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = null;
        target.Release();
        RenderPipelineManager.beginCameraRendering -= Handler;
        light.shadows = LightShadows.None;
        File.WriteAllText(path, sb.ToString());
    }
}
