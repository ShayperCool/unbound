using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The built-in uGUI sprites of `unity_builtin_extra` (UISprite, Background, Knob, Checkmark,
    /// DropdownArrow) and their textures, read from the install's own records by the host: the
    /// Sprite record by `{guid, fileId}` through the `editor-resource` call, its texture by the
    /// record's texture reference through the serialized Texture2D loader. Nothing is drawn or
    /// assumed here: a record the host cannot answer, an object of another class, or record
    /// content this reader does not support (a packed or skinned sprite, a downscaled texture, a
    /// mesh it cannot decode) is an error naming the object, and no object is made for it.
    /// </summary>
    internal static class BuiltinSpriteData
    {
        /// <summary>The built-in sprites (and their textures, which carry the same names) read here.</summary>
        static readonly string[] s_Names = { "UISprite", "Background", "Knob", "Checkmark", "DropdownArrow" };

        const int SpriteClassId = 213;
        const int Texture2DClassId = 28;

        /// <summary>Whether the built-in object `name` of the file `guid` (lower case) is one of
        /// the sprites, or sprite textures, read from the install.</summary>
        internal static bool Reads(string guid, string name) =>
            guid == BuiltinShaders.ExtraGuid && Array.IndexOf(s_Names, name) >= 0;

        static string Identity(string type, string guid, long fileId, string name) =>
            $"built-in {type} '{name}' ({{fileID: {fileId}, guid: {guid}}})";

        /// <summary>The installed texture `fileId` of `unity_builtin_extra`, through the serialized
        /// Texture2D loader, with the sampler of its record (`m_TextureSettings`: filter mode,
        /// anisotropic level, mip bias, wrap modes). Null, with an error naming it, when its record
        /// or its pixels cannot be read or it is not the texture the profile's table names.</summary>
        internal static Texture2D LoadTexture(string guid, long fileId, string name)
        {
            var identity = Identity("Texture2D", guid, fileId, name);
            if (!Native.IsReady)
            {
                Debug.LogError($"Unbound: {identity} is not available: it is read from the install's resources by the host, which is not connected.");
                return null;
            }
            // The loader uploads the texture with its stored sampler; the managed texture reports
            // that sampler too (and sends it again when a script changes one of its settings).
            (FilterMode Filter, TextureWrapMode U, TextureWrapMode V, TextureWrapMode W, int Aniso, float MipBias) sampler;
            try
            {
                var fields = RecordFields(guid, fileId, Texture2DClassId, "Texture2D");
                var recordName = Str(fields, "m_Name");
                if (recordName != name) throw new FormatException($"the install's object {fileId} is the texture '{recordName}'");
                sampler = SamplerOf(Field(fields, "m_TextureSettings"));
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: {identity} is not available: its record could not be read ({e.Message}).");
                return null;
            }
            var texture = Texture2D.FromSerialized(guid, fileId);
            if (texture == null)
            {
                Debug.LogError($"Unbound: {identity} is not available: the install's texture could not be read; no texture is drawn in its place.");
                return null;
            }
            if (texture.name != name)
            {
                Debug.LogError($"Unbound: {identity} is not available: the install's object {fileId} is the texture '{texture.name}'.");
                Object.DestroyImmediate(texture, true);
                return null;
            }
            texture.SetSamplerQuietly(sampler.Filter, sampler.U, sampler.V, sampler.W, sampler.Aniso, sampler.MipBias);
            if (sampler.MipBias != 0f)
            {
                // The loader's upload carries no mip bias: it goes on its own, as for any texture
                // uploaded with a quiet one.
                try
                {
                    texture.EmitSamplerMetadata();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Unbound: {identity} is not available: its mip bias {sampler.MipBias} could not be set ({e.Message}).");
                    Object.DestroyImmediate(texture, true);
                    return null;
                }
            }
            return texture;
        }

        /// <summary>A texture record's sampler (`m_TextureSettings`), checked: a filter or wrap
        /// mode outside Unity's enums or a negative anisotropic level is not read here. A record
        /// without a mip bias has none.</summary>
        static (FilterMode, TextureWrapMode, TextureWrapMode, TextureWrapMode, int, float) SamplerOf(JsonElement settings)
        {
            int filter = Int(settings, "m_FilterMode");
            if (filter < (int)FilterMode.Point || filter > (int)FilterMode.Trilinear)
                throw new NotSupportedException($"the texture's filter mode {filter} is not read here");
            int aniso = Int(settings, "m_Aniso");
            if (aniso < 0) throw new NotSupportedException($"the texture's anisotropic level {aniso} is not read here");
            float mipBias = settings.TryGetProperty("m_MipBias", out _) ? Float(settings, "m_MipBias") : 0f;
            if (!float.IsFinite(mipBias)) throw new FormatException($"the texture's mip bias {mipBias}");
            return ((FilterMode)filter, Wrap(settings, "m_WrapU"), Wrap(settings, "m_WrapV"), Wrap(settings, "m_WrapW"), aniso, mipBias);
        }

        static TextureWrapMode Wrap(JsonElement settings, string name)
        {
            int wrap = Int(settings, name);
            if (wrap < (int)TextureWrapMode.Repeat || wrap > (int)TextureWrapMode.MirrorOnce)
                throw new NotSupportedException($"the texture's wrap mode {wrap} (`{name}`) is not read here");
            return (TextureWrapMode)wrap;
        }

        /// <summary>The `fields` of the install's object `fileId` as the host's `editor-resource`
        /// call answers it; an error when the host cannot answer or the object is not of class
        /// `classId`.</summary>
        static JsonElement RecordFields(string guid, long fileId, int classId, string className)
        {
            var answer = Native.Call("editor-resource", JsonSerializer.Serialize(new { guid, fileId }));
            using var document = JsonDocument.Parse(answer);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("classId", out var classElement)
                || classElement.ValueKind != JsonValueKind.Number || !classElement.TryGetInt32(out var actual))
                throw new FormatException("the answer names no class");
            if (actual != classId)
                throw new FormatException($"the install's object {fileId} is of class {actual}, not a {className} ({classId})");
            return Field(root, "fields").Clone();
        }

        /// <summary>The installed Sprite `fileId` of `unity_builtin_extra` from its record: its
        /// texture (the record's reference, shared with every other reference to it), rect,
        /// pivot, pixels per unit, border, mesh, texture rect and physics shapes. Null, with an
        /// error naming it, when the record cannot be read or is not supported.</summary>
        internal static Sprite LoadSprite(string guid, long fileId, string name)
        {
            var identity = Identity("Sprite", guid, fileId, name);
            if (!Native.IsReady)
            {
                Debug.LogError($"Unbound: {identity} is not available: it is read from the install's resources by the host, which is not connected.");
                return null;
            }
            Record entry;
            try
            {
                entry = Record.Read(RecordFields(guid, fileId, SpriteClassId, "Sprite"));
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: {identity} is not available: its record could not be read ({e.Message}).");
                return null;
            }
            if (entry.Name != name)
            {
                Debug.LogError($"Unbound: {identity} is not available: the install's object {fileId} is the Sprite '{entry.Name}'.");
                return null;
            }

            var texture = TextureOf(entry, identity);
            if (texture == null) return null;
            if (entry.Rect.x < 0 || entry.Rect.y < 0 || entry.Rect.xMax > texture.width || entry.Rect.yMax > texture.height)
            {
                Debug.LogError($"Unbound: {identity} is not available: its rect {entry.Rect} lies outside its {texture.width}x{texture.height} texture.");
                return null;
            }
            // The record's mesh positions are in units from the pivot; its uvTransform maps them to
            // texture pixels (x * scale + offset), from which the uvs follow.
            var uv = new Vector2[entry.Vertices.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                var v = entry.Vertices[i];
                uv[i] = new Vector2((v.x * entry.UvTransform.x + entry.UvTransform.y) / texture.width,
                    (v.y * entry.UvTransform.z + entry.UvTransform.w) / texture.height);
            }
            return Sprite.FromImport(texture, entry.Name, entry.Rect, entry.Pivot, entry.Border, entry.PixelsPerUnit, entry.MeshType,
                entry.Vertices, uv, entry.Triangles, entry.TextureRect, entry.PhysicsShape);
        }

        /// <summary>The texture the record references, made (or found) as the built-in object it is.</summary>
        static Texture2D TextureOf(Record entry, string identity)
        {
            var guid = entry.TextureGuid;
            long fileId = entry.TextureFileId;
            bool extra = guid == BuiltinShaders.ExtraGuid;
            var names = extra ? BuiltinShaders.ExtraNames : BuiltinShaders.DefaultNames;
            var classes = extra ? BuiltinObjects.ExtraClasses : BuiltinObjects.DefaultClasses;
            if (!(extra || guid == BuiltinShaders.DefaultGuid) || !names.TryGetValue(fileId, out var textureName) || !classes.TryGetValue(fileId, out var classId))
            {
                Debug.LogError($"Unbound: {identity} is not available: its texture {{fileID: {fileId}, guid: {guid}}} is not a built-in object of the release profile.");
                return null;
            }
            if (classId != Texture2DClassId)
            {
                Debug.LogError($"Unbound: {identity} is not available: its texture {{fileID: {fileId}, guid: {guid}}} ('{textureName}') is of class {classId}, not a Texture2D ({Texture2DClassId}).");
                return null;
            }
            if (!Reads(guid, textureName))
            {
                Debug.LogError($"Unbound: {identity} is not available: its texture '{textureName}' ({{fileID: {fileId}, guid: {guid}}}) is not one read from the install.");
                return null;
            }
            if (BuiltinObjects.Get(guid, fileId, textureName) is Texture2D texture) return texture;
            Debug.LogError($"Unbound: {identity} is not available: its texture '{textureName}' ({{fileID: {fileId}, guid: {guid}}}) could not be read.");
            return null;
        }

        /// <summary>The fields of a Sprite record this reader uses, checked.</summary>
        sealed class Record
        {
            internal string Name;
            internal Rect Rect;
            internal Vector2 Pivot;
            internal Vector4 Border;
            internal float PixelsPerUnit;
            internal int MeshType;
            internal string TextureGuid;
            internal long TextureFileId;
            internal Rect TextureRect;
            internal Vector4 UvTransform;
            internal Vector2[] Vertices;
            internal ushort[] Triangles;
            internal Vector2[][] PhysicsShape;

            internal static Record Read(JsonElement fields)
            {
                var r = new Record
                {
                    Name = Str(fields, "m_Name"),
                    Rect = RectOf(Field(fields, "m_Rect")),
                    Pivot = V2(Field(fields, "m_Pivot")),
                    Border = V4(Field(fields, "m_Border")),
                    PixelsPerUnit = Float(fields, "m_PixelsToUnits"),
                };
                if (!(r.PixelsPerUnit > 0)) throw new FormatException($"pixels per unit {r.PixelsPerUnit}");
                if (Reference(Field(fields, "m_SpriteAtlas"), out _, out _))
                    throw new NotSupportedException("the sprite belongs to a sprite atlas, which is not read here");
                if (Field(fields, "m_Bones").GetArrayLength() != 0)
                    throw new NotSupportedException("the sprite has bones, which are not read here");

                var rd = Field(fields, "m_RD");
                if (!Reference(Field(rd, "texture"), out r.TextureGuid, out r.TextureFileId))
                    throw new FormatException("the sprite has no texture");
                if (Reference(Field(rd, "alphaTexture"), out _, out _))
                    throw new NotSupportedException("the sprite has an alpha texture, which is not read here");
                if (Field(rd, "secondaryTextures").GetArrayLength() != 0)
                    throw new NotSupportedException("the sprite has secondary textures, which are not read here");
                if (Field(rd, "m_Bindpose").GetArrayLength() != 0)
                    throw new NotSupportedException("the sprite has bind poses, which are not read here");
                if (Float(rd, "downscaleMultiplier") != 1f)
                    throw new NotSupportedException($"the sprite's texture is downscaled ({Str(rd, "downscaleMultiplier")}), which is not read here");
                // settingsRaw: bit 0 packed, bit 6 the mesh type (0 FullRect, 1 Tight).
                var settings = uint.Parse(Str(rd, "settingsRaw"), NumberStyles.Integer, CultureInfo.InvariantCulture);
                if ((settings & 1) != 0) throw new NotSupportedException("the sprite is packed, which is not read here");
                r.MeshType = (int)((settings >> 6) & 1);
                r.TextureRect = RectOf(Field(rd, "textureRect"));
                r.UvTransform = V4(Field(rd, "uvTransform"));
                if (!(r.UvTransform.x > 0 && r.UvTransform.z > 0)) throw new FormatException($"uv transform {r.UvTransform}");

                r.Vertices = Positions(Field(rd, "m_VertexData"));
                r.Triangles = Indices(rd, r.Vertices.Length);

                var shapes = new List<Vector2[]>();
                foreach (var shape in Field(fields, "m_PhysicsShape").EnumerateArray())
                {
                    var points = new Vector2[shape.GetArrayLength()];
                    int i = 0;
                    foreach (var p in shape.EnumerateArray()) points[i++] = V2(p);
                    shapes.Add(points);
                }
                r.PhysicsShape = shapes.Count == 0 ? null : shapes.ToArray();
                return r;
            }

            /// <summary>The vertex positions (x, y) of the record's vertex data: its channels, each
            /// stream's vertices one after the other, the streams in order, each starting at a
            /// multiple of 16 bytes. The position channel must be 32-bit floats.</summary>
            static Vector2[] Positions(JsonElement vertexData)
            {
                int count = Int(vertexData, "m_VertexCount");
                if (count <= 0) throw new FormatException("the sprite mesh has no vertices");
                var data = Bytes(vertexData, "m_DataSize");
                var channels = new List<(int Stream, int Offset, int Format, int Dimension)>();
                foreach (var c in Field(vertexData, "m_Channels").EnumerateArray())
                    channels.Add((Int(c, "stream"), Int(c, "offset"), Int(c, "format"), Int(c, "dimension") & 0xF));
                return SpriteRenderGeometry.Positions(count, data, channels);
            }

            /// <summary>The triangles of the record's one submesh (16-bit indices).</summary>
            static ushort[] Indices(JsonElement rd, int vertexCount)
            {
                var submeshes = Field(rd, "m_SubMeshes");
                if (submeshes.GetArrayLength() != 1)
                    throw new NotSupportedException($"the sprite mesh has {submeshes.GetArrayLength()} submeshes");
                JsonElement sub = default;
                foreach (var s in submeshes.EnumerateArray()) sub = s;
                if (Int(sub, "topology") != 0) throw new NotSupportedException($"the sprite mesh's topology is {Int(sub, "topology")}, not triangles");
                if (Int(sub, "baseVertex") != 0) throw new NotSupportedException("the sprite mesh has a base vertex");
                int first = Int(sub, "firstByte"), count = Int(sub, "indexCount");
                var bytes = Bytes(rd, "m_IndexBuffer");
                return SpriteRenderGeometry.Indices(bytes, first, count, vertexCount);
            }
        }

        static JsonElement Field(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : throw new FormatException($"no `{name}`");

        static string Str(JsonElement e, string name)
        {
            var v = Field(e, name);
            return v.ValueKind == JsonValueKind.String ? v.GetString() : throw new FormatException($"`{name}` is not a value");
        }

        static float Float(JsonElement e, string name) => float.Parse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture);

        static int Int(JsonElement e, string name) => int.Parse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture);

        static byte[] Bytes(JsonElement e, string name)
        {
            var hex = Str(e, name);
            return hex.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(hex);
        }

        static Vector2 V2(JsonElement e) => new Vector2(Float(e, "x"), Float(e, "y"));

        static Vector4 V4(JsonElement e) => new Vector4(Float(e, "x"), Float(e, "y"), Float(e, "z"), Float(e, "w"));

        static Rect RectOf(JsonElement e) => new Rect(Float(e, "x"), Float(e, "y"), Float(e, "width"), Float(e, "height"));

        /// <summary>A reference as the host answers it: `{fileID, guid}`, or `{fileID: 0}` for none.</summary>
        static bool Reference(JsonElement e, out string guid, out long fileId)
        {
            guid = null;
            fileId = 0;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("fileID", out var id) || !id.TryGetInt64(out fileId))
                throw new FormatException("a reference without a fileID");
            if (fileId == 0) return false;
            guid = e.TryGetProperty("guid", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString()?.ToLowerInvariant() : null;
            if (string.IsNullOrEmpty(guid)) throw new FormatException($"the reference to {fileId} has no file");
            return true;
        }
    }
}
