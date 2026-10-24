using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders one small scenario per case — camera clears, renderers with property blocks, blits,
/// command buffers (immediate and at camera events), image effects, GL, DrawMeshNow, queued
/// DrawMesh, half-float targets — reads each result back with <c>Texture2D.ReadPixels</c> and
/// writes every pixel to <c>GRAPHICS_PROBE_OUT</c> (JSON), then quits. The same script runs in
/// the Editor (the reference) and in Unbound; <c>compare.py</c> compares the two files.
/// </summary>
public class GraphicsProbe : MonoBehaviour
{
    const int Size = 8;
    const int ProbeLayer = 30;

    readonly StringBuilder m_Json = new StringBuilder();
    int m_Cases;
    Mesh m_Quad;
    Texture2D m_Gradient;
    Camera m_Camera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("GraphicsProbe").AddComponent<GraphicsProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        m_Quad = Quad();
        m_Gradient = Gradient();
        var cameraObject = new GameObject("ProbeCamera");
        cameraObject.transform.position = new Vector3(0, 0, -5);
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.orthographic = true;
        m_Camera.orthographicSize = 1f;
        m_Camera.cullingMask = 1 << ProbeLayer;
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 1f);

        m_Json.Append("{\n  \"colorSpace\": \"").Append(QualitySettings.activeColorSpace).Append("\",\n  \"cases\": [\n");
        Case("cameraClear", rt =>
        {
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("cameraQuad", rt =>
        {
            var quad = QuadObject(Color("Probe/Color", new Color(1f, 0.25f, 0f, 1f)), new Vector3(0.5f, 0.5f, 0f), 1f);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            DestroyImmediate(quad);
        });
        Case("rendererPropertyBlock", rt =>
        {
            var quad = QuadObject(Color("Probe/Color", UnityEngine.Color.red), Vector3.zero, 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(0f, 0.75f, 0.25f, 1f));
            quad.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            DestroyImmediate(quad);
        });
        Case("blitCopy", rt => Graphics.Blit(m_Gradient, rt));
        Case("blitMaterial", rt => Graphics.Blit(m_Gradient, rt, new Material(Shader.Find("Probe/Invert"))));
        Case("blitPass1", rt => Graphics.Blit(null, rt, new Material(Shader.Find("Probe/TwoPass")), 1));
        Case("blitScaleOffset", rt => Graphics.Blit(m_Gradient, rt, new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.5f)));
        Case("commandBuffer", rt =>
        {
            var cb = new CommandBuffer { name = "probe" };
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, new Color(1f, 1f, 0f, 1f));
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.TRS(new Vector3(0.25f, 0f, 0f), Quaternion.identity, new Vector3(1f, 2f, 1f)),
                Color("Probe/Color", new Color(0f, 0f, 1f, 1f)));
            Graphics.ExecuteCommandBuffer(cb);
        });
        Case("commandBufferPropertyBlock", rt =>
        {
            var cb = new CommandBuffer();
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(1f, 0f, 1f, 1f));
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, UnityEngine.Color.black);
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.Scale(new Vector3(2f, 1f, 1f)), Color("Probe/Color", UnityEngine.Color.white), 0, 0, block);
            Graphics.ExecuteCommandBuffer(cb);
        });
        Case("commandBufferGlobal", rt =>
        {
            var cb = new CommandBuffer();
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, UnityEngine.Color.black);
            cb.SetGlobalVector("_ProbeGlobal", new Vector4(0f, 1f, 1f, 1f));
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.Scale(new Vector3(1f, 2f, 1f)), new Material(Shader.Find("Probe/Global")));
            Graphics.ExecuteCommandBuffer(cb);
        });
        Case("cameraEvent", rt =>
        {
            var cb = new CommandBuffer { name = "after everything" };
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.TRS(new Vector3(-0.5f, 0f, 0f), Quaternion.identity, new Vector3(1f, 2f, 1f)),
                Color("Probe/Color", new Color(0.5f, 0f, 1f, 1f)));
            m_Camera.AddCommandBuffer(CameraEvent.AfterEverything, cb);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            m_Camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
        });
        Case("onRenderImage", rt =>
        {
            var effect = m_Camera.gameObject.AddComponent<ProbeImageEffect>();
            effect.material = new Material(Shader.Find("Probe/Invert"));
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            DestroyImmediate(effect);
        });
        Case("gl", rt =>
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, UnityEngine.Color.black);
            new Material(Shader.Find("Probe/VertexColor")).SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);
            GL.Color(UnityEngine.Color.red); GL.Vertex3(0f, 0f, 0f);
            GL.Color(UnityEngine.Color.green); GL.Vertex3(0f, 0.5f, 0f);
            GL.Color(UnityEngine.Color.blue); GL.Vertex3(0.75f, 0.5f, 0f);
            GL.Color(UnityEngine.Color.white); GL.Vertex3(0.75f, 0f, 0f);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = null;
        });
        Case("drawMeshNow", rt =>
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, UnityEngine.Color.black);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(Matrix4x4.identity);
            GL.modelview = Matrix4x4.identity;
            Color("Probe/Color", new Color(0.25f, 0.5f, 0.75f, 1f)).SetPass(0);
            Graphics.DrawMeshNow(m_Quad, Matrix4x4.TRS(new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(2f, 1f, 1f)));
            GL.PopMatrix();
            RenderTexture.active = null;
        });
        Case("drawMeshQueued", rt =>
        {
            Graphics.DrawMesh(m_Quad, Matrix4x4.TRS(new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(2f, 1f, 1f)),
                Color("Probe/Color", new Color(1f, 0.5f, 0f, 1f)), ProbeLayer, m_Camera);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("halfFloat", rt => Graphics.Blit(null, rt, new Material(Shader.Find("Probe/Hdr"))), RenderTextureFormat.ARGBHalf);
        Case("readPixelsOffset", rt =>
        {
            Graphics.Blit(m_Gradient, rt);
        }, RenderTextureFormat.ARGB32, true);

        // Temporaries: Unity hands a released one back for the same request.
        var a = RenderTexture.GetTemporary(16, 16, 0);
        RenderTexture.ReleaseTemporary(a);
        var b = RenderTexture.GetTemporary(16, 16, 0);
        var c = RenderTexture.GetTemporary(16, 16, 0);
        Value("temporaryReused", a == b);
        Value("temporarySecondDistinct", b != c);
        Value("temporaryCreated", a.IsCreated());
        RenderTexture.ReleaseTemporary(b);
        RenderTexture.ReleaseTemporary(c);

        m_Json.Append("\n  ]\n}\n");
        var output = Environment.GetEnvironmentVariable("GRAPHICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) Debug.Log(m_Json.ToString());
        else
        {
            File.WriteAllText(output, m_Json.ToString());
            Debug.Log("graphics probe written to " + output);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Runs a case into a fresh 8x8 target and records its pixels, read back.</summary>
    void Case(string name, Action<RenderTexture> draw, RenderTextureFormat format = RenderTextureFormat.ARGB32, bool offset = false)
    {
        var rt = new RenderTexture(Size, Size, 24, format);
        rt.filterMode = FilterMode.Point;
        rt.Create();
        m_Camera.targetTexture = null;
        try { draw(rt); }
        catch (Exception e) { Debug.LogException(e); }
        m_Camera.targetTexture = null;
        bool half = format == RenderTextureFormat.ARGBHalf;
        var texture = new Texture2D(Size, Size, half ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        if (offset)
        {
            var white = new Color[Size * Size];
            for (int i = 0; i < white.Length; i++) white[i] = UnityEngine.Color.white;
            texture.SetPixels(white);
            texture.ReadPixels(new Rect(2, 1, 4, 5), 3, 2);
        }
        else texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        rt.Release();

        Separator();
        m_Json.Append("    {\"name\": \"").Append(name).Append("\", \"pixels\": [");
        var pixels = texture.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            if (i > 0) m_Json.Append(", ");
            var p = pixels[i];
            m_Json.Append('[').Append(F(p.r)).Append(", ").Append(F(p.g)).Append(", ").Append(F(p.b)).Append(", ").Append(F(p.a)).Append(']');
        }
        m_Json.Append("]}");
    }

    void Value(string name, bool value)
    {
        Separator();
        m_Json.Append("    {\"name\": \"").Append(name).Append("\", \"value\": ").Append(value ? "true" : "false").Append('}');
    }

    void Separator()
    {
        if (m_Cases++ > 0) m_Json.Append(",\n");
    }

    static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    static Material Color(string shader, Color color)
    {
        var material = new Material(Shader.Find(shader));
        material.SetColor("_Color", color);
        return material;
    }

    GameObject QuadObject(Material material, Vector3 position, float size)
    {
        var go = new GameObject("ProbeQuad");
        go.layer = ProbeLayer;
        go.transform.position = position;
        go.transform.localScale = new Vector3(size, size, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = m_Quad;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static Mesh Quad()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        return mesh;
    }

    /// <summary>A 4x4 texture whose texels all differ, point-filtered.</summary>
    static Texture2D Gradient()
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                texture.SetPixel(x, y, new Color(x / 3f, y / 3f, (x + y) / 6f, 1f));
        texture.Apply();
        return texture;
    }
}

/// <summary>An image effect: inverts the camera's image.</summary>
public class ProbeImageEffect : MonoBehaviour
{
    public Material material;

    void OnRenderImage(RenderTexture source, RenderTexture destination) => Graphics.Blit(source, destination, material);
}
