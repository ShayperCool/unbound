using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// What the Game view shows of stacked screen cameras (built-in RP) whose upper cameras clear
/// only depth or nothing, at full and partial rects, with and without HDR and MSAA.
///
/// Three orthographic cameras look down +z from the same place, each drawing its own layer:
/// <list type="bullet">
/// <item>A (depth 0, full rect, solid dark blue clear): an opaque red quad, rotated, 4 units away.</item>
/// <item>B (depth 1, the case's clear flags, rect, HDR, MSAA and rendering path): an opaque green
/// quad of HDR intensity 8 units away (behind A's red one) and a half-transparent blue quad 2
/// units away (in front, blended over what is under it), and a small opaque cyan quad 5 units
/// away, under C's rect.</item>
/// <item>C (depth 2, Don't Clear, LDR, right-hand rect over parts of A's red quad and B's quads):
/// a yellow quad 6 units away over its whole view. Where the screen's depth is nearer (A's red
/// quad, B's cyan one) it stays hidden: what C shows is the depth the screen kept.</item>
/// </list>
/// Each case starts with one frame in which A alone clears the screen (color magenta, and depth)
/// and draws nothing. Then the case's cameras and quality AA render; the Game view is captured
/// after the case's first and fourth frames (<c>STACK_PROBE_OUT</c>/&lt;case&gt;.f1.png, .f4.png;
/// the screen keeps its color and depth from frame to frame). A "first" case disables A, so B is
/// the first camera on the screen.
/// </summary>
public class StackProbe : MonoBehaviour
{
    public static Action<int> finished;

    const int LayerA = 8, LayerB = 9, LayerC = 10;
    static readonly Rect Full = new Rect(0f, 0f, 1f, 1f);
    // Whole pixels at the probe's 160x90 Game view.
    static readonly Rect PartialB = new Rect(0.1f, 0.2f, 0.6f, 0.6f);
    static readonly Rect RectC = new Rect(0.5f, 0.3f, 0.45f, 0.6f);

    struct Case
    {
        public string Name;
        public int Quality;          // QualitySettings.antiAliasing
        public bool A;               // A renders (else B is first on the screen)
        public bool AHdr, AMsaa;
        public CameraClearFlags BClear;
        public bool BPartial, BHdr, BMsaa, BDeferred;
        public bool CFull;           // C covers the whole screen (else its right-hand rect)
    }

