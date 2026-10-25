using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The environment lighting the Editor derives from the skybox (measured:
    /// tests/projects/ambient): the sky drawn into a cube (its shader, with the scene's sun) and
    /// projected onto spherical harmonics, stored as Unity keeps its ambient probe: each band's
    /// projection times the cosine lobe's (1, 2/3, 1/4) and the basis function's constant, so that
    /// a uniform sky of radiance L gives L in the first coefficient. The ambient intensity scales
    /// it by intensity^2.2 in a linear project.
    /// </summary>
    public sealed partial class DynamicGI
    {
        const int EnvironmentSize = 64;

        public static void UpdateEnvironment()
        {
            var sh = SkyboxProbe();
            if (sh.HasValue) RenderSettings.s_EnvironmentProbe = sh;
        }

        static SphericalHarmonicsL2? SkyboxProbe()
        {
            var sky = RenderSettings.skybox;
            if (sky == null) return null;
            int n = EnvironmentSize;
            var target = RenderTexture.GetTemporary(n, n, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var cameraObject = new GameObject("Unbound Environment Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 90f;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.targetTexture = target;
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
                    camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(forward, up));
                    camera.Render();
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
                    RenderTexture.active = previous;
                    var pixels = readback.GetPixels();
                    var right = camera.transform.right;
                    var cameraUp = camera.transform.up;
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                            var direction = forward + u * right + v * cameraUp;
                            double length2 = 1.0 + u * u + v * v;
                            double weight = (2.0 / n) * (2.0 / n) / (length2 * Math.Sqrt(length2));
                            direction.Normalize();
                            Accumulate(coefficients, pixels[y * n + x], direction, weight);
                        }
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(readback);
            }
            float scale = RenderSettings.ambientIntensity;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) scale = Mathf.Pow(scale, 2.2f);
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++)
                    sh[c, k] = (float)coefficients[c, k] * scale;
            return sh;
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
