using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders small scenes lit by the built-in pipeline's forward lighting: point, spot and directional
/// lights as pixel lights (ForwardAdd), vertex lights and spherical-harmonics lights, light render
/// modes and the quality level's pixel light count, cookies, with the Standard, Legacy Diffuse and
/// VertexLit shaders. Each case is a PNG in the directory <c>LIGHTING_PROBE_OUT</c>; then it quits.
/// The Linux Editor's images are the reference.
/// </summary>
public class LightingProbe : MonoBehaviour
{
    const int Width = 200, Height = 150;
    readonly List<GameObject> m_Case = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("LightingProbe").AddComponent<LightingProbe>();

    GameObject Add(GameObject go)
    {
        m_Case.Add(go);
        return go;
    }

    Material Standard(Color color, float smoothness = 0.5f)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = color;
        m.SetFloat("_Glossiness", smoothness);
        return m;
    }

    /// <summary>A ground plane, two spheres and a cube, all with the material.</summary>
    void Stage(Material material)
    {
        var ground = Add(GameObject.CreatePrimitive(PrimitiveType.Plane));
        ground.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        ground.GetComponent<Renderer>().sharedMaterial = material;
        foreach (var (type, position) in new[]
        {
            (PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f)),
            (PrimitiveType.Sphere, new Vector3(1f, 0.5f, 0f)),
            (PrimitiveType.Cube, new Vector3(0f, 0.5f, 1f)),
        })
        {
            var go = Add(GameObject.CreatePrimitive(type));
            go.transform.position = position;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
    }

    Light Point(Vector3 position, Color color, float range, float intensity, LightRenderMode mode = LightRenderMode.Auto)
    {
        var go = Add(new GameObject("point"));
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.renderMode = mode;
        return l;
    }

    Light Spot(Vector3 position, Vector3 target, float angle, float range, float intensity, Color color)
    {
        var go = Add(new GameObject("spot"));
        go.transform.position = position;
        go.transform.LookAt(target);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot;
        l.spotAngle = angle;
        l.range = range;
        l.intensity = intensity;
        l.color = color;
        return l;
    }

    Light Directional(Vector3 euler, Color color, float intensity)
    {
        var go = Add(new GameObject("directional"));
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        return l;
    }

    void Capture(string dir, string name)
    {
        var go = Add(new GameObject("camera"));
        go.transform.position = new Vector3(0f, 2.2f, -4.5f);
        go.transform.LookAt(new Vector3(0f, 0.3f, 0.3f));
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
        DestroyImmediate(target);
        foreach (var o in m_Case) DestroyImmediate(o);
        m_Case.Clear();
        QualitySettings.pixelLightCount = 4;
    }

    /// <summary>Writes the built-in shadow variables the forward base pass sees (Probe/GlobalsDump)
    /// while a shadowed sun lights the stage: cascades -1 keeps the project's.</summary>
    void DumpGlobals(string dir, string name, LightShadows shadows, int cascades, float normalBias, float bias)
    {
        int savedCascades = QualitySettings.shadowCascades;
        if (cascades > 0) QualitySettings.shadowCascades = cascades;
        Stage(new Material(Shader.Find("Standard")) { color = Color.white });
        var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        sun.shadows = shadows;
        sun.shadowNormalBias = normalBias;
        sun.shadowBias = bias;
        var quad = Add(GameObject.CreatePrimitive(PrimitiveType.Quad));
        quad.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Probe/GlobalsDump"));
        quad.transform.position = new Vector3(0f, 0.3f, 0.3f);
        var go = Add(new GameObject("camera"));
        go.transform.position = new Vector3(0f, 2.2f, -4.5f);
        go.transform.LookAt(new Vector3(0f, 0.3f, 0.3f));
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, 1, TextureFormat.RGBAFloat, false);
        texture.ReadPixels(new Rect(0, 0, Width, 1), 0, 0);
        RenderTexture.active = null;
        camera.targetTexture = null;
        var dump = new System.Text.StringBuilder();
        var px = texture.GetPixels();
        for (int i = 0; i < 29; i++)
            dump.Append(i).Append(' ').Append(px[i].r.ToString("R")).Append(' ').Append(px[i].g.ToString("R")).Append(' ').Append(px[i].b.ToString("R")).Append(' ').Append(px[i].a.ToString("R")).Append('\n');
        File.WriteAllText(Path.Combine(dir, name + ".txt"), dump.ToString());
        DestroyImmediate(target);
        foreach (var o in m_Case) DestroyImmediate(o);
        m_Case.Clear();
        QualitySettings.shadowCascades = savedCascades;
    }

    /// <summary>Writes the built-in shadow variables the ForwardAdd pass sees (Probe/GlobalsDumpAdd)
    /// while a shadowed spot or point light lights the stage.</summary>
    void DumpAddGlobals(string dir, string name, System.Func<Light> make, LightShadows shadows, float normalBias, float bias)
    {
        Stage(new Material(Shader.Find("Standard")) { color = Color.white });
        var light = make();
        light.shadows = shadows;
        light.shadowNormalBias = normalBias;
        light.shadowBias = bias;
        light.renderMode = LightRenderMode.ForcePixel;
        var quad = Add(GameObject.CreatePrimitive(PrimitiveType.Quad));
        quad.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Probe/GlobalsDumpAdd"));
        quad.transform.position = new Vector3(0f, 0.3f, 0.3f);
        var go = Add(new GameObject("camera"));
        go.transform.position = new Vector3(0f, 2.2f, -4.5f);
        go.transform.LookAt(new Vector3(0f, 0.3f, 0.3f));
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, 1, TextureFormat.RGBAFloat, false);
        texture.ReadPixels(new Rect(0, 0, Width, 1), 0, 0);
        RenderTexture.active = null;
        camera.targetTexture = null;
        var dump = new System.Text.StringBuilder();
        var px = texture.GetPixels();
        for (int i = 0; i < 19; i++)
            dump.Append(i).Append(' ').Append(px[i].r.ToString("R")).Append(' ').Append(px[i].g.ToString("R")).Append(' ').Append(px[i].b.ToString("R")).Append(' ').Append(px[i].a.ToString("R")).Append('\n');
        File.WriteAllText(Path.Combine(dir, name + ".txt"), dump.ToString());
        DestroyImmediate(target);
        foreach (var o in m_Case) DestroyImmediate(o);
        m_Case.Clear();
    }

    /// <summary>An orthographic view straight down on the ground, 4 units across.</summary>
    void CaptureTop(string dir, string name)
    {
        var go = Add(new GameObject("camera"));
        go.transform.position = new Vector3(0f, 5f, 0f);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var target = new RenderTexture(200, 200, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(200, 200, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, 200, 200), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
        DestroyImmediate(target);
        foreach (var o in m_Case) DestroyImmediate(o);
        m_Case.Clear();
    }

    static Texture2D Checker()
    {
        var t = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                bool on = ((x / 8) + (y / 8)) % 2 == 0;
                float edge = Mathf.Clamp01(1f - new Vector2(x - 15.5f, y - 15.5f).magnitude / 16f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, on ? edge : 0.3f * edge));
            }
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        return t;
    }

    void Start()
    {
        string dir = Environment.GetEnvironmentVariable("LIGHTING_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            var info = new System.Text.StringBuilder();
            void Probe(string label)
            {
                var sh = RenderSettings.ambientProbe;
                info.Append(label).Append(": mode ").Append(RenderSettings.ambientMode).Append(" light ").Append(RenderSettings.ambientLight)
                    .Append(" sh ").Append(sh[0, 0].ToString("R")).Append(' ').Append(sh[1, 0].ToString("R")).Append(' ').Append(sh[2, 0].ToString("R"))
                    .Append(' ').Append(sh[0, 1].ToString("R")).Append(' ').Append(sh[0, 6].ToString("R")).Append('\n');
            }
            Probe("start");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.05f);
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.skybox = null;
            Probe("flat set");
            File.WriteAllText(Path.Combine(dir, "ambient.txt"), info.ToString());
            var white = Standard(Color.white);

            Stage(white);
            Capture(dir, "unlit_ambient");

            Stage(white);
            Point(new Vector3(0f, 1.5f, -0.5f), Color.white, 4f, 1.5f);
            Capture(dir, "point");

            Stage(white);
            Point(new Vector3(1.5f, 0.8f, -1f), new Color(1f, 0.6f, 0.2f), 2f, 2f);
            Capture(dir, "point_range");

            Stage(white);
            Spot(new Vector3(0f, 3f, -1f), new Vector3(0f, 0f, 0.5f), 50f, 6f, 2f, Color.white);
            Capture(dir, "spot");

            Stage(white);
            Point(new Vector3(-1.2f, 1f, -0.8f), new Color(1f, 0.2f, 0.2f), 3f, 2f);
            Point(new Vector3(1.2f, 1f, -0.8f), new Color(0.2f, 0.3f, 1f), 3f, 2f);
            Capture(dir, "two_points");

            Stage(white);
            for (int i = 0; i < 5; i++)
                Point(new Vector3(-2f + i, 0.8f, -0.6f), Color.HSVToRGB(i / 5f, 0.7f, 1f), 2f, 1.2f + i * 0.1f);
            Capture(dir, "five_points");

            Stage(white);
            Point(new Vector3(-1f, 1f, -0.8f), new Color(1f, 0.3f, 0.3f), 3f, 2f, LightRenderMode.ForceVertex);
            Point(new Vector3(1f, 1f, -0.8f), new Color(0.3f, 1f, 0.3f), 3f, 2f, LightRenderMode.ForceVertex);
            Capture(dir, "not_important");

            Stage(white);
            QualitySettings.pixelLightCount = 1;
            Point(new Vector3(-1.2f, 1f, -0.8f), new Color(1f, 0.2f, 0.2f), 3f, 2.5f);
            Point(new Vector3(0f, 1f, -0.8f), new Color(0.2f, 1f, 0.2f), 3f, 1f, LightRenderMode.ForcePixel);
            Point(new Vector3(1.2f, 1f, -0.8f), new Color(0.2f, 0.3f, 1f), 3f, 2f);
            Capture(dir, "pixel_light_count");

            Stage(white);
            Directional(new Vector3(50f, -30f, 0f), new Color(1f, 0.95f, 0.85f), 0.6f);
            Point(new Vector3(0f, 1.2f, -0.6f), new Color(0.3f, 0.6f, 1f), 3f, 2f);
            Capture(dir, "directional_point");

            Stage(white);
            Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
            Directional(new Vector3(30f, 120f, 0f), new Color(1f, 0.4f, 0.2f), 0.6f);
            Capture(dir, "two_directionals");

            Stage(new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = Color.white });
            Point(new Vector3(0f, 1.5f, -0.5f), Color.white, 4f, 1.5f);
            Point(new Vector3(1.5f, 0.8f, -1f), new Color(1f, 0.6f, 0.2f), 2f, 2f);
            Capture(dir, "legacy_diffuse");

            Stage(new Material(Shader.Find("Legacy Shaders/VertexLit")) { color = Color.white });
            Point(new Vector3(0f, 1.5f, -0.5f), Color.white, 4f, 1.5f);
            Point(new Vector3(1.5f, 0.8f, -1f), new Color(1f, 0.6f, 0.2f), 2f, 2f);
            Capture(dir, "vertexlit");

            Stage(white);
            var cookie = Spot(new Vector3(0f, 3f, -1f), new Vector3(0f, 0f, 0.5f), 60f, 6f, 2f, Color.white);
            cookie.cookie = Checker();
            Capture(dir, "spot_cookie");

            // The shadow variables while a shadowed sun lights the stage, as the project sets it
            // up, then with one or four cascades, hard or soft, and other biases.
            DumpGlobals(dir, "shadow_globals", LightShadows.Soft, -1, 0.4f, 0.05f);
            DumpGlobals(dir, "shadow_globals_hard1", LightShadows.Hard, 1, 0.4f, 0.05f);
            DumpGlobals(dir, "shadow_globals_soft1", LightShadows.Soft, 1, 0.4f, 0.05f);
            DumpGlobals(dir, "shadow_globals_hard4", LightShadows.Hard, 4, 0.4f, 0.05f);
            DumpGlobals(dir, "shadow_globals_soft4_biased", LightShadows.Soft, 4, 1f, 0.2f);
            DumpGlobals(dir, "shadow_globals_hard2", LightShadows.Hard, 2, 0.4f, 0.05f);
            // The same for spot and point lights' ForwardAdd passes.
            DumpAddGlobals(dir, "shadow_globals_spot_hard", () => Spot(new Vector3(-1.5f, 3f, -1.5f), new Vector3(0.3f, 0f, 0.5f), 70f, 8f, 3f, Color.white), LightShadows.Hard, 0.4f, 0.05f);
            DumpAddGlobals(dir, "shadow_globals_spot_soft", () => Spot(new Vector3(-1.5f, 3f, -1.5f), new Vector3(0.3f, 0f, 0.5f), 70f, 8f, 3f, Color.white), LightShadows.Soft, 0.4f, 0.05f);
            DumpAddGlobals(dir, "shadow_globals_spot_narrow", () => Spot(new Vector3(0.5f, 4f, -2f), new Vector3(0f, 0f, 0f), 30f, 12f, 3f, Color.white), LightShadows.Soft, 1f, 0.2f);
            DumpAddGlobals(dir, "shadow_globals_point_hard", () => Point(new Vector3(0.2f, 1.6f, -0.9f), Color.white, 5f, 2f), LightShadows.Hard, 0.4f, 0.05f);
            DumpAddGlobals(dir, "shadow_globals_point_soft", () => Point(new Vector3(0.2f, 1.6f, -0.9f), Color.white, 5f, 2f), LightShadows.Soft, 0.4f, 0.05f);
            DumpAddGlobals(dir, "shadow_globals_point_biased", () => Point(new Vector3(-0.5f, 2.5f, 0.5f), Color.white, 10f, 2f), LightShadows.Hard, 1f, 0.2f);

            // Shadows: directional (hard, soft), spot and point, onto the ground.
            foreach (var (name, shadows) in new[] { ("shadow_directional_hard", LightShadows.Hard), ("shadow_directional_soft", LightShadows.Soft) })
            {
                Stage(white);
                var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
                sun.shadows = shadows;
                // Also the screen-space shadow mask the forward pass reads, through a light command
                // buffer (CurrentActive is the mask after it is collected).
                var mask = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
                var cmd = new CommandBuffer();
                cmd.Blit(BuiltinRenderTextureType.CurrentActive, mask);
                sun.AddCommandBuffer(LightEvent.AfterScreenspaceMask, cmd);
                Capture(dir, name);
                if (mask != null)
                {
                    RenderTexture.active = mask;
                    var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                    texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, name + "_mask.png"), texture.EncodeToPNG());
                    DestroyImmediate(mask);
                }
            }
            {
                // A long ground under a low sun: the cascades along the view.
                var ground = Add(GameObject.CreatePrimitive(PrimitiveType.Plane));
                ground.transform.localScale = new Vector3(3f, 1f, 6f);
                ground.transform.position = new Vector3(0f, 0f, 25f);
                ground.GetComponent<Renderer>().sharedMaterial = white;
                for (int i = 0; i < 12; i++)
                {
                    var pole = Add(GameObject.CreatePrimitive(PrimitiveType.Cube));
                    pole.transform.position = new Vector3((i % 2 == 0 ? -1.5f : 1.5f), 1f, i * 4f);
                    pole.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
                    pole.GetComponent<Renderer>().sharedMaterial = white;
                }
                var sun = Directional(new Vector3(30f, 60f, 0f), Color.white, 1f);
                sun.shadows = LightShadows.Hard;
                Capture(dir, "shadow_cascades");
            }
            {
                Stage(white);
                var spot = Spot(new Vector3(-1.5f, 3f, -1.5f), new Vector3(0.3f, 0f, 0.5f), 70f, 8f, 3f, Color.white);
                spot.shadows = LightShadows.Hard;
                Capture(dir, "shadow_spot");
            }
            {
                // Two shadowed suns: the second one's ForwardAdd pass reads its own mask.
                Stage(white);
                var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 0.7f);
                sun.shadows = LightShadows.Hard;
                var second = Directional(new Vector3(35f, 60f, 0f), new Color(1f, 0.7f, 0.4f), 0.6f);
                second.shadows = LightShadows.Soft;
                Capture(dir, "shadow_two_directionals");
            }
            {
                Stage(white);
                var point = Point(new Vector3(0.2f, 1.6f, -0.9f), Color.white, 5f, 2f);
                point.shadows = LightShadows.Hard;
                Capture(dir, "shadow_point");
            }

            {
                // A ShadowsOnly cube: its shadow is drawn, the cube is not.
                Stage(white);
                var cube = Add(GameObject.CreatePrimitive(PrimitiveType.Cube));
                cube.transform.position = new Vector3(0.2f, 1.3f, -0.9f);
                cube.transform.localScale = Vector3.one * 0.6f;
                var renderer = cube.GetComponent<Renderer>();
                renderer.sharedMaterial = Standard(Color.red);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
                sun.shadows = LightShadows.Hard;
                Capture(dir, "shadow_shadows_only");
            }
            {
                // Two quads turned away from the sun (and from the camera, which sees their backs
                // culled): a TwoSided one shades the ground, a one-sided one does not.
                var ground = Add(GameObject.CreatePrimitive(PrimitiveType.Plane));
                ground.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                ground.GetComponent<Renderer>().sharedMaterial = white;
                foreach (var (x, mode) in new[] { (-0.8f, UnityEngine.Rendering.ShadowCastingMode.TwoSided), (0.8f, UnityEngine.Rendering.ShadowCastingMode.On) })
                {
                    var quad = Add(GameObject.CreatePrimitive(PrimitiveType.Quad));
                    quad.transform.position = new Vector3(x, 0.8f, 0f);
                    quad.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                    quad.transform.localScale = Vector3.one * 0.9f;
                    var renderer = quad.GetComponent<Renderer>();
                    renderer.sharedMaterial = white;
                    renderer.shadowCastingMode = mode;
                }
                var sun = Directional(new Vector3(70f, -20f, 0f), Color.white, 1f);
                sun.shadows = LightShadows.Hard;
                Capture(dir, "shadow_two_sided");
            }

            // The light textures themselves, seen from above (2 units to each side).
            foreach (var (name, spot) in new[] { ("textures_point", false), ("textures_spot", true) })
            {
                var view = new Material(Shader.Find("Probe/AttenuationView"));
                var ground = Add(GameObject.CreatePrimitive(PrimitiveType.Plane));
                ground.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
                ground.GetComponent<Renderer>().sharedMaterial = view;
                if (spot) Spot(new Vector3(0f, 1f, 0f), Vector3.zero, 120f, 3f, 1f, Color.white);
                else Point(new Vector3(0f, 0f, 0f), Color.white, 2f, 1f, LightRenderMode.ForcePixel);
                CaptureTop(dir, name);
            }

            // A wide plane under a low light: the falloff along the ground.
            {
                var ground = Add(GameObject.CreatePrimitive(PrimitiveType.Plane));
                ground.transform.localScale = new Vector3(2f, 1f, 2f);
                ground.GetComponent<Renderer>().sharedMaterial = Standard(Color.white, 0f);
                Point(new Vector3(0f, 0.05f, 0.5f), Color.white, 3f, 1f);
                Capture(dir, "falloff_point");
            }
            {
                var ground = Add(GameObject.CreatePrimitive(PrimitiveType.Plane));
                ground.transform.localScale = new Vector3(2f, 1f, 2f);
                ground.GetComponent<Renderer>().sharedMaterial = Standard(Color.white, 0f);
                Spot(new Vector3(0f, 2f, 0.5f), new Vector3(0f, 0f, 0.5f), 90f, 4f, 1f, Color.white);
                Capture(dir, "falloff_spot");
            }
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
