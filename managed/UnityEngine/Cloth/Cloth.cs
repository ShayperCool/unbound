using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public struct ClothSkinningCoefficient
    {
        public float maxDistance;
        public float collisionSphereDistance;
    }

    public struct ClothSphereColliderPair
    {
        public ClothSphereColliderPair(SphereCollider a)
        {
            first = a;
            second = null;
        }

        public ClothSphereColliderPair(SphereCollider a, SphereCollider b)
        {
            first = a;
            second = b;
        }

        public SphereCollider first { get; set; }
        public SphereCollider second { get; set; }
    }

    /// <summary>
    /// A cloth on its GameObject's SkinnedMeshRenderer: NvCloth (native/nvcloth, the solver Unity
    /// runs) set up as Unity sets it up (measured: tests/projects/cloth). The particles are the
    /// mesh's vertices welded by position, in the transform's frame without its scale (the scale is
    /// in the particles); vertices whose max distance is 0 are fixed to the mesh. The fabric is
    /// cooked with geodesic tethers; stretching stiffness drives the vertical and horizontal
    /// phases, bending stiffness the bending ones, shearing stays stiff; world acceleration scale is
    /// the frame's inertia. Cloths step by the fixed time step, once per physics step of the frame,
    /// after the frame's scripts, while their renderer updates offscreen or was visible. A mesh
    /// skinned by bones simulates in its root bone's frame.
    /// </summary>
    [ClassId(183)]
    [RequireComponent(typeof(Transform), typeof(SkinnedMeshRenderer))]
    public sealed partial class Cloth : Component
    {
        // Unity's virtual particle weights (the first is the one each triangle gets).
        static readonly Vector3[] s_DefaultWeights =
        {
            new Vector3(1f / 3f, 1f / 3f, 1f / 3f),
            new Vector3(2f / 3f, 1f / 6f, 1f / 6f),
            new Vector3(0.5f, 0.5f, 0f),
        };

        // Serialized state.
        internal float m_StretchingStiffness = 1f;
        internal float m_BendingStiffness;
        internal bool m_UseTethers = true;
        internal bool m_UseGravity = true;
        internal float m_Damping;
        internal Vector3 m_ExternalAcceleration;
        internal Vector3 m_RandomAcceleration;
        internal float m_WorldVelocityScale = 0.5f;
        internal float m_WorldAccelerationScale = 1f;
        internal float m_Friction = 0.5f;
        internal float m_CollisionMassScale;
        internal bool m_UseContinuousCollision = true;
        internal float m_UseVirtualParticles = 1f;
        internal float m_SolverFrequency = 120f;
        internal float m_StiffnessFrequency = 10f;
        internal float m_SleepThreshold = 0.1f;
        internal float m_SelfCollisionDistance;
        internal float m_SelfCollisionStiffness = 0.2f;
        internal bool m_Enabled = true;
        internal ClothSkinningCoefficient[] m_Coefficients;
        internal CapsuleCollider[] m_CapsuleColliders = Array.Empty<CapsuleCollider>();
        internal ClothSphereColliderPair[] m_SphereColliders = Array.Empty<ClothSphereColliderPair>();
        internal readonly List<uint> m_SelfAndInterCollisionIndices = new List<uint>();
        internal readonly List<uint> m_VirtualParticleIndices = new List<uint>();
        internal readonly List<Vector3> m_VirtualParticleWeights = new List<Vector3>();

        // Runtime state.
        internal bool m_Active;
        internal ulong m_Native;
        Mesh m_Source;
        /// <summary>Per mesh vertex its particle.</summary>
        int[] m_ParticleOf = Array.Empty<int>();
        /// <summary>Per particle its first mesh vertex.</summary>
        int[] m_VertexOf = Array.Empty<int>();
        /// <summary>Triangles over particles (degenerate ones removed).</summary>
        uint[] m_Triangles = Array.Empty<uint>();
        /// <summary>The particles' skinned positions and normals in the cloth's frame.</summary>
        Vector3[] m_Targets = Array.Empty<Vector3>();
        Vector3[] m_TargetNormals = Array.Empty<Vector3>();
        /// <summary>The particles after the last step, in the cloth's frame.</summary>
        Vector3[] m_Positions = Array.Empty<Vector3>();
        Vector3[] m_Normals = Array.Empty<Vector3>();
        Mesh m_Rendered;
        int m_RenderedVersion = -1;
        int m_Version;
        bool m_Dirty = true;
        bool m_ConstraintsDirty = true;
        bool m_CollidersDirty = true;
        /// <summary>How much of the simulation the vertices show (1 but while fading) and how fast
        /// that changes per second.</summary>
        float m_Weight = 1f;
        float m_FadeRate;
        /// <summary>A fade out ended: the cloth is to be disabled after the step.</summary>
        internal bool m_FadedOut;
        /// <summary>The cloth has stepped since it was (re)created: its particles are its own.</summary>
        bool m_Simulated;
        /// <summary>The native cloth is to be created at the next step (from the pose then).</summary>
        internal bool m_SetupPending;

        public Cloth() { }

        // ------------------------------------------------------------------ lifecycle

        internal override void OnEngineEnable()
        {
            m_Active = true;
            if (m_Enabled) ClothWorld.Add(this);
        }

        internal override void OnEngineDisable()
        {
            m_Active = false;
            ClothWorld.Remove(this);
        }

        internal override void OnEngineDestroy()
        {
            m_Active = false;
            ClothWorld.Remove(this);
            if (m_Rendered != null) DestroyImmediate(m_Rendered);
        }

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value) return;
                m_Enabled = value;
                if (!m_Active) return;
                // Enabled again, a cloth starts over from its mesh (measured).
                if (value) ClothWorld.Add(this);
                else ClothWorld.Remove(this);
            }
        }

        public void SetEnabledFading(bool enabled) => SetEnabledFading(enabled, 0.5f);

        /// <summary>Fades the cloth's vertices to or from its skinned pose over a time: simulation
        /// goes on underneath; faded out, the cloth is disabled; enabled again, it starts over
        /// (measured).</summary>
        public void SetEnabledFading(bool enabled, float interpolationTime)
        {
            if (interpolationTime <= 0f)
            {
                m_FadeRate = 0f;
                m_Weight = 1f;
                this.enabled = enabled;
                return;
            }
            if (enabled)
            {
                if (!m_Enabled)
                {
                    m_Weight = 0f;
                    this.enabled = true;
                }
                m_FadeRate = 1f / interpolationTime;
            }
            else if (m_Enabled)
            {
                m_FadeRate = -1f / interpolationTime;
            }
        }

        /// <summary>Advances a fade by a step; a cloth faded out is disabled.</summary>
        internal void Fade(float dt)
        {
            if (m_FadeRate == 0f) return;
            m_Weight = Mathf.Clamp01(m_Weight + m_FadeRate * dt);
            if (m_FadeRate > 0f && m_Weight >= 1f) m_FadeRate = 0f;
            else if (m_FadeRate < 0f && m_Weight <= 0f)
            {
                m_FadeRate = 0f;
                m_Weight = 1f;
                m_FadedOut = true;
            }
        }

        public void ClearTransformMotion()
        {
            if (m_Native == 0) return;
            var t = Frame;
            ClothNative.uac_cloth_teleport(m_Native, t.position, t.rotation);
        }

        // ------------------------------------------------------------------ settings

        void Changed() => m_Dirty = true;

        public float sleepThreshold { get => m_SleepThreshold; set { m_SleepThreshold = value; Changed(); } }
        public float bendingStiffness { get => m_BendingStiffness; set { m_BendingStiffness = value; Changed(); } }
        public float stretchingStiffness { get => m_StretchingStiffness; set { m_StretchingStiffness = value; Changed(); } }
        public float damping { get => m_Damping; set { m_Damping = value; Changed(); } }
        public Vector3 externalAcceleration { get => m_ExternalAcceleration; set { m_ExternalAcceleration = value; Changed(); } }
        public Vector3 randomAcceleration { get => m_RandomAcceleration; set { m_RandomAcceleration = value; Changed(); } }
        public bool useGravity { get => m_UseGravity; set { m_UseGravity = value; Changed(); } }
        public float friction { get => m_Friction; set { m_Friction = value; Changed(); } }
        public float collisionMassScale { get => m_CollisionMassScale; set { m_CollisionMassScale = value; Changed(); } }
        public bool enableContinuousCollision { get => m_UseContinuousCollision; set { m_UseContinuousCollision = value; Changed(); } }

        public float useVirtualParticles
        {
            get => m_UseVirtualParticles;
            set { m_UseVirtualParticles = value; m_ConstraintsDirty = true; }
        }

        public float worldVelocityScale { get => m_WorldVelocityScale; set { m_WorldVelocityScale = value; Changed(); } }
        public float worldAccelerationScale { get => m_WorldAccelerationScale; set { m_WorldAccelerationScale = value; Changed(); } }
        public float clothSolverFrequency { get => m_SolverFrequency; set { m_SolverFrequency = value; Changed(); } }
        public bool useTethers { get => m_UseTethers; set { m_UseTethers = value; Changed(); } }
        public float stiffnessFrequency { get => m_StiffnessFrequency; set { m_StiffnessFrequency = value; Changed(); } }
        public float selfCollisionDistance { get => m_SelfCollisionDistance; set { m_SelfCollisionDistance = value; Changed(); } }
        public float selfCollisionStiffness { get => m_SelfCollisionStiffness; set { m_SelfCollisionStiffness = value; Changed(); } }

        [Obsolete("Parameter solverFrequency is obsolete and no longer supported. Please use clothSolverFrequency instead.")]
        public bool solverFrequency { get => m_SolverFrequency > 0f; set => clothSolverFrequency = value ? 120f : 0f; }

        [Obsolete("useContinuousCollision is no longer supported, use enableContinuousCollision instead")]
        public float useContinuousCollision { get; set; }

        [Obsolete("Deprecated.Cloth.selfCollisions is no longer supported since Unity 5.0.")]
        [Unbound.EditorFaithful("obsolete since Unity 5.0: Unity answers false too")]
        public bool selfCollision => false;

        public ClothSkinningCoefficient[] coefficients
        {
            get
            {
                EnsureMesh();
                return (ClothSkinningCoefficient[])m_Coefficients.Clone();
            }
            set
            {
                EnsureMesh();
                if (value == null || value.Length != m_Coefficients.Length)
                {
                    Debug.LogError("Number of coefficients must match number of cloth vertices", this);
                    return;
                }
                var old = m_Coefficients;
                m_Coefficients = (ClothSkinningCoefficient[])value.Clone();
                // Fixing or freeing a vertex changes the fabric (its tethers); limits only constraints.
                for (int i = 0; i < old.Length; i++)
                {
                    if ((old[i].maxDistance == 0f) != (m_Coefficients[i].maxDistance == 0f))
                    {
                        Rebuild();
                        return;
                    }
                }
                m_ConstraintsDirty = true;
            }
        }

        public CapsuleCollider[] capsuleColliders
        {
            get => (CapsuleCollider[])m_CapsuleColliders.Clone();
            set { m_CapsuleColliders = value != null ? (CapsuleCollider[])value.Clone() : Array.Empty<CapsuleCollider>(); m_CollidersDirty = true; }
        }

        public ClothSphereColliderPair[] sphereColliders
        {
            get => (ClothSphereColliderPair[])m_SphereColliders.Clone();
            set { m_SphereColliders = value != null ? (ClothSphereColliderPair[])value.Clone() : Array.Empty<ClothSphereColliderPair>(); m_CollidersDirty = true; }
        }

        public void GetSelfAndInterCollisionIndices(List<uint> indices)
        {
            if (indices == null) throw new ArgumentNullException(nameof(indices));
            indices.Clear();
            indices.AddRange(m_SelfAndInterCollisionIndices);
        }

        public void SetSelfAndInterCollisionIndices(List<uint> indices)
        {
            m_SelfAndInterCollisionIndices.Clear();
            if (indices != null) m_SelfAndInterCollisionIndices.AddRange(indices);
            m_ConstraintsDirty = true;
        }

        public void GetVirtualParticleIndices(List<uint> indicesOutList)
        {
            if (indicesOutList == null) throw new ArgumentNullException(nameof(indicesOutList));
            indicesOutList.Clear();
            indicesOutList.AddRange(m_VirtualParticleIndices);
        }

        public void SetVirtualParticleIndices(List<uint> indicesIn)
        {
            m_VirtualParticleIndices.Clear();
            if (indicesIn != null) m_VirtualParticleIndices.AddRange(indicesIn);
            m_ConstraintsDirty = true;
        }

        public void GetVirtualParticleWeights(List<Vector3> weightsOutList)
        {
            if (weightsOutList == null) throw new ArgumentNullException(nameof(weightsOutList));
            weightsOutList.Clear();
            weightsOutList.AddRange(m_VirtualParticleWeights.Count > 0 ? m_VirtualParticleWeights : (IEnumerable<Vector3>)s_DefaultWeights);
        }

        public void SetVirtualParticleWeights(List<Vector3> weights)
        {
            m_VirtualParticleWeights.Clear();
            if (weights != null) m_VirtualParticleWeights.AddRange(weights);
            m_ConstraintsDirty = true;
        }

        /// <summary>The particles' positions in the cloth's frame (the transform without its scale).</summary>
        public Vector3[] vertices
        {
            get
            {
                EnsureMesh();
                return PerParticle(m_Simulated ? m_Positions : m_Targets);
            }
        }

        public Vector3[] normals
        {
            get
            {
                EnsureMesh();
                return PerParticle(m_Simulated ? m_Normals : m_TargetNormals);
            }
        }

        Vector3[] PerParticle(Vector3[] values) => (Vector3[])values.Clone();

        // ------------------------------------------------------------------ the mesh

        SkinnedMeshRenderer Renderer => GetComponent<SkinnedMeshRenderer>();

        /// <summary>The cloth's frame: the root bone of a mesh skinned by bones (its fixed
        /// vertices move with it, measured), else the cloth's transform.</summary>
        Transform Frame
        {
            get
            {
                var renderer = Renderer;
                return renderer != null && renderer.IsBoneSkinned && renderer.rootBone != null ? renderer.rootBone : transform;
            }
        }

        /// <summary>Welds the renderer's mesh into particles (vertices at the same position are one
        /// particle) and takes the skinned rest pose.</summary>
        void EnsureMesh()
        {
            var renderer = Renderer;
            var mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == m_Source && m_Coefficients != null) return;
            m_Source = mesh;
            if (mesh == null)
            {
                m_ParticleOf = Array.Empty<int>();
                m_VertexOf = Array.Empty<int>();
                m_Triangles = Array.Empty<uint>();
                m_Targets = m_TargetNormals = Array.Empty<Vector3>();
                m_Coefficients ??= Array.Empty<ClothSkinningCoefficient>();
                return;
            }
            var vertices = mesh.vertices;
            var map = new Dictionary<Vector3, int>();
            m_ParticleOf = new int[vertices.Length];
            var first = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!map.TryGetValue(vertices[i], out int p))
                {
                    p = first.Count;
                    map[vertices[i]] = p;
                    first.Add(i);
                }
                m_ParticleOf[i] = p;
            }
            m_VertexOf = first.ToArray();
            var triangles = new List<uint>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    int a = m_ParticleOf[t[k]], b = m_ParticleOf[t[k + 1]], c = m_ParticleOf[t[k + 2]];
                    if (a == b || b == c || a == c) continue;
                    triangles.Add((uint)a);
                    triangles.Add((uint)b);
                    triangles.Add((uint)c);
                }
            }
            m_Triangles = triangles.ToArray();
            if (m_Coefficients == null || m_Coefficients.Length != m_VertexOf.Length)
            {
                m_Coefficients = new ClothSkinningCoefficient[m_VertexOf.Length];
                for (int i = 0; i < m_Coefficients.Length; i++)
                    m_Coefficients[i] = new ClothSkinningCoefficient { maxDistance = float.MaxValue, collisionSphereDistance = float.MaxValue };
            }
            UpdateTargets();
            m_Positions = (Vector3[])m_Targets.Clone();
            m_Normals = (Vector3[])m_TargetNormals.Clone();
        }

        /// <summary>The skinned pose of the particles in the cloth's frame.</summary>
        void UpdateTargets()
        {
            var renderer = Renderer;
            var mesh = m_Source;
            if (renderer == null || mesh == null) return;
            var t = transform;
            var f = Frame;
            var frame = Matrix4x4.TRS(f.position, f.rotation, Vector3.one).inverse;
            Vector3[] positions, normals;
            if (renderer.SkinnedPose(out positions, out normals))
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = frame.MultiplyPoint3x4(positions[i]);
                    if (normals != null) normals[i] = frame.MultiplyVector(normals[i]);
                }
            }
            else
            {
                var scale = Matrix4x4.Scale(t.lossyScale);
                positions = mesh.vertices;
                normals = mesh.normals;
                for (int i = 0; i < positions.Length; i++) positions[i] = scale.MultiplyPoint3x4(positions[i]);
                if (normals != null && normals.Length != positions.Length) normals = null;
            }
            m_Targets = new Vector3[m_VertexOf.Length];
            m_TargetNormals = new Vector3[m_VertexOf.Length];
            for (int p = 0; p < m_VertexOf.Length; p++)
            {
                m_Targets[p] = positions[m_VertexOf[p]];
                m_TargetNormals[p] = normals != null ? normals[m_VertexOf[p]].normalized : Vector3.zero;
            }
        }

        // ------------------------------------------------------------------ simulation

        internal bool ShouldSimulate
        {
            get
            {
                var renderer = Renderer;
                // Visible as the last culling saw it (a new cloth that does not update offscreen
                // waits a step), the renderer enabled or not (measured).
                return renderer != null && (renderer.updateWhenOffscreen || renderer.isVisible);
            }
        }

        /// <summary>Creates the native cloth from the mesh's rest pose.</summary>
        internal void Setup()
        {
            Release();
            m_Source = null;
            EnsureMesh();
            int count = m_Targets.Length;
            if (count == 0 || m_Triangles.Length == 0) return;
            var particles = new float[4 * count];
            for (int i = 0; i < count; i++)
            {
                particles[4 * i] = m_Targets[i].x;
                particles[4 * i + 1] = m_Targets[i].y;
                particles[4 * i + 2] = m_Targets[i].z;
                particles[4 * i + 3] = m_Coefficients[i].maxDistance == 0f ? 0f : 1f;
            }
            unsafe
            {
                fixed (float* p = particles)
                fixed (uint* tri = m_Triangles)
                    m_Native = ClothNative.uac_cloth_create(p, count, tri, m_Triangles.Length / 3, new Vector3(0f, -1f, 0f), 1);
            }
            if (m_Native == 0)
            {
                Debug.LogError("Cloth: NvCloth could not cook the mesh", this);
                return;
            }
            var t = Frame;
            ClothNative.uac_cloth_set_frame(m_Native, t.position, t.rotation);
            ClothNative.uac_cloth_clear_inertia(m_Native);
            m_Positions = (Vector3[])m_Targets.Clone();
            m_Normals = (Vector3[])m_TargetNormals.Clone();
            m_Simulated = false;
            m_SetupPending = false;
            m_Dirty = m_ConstraintsDirty = m_CollidersDirty = true;
            m_Version++;
        }

        internal void Release()
        {
            if (m_Native != 0) ClothNative.uac_cloth_destroy(m_Native);
            m_Native = 0;
        }

        void Rebuild()
        {
            if (m_Native == 0) return;
            Release();
            m_SetupPending = true;
        }

        /// <summary>Hands this frame's frame, settings, constraints and colliders to NvCloth.</summary>
        internal unsafe void BeforeStep()
        {
            if (m_Native == 0) return;
            var renderer = Renderer;
            if (renderer != null && renderer.sharedMesh != m_Source)
            {
                Setup();
                if (m_Native == 0) return;
            }
            var t = Frame;
            ClothNative.uac_cloth_set_frame(m_Native, t.position, t.rotation);
            if (renderer != null && renderer.IsBoneSkinned)
            {
                UpdateTargets();
                UpdateFixedParticles();
                m_ConstraintsDirty = true;
            }
            // The random acceleration changes every step.
            if (m_Dirty || m_RandomAcceleration != Vector3.zero) Configure();
            if (m_ConstraintsDirty) Constrain();
            UpdateColliders();
        }

        unsafe void UpdateFixedParticles()
        {
            var values = new float[4 * m_Targets.Length];
            int count;
            fixed (float* p = values) count = ClothNative.uac_cloth_particles(m_Native, p, m_Targets.Length);
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (values[4 * i + 3] != 0f) continue;
                values[4 * i] = m_Targets[i].x;
                values[4 * i + 1] = m_Targets[i].y;
                values[4 * i + 2] = m_Targets[i].z;
                any = true;
            }
            if (any)
                fixed (float* p = values) ClothNative.uac_cloth_set_particles(m_Native, p, count, 0);
        }

        unsafe void Configure()
        {
            m_Dirty = false;
            var gravity = m_UseGravity ? Physics.gravity : Vector3.zero;
            gravity += m_ExternalAcceleration;
            if (m_RandomAcceleration != Vector3.zero)
                gravity += Vector3.Scale(m_RandomAcceleration, new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)));
            float inertia = m_WorldAccelerationScale;
            var p = new ClothNative.Params
            {
                Gravity = gravity,
                Damping = Vector3.one * m_Damping,
                LinearDrag = Vector3.zero,
                AngularDrag = Vector3.zero,
                LinearInertia = Vector3.one * inertia,
                AngularInertia = Vector3.one * inertia,
                CentrifugalInertia = Vector3.one * inertia,
                SolverFrequency = Mathf.Max(1f, m_SolverFrequency),
                StiffnessFrequency = Mathf.Max(1f, m_StiffnessFrequency),
                StretchStiffness = Mathf.Clamp01(m_StretchingStiffness),
                ShearStiffness = 1f,
                BendStiffness = Mathf.Clamp01(m_BendingStiffness),
                TetherStiffness = m_UseTethers ? 1f : 0f,
                TetherScale = 1f,
                Friction = m_Friction,
                CollisionMassScale = m_CollisionMassScale,
                ContinuousCollision = m_UseContinuousCollision ? 1 : 0,
                SelfCollisionDistance = Mathf.Max(0f, m_SelfCollisionDistance),
                SelfCollisionStiffness = m_SelfCollisionStiffness,
                SleepThreshold = m_SleepThreshold,
                MotionConstraintStiffness = 1f,
            };
            ClothNative.uac_cloth_configure(m_Native, p);
        }

        /// <summary>Max distances (motion constraints around the skinned pose), collision sphere
        /// distances (separation spheres behind it, as large as the mesh's half diagonal: measured),
        /// virtual particles and self collision indices.</summary>
        unsafe void Constrain()
        {
            m_ConstraintsDirty = false;
            int count = m_Targets.Length;
            bool limited = false, separated = false;
            for (int i = 0; i < count; i++)
            {
                float d = m_Coefficients[i].maxDistance;
                if (d > 0f && d < float.MaxValue) limited = true;
                if (m_Coefficients[i].collisionSphereDistance < float.MaxValue) separated = true;
            }
            var values = new float[4 * count];
            if (limited)
            {
                for (int i = 0; i < count; i++)
                {
                    values[4 * i] = m_Targets[i].x;
                    values[4 * i + 1] = m_Targets[i].y;
                    values[4 * i + 2] = m_Targets[i].z;
                    values[4 * i + 3] = Mathf.Max(0f, m_Coefficients[i].maxDistance);
                }
            }
            fixed (float* p = values) ClothNative.uac_cloth_motion_constraints(m_Native, p, limited ? count : 0);
            if (separated)
            {
                float radius = m_Source != null ? Vector3.Scale(m_Source.bounds.extents, transform.lossyScale).magnitude : 1f;
                for (int i = 0; i < count; i++)
                {
                    float d = m_Coefficients[i].collisionSphereDistance;
                    var centre = d < float.MaxValue ? m_Targets[i] - m_TargetNormals[i] * (radius + d) : m_Targets[i];
                    values[4 * i] = centre.x;
                    values[4 * i + 1] = centre.y;
                    values[4 * i + 2] = centre.z;
                    values[4 * i + 3] = d < float.MaxValue ? radius : 0f;
                }
            }
            fixed (float* p = values) ClothNative.uac_cloth_separation_constraints(m_Native, p, separated ? count : 0);

            // Virtual particles: the user's, else one per triangle at its centre.
            var indices = new List<uint>();
            List<Vector3> weights;
            if (m_VirtualParticleIndices.Count > 0)
            {
                indices.AddRange(m_VirtualParticleIndices);
                weights = m_VirtualParticleWeights.Count > 0 ? m_VirtualParticleWeights : new List<Vector3>(s_DefaultWeights);
            }
            else
            {
                weights = new List<Vector3>(s_DefaultWeights);
                if (m_UseVirtualParticles > 0f)
                {
                    for (int k = 0; k + 2 < m_Triangles.Length; k += 3)
                    {
                        indices.Add(m_Triangles[k]);
                        indices.Add(m_Triangles[k + 1]);
                        indices.Add(m_Triangles[k + 2]);
                        indices.Add(0);
                    }
                }
            }
            var w = new float[3 * weights.Count];
            for (int i = 0; i < weights.Count; i++)
            {
                w[3 * i] = weights[i].x;
                w[3 * i + 1] = weights[i].y;
                w[3 * i + 2] = weights[i].z;
            }
            var ia = indices.ToArray();
            fixed (uint* pi = ia)
            fixed (float* pw = w)
                ClothNative.uac_cloth_virtual_particles(m_Native, pi, ia.Length / 4, pw, weights.Count);
            var self = m_SelfAndInterCollisionIndices.ToArray();
            fixed (uint* ps = self) ClothNative.uac_cloth_self_collision_indices(m_Native, ps, self.Length);
        }

        /// <summary>Capsule colliders, then sphere pairs (a cone between two spheres), in the
        /// cloth's frame; at most 32 spheres, as NvCloth.</summary>
        unsafe void UpdateColliders()
        {
            var t = Frame;
            var toFrame = Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            var spheres = new List<Vector4>();
            var capsules = new List<uint>();
            foreach (var capsule in m_CapsuleColliders)
            {
                if (capsule == null || !capsule.enabled || !capsule.gameObject.activeInHierarchy || spheres.Count + 2 > 32) continue;
                capsule.Segment(capsule.transform.localToWorldMatrix, out var a, out var b, out float r);
                capsules.Add((uint)spheres.Count);
                spheres.Add(Sphere(toFrame, a, r));
                capsules.Add((uint)spheres.Count);
                spheres.Add(Sphere(toFrame, b, r));
            }
            foreach (var pair in m_SphereColliders)
            {
                bool first = Usable(pair.first), second = Usable(pair.second);
                if (!first && !second) continue;
                if (first && second)
                {
                    if (spheres.Count + 2 > 32) continue;
                    capsules.Add((uint)spheres.Count);
                    spheres.Add(Sphere(toFrame, pair.first));
                    capsules.Add((uint)spheres.Count);
                    spheres.Add(Sphere(toFrame, pair.second));
                }
                else if (spheres.Count < 32)
                {
                    spheres.Add(Sphere(toFrame, first ? pair.first : pair.second));
                }
            }
            if (!m_CollidersDirty && spheres.Count == 0) return;
            m_CollidersDirty = spheres.Count > 0;
            var s = new float[4 * spheres.Count];
            for (int i = 0; i < spheres.Count; i++)
            {
                s[4 * i] = spheres[i].x;
                s[4 * i + 1] = spheres[i].y;
                s[4 * i + 2] = spheres[i].z;
                s[4 * i + 3] = spheres[i].w;
            }
            var c = capsules.ToArray();
            fixed (float* ps = s)
            fixed (uint* pc = c)
                ClothNative.uac_cloth_collision(m_Native, ps, spheres.Count, pc, c.Length / 2);
        }

        static bool Usable(SphereCollider c) => c != null && c.enabled && c.gameObject.activeInHierarchy;

        static Vector4 Sphere(Matrix4x4 toFrame, Vector3 world, float radius)
        {
            var p = toFrame.MultiplyPoint3x4(world);
            return new Vector4(p.x, p.y, p.z, radius);
        }

        static Vector4 Sphere(Matrix4x4 toFrame, SphereCollider c)
        {
            var m = c.transform.localToWorldMatrix;
            var s = Collider.AxisScales(m);
            float r = Mathf.Abs(c.radius) * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            return Sphere(toFrame, m.MultiplyPoint3x4(c.center), r);
        }

        /// <summary>Reads the particles back after a step.</summary>
        internal unsafe void AfterStep()
        {
            if (m_Native == 0) return;
            int count = m_Targets.Length;
            var values = new float[4 * count];
            fixed (float* p = values) count = ClothNative.uac_cloth_particles(m_Native, p, count);
            if (m_Positions.Length != count) m_Positions = new Vector3[count];
            for (int i = 0; i < count; i++) m_Positions[i] = new Vector3(values[4 * i], values[4 * i + 1], values[4 * i + 2]);
            if (m_Weight < 1f)
                for (int i = 0; i < count && i < m_Targets.Length; i++) m_Positions[i] = Vector3.Lerp(m_Targets[i], m_Positions[i], m_Weight);
            m_Normals = SmoothNormals(m_Positions, m_Triangles);
            m_Simulated = true;
            m_Version++;
        }

        /// <summary>Per particle the normalized sum of its triangles' unit normals (measured).</summary>
        static Vector3[] SmoothNormals(Vector3[] positions, uint[] triangles)
        {
            var normals = new Vector3[positions.Length];
            for (int k = 0; k + 2 < triangles.Length; k += 3)
            {
                int a = (int)triangles[k], b = (int)triangles[k + 1], c = (int)triangles[k + 2];
                var n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).normalized;
                normals[a] += n;
                normals[b] += n;
                normals[c] += n;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
            return normals;
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>The mesh the renderer draws while the cloth simulates: the source's vertices
        /// moved to their particles, in the cloth's frame (draw with <see cref="RenderMatrix"/>).</summary>
        internal Mesh RenderMesh()
        {
            if (m_Native == 0 || m_Source == null) return null;
            if (m_Rendered == null || m_RenderedVersion != m_Version)
            {
                if (m_Rendered == null)
                {
                    m_Rendered = (Mesh)((Instantiation.ICloneable)m_Source).CloneObject();
                    m_Rendered.ClearBlendShapes();
                    m_Rendered.name = m_Source.name + " (Cloth)";
                }
                var vertices = new Vector3[m_ParticleOf.Length];
                var normals = new Vector3[m_ParticleOf.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = m_Positions[m_ParticleOf[i]];
                    normals[i] = m_Normals[m_ParticleOf[i]];
                }
                m_Rendered.vertices = vertices;
                m_Rendered.normals = normals;
                m_Rendered.RecalculateBounds();
                m_RenderedVersion = m_Version;
            }
            return m_Rendered;
        }

        internal Matrix4x4 RenderMatrix
        {
            get
            {
                var t = Frame;
                return Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            }
        }

        // ------------------------------------------------------------------ serialization

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Enabled = body.Bool("m_Enabled", true);
            m_StretchingStiffness = body.Float("m_StretchingStiffness", 1f);
            m_BendingStiffness = body.Float("m_BendingStiffness", 0f);
            m_UseTethers = body.Bool("m_UseTethers", true);
            m_UseGravity = body.Bool("m_UseGravity", true);
            m_Damping = body.Float("m_Damping", 0f);
            m_ExternalAcceleration = body.Vector3("m_ExternalAcceleration");
            m_RandomAcceleration = body.Vector3("m_RandomAcceleration");
            m_WorldVelocityScale = body.Float("m_WorldVelocityScale", 0.5f);
            m_WorldAccelerationScale = body.Float("m_WorldAccelerationScale", 1f);
            m_Friction = body.Float("m_Friction", 0.5f);
            m_CollisionMassScale = body.Float("m_CollisionMassScale", 0f);
            m_UseContinuousCollision = body.Bool("m_UseContinuousCollision", true);
            m_UseVirtualParticles = body.Float("m_UseVirtualParticles", 1f);
            m_SolverFrequency = body.Float("m_SolverFrequency", 120f);
            m_StiffnessFrequency = body.Float("m_StiffnessFrequency", 10f);
            m_SleepThreshold = body.Float("m_SleepThreshold", 0.1f);
            m_SelfCollisionDistance = body.Float("m_SelfCollisionDistance", 0f);
            m_SelfCollisionStiffness = body.Float("m_SelfCollisionStiffness", 0.2f);
            var coefficients = new List<ClothSkinningCoefficient>();
            foreach (var item in body.Get("m_Coefficients")?.Seq() ?? Array.Empty<YNode>())
                coefficients.Add(new ClothSkinningCoefficient { maxDistance = item.Float("maxDistance", float.MaxValue), collisionSphereDistance = item.Float("collisionSphereDistance", float.MaxValue) });
            m_Coefficients = coefficients.Count > 0 ? coefficients.ToArray() : null;
            var capsules = new List<CapsuleCollider>();
            foreach (var item in body.Get("m_CapsuleColliders")?.Seq() ?? Array.Empty<YNode>())
            {
                var r = item.AsRef();
                capsules.Add(r.IsNull ? null : resolver.Resolve(r, typeof(CapsuleCollider)) as CapsuleCollider);
            }
            m_CapsuleColliders = capsules.ToArray();
            var pairs = new List<ClothSphereColliderPair>();
            foreach (var item in body.Get("m_SphereColliders")?.Seq() ?? Array.Empty<YNode>())
            {
                var a = item.Ref("first");
                var b = item.Ref("second");
                pairs.Add(new ClothSphereColliderPair(
                    a.IsNull ? null : resolver.Resolve(a, typeof(SphereCollider)) as SphereCollider,
                    b.IsNull ? null : resolver.Resolve(b, typeof(SphereCollider)) as SphereCollider));
            }
            m_SphereColliders = pairs.ToArray();
            ReadIndices(body, "m_SelfAndInterCollisionIndices", m_SelfAndInterCollisionIndices);
            ReadIndices(body, "m_VirtualParticleIndices", m_VirtualParticleIndices);
            m_VirtualParticleWeights.Clear();
            foreach (var item in body.Get("m_VirtualParticleWeights")?.Seq() ?? Array.Empty<YNode>())
                m_VirtualParticleWeights.Add(new Vector3(item.Float("x"), item.Float("y"), item.Float("z")));
        }

        /// <summary>An index list: Unity writes it as hex (4 little-endian bytes per index).</summary>
        static void ReadIndices(YNode body, string key, List<uint> into)
        {
            into.Clear();
            var node = body.Get(key);
            if (node == null) return;
            if (node.Kind == YKind.Scalar)
            {
                var hex = node.AsString().Trim();
                if (hex.Length < 8) return;
                var bytes = Convert.FromHexString(hex.Substring(0, hex.Length / 8 * 8));
                for (int i = 0; i + 3 < bytes.Length; i += 4) into.Add(BitConverter.ToUInt32(bytes, i));
                return;
            }
            foreach (var item in node.Seq()) into.Add(unchecked((uint)item.AsLong(0)));
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Cloth)source;
            m_Enabled = s.m_Enabled;
            m_StretchingStiffness = s.m_StretchingStiffness;
            m_BendingStiffness = s.m_BendingStiffness;
            m_UseTethers = s.m_UseTethers;
            m_UseGravity = s.m_UseGravity;
            m_Damping = s.m_Damping;
            m_ExternalAcceleration = s.m_ExternalAcceleration;
            m_RandomAcceleration = s.m_RandomAcceleration;
            m_WorldVelocityScale = s.m_WorldVelocityScale;
            m_WorldAccelerationScale = s.m_WorldAccelerationScale;
            m_Friction = s.m_Friction;
            m_CollisionMassScale = s.m_CollisionMassScale;
            m_UseContinuousCollision = s.m_UseContinuousCollision;
            m_UseVirtualParticles = s.m_UseVirtualParticles;
            m_SolverFrequency = s.m_SolverFrequency;
            m_StiffnessFrequency = s.m_StiffnessFrequency;
            m_SleepThreshold = s.m_SleepThreshold;
            m_SelfCollisionDistance = s.m_SelfCollisionDistance;
            m_SelfCollisionStiffness = s.m_SelfCollisionStiffness;
            m_Coefficients = s.m_Coefficients != null ? (ClothSkinningCoefficient[])s.m_Coefficients.Clone() : null;
            m_CapsuleColliders = Array.ConvertAll(s.m_CapsuleColliders, c => remap(c) as CapsuleCollider);
            m_SphereColliders = Array.ConvertAll(s.m_SphereColliders, p => new ClothSphereColliderPair(remap(p.first) as SphereCollider, remap(p.second) as SphereCollider));
            m_SelfAndInterCollisionIndices.Clear();
            m_SelfAndInterCollisionIndices.AddRange(s.m_SelfAndInterCollisionIndices);
            m_VirtualParticleIndices.Clear();
            m_VirtualParticleIndices.AddRange(s.m_VirtualParticleIndices);
            m_VirtualParticleWeights.Clear();
            m_VirtualParticleWeights.AddRange(s.m_VirtualParticleWeights);
        }
    }
}
