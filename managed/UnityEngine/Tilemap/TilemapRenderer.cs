using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Tilemaps
{
    /// <summary>
    /// Draws a tilemap's sprites with the renderer's material: each cell's sprite mesh at the cell
    /// center (the tile anchor), under the tilemap's orientation and the cell's transform, colored
    /// by the cell's and the tilemap's colors. Chunk mode draws the whole tilemap as one sorted
    /// unit (a mesh per texture), its cells ordered by the sort order; Individual mode sorts each
    /// cell on its own at its center.
    /// </summary>
    [ClassId(483693784)]
    [RequireComponent(typeof(Tilemap))]
    public sealed partial class TilemapRenderer : Renderer
    {
        Vector3Int m_ChunkSize = new Vector3Int(32, 32, 32);
        Vector3 m_ChunkCullingBounds;
        int m_MaxChunkCount = 16;
        int m_MaxFrameAge = 16;
        SortOrder m_SortOrder;
        Mode m_Mode;
        DetectChunkCullingBounds m_DetectChunkCullingBounds;
        SpriteMaskInteraction m_MaskInteraction;

        /// <summary>The meshes drawn, rebuilt when the tilemap, its grid or the settings change.</summary>
        readonly List<(Mesh Mesh, Texture Texture, Vector3 Center)> m_Batches = new List<(Mesh, Texture, Vector3)>();
        (int Tilemap, int Grid, SortOrder Order, Mode Mode) m_BuiltFor = (-1, -1, default, default);

        public TilemapRenderer() { }

        public Vector3Int chunkSize { get => m_ChunkSize; set => m_ChunkSize = value; }
        public Vector3 chunkCullingBounds { get => m_ChunkCullingBounds; set => m_ChunkCullingBounds = value; }
        public int maxChunkCount { get => m_MaxChunkCount; set => m_MaxChunkCount = value; }
        public int maxFrameAge { get => m_MaxFrameAge; set => m_MaxFrameAge = value; }
        public SortOrder sortOrder { get => m_SortOrder; set => m_SortOrder = value; }
        public Mode mode { get => m_Mode; set => m_Mode = value; }
        public DetectChunkCullingBounds detectChunkCullingBounds { get => m_DetectChunkCullingBounds; set => m_DetectChunkCullingBounds = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        internal override SpriteMaskInteraction MaskInteraction => m_MaskInteraction;

        /// <summary>A renderer added by a script starts with the Sprites-Default material.</summary>
        internal override void OnAttached()
        {
            base.OnAttached();
            var material = BuiltinObjects.SpritesDefault;
            if (material != null) m_Materials = new[] { material };
        }

        internal override Bounds GetLocalBounds()
        {
            var tilemap = m_GameObject.GetComponent<Tilemap>();
            return tilemap != null ? tilemap.localBounds : default;
        }

        /// <summary>The cells in the order the sort order draws them (z first, then rows, then
        /// columns).</summary>
        List<KeyValuePair<Vector3Int, Tilemap.Cell>> OrderedCells(Tilemap tilemap)
        {
            var cells = new List<KeyValuePair<Vector3Int, Tilemap.Cell>>(tilemap.m_Cells);
            bool top = m_SortOrder == SortOrder.TopLeft || m_SortOrder == SortOrder.TopRight;
            bool right = m_SortOrder == SortOrder.BottomRight || m_SortOrder == SortOrder.TopRight;
            cells.Sort((a, b) =>
            {
                int c = a.Key.z.CompareTo(b.Key.z);
                if (c != 0) return c;
                c = a.Key.y.CompareTo(b.Key.y);
                if (c != 0) return top ? -c : c;
                c = a.Key.x.CompareTo(b.Key.x);
                return right ? -c : c;
            });
            return cells;
        }

        void Rebuild(Tilemap tilemap)
        {
            foreach (var batch in m_Batches) if (batch.Mesh != null) DestroyImmediate(batch.Mesh);
            m_Batches.Clear();
            var tint = tilemap.color;
            var orientation = tilemap.orientationMatrix;
            var groups = new List<(Texture Texture, List<Vector3> Vertices, List<Vector2> Uvs, List<Color> Colors, List<int> Triangles, Vector3 Center)>();
            foreach (var pair in OrderedCells(tilemap))
            {
                var cell = pair.Value;
                var sprite = Tilemap.ShownSprite(cell);
                if (sprite == null || sprite.texture == null) continue;
                var center = tilemap.TileCenterLocal(pair.Key);
                var model = Matrix4x4.Translate(center) * orientation * cell.Transform;
                var color = cell.Color * tint;
                int g = groups.Count - 1;
                // Chunk mode batches consecutive cells of one texture; Individual mode draws each
                // cell on its own.
                if (m_Mode == Mode.Individual || g < 0 || groups[g].Texture != sprite.texture)
                {
                    groups.Add((sprite.texture, new List<Vector3>(), new List<Vector2>(), new List<Color>(), new List<int>(), center));
                    g++;
                }
                var group = groups[g];
                int start = group.Vertices.Count;
                var vertices = sprite.m_Vertices;
                var uvs = sprite.m_UV;
                for (int i = 0; i < vertices.Length; i++)
                {
                    group.Vertices.Add(model.MultiplyPoint3x4(vertices[i]));
                    group.Uvs.Add(uvs[i]);
                    group.Colors.Add(color);
                }
                foreach (var t in sprite.m_Triangles) group.Triangles.Add(start + t);
            }
            foreach (var group in groups)
            {
                var mesh = new Mesh { name = "Tilemap", hideFlags = HideFlags.HideAndDontSave };
                if (group.Vertices.Count > 65535) mesh.indexFormat = Rendering.IndexFormat.UInt32;
                mesh.SetVertices(group.Vertices);
                mesh.SetUVs(0, group.Uvs);
                mesh.SetColors(group.Colors);
                mesh.SetTriangles(group.Triangles, 0);
                m_Batches.Add((mesh, group.Texture, group.Center));
            }
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var tilemap = m_GameObject.GetComponent<Tilemap>();
            if (tilemap == null) return;
            var grid = tilemap.layoutGrid;
            var key = (tilemap.m_RenderVersion, grid != null ? grid.m_LayoutVersion : 0, m_SortOrder, m_Mode);
            if (!key.Equals(m_BuiltFor))
            {
                Rebuild(tilemap);
                m_BuiltFor = key;
            }
            if (m_Batches.Count == 0 || m_Materials.Length == 0) return;
            var model = transform.localToWorldMatrix;
            float chunkDistance = FrameBuilder.Distance(camera, bounds.center);
            var groups = FrameBuilder.GroupsOf(this, camera);
            int layer = SortingLayer.GetLayerValueFromID(m_SortingLayerID);
            foreach (var batch in m_Batches)
            {
                ulong mesh = batch.Mesh.Sync();
                ulong texture = TextureHandles.Of(batch.Texture);
                float distance = m_Mode == Mode.Individual ? FrameBuilder.Distance(camera, model.MultiplyPoint3x4(batch.Center)) : chunkDistance;
                for (int i = 0; i < m_Materials.Length; i++)
                {
                    var material = DrawnMaterial(i, m_Materials[i]);
                    if (material == null) continue;
                    into.Add(new FrameBuilder.DrawItem
                    {
                        Mesh = mesh,
                        Submesh = 0,
                        Material = material,
                        Model = model,
                        Color = Color.white,
                        MainTexture = texture,
                        Queue = material.renderQueue,
                        SortingLayer = layer,
                        SortingOrder = m_SortingOrder,
                        Distance = distance,
                        Sequence = FrameBuilder.NextSequence(),
                        Masked = this,
                        Groups = groups,
                    });
                }
            }
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            foreach (var batch in m_Batches) if (batch.Mesh != null) DestroyImmediate(batch.Mesh);
            m_Batches.Clear();
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            var chunk = body.Get("m_ChunkSize");
            if (chunk != null) m_ChunkSize = new Vector3Int(chunk.Int("x"), chunk.Int("y"), chunk.Int("z"));
            m_ChunkCullingBounds = body.Vector3("m_ChunkCullingBounds");
            m_MaxChunkCount = body.Int("m_MaxChunkCount", 16);
            m_MaxFrameAge = body.Int("m_MaxFrameAge", 16);
            m_SortOrder = (SortOrder)body.Int("m_SortOrder");
            m_Mode = (Mode)body.Int("m_Mode");
            m_DetectChunkCullingBounds = (DetectChunkCullingBounds)body.Int("m_DetectChunkCullingBounds");
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (TilemapRenderer)source;
            CopyRendererData(s);
            m_ChunkSize = s.m_ChunkSize;
            m_ChunkCullingBounds = s.m_ChunkCullingBounds;
            m_MaxChunkCount = s.m_MaxChunkCount;
            m_MaxFrameAge = s.m_MaxFrameAge;
            m_SortOrder = s.m_SortOrder;
            m_Mode = s.m_Mode;
            m_DetectChunkCullingBounds = s.m_DetectChunkCullingBounds;
            m_MaskInteraction = s.m_MaskInteraction;
        }

        public enum SortOrder
        {
            BottomLeft = 0,
            BottomRight = 1,
            TopLeft = 2,
            TopRight = 3,
        }

        public enum Mode
        {
            Chunk = 0,
            Individual = 1,
            SRPBatch = 2,
        }

        public enum DetectChunkCullingBounds
        {
            Auto = 0,
            Manual = 1,
        }
    }
}
