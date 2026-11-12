using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The environment lighting the Editor derives from the skybox (measured:
    /// tests/projects/ambient): the sky drawn into a cube (its shader, with the scene's sun) and
    /// projected onto spherical harmonics, stored as Unity keeps its ambient probe: each band's
    /// projection times the cosine lobe's (1, 2/3, 1/4) and the basis function's constant, so that
    /// a uniform sky of radiance L gives L in the first coefficient. The probe is kept at ambient
    /// intensity 1; RenderSettings.ambientProbe scales it by the intensity of the moment.
    /// </summary>
    public sealed partial class DynamicGI
    {
        const int EnvironmentSize = 64;

        static float s_IndirectScale = 1f, s_SceneIndirectScale = 1f;

        /// <summary>Starts as the loaded scene's Lighting Settings Indirect Intensity (1 without
        /// settings): measured, tests/projects/realtimegi (1 and 1.5). It scales the realtime
        /// lightmaps of precomputed realtime GI (<see cref="RealtimeGI"/>; 2 doubles them); its
        /// effect on baked lightmaps is unmeasured and Unbound gives it none.</summary>
        public static float indirectScale
        {
            get => s_IndirectScale;
            set
            {
                s_IndirectScale = value;
                if (value != s_SceneIndirectScale && GraphicsSettings.currentRenderPipeline == null && LightmapSettings.Count > 0)
                    Unbound.Runtime.Stubs.Ignored("DynamicGI.indirectScale",
                        "scales realtime lightmaps only; baked lightmaps keep their intensity (unmeasured)");
            }
        }

        /// <summary>How much, in percent, the inputs of a part of a realtime lightmap have to change,
        /// relative to their brightness, before precomputed realtime GI updates it again
        /// (default 1). Converged lighting depends on it: matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/realtimegi.</summary>
        public static float updateThreshold { get; set; } = 1f;

        /// <summary>Whether precomputed realtime GI has settled after the last change of its
        /// inputs (true without any).</summary>
        public static bool isConverged => RealtimeGI.IsConverged;

        /// <summary>Every system updates in each frame, so the frame shows the new output. Off,
        /// Unbound updates the systems within a frame budget.</summary>
        public static bool synchronousMode
        {
            get => RealtimeGI.Synchronous;
            set => RealtimeGI.Synchronous = value;
        }

        /// <summary>The renderer emits <paramref name="color"/> for precomputed realtime GI,
        /// without its system's emission being rendered again. Measured
        /// (tests/projects/realtimegi room-setemissive-then-call): a later material emission
        /// change does not replace it, even with UpdateGIMaterials.</summary>
        public static void SetEmissive(Renderer renderer, Color color) => RealtimeGI.SetEmissive(renderer, color);

        [Obsolete("DynamicGI.UpdateMaterials(Renderer) is deprecated; instead, use extension method from RendererExtensions: 'renderer.UpdateGIMaterials()' (UnityUpgradable).")]
        public static void UpdateMaterials(Renderer renderer) => RealtimeGI.MaterialsChanged(renderer);

        /// <summary>A scene loaded alone (not additively) with <paramref name="settings"/>; additive
        /// loads keep the value, and a scene without Lighting Settings gives 1 (both unmeasured).</summary>
        internal static void SceneLoaded(LightingSettings settings) =>
            s_IndirectScale = s_SceneIndirectScale = settings != null ? settings.indirectScale : 1f;

        /// <summary>Derives the ambient probe from the skybox again, and precomputed realtime GI
        /// makes its environment again from the ambient source.</summary>
        public static void UpdateEnvironment()
        {
            var sh = SkyboxProbe();
            if (sh.HasValue) RenderSettings.SetEnvironmentProbe(sh.Value);
            RenderSettings.AmbientChanged();
            RealtimeGI.EnvironmentChanged();
        }

        /// <summary>Sets the environment of precomputed realtime GI: a cube of six faces of 8 x 8
        /// texels of four floats (1536 in all; another length throws). The ambient source
        /// replaces it when its inputs change, except in a scene whose environment lighting is
        /// baked, which keeps it; <see cref="UpdateEnvironment"/> and a scene loaded alone drop
        /// it for both.</summary>
        public static void SetEnvironmentData(float[] input) => RealtimeGI.SetEnvironmentData(input);

        static SphericalHarmonicsL2? SkyboxProbe()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) return null;
            int n = EnvironmentSize;
            // A gamma-space project's sky draws gamma-encoded colors (tests/projects/realtimegi
            // `--color-space gamma`, `room-sky`).
            bool gamma = QualitySettings.activeColorSpace == ColorSpace.Gamma;
            var readback = new Texture2D(n, n, TextureFormat.RGBAFloat, false, true);
            var coefficients = new double[3, 9];
            var faces = new (Vector3 forward, Vector3 up)[]
            {
                (Vector3.right, Vector3.up), (Vector3.left, Vector3.up), (Vector3.up, Vector3.back),
                (Vector3.down, Vector3.forward), (Vector3.forward, Vector3.up), (Vector3.back, Vector3.up),
            };
            try
            {
                foreach (var (forward, up) in faces)
                {
                    RenderSky(n, forward, up, readback);
                    var pixels = readback.GetPixels();
                    var rotation = Quaternion.LookRotation(forward, up);
                    var right = rotation * Vector3.right;
                    var cameraUp = rotation * Vector3.up;
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                            var direction = forward + u * right + v * cameraUp;
                            double length2 = 1.0 + u * u + v * v;
                            double weight = (2.0 / n) * (2.0 / n) / (length2 * Math.Sqrt(length2));
                            direction.Normalize();
                            var radiance = pixels[y * n + x];
                            Accumulate(coefficients, gamma ? radiance.linear : radiance, direction, weight);
                        }
                }
            }
            finally
            {
                Object.DestroyImmediate(readback);
            }
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++)
                    sh[c, k] = (float)coefficients[c, k];
            return sh;
        }

        internal static Camera s_SkyCamera;
        /// <summary>The capture's targets by size: the ambient probe's and realtime GI's.</summary>
        static readonly Dictionary<int, RenderTexture> s_SkyTargets = new Dictionary<int, RenderTexture>();

        /// <summary>Renders the skybox as a 90-degree camera at the origin looking along
        /// <paramref name="forward"/> with <paramref name="up"/> sees it into
        /// <paramref name="readback"/> (n x n, linear, read back from the bottom row up). The
        /// camera and the target of each size are kept for the next call.</summary>
        internal static void RenderSky(int n, Vector3 forward, Vector3 up, Texture2D readback)
        {
            var camera = SkyCamera();
            if (!s_SkyTargets.TryGetValue(n, out var target) || target == null)
                s_SkyTargets[n] = target = new RenderTexture(n, n, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                    { hideFlags = HideFlags.HideAndDontSave };
            camera.targetTexture = target;
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(forward, up));
            // The engine's own draw of the sky, whatever the render pipeline: a pipeline's
            // camera would send its events and draw the sky its way (the Editor's ambient probe
            // and realtime GI environment under the Universal Render Pipeline are the built-in
            // pipeline's: tests/projects/realtimegi `--pipeline urp` pipeline-sky*).
            FrameBuilder.EmitLightingGlobals();
            FrameBuilder.RenderCamera(camera);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
            RenderTexture.active = previous;
            camera.targetTexture = null;
        }

        /// <summary>The capture's camera, made on first use and kept. It is in no scene: an
        /// environment update leaves the scenes' roots and their sibling indices as they were
        /// (Unity 6000.0.71f1: tests/projects/coreapis, oroorder, instantiateasync).</summary>
        internal static Camera SkyCamera()
        {
            if (s_SkyCamera != null) return s_SkyCamera;
            var camera = GameObject.CreateDetached("Unbound Environment Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.m_EnvironmentCapture = true;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            return s_SkyCamera = camera;
        }

        /// <summary>One texel's radiance onto the stored coefficients: the projection's factor of
        /// each basis polynomial with the cosine lobe and the basis constant squared.</summary>
        static void Accumulate(double[,] sh, Color radiance, Vector3 d, double weight)
        {
            double x = d.x, y = d.y, z = d.z;
            double pi = Math.PI;
            Span<double> basis = stackalloc double[9];
            basis[0] = 1.0 / (4.0 * pi);
            basis[1] = y / (2.0 * pi);
            basis[2] = z / (2.0 * pi);
            basis[3] = x / (2.0 * pi);
            basis[4] = 15.0 / (16.0 * pi) * x * y;
            basis[5] = 15.0 / (16.0 * pi) * y * z;
            basis[6] = 5.0 / (64.0 * pi) * (3.0 * z * z - 1.0);
            basis[7] = 15.0 / (16.0 * pi) * x * z;
            basis[8] = 15.0 / (64.0 * pi) * (x * x - y * y);
            for (int k = 0; k < 9; k++)
            {
                double b = basis[k] * weight;
                sh[0, k] += radiance.r * b;
                sh[1, k] += radiance.g * b;
                sh[2, k] += radiance.b * b;
            }
        }
    }
}
