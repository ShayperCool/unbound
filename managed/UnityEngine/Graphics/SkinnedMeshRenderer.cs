using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public enum SkinQuality
    {
        Auto = 0,
        Bone1 = 1,
        Bone2 = 2,
        Bone4 = 4,
    }

    /// <summary>
    /// A mesh deformed by blend shapes and bones: the shapes' deltas at the renderer's weights, then
    /// linear blend skinning, on the CPU once a frame however many cameras draw it. Each vertex
    /// moves by its bones' current world matrices times their bind poses, weighted (up to the
    /// quality's bone count); the result is drawn in world space. A mesh without bone weights draws
    /// like a MeshRenderer's (with its shapes).
    /// </summary>
    [ClassId(137)]
    public partial class SkinnedMeshRenderer : Renderer
    {
        internal Mesh m_Mesh;
        internal Transform[] m_Bones = Array.Empty<Transform>();
        internal Transform m_RootBone;
        internal SkinQuality m_Quality;
        internal bool m_UpdateWhenOffscreen;
        internal bool m_SkinnedMotionVectors = true;
        internal readonly List<float> m_BlendShapeWeights = new List<float>();

        Bounds m_LocalBounds;
        bool m_HasLocalBounds, m_ExactLocalBounds, m_AuthoredLocalBounds;
        /// <summary>Read from a scene, prefab or model (or cloned from one), not created at runtime.</summary>
        bool m_Deserialized;
        Bounds? m_CullingBounds;
        Mesh m_RigidSource;
        Vector3[] m_RigidVertices;
        BoneWeight[] m_RigidWeights;
        Matrix4x4[] m_RigidBindposes;
        Bounds m_RigidBoneBounds;
        bool m_Rigid;

        internal Mesh m_Skinned;
        /// <summary>The skinning pass <see cref="m_Skinned"/> holds (<see cref="SkinPass"/>; -1:
        /// none, or to be redone).</summary>
        int m_SkinnedEpoch = -1;
        Mesh m_SkinnedSource;
        /// <summary>The skinning before <see cref="m_Skinned"/>'s while its buffers alternate
        /// (<see cref="GetPreviousVertexBuffer"/>), with its own arrays; valid in
        /// <see cref="m_PreviousEpoch"/>'s pass only.</summary>
        internal Mesh m_SkinnedPrevious;
        Vector3[] m_PreviousPositions, m_PreviousNormals;
        Vector4[] m_PreviousTangents;
        int m_PreviousEpoch = -1;
        /// <summary>The skinning pass in which a camera drew this renderer's motion vectors.</summary>
        internal int m_MotionEpoch = -1;
        /// <summary>The skinning pass the renderer was created in: none has skinned it yet.</summary>
        readonly int m_BornEpoch = SkinPass;

        /// <summary>Unity skins once a frame, at PostLateUpdate.UpdateAllSkinnedMeshes: each pass
        /// there starts a skinning pass, and a renderer skins once per pass, when it is first drawn or
        /// its buffer is first asked for after it. Draws before it in a frame (a camera rendered from
        /// a script) and <see cref="GetVertexBuffer"/> read the last pass's skin, as the Unity
        /// 6000.0.71f1 Editor's do (tests/projects/renderapi <c>followup.skin.*</c>,
        /// <c>prevskin.*</c>). A pass is named by its frame: before the frame's
        /// UpdateAllSkinnedMeshes the last frame's pass goes on.</summary>
        internal static int SkinPass => s_SkinPassFrame == Time.s_FrameCount ? Time.s_FrameCount : Time.s_FrameCount - 1;

        /// <summary>The frame whose UpdateAllSkinnedMeshes ran last.</summary>
        static int s_SkinPassFrame = -1;

        static SkinnedMeshRenderer() =>
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllSkinnedMeshes), SkinningPass);

        /// <summary>The renderers whose buffers were asked for: they skin at the pass itself
        /// (others when they are drawn, which comes after it).</summary>
        static readonly List<SkinnedMeshRenderer> s_BufferUsers = new List<SkinnedMeshRenderer>();

        static void SkinningPass()
        {
            s_SkinPassFrame = Time.s_FrameCount;
            for (int i = 0; i < s_BufferUsers.Count; i++)
            {
                var r = s_BufferUsers[i];
                if (r.Skinning) r.Deform();
            }
        }

        /// <summary>The pass skins enabled renderers that are visible or update offscreen.</summary>
        bool Skinning => enabled && m_GameObject != null && m_GameObject.ActiveInHierarchyNoThrow && (m_UpdateWhenOffscreen || isVisible);
        Matrix4x4[] m_Matrices = Array.Empty<Matrix4x4>();

        public SkinnedMeshRenderer() { }

        public Mesh sharedMesh
        {
            get => m_Mesh;
            set
            {
                m_Mesh = value;
                FitWeights();
            }
        }

        /// <summary>One weight per blend shape of the mesh (kept where they were).</summary>
        void FitWeights()
        {
            int count = m_Mesh != null ? m_Mesh.blendShapeCount : 0;
            if (m_BlendShapeWeights.Count > count) m_BlendShapeWeights.RemoveRange(count, m_BlendShapeWeights.Count - count);
            while (m_BlendShapeWeights.Count < count) m_BlendShapeWeights.Add(0f);
        }

        public Transform[] bones
        {
            get => (Transform[])m_Bones.Clone();
            set => m_Bones = value != null ? (Transform[])value.Clone() : Array.Empty<Transform>();
        }

        public Transform rootBone
        {
            get => m_RootBone;
            set => m_RootBone = value;
        }

        public SkinQuality quality
        {
            get => m_Quality;
            set => m_Quality = value;
        }

        public bool updateWhenOffscreen
        {
            get => m_UpdateWhenOffscreen;
            set => m_UpdateWhenOffscreen = value;
        }

        public bool forceMatrixRecalculationPerRender { get; set; }

        public bool skinnedMotionVectors
        {
            get => m_SkinnedMotionVectors;
            set => m_SkinnedMotionVectors = value;
        }

        bool m_GraphicsOutputRequested;
        GraphicsBuffer.Target m_VertexBufferTarget = GraphicsBuffer.Target.Vertex;
        public GraphicsBuffer.Target vertexBufferTarget
        {
            get => m_VertexBufferTarget;
            set
            {
                m_VertexBufferTarget = value | GraphicsBuffer.Target.Vertex;
                if (m_Skinned != null) m_Skinned.vertexBufferTarget = m_VertexBufferTarget;
            }
        }

        /// <summary>The skin of the pass before the current one while the renderer's buffers
        /// alternate: with <see cref="skinnedMotionVectors"/> and a camera that drew its motion
        /// vectors in the last pass. Otherwise the current buffer itself (Unity 6000.0.71f1 Editor,
        /// tests/projects/renderapi <c>prevskin.*</c>; the Scripting API: "skinned meshes that have
        /// skinnedMotionVectors on alternate between two GPU vertex buffers").</summary>
        public GraphicsBuffer GetPreviousVertexBuffer() => SkinBuffer(true);

        public GraphicsBuffer GetVertexBuffer() => SkinBuffer(false);

        /// <summary>The skin output buffer: null until a skinning pass has run since the renderer
        /// was created; a simulating cloth's is its particles' mesh, in the cloth's frame, and has no
        /// previous one of its own (measured: tests/projects/renderapi <c>prevskin.cloth.*</c>; the
        /// particles' positions and normals are the cloth's <c>vertices</c> and <c>normals</c>).</summary>
        GraphicsBuffer SkinBuffer(bool previous)
        {
            if (m_Mesh == null) return null;
            Mesh output = null;
            var cloth = GetComponent<Cloth>();
            // Before its first simulation step a cloth draws, and reads, the skin it targets.
            if (cloth != null && cloth.enabled && cloth.m_Active) output = cloth.RenderMesh();
            if (output == null)
            {
                if (m_SkinnedEpoch < 0 && SkinPass == m_BornEpoch) return null;
                if (!m_GraphicsOutputRequested)
                {
                    m_GraphicsOutputRequested = true;
                    m_SkinnedEpoch = -1;
                    s_BufferUsers.Add(this);
                }
                // A renderer the pass does not skin keeps its last skin (measured: prevskin.*.disabled).
                output = Skinning || m_Skinned == null || m_SkinnedEpoch < 0 || m_SkinnedSource != m_Mesh ? Deform() : m_Skinned;
                if (previous && m_SkinnedPrevious != null && m_PreviousEpoch == m_SkinnedEpoch) output = m_SkinnedPrevious;
            }
            output.vertexBufferTarget = m_VertexBufferTarget;
            var buffer = output.GetVertexBuffer(0);
            output.Sync();
            return buffer;
        }

        public float GetBlendShapeWeight(int index) => index >= 0 && index < m_BlendShapeWeights.Count ? m_BlendShapeWeights[index] : 0f;

        public void SetBlendShapeWeight(int index, float value)
        {
            FitWeights();
            if (index < 0 || index >= m_BlendShapeWeights.Count)
            {
                Debug.LogError($"Array index ({index}) is out of bounds (size={m_BlendShapeWeights.Count})", this);
                return;
            }
            m_BlendShapeWeights[index] = value;
        }

        bool HasShapes => m_Mesh != null && m_Mesh.blendShapeCount > 0 && m_BlendShapeWeights.Exists(w => w != 0f);

        bool IsSkinned => m_Mesh != null && m_Mesh.m_BoneWeights != null && m_Mesh.m_BoneWeights.Length == m_Mesh.vertexCount
            && m_Mesh.m_Bindposes != null && (m_Bones.Length > 0 || SkeletonSkin() != null);

        // Skinning from an Animator's skeleton (a renderer without bones below an Animator without
        // its transform hierarchy): the mesh's bone paths as the skeleton's nodes.
        Animator m_SkinAnimator;
        AnimatorSkeleton m_SkinSkeleton;
        Mesh m_SkinMesh;
        int[] m_SkinNodes = Array.Empty<int>();
        int m_SkinRoot;
        int m_SkinMissFrame = -1;

        /// <summary>The skeleton that skins this renderer: the nearest Animator's above it (itself
        /// included) when it has no transform hierarchy and the mesh names its bones; else null.</summary>
        AnimatorSkeleton SkeletonSkin()
        {
            var mesh = m_Mesh;
            if (m_Bones.Length != 0 || mesh == null || mesh.m_BoneNameHashes == null || mesh.m_Bindposes == null
                || mesh.m_BoneNameHashes.Length != mesh.m_Bindposes.Length) return null;
            if (m_SkinAnimator == null || !transform.IsChildOf(m_SkinAnimator.transform))
            {
                // Without an Animator above, looked for again once a frame (a renderer moved under
                // an optimized Animator skins from it from the next frame).
                if (m_SkinMissFrame == Time.s_FrameCount) return null;
                m_SkinAnimator = null;
                for (var t = transform; t != null && m_SkinAnimator == null; t = t.m_Parent)
                    m_SkinAnimator = t.m_GameObject.GetComponent<Animator>();
                if (m_SkinAnimator == null)
                {
                    m_SkinMissFrame = Time.s_FrameCount;
                    return null;
                }
            }
            var skeleton = m_SkinAnimator != null ? m_SkinAnimator.Skeleton : null;
            if (skeleton == null) return null;
            if (skeleton != m_SkinSkeleton || mesh != m_SkinMesh)
            {
                // The root: the mesh's root bone, else this renderer's own path below the Animator.
                var root = m_SkinAnimator.transform;
                m_SkinRoot = mesh.m_RootBoneNameHash != 0 ? skeleton.NodeOfPath(mesh.m_RootBoneNameHash)
                    : transform == root ? 0 : skeleton.NodeOfPath(HashedBindings.Crc32(HierarchyOptimizer.PathBelow(transform, root)));
                var hashes = mesh.m_BoneNameHashes;
                if (m_SkinNodes.Length != hashes.Length) m_SkinNodes = new int[hashes.Length];
                for (int b = 0; b < hashes.Length; b++) m_SkinNodes[b] = skeleton.NodeOfPath(hashes[b]);
                m_SkinSkeleton = skeleton;
                m_SkinMesh = mesh;
            }
            return skeleton;
        }

        /// <summary>The bones a vertex takes: the renderer's quality, Auto following the quality
        /// level's skin weights (measured: tests/projects/builtinfx skin_*).</summary>
        int MaxBones => m_Quality switch
        {
            SkinQuality.Bone1 => 1,
            SkinQuality.Bone2 => 2,
            SkinQuality.Bone4 => 4,
            _ => QualitySettings.skinWeights switch
            {
                SkinWeights.OneBone => 1,
                SkinWeights.TwoBones => 2,
                _ => 4,
            },
        };

        /// <summary>The deformed mesh, computed once a skinning pass (<see cref="SkinPass"/>; or
        /// now): the shapes added in the mesh's space, then skinned into world space when the mesh
        /// has bones (the renderer's space once its buffer is asked for).</summary>
        Mesh Deform(bool now = false)
        {
            if (now)
            {
                // Skinned into a mesh of its own: the pass's skin and its buffers stay as they are.
                SwapNowSkin();
                try { return Skin(); }
                finally { SwapNowSkin(); }
            }
            int pass = SkinPass;
            if (m_Skinned != null && m_SkinnedEpoch == pass && m_SkinnedSource == m_Mesh) return m_Skinned;
            if (m_Skinned != null && m_SkinnedSource != m_Mesh) DropPreviousSkin();
            // The buffers alternate: the last pass's skin becomes the previous one.
            if (m_Skinned != null && m_SkinnedSource == m_Mesh && m_GraphicsOutputRequested && m_SkinnedMotionVectors
                && m_SkinnedEpoch >= 0 && m_MotionEpoch == m_SkinnedEpoch)
                AlternateSkin();
            m_SkinnedEpoch = pass;
            return Skin();
        }

        /// <summary>A skin computed now (<see cref="BakeMesh(Mesh, bool)"/>, cloth targets) and its
        /// arrays, apart from the pass's.</summary>
        Mesh m_SkinnedNow, m_NowSource;
        Vector3[] m_NowPositions, m_NowNormals;
        Vector4[] m_NowTangents;

        void SwapNowSkin()
        {
            (m_Skinned, m_SkinnedNow) = (m_SkinnedNow, m_Skinned);
            (m_SkinnedSource, m_NowSource) = (m_NowSource, m_SkinnedSource);
            (m_DeformedPositions, m_NowPositions) = (m_NowPositions, m_DeformedPositions);
            (m_DeformedNormals, m_NowNormals) = (m_NowNormals, m_DeformedNormals);
            (m_DeformedTangents, m_NowTangents) = (m_NowTangents, m_DeformedTangents);
        }

        /// <summary>Deforms the mesh into <see cref="m_Skinned"/> (made for its source the first
        /// time) and its arrays.</summary>
        Mesh Skin()
        {
            var mesh = m_Mesh;
            if (m_Skinned == null || m_SkinnedSource != mesh)
            {
                // The deformed copy shares everything but positions, normals and tangents with its source.
                if (m_Skinned != null) DestroyImmediate(m_Skinned);
                m_Skinned = (Mesh)((Instantiation.ICloneable)mesh).CloneObject();
                m_Skinned.ClearBlendShapes();
                m_Skinned.m_BoneWeights = null;
                m_Skinned.m_Bindposes = null;
                m_Skinned.SetSkinOutputLayout();
                m_Skinned.vertexBufferTarget = m_VertexBufferTarget;
                m_Skinned.name = mesh.name + " (Skinned)";
                m_SkinnedSource = mesh;
            }
            // The deformed copy keeps its arrays from frame to frame: refilled here, then given
            // back to it, so a frame's skinning allocates nothing.
            var positions = Refill(ref m_DeformedPositions, mesh.m_Vertices);
            var normals = mesh.m_Normals != null && mesh.m_Normals.Length == positions.Length ? Refill(ref m_DeformedNormals, mesh.m_Normals) : null;
            var tangents = mesh.m_Tangents != null && mesh.m_Tangents.Length == positions.Length ? Refill(ref m_DeformedTangents, mesh.m_Tangents) : null;
            FitWeights();
            mesh.AddBlendShapes(m_BlendShapeWeights, positions, normals, tangents);
            if (IsSkinned)
            {
                Skin(positions, normals, tangents);
                if (m_GraphicsOutputRequested)
                {
                    var inverse = transform.worldToLocalMatrix;
                    var normalToLocal = transform.localToWorldMatrix.transpose;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        positions[i] = inverse.MultiplyPoint3x4(positions[i]);
                        if (normals != null) normals[i] = normalToLocal.MultiplyVector(normals[i]);
                        if (tangents != null)
                        {
                            var t = inverse.MultiplyVector((Vector3)tangents[i]);
                            tangents[i] = new Vector4(t.x,t.y,t.z,tangents[i].w);
                        }
                    }
                }
            }
            m_Skinned.ShareDrawIndices(mesh);
            m_Skinned.AdoptVertices(positions);
            if (normals != null) m_Skinned.AdoptNormals(normals);
            if (tangents != null) m_Skinned.AdoptTangents(tangents);
            m_Skinned.RecalculateBounds();
            return m_Skinned;
        }

        /// <summary>The current skin becomes the previous one, with its arrays; the other mesh (a
        /// copy of the current one the first time) takes the next skin.</summary>
        void AlternateSkin()
        {
            if (m_SkinnedPrevious == null)
            {
                m_SkinnedPrevious = (Mesh)((Instantiation.ICloneable)m_Skinned).CloneObject();
                m_SkinnedPrevious.name = m_Skinned.name;
            }
            (m_Skinned, m_SkinnedPrevious) = (m_SkinnedPrevious, m_Skinned);
            (m_DeformedPositions, m_PreviousPositions) = (m_PreviousPositions, m_DeformedPositions);
            (m_DeformedNormals, m_PreviousNormals) = (m_PreviousNormals, m_DeformedNormals);
            (m_DeformedTangents, m_PreviousTangents) = (m_PreviousTangents, m_DeformedTangents);
            m_PreviousEpoch = SkinPass;
        }

        void DropPreviousSkin()
        {
            if (m_SkinnedPrevious != null) DestroyImmediate(m_SkinnedPrevious);
            m_SkinnedPrevious = null;
            m_PreviousPositions = m_PreviousNormals = null;
            m_PreviousTangents = null;
            m_PreviousEpoch = -1;
        }

        Vector3[] m_DeformedPositions, m_DeformedNormals;
        Vector4[] m_DeformedTangents;

        /// <summary>A copy of <paramref name="source"/> in <paramref name="buffer"/>, made anew
        /// only when the length changes.</summary>
        static T[] Refill<T>(ref T[] buffer, T[] source)
        {
            if (buffer == null || buffer.Length != source.Length) buffer = new T[source.Length];
            Array.Copy(source, buffer, source.Length);
            return buffer;
        }

        /// <summary>The vertices, normals and tangents (in the mesh's space) into world space by the
        /// bones (normals and tangents keep the matrices' scale, as Unity's do).</summary>
        void Skin(Vector3[] positions, Vector3[] normals, Vector4[] tangents)
        {
            var mesh = m_Mesh;
            var weights = mesh.m_BoneWeights;
            var bindposes = mesh.m_Bindposes;
            var skeleton = m_Bones.Length == 0 ? SkeletonSkin() : null;
            int boneCount = Math.Min(skeleton != null ? m_SkinNodes.Length : m_Bones.Length, bindposes.Length);
            if (m_Matrices.Length != boneCount) m_Matrices = new Matrix4x4[boneCount];
            var fallback = transform.localToWorldMatrix;
            if (skeleton != null)
            {
                // Relative to the skin's root node, placed by this renderer's transform (which the
                // Animator keeps at that node's pose when it is exposed).
                var toWorld = m_SkinRoot > 0 ? fallback * skeleton.Global(m_SkinRoot).inverse : fallback;
                for (int b = 0; b < boneCount; b++)
                {
                    int node = m_SkinNodes[b];
                    m_Matrices[b] = (node >= 0 ? toWorld * skeleton.Global(node) : fallback) * bindposes[b];
                }
            }
            else
                for (int b = 0; b < boneCount; b++)
                {
                    var bone = m_Bones[b];
                    m_Matrices[b] = (bone != null ? bone.localToWorldMatrix : fallback) * bindposes[b];
                }
            int maxBones = MaxBones;
            for (int i = 0; i < positions.Length; i++)
            {
                var w = weights[i];
                var v = positions[i];
                var sn = normals != null ? normals[i] : default;
                var st = tangents != null ? (Vector3)tangents[i] : default;
                Vector3 p = default, n = default, t = default;
                float total = 0f;
                for (int k = 0; k < maxBones; k++)
                {
                    float weight;
                    int index;
                    switch (k)
                    {
                        case 0: weight = w.weight0; index = w.boneIndex0; break;
                        case 1: weight = w.weight1; index = w.boneIndex1; break;
                        case 2: weight = w.weight2; index = w.boneIndex2; break;
                        default: weight = w.weight3; index = w.boneIndex3; break;
                    }
                    if (weight <= 0f || index < 0 || index >= boneCount) continue;
                    var m = m_Matrices[index];
                    p += m.MultiplyPoint3x4(v) * weight;
                    n += m.MultiplyVector(sn) * weight;
                    t += m.MultiplyVector(st) * weight;
                    total += weight;
                }
                if (total <= 0f)
                {
                    p = fallback.MultiplyPoint3x4(v);
                    n = fallback.MultiplyVector(sn);
                    t = fallback.MultiplyVector(st);
                }
                else if (total < 0.999f)
                {
                    // Fewer bones than the vertex has (quality): renormalize what is left.
                    p /= total;
                    n /= total;
                    t /= total;
                }
                positions[i] = p;
                if (normals != null) normals[i] = n;
                if (tangents != null) tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
            }
        }

        /// <summary>The object matrix of a bone-skinned mesh, which its object motion follows: its
        /// actual root bone's (rootBone, else the renderer's own transform) position and rotation,
        /// without scale (the skin's scale is in its pose). Moving only the root bone moves the
        /// skin; moving the renderer apart from its bones does not (Unity 6000.0.71f1,
        /// tests/projects/urp motion_blur_skinned_root, motion_blur_skinned_renderer,
        /// motion_blur_skinned_root_scale).</summary>
        internal override Matrix4x4 MotionMatrix
        {
            get
            {
                if (!IsSkinned) return base.MotionMatrix;
                var root = m_RootBone != null ? m_RootBone : transform;
                root.GetPositionAndRotation(out var position, out var rotation);
                return Matrix4x4.TRS(position, rotation, Vector3.one);
            }
        }

        // The authored local bound belongs to rootBone, falling back to the renderer transform.
        // It is independent of the drawing matrix (bone-skinned geometry is already in world space).
        Matrix4x4 RootMatrix => (m_RootBone != null ? m_RootBone : transform).localToWorldMatrix;

        /// <summary>Only the rigid single-bone dynamic bound is measured here. This computes a
        /// bone-space envelope once from the authored vertices/bind pose, never from a sampled
        /// deformed mesh. Multi-bone deformation, blend shapes and cloth need separate measurements.</summary>
        bool RigidBounds(out Bounds local)
        {
            local = default;
            var mesh = m_Mesh;
            if (mesh == null || mesh.m_UnmeasuredSkinBoundsMutation) return false;
            if (m_RigidSource != mesh || m_RigidVertices != mesh.m_Vertices ||
                m_RigidWeights != mesh.m_BoneWeights || m_RigidBindposes != mesh.m_Bindposes)
            {
                m_RigidSource = mesh;
                m_RigidVertices = mesh.m_Vertices;
                m_RigidWeights = mesh.m_BoneWeights;
                m_RigidBindposes = mesh.m_Bindposes;
                m_Rigid = mesh.vertexCount > 0 && m_RigidWeights?.Length == mesh.vertexCount &&
                    m_RigidBindposes?.Length == 1 && mesh.blendShapeCount == 0;
                if (m_Rigid)
                {
                    var bind = m_RigidBindposes[0];
                    var lo = Vector3.positiveInfinity;
                    var hi = Vector3.negativeInfinity;
                    for (int i = 0; i < m_RigidVertices.Length; i++)
                    {
                        var w = m_RigidWeights[i];
                        if (w.boneIndex0 != 0 || w.weight0 != 1f || w.weight1 != 0f || w.weight2 != 0f || w.weight3 != 0f)
                        { m_Rigid = false; break; }
                        var v = bind.MultiplyPoint3x4(m_RigidVertices[i]);
                        lo = Vector3.Min(lo, v);
                        hi = Vector3.Max(hi, v);
                    }
                    m_RigidBoneBounds.SetMinMax(lo, hi);
                }
            }
            if (!m_Rigid || mesh.blendShapeCount != 0 || m_Bones.Length != 1 || m_Bones[0] == null || GetComponent<Cloth>() != null) return false;
            local = TransformBounds(m_RigidBoneBounds, RootMatrix.inverse * m_Bones[0].localToWorldMatrix);
            return true;
        }

        void EnsureLocalBounds()
        {
            if (m_HasLocalBounds || m_Mesh == null) return;
            m_ExactLocalBounds = RigidBounds(out m_LocalBounds);
            if (!m_ExactLocalBounds) m_LocalBounds = m_Mesh.bounds;
            m_HasLocalBounds = true;
        }

        internal bool HasMeasuredCullingBounds
        {
            get
            {
                if (!Compat.SkinnedBoundsMeasured) return false;
                EnsureLocalBounds();
                return m_UpdateWhenOffscreen ? RigidBounds(out _) :
                    m_AuthoredLocalBounds || m_ExactLocalBounds && RigidBounds(out _);
            }
        }

        internal void ReportBoundsLimitation()
        {
            if (HasMeasuredCullingBounds || m_Mesh == null) return;
            Stubs.Ignored("SkinnedMeshRenderer bounds outside measured support",
                "root-space culling bounds are measured for Unity 6000.0.71f1 with authored local bounds, " +
                "or a rigid single-bone mesh. Offscreen bounds updates for multi-bone, blend-shape and cloth meshes " +
                "and automatic bounds after raw vertex/weight buffer edits or for other runtime meshes remain unsupported; " +
                "provide localBounds and disable updateWhenOffscreen");
        }

        internal override Bounds GetLocalBounds()
        {
            if (!Compat.SkinnedBoundsMeasured) return m_Mesh != null ? m_Mesh.bounds : default;
            EnsureLocalBounds();
            if (m_UpdateWhenOffscreen && RigidBounds(out var dynamicBounds)) return dynamicBounds;
            ReportBoundsLimitation();
            return m_LocalBounds;
        }

        internal override void SetLocalBounds(Bounds value)
        {
            if (!Compat.SkinnedBoundsMeasured) { base.SetLocalBounds(value); return; }
            m_LocalBounds = value;
            m_HasLocalBounds = m_ExactLocalBounds = m_AuthoredLocalBounds = true;
            // Unlike transform/root changes, a local-bounds write invalidates cached world bounds.
            m_CullingBounds = null;
        }

        internal override void ResetLocalBoundsCore()
        {
            // Measured: the skinned local bound survives Renderer.ResetLocalBounds.
            if (!Compat.SkinnedBoundsMeasured) base.ResetLocalBoundsCore();
        }

        internal override Bounds GetWorldBounds()
        {
            if (!Compat.SkinnedBoundsMeasured) return base.GetWorldBounds();
            if (!HasMeasuredCullingBounds)
            {
                // A renderer created at runtime keeps its first-queried local bound in root space
                // even when that bound is only the mesh's (multi-bone) or outlived its mesh
                // (skinnedbounds static/swap). A loaded renderer without an authored bound has
                // only its mesh-space bounds, so it keeps the renderer-space fallback.
                return m_HasLocalBounds && !m_Deserialized ? TransformBounds(localBounds, RootMatrix) : base.GetWorldBounds();
            }
            if (!m_UpdateWhenOffscreen && m_Enabled && gameObject.activeInHierarchy)
            {
                // The first query (including after a local-bounds write) captures this box until
                // culling refreshes it, even if animation changes the root later in the frame.
                if (m_CullingBounds is { } cached) return cached;
                var world = TransformBounds(localBounds, RootMatrix);
                m_CullingBounds = world;
                return world;
            }
            return TransformBounds(localBounds, RootMatrix);
        }

        internal override void PrepareCullingBounds()
        {
            if (!Compat.SkinnedBoundsMeasured) return;
            ReportBoundsLimitation();
            if (!HasMeasuredCullingBounds) { m_CullingBounds = null; return; }
            m_CullingBounds = TransformBounds(localBounds, RootMatrix);
        }

        /// <summary>The mesh has bones to skin it by.</summary>
        internal bool IsBoneSkinned => IsSkinned;

        /// <summary>The skinned pose in world space, when the mesh has bones.</summary>
        internal bool SkinnedPose(out Vector3[] positions, out Vector3[] normals)
        {
            positions = null;
            normals = null;
            if (!IsSkinned) return false;
            var deformed = Deform(now: true);
            positions = deformed.vertices;
            var n = deformed.normals;
            normals = n != null && n.Length == positions.Length ? n : null;
            if (m_GraphicsOutputRequested)
            {
                var world = transform.localToWorldMatrix;
                var normalToWorld = transform.worldToLocalMatrix.transpose;
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = world.MultiplyPoint3x4(positions[i]);
                    if (normals != null) normals[i] = normalToWorld.MultiplyVector(normals[i]);
                }
            }
            return true;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var mesh = DrawGeometry(out var model);
            if (mesh != null) FrameBuilder.AddMeshDraws(this, mesh, model, Color.white, camera, into);
        }

        internal override Mesh DrawGeometry(out Matrix4x4 model)
        {
            model = transform.localToWorldMatrix;
            if (m_Mesh == null) return null;
            // A simulating cloth draws its particles instead.
            var cloth = GetComponent<Cloth>();
            if (cloth != null && cloth.m_Active && cloth.enabled)
            {
                var clothMesh = cloth.RenderMesh();
                if (clothMesh != null)
                {
                    model = cloth.RenderMatrix;
                    return clothMesh;
                }
            }
            if (!IsSkinned)
            {
                return HasShapes || m_GraphicsOutputRequested ? Deform() : m_Mesh;
            }
            model = m_GraphicsOutputRequested ? transform.localToWorldMatrix : Matrix4x4.identity;
            return Deform();
        }

        public void BakeMesh(Mesh mesh) => BakeMesh(mesh, false);

        /// <summary>The current pose in the renderer's space (with its scale only when asked).</summary>
        public void BakeMesh(Mesh mesh, bool useScale)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh), "The mesh to bake into is null");
            if (m_Mesh == null) return;
            var t = transform;
            var toLocal = useScale ? t.worldToLocalMatrix : Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            bool skinned = IsSkinned;
            var source = skinned || HasShapes ? Deform(now: true) : m_Mesh;
            mesh.Clear();
            var vertices = (Vector3[])source.m_Vertices.Clone();
            var fromSource = skinned ? (m_GraphicsOutputRequested ? toLocal * t.localToWorldMatrix : toLocal) : (useScale ? Matrix4x4.identity : Matrix4x4.Scale(t.lossyScale));
            for (int i = 0; i < vertices.Length; i++) vertices[i] = fromSource.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            // Normals and tangents as the matrices take them, not renormalized (as Unity's).
            var normals = source.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                var normalFromSource = skinned && m_GraphicsOutputRequested ? toLocal * t.worldToLocalMatrix.transpose : fromSource;
                for (int i = 0; i < normals.Length; i++) normals[i] = normalFromSource.MultiplyVector(normals[i]);
                mesh.normals = normals;
            }
            var tangents = source.tangents;
            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    var d = fromSource.MultiplyVector(tangents[i]);
                    tangents[i] = new Vector4(d.x, d.y, d.z, tangents[i].w);
                }
                mesh.tangents = tangents;
            }
            mesh.uv = m_Mesh.uv;
            mesh.colors = m_Mesh.colors;
            mesh.subMeshCount = m_Mesh.subMeshCount;
            for (int s = 0; s < m_Mesh.subMeshCount; s++) mesh.SetTriangles(m_Mesh.GetTriangles(s), s);
            mesh.RecalculateBounds();
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            if (m_Skinned != null) DestroyImmediate(m_Skinned);
            if (m_SkinnedNow != null) DestroyImmediate(m_SkinnedNow);
            m_Skinned = m_SkinnedNow = null;
            DropPreviousSkin();
            s_BufferUsers.Remove(this);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            m_Deserialized = true;
            var mesh = body.Ref("m_Mesh");
            m_Mesh = mesh.IsNull ? null : resolver.Resolve(mesh, typeof(Mesh)) as Mesh;
            var bones = new List<Transform>();
            foreach (var item in body.Get("m_Bones")?.Seq() ?? Array.Empty<YNode>())
            {
                var r = item.AsRef();
                bones.Add(r.IsNull ? null : resolver.Resolve(r, typeof(Transform)) as Transform);
            }
            m_Bones = bones.ToArray();
            var root = body.Ref("m_RootBone");
            m_RootBone = root.IsNull ? null : resolver.Resolve(root, typeof(Transform)) as Transform;
            m_Quality = (SkinQuality)body.Int("m_Quality");
            m_UpdateWhenOffscreen = body.Bool("m_UpdateWhenOffscreen");
            m_SkinnedMotionVectors = body.Bool("m_SkinnedMotionVectors", true);
            var aabb = body.Get("m_AABB");
            if (aabb != null && Compat.SkinnedBoundsMeasured)
            {
                m_LocalBounds = new Bounds(aabb.Vector3("m_Center"), aabb.Vector3("m_Extent") * 2f);
                m_HasLocalBounds = m_ExactLocalBounds = m_AuthoredLocalBounds = true;
            }
            m_BlendShapeWeights.Clear();
            foreach (var item in body.Get("m_BlendShapeWeights")?.Seq() ?? Array.Empty<YNode>()) m_BlendShapeWeights.Add(item.AsFloat());
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SkinnedMeshRenderer)source;
            CopyRendererData(s);
            m_Mesh = s.m_Mesh;
            m_LocalBounds = s.m_LocalBounds;
            m_HasLocalBounds = s.m_HasLocalBounds;
            m_ExactLocalBounds = s.m_ExactLocalBounds;
            m_AuthoredLocalBounds = s.m_AuthoredLocalBounds;
            m_Deserialized = s.m_Deserialized;
            m_CullingBounds = null;
            m_Bones = new Transform[s.m_Bones.Length];
            for (int i = 0; i < m_Bones.Length; i++) m_Bones[i] = remap(s.m_Bones[i]) as Transform;
            m_RootBone = remap(s.m_RootBone) as Transform;
            m_Quality = s.m_Quality;
            m_UpdateWhenOffscreen = s.m_UpdateWhenOffscreen;
            m_SkinnedMotionVectors = s.m_SkinnedMotionVectors;
            m_BlendShapeWeights.Clear();
            m_BlendShapeWeights.AddRange(s.m_BlendShapeWeights);
        }
    }
}
