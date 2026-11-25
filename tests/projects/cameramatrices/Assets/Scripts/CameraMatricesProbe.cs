using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes the exact bits of a camera's matrices into values.jsonl in $CAMERA_MATRICES_OUT
/// (tests/value_probe.py): projectionMatrix over fields of view, aspects and clip planes,
/// worldToCameraMatrix / cameraToWorldMatrix and the transform's matrices over rotations and
/// positions (also under a rotated, scaled parent), cullingMatrix, and GL.GetGPUProjectionMatrix.
/// Each matrix is its 16 elements in Matrix4x4 index order (column-major) as hexadecimal float bits.
/// </summary>
public static class CameraMatricesProbe
{
    static readonly StringBuilder s_Out = new StringBuilder();

    static void Row(string key, string value) => s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Bits(Matrix4x4 m)
    {
        var b = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            if (i > 0) b.Append(' ');
            b.Append(BitConverter.SingleToInt32Bits(m[i]).ToString("x8"));
        }
        return b.ToString();
    }

    static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        try
        {
            var go = new GameObject("probe camera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;

            // Projections: fields of view at an aspect of 16:9 set on the camera.
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.aspect = 16f / 9f;
            foreach (float fov in new[] { 1f, 10f, 30f, 45f, 59.5f, 60f, 61.3f, 75f, 90f, 110f, 150f, 179f })
            {
                camera.fieldOfView = fov;
                Row($"projection/fov {F(fov)}", Bits(camera.projectionMatrix));
            }
            // Aspects set on the camera.
            camera.fieldOfView = 60f;
            foreach (float aspect in new[] { 1f, 4f / 3f, 0.37f, 2.4f, 21f / 9f })
            {
                camera.aspect = aspect;
                Row($"projection/aspect {F(aspect)}", Bits(camera.projectionMatrix));
            }
            // Aspects of the camera's target texture.
            camera.ResetAspect();
            foreach (var (w, h) in new[] { (320, 180), (100, 37), (160, 90), (1, 1), (1000, 3) })
            {
                var rt = new RenderTexture(w, h, 0);
                camera.targetTexture = rt;
                Row($"projection/target {w}x{h}", Bits(camera.projectionMatrix) + " aspect " + F(camera.aspect));
                camera.targetTexture = null;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            // Clip planes.
            camera.aspect = 16f / 9f;
            foreach (var (near, far) in new[] { (0.01f, 100000f), (5f, 6f), (0.3f, 1000f), (1f, 1500f) })
            {
                camera.nearClipPlane = near;
                camera.farClipPlane = far;
                Row($"projection/clip {F(near)} {F(far)}", Bits(camera.projectionMatrix));
            }
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            // Orthographic.
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            Row("projection/orthographic 5", Bits(camera.projectionMatrix));
            camera.orthographicSize = 3.3f;
            camera.aspect = 0.37f;
            Row("projection/orthographic 3.3 0.37", Bits(camera.projectionMatrix));
            camera.orthographic = false;
            camera.aspect = 16f / 9f;
            camera.fieldOfView = 60f;

            // GPU projections (OpenGL Core: as the device draws with them).
            Row("gpu projection/perspective", Bits(GL.GetGPUProjectionMatrix(camera.projectionMatrix, false)));
            Row("gpu projection/perspective texture", Bits(GL.GetGPUProjectionMatrix(camera.projectionMatrix, true)));

            // Views: rotations and positions of a root camera.
            var rotations = new (string, Quaternion)[]
            {
                ("euler 10 0 0", Quaternion.Euler(10f, 0f, 0f)),
                ("euler 0 37 0", Quaternion.Euler(0f, 37f, 0f)),
                ("euler 33 -71 12", Quaternion.Euler(33f, -71f, 12f)),
                ("euler -89 0 0", Quaternion.Euler(-89f, 0f, 0f)),
                ("euler 179.9 45 0", Quaternion.Euler(179.9f, 45f, 0f)),
                ("euler 0 0 90", Quaternion.Euler(0f, 0f, 90f)),
                ("euler 45 45 45", Quaternion.Euler(45f, 45f, 45f)),
                ("quaternion 0.08715574 0 0 0.9961947", new Quaternion(0.08715574f, 0f, 0f, 0.9961947f)),
                ("identity", Quaternion.identity),
            };
            var positions = new[] { new Vector3(0f, 1.5f, -6f), new Vector3(123.456f, -7.89f, 1000.5f), Vector3.zero, new Vector3(-0.001f, 2e5f, 3f) };
            foreach (var (name, rotation) in rotations)
            {
                go.transform.rotation = rotation;
                Row($"rotation/{name}", string.Join(" ", F(go.transform.rotation.x), F(go.transform.rotation.y), F(go.transform.rotation.z), F(go.transform.rotation.w)));
                for (int i = 0; i < positions.Length; i++)
                {
                    go.transform.position = positions[i];
                    string key = $"{name} at {i}";
                    Row($"world to camera/{key}", Bits(camera.worldToCameraMatrix));
                    Row($"camera to world/{key}", Bits(camera.cameraToWorldMatrix));
                    Row($"world to local/{key}", Bits(go.transform.worldToLocalMatrix));
                    Row($"local to world/{key}", Bits(go.transform.localToWorldMatrix));
                }
                go.transform.position = positions[0];
                Row($"culling/{name}", Bits(camera.cullingMatrix));
            }

            // A camera under a rotated, scaled parent.
            var parent = new GameObject("parent");
            parent.transform.position = new Vector3(1f, 2f, 3f);
            parent.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            parent.transform.localScale = new Vector3(2f, 2f, 2f);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 1f, -5f);
            go.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            Row("world to camera/child of scaled parent", Bits(camera.worldToCameraMatrix));
            Row("camera to world/child of scaled parent", Bits(camera.cameraToWorldMatrix));
            Row("world to local/child of scaled parent", Bits(go.transform.worldToLocalMatrix));
            Row("local to world/child of scaled parent", Bits(go.transform.localToWorldMatrix));
            parent.transform.localScale = new Vector3(1f, 3f, 0.5f);
            Row("world to camera/child of nonuniform parent", Bits(camera.worldToCameraMatrix));
            Row("world to local/child of nonuniform parent", Bits(go.transform.worldToLocalMatrix));

            s_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        }
        catch (Exception e)
        {
            Debug.LogError("cameramatrices probe failed: " + e);
        }
        var dir = Environment.GetEnvironmentVariable("CAMERA_MATRICES_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
