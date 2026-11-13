using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A region of a texture drawn as a quad (or a tight mesh), with a pivot and a scale
    /// (pixels per unit).</summary>
    [global::UnityEngine.@ExcludeFromPresetAttribute] [ClassId(213)]
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
        /// <summary>Made without a texture (vector sprites): drawn with its material alone. A
        /// sprite whose texture is missing is not drawn (measured: tests/projects/svgsprites,
        /// tests/projects/spriteassets).</summary>
        internal bool m_DrawnWithoutTexture;
        /// <summary>The geometry the particle Shape module samples (see ParticleShapeMesh).</summary>
        Mesh m_ShapeMesh;
        /// <summary>The secondary textures (an imported sprite's are loaded when first asked for).</summary>
        SecondarySpriteTexture[] m_SecondaryTextures;
        (string Name, string Guid)[] m_SecondaryGuids;
        System.Collections.Generic.Dictionary<int, object> m_SecondaryProperties;
        /// <summary>The imported sprite's place in the Play-mode atlas set (its texture's GUID and
        /// file id), when a packed atlas holds it (see U2D/SpriteAtlasPacking.cs).</summary>
        internal (string Texture, long FileId)? m_AtlasBinding;
        /// <summary>Drawn from a packed atlas's page: its texture rect there.</summary>
        Rect? m_AtlasRect;
        SpritePackingMode m_PackingMode = SpritePackingMode.Rectangle;
        SpritePackingRotation m_PackingRotation;
        float m_AtlasScale = 1f;

        Sprite() { }

        /// <summary>A copy sharing the texture (what Instantiate and SpriteAtlas.GetSprite give).</summary>
        Object Instantiation.ICloneable.CloneObject()
        {
            var copy = new Sprite();
            foreach (var field in typeof(Sprite).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                field.SetValue(copy, field.GetValue(this));
            copy.m_Mesh = null;
            // The copy builds its own data access view: writing through it must not change this one.
            copy.m_MeshData = null;
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
            // A Tight rect under 32 pixels on a side stays a quad: nothing to trace for its mesh.
            bool tight = meshType == SpriteMeshType.Tight && rect.width >= 32 && rect.height >= 32;
            if (tight || generateFallbackPhysicsShape) sprite.Trace(tight, generateFallbackPhysicsShape);
            return sprite;
        }

        /// <summary>The texture an imported sprite was made from (its page replaces m_Texture when an
        /// atlas packs it); null for sprites made otherwise.</summary>
        Texture2D m_SourceTexture;
        /// <summary>The outline colliders take when the sprite has no physics shape, once traced
        /// from an imported texture's import.</summary>
        Vector2[][] m_FallbackOutline;

        sealed class GeometryJson
        {
            public SpriteMeshJson Mesh { get; set; }
            public float[] TextureRect { get; set; }
            public float[][][] Physics { get; set; }
        }

        sealed class SpriteMeshJson
        {
            public float[][] Vertices { get; set; }
            public float[][] Uv { get; set; }
            public int[] Triangles { get; set; }
        }

        static readonly System.Text.Json.JsonSerializerOptions s_Json = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

        /// <summary>The texels a trace of <paramref name="rect"/> reads: an imported texture whose
        /// texels are still the import's by its GUID, else the texture's CPU texels within the rect
        /// (widened to whole texels): also those a script wrote but did not apply yet, and those of
        /// a texture made non-readable by Apply (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteoutlines). Null without a texture (the sprite stays a quad).</summary>
        internal static Dictionary<string, object> TexelSource(Texture2D texture, Rect rect)
        {
            if (texture == null || !Native.IsReady) return null;
            if (texture.TexelsAsImported) return new Dictionary<string, object> { ["guid"] = texture.m_AssetGuid };
            int width = texture.width, height = texture.height;
            int x0 = Math.Clamp((int)MathF.Floor(rect.x), 0, width), y0 = Math.Clamp((int)MathF.Floor(rect.y), 0, height);
            int x1 = Math.Clamp((int)MathF.Ceiling(rect.x + rect.width), x0, width), y1 = Math.Clamp((int)MathF.Ceiling(rect.y + rect.height), y0, height);
            var texels = texture.EngineTexels32Window(x0, y0, x1 - x0, y1 - y0);
            var bytes = new byte[texels.Length * 4];
            for (int i = 0; i < texels.Length; i++)
            {
                bytes[i * 4] = texels[i].r; bytes[i * 4 + 1] = texels[i].g; bytes[i * 4 + 2] = texels[i].b; bytes[i * 4 + 3] = texels[i].a;
            }
            return new Dictionary<string, object>
            {
                ["rgba"] = Convert.ToBase64String(bytes), ["width"] = width, ["height"] = height,
                ["window"] = new[] { x0, y0, x1 - x0, y1 - y0 },
            };
        }

        /// <summary>Traces the texture as the importer does: the Tight mesh and the fallback
        /// physics shape (matches the Unity 6000.0.71f1 Editor's Sprite.Create,
        /// tests/projects/spriteoutlines).</summary>
        void Trace(bool tight, bool physics)
        {
            var request = TexelSource(m_Texture, m_Rect);
            if (request == null) return;
            var geometry = TraceRequest(request, tight, physics);
            if (geometry == null) return;
            if (tight && geometry.Mesh?.Triangles is { Length: > 0 } triangles)
            {
                m_Vertices = Array.ConvertAll(geometry.Mesh.Vertices, v => new Vector2(v[0], v[1]));
                m_UV = Array.ConvertAll(geometry.Mesh.Uv, v => new Vector2(v[0], v[1]));
                m_Triangles = Array.ConvertAll(triangles, i => (ushort)i);
                var t = geometry.TextureRect;
                m_TextureRect = new Rect(t[0], t[1], t[2], t[3]);
            }
            if (physics) m_PhysicsShape = Paths(geometry.Physics);
        }

        GeometryJson TraceRequest(Dictionary<string, object> request, bool tight, bool physics)
        {
            request["rect"] = new[] { m_Rect.x, m_Rect.y, m_Rect.width, m_Rect.height };
            request["pivot"] = new[] { m_Rect.width > 0 ? m_Pivot.x / m_Rect.width : 0f, m_Rect.height > 0 ? m_Pivot.y / m_Rect.height : 0f };
            request["pixelsPerUnit"] = m_PixelsPerUnit;
            request["extrude"] = m_Extrude;
            request["tight"] = tight;
            request["physics"] = physics;
            try { return System.Text.Json.JsonSerializer.Deserialize<GeometryJson>(Native.Call("sprite-geometry", System.Text.Json.JsonSerializer.Serialize(request)), s_Json); }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the texture of sprite {m_Name} could not be traced: {e.Message}");
                return null;
            }
        }

        static Vector2[][] Paths(float[][][] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            return Array.ConvertAll(paths, p => Array.ConvertAll(p, v => new Vector2(v[0], v[1])));
        }

        /// <summary>The outlines a PolygonCollider2D or a sprite tile's collider takes: the physics
        /// shape, else the outline traced as a generated physics shape would be (also for Full Rect
        /// sprites: matches the Unity 6000.0.71f1 Editor, tests/projects/spriteoutlines); empty when
        /// the texture cannot be traced.</summary>
        internal Vector2[][] ColliderOutline()
        {
            if (m_PhysicsShape is { Length: > 0 }) return m_PhysicsShape;
            var texture = m_SourceTexture ?? m_Texture;
            // Kept only while nothing rewrote the texels it was traced from.
            bool imported = texture != null && texture.TexelsAsImported;
            if (m_FallbackOutline != null && imported) return m_FallbackOutline;
            var request = TexelSource(texture, m_Rect);
            var geometry = request == null ? null : TraceRequest(request, false, true);
            var outline = Paths(geometry?.Physics) ?? Array.Empty<Vector2[]>();
            m_FallbackOutline = imported ? outline : null;
            return outline;
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

        /// <summary>A Tight sprite with a rect, a normalized pivot and an optional texture but no
        /// geometry yet (OverrideGeometry gives it some), as the SVG importer makes its sprites.
        /// A pixels per unit of 0 or less keeps the sprite's default (Unbound's 100; unmeasured).</summary>
        internal static Sprite Create(Rect rect, Vector2 pivot, float pixelsToUnits, Texture2D texture) => new Sprite
        {
            m_Texture = texture,
            m_Rect = rect,
            m_Pivot = new Vector2(pivot.x * rect.width, pivot.y * rect.height),
            m_PixelsPerUnit = pixelsToUnits > 0 ? pixelsToUnits : 100f,
            m_MeshType = SpriteMeshType.Tight,
            m_DrawnWithoutTexture = texture == null,
            m_Vertices = Array.Empty<Vector2>(),
            m_UV = Array.Empty<Vector2>(),
            m_Triangles = Array.Empty<ushort>(),
        };

        internal static Sprite Create(Rect rect, Vector2 pivot, float pixelsToUnits) => Create(rect, pivot, pixelsToUnits, null);

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
            sprite.m_SourceTexture = texture;
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
            // The Editor's quad triangle order (measured: tests/projects/spriteimport).
            m_Triangles = new ushort[] { 0, 1, 2, 2, 1, 3 };
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
        /// <summary>The rect, or a Tight mesh's traced bounds (measured: tests/projects/spriteimport);
        /// in a packed atlas its place on the page, also for tightly packed sprites (measured:
        /// tests/projects/spriteatlas).</summary>
        public Rect textureRect => m_AtlasRect ?? m_TextureRect ?? m_Rect;
        public Vector2 textureRectOffset => m_SerializedTextureRectOffset ?? (m_TextureRect is Rect t ? new Vector2(t.x - m_Rect.x, t.y - m_Rect.y) : Vector2.zero);
        public Texture2D texture => m_Texture;
        [Unbound.EditorFaithful("alpha-split textures are made for ETC1 Android builds only, never in the Editor")]
        public Texture2D associatedAlphaSplitTexture => null;
        public Vector2 pivot => m_Pivot;
        public float pixelsPerUnit => m_PixelsPerUnit;
        /// <summary>A variant atlas's scale for the clones it gives (measured: tests/projects/spriteatlas).</summary>
        public float spriteAtlasTextureScale => m_AtlasScale;
        public Vector4 border => m_Border;
        public bool packed => m_AtlasRect != null;
        public SpritePackingMode packingMode => m_PackingMode;
        public SpritePackingRotation packingRotation => m_PackingRotation;

        /// <summary>Draws the sprite from a packed atlas's page: its texture rect and UV transform
        /// (pixels per unit, offset) there, in a page of `uvSize` (a variant's scaled page keeps its
        /// master's rects and UVs, with its `scale`); the UVs follow, mirrored as the packer flipped
        /// the sprite (Unity 6000.0.71f1, tests/projects/spriteatlas).</summary>
        internal void BindAtlas(Texture2D page, Rect atlasRect, Vector4 uvTransform, SpritePackingMode mode, SpritePackingRotation rotation,
            Vector2 uvSize, float scale)
        {
            m_Texture = page;
            m_AtlasRect = atlasRect;
            m_PackingMode = mode;
            m_PackingRotation = rotation;
            m_AtlasScale = scale;
            float w = uvSize.x, h = uvSize.y;
            var offset = textureRectOffset;
            // The mirror lines: the atlas rect's far edges from the sprite's pivot.
            float pivotX = m_Rect.width > 0 ? m_Pivot.x / m_Rect.width : 0f;
            float pivotY = m_Rect.height > 0 ? m_Pivot.y / m_Rect.height : 0f;
            float farX = uvTransform.y - m_Rect.width * pivotX + atlasRect.width + offset.x;
            float farY = uvTransform.w - m_Rect.height * pivotY + atlasRect.height + offset.y;
            float farU = farX / w, nearU = (farX - atlasRect.width) / w;
            float farV = farY / h, nearV = (farY - atlasRect.height) / h;
            bool flipX = rotation == SpritePackingRotation.FlipHorizontal || rotation == SpritePackingRotation.Rotate180;
            bool flipY = rotation == SpritePackingRotation.FlipVertical || rotation == SpritePackingRotation.Rotate180;
            m_UV = new Vector2[m_Vertices.Length];
            for (int i = 0; i < m_Vertices.Length; i++)
            {
                float u = (m_Vertices[i].x * uvTransform.x + uvTransform.y) / w;
                float v = (m_Vertices[i].y * uvTransform.z + uvTransform.w) / h;
                if (flipX) u = farU - u + nearU;
                if (flipY) v = farV - v + nearV;
                m_UV[i] = new Vector2(u, v);
            }
            m_Mesh = null;
            m_ShapeMesh = null;
        }

        /// <summary>The texture size the sprite's rects are in: a variant atlas's scaled page keeps
        /// its master's rects (measured: tests/projects/spriteatlas).</summary>
        internal Vector2 RectSpace
        {
            get
            {
                var t = m_Texture;
                return t == null ? Vector2.one : new Vector2(t.width / m_AtlasScale, t.height / m_AtlasScale);
            }
        }

        /// <summary>The sprite's rect where it is drawn from: in a packed atlas, the rect placed as
        /// its texture rect is (the rect itself otherwise).</summary>
        internal Rect DrawnRect
        {
            get
            {
                if (m_AtlasRect is not Rect t) return m_Rect;
                var offset = textureRectOffset;
                return new Rect(t.x - offset.x, t.y - offset.y, m_Rect.width, m_Rect.height);
            }
        }

        /// <summary>Mirrors UVs within the drawn rect as the packer flipped the sprite (UVs made
        /// for the unflipped drawn rect, as sliced and tiled geometry makes them).</summary>
        internal void MirrorPackedUVs(Vector2[] uvs)
        {
            bool flipX = m_PackingRotation == SpritePackingRotation.FlipHorizontal || m_PackingRotation == SpritePackingRotation.Rotate180;
            bool flipY = m_PackingRotation == SpritePackingRotation.FlipVertical || m_PackingRotation == SpritePackingRotation.Rotate180;
            if (m_AtlasRect == null || !(flipX || flipY)) return;
            var r = DrawnRect;
            var space = RectSpace;
            float sumU = (2f * r.x + r.width) / space.x, sumV = (2f * r.y + r.height) / space.y;
            for (int i = 0; i < uvs.Length; i++)
            {
                if (flipX) uvs[i].x = sumU - uvs[i].x;
                if (flipY) uvs[i].y = sumV - uvs[i].y;
            }
        }

        /// <summary>A sprite of an atlas that binds late, before the atlas is registered: no texture
        /// (measured: tests/projects/spriteatlas).</summary>
        internal void UnbindTexture()
        {
            m_Texture = null;
            // Without a texture the UVs are zero (as for Sprites saved without one).
            m_UV = new Vector2[m_Vertices.Length];
            m_Mesh = null;
            m_ShapeMesh = null;
        }
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
            foreach (var vertex in vertices)
                if (vertex.x < 0f || vertex.y < 0f || vertex.x > m_Rect.width || vertex.y > m_Rect.height)
                {
                    Debug.LogError(FormattableString.Invariant($"Invalid vertex array. Some vertices are outside of the Sprite rectangle: ({m_Rect.width:F6}, {m_Rect.height:F6})."));
                    return;
                }
            m_Vertices = new Vector2[vertices.Length];
            m_UV = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                // Overridden vertices are in pixels within the rect; without a texture the UVs
                // stay zero (measured: tests/projects/svgsprites, tests/projects/spriteassets).
                m_Vertices[i] = new Vector2((vertices[i].x - m_Pivot.x) / m_PixelsPerUnit, (vertices[i].y - m_Pivot.y) / m_PixelsPerUnit);
                if (m_Texture != null)
                    m_UV[i] = new Vector2((m_Rect.x + vertices[i].x) / m_Texture.width, (m_Rect.y + vertices[i].y) / m_Texture.height);
            }
            // The other per-vertex channels described the old vertices; they are dropped (Unbound's
            // choice, unmeasured: the SVG importer overrides only empty sprites).
            m_Normals = null;
            m_Tangents = null;
            m_Colors = null;
            m_BoneWeights = null;
            m_TexCoords = null;
            // A new data access view is made when next asked for. Freeing the old one releases its
            // safety handle: slices taken from it throw instead of reading freed memory.
            m_MeshData?.Free();
            m_MeshData = null;
            m_Triangles = (ushort[])triangles.Clone();
            m_GeometryOverridden = true;
            m_Mesh = null;
            m_ShapeMesh = null;
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
            var mesh = GeometryMesh("Sprite " + m_Name, colors: true);
            SpriteVertexFrames(mesh, mesh.m_Vertices.Length);
            m_Mesh = mesh;
            return mesh;
        }

        /// <summary>The sprite's geometry as the particle Shape module's Sprite and SpriteRenderer
        /// shapes sample it: no normals (particles leave along +z) and no colours (6000.0.71f1,
        /// tests/projects/particlemeshshape sprite/*; a sprite with its own colour channel is
        /// unmeasured and also gives none). It is never drawn, so it is marked as sent:
        /// the shape's sampling tables stay built.</summary>
        internal Mesh ParticleShapeMesh()
        {
            if (m_ShapeMesh != null) return m_ShapeMesh;
            m_ShapeMesh = GeometryMesh("Sprite shape " + m_Name, colors: false);
            m_ShapeMesh.m_Dirty = false;
            return m_ShapeMesh;
        }

        /// <summary>A mesh of the sprite's vertices (units from the pivot), UVs (channel 0) and
        /// triangles; with `colors`, the sprite's vertex colours when it has them (a sprite renderer
        /// multiplies them in, as measured in tests/projects/spriteassets).</summary>
        Mesh GeometryMesh(string name, bool colors)
        {
            var mesh = new Mesh { name = name };
            var positions = new Vector3[m_Vertices.Length];
            for (int i = 0; i < positions.Length; i++) positions[i] = m_Vertices[i];
            mesh.vertices = positions;
            mesh.uv = m_UV;
            if (colors && m_Colors != null && m_Colors.Length == positions.Length) mesh.colors32 = m_Colors;
            if (m_TexCoords != null)
                for (int channel = 1; channel < m_TexCoords.Length; channel++)
                    if (m_TexCoords[channel] != null && m_TexCoords[channel].Length == positions.Length)
                        mesh.SetUVs(channel, m_TexCoords[channel]);
            var tris = new int[m_Triangles.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = m_Triangles[i];
            mesh.triangles = tris;
            return mesh;
        }
    }

    /// <summary>A texture a sprite brings besides its main one, bound to a shader property.</summary>
    [global::System.Serializable] public partial struct SecondarySpriteTexture
    {
        public string name;
        public Texture2D texture;
    }

    /// <summary>The sprite geometry functions UI code uses (UnityEngine.Sprites.DataUtility).</summary>
    namespace Sprites
    {
        public sealed partial class DataUtility
        {
            /// <summary>Without a texture the inner and outer UVs are the unit square (measured for a
            /// late-binding sprite before its atlas registers: tests/projects/spriteatlas).</summary>
            static readonly Vector4 s_NoTexture = new Vector4(0f, 0f, 1f, 1f);

            public static Vector4 GetInnerUV(Sprite sprite)
            {
                var t = sprite.texture;
                if (t == null) return s_NoTexture;
                var (w, h) = (sprite.RectSpace.x, sprite.RectSpace.y);
                var r = sprite.DrawnRect;
                var b = sprite.border;
                return new Vector4((r.x + b.x) / w, (r.y + b.y) / h, (r.xMax - b.z) / w, (r.yMax - b.w) / h);
            }

            /// <summary>The texture rect in its texture: a Tight sprite's trimmed rect, a packed
            /// sprite's place on its page (Unity 6000.0.71f1, tests/projects/spriteatlas).</summary>
            public static Vector4 GetOuterUV(Sprite sprite)
            {
                var t = sprite.texture;
                if (t == null) return s_NoTexture;
                var (w, h) = (sprite.RectSpace.x, sprite.RectSpace.y);
                var r = sprite.textureRect;
                return new Vector4(r.x / w, r.y / h, r.xMax / w, r.yMax / h);
            }

            /// <summary>The texture rect inset from the rect: left, bottom, right, top (Unity
            /// 6000.0.71f1, tests/projects/spriteatlas: a trimmed Tight sprite, packed or not).</summary>
            public static Vector4 GetPadding(Sprite sprite)
            {
                var r = sprite.rect;
                var t = sprite.textureRect;
                var offset = sprite.textureRectOffset;
                return new Vector4(offset.x, offset.y, r.width - t.width - offset.x, r.height - t.height - offset.y);
            }

            public static Vector2 GetMinSize(Sprite sprite) => new Vector2(sprite.border.x + sprite.border.z, sprite.border.y + sprite.border.w);
        }
    }
}
