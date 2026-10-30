using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;
using R = Unbound.Runtime.ParticleRandom;

namespace UnityEngine
{
    /// <summary>
    /// Draws its particle system's particles: every frame and camera it builds one world-space mesh
    /// of camera-facing quads (billboard, stretched, horizontal, vertical) or instanced meshes,
    /// sorted by the sort mode, with the particle colors as vertex colors and the texture sheet
    /// animation in the uvs, drawn with the first material; trails are ribbons drawn with the
    /// trail material.
    /// </summary>
    [ClassId(199)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class ParticleSystemRenderer : Renderer
    {
        ParticleSystemRenderMode m_RenderMode;
        ParticleSystemSortMode m_SortMode;
        float m_MinParticleSize;
        float m_MaxParticleSize = 0.5f;
        float m_CameraVelocityScale;
        float m_VelocityScale;
        float m_LengthScale = 2f;
        float m_SortingFudge;
        float m_NormalDirection = 1f;
        float m_ShadowBias;
        ParticleSystemRenderSpace m_RenderAlignment;
        Vector3 m_Pivot;
        Vector3 m_Flip;
        SpriteMaskInteraction m_MaskInteraction;
        bool m_EnableGPUInstancing = true;
        bool m_AllowRoll = true;
        bool m_FreeformStretching;
        bool m_RotateWithStretchDirection = true;
        ParticleSystemMeshDistribution m_MeshDistribution;
        readonly List<Mesh> m_Meshes = new List<Mesh>();
        readonly List<float> m_MeshWeightings = new List<float>();
        readonly List<ParticleSystemVertexStream> m_VertexStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        };
        readonly List<ParticleSystemVertexStream> m_TrailVertexStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        };

        ParticleSystem m_System;
        readonly Dictionary<Camera, Mesh> m_CameraMeshes = new Dictionary<Camera, Mesh>();
        Mesh m_FallbackMesh;
        readonly Geometry m_Geometry = new Geometry();

        public ParticleSystemRenderer() { }

        public ParticleSystemRenderSpace alignment { get => m_RenderAlignment; set => m_RenderAlignment = value; }
        public ParticleSystemRenderMode renderMode { get => m_RenderMode; set => m_RenderMode = value; }
        public ParticleSystemMeshDistribution meshDistribution { get => m_MeshDistribution; set => m_MeshDistribution = value; }
        public ParticleSystemSortMode sortMode { get => m_SortMode; set => m_SortMode = value; }
        public float lengthScale { get => m_LengthScale; set => m_LengthScale = value; }
        public float velocityScale { get => m_VelocityScale; set => m_VelocityScale = value; }
        public float cameraVelocityScale { get => m_CameraVelocityScale; set => m_CameraVelocityScale = value; }
        public float normalDirection { get => m_NormalDirection; set => m_NormalDirection = value; }
        public float shadowBias { get => m_ShadowBias; set => m_ShadowBias = value; }
        public float sortingFudge { get => m_SortingFudge; set => m_SortingFudge = value; }
        public float minParticleSize { get => m_MinParticleSize; set => m_MinParticleSize = value; }
        public float maxParticleSize { get => m_MaxParticleSize; set => m_MaxParticleSize = value; }
        public Vector3 pivot { get => m_Pivot; set => m_Pivot = value; }
        public Vector3 flip { get => m_Flip; set => m_Flip = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        internal override SpriteMaskInteraction MaskInteraction => m_MaskInteraction;
        public bool enableGPUInstancing { get => m_EnableGPUInstancing; set => m_EnableGPUInstancing = value; }
        public bool allowRoll { get => m_AllowRoll; set => m_AllowRoll = value; }
        public bool freeformStretching { get => m_FreeformStretching; set => m_FreeformStretching = value; }
        public bool rotateWithStretchDirection { get => m_RotateWithStretchDirection; set => m_RotateWithStretchDirection = value; }

        public Material trailMaterial
        {
            get => m_Materials.Length > 1 ? m_Materials[1] : null;
            set
            {
                if (m_Materials.Length < 2) Array.Resize(ref m_Materials, 2);
                m_Materials[1] = value;
            }
        }

        public Mesh mesh
        {
            get => m_Meshes.Count > 0 ? m_Meshes[0] : null;
            set
            {
                if (m_Meshes.Count == 0) m_Meshes.Add(value);
                else m_Meshes[0] = value;
            }
        }

        public int meshCount => m_Meshes.Count;
        public int activeVertexStreamsCount => m_VertexStreams.Count;
        public int activeTrailVertexStreamsCount => m_TrailVertexStreams.Count;
        public bool supportsMeshInstancing
        {
            get
            {
                Stubs.Ignored("ParticleSystemRenderer.supportsMeshInstancing", "is always false: mesh particles are not drawn instanced");
                return false;
            }
        }

        public int GetMeshes(Mesh[] meshes)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            int n = Math.Min(meshes.Length, m_Meshes.Count);
            for (int i = 0; i < n; i++) meshes[i] = m_Meshes[i];
            return n;
        }

        public void SetMeshes(Mesh[] meshes, int size)
        {
            m_Meshes.Clear();
            if (meshes == null) return;
            for (int i = 0; i < size && i < meshes.Length; i++) m_Meshes.Add(meshes[i]);
        }

        public void SetMeshes(Mesh[] meshes) => SetMeshes(meshes, meshes?.Length ?? 0);

        public int GetMeshWeightings(float[] weightings)
        {
            if (weightings == null) throw new ArgumentNullException(nameof(weightings));
            int n = Math.Min(weightings.Length, m_MeshWeightings.Count);
            for (int i = 0; i < n; i++) weightings[i] = m_MeshWeightings[i];
            return n;
        }

