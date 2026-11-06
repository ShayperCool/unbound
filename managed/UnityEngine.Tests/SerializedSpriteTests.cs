using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Committed Editor-authored 6000.0.71f1 inputs. Malformed variants below test
    /// Unbound's rejection policy, not undocumented Unity behaviour.</summary>
    public sealed class SerializedSpriteTests : IDisposable
    {
        readonly Texture2D m_Texture = new Texture2D(64, 64) { name = "Imported", IsPersistentAsset = true };
        readonly string m_Guid;
        readonly List<LoadedFile> m_Loaded = new List<LoadedFile>();

        static List<YDocument> Documents(string name) => UnityYaml.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "SpriteFixtures", name + ".asset")));

        public SerializedSpriteTests()
        {
            m_Guid = Documents("Rectangle")[0].Body.Get("m_RD").Ref("texture").Guid;
            var textureFile = new LoadedFile { Guid = m_Guid, MainObject = m_Texture, Path = "Assets/Imported.png" };
            textureFile.Objects[2800000] = m_Texture;
            AssetDatabaseRuntime.AddLoaded(textureFile);
        }

        public void Dispose()
        {
            foreach (var file in m_Loaded)
                foreach (var asset in file.Objects.Values) Object.DestroyImmediate(asset, true);
            AssetDatabaseRuntime.Invalidate(m_Guid);
            Object.DestroyImmediate(m_Texture, true);
        }

        LoadedFile Load(List<YDocument> docs)
        {
            var file = ObjectGraphBuilder.Build(docs, "Assets/SerializedSprite.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            m_Loaded.Add(file);
            return file;
        }

        [Fact]
        public void AuthoredSubrectangleResolvesTheExactTextureAndKeepsItsValues()
        {
            var sprite = Assert.IsType<Sprite>(Load(Documents("Rectangle")).MainObject);
            Assert.Same(m_Texture, sprite.texture);
            Assert.Equal(new Rect(8, 12, 40, 32), sprite.rect);
            Assert.Equal(new Vector2(8, 24), sprite.pivot);
            Assert.Equal(24, sprite.pixelsPerUnit);
            Assert.Equal(new Vector4(3, 5, 7, 9), sprite.border);
            Assert.Equal(new ushort[] { 0, 1, 2, 2, 1, 3 }, sprite.triangles);
            Assert.Equal(new Vector2(.125f, .6875f), sprite.uv[0]);
            Assert.Equal(5, sprite.GetPhysicsShapePointCount(0));
        }

        [Fact]
        public void AuthoredTightMeshKeepsItsNonrectangularGeometry()
        {
            var sprite = Assert.IsType<Sprite>(Load(Documents("Polygon")).MainObject);
            Assert.Equal(7, sprite.vertices.Length);
            Assert.Equal(new ushort[] { 6, 5, 1, 4, 1, 5, 3, 1, 4, 0, 1, 3, 2, 1, 0 }, sprite.triangles);
            Assert.Equal(new Vector2(.46875f, 1), sprite.uv[0]);
            Assert.Equal(new Vector2(1, 0), sprite.uv[1]);
            Assert.Equal(7, sprite.GetPhysicsShapePointCount(0));
            var mesh = sprite.RenderMesh();
            Assert.Equal(sprite.triangles.Select(i => (int)i), mesh.triangles);
            Object.DestroyImmediate(mesh);
        }

        [Fact]
        public void LocalTextureReferencesAndStoredOffsetsUseTheResolverAndAuthoredData()
        {
            var docs = Documents("Polygon");
            var rd = docs[0].Body.Get("m_RD");
            Set(rd, "texture", YNode.MakeMap(("fileID", YNode.MakeScalar("2800000"))));
            Set(rd, "textureRectOffset", YNode.MakeMap(("x", YNode.MakeScalar("3")), ("y", YNode.MakeScalar("4"))));
            var file = ObjectGraphBuilder.Build(docs, "Assets/local.asset", null, ObjectGraphBuilder.Kind.Asset, null,
                f => f.Objects[2800000] = m_Texture);
            m_Loaded.Add(file);
            var sprite = Assert.IsType<Sprite>(file.MainObject);
            Assert.Same(m_Texture, sprite.texture);
            Assert.Equal(new Vector2(3, 4), sprite.textureRectOffset);
        }

        [Fact]
        public void DoctorReportsRejectedLayoutsWithTheirFileAndFileId()
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SpriteFixtures", "Rectangle.asset"));
            var found = new ClassCoverage.Found { ClassId = 213, TypeName = "Sprite",
                SpriteDocuments = new Dictionary<string, string> { ["Assets/packed.asset"] = text.Replace("settingsRaw: 0", "settingsRaw: 1") } };
            var rejected = Assert.Single(ClassCoverage.Report("6000.0.71f1", new[] { found }).Classes);
            Assert.Equal("Unsupported", rejected.Reading);
            Assert.Contains("Assets/packed.asset fileID 21300000", rejected.Detail);
            Assert.Contains("settingsRaw 1", rejected.Detail);
            found.SpriteDocuments["Assets/packed.asset"] = text;
            Assert.Equal("Read", Assert.Single(ClassCoverage.Report("6000.0.71f1", new[] { found }).Classes).Reading);
        }

        static void Set(YNode node, string key, YNode value)
        {
            int at = Array.IndexOf(node.Keys, key);
            Assert.True(at >= 0, key);
            node.Values[at] = value;
        }

        [Theory]
        [InlineData("packed", "settingsRaw")]
        [InlineData("rotated", "settingsRaw")]
        [InlineData("atlas", "SpriteAtlas")]
        [InlineData("alpha", "alpha")]
        [InlineData("secondary", "secondaryTextures")]
        [InlineData("bones", "m_Bones")]
        [InlineData("downscale", "downscaled")]
        [InlineData("version", "serializedVersion")]
        [InlineData("unknown-field", "unfamiliar")]
        [InlineData("missing-texture", "Texture2D")]
        [InlineData("wrong-fileid", "Texture2D")]
        [InlineData("indices", "index 65535")]
        [InlineData("vertices", "m_DataSize")]
        public void UnsupportedDataIsAnErrorAndLeavesNoDefaultSprite(string variant, string diagnostic)
        {
            var docs = Documents("Rectangle");
            var body = docs[0].Body;
            var rd = body.Get("m_RD");
            switch (variant)
            {
                case "packed": Set(rd, "settingsRaw", YNode.MakeScalar("65")); break;
                case "rotated": Set(rd, "settingsRaw", YNode.MakeScalar("16")); break;
                case "atlas": Set(body, "m_SpriteAtlas", YNode.MakeMap(("fileID", YNode.MakeScalar("687078895")))); break;
                case "alpha": Set(rd, "alphaTexture", rd.Get("texture")); break;
                case "secondary": Set(rd, "secondaryTextures", new YNode { Kind = YKind.Seq, Items = new[] { YNode.MakeMap() } }); break;
                case "bones": Set(body, "m_Bones", new YNode { Kind = YKind.Seq, Items = new[] { YNode.MakeMap() } }); break;
                case "downscale": Set(rd, "downscaleMultiplier", YNode.MakeScalar(".5")); break;
                case "version": Set(rd, "serializedVersion", YNode.MakeScalar("99")); break;
                case "unknown-field": rd.Keys = rd.Keys.Append("futureMesh").ToArray(); rd.Values = rd.Values.Append(YNode.MakeMap()).ToArray(); break;
                case "missing-texture": Set(rd, "texture", YNode.MakeMap(("fileID", YNode.MakeScalar("0")))); break;
                case "wrong-fileid": Set(rd.Get("texture"), "fileID", YNode.MakeScalar("2800001")); break;
                case "indices": Set(rd, "m_IndexBuffer", YNode.MakeScalar("ffff01000200020001000300")); break;
                case "vertices": Set(rd.Get("m_VertexData"), "_typelessdata", YNode.MakeScalar("00")); break;
            }
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Errors(previous);
            logger.logHandler = capture;
            LoadedFile loaded;
            try { loaded = Load(docs); }
            finally { logger.logHandler = previous; }
            Assert.Empty(loaded.Objects);
            Assert.Null(loaded.MainObject);
            string error = Assert.Single(capture.Messages);
            Assert.Contains("Sprite 'Rectangle' (class 213, fileID 21300000)", error);
            Assert.Contains("Assets/SerializedSprite.asset", error);
            Assert.Contains(diagnostic, error);
        }

        sealed class Errors : ILogHandler
        {
            readonly ILogHandler m_Inner;
            internal readonly List<string> Messages = new List<string>();
            internal Errors(ILogHandler inner) { m_Inner = inner; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                if (type == LogType.Error && text.Contains("cannot load Sprite")) Messages.Add(text);
                m_Inner.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }
    }
}
