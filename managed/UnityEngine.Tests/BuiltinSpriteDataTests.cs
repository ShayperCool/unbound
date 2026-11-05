using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The built-in uGUI sprites of `unity_builtin_extra` (UISprite, Background, Knob, Checkmark,
    /// DropdownArrow) and their textures are the install's own objects: the Sprite record comes
    /// from the host's `editor-resource` call by `{guid, fileId}`, its texture through the
    /// serialized Texture2D loader (`load-texture-object`) by the record's texture reference.
    /// A stand-in host answers both calls in the shape the host's type-tree reader gives
    /// (scalars as strings, byte arrays as hex, references as `{fileID, guid}`).
    ///
    /// Provenance of the values: the ids, classes, names, texture sizes and mip counts, rects,
    /// pivots, pixels per unit, borders, texture rects, uv transforms, `settingsRaw`, vertex,
    /// index and physics-point counts in <see cref="s_Installed"/> are the 6000.0.71f1 install's
    /// `unity_builtin_extra` records (a Native asset-state observation of the resource file read
    /// as data, not a measured API readout; the host's own reading of the file is tested on the
    /// Rust side). The mesh positions, physics points, index order and image data are NOT the
    /// install's: they are generated stand-ins of the recorded counts (no asset payload is kept
    /// here). The malformed and unsupported records are mechanism stand-ins, not claims about
    /// Unity.
    /// </summary>
    public unsafe class BuiltinSpriteDataTests : IDisposable
    {
        const string Extra = BuiltinShaders.ExtraGuid;
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", PrivateStatic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", PrivateStatic);
        static readonly IDictionary Objects = (IDictionary)typeof(BuiltinObjects).GetField("s_Objects", PrivateStatic).GetValue(null);

        /// <summary>One installed sprite and its texture (6000.0.71f1 `unity_builtin_extra`).</summary>
        sealed class Installed
        {
            internal long Sprite, Texture;
            internal int TextureWidth, TextureHeight, Mips;
            internal Rect Rect;
            internal float Border, PixelsPerUnit;
            internal Rect TextureRect;
            internal Vector4 UvTransform;
            internal uint SettingsRaw;
            internal int Vertices, Indices, PhysicsPoints;
        }

        static readonly Dictionary<string, Installed> s_Installed = new Dictionary<string, Installed>
        {
            ["UISprite"] = new Installed
            {
                Sprite = 10905, Texture = 10904, TextureWidth = 32, TextureHeight = 32, Mips = 6,
                Rect = new Rect(0f, 0f, 32f, 32f), Border = 10f, PixelsPerUnit = 200f,
                TextureRect = new Rect(0.026750982f, 0.05131668f, 31.946499f, 31.897366f),
                UvTransform = new Vector4(200f, 16f, 200f, 16f), SettingsRaw = 64, Vertices = 7, Indices = 15, PhysicsPoints = 6,
            },
            ["Background"] = new Installed
            {
                Sprite = 10907, Texture = 10906, TextureWidth = 32, TextureHeight = 32, Mips = 6,
                Rect = new Rect(0f, 0f, 32f, 32f), Border = 10f, PixelsPerUnit = 200f,
                TextureRect = new Rect(0.026750982f, 0.05131668f, 31.946499f, 31.897366f),
                UvTransform = new Vector4(200f, 16f, 200f, 16f), SettingsRaw = 64, Vertices = 7, Indices = 15, PhysicsPoints = 6,
            },
            ["Knob"] = new Installed
            {
                Sprite = 10913, Texture = 10912, TextureWidth = 64, TextureHeight = 64, Mips = 7,
                Rect = new Rect(12f, 12f, 40f, 40f), Border = 0f, PixelsPerUnit = 200f,
                TextureRect = new Rect(12.026751f, 12.026751f, 39.946495f, 39.946495f),
                UvTransform = new Vector4(200f, 32f, 200f, 32f), SettingsRaw = 64, Vertices = 9, Indices = 21, PhysicsPoints = 9,
            },
            ["Checkmark"] = new Installed
            {
                Sprite = 10901, Texture = 10900, TextureWidth = 64, TextureHeight = 64, Mips = 7,
                Rect = new Rect(12f, 12f, 40f, 40f), Border = 0f, PixelsPerUnit = 200f,
                TextureRect = new Rect(19.07612f, 20.07612f, 26.861765f, 22.84776f),
                UvTransform = new Vector4(200f, 32f, 200f, 32f), SettingsRaw = 64, Vertices = 9, Indices = 21, PhysicsPoints = 7,
            },
            ["DropdownArrow"] = new Installed
            {
                Sprite = 10915, Texture = 10914, TextureWidth = 64, TextureHeight = 64, Mips = 7,
                Rect = new Rect(12f, 12f, 40f, 40f), Border = 0f, PixelsPerUnit = 200f,
                TextureRect = new Rect(20.07612f, 23.07612f, 23.84776f, 15.847759f),
                UvTransform = new Vector4(200f, 32f, 200f, 32f), SettingsRaw = 64, Vertices = 6, Indices = 12, PhysicsPoints = 6,
            },
        };

        /// <summary>The stand-in host's answers by (method, guid, fileId): status 0 with JSON, else an error.</summary>
        static readonly Dictionary<(string, string, long), (int Status, string Answer)> s_Answers = new Dictionary<(string, string, long), (int, string)>();
        /// <summary>Every native call in order: "method guid fileId" for the two answered calls, else the method.</summary>
        static readonly List<string> s_Calls = new List<string>();

        readonly object m_PreviousApi = Api.GetValue(null), m_PreviousReady = Ready.GetValue(null);
        readonly List<(long Id, string Name, int Class, bool Had)> m_Registered = new List<(long, string, int, bool)>();
        readonly HashSet<object> m_Objects;

        public BuiltinSpriteDataTests()
        {
            m_Objects = new HashSet<object>(Objects.Keys.Cast<object>());
            s_Answers.Clear();
            s_Calls.Clear();
            var api = new NativeApi
            {
                Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free, SubmitCommands = &Submit,
                ReleaseMaterial = &Release, ReleaseTexture = &Release, ReleaseMesh = &Release,
                // The drawn textures of the other built-in sprites upload their pixels.
                UploadTexture = &Upload,
            };
            Native.Initialize(&api);
            // The release profile's table rows of the built-in UI objects (data/profiles/builtin-6000.0.json).
            foreach (var (name, row) in s_Installed)
            {
                Register(row.Texture, name, 28);
                Register(row.Sprite, name, 213);
            }
            Register(10910, "InputFieldBackground", 28);
            Register(10911, "InputFieldBackground", 213);
        }

        public void Dispose()
        {
            foreach (var key in Objects.Keys.Cast<object>().Where(k => !m_Objects.Contains(k)).ToList())
            {
                var obj = (Object)Objects[key];
                Objects.Remove(key);
                if (obj) Object.DestroyImmediate(obj, true);
            }
            for (int i = m_Registered.Count - 1; i >= 0; i--)
            {
                var r = m_Registered[i];
                if (r.Had)
                {
                    BuiltinShaders.ExtraNames[r.Id] = r.Name;
                    BuiltinObjects.ExtraClasses[r.Id] = r.Class;
                }
                else
                {
                    BuiltinShaders.ExtraNames.Remove(r.Id);
                    BuiltinObjects.ExtraClasses.Remove(r.Id);
                }
            }
            Api.SetValue(null, m_PreviousApi);
            Ready.SetValue(null, m_PreviousReady);
            s_Answers.Clear();
            s_Calls.Clear();
        }

        void Register(long fileId, string name, int classId)
        {
            bool had = BuiltinShaders.ExtraNames.TryGetValue(fileId, out var oldName) & BuiltinObjects.ExtraClasses.TryGetValue(fileId, out var oldClass);
            m_Registered.Add((fileId, oldName, oldClass, had));
            BuiltinShaders.ExtraNames[fileId] = name;
            BuiltinObjects.ExtraClasses[fileId] = classId;
        }

        [UnmanagedCallersOnly]
        static int Call(byte* method, int methodLength, byte* request, int requestLength, NativeBuffer* response)
        {
            int status;
            string result;
            var name = Encoding.UTF8.GetString(method, methodLength);
            try
            {
                if (name == "editor-resource" || name == "load-texture-object")
                {
                    using var input = JsonDocument.Parse(Encoding.UTF8.GetString(request, requestLength));
                    var guid = input.RootElement.GetProperty("guid").GetString();
                    long fileId = input.RootElement.GetProperty("fileId").GetInt64();
                    s_Calls.Add(name + " " + guid + " " + fileId);
                    (status, result) = s_Answers.TryGetValue((name, guid, fileId), out var answer) ? answer : (1, "no object " + fileId);
                }
                else
                {
                    s_Calls.Add(name);
                    (status, result) = (1, "not answered by this stand-in host");
                }
            }
            catch (Exception e)
            {
                (status, result) = (1, e.Message);
            }
            var bytes = Encoding.UTF8.GetBytes(result);
            response->Data = (byte*)NativeMemory.Alloc((nuint)Math.Max(1, bytes.Length));
            response->Length = bytes.Length;
            bytes.CopyTo(new Span<byte>(response->Data, bytes.Length));
            return status;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Release(ulong handle) { }

        [UnmanagedCallersOnly]
        static void Upload(ulong handle, TexturePacket* packet) { }

        static int Asked(string method, long fileId) => s_Calls.Count(c => c == method + " " + Extra + " " + fileId);

        static string S(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        static JsonObject Xy(float x, float y) => new JsonObject { ["x"] = S(x), ["y"] = S(y) };

        static JsonObject Xyzw(Vector4 v) => new JsonObject { ["x"] = S(v.x), ["y"] = S(v.y), ["z"] = S(v.z), ["w"] = S(v.w) };

        static JsonObject RectNode(Rect r) => new JsonObject { ["x"] = S(r.x), ["y"] = S(r.y), ["width"] = S(r.width), ["height"] = S(r.height) };

        static JsonObject Reference(long fileId) => fileId == 0 ? new JsonObject { ["fileID"] = 0 } : new JsonObject { ["fileID"] = fileId, ["guid"] = Extra };

        static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        /// <summary>The generated stand-in mesh of a recorded vertex count: points on an ellipse
        /// inside the rect, in units from the (centred) pivot, and a triangle fan.</summary>
        static (Vector2[] Positions, ushort[] Triangles) StandInMesh(Installed row)
        {
            var positions = new Vector2[row.Vertices];
            for (int i = 0; i < positions.Length; i++)
            {
                double angle = 2 * Math.PI * i / positions.Length;
                positions[i] = new Vector2((float)(Math.Cos(angle) * 0.4 * row.Rect.width / row.PixelsPerUnit),
                    (float)(Math.Sin(angle) * 0.4 * row.Rect.height / row.PixelsPerUnit));
            }
            var triangles = new List<ushort>();
            for (int i = 1; i + 1 < positions.Length; i++) triangles.AddRange(new[] { (ushort)0, (ushort)i, (ushort)(i + 1) });
            return (positions, triangles.ToArray());
        }

        /// <summary>The vertex data in the record's layout: positions (3 floats) in stream 0, the
        /// uv channel (2 floats) in stream 1 starting at a multiple of 16 bytes.</summary>
        static JsonObject VertexData(Vector2[] positions, int positionFormat = 0)
        {
            int count = positions.Length;
            int uvStart = (count * 12 + 15) & ~15;
            var data = new byte[uvStart + count * 8];
            for (int v = 0; v < count; v++)
            {
                BitConverter.GetBytes(positions[v].x).CopyTo(data, v * 12);
                BitConverter.GetBytes(positions[v].y).CopyTo(data, v * 12 + 4);
            }
            var channels = new JsonArray();
            for (int c = 0; c < 14; c++)
            {
                int stream = c == 4 ? 1 : 0, format = c == 0 ? positionFormat : 0, dimension = c == 0 ? 3 : c == 4 ? 2 : 0;
                channels.Add(new JsonObject { ["stream"] = stream.ToString(CultureInfo.InvariantCulture), ["offset"] = "0", ["format"] = format.ToString(CultureInfo.InvariantCulture), ["dimension"] = dimension.ToString(CultureInfo.InvariantCulture) });
            }
            return new JsonObject { ["m_VertexCount"] = count.ToString(CultureInfo.InvariantCulture), ["m_Channels"] = channels, ["m_DataSize"] = Hex(data) };
        }

        /// <summary>The record the host answers for an installed sprite (with stand-in mesh and physics points).</summary>
        static JsonObject Record(string name)
        {
            var row = s_Installed[name];
            var (positions, triangles) = StandInMesh(row);
            var indexBytes = new byte[triangles.Length * 2];
            for (int i = 0; i < triangles.Length; i++) BitConverter.GetBytes(triangles[i]).CopyTo(indexBytes, i * 2);
            var shape = new JsonArray();
            for (int i = 0; i < row.PhysicsPoints; i++) shape.Add(Xy(positions[i % positions.Length].x, positions[i % positions.Length].y));
            var fields = new JsonObject
            {
                ["m_ObjectHideFlags"] = "8",
                ["m_Name"] = name,
                ["m_Rect"] = RectNode(row.Rect),
                ["m_Offset"] = Xy(0f, 0f),
                ["m_Border"] = Xyzw(new Vector4(row.Border, row.Border, row.Border, row.Border)),
                ["m_PixelsToUnits"] = S(row.PixelsPerUnit),
                ["m_Pivot"] = Xy(0.5f, 0.5f),
                ["m_Extrude"] = "1",
                ["m_IsPolygon"] = "false",
                ["m_AtlasName"] = "",
                ["m_PackingTag"] = "",
                ["m_AtlasTags"] = new JsonArray(),
                ["m_SpriteAtlas"] = Reference(0),
                ["m_RD"] = new JsonObject
                {
                    ["texture"] = Reference(row.Texture),
                    ["alphaTexture"] = Reference(0),
                    ["secondaryTextures"] = new JsonArray(),
                    ["m_SubMeshes"] = new JsonArray(new JsonObject
                    {
                        ["firstByte"] = "0", ["indexCount"] = row.Indices.ToString(CultureInfo.InvariantCulture), ["topology"] = "0",
                        ["baseVertex"] = "0", ["firstVertex"] = "0", ["vertexCount"] = row.Vertices.ToString(CultureInfo.InvariantCulture),
                    }),
                    ["m_IndexBuffer"] = Hex(indexBytes),
                    ["m_VertexData"] = VertexData(positions),
                    ["m_Bindpose"] = new JsonArray(),
                    ["textureRect"] = RectNode(row.TextureRect),
                    ["textureRectOffset"] = Xy(row.TextureRect.x - row.Rect.x, row.TextureRect.y - row.Rect.y),
                    ["atlasRectOffset"] = Xy(-1f, -1f),
                    ["settingsRaw"] = row.SettingsRaw.ToString(CultureInfo.InvariantCulture),
                    ["uvTransform"] = Xyzw(row.UvTransform),
                    ["downscaleMultiplier"] = S(1f),
                },
                ["m_PhysicsShape"] = new JsonArray(shape),
                ["m_Bones"] = new JsonArray(),
                ["m_ScriptableObjects"] = new JsonArray(),
                ["m_SpriteID"] = "",
            };
            return new JsonObject { ["classId"] = 213, ["fields"] = fields };
        }

        static string TextureAnswer(string name, int width, int height, int mips) =>
            new JsonObject
            {
                ["name"] = name, ["width"] = width, ["height"] = height, ["mipCount"] = mips,
                ["dimension"] = 2, ["rgbm"] = false, ["linear"] = false,
            }.ToJsonString();

        /// <summary>The host answers the installed sprite `name` (record and texture), the record
        /// changed by <paramref name="change"/>.</summary>
        static void Answer(string name, Action<JsonObject> change = null)
        {
            var row = s_Installed[name];
            var record = Record(name);
            change?.Invoke(record);
            s_Answers[("editor-resource", Extra, row.Sprite)] = (0, record.ToJsonString());
            s_Answers[("load-texture-object", Extra, row.Texture)] = (0, TextureAnswer(name, row.TextureWidth, row.TextureHeight, row.Mips));
        }

        static JsonObject Fields(JsonObject record) => (JsonObject)record["fields"];

        static JsonObject Rd(JsonObject record) => (JsonObject)Fields(record)["m_RD"];

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Errors;

            internal CaptureHandler(ILogHandler inner, List<string> errors)
            {
                m_Inner = inner;
                m_Errors = errors;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error)
                    m_Errors.Add(args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        static List<string> Errors(Action action)
        {
            var errors = new List<string>();
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            bool previousLogEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            try
            {
                logger.logHandler = new CaptureHandler(previousHandler, errors);
                logger.logEnabled = true;
                logger.filterLogType = LogType.Log;
                action();
            }
            finally
            {
                logger.logHandler = previousHandler;
                logger.logEnabled = previousLogEnabled;
                logger.filterLogType = previousFilter;
            }
            return errors;
        }

        /// <summary>No object was made for `fileId` (nothing drawn in its place).</summary>
        static void NothingMadeFor(long fileId) => Assert.DoesNotContain(Objects.Keys.Cast<object>(), k => k.Equals((Extra, fileId)));

        // ------------------------------------------------------------------ installed records

        /// <summary>Red on 8ece7bb2 (assertion): the host is never asked for the record
        /// (editor-resource Asked 0); the sprite is drawn instead (pixels per unit 100, rect the
        /// whole drawn 32x32 texture, borders by name). Each row is the install's record.</summary>
        [Theory]
        [InlineData("UISprite")]
        [InlineData("Background")]
        [InlineData("Knob")]
        [InlineData("Checkmark")]
        [InlineData("DropdownArrow")]
        public void TheSelectedSpritesCarryTheirInstalledRecords(string name)
        {
            var row = s_Installed[name];
            Answer(name);
            var sprite = BuiltinShaders.Resolve(Extra, row.Sprite) as Sprite;
            Assert.Equal(1, Asked("editor-resource", row.Sprite));
            Assert.NotNull(sprite);
            Assert.Equal(name, sprite.name);
            Assert.Equal(row.Rect, sprite.rect);
            Assert.Equal(new Vector2(0.5f * row.Rect.width, 0.5f * row.Rect.height), sprite.pivot);
            Assert.Equal(row.PixelsPerUnit, sprite.pixelsPerUnit);
            Assert.Equal(new Vector4(row.Border, row.Border, row.Border, row.Border), sprite.border);
            Assert.Equal(row.TextureRect, sprite.textureRect);
            Assert.Equal(new Vector2(row.TextureRect.x - row.Rect.x, row.TextureRect.y - row.Rect.y), sprite.textureRectOffset);
            Assert.Equal(1, sprite.GetPhysicsShapeCount());
            Assert.Equal(row.PhysicsPoints, sprite.GetPhysicsShapePointCount(0));
            Assert.Equal(1, Asked("load-texture-object", row.Texture));
            Assert.Same(BuiltinShaders.Resolve(Extra, row.Texture), sprite.texture);
            Assert.Equal(row.TextureWidth, sprite.texture.width);
            Assert.Equal(row.TextureHeight, sprite.texture.height);
        }

        /// <summary>Red on 8ece7bb2 (assertion): the drawn sprite has the 4-vertex quad of its
        /// rect, not the record's mesh. The vertices and triangles are the record's (stand-in
        /// positions and fan of the recorded counts); the uvs follow the record's uvTransform
        /// (units to texture pixels) over the texture's size; `settingsRaw` 64 is a Tight mesh.</summary>
        [Theory]
        [InlineData("UISprite")]
        [InlineData("Background")]
        [InlineData("Knob")]
        [InlineData("Checkmark")]
        [InlineData("DropdownArrow")]
        public void TheSpriteMeshIsTheRecordsMesh(string name)
        {
            var row = s_Installed[name];
            Answer(name);
            var sprite = (Sprite)BuiltinShaders.Resolve(Extra, row.Sprite);
            var (positions, triangles) = StandInMesh(row);
            Assert.Equal(positions, sprite.vertices);
            Assert.Equal(triangles, sprite.triangles);
            var uv = sprite.uv;
            Assert.Equal(positions.Length, uv.Length);
            for (int i = 0; i < uv.Length; i++)
            {
                Assert.Equal((positions[i].x * row.UvTransform.x + row.UvTransform.y) / row.TextureWidth, uv[i].x, 5);
                Assert.Equal((positions[i].y * row.UvTransform.z + row.UvTransform.w) / row.TextureHeight, uv[i].y, 5);
                // The installed uvTransform puts the pivot at rect.position + pivot * rect.size.
                Assert.Equal((row.Rect.x + 0.5f * row.Rect.width + positions[i].x * row.PixelsPerUnit) / row.TextureWidth, uv[i].x, 5);
            }
            Assert.Equal(SpriteMeshType.Tight, sprite.m_MeshType);
        }

        /// <summary>Red on 8ece7bb2 (assertion: load-texture-object Asked 0, the texture is drawn,
        /// 32x32 and readable). Each selected texture is the install's, through the serialized
        /// Texture2D loader, not readable (as stored).</summary>
        [Theory]
        [InlineData("UISprite")]
        [InlineData("Background")]
        [InlineData("Knob")]
        [InlineData("Checkmark")]
        [InlineData("DropdownArrow")]
        public void TheSelectedTexturesAreTheInstallsNotDrawn(string name)
        {
            var row = s_Installed[name];
            Answer(name);
            var texture = BuiltinShaders.Resolve(Extra, row.Texture) as Texture2D;
            Assert.Equal(1, Asked("load-texture-object", row.Texture));
            Assert.NotNull(texture);
            Assert.Equal(name, texture.name);
            Assert.Equal(row.TextureWidth, texture.width);
            Assert.Equal(row.TextureHeight, texture.height);
            Assert.Equal(row.Mips, texture.mipmapCount);
            Assert.False(texture.isReadable);
            Assert.Equal(0, Asked("editor-resource", row.Texture));
        }

        /// <summary>Red on 8ece7bb2 (assertion: load-texture-object Asked 0). The texture a
        /// reference resolves to and the sprite's are one object, read once; the sprite is made
        /// once and handed out again without asking again, whichever is asked for first.</summary>
        [Fact]
        public void TheTextureIsSharedWithItsOwnReferenceAndBothAreReadOnce()
        {
            Answer("Knob");
            Answer("UISprite");
            var knobTexture = BuiltinShaders.Resolve(Extra, 10912);
            var knob = (Sprite)BuiltinShaders.Resolve(Extra, 10913);
            var uiSprite = (Sprite)BuiltinShaders.Resolve(Extra, 10905);
            var uiTexture = BuiltinShaders.Resolve(Extra.ToUpperInvariant(), 10904);
            Assert.Equal(1, Asked("load-texture-object", 10912));
            Assert.Equal(1, Asked("load-texture-object", 10904));
            Assert.Same(knobTexture, knob.texture);
            Assert.Same(uiTexture, uiSprite.texture);
            Assert.Same(knob, BuiltinShaders.Resolve(Extra, 10913));
            Assert.Same(uiSprite, BuiltinObjects.Get(Extra.ToUpperInvariant(), 10905, "UISprite"));
            Assert.Equal(1, Asked("editor-resource", 10913));
            Assert.Equal(1, Asked("editor-resource", 10905));
        }

        // ------------------------------------------------------------------ loud failures

        /// <summary>Red on 8ece7bb2 (assertion: a drawn sprite is returned). A record the host
        /// cannot answer (missing object) is an error naming the sprite; its texture is not read
        /// and nothing is made in its place.</summary>
        [Theory]
        [InlineData("UISprite")]
        [InlineData("Background")]
        [InlineData("Knob")]
        [InlineData("Checkmark")]
        [InlineData("DropdownArrow")]
        public void AMissingRecordIsAnErrorAndNothingIsDrawn(string name)
        {
            var row = s_Installed[name];
            Object sprite = null;
            var errors = Errors(() => sprite = BuiltinShaders.Resolve(Extra, row.Sprite));
            Assert.Null(sprite);
            Assert.Contains(errors, e => e.Contains($"built-in Sprite '{name}' ({{fileID: {row.Sprite}, guid: {Extra}}})") && e.Contains("no object " + row.Sprite));
            Assert.Equal(1, Asked("editor-resource", row.Sprite));
            Assert.Equal(0, Asked("load-texture-object", row.Texture));
            NothingMadeFor(row.Sprite);
            NothingMadeFor(row.Texture);
        }

        /// <summary>Red on 8ece7bb2 (assertion: a drawn sprite and texture are returned). Without
        /// the host nothing is read and nothing is drawn: both are errors naming the object.</summary>
        [Fact]
        public void WithoutTheHostNeitherIsMade()
        {
            Ready.SetValue(null, false);
            Object sprite = null, texture = null;
            var errors = Errors(() =>
            {
                sprite = BuiltinShaders.Resolve(Extra, 10905);
                texture = BuiltinShaders.Resolve(Extra, 10912);
            });
            Assert.Null(sprite);
            Assert.Null(texture);
            Assert.Contains(errors, e => e.Contains("built-in Sprite 'UISprite' (") && e.Contains("not connected"));
            Assert.Contains(errors, e => e.Contains("built-in Texture2D 'Knob' (") && e.Contains("not connected"));
            NothingMadeFor(10905);
            NothingMadeFor(10912);
        }

        /// <summary>Red on 8ece7bb2 (assertion: a drawn sprite and texture are returned). A
        /// texture the loader cannot read is an error; the sprite referencing it is not made.</summary>
        [Fact]
        public void AnUnreadableTextureIsAnErrorAndNotDrawn()
        {
            Answer("Checkmark");
            s_Answers[("load-texture-object", Extra, 10900)] = (1, "reading texture 10900: the texture's image data is streamed from another file, which is not read yet");
            Object sprite = null, texture = null;
            var errors = Errors(() =>
            {
                sprite = BuiltinShaders.Resolve(Extra, 10901);
                texture = BuiltinShaders.Resolve(Extra, 10900);
            });
            Assert.Null(sprite);
            Assert.Null(texture);
            Assert.Contains(errors, e => e.Contains("built-in Texture2D 'Checkmark' ({fileID: 10900, guid: " + Extra + "})"));
            Assert.Contains(errors, e => e.Contains("built-in Sprite 'Checkmark' ({fileID: 10901, guid: " + Extra + "})"));
            NothingMadeFor(10900);
            NothingMadeFor(10901);
        }

        /// <summary>Red on 8ece7bb2 (assertion: a drawn texture is returned). A loaded texture
        /// that is not the one the profile names is an error, not that texture.</summary>
        [Fact]
        public void ATextureOfAnotherNameIsAnError()
        {
            Answer("DropdownArrow");
            s_Answers[("load-texture-object", Extra, 10914)] = (0, TextureAnswer("Knob", 64, 64, 7));
            Object texture = null;
            var errors = Errors(() => texture = BuiltinShaders.Resolve(Extra, 10914));
            Assert.Null(texture);
            Assert.Contains(errors, e => e.Contains("built-in Texture2D 'DropdownArrow' (") && e.Contains("is the texture 'Knob'"));
            NothingMadeFor(10914);
        }

        /// <summary>Red on 8ece7bb2 (assertion: a drawn sprite is returned, its texture taken at
        /// fileId - 1 whatever the record says). The record's texture reference decides: a
        /// reference outside the profile's table, to an object of another class, or to a
        /// texture not read from the install (which would be drawn) is an error.</summary>
        [Theory]
        [InlineData(99999L, "is not a built-in object of the release profile")]
        [InlineData(10905L, "is of class 213, not a Texture2D (28)")]
        [InlineData(10910L, "its texture 'InputFieldBackground'")]
        public void TheRecordsTextureReferenceMustBeAnInstalledTexture(long textureFileId, string message)
        {
            Answer("Background", r => Rd(r)["texture"] = Reference(textureFileId));
            Object sprite = null;
            var errors = Errors(() => sprite = BuiltinShaders.Resolve(Extra, 10907));
            Assert.Null(sprite);
            Assert.Contains(errors, e => e.Contains("built-in Sprite 'Background' (") && e.Contains(message));
            Assert.Equal(0, Asked("load-texture-object", 10906));
            NothingMadeFor(10907);
            NothingMadeFor(10910);
        }

        /// <summary>Red on 8ece7bb2 (assertion: a drawn sprite is returned). A record the reader
        /// cannot take as it is, the wrong object, or one this reader does not support, is an
        /// error naming the sprite and the reason; nothing is drawn in its place.</summary>
        [Theory]
        [InlineData("class", "is of class 28, not a Sprite (213)")]
        [InlineData("name", "is the Sprite 'Knob'")]
        [InlineData("missing-field", "no `m_Rect`")]
        [InlineData("no-texture", "the sprite has no texture")]
        [InlineData("packed", "the sprite is packed")]
        [InlineData("atlas", "sprite atlas")]
        [InlineData("bones", "has bones")]
        [InlineData("alpha", "alpha texture")]
        [InlineData("secondary", "secondary textures")]
        [InlineData("downscaled", "is downscaled")]
        [InlineData("submeshes", "has 2 submeshes")]
        [InlineData("half-positions", "vertex format 1")]
        [InlineData("index-range", "index 7 of 7 vertices")]
        [InlineData("short-vertices", "is shorter than 7 positions")]
        [InlineData("outside", "lies outside its 32x32 texture")]
        public void AnUnreadableOrUnsupportedRecordIsAnError(string variant, string message)
        {
            Answer("UISprite", r =>
            {
                var fields = Fields(r);
                var rd = Rd(r);
                switch (variant)
                {
                    case "class": r["classId"] = 28; break;
                    case "name": fields["m_Name"] = "Knob"; break;
                    case "missing-field": fields.Remove("m_Rect"); break;
                    case "no-texture": rd["texture"] = Reference(0); break;
                    case "packed": rd["settingsRaw"] = "65"; break;
                    case "atlas": fields["m_SpriteAtlas"] = Reference(10905); break;
                    case "bones": fields["m_Bones"] = new JsonArray(new JsonObject { ["name"] = "b" }); break;
                    case "alpha": rd["alphaTexture"] = Reference(10904); break;
                    case "secondary": rd["secondaryTextures"] = new JsonArray(new JsonObject { ["name"] = "_NormalMap" }); break;
                    case "downscaled": rd["downscaleMultiplier"] = S(0.5f); break;
                    case "submeshes":
                    {
                        var sub = ((JsonArray)rd["m_SubMeshes"])[0].DeepClone();
                        ((JsonArray)rd["m_SubMeshes"]).Add(sub);
                        break;
                    }
                    case "half-positions": rd["m_VertexData"] = VertexData(StandInMesh(s_Installed["UISprite"]).Positions, 1); break;
                    case "index-range":
                    {
                        var bytes = new byte[15 * 2];
                        BitConverter.GetBytes((ushort)7).CopyTo(bytes, 0);
                        rd["m_IndexBuffer"] = Hex(bytes);
                        break;
                    }
                    case "short-vertices": ((JsonObject)rd["m_VertexData"])["m_DataSize"] = Hex(new byte[40]); break;
                    case "outside": fields["m_Rect"] = RectNode(new Rect(8f, 8f, 32f, 32f)); break;
                }
            });
            Object sprite = null;
            var errors = Errors(() => sprite = BuiltinShaders.Resolve(Extra, 10905));
            Assert.Null(sprite);
            Assert.Contains(errors, e => e.Contains("built-in Sprite 'UISprite' ({fileID: 10905, guid: " + Extra + "})") && e.Contains(message));
            NothingMadeFor(10905);
        }

        // ------------------------------------------------------------------ preservation

        /// <summary>Preservation control, not red on 8ece7bb2: a built-in UI sprite outside the
        /// five keeps its path (no record or texture is asked for; the drawn 32x32 texture at
        /// pixels per unit 100 with a 10 pixel border, as before).</summary>
        [Fact]
        public void TheOtherBuiltinSpritesKeepTheirPath()
        {
            var sprite = BuiltinShaders.Resolve(Extra, 10911) as Sprite;
            Assert.NotNull(sprite);
            Assert.Equal("InputFieldBackground", sprite.name);
            Assert.Equal(new Rect(0f, 0f, 32f, 32f), sprite.rect);
            Assert.Equal(100f, sprite.pixelsPerUnit);
            Assert.Equal(new Vector4(10f, 10f, 10f, 10f), sprite.border);
            Assert.Same(BuiltinShaders.Resolve(Extra, 10910), sprite.texture);
            Assert.DoesNotContain(s_Calls, c => c.StartsWith("editor-resource", StringComparison.Ordinal) || c.StartsWith("load-texture-object", StringComparison.Ordinal));
        }
    }
}
