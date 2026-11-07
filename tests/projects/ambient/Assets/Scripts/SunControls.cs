using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Opt-in isolation of the procedural sun. The original ambient capture stays unchanged.
public class SunControls : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (Environment.GetEnvironmentVariable("AMBIENT_SUN_CONTROLS") != null)
            new GameObject("SunControls").AddComponent<SunControls>();
    }

    static string Sh(SphericalHarmonicsL2 sh)
    {
        var values = new List<string>();
        for (int c = 0; c < 3; c++)
            for (int k = 0; k < 9; k++) values.Add(sh[c, k].ToString("R", CultureInfo.InvariantCulture));
        return "[" + string.Join(",", values) + "]";
    }

    IEnumerator Start()
    {
        var output = Environment.GetEnvironmentVariable("AMBIENT_SUN_CONTROLS");
        Directory.CreateDirectory(output);
        var original = RenderSettings.skybox;
        var light = new GameObject("sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(30f, 60f, 0f);
        light.color = new Color(1f, 0.95f, 0.8f);
        light.intensity = 1.2f;
        RenderSettings.sun = light;
        RenderSettings.ambientIntensity = 1f;
        var records = new StringBuilder();
        foreach (bool tinted in new[] { false, true })
        foreach (string disk in new[] { "none", "hq", "zero" })
        {
            var sky = new Material(original);
            if (tinted)
            {
                sky.SetColor("_SkyTint", new Color(0.8f, 0.3f, 0.3f));
                sky.SetFloat("_Exposure", 2f);
                sky.SetFloat("_AtmosphereThickness", 0.6f);
                sky.SetColor("_GroundColor", new Color(0.2f, 0.4f, 0.1f));
            }
            sky.DisableKeyword("_SUNDISK_HIGH_QUALITY");
            sky.DisableKeyword("_SUNDISK_SIMPLE");
            sky.DisableKeyword("_SUNDISK_NONE");
            sky.EnableKeyword(disk == "none" ? "_SUNDISK_NONE" : "_SUNDISK_HIGH_QUALITY");
            sky.SetFloat("_SunDisk", disk == "none" ? 0f : 2f);
            if (disk == "zero") sky.SetFloat("_SunSize", 0f);
            RenderSettings.skybox = sky;
            var before = Sh(RenderSettings.ambientProbe);
            DynamicGI.UpdateEnvironment();
            // Sample a bounded window, then require stability. This is an observation,
            // not a guarantee of Unity update latency; unchanged updates are allowed.
            string previous = before;
            int stable = 0;
            bool changed = false;
            for (int frame = 0; frame < 180; frame++)
            {
                yield return null;
                string current = Sh(RenderSettings.ambientProbe);
                changed |= current != before;
                stable = current == previous ? stable + 1 : 0;
                previous = current;
                if (frame >= 60 && stable >= 8) break;
                if (frame == 179) throw new Exception("Environment result did not settle");
            }
            records.Append("{\"name\":\"").Append(tinted ? "tinted " : "default ").Append(disk)
                .Append("\",\"ambient\":").Append(Sh(RenderSettings.ambientProbe))
                .Append(",\"rendered\":").Append(Sh(ProjectSky())).Append("}\n");
            File.WriteAllText(Path.Combine(output, "sun.jsonl"), records.ToString());
            Debug.Log("Sun control recorded " + tinted + " " + disk + " changed=" + changed);
            Destroy(sky);
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "sun.jsonl"), records.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static SphericalHarmonicsL2 ProjectSky()
    {
        const int n = 64;
        var target = RenderTexture.GetTemporary(n, n, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var owner = new GameObject("projection camera");
        var camera = owner.AddComponent<Camera>();
        camera.enabled = false;
        camera.cullingMask = 0;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 90f;
        camera.aspect = 1f;
        camera.allowHDR = true;
        camera.allowMSAA = false;
        camera.targetTexture = target;
        var readback = new Texture2D(n, n, TextureFormat.RGBAFloat, false, true);
        var result = new SphericalHarmonicsL2();
        var coefficients = new double[3, 9];
        var faces = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        var ups = new[] { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
        for (int face = 0; face < 6; face++)
        {
            camera.transform.rotation = Quaternion.LookRotation(faces[face], ups[face]);
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
            RenderTexture.active = previous;
            var pixels = readback.GetPixels();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                var d = (faces[face] + u * camera.transform.right + v * camera.transform.up).normalized;
                double l2 = 1.0 + u * u + v * v;
                double w = 4.0 / (n * n) / (l2 * Math.Sqrt(l2));
                double pi = Math.PI;
                double[] basis = { 1/(4*pi), d.y/(2*pi), d.z/(2*pi), d.x/(2*pi),
                    15/(16*pi)*d.x*d.y, 15/(16*pi)*d.y*d.z, 5/(64*pi)*(3*d.z*d.z-1),
                    15/(16*pi)*d.x*d.z, 15/(64*pi)*(d.x*d.x-d.y*d.y) };
                var color = pixels[y * n + x];
                for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++) coefficients[c, k] += color[c] * basis[k] * w;
            }
        }
        for (int c = 0; c < 3; c++)
        for (int k = 0; k < 9; k++) result[c, k] = (float)coefficients[c, k];
        DestroyImmediate(owner);
        DestroyImmediate(readback);
        RenderTexture.ReleaseTemporary(target);
        return result;
    }
}
