using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine.U2D;

namespace UnityEngine
{
    public sealed partial class Sprite : ISerializedAsset
    {
        // The authored offset is independent of the original rectangle, e.g. trimmed meshes.
        Vector2? m_SerializedTextureRectOffset;

        internal static void ValidateSerialized(YNode body) => ReadSpriteData(body);

        /// <summary>A saved Sprite's data, read and checked before any of it is adopted.</summary>
        sealed class SavedSprite
        {
            internal Rect Rect, TextureRect;
            internal Vector2 Pivot, Offset;
            internal Vector4 Border, Transform;
            internal float Ppu;
            internal Vector2[] Vertices;
            internal ushort[] Triangles;
            internal SpriteRenderGeometry.Attributes Attributes;
            internal Vector2[][] Shapes;
            internal SpriteBone[] Bones;
            internal Matrix4x4[] BindPoses;
            /// <summary>Null: the Sprite was saved without a texture (one that was never an asset).</summary>
            internal ObjectRef Texture;
            internal (string Name, ObjectRef Texture)[] Secondary;
            internal int Settings;
            internal uint Extrude;
        }

        static SavedSprite ReadSpriteData(YNode body)
        {
            // Validate everything before adopting any data. ObjectGraphBuilder removes a failed
            // Sprite, including references already made to its placeholder; it never draws a quad.
            // A Sprite saved from a SpriteAtlas member keeps the atlas and its packed render data
            // (m_AtlasRD) beside its own; the Editor draws and reports it from m_RD, unpacked.
            if (!string.IsNullOrEmpty(body.Str("m_AtlasName")))
                throw new NotSupportedException("legacy Sprite Packer atlas representations are not supported");
            if (body.Get("m_ScriptableObjects") != null) Empty(body, "m_ScriptableObjects");
            var rd = Need(body, "m_RD");
            Version(rd, 3);
            foreach (var key in rd.Keys)
                if (!RenderKeys.Contains(key)) throw new NotSupportedException($"unfamiliar Sprite render-data field `{key}`");
            // settingsRaw: bit 0 packed, bit 1 tight packing, bits 2-5 the packing rotation, bit 6
            // the mesh type. m_RD of a saved Sprite is never packed (only its m_AtlasRD is).
            int settings = Integer(rd, "settingsRaw");
            if (settings != 0 && settings != 64)
                throw new NotSupportedException($"packed, rotated or unfamiliar Sprite settingsRaw {settings}");
            if (!Ref(rd, "alphaTexture").IsNull) throw new NotSupportedException("split alpha textures are not supported");
            if (Number(rd, "downscaleMultiplier") != 1f)
                throw new NotSupportedException("downscaled Sprite render data is not supported");
            var rect = Rectangle(Need(body, "m_Rect"));
            var pivot = V2(Need(body, "m_Pivot"));
            var border = V4(Need(body, "m_Border"));
            float ppu = Number(body, "m_PixelsToUnits");
            if (ppu <= 0 || rect.width <= 0 || rect.height <= 0)
                throw new FormatException("Sprite rect and pixels per unit must be positive");
            var textureRect = Rectangle(Need(rd, "textureRect"));
            var offset = V2(Need(rd, "textureRectOffset"));
            var transform = V4(Need(rd, "uvTransform"));
            if (transform.x != ppu || transform.z != ppu)
                throw new NotSupportedException("unfamiliar Sprite UV scale");
            var vd = Need(rd, "m_VertexData");
            // Version 2 (earlier Editors) has the same layout.
            int vdVersion = Integer(vd, "serializedVersion");
            if (vdVersion != 2 && vdVersion != 3) throw new NotSupportedException($"Sprite vertex data serializedVersion {vdVersion}");
            foreach (var key in vd.Keys)
                if (key != "serializedVersion" && key != "m_VertexCount" && key != "m_Channels"
                    && key != "m_DataSize" && key != "_typelessdata")
                    throw new NotSupportedException($"unfamiliar Sprite vertex-data field `{key}`");
            int count = Integer(vd, "m_VertexCount");
            var channels = new List<(int Stream, int Offset, int Format, int Dimension)>();
            foreach (var channel in Sequence(vd, "m_Channels"))
                channels.Add((Integer(channel, "stream"), Integer(channel, "offset"), Integer(channel, "format"), Integer(channel, "dimension")));
            var data = Bytes(vd, "_typelessdata");
            if (data.Length != Integer(vd, "m_DataSize")) throw new FormatException("Sprite vertex byte count does not match m_DataSize");
            var attributes = SpriteRenderGeometry.SavedAttributes(count, data, channels);
            var vertices = SpriteRenderGeometry.Positions(count, data, channels);
            var submeshes = Sequence(rd, "m_SubMeshes");
            if (submeshes.Length != 1) throw new NotSupportedException($"Sprite has {submeshes.Length} submeshes");
            var sub = submeshes[0];
            Version(sub, 2);
            if (Integer(sub, "topology") != 0 || Integer(sub, "baseVertex") != 0)
                throw new NotSupportedException("Sprite requires a triangle submesh with no base vertex");
            if (Integer(sub, "firstVertex") != 0 || Integer(sub, "vertexCount") != count)
                throw new NotSupportedException("Sprite submesh uses an unfamiliar vertex range");
            var triangles = SpriteRenderGeometry.Indices(Bytes(rd, "m_IndexBuffer"), Integer(sub, "firstByte"), Integer(sub, "indexCount"), count);
            var shapes = new List<Vector2[]>();
            foreach (var shape in Sequence(body, "m_PhysicsShape"))
            {
                if (shape.Kind != YKind.Seq) throw new FormatException("Sprite physics shape is not a sequence");
                var points = new Vector2[shape.Count];
                for (int i = 0; i < points.Length; i++) points[i] = V2(shape.Items[i]);
                shapes.Add(points);
            }
            // Earlier Editors saved no bones, scriptable objects or secondary textures fields.
            var bones = body.Get("m_Bones") != null ? Bones(Sequence(body, "m_Bones")) : Array.Empty<SpriteBone>();
            var bindPoses = Array.ConvertAll(Sequence(rd, "m_Bindpose"), Matrix);
            var secondary = rd.Get("secondaryTextures") != null
                ? Array.ConvertAll(Sequence(rd, "secondaryTextures"), t => (t.Str("name"), Ref(t, "texture")))
                : Array.Empty<(string, ObjectRef)>();
            return new SavedSprite
            {
                Rect = rect, Pivot = pivot, Border = border, Ppu = ppu, TextureRect = textureRect, Offset = offset,
                Transform = transform, Vertices = vertices, Triangles = triangles, Attributes = attributes,
                Shapes = shapes.ToArray(), Bones = bones, BindPoses = bindPoses, Texture = Ref(rd, "texture"),
                Secondary = secondary, Settings = settings, Extrude = checked((uint)Integer(body, "m_Extrude")),
            };
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var data = ReadSpriteData(body);
            Texture2D texture = null;
            if (!data.Texture.IsNull)
            {
                texture = resolver.Resolve(data.Texture, typeof(Texture2D)) as Texture2D;
                if (texture == null)
                    throw new NotSupportedException($"Sprite texture {data.Texture} did not resolve to a Texture2D");
            }
            var rect = data.Rect;
            var textureRect = data.TextureRect;
            if (texture != null && (rect.x < 0 || rect.y < 0 || rect.xMax > texture.width || rect.yMax > texture.height
                || textureRect.x < 0 || textureRect.y < 0 || textureRect.width <= 0 || textureRect.height <= 0
                || textureRect.xMax > texture.width || textureRect.yMax > texture.height))
                throw new NotSupportedException("Sprite rectangles lie outside the referenced texture");
            var secondary = new SecondarySpriteTexture[data.Secondary.Length];
            for (int i = 0; i < secondary.Length; i++)
            {
                var (name, reference) = data.Secondary[i];
                var t = reference.IsNull ? null : resolver.Resolve(reference, typeof(Texture2D)) as Texture2D;
                if (!reference.IsNull && t == null)
                    throw new NotSupportedException($"Sprite secondary texture {reference} did not resolve to a Texture2D");
                secondary[i] = new SecondarySpriteTexture { name = name, texture = t };
            }
            // The UVs map the positions through uvTransform into the texture; without a texture
            // they are zero (measured).
            var uv = new Vector2[data.Vertices.Length];
            if (texture != null)
                for (int i = 0; i < uv.Length; i++)
                {
                    uv[i] = new Vector2((data.Vertices[i].x * data.Transform.x + data.Transform.y) / texture.width,
                        (data.Vertices[i].y * data.Transform.z + data.Transform.w) / texture.height);
                    if (!float.IsFinite(uv[i].x) || !float.IsFinite(uv[i].y)) throw new FormatException("Sprite UV is not finite");
                }
            m_Texture = texture;
            m_Rect = rect;
            m_Pivot = new Vector2(data.Pivot.x * rect.width, data.Pivot.y * rect.height);
            m_PixelsPerUnit = data.Ppu;
            m_Border = data.Border;
            m_MeshType = (SpriteMeshType)((data.Settings >> 6) & 1);
            m_Extrude = data.Extrude;
            m_TextureRect = textureRect;
            m_SerializedTextureRectOffset = data.Offset;
            m_Vertices = data.Vertices;
            m_UV = uv;
            m_Triangles = data.Triangles;
            m_PhysicsShape = data.Shapes;
            m_Bones = data.Bones;
            m_BindPoses = data.BindPoses;
            m_BoneWeights = data.Attributes.Weights;
            m_Normals = data.Attributes.Normals;
            m_Tangents = data.Attributes.Tangents;
            m_Colors = data.Attributes.Colors;
            m_SecondaryTextures = secondary.Length != 0 ? secondary : null;
            m_SecondaryGuids = null;
            m_SecondaryProperties = null;
            m_MeshData = null;
            m_Mesh = null;
        }