        public void SetMeshWeightings(float[] weightings, int size)
        {
            m_MeshWeightings.Clear();
            if (weightings == null) return;
            for (int i = 0; i < size && i < weightings.Length; i++) m_MeshWeightings.Add(weightings[i]);
        }

        public void SetMeshWeightings(float[] weightings) => SetMeshWeightings(weightings, weightings?.Length ?? 0);

        public void SetActiveVertexStreams(List<ParticleSystemVertexStream> streams)
        {
            m_VertexStreams.Clear();
            if (streams != null) m_VertexStreams.AddRange(streams);
        }

        public void GetActiveVertexStreams(List<ParticleSystemVertexStream> streams)
        {
            if (streams == null) throw new ArgumentNullException(nameof(streams));
            streams.Clear();
            streams.AddRange(m_VertexStreams);
        }

        public void SetActiveTrailVertexStreams(List<ParticleSystemVertexStream> streams)
        {
            m_TrailVertexStreams.Clear();
            if (streams != null) m_TrailVertexStreams.AddRange(streams);
        }

        public void GetActiveTrailVertexStreams(List<ParticleSystemVertexStream> streams)
        {
            if (streams == null) throw new ArgumentNullException(nameof(streams));
            streams.Clear();
            streams.AddRange(m_TrailVertexStreams);
        }

#pragma warning disable CS0618
        [Obsolete("EnableVertexStreams is deprecated.Use SetActiveVertexStreams instead.", false)]
        public void EnableVertexStreams(ParticleSystemVertexStreams streams) => Stubs.Ignored("ParticleSystemRenderer.EnableVertexStreams", "is not honoured: use SetActiveVertexStreams");

        [Obsolete("DisableVertexStreams is deprecated.Use SetActiveVertexStreams instead.", false)]
        public void DisableVertexStreams(ParticleSystemVertexStreams streams) => Stubs.Ignored("ParticleSystemRenderer.DisableVertexStreams", "is not honoured: use SetActiveVertexStreams");

        [Obsolete("AreVertexStreamsEnabled is deprecated.Use GetActiveVertexStreams instead.", false)]
        public bool AreVertexStreamsEnabled(ParticleSystemVertexStreams streams) => (GetEnabledVertexStreams(streams) & streams) == streams;

        [Obsolete("GetEnabledVertexStreams is deprecated.Use GetActiveVertexStreams instead.", false)]
        public ParticleSystemVertexStreams GetEnabledVertexStreams(ParticleSystemVertexStreams streams) =>
            streams & (ParticleSystemVertexStreams.Position | ParticleSystemVertexStreams.Normal | ParticleSystemVertexStreams.Color | ParticleSystemVertexStreams.UV);
#pragma warning restore CS0618

