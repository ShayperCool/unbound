using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What ScriptableRenderContext.EmitGeometryForCamera and EmitWorldGeometryForSceneView make a
/// scriptable pipeline draw (<see cref="ProbePipelineAsset"/>): a camera renders a world space
/// canvas's half-transparent red quad (left) and its own screen space camera canvas's
/// half-transparent green quad (right) into a 64x64 target, as each camera type (Game,
/// Reflection, Preview, SceneView), with no emission, either call or both, then with none again
/// (whether an emission lasts); and a Reflection camera that emits but whose culling mask leaves
/// out the canvases' layer; then where a Game camera's render draws when the pipeline sets no
/// target, or sets another one before SetupCameraProperties; then a Game camera emitting after
/// its culling, or culling twice and drawing the first or the second results; the order of the
/// copies where two quads of a third canvas overlap (above the others: half-transparent red, then
/// blue); and what SetupCameraProperties leaves of a camera covering the right half of its target.
/// Each render records the colour (bytes) at both quads' centres; one drawn quad over black is
/// about half its colour, two draws more. Writes values.jsonl into EMIT_GEOMETRY_OUT and exits.
/// </summary>
public sealed class EmitGeometryProbe : MonoBehaviour
{
    const int Size = 64;
    const int CanvasLayer = 5;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Log = new List<string>();
    Camera m_Camera;
    RenderTexture m_Target;
    Texture2D m_Read;
    Vector3 m_WorldQuad, m_OverlapQuad;
    RenderTexture m_Other;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("EmitGeometryProbe").AddComponent<EmitGeometryProbe>();

    void Rec(string key, string json) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");

