using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;

namespace UnityEngine
{
    /// <summary>What line and trail renderers share (Unity's LineParameters): width, color, caps,
    /// alignment and texture mapping along the strip.</summary>
    internal sealed class LineParameters
    {
        internal float WidthMultiplier = 1f;
        internal AnimationCurve WidthCurve = new AnimationCurve(new Keyframe(0f, 1f));
        internal Gradient ColorGradient = new Gradient();
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
            var copy = new Gradient { mode = g.mode };
            copy.SetKeys(g.colorKeys, g.alphaKeys);
            return copy;
        }

        // The start/end shortcuts edit the first and last keys, as in Unity.

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
            if (keys.Length < 2)
            {
                float other = keys.Length == 1 ? keys[0].value : value;
                keys = new[] { new Keyframe(0f, other), new Keyframe(1f, other) };
            }
            int index = end == 0 ? 0 : keys.Length - 1;
            keys[index].value = value;
            WidthCurve = new AnimationCurve(keys);
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
            var colors = ColorGradient.colorKeys;
            var alphas = ColorGradient.alphaKeys;
            if (colors.Length < 2) colors = new[] { new GradientColorKey(StartColor, 0f), new GradientColorKey(EndColor, 1f) };
            if (alphas.Length < 2) alphas = new[] { new GradientAlphaKey(StartColor.a, 0f), new GradientAlphaKey(EndColor.a, 1f) };
            int ci = end == 0 ? 0 : colors.Length - 1;
            int ai = end == 0 ? 0 : alphas.Length - 1;
            colors[ci].color = new Color(color.r, color.g, color.b, 1f);
            alphas[ai].alpha = color.a;
            var g = new Gradient { mode = ColorGradient.mode };
            g.SetKeys(colors, alphas);
            ColorGradient = g;
        }
    }

    /// <summary>Builds the triangle strip of a line or trail: a quad per segment facing the camera
    /// (or perpendicular to the transform's Z), colored and sized by the parameters along the
    /// strip's length, with round caps.</summary>
    internal sealed class LineStrip
    {
        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Color> m_Colors = new List<Color>();
        readonly List<Vector2> m_Uvs = new List<Vector2>();
        readonly List<int> m_Triangles = new List<int>();
        float[] m_Distances = Array.Empty<float>();

        /// <summary>Builds the strip through world-space points (first is the start, t = 0). Returns
        /// false when there is nothing to draw.</summary>
        internal bool Build(IReadOnlyList<Vector3> points, bool loop, LineParameters p, Vector3 viewPoint, Vector3 viewForward, bool orthographic, Vector3 transformZ)
        {
            m_Vertices.Clear();
            m_Colors.Clear();
            m_Uvs.Clear();
            m_Triangles.Clear();
            int n = points.Count;
            if (n < 2) return false;
            int count = loop ? n + 1 : n;
            if (m_Distances.Length < count) m_Distances = new float[count];
            m_Distances[0] = 0f;
            for (int i = 1; i < count; i++) m_Distances[i] = m_Distances[i - 1] + Vector3.Distance(points[(i - 1) % n], points[i % n]);
            float total = m_Distances[count - 1];

            for (int i = 0; i < count; i++)
            {
                var point = points[i % n];
                float t = total > 0f ? m_Distances[i] / total : (float)i / (count - 1);
                var tangent = Tangent(points, i, n, loop);
                Vector3 normal;
                if (p.Alignment == LineAlignment.TransformZ) normal = transformZ;
                else normal = orthographic ? -viewForward : viewPoint - point;
                var side = Vector3.Cross(tangent, normal);
                if (side.sqrMagnitude < 1e-12f) side = Vector3.Cross(tangent, Vector3.up);
                if (side.sqrMagnitude < 1e-12f) side = Vector3.Cross(tangent, Vector3.right);
                side.Normalize();
                float half = 0.5f * p.WidthMultiplier * p.WidthCurve.Evaluate(t);
                var color = p.ColorGradient.Evaluate(t);
                float u = U(p, i, count, m_Distances[i], total, half);
                int at = m_Vertices.Count;
                m_Vertices.Add(point - side * half);
                m_Vertices.Add(point + side * half);
                m_Colors.Add(color);
                m_Colors.Add(color);
                m_Uvs.Add(new Vector2(u, 0f));
                m_Uvs.Add(new Vector2(u, 1f));
                if (i > 0)
                {
                    m_Triangles.Add(at - 2);
                    m_Triangles.Add(at - 1);
                    m_Triangles.Add(at);
                    m_Triangles.Add(at - 1);
                    m_Triangles.Add(at + 1);
                    m_Triangles.Add(at);
                }
            }
            if (!loop && p.NumCapVertices > 0)
            {
                Cap(points[0], -Tangent(points, 0, n, false), 0, p, 0f);
                Cap(points[n - 1], Tangent(points, n - 1, n, false), 2 * (count - 1), p, 1f);
            }
            return true;
        }

        static Vector3 Tangent(IReadOnlyList<Vector3> points, int i, int n, bool loop)
        {
            Vector3 a, b;
            if (loop)
            {
                a = points[(i - 1 + n) % n];
                b = points[(i + 1) % n];
            }
            else
            {
                a = points[Math.Max(0, i - 1)];
                b = points[Math.Min(n - 1, i + 1)];
            }
            var d = b - a;
            return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.right;
        }

        static float U(LineParameters p, int i, int count, float distance, float total, float half)
        {
            switch (p.TextureMode)
            {
                case LineTextureMode.Tile:
                    return distance * p.TextureScale.x;
                case LineTextureMode.DistributePerSegment:
                    return count > 1 ? (float)i / (count - 1) * p.TextureScale.x : 0f;
                case LineTextureMode.RepeatPerSegment:
                    return i * p.TextureScale.x;
                default:
                    return (total > 0f ? distance / total : (float)i / Math.Max(1, count - 1)) * p.TextureScale.x;
            }
        }

        /// <summary>A half disc of triangles closing one end of the strip, between its two edge vertices.</summary>
        void Cap(Vector3 center, Vector3 outward, int edgeStart, LineParameters p, float t)
        {
            var left = m_Vertices[edgeStart];
            var right = m_Vertices[edgeStart + 1];
            var side = (right - left) * 0.5f;
            float radius = side.magnitude;
            if (radius <= 0f) return;
            var color = p.ColorGradient.Evaluate(t);
            float u = m_Uvs[edgeStart].x;
            int centerIndex = m_Vertices.Count;
            m_Vertices.Add(center);
            m_Colors.Add(color);
            m_Uvs.Add(new Vector2(u, 0.5f));
            int previous = edgeStart;
            int steps = p.NumCapVertices + 1;
            var a = -side;
            var b = outward * radius;
            for (int k = 1; k <= steps; k++)
            {
                int index;
                if (k == steps)
                {
                    index = edgeStart + 1;
                }
                else
                {
                    float angle = Mathf.PI * k / steps;
                    var offset = a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                    index = m_Vertices.Count;
                    m_Vertices.Add(center + offset);
                    m_Colors.Add(color);
                    m_Uvs.Add(new Vector2(u, 0.5f - 0.5f * Mathf.Cos(angle)));
                }
                m_Triangles.Add(centerIndex);
                m_Triangles.Add(previous);
                m_Triangles.Add(index);
                previous = index;
            }
        }

        internal void WriteTo(Mesh mesh, Matrix4x4 toMesh)
        {
            mesh.Clear();
            var vertices = m_Vertices.ToArray();
            if (!toMesh.isIdentity)
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toMesh.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            mesh.colors = m_Colors.ToArray();
            mesh.uv = m_Uvs.ToArray();
            mesh.triangles = m_Triangles.ToArray();
            mesh.RecalculateBounds();
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

        internal static void Draw(Renderer renderer, LineStrip strip, Dictionary<Camera, Mesh> meshes, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into, string name)
        {
            var mesh = MeshFor(meshes, camera.Camera, name);
            strip.WriteTo(mesh, Matrix4x4.identity);
            FrameBuilder.AddMeshDraws(renderer, mesh, Matrix4x4.identity, Color.white, camera, into);
        }

        internal static Bounds BoundsOf(IReadOnlyList<Vector3> worldPoints, float width, Matrix4x4 worldToLocal)
        {
            if (worldPoints.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var b = new Bounds(worldToLocal.MultiplyPoint3x4(worldPoints[0]), Vector3.zero);
            for (int i = 1; i < worldPoints.Count; i++) b.Encapsulate(worldToLocal.MultiplyPoint3x4(worldPoints[i]));
            b.Expand(width);
            return b;
        }

        internal static void Bake(Mesh mesh, Renderer renderer, LineStrip strip, IReadOnlyList<Vector3> points, bool loop, LineParameters p, Camera camera, bool useTransform)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            camera ??= Camera.main;
            var position = camera != null ? camera.transform.position : Vector3.zero;
            var forward = camera != null ? camera.transform.forward : Vector3.forward;
            bool ortho = camera != null && camera.orthographic;
            if (!strip.Build(points, loop, p, position, forward, ortho, renderer.transform.forward))
            {
                mesh.Clear();
                return;
            }
            strip.WriteTo(mesh, useTransform ? renderer.transform.worldToLocalMatrix : Matrix4x4.identity);
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
        public int numCornerVertices { get => m_Parameters.NumCornerVertices; set => m_Parameters.NumCornerVertices = Mathf.Max(0, value); }
        public int numCapVertices { get => m_Parameters.NumCapVertices; set => m_Parameters.NumCapVertices = Mathf.Max(0, value); }
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

        public void SetPosition(int index, Vector3 position)
        {
            if (index < 0 || index >= m_Positions.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Index out of bounds: {index} (positionCount {m_Positions.Count})");
            m_Positions[index] = position;
        }

        public Vector3 GetPosition(int index)
        {
            if (index < 0 || index >= m_Positions.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Index out of bounds: {index} (positionCount {m_Positions.Count})");
            return m_Positions[index];
        }

        public int GetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            int n = Math.Min(positions.Length, m_Positions.Count);
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

        public int GetPositions(NativeArray<Vector3> positions)
        {
            int n = Math.Min(positions.Length, m_Positions.Count);
            for (int i = 0; i < n; i++) positions[i] = m_Positions[i];
            return n;
        }

        public int GetPositions(NativeSlice<Vector3> positions)
        {
            int n = Math.Min(positions.Length, m_Positions.Count);
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

        List<Vector3> WorldPoints()
        {
            m_World.Clear();
            if (m_UseWorldSpace) m_World.AddRange(m_Positions);
            else
            {
                var m = transform.localToWorldMatrix;
                foreach (var p in m_Positions) m_World.Add(m.MultiplyPoint3x4(p));
            }
            return m_World;
        }

        public void BakeMesh(Mesh mesh, [Internal.DefaultValue("false")] bool useTransform = false) => BakeMesh(mesh, Camera.main, useTransform);

        public void BakeMesh(Mesh mesh, Camera camera, [Internal.DefaultValue("false")] bool useTransform = false) =>
            LineDrawing.Bake(mesh, this, m_Strip, WorldPoints(), m_Loop, m_Parameters, camera, useTransform);

        internal override Bounds GetLocalBounds() =>
            LineDrawing.BoundsOf(WorldPoints(), Mathf.Abs(m_Parameters.WidthMultiplier), transform.worldToLocalMatrix);

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!m_Strip.Build(WorldPoints(), m_Loop, m_Parameters, camera.Position, camera.Forward, camera.Orthographic, transform.forward)) return;
            LineDrawing.Draw(this, m_Strip, m_Meshes, camera, into, "LineRenderer");
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

    /// <summary>A strip behind a moving object: a point every minVertexDistance, each fading out
    /// after <see cref="time"/> seconds; width and color run from the object (0) to the tail (1).</summary>
    [ClassId(96)]
    public sealed partial class TrailRenderer : Renderer
    {
        struct Point
        {
            internal Vector3 Position;
            internal float Time;
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
        int m_UpdatedFrame = -1;
        bool m_HadPoints;
        readonly LineStrip m_Strip = new LineStrip();
        readonly List<Vector3> m_Draw = new List<Vector3>();
        readonly Dictionary<Camera, Mesh> m_Meshes = new Dictionary<Camera, Mesh>();

        public TrailRenderer() { }

        public float time { get => m_Time; set => m_Time = value; }
        internal float previewTimeScale { get => m_PreviewTimeScale; set => m_PreviewTimeScale = value; }
        public float startWidth { get => m_Parameters.StartWidth; set => m_Parameters.StartWidth = value; }
        public float endWidth { get => m_Parameters.EndWidth; set => m_Parameters.EndWidth = value; }
        public float widthMultiplier { get => m_Parameters.WidthMultiplier; set => m_Parameters.WidthMultiplier = value; }
        public bool autodestruct { get => m_Autodestruct; set => m_Autodestruct = value; }
        public int numCornerVertices { get => m_Parameters.NumCornerVertices; set => m_Parameters.NumCornerVertices = Mathf.Max(0, value); }
        public int numCapVertices { get => m_Parameters.NumCapVertices; set => m_Parameters.NumCapVertices = Mathf.Max(0, value); }
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

        public bool emitting
        {
            get => m_Emitting;
            set
            {
                if (value && !m_Emitting) m_UpdatedFrame = -1;
                m_Emitting = value;
            }
        }

        public int positionCount
        {
            get
            {
                Advance();
                return m_Points.Count;
            }
        }

        [Obsolete("Use positionCount instead (UnityUpgradable) -> positionCount", false)]
        public int numPositions => positionCount;

        static float Now => Time.time;

        /// <summary>Ages the trail and adds the object's position when it moved far enough; once a frame.</summary>
        void Advance()
        {
            int frame = Time.frameCount;
            if (m_UpdatedFrame == frame) return;
            m_UpdatedFrame = frame;
            float now = Now;
            int expired = 0;
            while (expired < m_Points.Count && now - m_Points[expired].Time > m_Time) expired++;
            if (expired > 0) m_Points.RemoveRange(0, expired);
            if (m_Emitting && m_GameObject != null && m_GameObject.activeInHierarchy)
            {
                var position = transform.position;
                if (m_Points.Count == 0 || (position - m_Points[m_Points.Count - 1].Position).sqrMagnitude >= m_MinVertexDistance * m_MinVertexDistance)
                    m_Points.Add(new Point { Position = position, Time = now });
            }
            if (m_Points.Count > 0) m_HadPoints = true;
            else if (m_HadPoints && m_Autodestruct && Application.isPlaying)
            {
                m_HadPoints = false;
                Destroy(m_GameObject);
            }
        }

        /// <summary>The strip's points from the object (head) to the oldest point.</summary>
        List<Vector3> DrawPoints()
        {
            Advance();
            m_Draw.Clear();
            if (m_Emitting && m_GameObject != null)
            {
                var head = transform.position;
                if (m_Points.Count == 0 || head != m_Points[m_Points.Count - 1].Position) m_Draw.Add(head);
            }
            for (int i = m_Points.Count - 1; i >= 0; i--) m_Draw.Add(m_Points[i].Position);
            return m_Draw;
        }

        public void Clear()
        {
            m_Points.Clear();
            m_UpdatedFrame = Time.frameCount;
        }

        public void SetPosition(int index, Vector3 position)
        {
            if (index < 0 || index >= m_Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var p = m_Points[index];
            p.Position = position;
            m_Points[index] = p;
        }

        public Vector3 GetPosition(int index)
        {
            if (index < 0 || index >= m_Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return m_Points[index].Position;
        }

        public int GetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            int n = Math.Min(positions.Length, m_Points.Count);
            for (int i = 0; i < n; i++) positions[i] = m_Points[i].Position;
            return n;
        }

        public int GetVisiblePositions(Vector3[] positions) => GetPositions(positions);

        public void SetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            m_Points.Clear();
            AddPositions(positions);
        }

        public void AddPosition(Vector3 position) => m_Points.Add(new Point { Position = position, Time = Now });

        public void AddPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            foreach (var p in positions) AddPosition(p);
        }

        public void SetPositions(NativeArray<Vector3> positions) => SetPositions(positions.ToArray());
        public void SetPositions(NativeSlice<Vector3> positions) => SetPositions(positions.ToArray());
        public void AddPositions(NativeArray<Vector3> positions) => AddPositions(positions.ToArray());
        public void AddPositions(NativeSlice<Vector3> positions) => AddPositions(positions.ToArray());

        public int GetPositions(NativeArray<Vector3> positions)
        {
            int n = Math.Min(positions.Length, m_Points.Count);
            for (int i = 0; i < n; i++) positions[i] = m_Points[i].Position;
            return n;
        }

        public int GetPositions(NativeSlice<Vector3> positions)
        {
            int n = Math.Min(positions.Length, m_Points.Count);
            for (int i = 0; i < n; i++) positions[i] = m_Points[i].Position;
            return n;
        }

        public int GetVisiblePositions(NativeArray<Vector3> positions) => GetPositions(positions);
        public int GetVisiblePositions(NativeSlice<Vector3> positions) => GetPositions(positions);

        public void BakeMesh(Mesh mesh, [Internal.DefaultValue("false")] bool useTransform = false) => BakeMesh(mesh, Camera.main, useTransform);

        public void BakeMesh(Mesh mesh, Camera camera, [Internal.DefaultValue("false")] bool useTransform = false) =>
            LineDrawing.Bake(mesh, this, m_Strip, DrawPoints(), false, m_Parameters, camera, useTransform);

        internal override Bounds GetLocalBounds() =>
            LineDrawing.BoundsOf(DrawPoints(), Mathf.Abs(m_Parameters.WidthMultiplier), transform.worldToLocalMatrix);

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!m_Strip.Build(DrawPoints(), false, m_Parameters, camera.Position, camera.Forward, camera.Orthographic, transform.forward)) return;
            LineDrawing.Draw(this, m_Strip, m_Meshes, camera, into, "TrailRenderer");
        }

        internal override void OnEngineEnable()
        {
            base.OnEngineEnable();
            // A trail starts afresh where its object is.
            m_UpdatedFrame = -1;
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
    }

    /// <summary>Ramer-Douglas-Peucker simplification (LineUtility.Simplify, LineRenderer.Simplify).</summary>
    internal static class LineSimplifier
    {
        internal static void Keep(IReadOnlyList<Vector3> points, float tolerance, List<int> keep)
        {
            keep.Clear();
            if (points.Count == 0) return;
            if (points.Count < 3)
            {
                for (int i = 0; i < points.Count; i++) keep.Add(i);
                return;
            }
            var flags = new bool[points.Count];
            flags[0] = flags[points.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, points.Count - 1));
            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                float worst = 0f;
                int index = -1;
                for (int i = first + 1; i < last; i++)
                {
                    float d = DistanceToSegment(points[i], points[first], points[last]);
                    if (d > worst)
                    {
                        worst = d;
                        index = i;
                    }
                }
                if (index >= 0 && worst > tolerance)
                {
                    flags[index] = true;
                    stack.Push((first, index));
                    stack.Push((index, last));
                }
            }
            for (int i = 0; i < flags.Length; i++)
                if (flags[i]) keep.Add(i);
        }

        internal static void Simplify(IReadOnlyList<Vector3> points, float tolerance, List<Vector3> result)
        {
            var keep = new List<int>();
            Keep(points, tolerance, keep);
            result.Clear();
            foreach (var i in keep) result.Add(points[i]);
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            if (len <= 0f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
            return Vector3.Distance(p, a + ab * t);
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
