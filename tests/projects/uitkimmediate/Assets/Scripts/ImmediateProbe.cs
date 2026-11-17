using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImmediateProbe
{
    /// <summary>A probe element: counts its ImmediateRepaint calls, records GL's state during the
    /// first call of a frame, and draws a GL quad (local coordinates) in its color, or throws.</summary>
    public class QuadElement : ImmediateModeElement
    {
        internal static Material Material;
        internal static RenderTexture Target;
        internal Rect Quad;
        internal Color Color;
        internal bool Throw;
        internal bool LoadOrtho;
        internal int Calls;
        internal string State;

        protected override void ImmediateRepaint()
        {
            Calls++;
            if (State == null)
            {
                var m = GL.modelview;
                State = "modelview " + Row(m.GetRow(0)) + " / " + Row(m.GetRow(1)) + " / " + Row(m.GetRow(2)) + " / " + Row(m.GetRow(3))
                    + "; active " + (RenderTexture.active == Target ? "target" : RenderTexture.active == null ? "null" : RenderTexture.active.name)
                    + "; camera " + (Camera.current == null ? "null" : Camera.current.name);
            }
            if (Throw) throw new InvalidOperationException("probe failure in " + name);
            Material.SetPass(0);
            if (LoadOrtho)
            {
                GL.PushMatrix();
                GL.LoadOrtho();
            }
            GL.Begin(GL.QUADS);
            GL.Color(Color);
            GL.Vertex3(Quad.xMin, Quad.yMin, 0);
            GL.Vertex3(Quad.xMax, Quad.yMin, 0);
            GL.Vertex3(Quad.xMax, Quad.yMax, 0);
            GL.Vertex3(Quad.xMin, Quad.yMax, 0);
            GL.End();
            if (LoadOrtho) GL.PopMatrix();
        }

        static string Row(Vector4 v) => string.Join(" ", new[] { v.x, v.y, v.z, v.w }.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture)));
    }

    static class Linq
    {
        internal static IEnumerable<string> Select(this float[] values, Func<float, string> f)
        {
            foreach (var v in values) yield return f(v);
        }
    }

    /// <summary>
    /// ImmediateModeElement on a runtime panel drawing into an 800x600 render texture (cleared each
    /// frame): GL quads drawn by ImmediateRepaint in the elements' local space, clipped by an
    /// overflow-hidden parent, under a scaled element, before and after elements with backgrounds,
    /// with GL.LoadOrtho, culled and not culled outside the target, while hidden, fully transparent
    /// (itself or under a transparent parent), after an exception, and at panel scale 2. Values: GL's modelview, the active render texture and camera during the
    /// callback, the calls per frame, the logs, and the target's pixels at chosen points.
    /// </summary>
    public class ImmediateProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Logs = new List<string>();
        readonly Dictionary<string, QuadElement> m_Quads = new Dictionary<string, QuadElement>();
        VisualElement m_Root;
        RenderTexture m_Target;
        PanelSettings m_Settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_IMMEDIATE_OUT"))) return;
            var go = new GameObject("probe");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
            go.AddComponent<ImmediateProbeBehaviour>();
        }

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("UITK_IMMEDIATE_OUT");
            var document = GetComponent<UIDocument>();
            m_Settings = document.panelSettings;
            m_Target = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32) { name = "probeTarget" };
            m_Target.Create();
            QuadElement.Target = m_Target;
            var material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            QuadElement.Material = material;
            m_Settings.targetTexture = m_Target;
            m_Settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            m_Settings.scale = 1;
            m_Settings.clearColor = true;
            m_Settings.colorClearValue = Color.black;
            m_Root = document.rootVisualElement;
            Application.logMessageReceived += (condition, stack, type) => m_Logs.Add(type + ": " + condition);
            Build();
            bool failed = false;
            var steps = Record();
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext()) break;
                    current = steps.Current;
                }
                catch (Exception e)
                {
                    Put("error", e.GetType().Name + ": " + e.Message);
                    failed = true;
                    break;
                }
                yield return current;
            }
            if (!failed) Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

        void Put(string key, object value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value is bool b ? (b ? "true" : "false") : Json(Convert.ToString(value, Inv))).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static VisualElement Box(string name, float left, float top, float width, float height)
        {
            var e = new VisualElement { name = name };
            Place(e, left, top, width, height);
            return e;
        }

        static void Place(VisualElement e, float left, float top, float width, float height)
        {
            e.style.position = Position.Absolute;
            e.style.left = left;
            e.style.top = top;
            e.style.width = width;
            e.style.height = height;
        }

        QuadElement Quad(VisualElement parent, string name, float left, float top, float width, float height, Rect quad, Color color)
        {
            var e = new QuadElement { name = name, Quad = quad, Color = color };
            Place(e, left, top, width, height);
            parent.Add(e);
            m_Quads[name] = e;
            return e;
        }

        void Build()
        {
            // Spills 10 points around its 100x60 box (no clipping ancestor).
            Quad(m_Root, "spill", 40, 40, 100, 60, new Rect(-10, -10, 120, 80), Color.red);
            // Inside an overflow-hidden parent (200,40 80x80): the quad covers more than the parent.
            var clipper = Box("clipper", 200, 40, 80, 80);
            clipper.style.overflow = Overflow.Hidden;
            m_Root.Add(clipper);
            Quad(clipper, "clipped", 20, 20, 40, 40, new Rect(-30, -30, 100, 100), Color.green);
            // Scaled 2x3 about its top left.
            var scaled = Quad(m_Root, "scaled", 340, 40, 20, 20, new Rect(0, 0, 20, 20), Color.blue);
            scaled.style.transformOrigin = new TransformOrigin(0, 0, 0);
            scaled.style.scale = new Scale(new Vector2(2, 3));
            // Drawn before a later sibling's background that overlaps it.
            Quad(m_Root, "under", 450, 40, 60, 60, new Rect(0, 0, 60, 60), Color.cyan);
            var cover = Box("cover", 480, 40, 60, 60);
            cover.style.backgroundColor = Color.yellow;
            m_Root.Add(cover);
            // Drawn after an earlier sibling's background that it overlaps.
            var baseBox = Box("base", 600, 40, 60, 60);
            baseBox.style.backgroundColor = Color.white;
            m_Root.Add(baseBox);
            Quad(m_Root, "over", 630, 40, 60, 60, new Rect(0, 0, 60, 60), Color.magenta);
            // GL.LoadOrtho during the callback, then an element drawn after it.
            // Orange with channels whole bytes, so no device rounds a half.
            var ortho = Quad(m_Root, "ortho", 40, 200, 20, 20, new Rect(0.9f, 0.9f, 0.1f, 0.1f), new Color32(255, 102, 0, 255));
            ortho.LoadOrtho = true;
            var after = Box("after", 80, 200, 40, 40);
            after.style.backgroundColor = Color.yellow;
            m_Root.Add(after);
            // Outside the target (800 wide), culled and not; one partly inside, culled.
            Quad(m_Root, "culledOutside", 900, 300, 40, 40, new Rect(0, 0, 40, 40), Color.red).cullingEnabled = true;
            Quad(m_Root, "drawnOutside", 900, 360, 40, 40, new Rect(0, 0, 40, 40), Color.red);
            Quad(m_Root, "culledPartly", 780, 420, 40, 40, new Rect(0, 0, 40, 40), Color.green).cullingEnabled = true;
            // Hidden, and one that throws when asked (between two that draw).
            Quad(m_Root, "hidden", 200, 300, 40, 40, new Rect(0, 0, 40, 40), Color.red).style.visibility = Visibility.Hidden;
            Quad(m_Root, "beforeThrow", 200, 400, 20, 20, new Rect(0, 0, 20, 20), Color.green);
            Quad(m_Root, "thrower", 240, 400, 20, 20, new Rect(0, 0, 20, 20), Color.green);
            Quad(m_Root, "afterThrow", 280, 400, 20, 20, new Rect(0, 0, 20, 20), Color.green);
            // Fully transparent: the element itself, and an element under a transparent parent.
            Quad(m_Root, "transparent", 360, 400, 20, 20, new Rect(0, 0, 20, 20), Color.blue).style.opacity = 0f;
            var fadedParent = Box("fadedParent", 400, 400, 40, 40);
            fadedParent.style.opacity = 0f;
            m_Root.Add(fadedParent);
            Quad(fadedParent, "underTransparent", 10, 10, 20, 20, new Rect(0, 0, 20, 20), Color.blue);
        }

        void ResetCalls()
        {
            foreach (var q in m_Quads.Values) { q.Calls = 0; q.State = null; }
        }

        string Calls()
        {
            var parts = new List<string>();
            foreach (var kv in m_Quads) parts.Add(kv.Key + "=" + kv.Value.Calls);
            return string.Join(" ", parts);
        }

        Texture2D m_Read;

        /// <summary>The target's color at panel pixel (x, y) from its top left, as 0-255 RGBA.</summary>
        string Pixel(int x, int y)
        {
            var c = m_Read.GetPixel(x, m_Target.height - 1 - y);
            Color32 c32 = c;
            return c32.r + " " + c32.g + " " + c32.b + " " + c32.a;
        }

        void ReadTarget()
        {
            var old = RenderTexture.active;
            RenderTexture.active = m_Target;
            if (m_Read == null) m_Read = new Texture2D(m_Target.width, m_Target.height, TextureFormat.RGBA32, false);
            m_Read.ReadPixels(new Rect(0, 0, m_Target.width, m_Target.height), 0, 0);
            m_Read.Apply();
            RenderTexture.active = old;
        }

        void PutPixels(string prefix, params (string name, int x, int y)[] points)
        {
            foreach (var p in points) Put(prefix + "." + p.name, Pixel(p.x, p.y));
        }

        IEnumerator Record()
        {
            // Layout, then frames that draw.
            for (int i = 0; i < 4; i++) yield return null;
            ResetCalls();
            m_Logs.Clear();
            yield return null;
            yield return null;
            yield return null;
            // Three frames without changes.
            Put("calls.threeFrames", Calls());
            foreach (var kv in m_Quads) if (kv.Value.State != null) Put("state." + kv.Key, kv.Value.State);
            ReadTarget();
            PutPixels("pixels",
                ("spill.inside", 90, 70), ("spill.leftMargin", 35, 70), ("spill.topMargin", 90, 35), ("spill.beyond", 25, 70),
                ("clipped.inside", 240, 80), ("clipped.parentCorner", 203, 43), ("clipped.outsideParent", 195, 80), ("clipped.belowParent", 240, 125),
                ("scaled.far", 375, 95), ("scaled.beyondX", 385, 50), ("scaled.beyondY", 345, 105),
                ("under.alone", 460, 70), ("under.overlap", 495, 70), ("cover.alone", 530, 70),
                ("base.alone", 610, 70), ("over.overlap", 645, 70), ("over.alone", 680, 70),
                ("ortho.element", 50, 210), ("ortho.topRight", 795, 5), ("ortho.bottomRight", 795, 595), ("after.alone", 100, 220),
                ("culledPartly.inside", 790, 440), ("hidden.inside", 220, 320), ("beforeThrow.inside", 210, 410),
                ("transparent.inside", 370, 410), ("underTransparent.inside", 420, 420));
            Put("logs.drawing", string.Join(" | ", m_Logs));

            // A throwing callback: the frame's logs and calls.
            m_Quads["thrower"].Throw = true;
            ResetCalls();
            m_Logs.Clear();
            yield return null;
            Put("throw.calls", Calls());
            Put("throw.logs", string.Join(" | ", m_Logs));
            m_Quads["thrower"].Throw = false;
            yield return null;
            ResetCalls();
            m_Logs.Clear();
            yield return null;
            Put("afterThrow.calls", Calls());
            Put("afterThrow.logs", string.Join(" | ", m_Logs));

            // cullingEnabled turned on for an element outside, and off for one.
            m_Quads["drawnOutside"].cullingEnabled = true;
            m_Quads["culledOutside"].cullingEnabled = false;
            ResetCalls();
            yield return null;
            Put("cullingSwapped.calls", Calls());
            m_Quads["drawnOutside"].cullingEnabled = false;
            m_Quads["culledOutside"].cullingEnabled = true;

            // Panel scale 2: the quads' places and the matrices.
            m_Settings.scale = 2;
            for (int i = 0; i < 3; i++) yield return null;
            ResetCalls();
            m_Logs.Clear();
            yield return null;
            yield return null;
            Put("scale2.calls", Calls());
            foreach (var name in new[] { "spill", "scaled", "clipped" }) Put("scale2.state." + name, m_Quads[name].State);
            ReadTarget();
            PutPixels("scale2",
                ("spill.inside", 180, 140), ("spill.leftMargin", 65, 140), ("spill.beyond", 55, 140),
                ("clipped.inside", 480, 160), ("clipped.outsideParent", 395, 160),
                ("scaled.far", 750, 190), ("ortho.topRight", 795, 5));
            Put("scale2.logs", string.Join(" | ", m_Logs));
        }
    }
}
