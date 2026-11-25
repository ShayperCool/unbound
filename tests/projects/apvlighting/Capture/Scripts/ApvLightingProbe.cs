// The probe runs in the Editor only (it ends Play mode through the Editor).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Objects lit by the scene's baked Adaptive Probe Volume, each group seen by cameras of its own
/// that render into small textures, saved as images with a few rows of the probe volume's state
/// (see README.md): into APV_PROBE_OUT, then exits Play mode.
/// </summary>
public sealed class ApvLightingProbe : MonoBehaviour
{
    /// <summary>The pipeline asset's second renderer: the forward one in Forward+ mode.</summary>
    const int k_ForwardPlusRenderer = 1;
    const int k_Size = 96;
    // The groups' layers: each camera sees the room (Default) and its own group.
    const int k_GridLayer = 8;
    const int k_ShadersLayer = 9;
    const int k_ParticlesLayer = 10;
    const int k_SimpleParticlesLayer = 11;
    /// <summary>The volume layer of the options stage's volume.</summary>
    const int k_OptionsVolumeLayer = 12;
    /// <summary>The volume layer of the shaders options stage's volume.</summary>
    const int k_MoreOptionsVolumeLayer = 13;
    static readonly int[] k_Frames = { 4, 20 };

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<(string key, Camera camera)> m_Stages = new List<(string, Camera)>();
    Texture2D m_Shot;
    int m_Frame;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("ApvLightingProbe").AddComponent<ApvLightingProbe>();
    }

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value ?? "<null>")).Append("\"}\n");

    static Material Mat(string shader, Color color)
    {
        var found = Shader.Find(shader);
        if (found == null) throw new InvalidOperationException("no shader " + shader);
        var material = new Material(found);
        material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        return material;
    }

    static GameObject Sphere(string name, Vector3 position, Material material, int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.layer = layer;
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.7f;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>A camera at position looking at target that sees the room and the groups of
    /// layers; rendered into a texture of its own.</summary>
    Camera Stage(string key, Vector3 position, Vector3 target, float fov, params int[] layers)
    {
        var camera = new GameObject("camera " + key).AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.LookRotation(target - position);
        camera.fieldOfView = fov;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 50f;
        camera.clearFlags = CameraClearFlags.Skybox;
        int mask = 1 << 0;
        foreach (int layer in layers) mask |= 1 << layer;
        camera.cullingMask = mask;
        camera.targetTexture = new RenderTexture(k_Size, k_Size, 24);
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        m_Stages.Add((key, camera));
        return camera;
    }

    /// <summary>A global volume on its own layer with a ProbeVolumesOptions override.</summary>
    static ProbeVolumesOptions Options(string name, int layer)
    {
        var volume = new GameObject(name) { layer = layer }.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var options = profile.Add<ProbeVolumesOptions>(true);
        volume.sharedProfile = profile;
        return options;
    }

    ParticleSystem Particles(string name, Material material, int layer, IEnumerable<Vector3> positions)
    {
        var go = new GameObject(name) { layer = layer };
        var system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startSpeed = 0f;
        main.startLifetime = 1000f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        foreach (var position in positions)
            system.Emit(new ParticleSystem.EmitParams { position = position, startSize = 0.6f, startColor = Color.white, startLifetime = 1000f, velocity = Vector3.zero }, 1);
        return system;
    }

    static IEnumerable<Vector3> Grid(float z)
    {
        foreach (float y in new[] { 0.5f, 1.5f, 2.5f })
            foreach (float x in new[] { -3f, -1.5f, 0f, 1.5f, 3f })
                yield return new Vector3(x, y, z);
    }

    void Start()
    {
        try
        {
            if (Camera.main != null) Camera.main.enabled = false;
            m_Shot = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);

            // A grid of matte Lit spheres across the room: under the roof by the red wall, in
            // the sun by the green one, from the floor up to the roof's height.
            var lit = Mat("Universal Render Pipeline/Lit", new Color(0.8f, 0.8f, 0.8f, 1f));
            int n = 0;
            foreach (var position in Grid(0f))
                Sphere("grid " + n++, position, lit, k_GridLayer);
            var eye = new Vector3(0f, 1.5f, -7.5f);
            var at = new Vector3(0f, 1.5f, 0f);
            Stage("grid forward", eye, at, 55f, k_GridLayer);
            Stage("grid forward plus", eye, at, 55f, k_GridLayer).GetUniversalAdditionalCameraData().SetRenderer(k_ForwardPlusRenderer);

            // The grid again through a volume of its own that overrides five probe volume
            // options (no sampling noise, larger biases, no leak reduction, a lower intensity).
            var options = Stage("grid options", eye, at, 55f, k_GridLayer);
            options.GetUniversalAdditionalCameraData().volumeLayerMask = 1 << k_OptionsVolumeLayer;
            var overrides = Options("options volume", k_OptionsVolumeLayer);
            overrides.normalBias.Override(0.4f);
            overrides.viewBias.Override(0.6f);
            overrides.samplingNoise.Override(0f);
            overrides.leakReductionMode.Override(APVLeakReductionMode.None);
            overrides.intensityMultiplier.Override(0.5f);

            // Other lit shaders in a row by the back wall, the room's own walls and floor
            // behind them: a smooth Lit sphere (reflections), a Lit sphere that does not use
            // probes, and Unlit as control. All six whole in the view.
            float x = -2.75f;
            Sphere("simple lit", new Vector3(x, 1.2f, 2.5f), Mat("Universal Render Pipeline/Simple Lit", new Color(0.8f, 0.8f, 0.8f, 1f)), k_ShadersLayer);
            Sphere("complex lit", new Vector3(x += 1.1f, 1.2f, 2.5f), Mat("Universal Render Pipeline/Complex Lit", new Color(0.8f, 0.8f, 0.8f, 1f)), k_ShadersLayer);
            Sphere("baked lit", new Vector3(x += 1.1f, 1.2f, 2.5f), Mat("Universal Render Pipeline/Baked Lit", new Color(0.8f, 0.8f, 0.8f, 1f)), k_ShadersLayer);
            var smooth = Mat("Universal Render Pipeline/Lit", new Color(0.8f, 0.8f, 0.8f, 1f));
            smooth.SetFloat("_Smoothness", 0.9f);
            Sphere("smooth lit", new Vector3(x += 1.1f, 1.2f, 2.5f), smooth, k_ShadersLayer);
            var off = Sphere("lit probes off", new Vector3(x += 1.1f, 1.2f, 2.5f), lit, k_ShadersLayer);
            off.GetComponent<MeshRenderer>().lightProbeUsage = LightProbeUsage.Off;
            Sphere("unlit", new Vector3(x += 1.1f, 1.2f, 2.5f), Mat("Universal Render Pipeline/Unlit", new Color(0.8f, 0.8f, 0.8f, 1f)), k_ShadersLayer);
            var near = new Vector3(0f, 1.6f, -2.5f);
            var row = new Vector3(0f, 1.2f, 2.5f);
            Stage("shaders forward", near, row, 70f, k_ShadersLayer);
            Stage("shaders forward plus", near, row, 70f, k_ShadersLayer).GetUniversalAdditionalCameraData().SetRenderer(k_ForwardPlusRenderer);

            // The row through a volume that overrides the other options URP passes to the
            // shaders: the sampling position offset, the biases scaled by the probe spacing, the
            // reflection normalization's upper clamp (no URP 17.0.4 shader reads it), and no
            // animated noise.
            var shaderOptions = Stage("shaders options", near, row, 70f, k_ShadersLayer);
            shaderOptions.GetUniversalAdditionalCameraData().volumeLayerMask = 1 << k_MoreOptionsVolumeLayer;
            var more = Options("more options volume", k_MoreOptionsVolumeLayer);
            more.worldOffset.Override(new Vector3(0.4f, 0f, 0.3f));
            more.scaleBiasWithMinProbeDistance.Override(true);
            more.occlusionOnlyReflectionNormalization.Override(false);
            more.animateSamplingNoise.Override(false);

            // Lit and Simple Lit particles (billboards) where the grid's spheres are.
            Particles("lit particles", Mat("Universal Render Pipeline/Particles/Lit", Color.white), k_ParticlesLayer, Grid(-1f));
            Particles("simple lit particles", Mat("Universal Render Pipeline/Particles/Simple Lit", Color.white), k_SimpleParticlesLayer, Grid(-1f));
            Stage("particles forward", eye, at, 55f, k_ParticlesLayer);
            Stage("particles forward plus", eye, at, 55f, k_ParticlesLayer).GetUniversalAdditionalCameraData().SetRenderer(k_ForwardPlusRenderer);
            Stage("simple particles forward", eye, at, 55f, k_SimpleParticlesLayer);

            // The room alone from a corner: static receivers lit by the volume.
            Stage("room", new Vector3(3.5f, 2.6f, -3.8f), new Vector3(-1f, 1f, 2f), 80f);
        }
        catch (Exception e)
        {
            Debug.LogError("apvlighting probe: setup failed: " + e);
            m_Done = true;
            EditorApplication.ExitPlaymode();
            EditorApplication.Exit(1);
        }
    }

    void Shoot(int frame)
    {
        var dir = Environment.GetEnvironmentVariable("APV_PROBE_OUT");
        var active = RenderTexture.active;
        foreach (var (key, camera) in m_Stages)
        {
            RenderTexture.active = camera.targetTexture;
            m_Shot.ReadPixels(new Rect(0f, 0f, k_Size, k_Size), 0, 0);
            m_Shot.Apply();
            if (!string.IsNullOrEmpty(dir))
                File.WriteAllBytes(Path.Combine(dir, key.Replace(' ', '-') + "-" + frame + ".png"), m_Shot.EncodeToPNG());
        }
        RenderTexture.active = active;
    }

    void Update()
    {
        if (m_Done) return;
        m_Frame++;
        if (m_Frame == k_Frames[0])
        {
            var volume = ProbeReferenceVolume.instance;
            Rec("probe volume initialized", volume.isInitialized.ToString());
            Rec("probe volume data loaded", volume.DataHasBeenLoaded().ToString());
            Rec("probe volume sh bands", volume.shBands.ToString());
            Rec("probe volume video memory", volume.GetVideoMemoryCost().ToString());
        }
        // The textures hold what the cameras rendered in the frame before this one.
        if (Array.IndexOf(k_Frames, m_Frame) >= 0) Shoot(m_Frame);
        if (m_Frame < k_Frames[k_Frames.Length - 1]) return;
        m_Done = true;
        m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("APV_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }
}
#endif