    static List<Case> Cases()
    {
        var list = new List<Case>();
        void Add(int q, bool a, bool aHdr, bool aMsaa, CameraClearFlags clear, bool partial, bool hdr, bool msaa, bool deferred, bool cFull = false)
        {
            var name = new StringBuilder();
            name.Append("q").Append(q);
            name.Append(a ? (aHdr ? ".aH" : ".aL") + (aMsaa ? "" : "n") : ".first");
            name.Append(clear == CameraClearFlags.Depth ? ".bD" : clear == CameraClearFlags.SolidColor ? ".bS" : ".bN");
            name.Append(partial ? ".part" : ".full");
            name.Append(hdr ? ".bH" : ".bL");
            if (!msaa) name.Append("n");
            if (deferred) name.Append(".def");
            if (cFull) name.Append(".cF");
            list.Add(new Case
            {
                Name = name.ToString(), Quality = q, A = a, AHdr = aHdr, AMsaa = aMsaa, BClear = clear,
                BPartial = partial, BHdr = hdr, BMsaa = msaa, BDeferred = deferred, CFull = cFull,
            });
        }
        var clears = new[] { CameraClearFlags.Depth, CameraClearFlags.Nothing };
        // The main matrix: quality AA off and 4, A LDR or HDR, B's clear, rect and HDR.
        foreach (int q in new[] { 0, 4 })
            foreach (bool aHdr in new[] { false, true })
                foreach (var clear in clears)
                    foreach (bool partial in new[] { false, true })
                        foreach (bool hdr in new[] { false, true })
                            Add(q, true, aHdr, true, clear, partial, hdr, true, false);
        // B without MSAA (allowMSAA false) while the quality asks for 4 samples.
        foreach (var clear in clears)
            foreach (bool partial in new[] { false, true })
                foreach (bool hdr in new[] { false, true })
                    Add(4, true, false, true, clear, partial, hdr, false, false);
        // A without MSAA under quality AA 4.
        foreach (bool aHdr in new[] { false, true })
            foreach (bool hdr in new[] { false, true })
                Add(4, true, aHdr, false, CameraClearFlags.Nothing, false, hdr, true, false);
        // B is the first camera on the screen.
        foreach (int q in new[] { 0, 4 })
            foreach (var clear in clears)
                foreach (bool partial in new[] { false, true })
                    foreach (bool hdr in new[] { false, true })
                        Add(q, false, false, true, clear, partial, hdr, true, false);
        // B deferred (HDR, no MSAA by the path).
        foreach (var clear in clears)
            foreach (bool partial in new[] { false, true })
                Add(0, true, false, true, clear, partial, true, true, true);
        // B deferred under quality AA 4.
        foreach (var clear in clears)
            foreach (bool partial in new[] { false, true })
                Add(4, true, false, true, clear, partial, true, true, true);
        // B clears its color (solid color) inside the stack.
        foreach (int q in new[] { 0, 4 })
            foreach (bool partial in new[] { false, true })
                foreach (bool hdr in new[] { false, true })
                    Add(q, true, false, true, CameraClearFlags.SolidColor, partial, hdr, true, false);
        // A partial B between full-screen cameras: C covers the whole screen.
        foreach (int q in new[] { 0, 4 })
            foreach (var clear in clears)
                foreach (bool hdr in new[] { false, true })
                    Add(q, true, false, true, clear, true, hdr, true, false, true);
        return list;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("StackProbe").AddComponent<StackProbe>();

    readonly StringBuilder m_Values = new StringBuilder();
    string m_Directory;

    void Value(string k, string v) => m_Values.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(v).Append("}\n");
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static string B(bool b) => b ? "true" : "false";

    static Mesh QuadMesh()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    static void Quad(Mesh mesh, string name, int layer, Shader shader, Color color, Vector2 center, Vector2 size, float angle, float distance)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.position = new Vector3(center.x, center.y, -10f + distance);
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(shader) { color = color };
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static Camera MakeCamera(string name, float depth, int layer)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(0f, 0f, -10f);
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.nearClipPlane = 0.5f;
        camera.farClipPlane = 20f;
        camera.depth = depth;
        camera.cullingMask = 1 << layer;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.3f, 1f);
        go.AddComponent<RenderCounter>();
        return camera;
    }

    IEnumerator Start()
    {
        m_Directory = Environment.GetEnvironmentVariable("STACK_PROBE_OUT");
        int code = 0;
        if (string.IsNullOrEmpty(m_Directory))
        {
            Debug.LogError("[camerastack] STACK_PROBE_OUT is not set");
            code = 2;
        }
        else
        {
            Directory.CreateDirectory(m_Directory);
            var unlit = Shader.Find("Unlit/Color");
            var blended = Shader.Find("Sprites/Default");
            if (unlit == null || blended == null)
            {
                Value("fatal", Q("missing shader"));
                code = 3;
            }
            else
            {
                var mesh = QuadMesh();
                Quad(mesh, "red", LayerA, unlit, new Color(1f, 0.1f, 0.1f, 1f), new Vector2(-0.4f, 0f), new Vector2(1.2f, 1.2f), 20f, 4f);
                Quad(mesh, "green", LayerB, unlit, new Color(0.2f, 2f, 0.2f, 1f), new Vector2(0.3f, -0.1f), new Vector2(1.6f, 1f), -10f, 8f);
                Quad(mesh, "blue", LayerB, blended, new Color(0.2f, 0.4f, 1f, 0.5f), new Vector2(-0.6f, 0.3f), new Vector2(1.4f, 0.8f), 0f, 2f);
                Quad(mesh, "cyan", LayerB, unlit, new Color(0f, 1f, 1f, 1f), new Vector2(1.2f, 0.55f), new Vector2(0.5f, 0.5f), 0f, 5f);
                Quad(mesh, "yellow", LayerC, unlit, new Color(1f, 1f, 0f, 1f), Vector2.zero, new Vector2(10f, 10f), 0f, 6f);
                var a = MakeCamera("A", 0f, LayerA);
                a.clearFlags = CameraClearFlags.SolidColor;
                var b = MakeCamera("B", 1f, LayerB);
                var c = MakeCamera("C", 2f, LayerC);
                c.clearFlags = CameraClearFlags.Nothing;
                c.rect = RectC;
                c.allowHDR = false;
                c.allowMSAA = true;
                var counters = new[] { a.GetComponent<RenderCounter>(), b.GetComponent<RenderCounter>(), c.GetComponent<RenderCounter>() };
                Value("screen", "[" + Screen.width + "," + Screen.height + "]");
                Value("colorSpace", Q(QualitySettings.activeColorSpace.ToString()));
                foreach (var test in Cases())
                {
                    // One frame of a known screen: A clears its color to magenta and its depth, B
                    // and C off. The case's first frame (f1) then starts from it; its fourth (f4)
                    // also shows what the case's own earlier frames left.
                    QualitySettings.antiAliasing = test.Quality;
                    a.enabled = true;
                    a.backgroundColor = Color.magenta;
                    a.cullingMask = 0;
                    a.allowHDR = false;
                    a.allowMSAA = true;
                    b.enabled = c.enabled = false;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    a.enabled = test.A;
                    a.backgroundColor = new Color(0.1f, 0.1f, 0.3f, 1f);
                    a.cullingMask = 1 << LayerA;
                    a.allowHDR = test.AHdr;
                    a.allowMSAA = test.AMsaa;
                    b.enabled = c.enabled = true;
                    b.clearFlags = test.BClear;
                    b.rect = test.BPartial ? PartialB : Full;
                    b.allowHDR = test.BHdr;
                    b.allowMSAA = test.BMsaa;
                    b.renderingPath = test.BDeferred ? RenderingPath.DeferredShading : RenderingPath.Forward;
                    b.backgroundColor = new Color(0.3f, 0.25f, 0.05f, 1f);
                    c.rect = test.CFull ? Full : RectC;
                    foreach (var counter in counters) counter.renders = 0;
                    for (int frame = 1; frame <= 4; frame++)
                    {
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        if (frame != 1 && frame != 4) continue;
                        string key = test.Name + ".f" + frame;
                        var shot = ScreenCapture.CaptureScreenshotAsTexture();
                        if (shot == null)
                        {
                            Value(key + ".fatal", Q("no capture"));
                            code = 4;
                            break;
                        }
                        File.WriteAllBytes(Path.Combine(m_Directory, key + ".png"), shot.EncodeToPNG());
                        Value(key + ".size", "[" + shot.width + "," + shot.height + "]");
                        Destroy(shot);
                        if (frame == 1)
                            Value(key + ".renders", "[" + counters[0].renders + "," + counters[1].renders + "," + counters[2].renders + "]");
                    }
                    if (code != 0) break;
                    Value(test.Name + ".bPath", Q(b.actualRenderingPath.ToString()));
                }
            }
        }
        Value("complete", B(code == 0));
        if (!string.IsNullOrEmpty(m_Directory))
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Values.ToString());
        Debug.Log("STACK_PROBE_DONE " + m_Directory + " " + code);
        if (finished != null) finished(code);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
