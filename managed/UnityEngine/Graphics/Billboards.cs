using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A billboard's shape (vertices in 0..1 over its width and height), the images of the
    /// views it was rendered from, its size and material.</summary>
    [ClassId(226)]
    public sealed partial class BillboardAsset : Object, ISerializedAsset
    {
        float m_Width = 1f, m_Height = 1f, m_Bottom;
        Vector4[] m_ImageTexCoords = Array.Empty<Vector4>();
        Vector2[] m_Vertices = Array.Empty<Vector2>();
        ushort[] m_Indices = Array.Empty<ushort>();
        Material m_Material;
        internal int m_Version;

        public BillboardAsset() { }

        public float width { get => m_Width; set { m_Width = value; m_Version++; } }
        public float height { get => m_Height; set { m_Height = value; m_Version++; } }
        public float bottom { get => m_Bottom; set { m_Bottom = value; m_Version++; } }
        public int imageCount => m_ImageTexCoords.Length;
        public int vertexCount => m_Vertices.Length;
        public int indexCount => m_Indices.Length;
        public Material material { get => m_Material; set => m_Material = value; }

        public Vector4[] GetImageTexCoords() => (Vector4[])m_ImageTexCoords.Clone();
        public void GetImageTexCoords(List<Vector4> imageTexCoords) { imageTexCoords.Clear(); imageTexCoords.AddRange(m_ImageTexCoords); }
        public void SetImageTexCoords(Vector4[] imageTexCoords) { m_ImageTexCoords = (Vector4[])imageTexCoords.Clone(); m_Version++; }
        public void SetImageTexCoords(List<Vector4> imageTexCoords) { m_ImageTexCoords = imageTexCoords.ToArray(); m_Version++; }
        public Vector2[] GetVertices() => (Vector2[])m_Vertices.Clone();
        public void GetVertices(List<Vector2> vertices) { vertices.Clear(); vertices.AddRange(m_Vertices); }
        public void SetVertices(Vector2[] vertices) { m_Vertices = (Vector2[])vertices.Clone(); m_Version++; }
        public void SetVertices(List<Vector2> vertices) { m_Vertices = vertices.ToArray(); m_Version++; }
        public ushort[] GetIndices() => (ushort[])m_Indices.Clone();
        public void GetIndices(List<ushort> indices) { indices.Clear(); indices.AddRange(m_Indices); }
        public void SetIndices(ushort[] indices) { m_Indices = (ushort[])indices.Clone(); m_Version++; }
        public void SetIndices(List<ushort> indices) { m_Indices = indices.ToArray(); m_Version++; }

        internal Vector4[] ImageTexCoords => m_ImageTexCoords;
        internal Vector2[] Vertices => m_Vertices;
        internal ushort[] Indices => m_Indices;

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Width = body.Float("width", 1f);
            m_Height = body.Float("height", 1f);
            m_Bottom = body.Float("bottom");
            var coords = new List<Vector4>();
            foreach (var c in body.Get("imageTexCoords")?.Seq() ?? Array.Empty<YNode>())
                coords.Add(new Vector4(c.Float("x"), c.Float("y"), c.Float("z"), c.Float("w")));
            m_ImageTexCoords = coords.ToArray();
            var vertices = new List<Vector2>();
            foreach (var v in body.Get("vertices")?.Seq() ?? Array.Empty<YNode>())
                vertices.Add(new Vector2(v.Float("x"), v.Float("y")));
            m_Vertices = vertices.ToArray();
            // Indices: a hex string of little-endian 16-bit values.
            var hex = body.Str("indices");
            var bytes = string.IsNullOrEmpty(hex) ? Array.Empty<byte>() : Convert.FromHexString(hex);
            m_Indices = new ushort[bytes.Length / 2];
            for (int i = 0; i < m_Indices.Length; i++) m_Indices[i] = (ushort)(bytes[2 * i] | bytes[2 * i + 1] << 8);
            var material = body.Ref("material");
            m_Material = material.IsNull ? null : resolver.Resolve(material, typeof(Material)) as Material;
            m_Version++;
        }
    }

    /// <summary>Draws a BillboardAsset at the object's position, turned to the camera by Unity's
    /// billboard shaders (SpeedTreeBillboardCommon.cginc reads the per-camera and per-billboard
    /// variables set here).</summary>
    [ClassId(227)]
    public sealed partial class BillboardRenderer : Renderer
    {
        BillboardAsset m_Billboard;
        Mesh m_Mesh;
        (BillboardAsset, int, Vector3) m_MeshKey;

        public BillboardRenderer() { }

        /// <summary>The asset; its material becomes the renderer's when it has one.</summary>
        public BillboardAsset billboard
        {
            get => m_Billboard;
            set
            {
                m_Billboard = value;
                if (value != null && value.material != null) sharedMaterial = value.material;
            }
        }

        /// <summary>The billboard's box at the object's position, unrotated: width by height by
        /// width from its bottom (measured: tests/projects/builtinfx billboard.bounds).</summary>
        internal override Bounds GetLocalBounds()
        {
            if (m_Billboard == null) return default;
            var s = transform.lossyScale;
            return new Bounds(new Vector3(0f, (m_Billboard.bottom + m_Billboard.height * 0.5f) * s.y, 0f),
                new Vector3(m_Billboard.width * s.x, m_Billboard.height * s.y, m_Billboard.width * s.x));
        }

        internal override Matrix4x4 BoundsMatrix => Matrix4x4.Translate(transform.position);

        static readonly int s_Info = Shader.PropertyToID("unity_BillboardInfo");
        static readonly int s_Size = Shader.PropertyToID("unity_BillboardSize");
        static readonly int s_ImageTexCoords = Shader.PropertyToID("unity_BillboardImageTexCoords");
        static readonly int s_Normal = Shader.PropertyToID("unity_BillboardNormal");
        static readonly int s_Tangent = Shader.PropertyToID("unity_BillboardTangent");
        static readonly int s_CameraParams = Shader.PropertyToID("unity_BillboardCameraParams");

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var asset = m_Billboard;
            if (asset == null || asset.vertexCount == 0 || asset.indexCount == 0) return;
            var scale = transform.lossyScale;
            var key = (asset, asset.m_Version, scale);
            if (m_Mesh == null || m_MeshKey != key)
            {
                if (m_Mesh == null)
                    using (ObjectRegistry.AsAssets())
                        m_Mesh = new Mesh { name = "Billboard", hideFlags = HideFlags.HideAndDontSave };
                m_Mesh.Clear();
                var source = asset.Vertices;
                var vertices = new Vector3[source.Length];
                var uv0 = new Vector2[source.Length];
                var uv1 = new List<Vector4>();
                var normals = new Vector3[source.Length];
                var tangents = new Vector4[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    uv0[i] = source[i];
                    // Width and height scales, rotation, wind weight.
                    uv1.Add(new Vector4(scale.x, scale.y, 0f, 0f));
                    normals[i] = Vector3.forward;
                    tangents[i] = new Vector4(1f, 0f, 0f, -1f);
                }
                m_Mesh.vertices = vertices;
                m_Mesh.uv = uv0;
                m_Mesh.SetUVs(1, uv1);
                m_Mesh.normals = normals;
                m_Mesh.tangents = tangents;
                var indices = asset.Indices;
                var triangles = new int[indices.Length];
                for (int i = 0; i < indices.Length; i++) triangles[i] = indices[i];
                m_Mesh.SetTriangles(triangles, 0, false);
                m_Mesh.bounds = GetLocalBounds();
                m_MeshKey = key;
            }
            int from = into.Count;
            FrameBuilder.AddMeshDraws(this, m_Mesh, Matrix4x4.Translate(transform.position), Color.white, camera, into);
            // The view the billboard turns to: from the camera's forward in the XZ plane.
            var eye = -camera.Forward;
            var tangent = new Vector3(-eye.z, 0f, eye.x).normalized;
            var normal = new Vector3(tangent.z, 0f, -tangent.x);
            float angle = Mathf.Atan2(normal.z, normal.x);
            if (angle < 0f) angle += 2f * Mathf.PI;
            var coords = new Vector4[16];
            Array.Copy(asset.ImageTexCoords, coords, Math.Min(16, asset.imageCount));
            for (int i = from; i < into.Count; i++)
            {
                var item = into[i];
                var properties = item.Properties != null ? new Dictionary<int, object>(item.Properties) : new Dictionary<int, object>();
                properties[s_Info] = new Vector4(asset.imageCount, asset.imageCount / (2f * Mathf.PI), 0f, 0f);
                properties[s_Size] = new Vector4(asset.width, asset.height, asset.bottom, 0f);
                properties[s_ImageTexCoords] = coords;
                properties[s_Normal] = (Vector4)normal;
                properties[s_Tangent] = (Vector4)tangent;
                properties[s_CameraParams] = new Vector4(camera.Position.x, camera.Position.y, camera.Position.z, angle);
                item.Properties = properties;
                into[i] = item;
            }
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            CopyRendererData((Renderer)source);
            m_Billboard = remap(((BillboardRenderer)source).m_Billboard) as BillboardAsset;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            var billboard = body.Ref("m_Billboard");
            m_Billboard = billboard.IsNull ? null : resolver.Resolve(billboard, typeof(BillboardAsset)) as BillboardAsset;
        }
    }

    /// <summary>Static batching, as far as it shows: the renderers under a root become part of a
    /// static batch and stay where they were, relative to the root (which may still move them;
    /// measured: tests/projects/builtinfx static_batching_*). Unbound keeps drawing each renderer's
    /// own mesh instead of combining them.</summary>
    public sealed partial class StaticBatchingUtility
    {
        public StaticBatchingUtility() { }

        public static void Combine(GameObject staticBatchRoot)
        {
            if (staticBatchRoot == null) return;
            Combine(Array.ConvertAll(staticBatchRoot.GetComponentsInChildren<MeshFilter>(), f => f.gameObject), staticBatchRoot);
        }

        /// <summary>Combines the enabled mesh renderers whose meshes have the same vertex
        /// channels, in groups of two or more (a renderer alone with its vertex layout is left as
        /// it is, measured: a plane next to spheres is not combined, two planes are).</summary>
        public static void Combine(GameObject[] gos, GameObject staticBatchRoot)
        {
            if (gos == null) return;
            var root = staticBatchRoot != null ? staticBatchRoot.transform : null;
            var toRoot = root != null ? root.worldToLocalMatrix : Matrix4x4.identity;
            var groups = new Dictionary<int, List<MeshRenderer>>();
            foreach (var go in gos)
            {
                if (go == null) continue;
                var renderer = go.GetComponent<MeshRenderer>();
                var filter = go.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (renderer == null || !renderer.enabled || mesh == null || mesh.vertexCount == 0 || renderer.m_StaticBatched) continue;
                int key = Channels(mesh);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<MeshRenderer>();
                if (!list.Contains(renderer)) list.Add(renderer);
            }
            foreach (var list in groups.Values)
            {
                if (list.Count < 2) continue;
                foreach (var renderer in list)
                {
                    renderer.m_StaticBatchLocal = toRoot * renderer.transform.localToWorldMatrix;
                    renderer.m_StaticBatchRoot = root;
                    renderer.m_StaticBatched = true;
                }
            }
        }

        /// <summary>A mesh's vertex channels as bits.</summary>
        static int Channels(Mesh mesh)
        {
            int bits = 0;
            if (mesh.m_Normals != null && mesh.m_Normals.Length > 0) bits |= 1;
            if (mesh.m_Tangents != null && mesh.m_Tangents.Length > 0) bits |= 2;
            if (mesh.m_Colors != null && mesh.m_Colors.Length > 0) bits |= 4;
            for (int i = 0; i < 8; i++)
                if (mesh.m_UVs[i] != null && mesh.m_UVs[i].Length > 0) bits |= (8 << i) | (mesh.m_UVDimensions[i] << (16 + 2 * i));
            return bits;
        }
    }
}