        static SpriteBone[] Bones(YNode[] items)
        {
            var bones = new SpriteBone[items.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = items[i];
                var position = Need(b, "position");
                var rotation = Need(b, "rotation");
                int parent = Integer(b, "parentId");
                if (parent < -1 || parent >= items.Length) throw new FormatException($"Sprite bone {i} has parent {parent}");
                bones[i] = new SpriteBone
                {
                    name = b.Str("name"),
                    guid = b.Str("guid"),
                    position = new Vector3(Number(position, "x"), Number(position, "y"), Number(position, "z")),
                    rotation = new Quaternion(Number(rotation, "x"), Number(rotation, "y"), Number(rotation, "z"), Number(rotation, "w")),
                    length = Number(b, "length"),
                    parentId = parent,
                    color = PackedColor(Need(b, "color")),
                };
            }
            return bones;
        }

        /// <summary>A ColorRGBA32 field: serializedVersion 2, rgba = r | g &lt;&lt; 8 | b &lt;&lt; 16 | a &lt;&lt; 24.</summary>
        static Color32 PackedColor(YNode node)
        {
            Version(node, 2);
            uint rgba = uint.Parse(Scalar(node, "rgba"), NumberStyles.Integer, CultureInfo.InvariantCulture);
            return new Color32((byte)rgba, (byte)(rgba >> 8), (byte)(rgba >> 16), (byte)(rgba >> 24));
        }

