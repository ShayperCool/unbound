using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records Terrain.normalmapTexture: the normalmap format properties, when a terrain has the
/// texture (instanced or not, before and after a camera renders it, after its heights change,
/// after instancing is turned off), what the texture is (size, formats, mips, sampling) and its
/// texels (10-bit steps, the only numbers: compared within one 8-bit step). Terrains are made from fixed heights and rendered with
/// Camera.Render. Writes values.jsonl into TERRAIN_NORMALMAP_OUT and exits.
/// </summary>
public sealed class TerrainNormalmapProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    Camera m_Camera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("TerrainNormalmapProbe").AddComponent<TerrainNormalmapProbe>();

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        string dir = Environment.GetEnvironmentVariable("TERRAIN_NORMALMAP_OUT");
        try
        {
            Record();
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("terrain normalmap probe failed: " + e);
            code = 1;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    /// <summary>Fixed heights: a ramp, ridges and a step (exact multiples of 1/64).</summary>
    static float[,] Heights(int resolution)
    {
        var heights = new float[resolution, resolution];
        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
                heights[y, x] = ((x * 3 + y * 5) % 16 + (x * y) % 7 + (x > resolution / 2 ? 8 : 0)) / 64f;
        return heights;
    }

    static Terrain MakeTerrain(string name, int resolution, Vector3 size, Vector3 position)
    {
        var data = new TerrainData { heightmapResolution = resolution };
        data.size = size;
        data.SetHeights(0, 0, Heights(data.heightmapResolution));
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = name;
        go.transform.position = position;
        return go.GetComponent<Terrain>();
    }

    void Render() => m_Camera.Render();

    void Record()
    {
        Rec("format.normalmapFormat", Terrain.normalmapFormat.ToString());
        Rec("format.normalmapRenderTextureFormat", Terrain.normalmapRenderTextureFormat.ToString());
        Rec("format.normalmapTextureFormat", Terrain.normalmapTextureFormat.ToString());

        var cameraObject = new GameObject("Camera");
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.transform.position = new Vector3(32, 120, -40);
        m_Camera.transform.LookAt(new Vector3(32, 0, 32));
        m_Camera.targetTexture = new RenderTexture(64, 64, 24);
        m_Camera.enabled = false;

        var terrain = MakeTerrain("Terrain33", 33, new Vector3(64, 16, 64), Vector3.zero);
        Rec("created.drawInstanced", terrain.drawInstanced);
        Describe("created", terrain);
        Render();
        Describe("rendered", terrain);

        terrain.drawInstanced = true;
        Describe("instanced", terrain);
        Texels("instanced", terrain.normalmapTexture);
        Render();
        Describe("instanced.rendered", terrain);
        var first = terrain.normalmapTexture;
        Texels("instanced.rendered", first);
        Texels("instanced.rendered", first, 1);
        Texels("instanced.rendered", first, 5);
        Render();
        Rec("instanced.rendered.again.same", first != null && terrain.normalmapTexture == first);

        // A change of heights: before and after the next render.
        var patch = new float[5, 5];
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                patch[y, x] = (x + y) / 16f;
        terrain.terrainData.SetHeights(10, 12, patch);
        Rec("changed.same", first != null && terrain.normalmapTexture == first);
        Texels("changed", terrain.normalmapTexture);
        Render();
        Rec("changed.rendered.same", first != null && terrain.normalmapTexture == first);
        Texels("changed.rendered", terrain.normalmapTexture);

        // Another heightmap resolution and size.
        var other = MakeTerrain("Terrain65", 65, new Vector3(30, 40, 50), new Vector3(70, 0, 0));
        other.drawInstanced = true;
        Describe("other.instanced", other);
        m_Camera.transform.position = new Vector3(85, 120, -40);
        m_Camera.transform.LookAt(new Vector3(85, 0, 25));
        Render();
        Describe("other.instanced.rendered", other);
        Texels("other.instanced.rendered", other.normalmapTexture);

        // A map first read after the heights changed: without a render since instancing went on
        // (firstread.direct), and with one (firstread.rendered).
        var direct = MakeTerrain("TerrainFirstRead", 33, new Vector3(64, 16, 64), new Vector3(0, 0, 200));
        direct.drawInstanced = true;
        direct.terrainData.SetHeights(10, 12, patch);
        TexelRows("firstread.direct", direct.normalmapTexture, 9, 18);
        var rendered = MakeTerrain("TerrainFirstReadRendered", 33, new Vector3(64, 16, 64), new Vector3(0, 0, 300));
        rendered.drawInstanced = true;
        Render();
        rendered.terrainData.SetHeights(10, 12, patch);
        TexelRows("firstread.rendered", rendered.normalmapTexture, 9, 18);
        Render();
        TexelRows("firstread.rendered.again", rendered.normalmapTexture, 9, 18);

        // Instancing turned off again.
        terrain.drawInstanced = false;
        Rec("uninstanced.firstAlive", first != null);
        Describe("uninstanced", terrain);
        m_Camera.transform.position = new Vector3(32, 120, -40);
        m_Camera.transform.LookAt(new Vector3(32, 0, 32));
        Render();
        Rec("uninstanced.rendered.firstAlive", first != null);
        Describe("uninstanced.rendered", terrain);
    }

    void Describe(string key, Terrain terrain)
    {
        var rt = terrain.normalmapTexture;
        Rec(key + ".exists", rt != null);
        if (rt == null) return;
        Rec(key + ".name", rt.name);
        Rec(key + ".size", rt.width + "x" + rt.height + "x" + rt.volumeDepth);
        Rec(key + ".graphicsFormat", rt.graphicsFormat.ToString());
        Rec(key + ".format", rt.format.ToString());
        Rec(key + ".depthStencilFormat", rt.depthStencilFormat.ToString());
        Rec(key + ".dimension", rt.dimension.ToString());
        Rec(key + ".useMipMap", rt.useMipMap);
        Rec(key + ".autoGenerateMips", rt.autoGenerateMips);
        Rec(key + ".mipmapCount", rt.mipmapCount.ToString(CultureInfo.InvariantCulture));
        Rec(key + ".filterMode", rt.filterMode.ToString());
        Rec(key + ".wrapMode", rt.wrapMode.ToString());
        Rec(key + ".anisoLevel", rt.anisoLevel.ToString(CultureInfo.InvariantCulture));
        Rec(key + ".sRGB", rt.sRGB);
        Rec(key + ".antiAliasing", rt.antiAliasing.ToString(CultureInfo.InvariantCulture));
        Rec(key + ".enableRandomWrite", rt.enableRandomWrite);
        Rec(key + ".isCreated", rt.IsCreated());
        Rec(key + ".hideFlags", rt.hideFlags.ToString());
    }

    /// <summary>A level of the texture read back as floats, in steps of its 10-bit channels (alpha
    /// in its 2-bit steps), one list of RGBA per row, bottom row first.</summary>
    void Texels(string key, RenderTexture rt, int mip = 0)
    {
        if (rt == null) return;
        int width = Math.Max(1, rt.width >> mip), height = Math.Max(1, rt.height >> mip);
        var level = new RenderTexture(width, height, 0, rt.graphicsFormat);
        level.Create();
        Graphics.CopyTexture(rt, 0, mip, level, 0, 0);
        var previous = RenderTexture.active;
        RenderTexture.active = level;
        var copy = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        var pixels = copy.GetPixels();
        for (int y = 0; y < height; y++)
        {
            var row = new List<int>(width * 4);
            for (int x = 0; x < width; x++)
            {
                var p = pixels[y * width + x];
                row.Add(Mathf.RoundToInt(p.r * 1023)); row.Add(Mathf.RoundToInt(p.g * 1023));
                row.Add(Mathf.RoundToInt(p.b * 1023)); row.Add(Mathf.RoundToInt(p.a * 3));
            }
            Rec(key + ".texels" + (mip > 0 ? ".mip" + mip : "") + "." + y.ToString("D3", CultureInfo.InvariantCulture), row.ToArray());
        }
        Destroy(copy);
        level.Release();
        Destroy(level);
    }

    /// <summary>Rows <paramref name="from"/> to <paramref name="to"/> (exclusive) of the first
    /// level, as <see cref="Texels"/> writes them.</summary>
    void TexelRows(string key, RenderTexture rt, int from, int to)
    {
        var all = new StringBuilder();
        var saved = m_Out.ToString();
        m_Out.Clear();
        Texels(key, rt);
        foreach (var line in m_Out.ToString().Split('\n'))
        {
            int row;
            int at = line.IndexOf(".texels.", StringComparison.Ordinal);
            if (at < 0 || !int.TryParse(line.Substring(at + 8, 3), NumberStyles.None, CultureInfo.InvariantCulture, out row)) continue;
            if (row >= from && row < to) all.Append(line).Append('\n');
        }
        m_Out.Clear();
        m_Out.Append(saved).Append(all);
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    void Rec(string key, bool value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value ? "true" : "false").Append("}\n");

    void Rec(string key, int[] values)
    {
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) m_Out.Append(',');
            m_Out.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
        m_Out.Append("]}\n");
    }
}
