using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The cases: halos (Halo components, lights with a halo, the scene halo texture) and lens flares
// (sizes, positions, layouts, element switches, LensFlare and Light settings, occlusion and
// fading), with the draw order relative to the camera's command buffer events.
public partial class FlareProbe
{
    /// <summary>Image counts of the flare texture layouts (1 Large 4 Small, 1 Large 2 Medium 8 Small,
    /// 1 Texture, 2x2, 3x3, 4x4).</summary>
    static readonly int[] LayoutImages = { 5, 11, 1, 4, 9, 16 };
    static readonly Color Tint = new Color(1f, 0.5f, 0.25f, 0.5f);

    IEnumerator Shot(string key, int frames = 2, int every = 0) => Frames(key, frames, every);

    void ResetCamera()
    {
        cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5f), Quaternion.identity);
        cam.orthographic = false;
        cam.fieldOfView = 60f;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.targetTexture = target;
        cam.GetComponent<FlareLayer>().enabled = true;
        RenderSettings.haloStrength = 0.5f;
        RenderSettings.flareStrength = 1f;
        RenderSettings.flareFadeSpeed = 3f;
        RenderSettings.fog = false;
    }

    void Fog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 2f;
        RenderSettings.fogEndDistance = 8f;
        RenderSettings.fogColor = new Color(0.2f, 0.4f, 0.6f, 1f);
    }

    void DescribeFalloff(string key)
    {
        var t = Shader.GetGlobalTexture("_HaloFalloff");
        if (t == null) { Value(key, "null"); return; }
        Value(key, "{\"name\":" + Q(t.name) + ",\"size\":[" + t.width + "," + t.height + "],\"filter\":" + Q(t.filterMode.ToString())
            + ",\"wrap\":" + Q(t.wrapMode.ToString()) + ",\"mips\":" + t.mipmapCount + "}");
        var rt = new RenderTexture(t.width, t.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        rt.Create();
        Graphics.Blit(t, rt);
        Read(key, rt, t.width, t.height);
        rt.Release();
        DestroyImmediate(rt);
    }

    LensFlare Lens(string flare, Vector3 position, float fadeSpeed = 1000f, string name = "Lens")
    {
        var go = SceneObject(name);
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        var lens = go.GetComponent<LensFlare>();
        lens.flare = Resources.Load<Flare>(flare);
        if (lens.flare == null) throw new System.Exception("no flare " + flare);
        lens.fadeSpeed = fadeSpeed;
        lens.brightness = 1f;
        lens.color = Color.white;
        go.SetActive(true);
        return lens;
    }

    static GameObject Occluder(Vector3 position, bool visible, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Occluder";
        go.transform.position = position;
        go.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        if (!collider) DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<Renderer>();
        renderer.enabled = visible;
        var material = new Material(Shader.Find("Unlit/Color"));
        material.color = Color.black;
        renderer.sharedMaterial = material;
        return go;
    }

    IEnumerator Probe()
    {
        DescribeFalloff("start.haloFalloff");
        Value("defaults.haloStrength", F(RenderSettings.haloStrength));
        Value("defaults.flareStrength", F(RenderSettings.flareStrength));
        Value("defaults.flareFadeSpeed", F(RenderSettings.flareFadeSpeed));
        var fresh = new GameObject("FreshLensFlare").AddComponent<LensFlare>();
        Value("defaults.lensFlare", "[" + F(fresh.brightness) + "," + F(fresh.fadeSpeed) + "," + C(fresh.color) + "," + N(fresh.flare) + "]");
        DestroyImmediate(fresh.gameObject);
        var scene = SceneObject("Lens").GetComponent<LensFlare>();
        Value("scene.lensFlare", "[" + F(scene.brightness) + "," + F(scene.fadeSpeed) + "," + C(scene.color) + "," + N(scene.flare) + "]");
        Value("scene.haloPoint", "[" + B(SceneObject("HaloPoint").GetComponent<Light>().flare != null) + "," + B(SceneObject("HaloWhite").GetComponent("Halo") != null) + "]");
        MakeCamera();
        DontDestroyOnLoad(gameObject);
        DontDestroyOnLoad(cam.gameObject);
        ResetCamera();
        yield return null;
        Flush();
        yield return Halos();
        yield return Order();
        yield return FlareGeometry();
        yield return FlareSettings();
        yield return Fading();
        yield return Matrix();
        yield return HaloOrder();
        yield return HaloTexture();
        yield return HaloBuffers();
        yield return FlareLimits();
        yield return HaloLimits();
    }

    IEnumerator Halos()
    {
        var white = SceneObject("HaloWhite");
        white.SetActive(true);
        yield return Shot("h.white");
        DescribeFalloff("h.falloff");
        Flush();
        white.transform.position = new Vector3(1f, 0.5f, 0f);
        yield return Shot("h.white.moved");
        white.transform.localScale = new Vector3(3f, 2f, 1f);
        yield return Shot("h.white.scaled");
        white.transform.localScale = Vector3.one;
        white.transform.position = Vector3.zero;
        RenderSettings.haloStrength = 1f;
        yield return Shot("h.white.strength1");
        RenderSettings.haloStrength = 0.5f;
        cam.orthographic = true;
        cam.orthographicSize = 2f;
        yield return Shot("h.white.ortho");
        ResetCamera();
        cam.transform.rotation = Quaternion.Euler(10f, 20f, 30f);
        white.transform.position = cam.transform.position + cam.transform.rotation * new Vector3(0.5f, 0.3f, 5f);
        yield return Shot("h.white.rolled");
        ResetCamera();
        white.transform.position = Vector3.zero;
        cam.GetComponent<FlareLayer>().enabled = false;
        yield return Shot("h.white.noLayer");
        ResetCamera();
        cam.backgroundColor = new Color(0.5f, 0.25f, 0.75f, 1f);
        yield return Shot("h.white.background");
        ResetCamera();
        var wall = Occluder(new Vector3(0.5f, 0f, -0.1f), true, false);
        wall.transform.localScale = new Vector3(1f, 3f, 0.1f);
        yield return Shot("h.white.depth");
        wall.transform.position = new Vector3(-0.5f, 0f, -0.45f);
        yield return Shot("h.white.depthNear");
        Destroy(wall);
        Fog();
        yield return Shot("h.white.fog");
        ResetCamera();
        white.SetActive(false);

        var color = SceneObject("HaloColor");
        color.SetActive(true);
        yield return Shot("h.color");
        color.SetActive(false);

        var pointObject = SceneObject("HaloPoint");
        var point = pointObject.GetComponent<Light>();
        pointObject.SetActive(true);
        yield return Shot("h.point");
        point.range = 4f;
        yield return Shot("h.point.range4");
        point.range = 2f;
        point.intensity = 2f;
        yield return Shot("h.point.int2");
        point.intensity = 3f;
        RenderSettings.haloStrength = 1f;
        yield return Shot("h.point.int3s1");
        point.intensity = 1f;
        RenderSettings.haloStrength = 0.25f;
        yield return Shot("h.point.s025");
        RenderSettings.haloStrength = 0.5f;
        point.color = Color.white;
        yield return Shot("h.point.white");
        point.color = new Color(1f, 0.5f, 0.25f);
        point.enabled = false;
        yield return Shot("h.point.disabled");
        point.enabled = true;
        pointObject.SetActive(false);

        var spot = SceneObject("HaloSpot");
        spot.SetActive(true);
        yield return Shot("h.spot");
        spot.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        yield return Shot("h.spot.facing");
        spot.SetActive(false);
        var directional = SceneObject("HaloDirectional");
        directional.SetActive(true);
        yield return Shot("h.directional");
        directional.SetActive(false);
    }

    IEnumerator Order()
    {
        var white = SceneObject("HaloWhite");
        white.SetActive(true);
        var lens = Lens("S20", new Vector3(0.4f, 0.3f, 0f));
        foreach (var e in new[] { CameraEvent.AfterForwardAlpha, CameraEvent.BeforeHaloAndLensFlares, CameraEvent.AfterHaloAndLensFlares })
        {
            var cb = new CommandBuffer { name = "probe clear " + e };
            cb.ClearRenderTarget(false, true, new Color(0.25f, 0.5f, 0.125f, 1f));
            cam.AddCommandBuffer(e, cb);
            yield return Shot("o." + e);
            cam.RemoveCommandBuffer(e, cb);
            cb.Release();
        }
        lens.gameObject.SetActive(false);
        white.SetActive(false);
    }

    IEnumerator FlareGeometry()
    {
        foreach (var size in new[] { 5, 10, 20, 40 })
        {
            var lens = Lens("S" + size, Vector3.zero);
            yield return Shot("f.S" + size);
            lens.gameObject.SetActive(false);
        }
        var s20 = Lens("S20", Vector3.zero);
        cam.fieldOfView = 30f;
        yield return Shot("f.S20.fov30");
        cam.fieldOfView = 90f;
        yield return Shot("f.S20.fov90");
        ResetCamera();
        var wide = new RenderTexture(256, 128, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var tall = new RenderTexture(64, 128, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        cam.targetTexture = wide;
        for (int i = 1; i <= 2; i++) yield return new WaitForEndOfFrame();
        Read("f.S20.wide", wide, 256, 128);
        cam.targetTexture = tall;
        for (int i = 1; i <= 2; i++) yield return new WaitForEndOfFrame();
        Read("f.S20.tall", tall, 64, 128);
        Flush();
        ResetCamera();
        wide.Release(); tall.Release();
        cam.orthographic = true;
        cam.orthographicSize = 2f;
        yield return Shot("f.S20.ortho");
        ResetCamera();
        s20.transform.position = new Vector3(1f, 0.5f, 0f);
        yield return Shot("f.S20.moved");
        s20.transform.position = new Vector3(1f, 0.5f, -3f);
        yield return Shot("f.S20.near");
        cam.transform.rotation = Quaternion.Euler(10f, 20f, 30f);
        s20.transform.position = cam.transform.position + cam.transform.rotation * new Vector3(0.5f, 0.3f, 5f);
        yield return Shot("f.S20.rolled");
        ResetCamera();
        s20.gameObject.SetActive(false);

        foreach (var name in new[] { "Pp05", "Pp1", "Pp2", "Pm05", "RotP05" })
        {
            var lens = Lens(name, new Vector3(1f, 0.5f, 0f));
            yield return Shot("f." + name);
            lens.gameObject.SetActive(false);
        }
        var positions = new[] { new Vector3(1f, 0.5f, 0f), new Vector3(-1f, 0.3f, 0f), new Vector3(0f, -1f, 0f), Vector3.zero };
        for (int i = 0; i < positions.Length; i++)
        {
            var lens = Lens("Rot20", positions[i]);
            yield return Shot("f.Rot20." + i);
            lens.gameObject.SetActive(false);
        }
        for (int layout = 0; layout < LayoutImages.Length; layout++)
            for (int image = 0; image < LayoutImages[layout]; image++)
            {
                var lens = Lens("L" + layout + "_" + image, Vector3.zero);
                yield return Shot("f.L" + layout + "_" + image);
                lens.gameObject.SetActive(false);
            }
        var outside = Lens("L3_20", Vector3.zero);
        yield return Shot("f.L3_20");
        outside.gameObject.SetActive(false);
    }

    IEnumerator FlareSettings()
    {
        // The element switches while the flare fades in (3 per second, frames of 1/30 s), then at full.
        foreach (var name in new[] { "S20", "Zoom20", "Fade20" })
        {
            var lens = Lens(name, new Vector3(0.3f, 0.2f, 0f), 3f);
            yield return Shot("f." + name + ".in", 3, 1);
            yield return Shot("f." + name + ".full", 10);
            lens.gameObject.SetActive(false);
        }
        var light20 = Lens("Light20", Vector3.zero);
        light20.color = new Color(0.5f, 0.75f, 1f, 0.5f);
        yield return Shot("f.Light20");
        light20.gameObject.SetActive(false);
        var color20 = Lens("Color20", Vector3.zero);
        yield return Shot("f.Color20");
        color20.color = new Color(0.5f, 0.75f, 1f, 0.5f);
        yield return Shot("f.Color20.lensColor");
        color20.gameObject.SetActive(false);

        var s20 = Lens("S20", Vector3.zero);
        s20.color = Tint;
        yield return Shot("f.S20.color");
        s20.color = Color.white;
        s20.brightness = 0.5f;
        yield return Shot("f.S20.b05");
        s20.brightness = 2f;
        yield return Shot("f.S20.b2");
        s20.brightness = 1f;
        RenderSettings.flareStrength = 0.5f;
        yield return Shot("f.S20.strength05");
        RenderSettings.flareStrength = 2f;
        yield return Shot("f.S20.strength2");
        RenderSettings.flareStrength = 1f;
        Fog();
        yield return Shot("f.S20.fog");
        s20.flare = Resources.Load<Flare>("S20NoFog");
        yield return Shot("f.S20NoFog.fog");
        ResetCamera();
        s20.flare = Resources.Load<Flare>("S20");
        cam.GetComponent<FlareLayer>().enabled = false;
        yield return Shot("f.S20.noLayer");
        cam.GetComponent<FlareLayer>().enabled = true;
        s20.gameObject.SetActive(false);

        // A light's flare: the light's color and intensity, a directional light's direction.
        var lightObject = new GameObject("FlareLight");
        lightObject.transform.position = new Vector3(-1f, 0.5f, 0f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.5f, 0.25f);
        light.flare = Resources.Load<Flare>("Light20");
        yield return Shot("f.light.point");
        light.intensity = 2f;
        yield return Shot("f.light.point.int2");
        light.intensity = 1f;
        light.flare = Resources.Load<Flare>("Combo");
        yield return Shot("f.light.combo");
        light.flare = Resources.Load<Flare>("Light20");
        light.enabled = false;
        yield return Shot("f.light.point.disabled");
        light.enabled = true;
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.LookRotation(-new Vector3(0.2f, 0.1f, 1f));
        yield return Shot("f.light.directional");
        lightObject.transform.rotation = Quaternion.LookRotation(new Vector3(0.2f, 0.1f, 1f));
        yield return Shot("f.light.directional.behind");
        Destroy(lightObject);

        var directional = SceneObject("LensDirectional");
        directional.transform.SetPositionAndRotation(new Vector3(5f, 5f, 5f), Quaternion.LookRotation(new Vector3(0.2f, 0.1f, 1f)));
        directional.GetComponent<LensFlare>().fadeSpeed = 1000f;
        directional.SetActive(true);
        yield return Shot("f.lensDirectional.plusZ");
        directional.transform.rotation = Quaternion.LookRotation(-new Vector3(0.2f, 0.1f, 1f));
        yield return Shot("f.lensDirectional.minusZ");
        directional.SetActive(false);

        var combo = Lens("Combo", new Vector3(1f, 0.5f, 0f), 3f);
        combo.color = new Color(0.8f, 0.9f, 1f, 1f);
        yield return Shot("f.combo", 12, 3);
        combo.gameObject.SetActive(false);
    }

    IEnumerator Fading()
    {
        var lens = Lens("S20", new Vector3(1f, 0.5f, 0f), 3f);
        yield return Shot("f.fade.in", 11, 1);
        // On the line from the camera to the flare: a collider without a renderer.
        var occluder = Occluder(new Vector3(0.7f, 0.35f, -1.5f), false);
        yield return Shot("f.fade.occluded", 4, 1);
        Destroy(occluder);
        yield return Shot("f.fade.cleared", 4, 1);
        lens.fadeSpeed = 6f;
        RenderSettings.flareFadeSpeed = 1.5f;
        occluder = Occluder(new Vector3(0.7f, 0.35f, -1.5f), false);
        yield return Shot("f.speed.out", 3, 1);
        Destroy(occluder);
        yield return Shot("f.speed.in", 3, 1);
        yield return Shot("f.speed.settle", 20);
        RenderSettings.flareFadeSpeed = 3f;
        lens.fadeSpeed = 1000f;

        occluder = Occluder(new Vector3(0.7f, 0.35f, -1.5f), false);
        occluder.GetComponent<Collider>().isTrigger = true;
        yield return Shot("f.occ.trigger");
        occluder.layer = 2;
        occluder.GetComponent<Collider>().isTrigger = false;
        yield return Shot("f.occ.ignoreRaycastLayer");
        Destroy(occluder);
        var visible = Occluder(new Vector3(0.7f, 0.35f, -1.5f), true, false);
        visible.transform.localScale = Vector3.one;
        yield return Shot("f.occ.rendererOnly");
        Destroy(visible);
        yield return Shot("f.occ.none");
        lens.transform.position = new Vector3(6f, 0.5f, 0f);
        yield return Shot("f.offscreen");
        lens.transform.position = new Vector3(0f, 0f, -10f);
        yield return Shot("f.behind");
        lens.transform.position = new Vector3(1f, 0.5f, 0f);
        yield return Shot("f.back");
        lens.fadeSpeed = 3f;
        lens.gameObject.SetActive(false);
        yield return null;
        lens.gameObject.SetActive(true);
        yield return Shot("f.reactivated", 1);
        lens.gameObject.SetActive(false);

        var ignoring = Lens("S20", new Vector3(1f, 0.5f, 0f), 1000f, "LensIgnore");
        occluder = Occluder(new Vector3(0.7f, 0.35f, -1.5f), false);
        occluder.layer = 8;
        yield return Shot("f.occ.ignoredLayer");
        occluder.layer = 9;
        yield return Shot("f.occ.otherLayer");
        Destroy(occluder);
        ignoring.gameObject.SetActive(false);
    }

    IEnumerator HaloTexture()
    {
        SceneManager.LoadScene("HaloTexture", LoadSceneMode.Single);
        yield return null;
        yield return null;
        ResetCamera();
        DescribeFalloff("t.haloFalloffLoaded");
        var white = SceneObject("HaloWhite");
        white.SetActive(true);
        yield return Shot("t.white");
        DescribeFalloff("t.haloFalloff");
        white.SetActive(false);
        var point = SceneObject("HaloPoint");
        point.SetActive(true);
        yield return Shot("t.point");
        point.SetActive(false);
        Flush();
    }

    /// <summary>A scene halo with one command buffer that does not clear: empty at
    /// BeforeForwardOpaque, empty at AfterForwardAlpha, setting an unused global float at
    /// BeforeImageEffects; then without buffers again.</summary>
    IEnumerator HaloBuffers()
    {
        var white = SceneObject("HaloWhite");
        white.SetActive(true);
        yield return Shot("hb.scene");
        var cases = new (string, CameraEvent, bool)[]
        {
            ("empty", CameraEvent.BeforeForwardOpaque, false),
            ("empty", CameraEvent.AfterForwardAlpha, false),
            ("setFloat", CameraEvent.BeforeImageEffects, true),
        };
        foreach (var (kind, e, setFloat) in cases)
        {
            var cb = new CommandBuffer { name = "probe " + kind + " " + e };
            if (setFloat) cb.SetGlobalFloat("_FlareProbeUnused", 1f);
            cam.AddCommandBuffer(e, cb);
            Value("hb." + kind + "." + e + ".commandBufferCount", cam.commandBufferCount.ToString());
            yield return Shot("hb." + kind + "." + e);
            cam.RemoveCommandBuffer(e, cb);
            cb.Release();
        }
        yield return Shot("hb.sceneAfterBuffers");
        white.SetActive(false);
        Flush();
    }

    /// <summary>The four element kinds of Quad4 (plain, light color, fade, both) under each LensFlare
    /// and Light setting, at full visibility and while fading in.</summary>
    IEnumerator Matrix()
    {
        var position = new Vector3(1.5f, 1f, 0f);
        var lens = Lens("Quad4", position);
        lens.color = new Color(0.5f, 0.75f, 1f, 0.5f);
        yield return Shot("q.lens");
        lens.brightness = 0.5f;
        yield return Shot("q.lens.b05");
        lens.brightness = 2f;
        lens.color = new Color(0.5f, 0.25f, 0.125f, 1f);
        yield return Shot("q.lens.b2");
        lens.brightness = 1f;
        lens.color = new Color(0.5f, 0.75f, 1f, 0.5f);
        RenderSettings.flareStrength = 0.5f;
        yield return Shot("q.lens.strength05");
        RenderSettings.flareStrength = 1f;
        lens.gameObject.SetActive(false);
        lens.fadeSpeed = 3f;
        lens.brightness = 0.5f;
        lens.gameObject.SetActive(true);
        yield return Shot("q.lens.in", 3, 1);
        lens.gameObject.SetActive(false);

        var lightObject = new GameObject("MatrixLight");
        lightObject.transform.position = position;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.5f, 0.75f, 1f);
        light.flare = Resources.Load<Flare>("Quad4");
        yield return Shot("q.light.in", 4, 1);
        yield return Shot("q.light", 10);
        light.intensity = 0.5f;
        yield return Shot("q.light.int05");
        light.intensity = 2f;
        yield return Shot("q.light.int2");
        light.intensity = 1f;
        RenderSettings.flareStrength = 0.5f;
        yield return Shot("q.light.strength05");
        RenderSettings.flareStrength = 2f;
        yield return Shot("q.light.strength2");
        RenderSettings.flareStrength = 1f;
        light.range = 0.5f;
        yield return Shot("q.light.range05");
        light.range = 10f;
        var occluder = Occluder(Vector3.Lerp(cam.transform.position, position, 0.7f), false);
        yield return Shot("q.light.occluded", 4, 1);
        Destroy(occluder);
        yield return Shot("q.light.cleared", 3, 1);
        yield return Shot("q.light.settle", 30);
        RenderSettings.flareFadeSpeed = 6f;
        occluder = Occluder(Vector3.Lerp(cam.transform.position, position, 0.7f), false);
        yield return Shot("q.light.occluded6", 3, 1);
        Destroy(occluder);
        yield return Shot("q.light.cleared6", 3, 1);
        RenderSettings.flareFadeSpeed = 3f;
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.LookRotation(-new Vector3(0.3f, 0.2f, 1f));
        yield return Shot("q.directional", 10);
        light.intensity = 0.5f;
        yield return Shot("q.directional.int05");
        Destroy(lightObject);
        Flush();
    }

    /// <summary>A Halo added by script (its defaults), also reactivated; then a scene halo alone,
    /// with a command buffer clearing the target at each event around the halos, and with a
    /// flare.</summary>
    IEnumerator HaloOrder()
    {
        var go = new GameObject("ScriptHalo");
        var halo = go.AddComponent(typeof(Light).Assembly.GetType("UnityEngine.Halo"));
        Value("ho.component", Q(halo == null ? "null" : halo.GetType().FullName));
        yield return Shot("ho.added");
        go.SetActive(false);
        yield return null;
        go.SetActive(true);
        yield return Shot("ho.reactivated");
        Destroy(go);
        var white = SceneObject("HaloWhite");
        white.SetActive(true);
        yield return Shot("ho.scene");
        foreach (var e in new[] { CameraEvent.AfterForwardAlpha, CameraEvent.BeforeHaloAndLensFlares, CameraEvent.AfterHaloAndLensFlares })
        {
            var cb = new CommandBuffer { name = "probe clear " + e };
            cb.ClearRenderTarget(false, true, new Color(0.25f, 0.5f, 0.125f, 1f));
            cam.AddCommandBuffer(e, cb);
            yield return Shot("ho." + e);
            cam.RemoveCommandBuffer(e, cb);
            cb.Release();
        }
        yield return Shot("ho.sceneAfterBuffers");
        var lens = Lens("S20", new Vector3(0.4f, 0.3f, 0f));
        yield return Shot("ho.withFlare");
        lens.gameObject.SetActive(false);
        yield return Shot("ho.sceneAfterFlare");
        white.SetActive(false);
        Flush();
    }

    /// <summary>Flares at their limits: a zooming element under other brightnesses (a LensFlare's,
    /// also while it fades in, and a light's flare under flareStrength), a flare beyond the far
    /// plane and one nearer than the near plane, a directional flare 50 and 70 degrees off the
    /// view axis of a camera that sees 75 degrees to each side, and elements under a lens shift of
    /// a quarter of the width.</summary>
    IEnumerator FlareLimits()
    {
        // Back to the first scene, whose objects the flare cases use.
        SceneManager.LoadScene("Probe", LoadSceneMode.Single);
        yield return null;
        yield return null;
        ResetCamera();
        var zoom = Lens("Zoom20", new Vector3(0.3f, 0.2f, 0f));
        zoom.brightness = 0.5f;
        yield return Shot("fl.zoom.b05");
        zoom.brightness = 2f;
        yield return Shot("fl.zoom.b2");
        zoom.gameObject.SetActive(false);
        zoom = Lens("Zoom20", new Vector3(0.3f, 0.2f, 0f), 3f);
        zoom.brightness = 2f;
        yield return Shot("fl.zoom.b2.in", 3, 1);
        zoom.gameObject.SetActive(false);

        var lightObject = new GameObject("LimitLight");
        lightObject.transform.position = new Vector3(-1f, 0.5f, 0f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.flare = Resources.Load<Flare>("Zoom20");
        RenderSettings.flareFadeSpeed = 1000f;
        RenderSettings.flareStrength = 0.5f;
        yield return Shot("fl.light.zoom.strength05");
        RenderSettings.flareStrength = 2f;
        yield return Shot("fl.light.zoom.strength2");
        Destroy(lightObject);
        ResetCamera();

        // The camera is at z -5 with its planes at 0.3 and 100.
        var lens = Lens("S20", new Vector3(10f, 5f, 195f));
        yield return Shot("fl.beyondFar");
        lens.transform.position = new Vector3(0.01f, 0.005f, -4.8f);
        yield return Shot("fl.insideNear");
        lens.gameObject.SetActive(false);

        var directional = SceneObject("LensDirectional");
        cam.fieldOfView = 150f;
        directional.GetComponent<LensFlare>().fadeSpeed = 1000f;
        directional.transform.SetPositionAndRotation(new Vector3(5f, 5f, 5f), Quaternion.LookRotation(-(Quaternion.Euler(0f, 50f, 0f) * Vector3.forward)));
        directional.SetActive(true);
        yield return Shot("fl.directional.off50");
        directional.transform.rotation = Quaternion.LookRotation(-(Quaternion.Euler(0f, 70f, 0f) * Vector3.forward));
        yield return Shot("fl.directional.off70");
        directional.SetActive(false);
        ResetCamera();

        // An off-centre projection: the view axis meets the screen a quarter of its width from
        // its centre, so "towards the centre" and "towards the axis" are different places.
        cam.usePhysicalProperties = true;
        cam.lensShift = new Vector2(0.25f, 0f);
        Value("fl.shift.projection", "[" + F(cam.projectionMatrix.m00) + "," + F(cam.projectionMatrix.m02) + "," + F(cam.projectionMatrix.m11) + "," + F(cam.projectionMatrix.m12) + "]");
        foreach (var name in new[] { "Pp05", "Pp1", "RotP05" })
        {
            var shifted = Lens(name, new Vector3(1f, 0.5f, 0f));
            yield return Shot("fl.shift." + name);
            shifted.gameObject.SetActive(false);
        }
        cam.lensShift = Vector2.zero;
        cam.usePhysicalProperties = false;
        ResetCamera();
        Flush();
    }
    IEnumerator HaloLimits()
    {
        ResetCamera();
        RenderSettings.haloStrength = 1f;
        var pointObject = SceneObject("HaloPoint");
        pointObject.SetActive(true);
        var light = pointObject.GetComponent<Light>();
        light.range = 2f;
        light.intensity = 1f;
        light.color = new Color(1f, 0.5f, 0.25f);
        foreach (float depth in new[] { 1f, 2f, 2.5f, 3f, 4f, 5f })
        {
            pointObject.transform.position = cam.transform.position + new Vector3(0f, 0f, depth);
            yield return Shot("hl.depth" + F(depth));
        }
        cam.orthographic = true;
        cam.orthographicSize = 3f;
        pointObject.transform.position = cam.transform.position + new Vector3(0.3f, 0.2f, 3f);
        yield return Shot("hl.orthoFade");
        ResetCamera();
        pointObject.SetActive(false);
        Flush();
    }

}
