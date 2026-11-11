using System;
using System.Collections.Generic;
using Unbound.UIElements;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Object to draw 2D vector graphics (Unity 6000.0 Scripting API, Painter2D): paths of lines,
    /// arcs and Bézier curves, filled with <see cref="fillColor"/> by a fill rule or stroked with
    /// <see cref="lineWidth"/>, <see cref="strokeColor"/>, <see cref="lineJoin"/>,
    /// <see cref="lineCap"/> and <see cref="miterLimit"/>. A painter from
    /// <see cref="MeshGenerationContext.painter2D"/> draws into the element's content during its
    /// generateVisualContent callback; a detached painter keeps what it draws for
    /// <see cref="SaveToVectorImage"/>.
    /// </summary>
    /// <remarks>
    /// The geometry matches the Unity 6000.0.71f1 Editor vertex for vertex
    /// (tests/projects/uitkpainter; <see cref="UaVectorStroke"/>, <see cref="UaVectorFill"/>):
    /// anti-aliased edges through UIR's arc data, measured quirks included (paths drawn before
    /// BeginPath log an error and draw nothing; the context's painter starts every element reset;
    /// a closed path opened by QuadraticCurveTo closes to the origin). Fills of arcs and curves can
    /// triangulate their interior in another order where the triangulation of co-circular points is
    /// a tie; the covered area and every anti-aliased edge are the same. <see cref="strokeGradient"/>
    /// colours a stroke along its length (see <see cref="Unbound.UIElements.UaVectorStroke"/>).
    /// </remarks>
    public partial class Painter2D : IDisposable
    {
        readonly MeshGenerationContext m_UaContext;
        readonly bool m_UaIsDetached;
        readonly UaVectorPath m_UaPath = new UaVectorPath();
        readonly UaVectorMesh m_UaScratch = new UaVectorMesh();
        // A detached painter's content: everything Fill and Stroke drew, and its bounds.
        readonly UaVectorMesh m_UaDetached;
        UaVectorStroke m_UaStroker;
        UaVectorFill m_UaFiller;
        // The gradient last set, also after null turned it off: the getter keeps answering it.
        UaStrokeGradient m_UaStrokeGradient;
        bool m_UaBegun;
        bool m_UaDisposed;

        UaVectorStyle m_UaStyle;

        /// <summary>Whether context painters may draw: during generateVisualContent callbacks (and,
        /// as in Unity, after any detached painter was created until the next callback ends).</summary>
        internal static bool isPainterActive { get; set; }

        internal Painter2D(MeshGenerationContext ctx)
        {
            m_UaContext = ctx;
            Reset();
        }

        /// <summary>A detached painter: what it draws is kept until <see cref="Clear"/>.</summary>
        public Painter2D()
        {
            m_UaIsDetached = true;
            m_UaDetached = new UaVectorMesh();
            isPainterActive = true;
            Reset();
        }

        internal bool isDetached => m_UaIsDetached;

        /// <summary>Starts over: the default state (line width 1, black, miter joins, butt caps,
        /// miter limit 10), an empty path, and no path begun.</summary>
        internal void Reset()
        {
            m_UaStyle = new UaVectorStyle
            {
                LineWidth = 1f,
                StrokeColor = Color.black,
                FillColor = Color.black,
                Join = LineJoin.Miter,
                Cap = LineCap.Butt,
                MiterLimit = 10f,
            };
            // The gradient is off again; the getter still answers the last one set.
            m_UaPath.Clear();
            m_UaBegun = false;
        }

        public float lineWidth { get => m_UaStyle.LineWidth; set => m_UaStyle.LineWidth = value; }

        /// <summary>The stroke colour. Setting it turns a stroke gradient off: whichever of the two
        /// was set last is used (Unity 6000.0.71f1, tests/projects/uitkpainter gradient-color-set-after).</summary>
        public Color strokeColor
        {
            get => m_UaStyle.StrokeColor;
            set
            {
                m_UaStyle.StrokeColor = value;
                m_UaStyle.Gradient = null;
            }
        }

        /// <summary>The stroke gradient. Documented: a gradient overrides <see cref="strokeColor"/>
        /// and null falls back on it. The painter keeps the gradient as it is when set (later
        /// changes to the caller's object do not show); the getter answers a copy of the last one
        /// set, also after null, and a white gradient before any (Unity 6000.0.71f1,
        /// tests/projects/uitkpainter).</summary>
        public Gradient strokeGradient
        {
            get => m_UaStrokeGradient != null ? m_UaStrokeGradient.Copy() : new Gradient();
            set
            {
                if (value == null)
                {
                    m_UaStyle.Gradient = null;
                    return;
                }
                m_UaStrokeGradient = new UaStrokeGradient(value);
                m_UaStyle.Gradient = m_UaStrokeGradient;
            }
        }

        public Color fillColor { get => m_UaStyle.FillColor; set => m_UaStyle.FillColor = value; }

        public LineJoin lineJoin { get => m_UaStyle.Join; set => m_UaStyle.Join = value; }

        public LineCap lineCap { get => m_UaStyle.Cap; set => m_UaStyle.Cap = value; }

        public float miterLimit { get => m_UaStyle.MiterLimit; set => m_UaStyle.MiterLimit = value; }

        /// <summary>When created as a detached painter, clears the current content.</summary>
        public void Clear()
        {
            if (!m_UaIsDetached)
            {
                Debug.LogError("Clear() cannot be called on a Painter2D associated with a MeshGenerationContext. You should create your own instance of Painter2D instead.");
                return;
            }
            m_UaDetached.Clear();
            m_UaDetached.ClearBounds();
            Reset();
        }

        /// <summary>Releases the painter's content.</summary>
        public void Dispose()
        {
            if (m_UaDisposed) return;
            m_UaDisposed = true;
            m_UaDetached?.Clear();
            m_UaPath.Clear();
        }

        bool UaValidate()
        {
            bool valid = m_UaIsDetached || isPainterActive;
            if (!valid) Debug.LogError("Cannot issue vector graphics commands outside of generateVisualContent callback");
            return valid;
        }

        bool UaBegun(string call)
        {
            if (m_UaBegun) return true;
            // Measured: the console and the log callbacks see this report, the game's ILogHandler
            // does not (tests/projects/uitkpainter).
            Unbound.Runtime.Log.Write(LogType.Error, "Calling " + call + "() before BeginPath()", "", null);
            return false;
        }

        /// <summary>Begins a new path and empties the list of recorded sub-paths.</summary>
        public void BeginPath()
        {
            if (!UaValidate()) return;
            m_UaPath.Clear();
            m_UaBegun = true;
        }

        /// <summary>Closes the current sub-path with a straight line; does nothing if it is already
        /// closed.</summary>
        public void ClosePath()
        {
            if (UaValidate() && m_UaBegun) m_UaPath.ClosePath();
        }

        /// <summary>Begins a new sub-path at <paramref name="pos"/>.</summary>
        public void MoveTo(Vector2 pos)
        {
            if (UaValidate() && UaBegun("MoveTo")) m_UaPath.MoveTo(pos);
        }

        /// <summary>Adds a straight line to <paramref name="pos"/> (without a current point, it
        /// becomes the current point).</summary>
        public void LineTo(Vector2 pos)
        {
            if (UaValidate() && UaBegun("LineTo")) m_UaPath.LineTo(pos);
        }

        /// <summary>Adds an arc tangent to the lines from the current point to
        /// <paramref name="p1"/> and from <paramref name="p1"/> to <paramref name="p2"/>, preceded by
        /// a line to its first tangent point.</summary>
        public void ArcTo(Vector2 p1, Vector2 p2, float radius)
        {
            if (UaValidate() && UaBegun("ArcTo")) m_UaPath.ArcTo(p1, p2, radius);
        }

        /// <summary>Adds an arc of <paramref name="radius"/> around <paramref name="center"/> from
        /// <paramref name="startAngle"/> to <paramref name="endAngle"/>, preceded by a line from the
        /// current point to its start. Angles grow clockwise on screen (UI Toolkit's y axis points
        /// down).</summary>
        public void Arc(Vector2 center, float radius, Angle startAngle, Angle endAngle, ArcDirection direction = ArcDirection.Clockwise)
        {
            if (UaValidate() && UaBegun("Arc")) m_UaPath.Arc(center, radius, startAngle.ToRadians(), endAngle.ToRadians(), direction);
        }

        /// <summary>Adds a cubic Bézier curve to <paramref name="p3"/> with control points
        /// <paramref name="p1"/> and <paramref name="p2"/>.</summary>
        public void BezierCurveTo(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            if (UaValidate() && UaBegun("BezierTo")) m_UaPath.BezierCurveTo(p1, p2, p3);
        }

        /// <summary>Adds a quadratic Bézier curve to <paramref name="p2"/> with control point
        /// <paramref name="p1"/>.</summary>
        public void QuadraticCurveTo(Vector2 p1, Vector2 p2)
        {
            if (UaValidate() && UaBegun("QuadraticCurveTo")) m_UaPath.QuadraticCurveTo(p1, p2);
        }

        /// <summary>Strokes the current path.</summary>
        public void Stroke()
        {
            if (!UaValidate() || !UaBegun("Stroke")) return;
            var mesh = UaTarget();
            (m_UaStroker ??= new UaVectorStroke()).Stroke(m_UaPath, m_UaStyle, mesh);
            UaEmit(mesh);
        }

        /// <summary>Fills the current path (each sub-path closed) with <see cref="fillColor"/> by
        /// <paramref name="fillRule"/>.</summary>
        public void Fill(FillRule fillRule = FillRule.NonZero)
        {
            if (!UaValidate() || !m_UaBegun) return;
            var mesh = UaTarget();
            (m_UaFiller ??= new UaVectorFill()).Fill(m_UaPath, m_UaStyle.FillColor, fillRule, mesh);
            UaEmit(mesh);
        }

        UaVectorMesh UaTarget()
        {
            if (m_UaIsDetached) return m_UaDetached;
            m_UaScratch.Clear();
            return m_UaScratch;
        }

        void UaEmit(UaVectorMesh mesh)
        {
            if (m_UaIsDetached || mesh.VertexCount == 0) return;
            m_UaContext.uaPainter.QueueVectorMesh(mesh);
        }

        /// <summary>Saves the detached painter's content (its vertices relative to the content's
        /// bounds) into <paramref name="vectorImage"/>.</summary>
        public bool SaveToVectorImage(VectorImage vectorImage)
        {
            if (!m_UaIsDetached)
            {
                Debug.LogError("SaveToVectorImage cannot be called on a Painter2D associated with a MeshGenerationContext. You should create your own instance of Painter2D instead.");
                return false;
            }
            if (vectorImage == null) throw new NullReferenceException("The provided vectorImage is null");
            var mesh = m_UaDetached;
            var min = mesh.BoundsMin;
            var vertices = new VectorImageVertex[mesh.VertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                ref var v = ref mesh.Vertices[i];
                vertices[i] = new VectorImageVertex
                {
                    position = new Vector3(v.position.x - min.x, v.position.y - min.y, Vertex.nearZ),
                    tint = v.tint,
                    flags = new Color32(0, 0, v.arc ? (byte)1 : (byte)0, 0),
                    circle = v.circle,
                };
            }
            var indices = new ushort[mesh.IndexCount];
            for (int i = 0; i < indices.Length; i++) indices[i] = (ushort)mesh.Indices[i];
            vectorImage.version = 0;
            vectorImage.vertices = vertices;
            vectorImage.indices = indices;
            vectorImage.size = mesh.BoundsMax - mesh.BoundsMin;
            return true;
        }
    }
}

namespace UnityEngine.UIElements
{
    // The enums Painter2D's members use, declared here (not left to the generated stubs) so that the
    // hand-written code builds alone for `unbound gen-stubs`. Values from the 6000.0 Scripting API.

    /// <summary>Direction to draw an arc.</summary>
    public enum ArcDirection
    {
        Clockwise = 0,
        CounterClockwise = 1,
    }

    /// <summary>The fill rule of a filled path.</summary>
    public enum FillRule
    {
        NonZero = 0,
        OddEven = 1,
    }

    /// <summary>The shape at the end of a stroked line.</summary>
    public enum LineCap
    {
        Butt = 0,
        Round = 1,
    }

    /// <summary>The shape where two stroked segments meet.</summary>
    public enum LineJoin
    {
        Miter = 0,
        Bevel = 1,
        Round = 2,
    }
}
