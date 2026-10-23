using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A region of a texture drawn as a quad (or a tight mesh), with a pivot and a scale
    /// (pixels per unit).</summary>
    [ClassId(213)]
    public sealed partial class Sprite : Object
    {
        internal Texture2D m_Texture;
        internal Rect m_Rect;
        internal Vector2 m_Pivot;          // pixels from the rect's bottom-left
        internal float m_PixelsPerUnit = 100f;
        internal Vector4 m_Border;         // left, bottom, right, top (pixels)
        internal SpriteMeshType m_MeshType = SpriteMeshType.Tight;
        internal uint m_Extrude;
        internal Vector2[] m_Vertices;
        internal Vector2[] m_UV;
        internal ushort[] m_Triangles;
        /// <summary>Pixel outlines of a tight mesh, relative to the rect centre.</summary>
        internal Vector2[][] m_Outline;
        internal Vector2[][] m_PhysicsShape;
        internal Mesh m_Mesh;

        Sprite() { }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, [Internal.DefaultValue("100.0f")] float pixelsPerUnit,
            [Internal.DefaultValue("0")] uint extrude, [Internal.DefaultValue("SpriteMeshType.Tight")] SpriteMeshType meshType,
            [Internal.DefaultValue("Vector4.zero")] Vector4 border, [Internal.DefaultValue("false")] bool generateFallbackPhysicsShape)
        {
            if (texture == null) return null;
            if (rect.xMax > texture.width + 0.001f || rect.yMax > texture.height + 0.001f || rect.x < 0 || rect.y < 0)
            {
                Debug.LogError($"Could not create sprite ({rect.x}, {rect.y}, {rect.width}, {rect.height}) from a {texture.width}x{texture.height} texture.");
                return null;
            }
            if (pixelsPerUnit <= 0) pixelsPerUnit = 100f;
            var sprite = new Sprite
            {
                m_Texture = texture,
                m_Rect = rect,
                m_Pivot = new Vector2(pivot.x * rect.width, pivot.y * rect.height),
                m_PixelsPerUnit = pixelsPerUnit,
                m_Border = border,
                m_MeshType = meshType,
                m_Extrude = extrude,
                m_Name = texture.name,
            };
            sprite.BuildGeometry();
            return sprite;
        }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType, Vector4 border) =>
            Create(texture, rect, pivot, pixelsPerUnit, extrude, meshType, border, false);

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType) =>
            Create(texture, rect, pivot, pixelsPerUnit, extrude, meshType, Vector4.zero, false);

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude) =>
            Create(texture, rect, pivot, pixelsPerUnit, extrude, SpriteMeshType.Tight, Vector4.zero, false);

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) =>
            Create(texture, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.Tight, Vector4.zero, false);

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) =>
            Create(texture, rect, pivot, 100f, 0, SpriteMeshType.Tight, Vector4.zero, false);

        /// <summary>A sprite of an imported texture, from its import definition.</summary>
        internal static Sprite FromImport(Texture2D texture, string name, Rect rect, Vector2 normalizedPivot, Vector4 border, float pixelsPerUnit, int meshType, Vector2[][] outline, Vector2[][] physicsShape)
        {
            Sprite sprite;
            using (ObjectRegistry.AsAssets())
                sprite = new Sprite { IsPersistentAsset = true };
            sprite.m_Texture = texture;
            sprite.m_Name = name;
            sprite.m_Rect = rect;
            sprite.m_Pivot = new Vector2(normalizedPivot.x * rect.width, normalizedPivot.y * rect.height);
            sprite.m_Border = border;
            sprite.m_PixelsPerUnit = pixelsPerUnit > 0 ? pixelsPerUnit : 100f;
            sprite.m_MeshType = (SpriteMeshType)meshType;
            sprite.m_Outline = outline;
            sprite.m_PhysicsShape = physicsShape;
            sprite.BuildGeometry();
            return sprite;
        }

        /// <summary>Vertices in units relative to the pivot, uvs in the texture, two triangles per quad
        /// (or the authored outline, fan-triangulated).</summary>
        void BuildGeometry()
        {
            float w = m_Texture != null ? m_Texture.width : 1f;
            float h = m_Texture != null ? m_Texture.height : 1f;
            Vector2 ToVertex(Vector2 px) => new Vector2((px.x - m_Pivot.x) / m_PixelsPerUnit, (px.y - m_Pivot.y) / m_PixelsPerUnit);
            Vector2 ToUv(Vector2 px) => new Vector2((m_Rect.x + px.x) / w, (m_Rect.y + px.y) / h);
            if (m_MeshType == SpriteMeshType.Tight && m_Outline != null && m_Outline.Length > 0 && m_Outline[0].Length >= 3)
            {
                var vertices = new System.Collections.Generic.List<Vector2>();
                var uvs = new System.Collections.Generic.List<Vector2>();
                var triangles = new System.Collections.Generic.List<ushort>();
                var centre = new Vector2(m_Rect.width / 2f, m_Rect.height / 2f);
                foreach (var path in m_Outline)
                {
                    int start = vertices.Count;
                    foreach (var p in path)
                    {
                        var px = p + centre;
                        vertices.Add(ToVertex(px));
                        uvs.Add(ToUv(px));
                    }
                    for (int i = 1; i + 1 < path.Length; i++)
                    {
                        triangles.Add((ushort)start);
                        triangles.Add((ushort)(start + i));
                        triangles.Add((ushort)(start + i + 1));
                    }
                }
                m_Vertices = vertices.ToArray();
                m_UV = uvs.ToArray();
                m_Triangles = triangles.ToArray();
                return;
            }
            var corners = new[]
            {
                new Vector2(0, m_Rect.height), new Vector2(m_Rect.width, m_Rect.height),
                new Vector2(0, 0), new Vector2(m_Rect.width, 0),
            };
            m_Vertices = new Vector2[4];
            m_UV = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                m_Vertices[i] = ToVertex(corners[i]);
                m_UV[i] = ToUv(corners[i]);
            }
            m_Triangles = new ushort[] { 0, 1, 3, 3, 2, 0 };
        }

        public Bounds bounds
        {
            get
            {
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var v in m_Vertices)
                {
                    min = Vector2.Min(min, v);
                    max = Vector2.Max(max, v);
                }
                var b = new Bounds();
                b.SetMinMax(min, max);
                return b;
            }
        }

        public Rect rect => m_Rect;
        public Rect textureRect => m_Rect;
        public Vector2 textureRectOffset => Vector2.zero;
        public Texture2D texture => m_Texture;
        public Texture2D associatedAlphaSplitTexture => null;
        public Vector2 pivot => m_Pivot;
        public float pixelsPerUnit => m_PixelsPerUnit;
        public float spriteAtlasTextureScale => 1f;
        public Vector4 border => m_Border;
        public bool packed => false;
        public SpritePackingMode packingMode => SpritePackingMode.Rectangle;
        public SpritePackingRotation packingRotation => SpritePackingRotation.None;
        public Vector2[] vertices => (Vector2[])m_Vertices.Clone();
        public ushort[] triangles => (ushort[])m_Triangles.Clone();
        public Vector2[] uv => (Vector2[])m_UV.Clone();

        public int GetPhysicsShapeCount() => m_PhysicsShape?.Length ?? 0;

        public int GetPhysicsShapePointCount(int shapeIdx) => m_PhysicsShape != null && shapeIdx < m_PhysicsShape.Length ? m_PhysicsShape[shapeIdx].Length : 0;

        public int GetPhysicsShape(int shapeIdx, System.Collections.Generic.List<Vector2> physicsShape)
        {
            physicsShape.Clear();
            if (m_PhysicsShape == null || shapeIdx >= m_PhysicsShape.Length) return 0;
            foreach (var p in m_PhysicsShape[shapeIdx]) physicsShape.Add(p / m_PixelsPerUnit);
            return physicsShape.Count;
        }

        public void OverrideGeometry(Vector2[] vertices, ushort[] triangles)
        {
            float w = m_Texture != null ? m_Texture.width : 1f;
            float h = m_Texture != null ? m_Texture.height : 1f;
            m_Vertices = new Vector2[vertices.Length];
            m_UV = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                // Overridden vertices are in pixels within the rect.
                m_Vertices[i] = new Vector2((vertices[i].x - m_Pivot.x) / m_PixelsPerUnit, (vertices[i].y - m_Pivot.y) / m_PixelsPerUnit);
                m_UV[i] = new Vector2((m_Rect.x + vertices[i].x) / w, (m_Rect.y + vertices[i].y) / h);
            }
            m_Triangles = (ushort[])triangles.Clone();
            m_Mesh = null;
        }

        /// <summary>A mesh of the sprite's geometry for the renderer (shared by every renderer of it).</summary>
        internal Mesh RenderMesh()
        {
            if (m_Mesh != null) return m_Mesh;
            var mesh = new Mesh { name = "Sprite " + m_Name };
            var positions = new Vector3[m_Vertices.Length];
            for (int i = 0; i < positions.Length; i++) positions[i] = m_Vertices[i];
            mesh.vertices = positions;
            mesh.uv = m_UV;
            var tris = new int[m_Triangles.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = m_Triangles[i];
            mesh.triangles = tris;
            m_Mesh = mesh;
            return mesh;
        }
    }

    /// <summary>The sprite geometry functions UI code uses (UnityEngine.Sprites.DataUtility).</summary>
    namespace Sprites
    {
        public sealed partial class DataUtility
        {
            public static Vector4 GetInnerUV(Sprite sprite)
            {
                var t = sprite.texture;
                float w = t != null ? t.width : 1f, h = t != null ? t.height : 1f;
                var r = sprite.rect;
                var b = sprite.border;
                return new Vector4((r.x + b.x) / w, (r.y + b.y) / h, (r.xMax - b.z) / w, (r.yMax - b.w) / h);
            }

            public static Vector4 GetOuterUV(Sprite sprite)
            {
                var t = sprite.texture;
                float w = t != null ? t.width : 1f, h = t != null ? t.height : 1f;
                var r = sprite.rect;
                return new Vector4(r.x / w, r.y / h, r.xMax / w, r.yMax / h);
            }

            public static Vector4 GetPadding(Sprite sprite) => Vector4.zero;

            public static Vector2 GetMinSize(Sprite sprite) => new Vector2(sprite.border.x + sprite.border.z, sprite.border.y + sprite.border.w);
        }
    }
}