    static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    IEnumerator Start()
    {
        int code = 0;
        string dir = Environment.GetEnvironmentVariable("EMIT_GEOMETRY_OUT");
        var previous = GraphicsSettings.defaultRenderPipeline;
        try
        {
            Setup();
            GraphicsSettings.defaultRenderPipeline = ScriptableObject.CreateInstance<ProbePipelineAsset>();
        }
        catch (Exception e)
        {
            Debug.LogError("emit geometry probe setup failed: " + e);
            code = 1;
        }
        // The canvases build their geometry at the end of a frame's update.
        yield return null;
        yield return null;
        if (code == 0)
        {
            // A first render makes the pipeline (the Editor warns then about the probe's asset,
            // which declares no pipeline type or shader tag): not recorded.
            m_Camera.Render();
            Application.logMessageReceived += Capture;
            try
            {
                foreach (var type in new[] { CameraType.Game, CameraType.Reflection, CameraType.Preview, CameraType.SceneView })
                {
                    string name = type.ToString();
                    Render(name + ".none", type, 0);
                    Render(name + ".emit", type, 1);
                    Render(name + ".sceneView", type, 2);
                    Render(name + ".both", type, 3);
                    Render(name + ".noneAfter", type, 0);
                }
                // An emitting Reflection camera whose culling mask leaves out the canvases' layer.
                m_Camera.cullingMask = ~(1 << CanvasLayer);
                Render("Reflection.emitMasked", CameraType.Reflection, 1);
                m_Camera.cullingMask = -1;
                // Where a render without a target of its own draws: both targets filled with blue
                // first; the pipeline sets none, or another target before SetupCameraProperties.
                m_Other = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
                ProbePipelineAsset.Other = m_Other;
                RenderTargets("target.none", 1);
                RenderTargets("target.otherBeforeSetup", 2);
                // Emitting after culling, and two cullings of one render.
                ProbePipelineAsset.EmitAfterCull = true;
                try { Render("Game.emitAfterCull", CameraType.Game, 1); }
                finally { ProbePipelineAsset.EmitAfterCull = false; }
                foreach (var drawn in new[] { 1, 2 })
                {
                    ProbePipelineAsset.CullTwice = drawn;
                    try { Render("Game.cullTwice.drawn" + drawn, CameraType.Game, 1); }
                    finally { ProbePipelineAsset.CullTwice = 0; }
                }
                // The copies' order: red then blue where they overlap, once and with one emission.
                Overlap("order.none", 0);
                Overlap("order.emit", 1);
                // The camera covering the right half of its target, both filled with blue first:
                // what SetupCameraProperties binds (no target set) or SetRenderTarget(CameraTarget).
                Viewport("viewport.setup", 1);
                Viewport("viewport.cameraTarget", 0);
                Rec("complete", "true");
            }
            catch (Exception e)
            {
                Debug.LogError("emit geometry probe failed: " + e);
                code = 1;
            }
            finally
            {
                Application.logMessageReceived -= Capture;
                GraphicsSettings.defaultRenderPipeline = previous;
                m_Camera.cameraType = CameraType.Game;
            }
        }
        if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
        else
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    void Capture(string message, string stackTrace, LogType type)
    {
        // Unbound's own notes are not the Editor's.
        if (message.StartsWith("Unbound:", StringComparison.Ordinal)) return;
        m_Log.Add(type + ": " + message);
    }

    void Setup()
    {
        m_Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
        m_Read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        m_Camera = new GameObject("Camera").AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.targetTexture = m_Target;
        m_Camera.transform.position = Vector3.zero;
        m_Camera.fieldOfView = 60f;
        m_Camera.nearClipPlane = 0.3f;
        m_Camera.farClipPlane = 100f;

        // A world space canvas 3 units ahead, left of the centre: a red quad 0.4 units wide.
        var world = new GameObject("WorldCanvas", typeof(RectTransform));
        world.layer = CanvasLayer;
        var worldCanvas = world.AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        var worldRect = (RectTransform)world.transform;
        worldRect.sizeDelta = new Vector2(100f, 100f);
        m_WorldQuad = new Vector3(-0.6f, 0f, 3f);
        worldRect.position = m_WorldQuad;
        worldRect.localScale = Vector3.one * 0.01f;
        Quad(world.transform, Vector2.zero, 20f, new Color(1f, 0f, 0f, 0.5f));

        // The camera's own screen space canvas, 2 units ahead: a green quad right of the centre.
        var screen = new GameObject("ScreenCanvas", typeof(RectTransform));
        screen.layer = CanvasLayer;
        var screenCanvas = screen.AddComponent<Canvas>();
        screenCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        screenCanvas.worldCamera = m_Camera;
        screenCanvas.planeDistance = 2f;
        Quad(screen.transform, new Vector2(16f, 0f), 6f, new Color(0f, 1f, 0f, 0.5f));

        // A third, world space canvas above the others: a half-transparent red quad, then a blue
        // one over its right half.
        var overlap = new GameObject("OverlapCanvas", typeof(RectTransform));
        overlap.layer = CanvasLayer;
        overlap.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var overlapRect = (RectTransform)overlap.transform;
        overlapRect.sizeDelta = new Vector2(100f, 100f);
        overlapRect.position = new Vector3(0f, 0.8f, 3f);
        overlapRect.localScale = Vector3.one * 0.01f;
        Quad(overlap.transform, Vector2.zero, 20f, new Color(1f, 0f, 0f, 0.5f));
        Quad(overlap.transform, new Vector2(10f, 0f), 20f, new Color(0f, 0f, 1f, 0.5f));
        m_OverlapQuad = new Vector3(0.1f, 0.8f, 3f);
    }

    /// <summary>A canvas element drawing a square of half size <paramref name="half"/> (canvas
    /// units) of one colour at <paramref name="centre"/>, with the default UI material.</summary>
    static void Quad(Transform canvas, Vector2 centre, float half, Color color)
    {
        var go = new GameObject("Quad", typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = CanvasLayer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(canvas, false);
        rect.anchoredPosition = centre;
        rect.sizeDelta = new Vector2(2f * half, 2f * half);
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-half, -half), new Vector3(-half, half), new Vector3(half, half), new Vector3(half, -half) },
            colors = new[] { color, color, color, color },
            uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
            triangles = new[] { 0, 1, 2, 2, 3, 0 },
        };
        var renderer = go.GetComponent<CanvasRenderer>();
        renderer.SetMesh(mesh);
        renderer.materialCount = 1;
        renderer.SetMaterial(Canvas.GetDefaultCanvasMaterial(), 0);
        renderer.SetTexture(Texture2D.whiteTexture);
    }

    /// <summary>One render as <paramref name="type"/>, emitting as <paramref name="emit"/> asks:
    /// the colour at both quads' centres and what was logged.</summary>
    void Render(string key, CameraType type, int emit)
    {
        m_Camera.cameraType = type;
        ProbePipelineAsset.Emit = emit;
        m_Log.Clear();
        m_Camera.Render();
        ProbePipelineAsset.Emit = 0;
        Read(m_Target);
        var world = m_Camera.WorldToViewportPoint(m_WorldQuad);
        Rec(key + ".world", Bytes(m_Read.GetPixel((int)(world.x * Size), (int)(world.y * Size))));
        Rec(key + ".screen", Bytes(m_Read.GetPixel(Size / 2 + 16, Size / 2)));
        Rec(key + ".logs", m_Log.Count.ToString());
        for (int i = 0; i < m_Log.Count; i++) Rec(key + ".log" + i, Str(m_Log[i]));
    }

    /// <summary>A Game camera's render with the pipeline's target mode <paramref name="target"/>
    /// (<see cref="ProbePipelineAsset.Target"/>), the camera's target and the other one filled
    /// with blue first: the colour of both at the world quad's centre.</summary>
    void RenderTargets(string key, int target)
    {
        Fill(m_Target);
        Fill(m_Other);
        ProbePipelineAsset.Target = target;
        try { Render(key, CameraType.Game, 0); }
        finally { ProbePipelineAsset.Target = 0; }
        var world = m_Camera.WorldToViewportPoint(m_WorldQuad);
        Rec(key + ".otherWorld", Bytes(Read(m_Other).GetPixel((int)(world.x * Size), (int)(world.y * Size))));
    }

    /// <summary>A Game camera's render emitting as <paramref name="emit"/> asks: the colour where
    /// the third canvas's quads overlap.</summary>
    void Overlap(string key, int emit)
    {
        m_Camera.cameraType = CameraType.Game;
        ProbePipelineAsset.Emit = emit;
        m_Camera.Render();
        ProbePipelineAsset.Emit = 0;
        var at = m_Camera.WorldToViewportPoint(m_OverlapQuad);
        Rec(key + ".overlap", Bytes(Read(m_Target).GetPixel((int)(at.x * Size), (int)(at.y * Size))));
    }

    /// <summary>A Game camera covering the right half of its target (Camera.rect) with the
    /// pipeline's target mode <paramref name="target"/>, the target filled with blue first: the
    /// colour left of the camera's rectangle, at the world quad and at the screen quad.</summary>
    void Viewport(string key, int target)
    {
        Fill(m_Target);
        m_Camera.rect = new Rect(0.5f, 0f, 0.5f, 1f);
        ProbePipelineAsset.Target = target;
        try
        {
            m_Camera.cameraType = CameraType.Game;
            m_Log.Clear();
            m_Camera.Render();
            Read(m_Target);
            var world = m_Camera.WorldToScreenPoint(m_WorldQuad);
            Rec(key + ".left", Bytes(m_Read.GetPixel(Size / 4, Size / 2)));
            Rec(key + ".world", Bytes(m_Read.GetPixel((int)world.x, (int)world.y)));
            Rec(key + ".screen", Bytes(m_Read.GetPixel(Size - 3, Size / 2)));
            Rec(key + ".logs", m_Log.Count.ToString());
        }
        finally
        {
            ProbePipelineAsset.Target = 0;
            m_Camera.rect = new Rect(0f, 0f, 1f, 1f);
        }
    }

    static void Fill(RenderTexture target)
    {
        var active = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(true, true, Color.blue);
        RenderTexture.active = active;
    }

    Texture2D Read(RenderTexture target)
    {
        var active = RenderTexture.active;
        RenderTexture.active = target;
        m_Read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        m_Read.Apply();
        RenderTexture.active = active;
        return m_Read;
    }

    static string Bytes(Color c)
    {
        Color32 b = c;
        return "[" + b.r + "," + b.g + "," + b.b + "]";
    }
}
