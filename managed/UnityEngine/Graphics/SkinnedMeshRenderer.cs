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

        Mesh m_Skinned;
        int m_SkinnedFrame = -1;
        Mesh m_SkinnedSource;
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
            && m_Mesh.m_Bindposes != null && m_Bones.Length > 0;

        int MaxBones => m_Quality switch
        {
            SkinQuality.Bone1 => 1,
            SkinQuality.Bone2 => 2,
            _ => 4,
        };

        /// <summary>The deformed mesh, computed once a frame (or now): the shapes added in the
        /// mesh's space, then skinned into world space when the mesh has bones.</summary>
        Mesh Deform(bool now = false)
        {
            int frame = Time.frameCount;
            if (!now && m_Skinned != null && m_SkinnedFrame == frame && m_SkinnedSource == m_Mesh) return m_Skinned;
            m_SkinnedFrame = now ? -1 : frame;
            var mesh = m_Mesh;
            if (m_Skinned == null || m_SkinnedSource != mesh)
            {
                // The deformed copy shares everything but positions, normals and tangents with its source.
                if (m_Skinned != null) DestroyImmediate(m_Skinned);
                m_Skinned = (Mesh)((Instantiation.ICloneable)mesh).CloneObject();
                m_Skinned.ClearBlendShapes();
                m_Skinned.name = mesh.name + " (Skinned)";
                m_SkinnedSource = mesh;
            }
            var positions = (Vector3[])mesh.m_Vertices.Clone();
            var normals = mesh.m_Normals != null && mesh.m_Normals.Length == positions.Length ? (Vector3[])mesh.m_Normals.Clone() : null;
            var tangents = mesh.m_Tangents != null && mesh.m_Tangents.Length == positions.Length ? (Vector4[])mesh.m_Tangents.Clone() : null;
            FitWeights();
            mesh.AddBlendShapes(m_BlendShapeWeights, positions, normals, tangents);
            if (IsSkinned) Skin(positions, normals, tangents);
            m_Skinned.vertices = positions;
            if (normals != null) m_Skinned.normals = normals;
            if (tangents != null) m_Skinned.tangents = tangents;
            m_Skinned.RecalculateBounds();
            return m_Skinned;
        }

        /// <summary>The vertices, normals and tangents (in the mesh's space) into world space by the
        /// bones (normals and tangents keep the matrices' scale, as Unity's do).</summary>
        void Skin(Vector3[] positions, Vector3[] normals, Vector4[] tangents)
        {
            var mesh = m_Mesh;
            var weights = mesh.m_BoneWeights;
            var bindposes = mesh.m_Bindposes;
            int boneCount = Math.Min(m_Bones.Length, bindposes.Length);
            if (m_Matrices.Length != boneCount) m_Matrices = new Matrix4x4[boneCount];
            var fallback = transform.localToWorldMatrix;
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

        internal override Bounds GetLocalBounds() => m_Mesh != null ? m_Mesh.bounds : default;

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (m_Mesh == null) return;
            if (!IsSkinned)
            {
                FrameBuilder.AddMeshDraws(this, HasShapes ? Deform() : m_Mesh, transform.localToWorldMatrix, Color.white, camera, into);
                return;
            }
            FrameBuilder.AddMeshDraws(this, Deform(), Matrix4x4.identity, Color.white, camera, into);
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
            var vertices = source.vertices;
            var fromSource = skinned ? toLocal : (useScale ? Matrix4x4.identity : Matrix4x4.Scale(t.lossyScale));
            for (int i = 0; i < vertices.Length; i++) vertices[i] = fromSource.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            // Normals and tangents as the matrices take them, not renormalized (as Unity's).
            var normals = source.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++) normals[i] = fromSource.MultiplyVector(normals[i]);
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
            m_Skinned = null;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
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
            m_BlendShapeWeights.Clear();
            foreach (var item in body.Get("m_BlendShapeWeights")?.Seq() ?? Array.Empty<YNode>()) m_BlendShapeWeights.Add(item.AsFloat());
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SkinnedMeshRenderer)source;
            CopyRendererData(s);
            m_Mesh = s.m_Mesh;
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
