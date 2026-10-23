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
    /// A mesh deformed by bones: linear blend skinning on the CPU, once a frame however many cameras
    /// draw it. Each vertex moves by its bones' current world matrices times their bind poses,
    /// weighted (up to the quality's bone count); the result is drawn in world space. A mesh without
    /// bone weights draws like a MeshRenderer's.
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
        Vector3[] m_Positions = Array.Empty<Vector3>();
        Vector3[] m_Normals = Array.Empty<Vector3>();
        Matrix4x4[] m_Matrices = Array.Empty<Matrix4x4>();

        public SkinnedMeshRenderer() { }

        public Mesh sharedMesh
        {
            get => m_Mesh;
            set => m_Mesh = value;
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

        /// <summary>Kept for scripts that read it back; blend shapes are not imported yet, so it
        /// moves no vertices.</summary>
        public void SetBlendShapeWeight(int index, float value)
        {
            if (index < 0) return;
            while (m_BlendShapeWeights.Count <= index) m_BlendShapeWeights.Add(0f);
            m_BlendShapeWeights[index] = value;
        }

        bool IsSkinned => m_Mesh != null && m_Mesh.m_BoneWeights != null && m_Mesh.m_BoneWeights.Length == m_Mesh.vertexCount
            && m_Mesh.m_Bindposes != null && m_Bones.Length > 0;

        int MaxBones => m_Quality switch
        {
            SkinQuality.Bone1 => 1,
            SkinQuality.Bone2 => 2,
            _ => 4,
        };

        /// <summary>The skinned vertices in world space, computed once a frame.</summary>
        Mesh Skin()
        {
            int frame = Time.frameCount;
            if (m_Skinned != null && m_SkinnedFrame == frame && m_SkinnedSource == m_Mesh) return m_Skinned;
            m_SkinnedFrame = frame;
            var mesh = m_Mesh;
            if (m_Skinned == null || m_SkinnedSource != mesh)
            {
                // The deformed copy shares everything but positions and normals with its source.
                m_Skinned = (Mesh)((Instantiation.ICloneable)mesh).CloneObject();
                m_Skinned.name = mesh.name + " (Skinned)";
                m_SkinnedSource = mesh;
            }
            var source = mesh.m_Vertices;
            var sourceNormals = mesh.normals;
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
            if (m_Positions.Length != source.Length) m_Positions = new Vector3[source.Length];
            bool hasNormals = sourceNormals != null && sourceNormals.Length == source.Length;
            if (hasNormals && m_Normals.Length != source.Length) m_Normals = new Vector3[source.Length];
            int maxBones = MaxBones;
            for (int i = 0; i < source.Length; i++)
            {
                var w = weights[i];
                var v = source[i];
                Vector3 p = default, n = default;
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
                    if (hasNormals) n += m.MultiplyVector(sourceNormals[i]) * weight;
                    total += weight;
                }
                if (total <= 0f)
                {
                    p = fallback.MultiplyPoint3x4(v);
                    if (hasNormals) n = fallback.MultiplyVector(sourceNormals[i]);
                }
                else if (total < 0.999f)
                {
                    // Fewer bones than the vertex has (quality): renormalize what is left.
                    p /= total;
                    n /= total;
                }
                m_Positions[i] = p;
                if (hasNormals) m_Normals[i] = n.normalized;
            }
            m_Skinned.vertices = m_Positions;
            if (hasNormals) m_Skinned.normals = m_Normals;
            m_Skinned.RecalculateBounds();
            return m_Skinned;
        }

        internal override Bounds GetLocalBounds() => m_Mesh != null ? m_Mesh.bounds : default;

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (m_Mesh == null) return;
            if (!IsSkinned)
            {
                FrameBuilder.AddMeshDraws(this, m_Mesh, transform.localToWorldMatrix, Color.white, camera, into);
                return;
            }
            FrameBuilder.AddMeshDraws(this, Skin(), Matrix4x4.identity, Color.white, camera, into);
        }

        public void BakeMesh(Mesh mesh) => BakeMesh(mesh, false);

        /// <summary>The current pose in the renderer's space (with its scale only when asked).</summary>
        public void BakeMesh(Mesh mesh, bool useScale)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh), "The mesh to bake into is null");
            if (m_Mesh == null) return;
            var t = transform;
            var toLocal = useScale ? t.worldToLocalMatrix : Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            var posed = IsSkinned ? Skin() : null;
            mesh.Clear();
            var source = posed ?? m_Mesh;
            var vertices = source.vertices;
            var fromSource = posed != null ? toLocal : (useScale ? Matrix4x4.identity : Matrix4x4.Scale(t.lossyScale));
            for (int i = 0; i < vertices.Length; i++) vertices[i] = fromSource.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            var normals = source.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++) normals[i] = fromSource.MultiplyVector(normals[i]).normalized;
                mesh.normals = normals;
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
