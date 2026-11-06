using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Sprite : ISerializedAsset
    {
        // The authored offset is independent of the original rectangle, e.g. trimmed meshes.
        Vector2? m_SerializedTextureRectOffset;

        internal static void ValidateSerialized(YNode body) => ReadSpriteData(body);

        static (Rect Rect, Vector2 Pivot, Vector4 Border, float Ppu, Rect TextureRect, Vector2 Offset,
            Vector4 Transform, Vector2[] Vertices, ushort[] Triangles, Vector2[][] Shapes,
            ObjectRef Texture, int Settings, uint Extrude) ReadSpriteData(YNode body)
        {
            // Validate everything before adopting any data. ObjectGraphBuilder removes a failed
            // Sprite, including references already made to its placeholder; it never draws a quad.
            if (!Ref(body, "m_SpriteAtlas").IsNull || !string.IsNullOrEmpty(body.Str("m_AtlasName")))
                throw new NotSupportedException("SpriteAtlas/legacy atlas representations are not supported");
            Empty(body, "m_Bones");
            Empty(body, "m_ScriptableObjects");
            var rd = Need(body, "m_RD");
            Version(rd, 3);
            foreach (var key in rd.Keys)
                if (!RenderKeys.Contains(key)) throw new NotSupportedException($"unfamiliar Sprite render-data field `{key}`");
            int settings = Integer(rd, "settingsRaw");
            if (settings != 0 && settings != 64)
                throw new NotSupportedException($"packed, rotated or unfamiliar Sprite settingsRaw {settings}");
            if (!Ref(rd, "alphaTexture").IsNull) throw new NotSupportedException("split alpha textures are not supported");
            Empty(rd, "secondaryTextures");
            Empty(rd, "m_Bindpose");
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
            Version(vd, 3);
            foreach (var key in vd.Keys)
                if (key != "serializedVersion" && key != "m_VertexCount" && key != "m_Channels"
                    && key != "m_DataSize" && key != "_typelessdata")
                    throw new NotSupportedException($"unfamiliar Sprite vertex-data field `{key}`");
            int count = Integer(vd, "m_VertexCount");
            var channels = new List<(int Stream, int Offset, int Format, int Dimension)>();
            foreach (var channel in Sequence(vd, "m_Channels"))
            {
                int dimension = Integer(channel, "dimension");
                // Normals/tangents/colours and skin attributes would change the renderer's data.
                if (channels.Count != 0 && channels.Count != 4 && dimension != 0)
                    throw new NotSupportedException($"Sprite vertex channel {channels.Count} is not supported");
                channels.Add((Integer(channel, "stream"), Integer(channel, "offset"), Integer(channel, "format"), dimension));
            }
            if (channels.Count != 14 || channels[4].Format != 0 || channels[4].Dimension != 2)
                throw new NotSupportedException("unfamiliar Sprite vertex channel layout");
            var data = Bytes(vd, "_typelessdata");
            if (data.Length != Integer(vd, "m_DataSize")) throw new FormatException("Sprite vertex byte count does not match m_DataSize");
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
            var reference = Ref(rd, "texture");
            if (reference.IsNull) throw new NotSupportedException("Sprite texture did not resolve to a Texture2D");
            return (rect, pivot, border, ppu, textureRect, offset, transform, vertices, triangles,
                shapes.ToArray(), reference, settings, checked((uint)Integer(body, "m_Extrude")));
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var data = ReadSpriteData(body);
            var texture = resolver.Resolve(data.Texture, typeof(Texture2D)) as Texture2D;
            if (texture == null)
                throw new NotSupportedException($"Sprite texture {data.Texture} did not resolve to a Texture2D");
            var rect = data.Rect;
            var textureRect = data.TextureRect;
            if (rect.x < 0 || rect.y < 0 || rect.xMax > texture.width || rect.yMax > texture.height
                || textureRect.x < 0 || textureRect.y < 0 || textureRect.width <= 0 || textureRect.height <= 0
                || textureRect.xMax > texture.width || textureRect.yMax > texture.height)
                throw new NotSupportedException("Sprite rectangles lie outside the referenced texture");
            var uv = new Vector2[data.Vertices.Length];
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
            m_Mesh = null;
        }

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
