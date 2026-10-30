using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Flares and halos (the scene's light halo, Halo component, LensFlare, a light's flare; with and
// without a FlareLayer), rendering paths (actualRenderingPath, deferred, vertex lit, the deferred
// camera events) and light probe proxy volumes.
public partial class FxProbe
{
    static GameObject SceneObject(string name)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
        return null;
    }

    void Flares()
    {
        Value("flares.haloStrength", F(RenderSettings.haloStrength));
        Value("flares.flareStrength", F(RenderSettings.flareStrength));
        Value("flares.flareFadeSpeed", F(RenderSettings.flareFadeSpeed));
        var sceneCamera = SceneObject("SceneCamera");
        Value("flares.sceneCameraFlareLayer", B(sceneCamera != null && sceneCamera.GetComponent<FlareLayer>() != null));
        var lensFlare = SceneObject("LensFlareObject");
        var lf = lensFlare != null ? lensFlare.GetComponent<LensFlare>() : null;
        Value("flares.lensFlare", lf == null ? "null" : "[" + F(lf.brightness) + "," + F(lf.fadeSpeed) + "," + C(lf.color) + "," + N(lf.flare) + "]");
        var sun = SceneObject("FlareSun");
        Value("flares.lightFlare", sun == null ? "null" : N(sun.GetComponent<Light>().flare));
        var halo = SceneObject("HaloObject");
        Value("flares.haloComponent", B(halo != null && halo.GetComponent("Halo") != null));

        ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 0.5f);
        var camera = MakeCamera();
        camera.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
        Capture(camera, "flare_none");
        var layer = camera.gameObject.AddComponent<FlareLayer>();
        foreach (var name in new[] { "HaloLight", "HaloObject", "LensFlareObject", "FlareSun" })
        {
            var go = SceneObject(name);
            if (go == null) continue;
            go.SetActive(true);
            Capture(camera, "flare_" + name);
            layer.enabled = false;
            Capture(camera, "flare_" + name + "_no_layer");
            layer.enabled = true;
            go.SetActive(false);
        }
        RenderSettings.haloStrength = 1f;
        var light = SceneObject("HaloLight");
        if (light != null)
        {
            light.SetActive(true);
            Capture(camera, "flare_HaloLight_strength1");
            light.SetActive(false);
        }
        RenderSettings.haloStrength = 0.5f;
        Clear();
    }

    /// <summary>The camera events a render fires, in order (a clock copied and advanced by a
    /// command buffer at each).</summary>
    string EventOrder(Camera camera)
    {
        var increment = Add(new Material(Shader.Find("Probe/Increment")));
        RenderTexture New()
        {
            var rt = Add(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            rt.Create();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;
            return rt;
        }
        var clock = New();
        var active0 = RenderTexture.active;
        RenderTexture.active = clock;
        GL.Clear(false, true, new Color(0f, 0f, 0f, 1f));
        RenderTexture.active = active0;
        var tmp = New();
        var slots = new Dictionary<CameraEvent, RenderTexture>();
        foreach (CameraEvent e in Enum.GetValues(typeof(CameraEvent)))
        {
            var slot = slots[e] = New();
            var cmd = new CommandBuffer { name = "log " + e };
            cmd.Blit(clock, slot);
            cmd.Blit(clock, tmp, increment, 0);
            cmd.Blit(tmp, clock);
            camera.AddCommandBuffer(e, cmd);
        }
        camera.Render();
        camera.RemoveAllCommandBuffers();
        var fired = new List<(int, CameraEvent)>();
        foreach (var pair in slots)
        {
            var c = Pixel(pair.Value);
            if (c.a < 0.5f) continue;
            fired.Add((Mathf.RoundToInt(c.r * 255f / 8f), pair.Key));
        }
        fired.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        var order = new StringBuilder("[");
        foreach (var (_, e) in fired) order.Append(order.Length > 1 ? "," : "").Append(S(e.ToString()));
        return order.Append(']').ToString();
    }

    /// <summary>Lit, shadowed objects of several materials, a point and a spot light, and a
    /// transparent sphere.</summary>
    void PathStage()
    {
        Primitive(PrimitiveType.Plane, Vector3.zero, Standard(new Color(0.7f, 0.7f, 0.7f), 0.8f)).transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        Primitive(PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f), Standard(new Color(1f, 0.4f, 0.3f), 0.3f, 0f));
        Primitive(PrimitiveType.Sphere, new Vector3(1f, 0.5f, 0f), Standard(new Color(0.8f, 0.8f, 0.9f), 0.9f, 1f));
        Primitive(PrimitiveType.Cube, new Vector3(0f, 0.5f, 1f), Standard(new Color(0.3f, 0.4f, 1f), 0.5f));
        var transparent = Add(new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")));
        transparent.color = new Color(0.3f, 1f, 0.3f, 0.5f);
        Primitive(PrimitiveType.Sphere, new Vector3(0.3f, 0.6f, -1.2f), transparent, 0.6f);
        var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
        sun.shadows = LightShadows.Soft;
        Point(new Vector3(-0.8f, 1.2f, -0.8f), new Color(1f, 0.3f, 0.2f), 3f, 1.5f);
        var spot = Add(new GameObject("spot")).AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.transform.position = new Vector3(1.5f, 2.5f, -1f);
        spot.transform.LookAt(new Vector3(0.5f, 0f, 0.5f));
        spot.spotAngle = 40f;
        spot.range = 6f;
        spot.intensity = 2f;
        spot.color = new Color(0.3f, 0.5f, 1f);
        spot.shadows = LightShadows.Hard;
    }

    void RenderingPaths()
    {
        var sceneCamera = SceneObject("SceneCamera");
        if (sceneCamera != null)
        {
            var c = sceneCamera.GetComponent<Camera>();
            Value("paths.scene.renderingPath", S(c.renderingPath.ToString()));
            Value("paths.scene.actualRenderingPath", S(c.actualRenderingPath.ToString()));
        }
        var camera = MakeCamera();
        foreach (RenderingPath path in Enum.GetValues(typeof(RenderingPath)))
        {
            camera.renderingPath = path;
            camera.orthographic = false;
            Value("paths.actual." + path, S(camera.actualRenderingPath.ToString()));
            camera.orthographic = true;
            Value("paths.actualOrtho." + path, S(camera.actualRenderingPath.ToString()));
        }
        camera.orthographic = false;
        Clear();

        PathStage();
        camera = MakeCamera();
        camera.renderingPath = RenderingPath.DeferredShading;
        Capture(camera, "path_deferred");
        camera.allowHDR = false;
        Capture(camera, "path_deferred_ldr");
        camera.allowHDR = true;
        camera.renderingPath = RenderingPath.Forward;
        Capture(camera, "path_forward");
        camera.renderingPath = RenderingPath.VertexLit;
        Capture(camera, "path_vertexlit");
        camera.renderingPath = RenderingPath.DeferredShading;
        Value("paths.events.deferred", EventOrder(camera));
        camera.renderingPath = RenderingPath.Forward;
        Value("paths.events.forward", EventOrder(camera));
        Clear();
    }

    void ProxyVolumes()
    {
        Value("lppv.isFeatureSupported", B(LightProbeProxyVolume.isFeatureSupported));
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.9f, 0.3f, 0.2f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.8f, 0.3f);
        RenderSettings.ambientGroundColor = new Color(0.2f, 0.3f, 0.9f);
        var white = Standard(Color.white, 0.2f);
        var blend = Primitive(PrimitiveType.Sphere, new Vector3(-1.5f, 0.6f, 0f), white);
        var proxy = Primitive(PrimitiveType.Sphere, new Vector3(0f, 0.6f, 0f), white);
        var volume = proxy.AddComponent<LightProbeProxyVolume>();
        proxy.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.UseProxyVolume;
        var noVolume = Primitive(PrimitiveType.Sphere, new Vector3(1.5f, 0.6f, 0f), white);
        noVolume.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.UseProxyVolume;
        var off = Primitive(PrimitiveType.Cube, new Vector3(0f, 1.6f, 0.5f), white, 0.6f);
        off.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.Off;
        Value("lppv.defaults", "[" + S(volume.boundingBoxMode.ToString()) + "," + S(volume.resolutionMode.ToString()) + "," + S(volume.refreshMode.ToString())
            + "," + S(volume.probePositionMode.ToString()) + "," + S(volume.qualityMode.ToString()) + "," + I(volume.gridResolutionX) + "," + I(volume.gridResolutionY)
            + "," + I(volume.gridResolutionZ) + "," + F(volume.probeDensity) + "]");
        var scene = SceneObject("ProxyVolume");
        if (scene != null)
        {
            var v = scene.GetComponent<LightProbeProxyVolume>();
            Value("lppv.scene", "[" + S(v.resolutionMode.ToString()) + "," + I(v.gridResolutionX) + "," + I(v.gridResolutionY) + "," + I(v.gridResolutionZ) + "]");
        }
        var camera = MakeCamera();
        Capture(camera, "lppv");
        Clear();
    }
}
