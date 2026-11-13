using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine
{
    /// <summary>What line and trail renderers share (Unity's LineParameters): width, color, caps,
    /// alignment and texture mapping along the strip.</summary>
    internal sealed class LineParameters
    {
        internal float WidthMultiplier = 1f;
        internal AnimationCurve WidthCurve = new AnimationCurve(new Keyframe(0f, 1f));
        internal Gradient ColorGradient = new Gradient();
        /// <summary>Kept as set (negative counts read back unchanged); the strip uses at least 0.</summary>
        internal int NumCornerVertices;
        internal int NumCapVertices;
        internal LineAlignment Alignment = LineAlignment.View;
        internal LineTextureMode TextureMode = LineTextureMode.Stretch;
        internal Vector2 TextureScale = Vector2.one;
        internal float ShadowBias = 0.5f;
        internal bool GenerateLightingData;

        internal void Read(YNode node)
        {
            if (node == null) return;
            WidthMultiplier = node.Float("widthMultiplier", 1f);
            var curve = node.Get("widthCurve");
            if (curve != null) WidthCurve = BuiltinReaders.AnimationCurve(curve);
            var gradient = node.Get("colorGradient");
            if (gradient != null) ColorGradient = BuiltinReaders.Gradient(gradient);
            NumCornerVertices = node.Int("numCornerVertices");
            NumCapVertices = node.Int("numCapVertices");
            Alignment = (LineAlignment)node.Int("alignment");
            TextureMode = (LineTextureMode)node.Int("textureMode");
            TextureScale = node.Get("textureScale") != null ? node.Vector2("textureScale") : Vector2.one;
            ShadowBias = node.Float("shadowBias", 0.5f);
            GenerateLightingData = node.Bool("generateLightingData");
        }

        internal LineParameters Clone() => new LineParameters
        {
            WidthMultiplier = WidthMultiplier,
            WidthCurve = new AnimationCurve(WidthCurve.keys) { preWrapMode = WidthCurve.preWrapMode, postWrapMode = WidthCurve.postWrapMode },
            ColorGradient = CloneGradient(ColorGradient),
            NumCornerVertices = NumCornerVertices,
            NumCapVertices = NumCapVertices,
            Alignment = Alignment,
            TextureMode = TextureMode,
            TextureScale = TextureScale,
            ShadowBias = ShadowBias,
            GenerateLightingData = GenerateLightingData,
        };

        static Gradient CloneGradient(Gradient g)
        {
            var copy = new Gradient { mode = g.mode, colorSpace = g.colorSpace };
            copy.SetKeys(g.colorKeys, g.alphaKeys);
            return copy;
        }

        // The start/end shortcuts edit the first and last keys (measured, tests/projects/lines api.*):
        // a one-key width curve keeps its key for the start and gains a key at 1 for the end; colors
        // are stored at 8 bits per channel.

        internal float StartWidth
        {
            get => WidthCurve.length > 0 ? WidthCurve.Evaluate(0f) * WidthMultiplier : 0f;
            set => SetWidthKey(0, value);
        }

        internal float EndWidth
        {
            get => WidthCurve.length > 0 ? WidthCurve.Evaluate(1f) * WidthMultiplier : 0f;
            set => SetWidthKey(1, value);
        }

        void SetWidthKey(int end, float width)
        {
            float value = WidthMultiplier != 0f ? width / WidthMultiplier : width;
            var keys = WidthCurve.keys;
            if (keys.Length == 0) keys = new[] { new Keyframe(end == 0 ? 0f : 1f, value) };
            else if (keys.Length == 1 && end == 1 && keys[0].time < 1f) keys = new[] { keys[0], new Keyframe(1f, value) };
            else keys[end == 0 ? 0 : keys.Length - 1].value = value;
            WidthCurve = new AnimationCurve(keys) { preWrapMode = WidthCurve.preWrapMode, postWrapMode = WidthCurve.postWrapMode };
        }

        internal Color StartColor
        {
            get => ColorGradient.Evaluate(0f);
            set => SetColorKey(0, value);
        }

        internal Color EndColor
        {
            get => ColorGradient.Evaluate(1f);
            set => SetColorKey(1, value);
        }

        void SetColorKey(int end, Color color)
        {
            Color stored = (Color32)color;
            var colors = ColorGradient.colorKeys;
            var alphas = ColorGradient.alphaKeys;
            if (colors.Length < 2) colors = new[] { new GradientColorKey(StartColor, 0f), new GradientColorKey(EndColor, 1f) };
            if (alphas.Length < 2) alphas = new[] { new GradientAlphaKey(StartColor.a, 0f), new GradientAlphaKey(EndColor.a, 1f) };
            int ci = end == 0 ? 0 : colors.Length - 1;
            int ai = end == 0 ? 0 : alphas.Length - 1;
            colors[ci].color = new Color(stored.r, stored.g, stored.b, 1f);
            alphas[ai].alpha = stored.a;
            var g = new Gradient { mode = ColorGradient.mode, colorSpace = ColorGradient.colorSpace };
            g.SetKeys(colors, alphas);
            ColorGradient = g;
        }
    }

    /// <summary>The shared behaviour of LineRenderer and TrailRenderer: a strip mesh per camera.</summary>
    internal static class LineDrawing
    {
        internal static Mesh MeshFor(Dictionary<Camera, Mesh> meshes, Camera camera, string name)
        {
            if (camera == null) camera = Camera.main;
            if (camera != null && meshes.TryGetValue(camera, out var mesh) && mesh != null) return mesh;
            if (meshes.Count > 8)
            {
                var dead = new List<Camera>();
                foreach (var pair in meshes)
                    if (pair.Key == null) dead.Add(pair.Key);
                foreach (var c in dead) meshes.Remove(c);
            }
            mesh = new Mesh { name = name };
            if (camera != null) meshes[camera] = mesh;
            return mesh;
        }

        /// <summary>A strip's build space as <paramref name="camera"/> sees it; a TransformZ
        /// strip faces <paramref name="frame"/>'s z axis (see LineSpace.TransformZOrigin).</summary>
        internal static LineSpace Space(Renderer renderer, LineParameters p, Camera camera, Matrix4x4 frame, bool trail,
            Matrix4x4 inputToWorld, Matrix4x4 worldToOutput) =>
            LineSpace.Of(p, camera.worldToCameraMatrix, camera.orthographic, camera.nearClipPlane, frame,
                Origin(renderer, p, frame, trail), inputToWorld, worldToOutput);

        internal static LineSpace Space(Renderer renderer, LineParameters p, FrameBuilder.CameraContext camera, Matrix4x4 frame, bool trail,
            Matrix4x4 inputToWorld, Matrix4x4 worldToOutput) =>
            LineSpace.Of(p, camera.View, camera.Orthographic, camera.Camera != null ? camera.Camera.nearClipPlane : 0f, frame,
                Origin(renderer, p, frame, trail), inputToWorld, worldToOutput);

        static Vector3 Origin(Renderer renderer, LineParameters p, Matrix4x4 frame, bool trail)
        {
            if (p.Alignment != LineAlignment.TransformZ) return Vector3.zero;
            var e = renderer.GetWorldBounds().extents;
            return LineSpace.TransformZOrigin(frame, Mathf.Max(e.x, Mathf.Max(e.y, e.z)), trail);
        }

        internal static Bounds BoundsOf(IReadOnlyList<Vector3> worldPoints, float width, Matrix4x4 worldToLocal)
        {
            if (worldPoints.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var b = new Bounds(worldToLocal.MultiplyPoint3x4(worldPoints[0]), Vector3.zero);
            for (int i = 1; i < worldPoints.Count; i++) b.Encapsulate(worldToLocal.MultiplyPoint3x4(worldPoints[i]));
            b.Expand(width);
            return b;
        }

        /// <summary>The transform's rotation and scale without its position (what BakeMesh's
        /// useTransform applies to a local-space strip, measured).</summary>
        internal static Matrix4x4 RotationScale(Transform transform)
        {
            var m = transform.localToWorldMatrix;
            m.m03 = m.m13 = m.m23 = 0f;
            return m;
        }
    }

    /// <summary>A strip through a list of points, in world or local space.</summary>
    [ClassId(120)]
    public sealed partial class LineRenderer : Renderer
    {
        internal LineParameters m_Parameters = new LineParameters();
        internal List<Vector3> m_Positions = new List<Vector3> { Vector3.zero, new Vector3(0f, 0f, 1f) };
        bool m_UseWorldSpace = true;
        bool m_Loop;
        SpriteMaskInteraction m_MaskInteraction;
        readonly LineStrip m_Strip = new LineStrip();
        readonly List<Vector3> m_World = new List<Vector3>();
        readonly Dictionary<Camera, Mesh> m_Meshes = new Dictionary<Camera, Mesh>();

        public LineRenderer() { }

        public float startWidth { get => m_Parameters.StartWidth; set => m_Parameters.StartWidth = value; }
        public float endWidth { get => m_Parameters.EndWidth; set => m_Parameters.EndWidth = value; }
        public float widthMultiplier { get => m_Parameters.WidthMultiplier; set => m_Parameters.WidthMultiplier = value; }
        public int numCornerVertices { get => m_Parameters.NumCornerVertices; set => m_Parameters.NumCornerVertices = value; }
        public int numCapVertices { get => m_Parameters.NumCapVertices; set => m_Parameters.NumCapVertices = value; }
        public bool useWorldSpace { get => m_UseWorldSpace; set => m_UseWorldSpace = value; }
        public bool loop { get => m_Loop; set => m_Loop = value; }
        public Color startColor { get => m_Parameters.StartColor; set => m_Parameters.StartColor = value; }
        public Color endColor { get => m_Parameters.EndColor; set => m_Parameters.EndColor = value; }
        public Vector2 textureScale { get => m_Parameters.TextureScale; set => m_Parameters.TextureScale = value; }
        public float shadowBias { get => m_Parameters.ShadowBias; set => m_Parameters.ShadowBias = value; }
        public bool generateLightingData { get => m_Parameters.GenerateLightingData; set => m_Parameters.GenerateLightingData = value; }
        public LineTextureMode textureMode { get => m_Parameters.TextureMode; set => m_Parameters.TextureMode = value; }
        public LineAlignment alignment { get => m_Parameters.Alignment; set => m_Parameters.Alignment = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        internal override SpriteMaskInteraction MaskInteraction => m_MaskInteraction;
        public AnimationCurve widthCurve { get => m_Parameters.WidthCurve; set => m_Parameters.WidthCurve = value ?? new AnimationCurve(new Keyframe(0f, 1f)); }
        public Gradient colorGradient { get => m_Parameters.ColorGradient; set => m_Parameters.ColorGradient = value ?? new Gradient(); }

        public int positionCount
        {
            get => m_Positions.Count;
            set
            {
                value = Mathf.Max(0, value);
                while (m_Positions.Count > value) m_Positions.RemoveAt(m_Positions.Count - 1);
                while (m_Positions.Count < value) m_Positions.Add(Vector3.zero);
            }
        }

        [Obsolete("Use positionCount instead (UnityUpgradable) -> positionCount", false)]
        public int numPositions { get => positionCount; set => positionCount = value; }

        [Obsolete("Use startWidth, endWidth or widthCurve instead.", false)]
        public void SetWidth(float start, float end)
        {
            startWidth = start;
            endWidth = end;
        }

        [Obsolete("Use startColor, endColor or colorGradient instead.", false)]
        public void SetColors(Color start, Color end)
        {
            startColor = start;
            endColor = end;
        }

        [Obsolete("Use positionCount instead.", false)]
        public void SetVertexCount(int count) => positionCount = count;

        // Out-of-range indices and short arrays log an error instead of throwing (measured).

        public void SetPosition(int index, Vector3 position)
        {
            if (index < 0 || index >= m_Positions.Count)
            {
                Debug.LogError("LineRenderer.SetPosition index out of bounds!");
                return;
            }
            m_Positions[index] = position;
        }

        public Vector3 GetPosition(int index)
        {
            if (index < 0 || index >= m_Positions.Count)
            {
                Debug.LogError("LineRenderer.GetPosition index out of bounds!");
                return Vector3.zero;
            }
            return m_Positions[index];
        }

        int CopyCount(int length)
        {
            if (length < m_Positions.Count) Debug.LogError("LineRenderer.GetPositions array is too small!");
            return Math.Min(length, m_Positions.Count);
        }

        public int GetPositions([global::System.Runtime.InteropServices.Out] Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            int n = CopyCount(positions.Length);
            m_Positions.CopyTo(0, positions, 0, n);
            return n;
        }

        public void SetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            int n = Math.Min(positions.Length, m_Positions.Count);
            for (int i = 0; i < n; i++) m_Positions[i] = positions[i];
        }

        public void SetPositions(NativeArray<Vector3> positions) => SetPositions(positions.ToArray());

        public void SetPositions(NativeSlice<Vector3> positions) => SetPositions(positions.ToArray());

        public int GetPositions([global::System.Runtime.InteropServices.Out] NativeArray<Vector3> positions)
        {
            int n = CopyCount(positions.Length);
            for (int i = 0; i < n; i++) positions[i] = m_Positions[i];
            return n;
        }

        public int GetPositions([global::System.Runtime.InteropServices.Out] NativeSlice<Vector3> positions)
        {
            int n = CopyCount(positions.Length);
            for (int i = 0; i < n; i++) positions[i] = m_Positions[i];
            return n;
        }

        /// <summary>Drops points that move the line less than a tolerance (Ramer-Douglas-Peucker).</summary>
        public void Simplify(float tolerance)
        {
            var kept = new List<Vector3>();
            LineSimplifier.Simplify(m_Positions, tolerance, kept);
            m_Positions = kept;
        }

        /// <summary>
        /// The build space of the line (matches the Unity 6000.0.71f1 Editor, tests/projects/lines).
        /// BakeMesh writes world-space lines in world space and local-space lines in local space,
        /// or with useTransform in world orientation without the position; a TransformZ line faces
        /// the transform's z axis there, a world-space one without useTransform the world's. Drawn
        /// lines are written in world space and face their transform's z axis.
        /// </summary>
        LineSpace Space(Camera bakeCamera, FrameBuilder.CameraContext drawCamera, bool useTransform)
        {
            var t = transform;
            var localToWorld = t.localToWorldMatrix;
            bool baking = drawCamera == null;
            Matrix4x4 frame = localToWorld, inputToWorld = Matrix4x4.identity, worldToOutput = Matrix4x4.identity;
            if (m_UseWorldSpace)
            {
                if (baking) frame = useTransform ? LineDrawing.RotationScale(t) : Matrix4x4.identity;
            }
            else
            {
                inputToWorld = localToWorld;
                if (baking) worldToOutput = useTransform ? Matrix4x4.Translate(-t.position) : t.worldToLocalMatrix;
            }
            return baking
                ? LineDrawing.Space(this, m_Parameters, bakeCamera, frame, false, inputToWorld, worldToOutput)
                : LineDrawing.Space(this, m_Parameters, drawCamera, frame, false, inputToWorld, worldToOutput);
        }

        List<Vector3> WorldPoints()
        {
            m_World.Clear();
            if (m_UseWorldSpace) m_World.AddRange(m_Positions);
            else
            {
                var m = transform.localToWorldMatrix;
                for (int i = 0; i < m_Positions.Count; i++) m_World.Add(m.MultiplyPoint3x4(m_Positions[i]));
            }
            return m_World;
        }

        public void BakeMesh(Mesh mesh, [Internal.DefaultValue("false")] bool useTransform = false) => BakeMesh(mesh, Camera.main, useTransform);

        public void BakeMesh(Mesh mesh, Camera camera, [Internal.DefaultValue("false")] bool useTransform = false)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (!m_Strip.Build(m_Positions, null, m_Loop, m_Parameters, Space(camera, null, useTransform)))
            {
                mesh.Clear();
                return;
            }
            m_Strip.WriteTo(mesh, streaming: false);
        }

        internal override Bounds GetLocalBounds() =>
            LineDrawing.BoundsOf(WorldPoints(), Mathf.Abs(m_Parameters.WidthMultiplier), transform.worldToLocalMatrix);

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!m_Strip.Build(m_Positions, null, m_Loop, m_Parameters, Space(null, camera, false))) return;
            var mesh = LineDrawing.MeshFor(m_Meshes, camera.Camera, "LineRenderer");
            m_Strip.WriteTo(mesh, streaming: true);
            FrameBuilder.AddMeshDraws(this, mesh, Matrix4x4.identity, Color.white, camera, into);
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            foreach (var mesh in m_Meshes.Values) if (mesh != null) DestroyImmediate(mesh);
            m_Meshes.Clear();
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            m_Parameters.Read(body.Get("m_Parameters"));
            m_Positions = new List<Vector3>();
            foreach (var item in body.Get("m_Positions")?.Seq() ?? Array.Empty<YNode>())
                m_Positions.Add(new Vector3(item.Float("x"), item.Float("y"), item.Float("z")));
            m_UseWorldSpace = body.Bool("m_UseWorldSpace", true);
            m_Loop = body.Bool("m_Loop");
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (LineRenderer)source;
            CopyRendererData(s);
            m_Parameters = s.m_Parameters.Clone();
            m_Positions = new List<Vector3>(s.m_Positions);
            m_UseWorldSpace = s.m_UseWorldSpace;
            m_Loop = s.m_Loop;
            m_MaskInteraction = s.m_MaskInteraction;
        }
    }

    /// <summary>
    /// A strip behind a moving object (measured, tests/projects/lines trail.*). When its object has
    /// moved since the trail last looked (an API read, or once a frame before rendering), the trail
    /// first records the previous position if it has no points, then the new position once it is
    /// at least minVertexDistance from the newest point; points are stamped with Time.time and
    /// remember whether the trail was emitting (non-emitting points are drawn at zero width).
    /// On movement or camera collection the oldest point is dropped while the next one is older than <see cref="time"/>,
    /// and a last point when it is. The strip runs from the object's position through the points,
    /// newest first; the oldest point is drawn moved towards the next one by the fraction of their
    /// time difference it has outlived.
    /// </summary>
    [ClassId(96)]
    public sealed partial class TrailRenderer : Renderer
    {
        struct Point
        {
            internal Vector3 Position;
            internal float Time;
            internal bool Visible;
        }

        internal LineParameters m_Parameters = new LineParameters();
        float m_Time = 5f;
        float m_MinVertexDistance = 0.1f;
        bool m_Autodestruct;
        bool m_Emitting = true;
        float m_PreviewTimeScale = 1f;
        SpriteMaskInteraction m_MaskInteraction;
        /// <summary>Oldest first.</summary>
        readonly List<Point> m_Points = new List<Point>();
        Vector3 m_LastPosition;
        bool m_HasLastPosition;
        /// <summary>Whether <see cref="m_LastPosition"/> was looked at (read, rendered, moved to
        /// or cleared at) rather than only the enable position.</summary>
        bool m_LastObserved;
        bool m_HadPoints;
        /// <summary>The path the trail's points have travelled up to its newest point: a drawn
        /// trail in LineTextureMode.Static places its texture along it, so the texture stays where
        /// the points are; BakeMesh measures from the head instead (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/lines img.trails.f10). Clear and the expiry of every point
        /// restart it.</summary>
        float m_StaticPath;
        /// <summary>The newest point stored, kept after it expires.</summary>
        Vector3 m_Newest;
        bool m_HasAdded;
        readonly LineStrip m_Strip = new LineStrip();
        readonly List<Vector3> m_Draw = new List<Vector3>();
        /// <summary>Each strip point's width scale: 0 for a point added while not emitting.</summary>
        readonly List<float> m_DrawWidths = new List<float>();
        readonly Dictionary<Camera, Mesh> m_Meshes = new Dictionary<Camera, Mesh>();

        public TrailRenderer() { }

        public float time { get => m_Time; set => m_Time = value; }
        internal float previewTimeScale { get => m_PreviewTimeScale; set => m_PreviewTimeScale = value; }
        public float startWidth { get => m_Parameters.StartWidth; set => m_Parameters.StartWidth = value; }
        public float endWidth { get => m_Parameters.EndWidth; set => m_Parameters.EndWidth = value; }
        public float widthMultiplier { get => m_Parameters.WidthMultiplier; set => m_Parameters.WidthMultiplier = value; }
        public bool autodestruct { get => m_Autodestruct; set => m_Autodestruct = value; }
        public int numCornerVertices { get => m_Parameters.NumCornerVertices; set => m_Parameters.NumCornerVertices = value; }
        public int numCapVertices { get => m_Parameters.NumCapVertices; set => m_Parameters.NumCapVertices = value; }
        public float minVertexDistance { get => m_MinVertexDistance; set => m_MinVertexDistance = value; }
        public Color startColor { get => m_Parameters.StartColor; set => m_Parameters.StartColor = value; }
        public Color endColor { get => m_Parameters.EndColor; set => m_Parameters.EndColor = value; }
        public Vector2 textureScale { get => m_Parameters.TextureScale; set => m_Parameters.TextureScale = value; }
        public float shadowBias { get => m_Parameters.ShadowBias; set => m_Parameters.ShadowBias = value; }
        public bool generateLightingData { get => m_Parameters.GenerateLightingData; set => m_Parameters.GenerateLightingData = value; }
        public LineTextureMode textureMode { get => m_Parameters.TextureMode; set => m_Parameters.TextureMode = value; }
        public LineAlignment alignment { get => m_Parameters.Alignment; set => m_Parameters.Alignment = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        internal override SpriteMaskInteraction MaskInteraction => m_MaskInteraction;
        public AnimationCurve widthCurve { get => m_Parameters.WidthCurve; set => m_Parameters.WidthCurve = value ?? new AnimationCurve(new Keyframe(0f, 1f)); }
        public Gradient colorGradient { get => m_Parameters.ColorGradient; set => m_Parameters.ColorGradient = value ?? new Gradient(); }

        public bool emitting { get => m_Emitting; set => m_Emitting = value; }

        public int positionCount
        {
            get
            {
                TrackMovement();
                return m_Points.Count;
            }
        }

        [Obsolete("Use positionCount instead (UnityUpgradable) -> positionCount", false)]
        public int numPositions => positionCount;

        static float Now => Time.time;

        /// <summary>The shortest time between the two oldest points the tail's fade divides by.</summary>
        const float MinPointInterval = 1e-9f;

        /// <summary>Records the object's movement since the trail last looked; a move also drops
        /// the points that have outlived the trail's time (measured: a moving trail ages when
        /// it is read; stationary expiry is handled by camera collection).</summary>
        void TrackMovement()
        {
            if (m_GameObject == null) return;
            var position = transform.position;
            if (!m_HasLastPosition)
            {
                m_LastPosition = position;
                m_HasLastPosition = true;
                m_LastObserved = true;
                return;
            }
            if (position == m_LastPosition)
            {
                m_LastObserved = true;
                return;
            }
            float now = Now;
            // A first move records where the trail last looked as well, but not the enable
            // position nothing looked at (measured: read unmoved, then moved, gives both points;
            // moved before any read or render gives only the new one).
            if (m_Points.Count == 0 && m_LastObserved) Append(new Point { Position = m_LastPosition, Time = now, Visible = m_Emitting });
            if (m_Points.Count == 0 || (position - m_Points[m_Points.Count - 1].Position).sqrMagnitude >= m_MinVertexDistance * m_MinVertexDistance)
                Append(new Point { Position = position, Time = now, Visible = m_Emitting });
            m_LastPosition = position;
            m_LastObserved = true;
            Age(now);
        }

        /// <summary>Stores a new newest point. Each one adds its distance from the newest point
        /// stored before (also one that has since expired, but not one Clear removed) to
        /// <see cref="m_StaticPath"/>.</summary>
        void Append(Point point)
        {
            if (m_Points.Count > 0) m_Newest = m_Points[m_Points.Count - 1].Position;
            if (m_HasAdded) m_StaticPath += Vector3.Distance(m_Newest, point.Position);
            m_HasAdded = true;
            m_Points.Add(point);
        }

        /// <summary>Drops the oldest point while the next one is older than <see cref="time"/>,
        /// and a last point that is.</summary>
        void Age(float now)
        {
            int expired = 0;
            while (m_Points.Count - expired >= 2 && now - m_Points[expired + 1].Time > m_Time) expired++;
            if (expired > 0) m_Points.RemoveRange(0, expired);
            if (m_Points.Count == 1 && now - m_Points[0].Time > m_Time)
            {
                m_Newest = m_Points[0].Position;
                m_Points.Clear();
                m_StaticPath = 0f;
            }
            if (m_Points.Count > 0) m_HadPoints = true;
        }

        /// <summary>Once a frame, before rendering: record movement. Stationary expiry belongs
        /// to camera collection, not this unconditional update.</summary>
        internal void UpdateTrail() => TrackMovement();

        /// <summary>The strip's points from the object (head) to the oldest point.</summary>
        List<Vector3> DrawPoints()
        {
            TrackMovement();
            m_Draw.Clear();
            m_DrawWidths.Clear();
            int count = m_Points.Count;
            if (count == 0 || m_GameObject == null) return m_Draw;
            m_Draw.Add(transform.position);
            m_DrawWidths.Add(m_Emitting ? 1f : 0f);
            for (int i = count - 1; i >= 1; i--)
            {
                m_Draw.Add(m_Points[i].Position);
                m_DrawWidths.Add(m_Points[i].Visible ? 1f : 0f);
            }
            var oldest = m_Points[0];
            var tail = oldest.Position;
            if (count >= 2)
            {
                var next = m_Points[1];
                float f = (Now - m_Time - oldest.Time) / Mathf.Max(next.Time - oldest.Time, MinPointInterval);
                tail = Vector3.Lerp(oldest.Position, next.Position, Mathf.Clamp01(f));
            }
            m_Draw.Add(tail);
            m_DrawWidths.Add(oldest.Visible ? 1f : 0f);
            return m_Draw;
        }

        public void Clear()
        {
            m_Points.Clear();
            m_StaticPath = 0f;
            m_HasAdded = false;
            if (m_GameObject != null)
            {
                // Measured: a move after Clear records the cleared-at position too.
                m_LastPosition = transform.position;
                m_HasLastPosition = true;
                m_LastObserved = true;
            }
        }

        // Setting or reading points first records the object's pending movement, as the other
        // API reads do (measured: a point set after a move is not the one the move aged out).

        public void SetPosition(int index, Vector3 position)
        {
            TrackMovement();
            if (index < 0 || index >= m_Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var p = m_Points[index];
            p.Position = position;
            m_Points[index] = p;
        }

        public Vector3 GetPosition(int index)
        {
            TrackMovement();
            if (index < 0 || index >= m_Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return m_Points[index].Position;
        }

        public int GetPositions([global::System.Runtime.InteropServices.Out] Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            int n = Math.Min(positions.Length, m_Points.Count);
            for (int i = 0; i < n; i++) positions[i] = m_Points[i].Position;
            return n;
        }

        /// <summary>The points recorded while emitting, oldest first.</summary>
        public int GetVisiblePositions([global::System.Runtime.InteropServices.Out] Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            int n = 0;
            foreach (var p in m_Points)
            {
                if (!p.Visible) continue;
                if (n >= positions.Length) break;
                positions[n++] = p.Position;
            }
            return n;
        }

        /// <summary>Replaces the positions of the first points (as many as both have).</summary>
        public void SetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            int n = Math.Min(positions.Length, m_Points.Count);
            for (int i = 0; i < n; i++)
            {
                var p = m_Points[i];
                p.Position = positions[i];
                m_Points[i] = p;
            }
        }

        public void AddPosition(Vector3 position) => AddPositions(new[] { position });

        /// <summary>Appends points after the object's pending movement, stamped evenly between the
        /// newest point's time and now, short of now (measured: n points at the (i+1)/(n+1) steps).</summary>
        public void AddPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            float now = Now;
            float from = m_Points.Count > 0 ? m_Points[m_Points.Count - 1].Time : now;
            for (int i = 0; i < positions.Length; i++)
                Append(new Point { Position = positions[i], Time = from + (now - from) * (i + 1) / (positions.Length + 1), Visible = m_Emitting });
        }

        public void SetPositions(NativeArray<Vector3> positions) => SetPositions(positions.ToArray());
        public void SetPositions(NativeSlice<Vector3> positions) => SetPositions(positions.ToArray());
        public void AddPositions([global::System.Runtime.InteropServices.Out] NativeArray<Vector3> positions) => AddPositions(positions.ToArray());
        public void AddPositions([global::System.Runtime.InteropServices.Out] NativeSlice<Vector3> positions) => AddPositions(positions.ToArray());

        public int GetPositions([global::System.Runtime.InteropServices.Out] NativeArray<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetPositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public int GetPositions([global::System.Runtime.InteropServices.Out] NativeSlice<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetPositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public int GetVisiblePositions([global::System.Runtime.InteropServices.Out] NativeArray<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetVisiblePositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public int GetVisiblePositions([global::System.Runtime.InteropServices.Out] NativeSlice<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetVisiblePositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public void BakeMesh(Mesh mesh, [Internal.DefaultValue("false")] bool useTransform = false) => BakeMesh(mesh, Camera.main, useTransform);

        /// <summary>The trail's strip in world space as seen from <paramref name="camera"/>.
        /// useTransform only turns a TransformZ trail to face its transform's z axis.</summary>
        public void BakeMesh(Mesh mesh, Camera camera, [Internal.DefaultValue("false")] bool useTransform = false)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var points = DrawPoints();
            var frame = useTransform ? LineDrawing.RotationScale(transform) : Matrix4x4.identity;
            if (!m_Strip.Build(points, m_DrawWidths, false, m_Parameters,
                    LineDrawing.Space(this, m_Parameters, camera, frame, true, Matrix4x4.identity, Matrix4x4.identity)))
            {
                mesh.Clear();
                return;
            }
            m_Strip.WriteTo(mesh, streaming: false);
        }

        internal override Bounds GetLocalBounds() =>
            LineDrawing.BoundsOf(DrawPoints(), Mathf.Abs(m_Parameters.WidthMultiplier), transform.worldToLocalMatrix);

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            // Native's culling callback allows automatic destruction in RemoveOldPoints, before
            // checking for an empty strip. Movement/bounds and BakeMesh cannot make this request.
            // Source proof: tests/projects/lines/TRAIL_AUTODESTRUCT_NATIVE.md, Unity 6000.0.71f1.
            TrackMovement();
            Age(Now);
            if (m_Points.Count == 0 && m_HadPoints && m_Autodestruct && Application.isPlaying)
                TrailSystem.RequestDestroy(m_GameObject);
            var points = DrawPoints();
            var space = LineDrawing.Space(this, m_Parameters, camera, transform.localToWorldMatrix, true, Matrix4x4.identity, Matrix4x4.identity);
            if (m_Points.Count > 0) space.StaticOffset = Vector3.Distance(points[0], m_Points[m_Points.Count - 1].Position) + m_StaticPath;
            if (!m_Strip.Build(points, m_DrawWidths, false, m_Parameters, space)) return;
            var mesh = LineDrawing.MeshFor(m_Meshes, camera.Camera, "TrailRenderer");
            m_Strip.WriteTo(mesh, streaming: true);
            FrameBuilder.AddMeshDraws(this, mesh, Matrix4x4.identity, Color.white, camera, into);
        }

        internal override void OnEngineEnable()
        {
            base.OnEngineEnable();
            // Where the object is enabled is not yet a point the trail looked at.
            m_LastPosition = transform.position;
            m_HasLastPosition = true;
            m_LastObserved = false;
            TrailSystem.Add(this);
        }

        internal override void OnEngineDisable()
        {
            base.OnEngineDisable();
            TrailSystem.Remove(this);
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            TrailSystem.Remove(this);
            foreach (var mesh in m_Meshes.Values) if (mesh != null) DestroyImmediate(mesh);
            m_Meshes.Clear();
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            m_Time = body.Float("m_Time", 5f);
            m_PreviewTimeScale = body.Float("m_PreviewTimeScale", 1f);
            m_Parameters.Read(body.Get("m_Parameters"));
            m_MinVertexDistance = body.Float("m_MinVertexDistance", 0.1f);
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
            m_Autodestruct = body.Bool("m_Autodestruct");
            m_Emitting = body.Bool("m_Emitting", true);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (TrailRenderer)source;
            CopyRendererData(s);
            m_Parameters = s.m_Parameters.Clone();
            m_Time = s.m_Time;
            m_PreviewTimeScale = s.m_PreviewTimeScale;
            m_MinVertexDistance = s.m_MinVertexDistance;
            m_MaskInteraction = s.m_MaskInteraction;
            m_Autodestruct = s.m_Autodestruct;
            m_Emitting = s.m_Emitting;
        }

        /// <summary>The enabled trails, updated once a frame before rendering.</summary>
        static class TrailSystem
        {
            static readonly List<TrailRenderer> s_Trails = new List<TrailRenderer>();
            static readonly List<TrailRenderer> s_Snapshot = new List<TrailRenderer>();
            static readonly Queue<GameObject> s_DestroyRequests = new Queue<GameObject>();
            static readonly HashSet<GameObject> s_DestroyTargets = new HashSet<GameObject>();
            static bool s_Registered;
            static bool s_Draining;

            internal static void Add(TrailRenderer trail)
            {
                if (!s_Registered)
                {
                    s_Registered = true;
                    PlayerLoopDriver.RegisterLast(typeof(PL.PostLateUpdate.UpdateAllRenderers), UpdateAll);
                    // Native automatic requests use mode 0x43: fixed mask 1 and dynamic mask 2
                    // consume them; startup mask 4 and EOF mask 0x20 do not. Prepend so existing
                    // Invokes/coroutines at these same nodes observe the completed destruction.
                    PlayerLoopDriver.RegisterFirst(typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate), DrainDestroyRequests);
                    PlayerLoopDriver.RegisterFirst(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate), DrainDestroyRequests);
                    PlayerLoopDriver.RegisterFirst(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate), DrainDestroyRequests);
                }
                if (!s_Trails.Contains(trail)) s_Trails.Add(trail);
            }

            internal static void Remove(TrailRenderer trail) => s_Trails.Remove(trail);

            internal static void RequestDestroy(GameObject target)
            {
                // Capture the target, not a component whose later state could cancel the request.
                // Deduplication bounds repeated camera collections to one pending entry per target.
                if (target != null && s_DestroyTargets.Add(target)) s_DestroyRequests.Enqueue(target);
            }

            static void DrainDestroyRequests()
            {
                if (s_Draining || s_DestroyRequests.Count == 0) return;
                s_Draining = true;
                try
                {
                    int count = s_DestroyRequests.Count;
                    // A fixed snapshot: callbacks can enqueue more, but cannot consume those new
                    // requests in this pass, even by reentering a matching node.
                    for (int i = 0; i < count; i++)
                    {
                        var target = s_DestroyRequests.Dequeue();
                        s_DestroyTargets.Remove(target);
                        // Scene unload and leaving Play mode already destroy their GameObjects.
                        // Retaining the reference until this pass cannot target a replacement object.
                        if (target == null) continue;
                        try { Destruction.DestroyImmediate(target); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                }
                finally { s_Draining = false; }
            }

            static void UpdateAll()
            {
                s_Snapshot.Clear();
                s_Snapshot.AddRange(s_Trails);
                foreach (var trail in s_Snapshot)
                    if (trail != null) trail.UpdateTrail();
            }
        }
    }

    /// <summary>
    /// Ramer-Douglas-Peucker simplification (LineUtility.Simplify, LineRenderer.Simplify; matches
    /// the Unity 6000.0.71f1 Editor, tests/projects/lines simplify.*): the first and last points are
    /// kept; a span keeps its point farthest from the infinite line through its ends (the first of
    /// equal ones) when that distance is above the tolerance, and is split there. The first span
    /// ends at the last point apart from the first, so a closed polyline keeps a point across
    /// from its start. Fewer than two points keep none. Indices come out in ascending order.
    /// </summary>
    internal static class LineSimplifier
    {
        /// <summary>Points within this squared distance of the first count as the first.</summary>
        const float SameAsFirstSqr = 1e-12f;

        internal static void Keep(IReadOnlyList<Vector3> points, float tolerance, List<int> keep)
        {
            int count = points.Count;
            if (count < 2) return;
            int start = keep.Count;
            keep.Add(0);
            keep.Add(count - 1);
            if (count == 2) return;
            int end = count - 1;
            while (end > 0 && (points[end] - points[0]).sqrMagnitude <= SameAsFirstSqr) end--;
            float limit = tolerance * tolerance;
            var spans = new Stack<(int, int)>();
            spans.Push((0, end));
            while (spans.Count > 0)
            {
                var (first, last) = spans.Pop();
                float farthest = 0f;
                int at = first;
                if (first + 1 < last)
                {
                    var origin = points[first];
                    var direction = points[last] - origin;
                    direction *= 1f / direction.magnitude;
                    for (int i = first + 1; i < last; i++)
                    {
                        float d = Vector3.Cross(origin - points[i], direction).sqrMagnitude;
                        // A NaN distance (ends at the same point) takes over and keeps nothing.
                        if (!(d <= farthest))
                        {
                            farthest = d;
                            at = i;
                        }
                    }
                }
                if (limit < farthest)
                {
                    keep.Add(at);
                    spans.Push((first, at));
                    spans.Push((at, last));
                }
            }
            keep.Sort(start, keep.Count - start, null);
        }

        internal static void Simplify(IReadOnlyList<Vector3> points, float tolerance, List<Vector3> result)
        {
            var keep = new List<int>();
            Keep(points, tolerance, keep);
            result.Clear();
            foreach (var i in keep) result.Add(points[i]);
        }
    }
}

namespace UnityEngine
{
    public sealed partial class LineUtility
    {
        public LineUtility() { }

        public static void Simplify(List<Vector3> points, float tolerance, List<int> pointsToKeep)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (pointsToKeep == null) throw new ArgumentNullException(nameof(pointsToKeep));
            pointsToKeep.Clear();
            LineSimplifier.Keep(points, tolerance, pointsToKeep);
        }

        public static void Simplify(List<Vector3> points, float tolerance, List<Vector3> simplifiedPoints)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (simplifiedPoints == null) throw new ArgumentNullException(nameof(simplifiedPoints));
            LineSimplifier.Simplify(points, tolerance, simplifiedPoints);
        }

        public static void Simplify(List<Vector2> points, float tolerance, List<int> pointsToKeep)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (pointsToKeep == null) throw new ArgumentNullException(nameof(pointsToKeep));
            pointsToKeep.Clear();
            LineSimplifier.Keep(points.ConvertAll(p => (Vector3)p), tolerance, pointsToKeep);
        }

        public static void Simplify(List<Vector2> points, float tolerance, List<Vector2> simplifiedPoints)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (simplifiedPoints == null) throw new ArgumentNullException(nameof(simplifiedPoints));
            var keep = new List<int>();
            LineSimplifier.Keep(points.ConvertAll(p => (Vector3)p), tolerance, keep);
            simplifiedPoints.Clear();
            foreach (var i in keep) simplifiedPoints.Add(points[i]);
        }
    }
}