        [Obsolete("BakeMesh with useTransform is deprecated. Use BakeMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeMesh(Mesh mesh, bool useTransform = false) =>
            BakeMesh(mesh, Camera.main, useTransform ? ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition : ParticleSystemBakeMeshOptions.Default);

        [Obsolete("BakeMesh with useTransform is deprecated. Use BakeMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeMesh(Mesh mesh, Camera camera, bool useTransform = false) =>
            BakeMesh(mesh, camera, useTransform ? ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition : ParticleSystemBakeMeshOptions.Default);

        [Obsolete("BakeTrailsMesh with useTransform is deprecated. Use BakeTrailsMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeTrailsMesh(Mesh mesh, bool useTransform = false) => mesh?.Clear();

        [Obsolete("BakeTrailsMesh with useTransform is deprecated. Use BakeTrailsMesh with ParticleSystemBakeMeshOptions instead.", false)]
        public void BakeTrailsMesh(Mesh mesh, Camera camera, bool useTransform = false) => mesh?.Clear();

        public void BakeMesh(Mesh mesh, ParticleSystemBakeMeshOptions options) => BakeMesh(mesh, Camera.main, options);

        /// <summary>The particles' geometry as the camera would see it. Without BakePosition and
        /// BakeRotationAndScale the vertices are relative to the renderer's transform.</summary>
        public void BakeMesh(Mesh mesh, Camera camera, ParticleSystemBakeMeshOptions options)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh), "The mesh to bake into is null.");
            if (camera == null) throw new ArgumentNullException(nameof(camera), "The camera used for baking is null.");
            var view = View.Of(camera);
            mesh.Clear();
            if (System == null || !m_Geometry.Build(this, System, view)) return;
            var toLocal = Matrix4x4.identity;
            var t = transform;
            bool position = (options & ParticleSystemBakeMeshOptions.BakePosition) != 0;
            bool rotationScale = (options & ParticleSystemBakeMeshOptions.BakeRotationAndScale) != 0;
            if (!position || !rotationScale)
            {
                var keep = Matrix4x4.TRS(position ? t.position : Vector3.zero, rotationScale ? t.rotation : Quaternion.identity, rotationScale ? t.lossyScale : Vector3.one);
                toLocal = keep * t.worldToLocalMatrix;
            }
            m_Geometry.WriteTo(mesh, toLocal);
        }

        public void BakeTrailsMesh(Mesh mesh, ParticleSystemBakeMeshOptions options) => mesh?.Clear();

        public void BakeTrailsMesh(Mesh mesh, Camera camera, ParticleSystemBakeMeshOptions options) => mesh?.Clear();

        public int BakeTexture(ref Texture2D verticesTexture, ParticleSystemBakeTextureOptions options)
        {
            Stubs.Ignored("ParticleSystemRenderer.BakeTexture", "is not done yet: nothing is baked");
            return 0;
        }

        public int BakeTexture(ref Texture2D verticesTexture, Camera camera, ParticleSystemBakeTextureOptions options)
        {
            Stubs.Ignored("ParticleSystemRenderer.BakeTexture", "is not done yet: nothing is baked");
            return 0;
        }

        public int BakeTexture(ref Texture2D verticesTexture, ref Texture2D indicesTexture, ParticleSystemBakeTextureOptions options)
        {
            Stubs.Ignored("ParticleSystemRenderer.BakeTexture", "is not done yet: nothing is baked");
            return 0;
        }

        public int BakeTexture(ref Texture2D verticesTexture, ref Texture2D indicesTexture, Camera camera, ParticleSystemBakeTextureOptions options)
        {
            Stubs.Ignored("ParticleSystemRenderer.BakeTexture", "is not done yet: nothing is baked");
            return 0;
        }

        public int BakeTrailsTexture(ref Texture2D verticesTexture, ref Texture2D indicesTexture, ParticleSystemBakeTextureOptions options)
        {
            Stubs.Ignored("ParticleSystemRenderer.BakeTrailsTexture", "is not done yet: nothing is baked");
            return 0;
        }

        public int BakeTrailsTexture(ref Texture2D verticesTexture, ref Texture2D indicesTexture, Camera camera, ParticleSystemBakeTextureOptions options)
        {
            Stubs.Ignored("ParticleSystemRenderer.BakeTrailsTexture", "is not done yet: nothing is baked");
            return 0;
        }

        // ------------------------------------------------------------------ rendering

        ParticleSystem System
        {
            get
            {
                if (m_System == null || m_System.m_GameObject != m_GameObject)
                    m_System = m_GameObject != null ? m_GameObject.GetComponent<ParticleSystem>() : null;
                return m_System;
            }
        }

        internal override Bounds GetLocalBounds()
        {
            var ps = System;
            if (ps == null || ps.m_Count == 0) return default;
            var world = WorldBounds(ps);
            // The world box back in the transform's space (its corners' box).
            var inv = transform.worldToLocalMatrix;
            var c = inv.MultiplyPoint3x4(world.center);
            var e = world.extents;
            var x = inv.MultiplyVector(new Vector3(e.x, 0, 0));
            var y = inv.MultiplyVector(new Vector3(0, e.y, 0));
            var z = inv.MultiplyVector(new Vector3(0, 0, e.z));
            var extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(c, extents * 2f);
        }

        static Bounds WorldBounds(ParticleSystem ps)
        {
            var toWorld = ps.SimulationToWorld();
            var scale = ps.EmitterScale();
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < ps.m_Count; i++)
            {
                ref var p = ref ps.m_Particles[i];
                var w = toWorld.MultiplyPoint3x4(p.m_Position);
                var s = ps.CurrentSize3D(ref p);
                float r = 0.5f * maxScale * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                min = Vector3.Min(min, w - new Vector3(r, r, r));
                max = Vector3.Max(max, w + new Vector3(r, r, r));
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var ps = System;
            if (ps == null) return;
            CollectTrails(ps, camera, into);
            if (ps.m_Count == 0 || m_RenderMode == ParticleSystemRenderMode.None) return;
            // Without a material the particles draw with the error material (measured: magenta).
            var material = m_Materials.Length > 0 ? m_Materials[0] : null;
            if (material == null) material = ErrorMaterial;
            if (material == null) return;
            if (!m_Geometry.Build(this, ps, View.Of(camera))) return;
            var mesh = MeshFor(camera.Camera);
            m_Geometry.WriteTo(mesh, Matrix4x4.identity);
            float distance = FrameBuilder.Distance(camera, m_Geometry.Center);
            if (m_SortingFudge != 0f)
            {
                // Fudge moves the system's sorting distance (lower draws later, over other transparents).
                if (camera.Orthographic) distance += m_SortingFudge;
                else
                {
                    float d = Mathf.Max(0f, Mathf.Sqrt(Mathf.Max(0f, distance)) + m_SortingFudge);
                    distance = d * d;
                }
            }
            into.Add(new FrameBuilder.DrawItem
            {
                Mesh = mesh.Sync(),
                Submesh = 0,
                Material = material,
                Model = Matrix4x4.identity,
                Color = Color.white,
                MainTexture = m_Geometry.Texture != null ? TextureHandles.Of(m_Geometry.Texture) : 0,
                Queue = material.renderQueue,
                SortingLayer = SortingLayer.GetLayerValueFromID(m_SortingLayerID),
                SortingOrder = m_SortingOrder,
                Distance = distance,
                Sequence = FrameBuilder.NextSequence(),
                Masked = this,
                Groups = FrameBuilder.GroupsOf(this, camera),
            });
        }

        static Material s_ErrorMaterial;

        static Material ErrorMaterial
        {
            get
            {
                if (s_ErrorMaterial == null)
                {
                    var shader = Shader.Find("Hidden/InternalErrorShader");
                    if (shader != null) s_ErrorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return s_ErrorMaterial;
            }
        }

        readonly Geometry m_TrailGeometry = new Geometry();
        readonly Dictionary<Camera, Mesh> m_TrailMeshes = new Dictionary<Camera, Mesh>();

        void CollectTrails(ParticleSystem ps, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!ps.m_Trails.enabled || ps.m_TrailList.Count == 0) return;
            var material = trailMaterial;
            if (material == null) return;
            if (!m_TrailGeometry.BuildTrails(ps, View.Of(camera))) return;
            var key = camera.Camera;
            if (key == null || !m_TrailMeshes.TryGetValue(key, out var mesh) || mesh == null)
            {
                mesh = new Mesh { name = "ParticleSystemRenderer Trails" };
                if (key != null) m_TrailMeshes[key] = mesh;
            }
            m_TrailGeometry.WriteTo(mesh, Matrix4x4.identity);
            into.Add(new FrameBuilder.DrawItem
            {
                Mesh = mesh.Sync(),
                Submesh = 0,
                Material = material,
                Model = Matrix4x4.identity,
                Color = Color.white,
                Queue = material.renderQueue,
                SortingLayer = SortingLayer.GetLayerValueFromID(m_SortingLayerID),
                SortingOrder = m_SortingOrder,
                Distance = FrameBuilder.Distance(camera, m_TrailGeometry.Center),
                Sequence = FrameBuilder.NextSequence(),
                Masked = this,
                Groups = FrameBuilder.GroupsOf(this, camera),
            });
        }

        /// <summary>The mesh drawn for one camera (draws are submitted after every camera built its own).</summary>
        Mesh MeshFor(Camera camera)
        {
            if (camera == null)
            {
                m_FallbackMesh ??= new Mesh { name = "ParticleSystemRenderer" };
                return m_FallbackMesh;
            }
            if (m_CameraMeshes.TryGetValue(camera, out var mesh) && mesh != null) return mesh;
            if (m_CameraMeshes.Count > 8)
            {
                var dead = new List<Camera>();
                foreach (var pair in m_CameraMeshes)
                    if (pair.Key == null) dead.Add(pair.Key);
                foreach (var c in dead)
                {
                    if (m_CameraMeshes[c] != null) DestroyImmediate(m_CameraMeshes[c]);
                    m_CameraMeshes.Remove(c);
                }
            }
            mesh = new Mesh { name = "ParticleSystemRenderer" };
            m_CameraMeshes[camera] = mesh;
            return mesh;
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            foreach (var mesh in m_CameraMeshes.Values)
                if (mesh != null) DestroyImmediate(mesh);
            m_CameraMeshes.Clear();
            if (m_FallbackMesh != null) DestroyImmediate(m_FallbackMesh);
            m_FallbackMesh = null;
        }

        /// <summary>What a camera contributes to particle geometry.</summary>
        internal struct View
        {
            internal Vector3 Position;
            internal Vector3 Right;
            internal Vector3 Up;
            internal Vector3 Forward;
            internal bool Orthographic;
            internal float OrthographicSize;
            internal float TanHalfFov;

            internal static View Of(FrameBuilder.CameraContext camera) => FromMatrix(camera.View, camera.Position, camera.Orthographic, camera.Camera);

            internal static View Of(Camera camera) => FromMatrix(camera.worldToCameraMatrix, camera.transform.position, camera.orthographic, camera);

            /// <summary>The camera axes are the rows of the world-to-camera matrix (it looks down -Z).</summary>
            internal static View FromMatrix(Matrix4x4 worldToCamera, Vector3 position, bool orthographic, Camera camera)
            {
                var right = new Vector3(worldToCamera.m00, worldToCamera.m01, worldToCamera.m02).normalized;
                var up = new Vector3(worldToCamera.m10, worldToCamera.m11, worldToCamera.m12).normalized;
                var back = new Vector3(worldToCamera.m20, worldToCamera.m21, worldToCamera.m22).normalized;
                return new View
                {
                    Position = position,
                    Right = right,
                    Up = up,
                    Forward = -back,
                    Orthographic = orthographic,
                    OrthographicSize = camera != null ? camera.orthographicSize : 5f,
                    TanHalfFov = Mathf.Tan((camera != null ? camera.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad),
                };
            }
        }

        /// <summary>Builds the particles' vertices for one view, reusing its buffers.</summary>
        internal sealed class Geometry
        {
            internal Vector3[] Positions = new Vector3[64];
            internal Vector3[] Normals = new Vector3[64];
            internal Color32[] Colors = new Color32[64];
            internal Vector4[] Uvs = new Vector4[64];
            internal int[] Indices = new int[96];
            internal int VertexCount;
            internal int IndexCount;
            internal Vector3 Center;
            internal Texture Texture;
            int[] m_Order = new int[16];
            float[] m_Keys = new float[16];

            void Reserve(int vertices, int indices)
            {
                if (Positions.Length < vertices)
                {
                    int n = Math.Max(vertices, Positions.Length * 2);
                    Array.Resize(ref Positions, n);
                    Array.Resize(ref Normals, n);
                    Array.Resize(ref Colors, n);
                    Array.Resize(ref Uvs, n);
                }
                if (Indices.Length < indices) Array.Resize(ref Indices, Math.Max(indices, Indices.Length * 2));
            }

            /// <summary>The trails as camera-facing ribbons from each particle back along its points:
            /// width and color along the trail by the module's curves, u from the particle (0) to the
            /// tail (1).</summary>
            internal bool BuildTrails(ParticleSystem ps, View view)
            {
                VertexCount = 0;
                IndexCount = 0;
                Texture = null;
                var T = ps.m_Trails;
                var toWorld = T.worldSpace ? Matrix4x4.identity : ps.SimulationToWorld();
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                var points = new List<Vector3>();
                var distances = new List<float>();
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                foreach (var trail in ps.m_TrailList)
                {
                    if (trail == null || trail.Points.Count == 0) continue;
                    points.Clear();
                    var head = toWorld.MultiplyPoint3x4(trail.Head);
                    points.Add(head);
                    for (int i = trail.Points.Count - 1; i >= 0; i--)
                    {
                        var w = toWorld.MultiplyPoint3x4(trail.Points[i]);
                        if ((w - points[points.Count - 1]).sqrMagnitude > 1e-12f) points.Add(w);
                    }
                    if (points.Count < 2) continue;
                    distances.Clear();
                    float total = 0f;
                    distances.Add(0f);
                    for (int i = 1; i < points.Count; i++)
                    {
                        total += (points[i] - points[i - 1]).magnitude;
                        distances.Add(total);
                    }
                    Reserve(VertexCount + points.Count * 2, IndexCount + (points.Count - 1) * 6);
                    int start = VertexCount;
                    for (int i = 0; i < points.Count; i++)
                    {
                        float t = total > 0f ? distances[i] / total : 0f;
                        var tangent = points[Math.Max(i - 1, 0)] - points[Math.Min(i + 1, points.Count - 1)];
                        var toCamera = view.Orthographic ? -view.Forward : (view.Position - points[i]).normalized;
                        var side = Vector3.Cross(tangent, toCamera);
                        side = side.sqrMagnitude > 1e-12f ? side.normalized : view.Right;
                        float width = trail.Width * T.widthOverTrail.Evaluate(t, 1f);
                        Color color = (Color)trail.Color * T.colorOverTrail.Evaluate(t, 1f);
                        Color32 c = linear ? (Color32)color.linear : (Color32)color;
                        float u = T.textureMode == ParticleSystemTrailTextureMode.Tile ? distances[i] * T.textureScale.x : t;
                        for (int k = 0; k < 2; k++)
                        {
                            Positions[VertexCount] = points[i] + side * (width * (k == 0 ? 0.5f : -0.5f));
                            Normals[VertexCount] = toCamera;
                            Colors[VertexCount] = c;
                            Uvs[VertexCount] = new Vector4(u, k == 0 ? 1f : 0f, 0f, 0f);
                            VertexCount++;
                        }
                        min = Vector3.Min(min, points[i]);
                        max = Vector3.Max(max, points[i]);
                    }
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        int a = start + i * 2;
                        Indices[IndexCount++] = a;
                        Indices[IndexCount++] = a + 2;
                        Indices[IndexCount++] = a + 1;
                        Indices[IndexCount++] = a + 1;
                        Indices[IndexCount++] = a + 2;
                        Indices[IndexCount++] = a + 3;
                    }
                }
                if (VertexCount == 0) return false;
                Center = (min + max) * 0.5f;
                return true;
            }

            internal bool Build(ParticleSystemRenderer r, ParticleSystem ps, View view)
            {
                VertexCount = 0;
                IndexCount = 0;
                Texture = null;
                int count = ps.m_Count;
                if (count == 0) return false;
                var toWorld = ps.SimulationToWorld();
                var scale = ps.EmitterScale();
                var systemRotation = ps.transform.rotation;
                var sheet = ps.m_TextureSheet;
                if (sheet.enabled && sheet.mode == ParticleSystemAnimationMode.Sprites)
                    foreach (var s in sheet.sprites)
                        if (s != null) { Texture = s.texture; break; }

                Order(r, ps, view, toWorld);

                Mesh[] meshes = null;
                if (r.m_RenderMode == ParticleSystemRenderMode.Mesh)
                {
                    meshes = r.m_Meshes.FindAll(m => m != null).ToArray();
                    if (meshes.Length == 0) return false;
                }

                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                for (int o = 0; o < count; o++)
                {
                    int i = m_Order[o];
                    ref var p = ref ps.m_Particles[i];
                    var position = toWorld.MultiplyPoint3x4(p.m_Position);
                    var size = Vector3.Scale(ps.CurrentSize3D(ref p), scale);
                    Color32 color = ps.CurrentColor(ref p);
                    // Vertex colors reach the shader in the project's color space (measured).
                    if (linear) color = ((Color)color).linear;
                    var uvRect = FrameRect(ps, ref p);
                    uint seed = p.m_RandomSeed;
                    bool flipU = r.m_Flip.x > 0f && R.Value(seed, R.FlipU) < r.m_Flip.x;
                    bool flipV = r.m_Flip.y > 0f && R.Value(seed, R.FlipV) < r.m_Flip.y;
                    if (flipU) uvRect = new Rect(uvRect.xMax, uvRect.y, -uvRect.width, uvRect.height);
                    if (flipV) uvRect = new Rect(uvRect.x, uvRect.yMax, uvRect.width, -uvRect.height);
                    // Billboards turn clockwise for a positive rotation, meshes counterclockwise
                    // (measured).
                    var euler = new Vector3(p.m_Rotation.x, p.m_Rotation.y, -p.m_Rotation.z) * Mathf.Rad2Deg;
                    var rotation = Quaternion.Euler(euler);

                    if (meshes != null)
                    {
                        var m = meshes[Math.Min(ps.MeshIndexOf(ref p), meshes.Length - 1) % meshes.Length];
                        AddMesh(r, m, position, size, Quaternion.Euler(p.m_Rotation * Mathf.Rad2Deg), systemRotation, color, uvRect, toWorld.MultiplyVector(p.m_Velocity + p.m_AnimatedVelocity), view);
                    }
                    else
                    {
                        if (r.m_RenderMode != ParticleSystemRenderMode.Stretch)
                            size = ClampToScreen(r, view, position, size);
                        AddQuad(r, position, size, rotation, systemRotation, color, uvRect, toWorld.MultiplyVector(p.m_Velocity + p.m_AnimatedVelocity), view);
                    }
                    float extent = 0.5f * Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z)));
                    min = Vector3.Min(min, position - new Vector3(extent, extent, extent));
                    max = Vector3.Max(max, position + new Vector3(extent, extent, extent));
                }
                Center = (min + max) * 0.5f;
                return VertexCount > 0;
            }

            /// <summary>Draw order by the sort mode (later particles draw over earlier ones).</summary>
            void Order(ParticleSystemRenderer r, ParticleSystem ps, View view, Matrix4x4 toWorld)
            {
                int count = ps.m_Count;
                if (m_Order.Length < count)
                {
                    m_Order = new int[Math.Max(count, m_Order.Length * 2)];
                    m_Keys = new float[m_Order.Length];
                }
                for (int i = 0; i < count; i++) m_Order[i] = i;
                var mode = r.m_SortMode;
                if (mode == ParticleSystemSortMode.None) return;
                for (int i = 0; i < count; i++)
                {
                    ref var p = ref ps.m_Particles[i];
                    float key;
                    switch (mode)
                    {
                        case ParticleSystemSortMode.Distance:
                        case ParticleSystemSortMode.DistanceReverse:
                        {
                            var w = toWorld.MultiplyPoint3x4(p.m_Position);
                            // Far first: sort ascending by negative distance.
                            key = view.Orthographic ? -Vector3.Dot(w - view.Position, view.Forward) : -(w - view.Position).sqrMagnitude;
                            if (mode == ParticleSystemSortMode.DistanceReverse) key = -key;
                            break;
                        }
                        case ParticleSystemSortMode.Depth:
                        case ParticleSystemSortMode.DepthReverse:
                        {
                            var w = toWorld.MultiplyPoint3x4(p.m_Position);
                            key = -Vector3.Dot(w - view.Position, view.Forward);
                            if (mode == ParticleSystemSortMode.DepthReverse) key = -key;
                            break;
                        }
                        case ParticleSystemSortMode.OldestInFront:
                            // Youngest first, oldest drawn last.
                            key = p.m_StartLifetime - p.m_Lifetime;
                            break;
                        case ParticleSystemSortMode.YoungestInFront:
                            key = p.m_Lifetime - p.m_StartLifetime;
                            break;
                        default:
                            key = i;
                            break;
                    }
                    m_Keys[i] = key;
                }
                Array.Sort(m_Keys, m_Order, 0, count);
            }

            static Vector3 ClampToScreen(ParticleSystemRenderer r, View view, Vector3 position, Vector3 size)
            {
                float extent = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y));
                if (extent <= 0f) return size;
                float height = view.Orthographic
                    ? 2f * view.OrthographicSize
                    : 2f * Mathf.Max(Vector3.Dot(position - view.Position, view.Forward), 1e-4f) * view.TanHalfFov;
                if (height <= 0f) return size;
                float fraction = extent / height;
                float min = r.m_MinParticleSize, max = r.m_MaxParticleSize;
                if (max > 0f && fraction > max) return size * (max / fraction);
                if (min > 0f && fraction < min) return size * (min / fraction);
                return size;
            }

            /// <summary>The particle's uv rectangle from the texture sheet animation.</summary>
            static Rect FrameRect(ParticleSystem ps, ref ParticleSystem.Particle p)
            {
                var T = ps.m_TextureSheet;
                if (!T.enabled) return new Rect(0f, 0f, 1f, 1f);
                uint seed = p.m_RandomSeed;
                float age = p.Age01;
                int frames;
                if (T.mode == ParticleSystemAnimationMode.Sprites) frames = Math.Max(1, T.sprites.Count);
                else frames = T.animation == ParticleSystemAnimationType.SingleRow ? Math.Max(1, T.numTilesX) : Math.Max(1, T.numTilesX * T.numTilesY);

                float phase;
                switch (T.timeMode)
                {
                    case ParticleSystemAnimationTimeMode.Speed:
                    {
                        float s = Mathf.InverseLerp(T.speedRange.x, T.speedRange.y, (p.m_Velocity + p.m_AnimatedVelocity).magnitude);
                        phase = T.frameOverTime.Evaluate(s, R.Value(seed, R.Frame));
                        break;
                    }
                    case ParticleSystemAnimationTimeMode.FPS:
                        phase = (p.m_StartLifetime - p.m_Lifetime) * T.fps / frames;
                        break;
                    default:
                    {
                        float cycles = Mathf.Max(1, T.cycleCount);
                        float t = age * cycles;
                        t = t - Mathf.Floor(t);
                        if (age >= 1f) t = 1f;
                        phase = T.frameOverTime.Evaluate(t, R.Value(seed, R.Frame));
                        break;
                    }
                }
                float f = T.startFrame.Evaluate(age, R.Value(seed, R.StartFrame)) + phase;
                f -= Mathf.Floor(f);
                int frame = Math.Min((int)(f * frames), frames - 1);

                if (T.mode == ParticleSystemAnimationMode.Sprites)
                {
                    var sprite = T.sprites.Count > 0 ? T.sprites[frame] : null;
                    var tex = sprite != null ? sprite.texture : null;
                    if (tex == null || tex.width == 0 || tex.height == 0) return new Rect(0f, 0f, 1f, 1f);
                    var tr = sprite.textureRect;
                    return new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                }

                int tilesX = Math.Max(1, T.numTilesX), tilesY = Math.Max(1, T.numTilesY);
                int column, row;
                if (T.animation == ParticleSystemAnimationType.SingleRow)
                {
                    column = frame;
                    switch (T.rowMode)
                    {
                        case ParticleSystemAnimationRowMode.Random:
                            row = Math.Min((int)(R.Value(seed, R.Row) * tilesY), tilesY - 1);
                            break;
                        case ParticleSystemAnimationRowMode.MeshIndex:
                            row = ps.MeshIndexOf(ref p) % tilesY;
                            break;
                        default:
                            row = Mathf.Clamp(T.rowIndex, 0, tilesY - 1);
                            break;
                    }
                }
                else
                {
                    column = frame % tilesX;
                    row = frame / tilesX;
                }
                // Frame 0 is the top-left tile; uv (0,0) is the texture's bottom-left.
                float w = 1f / tilesX, h = 1f / tilesY;
                return new Rect(column * w, 1f - (row + 1) * h, w, h);
            }

            static readonly Vector2[] s_Corners = { new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, -0.5f) };
            static readonly Vector2[] s_CornerUvs = { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };

            void AddQuad(ParticleSystemRenderer r, Vector3 position, Vector3 size, Quaternion rotation, Quaternion systemRotation, Color32 color, Rect uv, Vector3 velocity, View view)
            {
                Vector3 right, up;
                var toCamera = view.Orthographic ? -view.Forward : (view.Position - position).normalized;
                switch (r.m_RenderMode)
                {
                    case ParticleSystemRenderMode.Stretch:
                        AddStretched(r, position, size, color, uv, velocity, view, toCamera);
                        return;
                    case ParticleSystemRenderMode.HorizontalBillboard:
                        right = Vector3.right;
                        up = Vector3.forward;
                        break;
                    case ParticleSystemRenderMode.VerticalBillboard:
                    {
                        up = Vector3.up;
                        var facing = new Vector3(-view.Forward.x, 0f, -view.Forward.z);
                        if (facing.sqrMagnitude < 1e-8f) facing = Vector3.back;
                        right = Vector3.Cross(facing.normalized, up).normalized;
                        break;
                    }
                    default:
                        Axes(r.m_RenderAlignment, view, toCamera, systemRotation, velocity, out right, out up);
                        break;
                }
                // Horizontal and vertical billboards are 1/√2 of the particle's size (measured).
                if (r.m_RenderMode == ParticleSystemRenderMode.HorizontalBillboard || r.m_RenderMode == ParticleSystemRenderMode.VerticalBillboard)
                    size *= 0.70710678f;
                var normal = Vector3.Cross(up, right);
                var pivot = new Vector3(r.m_Pivot.x * size.x, r.m_Pivot.y * size.y, r.m_Pivot.z * size.z);
                Reserve(VertexCount + 4, IndexCount + 6);
                int start = VertexCount;
                var faceNormal = r.m_RenderMode == ParticleSystemRenderMode.HorizontalBillboard ? Vector3.up : toCamera;
                for (int c = 0; c < 4; c++)
                {
                    var corner = new Vector3(s_Corners[c].x * size.x, s_Corners[c].y * size.y, 0f) + pivot;
                    var o = rotation * corner;
                    Positions[VertexCount] = position + right * o.x + up * o.y + normal * o.z;
                    Normals[VertexCount] = faceNormal;
                    Colors[VertexCount] = color;
                    Uvs[VertexCount] = new Vector4(uv.x + s_CornerUvs[c].x * uv.width, uv.y + s_CornerUvs[c].y * uv.height, 0f, 0f);
                    VertexCount++;
                }
                AddQuadIndices(start);
            }

            void AddQuadIndices(int start)
            {
                Indices[IndexCount++] = start;
                Indices[IndexCount++] = start + 1;
                Indices[IndexCount++] = start + 2;
                Indices[IndexCount++] = start + 2;
                Indices[IndexCount++] = start + 3;
                Indices[IndexCount++] = start;
            }

            static void Axes(ParticleSystemRenderSpace alignment, View view, Vector3 toCamera, Quaternion systemRotation, Vector3 velocity, out Vector3 right, out Vector3 up)
            {
                switch (alignment)
                {
                    case ParticleSystemRenderSpace.World:
                        right = Vector3.right;
                        up = Vector3.up;
                        return;
                    case ParticleSystemRenderSpace.Local:
                        right = systemRotation * Vector3.right;
                        up = systemRotation * Vector3.up;
                        return;
                    case ParticleSystemRenderSpace.Facing:
                    {
                        // Faces the camera's position (not its plane), keeping the camera's up.
                        var f = -toCamera;
                        right = Vector3.Cross(view.Up, f);
                        if (right.sqrMagnitude < 1e-8f) right = view.Right;
                        right.Normalize();
                        up = Vector3.Cross(f, right).normalized;
                        return;
                    }
                    case ParticleSystemRenderSpace.Velocity when velocity.sqrMagnitude > 1e-10f:
                    {
                        var q = Quaternion.LookRotation(velocity);
                        right = q * Vector3.right;
                        up = q * Vector3.up;
                        return;
                    }
                    default:
                        right = view.Right;
                        up = view.Up;
                        return;
                }
            }

            /// <summary>A quad along the particle's screen velocity: width is the size, length is
            /// size × length scale + speed × velocity scale, trailing behind the particle; u runs from
            /// the particle back (measured).</summary>
            void AddStretched(ParticleSystemRenderer r, Vector3 position, Vector3 size, Color32 color, Rect uv, Vector3 velocity, View view, Vector3 toCamera)
            {
                float speed = velocity.magnitude;
                Vector3 axis = speed > 1e-6f ? velocity / speed : view.Up;
                var onScreen = axis - toCamera * Vector3.Dot(axis, toCamera);
                if (onScreen.sqrMagnitude < 1e-10f) onScreen = view.Up;
                axis = onScreen.normalized;
                var side = Vector3.Cross(toCamera, axis).normalized;
                float width = size.x;
                float length = size.y * r.m_LengthScale + speed * r.m_VelocityScale;
                var back = axis * length;
                var halfWidth = side * (width * 0.5f);
                Reserve(VertexCount + 4, IndexCount + 6);
                int start = VertexCount;
                var corners = new[] { position - back - halfWidth, position - back + halfWidth, position + halfWidth, position - halfWidth };
                var uvs = new[] { new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f) };
                for (int c = 0; c < 4; c++)
                {
                    Positions[VertexCount] = corners[c];
                    Normals[VertexCount] = toCamera;
                    Colors[VertexCount] = color;
                    Uvs[VertexCount] = new Vector4(uv.x + uvs[c].x * uv.width, uv.y + uvs[c].y * uv.height, 0f, 0f);
                    VertexCount++;
                }
                AddQuadIndices(start);
            }

            void AddMesh(ParticleSystemRenderer r, Mesh mesh, Vector3 position, Vector3 size, Quaternion rotation, Quaternion systemRotation, Color32 color, Rect uv, Vector3 velocity, View view)
            {
                var vertices = mesh.m_Vertices;
                int n = vertices.Length;
                if (n == 0) return;
                Quaternion orientation;
                switch (r.m_RenderAlignment)
                {
                    case ParticleSystemRenderSpace.View:
                        orientation = Quaternion.LookRotation(view.Forward, view.Up);
                        break;
                    case ParticleSystemRenderSpace.Local:
                        orientation = systemRotation;
                        break;
                    case ParticleSystemRenderSpace.Facing:
                        orientation = Quaternion.LookRotation(position - view.Position, view.Up);
                        break;
                    case ParticleSystemRenderSpace.Velocity when velocity.sqrMagnitude > 1e-10f:
                        orientation = Quaternion.LookRotation(velocity);
                        break;
                    default:
                        orientation = Quaternion.identity;
                        break;
                }
                var q = orientation * rotation;
                var normals = mesh.m_Normals;
                var colors = mesh.m_Colors;
                var uvs = mesh.m_UVs[0];
                int indexTotal = 0;
                foreach (var s in mesh.m_SubMeshes) if (s.Topology == MeshTopology.Triangles) indexTotal += s.Indices.Length;
                Reserve(VertexCount + n, IndexCount + indexTotal);
                int start = VertexCount;
                Color tint = color;
                for (int v = 0; v < n; v++)
                {
                    Positions[VertexCount] = position + q * Vector3.Scale(vertices[v], size);
                    Normals[VertexCount] = normals != null && v < normals.Length ? q * normals[v] : -view.Forward;
                    Colors[VertexCount] = colors != null && v < colors.Length ? (Color32)((Color)colors[v] * tint) : color;
                    var t = uvs != null && v < uvs.Length ? uvs[v] : Vector4.zero;
                    Uvs[VertexCount] = new Vector4(uv.x + t.x * uv.width, uv.y + t.y * uv.height, 0f, 0f);
                    VertexCount++;
                }
                foreach (var s in mesh.m_SubMeshes)
                {
                    if (s.Topology != MeshTopology.Triangles) continue;
                    foreach (var index in s.Indices) Indices[IndexCount++] = start + s.BaseVertex + index;
                }
            }

            /// <summary>Writes the built geometry into a mesh (vertices transformed by `transform`).</summary>
            internal void WriteTo(Mesh mesh, Matrix4x4 transform)
            {
                bool identity = transform == Matrix4x4.identity;
                int n = VertexCount;
                var positions = mesh.m_Vertices.Length == n ? mesh.m_Vertices : new Vector3[n];
                var normals = mesh.m_Normals != null && mesh.m_Normals.Length == n ? mesh.m_Normals : new Vector3[n];
                var colors = mesh.m_Colors != null && mesh.m_Colors.Length == n ? mesh.m_Colors : new Color32[n];
                var uvs = mesh.m_UVs[0] != null && mesh.m_UVs[0].Length == n ? mesh.m_UVs[0] : new Vector4[n];
                for (int i = 0; i < n; i++)
                {
                    positions[i] = identity ? Positions[i] : transform.MultiplyPoint3x4(Positions[i]);
                    normals[i] = identity ? Normals[i] : transform.MultiplyVector(Normals[i]).normalized;
                }
                Array.Copy(Colors, colors, n);
                Array.Copy(Uvs, uvs, n);
                mesh.m_Vertices = positions;
                mesh.m_Normals = n > 0 ? normals : null;
                mesh.m_Colors = n > 0 ? colors : null;
                mesh.m_UVs[0] = n > 0 ? uvs : null;
                mesh.m_UVDimensions[0] = 2;
                for (int c = 1; c < 8; c++) mesh.m_UVs[c] = null;
                mesh.m_Tangents = null;
                if (mesh.m_SubMeshes.Count != 1)
                {
                    mesh.m_SubMeshes.Clear();
                    mesh.m_SubMeshes.Add(new Mesh.SubMesh());
                }
                var sub = mesh.m_SubMeshes[0];
                if (sub.Indices.Length != IndexCount) sub.Indices = new int[IndexCount];
                Array.Copy(Indices, sub.Indices, IndexCount);
                sub.Topology = MeshTopology.Triangles;
                sub.BaseVertex = 0;
                mesh.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.RecalculateBounds();
                mesh.m_Dirty = true;
            }
        }
    }
}
