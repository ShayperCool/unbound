using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Renders the sky with Hidden/Unbound/SkyMeshProbe into six 90° views and writes the
/// distinct flat values (provoking vertices' object positions, distance) as JSON lines.</summary>
public static class SkyMeshProbe
{
    public static void Record(string path)
    {
        var camera = Camera.main;
        var shader = Shader.Find("Hidden/Unbound/SkyMeshProbe");
        var material = new Material(shader);
        var (sky, clear, fov, rotation, position) = (RenderSettings.skybox, camera.clearFlags, camera.fieldOfView, camera.transform.rotation, camera.transform.position);
        RenderSettings.skybox = material;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 90f;
        var seen = new HashSet<string>();
        var sb = new StringBuilder();
        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBFloat);
        var read = new Texture2D(128, 128, TextureFormat.RGBAFloat, false);
        var culling = camera.cullingMask;
        camera.cullingMask = 0;
        foreach (var euler in new[] { new Vector3(0, 0, 0), new Vector3(0, 90, 0), new Vector3(0, 180, 0), new Vector3(0, 270, 0), new Vector3(-90, 0, 0), new Vector3(90, 0, 0) })
        {
            camera.transform.rotation = Quaternion.Euler(euler);
            camera.targetTexture = target;
            camera.aspect = 1f;
            camera.Render();
            RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            foreach (var c in read.GetPixels())
            {
                var key = string.Format(CultureInfo.InvariantCulture, "[{0:R},{1:R},{2:R},{3:R}]", c.r, c.g, c.b, c.a);
                if (seen.Add(key)) sb.Append(key).Append('\n');
            }
        }
        camera.targetTexture = null;
        camera.ResetAspect();
        camera.cullingMask = culling;
        (RenderSettings.skybox, camera.clearFlags, camera.fieldOfView) = (sky, clear, fov);
        camera.transform.rotation = rotation;
        camera.transform.position = position;
        target.Release();
        File.WriteAllText(path, sb.ToString());
    }
}