        static Matrix4x4 Matrix(YNode node)
        {
            var m = new Matrix4x4();
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    m[row, column] = Number(node, MatrixKeys[row * 4 + column]);
            return m;
        }

        static readonly string[] MatrixKeys =
        {
            "e00", "e01", "e02", "e03", "e10", "e11", "e12", "e13", "e20", "e21", "e22", "e23", "e30", "e31", "e32", "e33",
        };

        static readonly HashSet<string> RenderKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "serializedVersion", "texture", "alphaTexture", "secondaryTextures", "m_SubMeshes", "m_IndexBuffer",
            "m_VertexData", "m_Bindpose", "textureRect", "textureRectOffset", "atlasRectOffset", "settingsRaw",
            "uvTransform", "downscaleMultiplier",
        };

        static YNode Need(YNode node, string key) => node?.Get(key) ?? throw new FormatException($"Sprite has no `{key}`");
        static string Scalar(YNode node, string key) => Need(node, key) is var n && n.Kind == YKind.Scalar
            ? n.Scalar : throw new FormatException($"Sprite `{key}` is not a scalar");
        static float Number(YNode node, string key)
        {
            float value = float.Parse(Scalar(node, key), NumberStyles.Float, CultureInfo.InvariantCulture);
            return float.IsFinite(value) ? value : throw new FormatException($"Sprite `{key}` is not finite");
        }
        static int Integer(YNode node, string key) => int.Parse(Scalar(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture);
        static byte[] Bytes(YNode node, string key) => Convert.FromHexString(Scalar(node, key));
        static Vector2 V2(YNode node) => new Vector2(Number(node, "x"), Number(node, "y"));
        static Vector4 V4(YNode node) => new Vector4(Number(node, "x"), Number(node, "y"), Number(node, "z"), Number(node, "w"));
        static Rect Rectangle(YNode node)
        {
            Version(node, 2);
            return new Rect(Number(node, "x"), Number(node, "y"), Number(node, "width"), Number(node, "height"));
        }
        static ObjectRef Ref(YNode node, string key)
        {
            var reference = Need(node, key);
            long.Parse(Scalar(reference, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture);
            return reference.AsRef();
        }
        static YNode[] Sequence(YNode node, string key) => Need(node, key) is var n && n.Kind == YKind.Seq
            ? n.Items : throw new FormatException($"Sprite `{key}` is not a sequence");
        static void Empty(YNode node, string key)
        {
            if (Sequence(node, key).Length != 0) throw new NotSupportedException($"Sprite `{key}` is not supported");
        }
        static void Version(YNode node, int expected)
        {
            int actual = Integer(node, "serializedVersion");
            if (actual != expected) throw new NotSupportedException($"Sprite serializedVersion {actual}, expected {expected}");
        }
    }
}
