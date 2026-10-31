using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A region of a texture drawn as a quad (or a tight mesh), with a pivot and a scale
    /// (pixels per unit).</summary>
    [ClassId(213)]
    public sealed partial class Sprite : Object, Instantiation.ICloneable
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
        /// <summary>The physics shapes in units from the pivot (made at import).</summary>
        internal Vector2[][] m_PhysicsShape;
        /// <summary>The traced bounds of a Tight mesh in texture pixels (else the rect).</summary>
        internal Rect? m_TextureRect;
        /// <summary>OverrideGeometry replaced the mesh: the bounds follow its vertices.</summary>
        bool m_GeometryOverridden;
        internal Mesh m_Mesh;
        /// <summary>The secondary textures (an imported sprite's are loaded when first asked for).</summary>
        SecondarySpriteTexture[] m_SecondaryTextures;
        (string Name, string Guid)[] m_SecondaryGuids;
        System.Collections.Generic.Dictionary<int, object> m_SecondaryProperties;

        Sprite() { }

        /// <summary>A copy sharing the texture (what Instantiate and SpriteAtlas.GetSprite give).</summary>
        Object Instantiation.ICloneable.CloneObject()
        {
            var copy = new Sprite();
            foreach (var field in typeof(Sprite).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                field.SetValue(copy, field.GetValue(this));
            copy.m_Mesh = null;
            return copy;
        }

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

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType,
            Vector4 border, bool generateFallbackPhysicsShape, SecondarySpriteTexture[] secondaryTextures)
        {
            var sprite = Create(texture, rect, pivot, pixelsPerUnit, extrude, meshType, border, generateFallbackPhysicsShape);
            if (sprite != null && secondaryTextures != null && secondaryTextures.Length > 0)
                sprite.m_SecondaryTextures = (SecondarySpriteTexture[])secondaryTextures.Clone();
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

        /// <summary>A sprite of an imported texture, from its import definition: the Tight mesh
        /// (vertices in units from the pivot, uvs, triangles; null for a quad), the texture rect
        /// and the physics shapes (units from the pivot) the importer made.</summary>
        internal static Sprite FromImport(Texture2D texture, string name, Rect rect, Vector2 normalizedPivot, Vector4 border, float pixelsPerUnit, int meshType,
            Vector2[] meshVertices, Vector2[] meshUv, ushort[] meshTriangles, Rect? textureRect, Vector2[][] physicsShape)
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
            sprite.m_TextureRect = textureRect;
            sprite.m_PhysicsShape = physicsShape;
            if (meshVertices != null && meshVertices.Length > 0 && meshTriangles != null && meshTriangles.Length > 0)
            {
                sprite.m_Vertices = meshVertices;
                sprite.m_UV = meshUv;
                sprite.m_Triangles = meshTriangles;
            }
            else
                sprite.BuildGeometry();
            return sprite;
        }

        /// <summary>The rect's corners in units relative to the pivot.</summary>
        Vector2[] QuadCorners()
        {
            Vector2 ToVertex(Vector2 px) => new Vector2((px.x - m_Pivot.x) / m_PixelsPerUnit, (px.y - m_Pivot.y) / m_PixelsPerUnit);
            return new[]
            {
                ToVertex(new Vector2(0, m_Rect.height)), ToVertex(new Vector2(m_Rect.width, m_Rect.height)),
                ToVertex(new Vector2(0, 0)), ToVertex(new Vector2(m_Rect.width, 0)),
            };
        }

        /// <summary>Vertices in units relative to the pivot, uvs in the texture, two triangles per
        /// quad. (Tight meshes of imported sprites come from the importer; see FromImport.)</summary>
        void BuildGeometry()
        {
            float w = m_Texture != null ? m_Texture.width : 1f;
            float h = m_Texture != null ? m_Texture.height : 1f;
            Vector2 ToVertex(Vector2 px) => new Vector2((px.x - m_Pivot.x) / m_PixelsPerUnit, (px.y - m_Pivot.y) / m_PixelsPerUnit);
            Vector2 ToUv(Vector2 px) => new Vector2((m_Rect.x + px.x) / w, (m_Rect.y + px.y) / h);
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

        /// <summary>An imported sprite's secondary textures, by shader property name and texture GUID.</summary>
        internal void SetImportedSecondaryTextures((string Name, string Guid)[] textures) => m_SecondaryGuids = textures;

        SecondarySpriteTexture[] SecondaryTextures
        {
            get
            {
                if (m_SecondaryTextures == null && m_SecondaryGuids != null)
                {
                    var list = new System.Collections.Generic.List<SecondarySpriteTexture>();
                    foreach (var (name, guid) in m_SecondaryGuids)
                        list.Add(new SecondarySpriteTexture { name = name, texture = AssetDatabaseRuntime.LoadMain(guid, typeof(Texture2D)) as Texture2D });
                    m_SecondaryTextures = list.ToArray();
                    m_SecondaryGuids = null;
                }
                return m_SecondaryTextures;
            }
        }

        public int GetSecondaryTextureCount() => SecondaryTextures?.Length ?? 0;

        public int GetSecondaryTextures(SecondarySpriteTexture[] secondaryTexture)
        {
            if (secondaryTexture == null) throw new ArgumentNullException(nameof(secondaryTexture));
            var own = SecondaryTextures;
            if (own == null) return 0;
            int n = Math.Min(own.Length, secondaryTexture.Length);
            Array.Copy(own, secondaryTexture, n);
            return n;
        }

        internal Texture2D GetSecondaryTexture(int index)
        {
            var own = SecondaryTextures;
            return own != null && index >= 0 && index < own.Length ? own[index].texture : null;
        }

        /// <summary>The secondary textures as per-draw properties (null when there are none): a
        /// sprite renderer binds them over its material and property block (measured:
        /// tests/projects/resources secondary_textures).</summary>
        internal System.Collections.Generic.Dictionary<int, object> SecondaryProperties()
        {
            var own = SecondaryTextures;
            if (own == null || own.Length == 0) return null;
            if (m_SecondaryProperties != null) return m_SecondaryProperties;
            m_SecondaryProperties = new System.Collections.Generic.Dictionary<int, object>();
            foreach (var t in own)
                if (!string.IsNullOrEmpty(t.name) && t.texture != null) m_SecondaryProperties[Shader.PropertyToID(t.name)] = t.texture;
            return m_SecondaryProperties;
        }

        public Bounds bounds
        {
            get
            {
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                // The rect's, also for a Tight mesh (measured: tests/projects/spriteimport).
                foreach (var v in m_GeometryOverridden ? m_Vertices : QuadCorners())
                {
                    min = Vector2.Min(min, v);
                    max = Vector2.Max(max, v);
                }
                // 0.2 deep, as Unity reports sprite bounds.
                var b = new Bounds();
                b.SetMinMax(new Vector3(min.x, min.y, -0.1f), new Vector3(max.x, max.y, 0.1f));
                return b;
            }
        }

        public Rect rect => m_Rect;
        /// <summary>The rect, or a Tight mesh's traced bounds (measured: tests/projects/spriteimport).</summary>
        public Rect textureRect => m_TextureRect ?? m_Rect;
        [Unbound.EditorFaithful("sprites are not packed in the Editor's Play mode by default (atlases are built for players; see U2D/SpriteAtlas.cs): the texture rect is the sprite's rect or its Tight mesh's bounds")]
        public Vector2 textureRectOffset => m_TextureRect is Rect t ? new Vector2(t.x - m_Rect.x, t.y - m_Rect.y) : Vector2.zero;
        public Texture2D texture => m_Texture;
        [Unbound.EditorFaithful("alpha-split textures are made for ETC1 Android builds only, never in the Editor")]
        public Texture2D associatedAlphaSplitTexture => null;
        public Vector2 pivot => m_Pivot;
        public float pixelsPerUnit => m_PixelsPerUnit;
        [Unbound.EditorFaithful("sprites are not packed in the Editor's Play mode by default (atlases are built for players; see U2D/SpriteAtlas.cs): the texture rect is the sprite's rect")]
        public float spriteAtlasTextureScale => 1f;
        public Vector4 border => m_Border;
        [Unbound.EditorFaithful("sprites are not packed in the Editor's Play mode by default (atlases are built for players; see U2D/SpriteAtlas.cs): the texture rect is the sprite's rect")]
        public bool packed => false;
        public SpritePackingMode packingMode => SpritePackingMode.Rectangle;
        [Unbound.EditorFaithful("sprites are not packed in the Editor's Play mode by default (atlases are built for players; see U2D/SpriteAtlas.cs): the texture rect is the sprite's rect")]
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
            physicsShape.AddRange(m_PhysicsShape[shapeIdx]);
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
            m_GeometryOverridden = true;
            m_Mesh = null;
        }

        /// <summary>The normal and tangent a sprite renderer gives every vertex: towards the camera,
        /// along +x with a negative handedness (measured: tests/projects/resources
        /// sprite.attributes).</summary>
        internal static void SpriteVertexFrames(Mesh mesh, int count)
        {
            var normals = new Vector3[count];
            var tangents = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                normals[i] = new Vector3(0f, 0f, -1f);
                tangents[i] = new Vector4(1f, 0f, 0f, -1f);
            }
            mesh.normals = normals;
            mesh.tangents = tangents;
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
            SpriteVertexFrames(mesh, positions.Length);
            var tris = new int[m_Triangles.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = m_Triangles[i];
            mesh.triangles = tris;
            m_Mesh = mesh;
            return mesh;
        }
    }

    /// <summary>A texture a sprite brings besides its main one, bound to a shader property.</summary>
    public partial struct SecondarySpriteTexture
    {
        public string name;
        public Texture2D texture;
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

            [Unbound.EditorFaithful("sprites are not packed in the Editor's Play mode by default (atlases are built for players; see U2D/SpriteAtlas.cs): the texture rect is the sprite's rect")]
            public static Vector4 GetPadding(Sprite sprite) => Vector4.zero;

            public static Vector2 GetMinSize(Sprite sprite) => new Vector2(sprite.border.x + sprite.border.z, sprite.border.y + sprite.border.w);
        }
    }
}
