using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The Graphics settings' built-in shader settings (GraphicsSettings.SetShaderMode and
// SetCustomShader) as the built-in renderer applies them: for each setting the renderer uses
// (deferred shading, deferred reflections, screen-space shadows, depth-normals, motion vectors,
// light halo, lens flare) an image with the built-in shader, with a custom one (Assets/Shaders/
// Settings*.shader: a constant color each, so an image shows where it draws), in custom mode
// without a shader, and with No Support; the mode and custom shader read back and what the
// render logs (settings_<setting>_<mode>.png, settings.<setting>.<mode>.*).
public partial class FxProbe
{
    static readonly string[] SettingModes = { "builtin", "custom", "customNull", "disabled" };
    // Settings no earlier part of the probe renders with: the modes without a shader first.
    static readonly string[] UnusedSettingModes = { "customNull", "disabled", "builtin", "custom" };

    void ShaderSettings()
    {
        var logs = new List<string>();
        void OnLog(string condition, string stack, LogType type) => logs.Add(type + ": " + condition);
        Application.logMessageReceived += OnLog;
        try
        {
            Setting("deferred", BuiltinShaderType.DeferredShading, "Probe/Settings/Deferred", logs, () =>
            {
                PathStage();
                var camera = MakeCamera();
                camera.renderingPath = RenderingPath.DeferredShading;
                return camera;
            }, null, SettingModes);
            Setting("deferredLdr", BuiltinShaderType.DeferredShading, "Probe/Settings/Deferred", logs, () =>
            {
                PathStage();
                var camera = MakeCamera();
                camera.renderingPath = RenderingPath.DeferredShading;
                camera.allowHDR = false;
                return camera;
            }, null, SettingModes);
            Setting("deferredReflections", BuiltinShaderType.DeferredReflections, "Probe/Settings/DeferredReflections", logs, () =>
            {
                PathStage();
                var cube = Add(new Cubemap(4, TextureFormat.RGBA32, false));
                var texels = new Color[16];
                for (int i = 0; i < texels.Length; i++) texels[i] = new Color(0.8f, 0.6f, 0.2f, 1f);
                for (int face = 0; face < 6; face++) cube.SetPixels(texels, (CubemapFace)face);
                cube.Apply(false);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 1f;
                var camera = MakeCamera();
                camera.renderingPath = RenderingPath.DeferredShading;
                return camera;
            }, () =>
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.customReflectionTexture = null;
            }, SettingModes);
            Setting("screenSpaceShadows", BuiltinShaderType.ScreenSpaceShadows, "Probe/Settings/ScreenSpaceShadows", logs, () =>
            {
                PathStage();
                return MakeCamera();
            }, null, SettingModes);
            Setting("depthNormals", BuiltinShaderType.DepthNormals, "Probe/Settings/DepthNormals", logs, () =>
            {
                ColorStage();
                Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
                var camera = MakeCamera();
                camera.depthTextureMode = DepthTextureMode.DepthNormals;
                Show(camera, 0f);
                return camera;
            }, null, UnusedSettingModes);
            Setting("motionVectors", BuiltinShaderType.MotionVectors, "Probe/Settings/MotionVectors", logs, () =>
            {
                var stage = ColorStage();
                Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
                var camera = MakeCamera();
                camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                var mover = stage[1];
                mover.GetComponent<Renderer>().motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                // A first render, then the sphere and the camera moved: the second one has
                // object and camera motion.
                camera.Render();
                mover.transform.position += new Vector3(0.4f, 0.2f, 0f);
                camera.transform.position += new Vector3(0.1f, 0f, 0f);
                Show(camera, 1f);
                return camera;
            }, null, UnusedSettingModes);
            Setting("halo", BuiltinShaderType.LightHalo, "Probe/Settings/Halo", logs, () =>
            {
                ColorStage();
                Directional(new Vector3(50f, -30f, 0f), Color.white, 0.5f);
                var camera = MakeCamera();
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
                camera.gameObject.AddComponent<FlareLayer>();
                return camera;
            }, null, SettingModes, "HaloLight", "HaloObject");
            // A custom shader set and then unset before any render: the flare's custom shader
            // given to the halo setting, then null.
            {
                logs.Clear();
                GraphicsSettings.SetCustomShader(BuiltinShaderType.LightHalo, Shader.Find("Probe/Settings/Flare"));
                GraphicsSettings.SetCustomShader(BuiltinShaderType.LightHalo, null);
                SceneObject("HaloLight")?.SetActive(true);
                SceneObject("HaloObject")?.SetActive(true);
                ColorStage();
                Directional(new Vector3(50f, -30f, 0f), Color.white, 0.5f);
                var camera = MakeCamera();
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
                camera.gameObject.AddComponent<FlareLayer>();
                Capture(camera, "settings_halo_unrendered");
                SceneObject("HaloLight")?.SetActive(false);
                SceneObject("HaloObject")?.SetActive(false);
                Clear();
                Environment(new Color(0.1f, 0.1f, 0.1f));
                GraphicsSettings.SetCustomShader(BuiltinShaderType.LightHalo, Shader.Find("Hidden/Internal-Halo"));
            }
            Setting("lensFlare", BuiltinShaderType.LensFlare, "Probe/Settings/Flare", logs, () =>
            {
                ColorStage();
                Directional(new Vector3(50f, -30f, 0f), Color.white, 0.5f);
                var camera = MakeCamera();
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
                camera.gameObject.AddComponent<FlareLayer>();
                return camera;
            }, null, SettingModes, "LensFlareObject", "FlareSun");
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
        }
    }

    /// <summary>Draws a camera texture over the camera's image after everything: 0 the
    /// depth-normals texture, 1 the motion vectors (|xy| * 4).</summary>
    void Show(Camera camera, float which)
    {
        var material = Add(new Material(Shader.Find("Probe/Settings/ShowGlobal")));
        material.SetFloat("_Which", which);
        var show = new CommandBuffer { name = "show camera texture" };
        show.Blit((Texture)null, BuiltinRenderTextureType.CameraTarget, material);
        camera.AddCommandBuffer(CameraEvent.AfterEverything, show);
    }

    /// <summary>One setting in each mode: the stage made anew, the scene objects named active,
    /// the setting applied, the camera rendered; then the setting is put back.</summary>
    void Setting(string name, BuiltinShaderType type, string custom, List<string> logs, Func<Camera> stage, Action restore, string[] modes,
        params string[] sceneObjects)
    {
        var shader = Shader.Find(custom);
        var initial = GraphicsSettings.GetCustomShader(type);
        Value("settings." + name + ".shaderFound", B(shader != null));
        Value("settings." + name + ".initial", "[" + S(GraphicsSettings.GetShaderMode(type).ToString()) + "," + N(GraphicsSettings.GetCustomShader(type)) + "]");
        foreach (var mode in modes)
        {
            string key = "settings." + name + "." + mode;
            logs.Clear();
            try
            {
                GraphicsSettings.SetShaderMode(type, mode == "disabled" ? BuiltinShaderMode.Disabled : mode == "builtin" ? BuiltinShaderMode.UseBuiltin : BuiltinShaderMode.UseCustom);
                GraphicsSettings.SetCustomShader(type, mode == "custom" ? shader : null);
                Value(key + ".set", "[" + S(GraphicsSettings.GetShaderMode(type).ToString()) + "," + N(GraphicsSettings.GetCustomShader(type)) + "]");
                foreach (var o in sceneObjects) SceneObject(o)?.SetActive(true);
                var camera = stage();
                Value(key + ".actualPath", S(camera.actualRenderingPath.ToString()));
                Capture(camera, "settings_" + name + "_" + mode);
            }
            catch (Exception e)
            {
                Value(key + ".exception", S(e.GetType().Name + ": " + e.Message));
            }
            foreach (var o in sceneObjects) SceneObject(o)?.SetActive(false);
            if (logs.Count > 0)
            {
                var all = new System.Text.StringBuilder("[");
                foreach (var line in logs) all.Append(all.Length > 1 ? "," : "").Append(S(line));
                Value(key + ".logs", all.Append(']').ToString());
            }
            Clear();
            Environment(new Color(0.1f, 0.1f, 0.1f));
            restore?.Invoke();
        }
        // Back to the built-in mode and the setting's own shader.
        GraphicsSettings.SetShaderMode(type, BuiltinShaderMode.UseBuiltin);
        GraphicsSettings.SetCustomShader(type, initial);
        Value("settings." + name + ".restored", "[" + S(GraphicsSettings.GetShaderMode(type).ToString()) + "," + N(GraphicsSettings.GetCustomShader(type)) + "]");
    }
}
