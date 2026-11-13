using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.U2D;
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
        readonly List<(string Guid, Texture2D Texture)> m_Extra = new List<(string, Texture2D)>();

        static List<YDocument> Documents(string name) => UnityYamlText.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "SpriteFixtures", name + ".asset")))
            .Select(d => new YDocument { ClassId = d.ClassId, FileId = d.FileId, TypeName = d.ClassName, Body = d.Body.ToNode() }).ToList();

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
            foreach (var (guid, texture) in m_Extra)
            {
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(texture, true);
            }
        }

        LoadedFile Load(List<YDocument> docs)
        {
            var file = ObjectGraphBuilder.Build(docs, "Assets/SerializedSprite.asset", null, ObjectGraphBuilder.Kind.Asset, null, created: _ => { });
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
                Documents = new Dictionary<string, string> { ["Assets/packed.asset"] = text.Replace("settingsRaw: 0", "settingsRaw: 1") } };
            var rejected = Assert.Single(ClassCoverage.Report("6000.0.71f1", new[] { found }).Classes);
            Assert.Equal("Unsupported", rejected.Reading);
            Assert.Contains("Assets/packed.asset fileID 21300000", rejected.Detail);
            Assert.Contains("settingsRaw 1", rejected.Detail);
            found.Documents["Assets/packed.asset"] = text;
            Assert.Equal("Read", Assert.Single(ClassCoverage.Report("6000.0.71f1", new[] { found }).Classes).Reading);
        }

        [Fact]
        public void ATextureDocumentAfterItsSpriteIsFilledBeforeResolvingItsDimensions()
        {
            var docs = Documents("Rectangle");
            Set(docs[0].Body.Get("m_RD"), "texture", YNode.MakeMap(("fileID", YNode.MakeScalar("2800000"))));
            docs.Add(new YDocument { ClassId = 28, FileId = 2800000, TypeName = "Texture2D",
                Body = YNode.MakeMap(("m_Name", YNode.MakeScalar("LocalTexture")), ("m_Width", YNode.MakeScalar("64")),
                    ("m_Height", YNode.MakeScalar("64")), ("m_IsReadable", YNode.MakeScalar("1"))) });
            var file = Load(docs);
            var sprite = Assert.IsType<Sprite>(file.MainObject);
            Assert.Same(file.Objects[2800000], sprite.texture);
            Assert.Equal(64, sprite.texture.width);
        }

        /// <summary>Registers a texture for the GUID a layout fixture's render data references.</summary>
        Texture2D TextureFor(YNode reference, int width, int height, string name)
        {
            var texture = new Texture2D(width, height) { name = name, IsPersistentAsset = true };
            var file = new LoadedFile { Guid = reference.AsRef().Guid, MainObject = texture, Path = "Assets/" + name + ".png" };
            file.Objects[2800000] = texture;
            AssetDatabaseRuntime.AddLoaded(file);
            m_Extra.Add((file.Guid, texture));
            return texture;
        }

        [Fact]
        public void SkinnedSpritesKeepTheirBonesBindPosesAndBlendChannels()
        {
            var sprite = Assert.IsType<Sprite>(Load(Documents("Layouts/Skinned")).MainObject);
            Assert.Same(m_Texture, sprite.texture);
            var bones = sprite.GetBones();
            Assert.Equal(new[] { "root", "tip" }, bones.Select(b => b.name));
            Assert.Equal(new[] { "b0", "b1" }, bones.Select(b => b.guid));
            Assert.Equal(new[] { -1, 0 }, bones.Select(b => b.parentId));
            Assert.Equal(new Vector3(-.2f, -.1f, 0), bones[0].position);
            Assert.Equal(new Quaternion(0, 0, .25881904f, .9659258f), bones[1].rotation);
            Assert.Equal(.2f, bones[1].length);
            Assert.Equal(new Color32(0, 255, 0, 255), bones[1].color);
            var poses = sprite.GetBindPoses();
            Assert.Equal(2, poses.Length);
            Assert.Equal(.2f, poses[0].m03);
            Assert.Equal(.9848077f, poses[0].m00);
            Assert.True(sprite.HasVertexAttribute(Rendering.VertexAttribute.BlendWeight));
            Assert.True(sprite.HasVertexAttribute(Rendering.VertexAttribute.BlendIndices));
            // A saved skinned Sprite has the channels it was saved with: no tangents.
            Assert.False(sprite.HasVertexAttribute(Rendering.VertexAttribute.Tangent));
            var weights = sprite.GetVertexAttribute<BoneWeight>(Rendering.VertexAttribute.BlendWeight).ToArray();
            Assert.Equal(7, weights.Length);
            Assert.Equal((1f, 0f, 0, 1), (weights[0].weight0, weights[0].weight1, weights[0].boneIndex0, weights[0].boneIndex1));
            Assert.Equal((1, 0), (weights[3].boneIndex0, weights[3].boneIndex1));
        }

        [Fact]
        public void SavedVertexColoursNormalsAndTangentsAreReadAndColoursAreDrawn()
        {
            var sprite = Assert.IsType<Sprite>(Load(Documents("Layouts/Attributes")).MainObject);
            foreach (var channel in new[] { Rendering.VertexAttribute.Normal, Rendering.VertexAttribute.Tangent, Rendering.VertexAttribute.Color })
                Assert.True(sprite.HasVertexAttribute(channel), channel.ToString());
            Assert.False(sprite.HasVertexAttribute(Rendering.VertexAttribute.BlendWeight));
            var colors = sprite.GetVertexAttribute<Color32>(Rendering.VertexAttribute.Color).ToArray();
            Assert.Equal(new Color32(40, 255, 90, 255), colors[0]);
            Assert.Equal(new Color32(61, 242, 90, 246), colors[1]);
            Assert.Equal(new Vector3(.1f, -.5f, -1f), sprite.GetVertexAttribute<Vector3>(Rendering.VertexAttribute.Normal)[1]);
            Assert.Equal(new Vector4(1f, .05f, 0f, 1f), sprite.GetVertexAttribute<Vector4>(Rendering.VertexAttribute.Tangent)[1]);
            var mesh = sprite.RenderMesh();
            Assert.Equal(colors, mesh.colors32);
            Object.DestroyImmediate(mesh);
            Assert.Empty(sprite.ParticleShapeMesh().colors32);
            // A copy's data access view is its own.
            var copy = Object.Instantiate(sprite);
            var white = new Unity.Collections.NativeArray<Color32>(copy.GetVertexCount(), Unity.Collections.Allocator.Temp);
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
            copy.SetVertexAttribute(Rendering.VertexAttribute.Color, white);
            Assert.Equal(new Color32(255, 255, 255, 255), copy.GetVertexAttribute<Color32>(Rendering.VertexAttribute.Color)[0]);
            Assert.Equal(new Color32(40, 255, 90, 255), sprite.GetVertexAttribute<Color32>(Rendering.VertexAttribute.Color)[0]);
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void AtlasMembersAndSecondaryTexturesAreReadFromTheSpritesOwnRenderData()
        {
            var atlasDocs = Documents("Layouts/AtlasTight");
            var atlased = TextureFor(atlasDocs[0].Body.Get("m_RD").Get("texture"), 64, 64, "Atlased");
            var sprite = Assert.IsType<Sprite>(Load(atlasDocs).MainObject);
            Assert.Same(atlased, sprite.texture);
            Assert.False(sprite.packed);
            Assert.Equal(10, sprite.vertices.Length);
            Assert.Equal(new Vector2(.984375f, .890625f), sprite.uv[0]);

            var litDocs = Documents("Layouts/Secondary");
            var rd = litDocs[0].Body.Get("m_RD");
            var lit = TextureFor(rd.Get("texture"), 32, 32, "Lit");
            var normal = TextureFor(rd.Get("secondaryTextures").Items[0].Get("texture"), 32, 32, "LitNormal");
            var secondary = Assert.IsType<Sprite>(Load(litDocs).MainObject);
            Assert.Same(lit, secondary.texture);
            var textures = new SecondarySpriteTexture[secondary.GetSecondaryTextureCount()];
            Assert.Equal(1, secondary.GetSecondaryTextures(textures));
            Assert.Equal("_NormalMap", textures[0].name);
            Assert.Same(normal, textures[0].texture);
        }

        [Fact]
        public void SavedTextureCoordinateChannelsLoad()
        {
            var sprite = Assert.IsType<Sprite>(Load(Documents("Layouts/TexCoords")).MainObject);
            int count = sprite.GetVertexCount();
            for (int channel = 1; channel <= 7; channel++)
            {
                var attribute = UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel;
                Assert.True(sprite.HasVertexAttribute(attribute));
                var values = sprite.GetVertexAttribute<Vector2>(attribute);
                Assert.Equal(count, values.Length);
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal(channel + i * .25f, values[i].x, 5);
                    Assert.Equal(channel * .5f - i, values[i].y, 5);
                }
            }
            var own = Assert.IsType<Sprite>(Load(Documents("Layouts/TexCoord0")).MainObject);
            for (int i = 0; i < own.uv.Length; i++)
            {
                Assert.Equal(.05f + i * .1f, own.uv[i].x, 5);
                Assert.Equal(.9f - i * .05f, own.uv[i].y, 5);
            }
        }

        [Fact]
        public void ScriptableObjectsAreAListWithoutRepeats()
        {
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f), 8);
            var a = ScriptableObject.CreateInstance<ScriptableObject>();
            var b = ScriptableObject.CreateInstance<ScriptableObject>();
            var c = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                Assert.Equal(0u, sprite.GetScriptableObjectsCount());
                Assert.True(sprite.AddScriptableObject(a));
                Assert.False(sprite.AddScriptableObject(a));
                Assert.Equal("obj", Assert.Throws<ArgumentNullException>(() => sprite.AddScriptableObject(null)).ParamName);
                Assert.True(sprite.AddScriptableObject(b));
                var two = new ScriptableObject[1];
                Assert.Equal(1u, sprite.GetScriptableObjects(two));
                Assert.Same(a, two[0]);
                Assert.Equal("scriptableObjects", Assert.Throws<ArgumentNullException>(() => sprite.GetScriptableObjects(null)).ParamName);
                Assert.False(sprite.SetScriptableObjectAt(b, 0));
                Assert.False(sprite.SetScriptableObjectAt(a, 0));
                Assert.False(sprite.SetScriptableObjectAt(c, 9));
                Assert.True(sprite.SetScriptableObjectAt(c, 1));
                Assert.False(sprite.RemoveScriptableObjectAt(7));
                var copy = Object.Instantiate(sprite);
                Object.DestroyImmediate(c);
                var held = new ScriptableObject[3];
                Assert.Equal(2u, sprite.GetScriptableObjects(held));
                Assert.Same(a, held[0]);
                Assert.True(ReferenceEquals(held[1], null));
                Assert.True(sprite.RemoveScriptableObjectAt(0));
                Assert.Equal(1u, sprite.GetScriptableObjectsCount());
                // A copy keeps its own list.
                Assert.Equal(2u, copy.GetScriptableObjectsCount());
                Object.DestroyImmediate(copy);
            }
            finally
            {
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
                Object.DestroyImmediate(a); Object.DestroyImmediate(b);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(128, true)]
        public void StoredTextureCoordinatesAreUsedOnlyAsTheSpritesOwn(int setting, bool stored)
        {
            // An ordinary save stores zeros and the UVs come from uvTransform; settingsRaw bit 7
            // marks texture coordinates set through SetVertexAttribute (tests/projects/spriteassets).
            var computed = Assert.IsType<Sprite>(Load(Documents("Rectangle")).MainObject).uv;
            var docs = Documents("Rectangle");
            var rd = docs[0].Body.Get("m_RD");
            var vd = rd.Get("m_VertexData");
            var bytes = Convert.FromHexString(vd.Str("_typelessdata"));
            BitConverter.GetBytes(1f).CopyTo(bytes, 48);
            Set(vd, "_typelessdata", YNode.MakeScalar(Convert.ToHexString(bytes)));
            Set(rd, "settingsRaw", YNode.MakeScalar(setting.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var uv = Assert.IsType<Sprite>(Load(docs).MainObject).uv;
            if (stored)
            {
                Assert.Equal(new Vector2(1f, 0f), uv[0]);
                Assert.All(uv.Skip(1), v => Assert.Equal(Vector2.zero, v));
            }
            else Assert.Equal(computed, uv);
        }

        [Fact]
        public void EarlierVertexDataAndSpritesSavedWithoutATextureLoad()
        {
            var v2 = Assert.IsType<Sprite>(Load(Documents("Layouts/VertexV2")).MainObject);
            Assert.Equal(new Rect(8, 12, 40, 32), v2.rect);
            Assert.Equal(new Vector2(.125f, .6875f), v2.uv[0]);

            var docs = Documents("Rectangle");
            Set(docs[0].Body.Get("m_RD"), "texture", YNode.MakeMap(("fileID", YNode.MakeScalar("0"))));
            var untextured = Assert.IsType<Sprite>(Load(docs).MainObject);
            Assert.Null(untextured.texture);
            Assert.Equal(new Rect(8, 12, 40, 32), untextured.rect);
            Assert.All(untextured.uv, uv => Assert.Equal(Vector2.zero, uv));
            // An earlier Editor's document has no secondary textures or scriptable objects fields.
            var earlier = Documents("Rectangle");
            Remove(earlier[0].Body, "m_ScriptableObjects");
            Remove(earlier[0].Body.Get("m_RD"), "secondaryTextures");
            Set(earlier[0].Body.Get("m_RD").Get("m_VertexData"), "serializedVersion", YNode.MakeScalar("2"));
            var old = Assert.IsType<Sprite>(Load(earlier).MainObject);
            Assert.Equal(0, old.GetSecondaryTextureCount());
            Assert.Equal(v2.uv, old.uv);
            var go = new GameObject("untextured");
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = untextured;
            Assert.False(renderer.MaskShape(out _, out _, out _));
            Object.DestroyImmediate(go);
        }

        static void Remove(YNode node, string key)
        {
            int at = Array.IndexOf(node.Keys, key);
            Assert.True(at >= 0, key);
            node.Keys = node.Keys.Where((_, i) => i != at).ToArray();
            node.Values = node.Values.Where((_, i) => i != at).ToArray();
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
        [InlineData("legacy-atlas", "legacy Sprite Packer")]
        [InlineData("alpha", "alpha")]
        [InlineData("downscale", "downscaled")]
        [InlineData("version", "serializedVersion")]
        [InlineData("vertex-version", "vertex data serializedVersion 4")]
        [InlineData("channel", "TexCoord1")]
        [InlineData("unknown-field", "unfamiliar")]
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
                case "legacy-atlas": Set(body, "m_AtlasName", YNode.MakeScalar("Legacy")); break;
                case "alpha": Set(rd, "alphaTexture", rd.Get("texture")); break;
                case "downscale": Set(rd, "downscaleMultiplier", YNode.MakeScalar(".5")); break;
                case "version": Set(rd, "serializedVersion", YNode.MakeScalar("99")); break;
                case "vertex-version": Set(rd.Get("m_VertexData"), "serializedVersion", YNode.MakeScalar("4")); break;
                // Texture coordinates 1 to 7 are saved as two floats; three are not a saved layout.
                case "channel": Set(rd.Get("m_VertexData").Get("m_Channels").Items[5], "dimension", YNode.MakeScalar("3")); break;
                case "unknown-field": rd.Keys = rd.Keys.Append("futureMesh").ToArray(); rd.Values = rd.Values.Append(YNode.MakeMap()).ToArray(); break;
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
                string text = args == null || args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                if (type == LogType.Error && text.Contains("cannot load Sprite")) Messages.Add(text);
                m_Inner.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }
    }
}
