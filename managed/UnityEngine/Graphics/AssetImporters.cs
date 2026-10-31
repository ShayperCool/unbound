using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>The objects imported assets give: a texture and its sprites, ...</summary>
    internal static class AssetImporters
    {
        internal const long Texture2DFileId = 2800000;
        internal const long CubemapFileId = 8900000;

        static AssetImporters()
        {
            AssetDatabaseRuntime.Importers["texture"] = ImportTexture;
            AssetDatabaseRuntime.Importers["audio"] = AudioClipImporter.Import;
            AssetDatabaseRuntime.Importers["text"] = ImportText;
            AssetDatabaseRuntime.Importers["video"] = ImportVideo;
            AssetDatabaseRuntime.Importers["shader"] = ImportShader;
            AssetDatabaseRuntime.Importers["compute"] = ComputeShaderImporter.Import;
            AssetDatabaseRuntime.Importers["script"] = ImportScript;
            AssetDatabaseRuntime.Importers["scripted"] = ScriptedImports.Import;
            AssetDatabaseRuntime.Importers["scene"] = ImportScene;
            FontImporter.Register();
            Unbound.UIElements.UiImporters.Register();
            // Files Unity imports as DefaultAsset: nothing loadable.
            AssetDatabaseRuntime.Importers["unknown"] = _ => new LoadedFile();
            AssetDatabaseRuntime.Importers["folder"] = _ => new LoadedFile();
        }

        internal const long TextAssetFileId = 4900000;
        internal const long ShaderFileId = 4800000;
        internal const long MonoScriptFileId = 11500000;

        /// <summary>A video file: its VideoClip (fileID 32900000), probed through ffprobe.</summary>
        static LoadedFile ImportVideo(AssetDatabaseRuntime.AssetInfo info)
        {
            var clip = UnityEngine.Video.VideoClip.Probe(info.File, info.Path);
            var file = new LoadedFile { MainObject = clip };
            file.Objects[32900000] = clip;
            return file;
        }

        static LoadedFile ImportText(AssetDatabaseRuntime.AssetInfo info)
        {
            TextAsset asset;
            using (ObjectRegistry.AsAssets())
                asset = new TextAsset(info.File, System.IO.Path.GetFileNameWithoutExtension(info.Path), new System.IO.FileInfo(info.File).Length) { IsPersistentAsset = true };
            var file = new LoadedFile { MainObject = asset };
            file.Objects[TextAssetFileId] = asset;
            return file;
        }

        internal const long SceneAssetFileId = 102900000;

        /// <summary>A scene file as the Editor's asset database has it: a SceneAsset (fileID
        /// 102900000) named after the file; its contents load through the scene manager.</summary>
        static LoadedFile ImportScene(AssetDatabaseRuntime.AssetInfo info)
        {
            UnityEditor.SceneAsset scene;
            using (ObjectRegistry.AsAssets())
                scene = new UnityEditor.SceneAsset { name = System.IO.Path.GetFileNameWithoutExtension(info.Path), IsPersistentAsset = true };
            var file = new LoadedFile { MainObject = scene };
            file.Objects[SceneAssetFileId] = scene;
            return file;
        }

        static LoadedFile ImportScript(AssetDatabaseRuntime.AssetInfo info)
        {
            UnityEditor.MonoScript script;
            using (ObjectRegistry.AsAssets())
                script = new UnityEditor.MonoScript(info.File, System.IO.Path.GetFileNameWithoutExtension(info.Path), new System.IO.FileInfo(info.File).Length, info.Guid) { IsPersistentAsset = true };
            var file = new LoadedFile { MainObject = script };
            file.Objects[MonoScriptFileId] = script;
            return file;
        }

        /// <summary>A project shader: its declared name, queue and properties (the renderer reads its
        /// render states from the source itself).</summary>
        static LoadedFile ImportShader(AssetDatabaseRuntime.AssetInfo info)
        {
            var source = System.IO.File.ReadAllText(info.File);
            var name = ShaderLab.Name(source) ?? System.IO.Path.GetFileNameWithoutExtension(info.Path);
            Shader shader;
            using (ObjectRegistry.AsAssets())
                shader = Shader.Create(name, ShaderLab.Queue(source));
            shader.m_Properties.Clear();
            shader.m_Properties.AddRange(ShaderLab.Properties(source));
            shader.PropertiesChanged();
            shader.IsPersistentAsset = true;
            Shader.Register(shader);
            var file = new LoadedFile { MainObject = shader };
            file.Objects[ShaderFileId] = shader;
            return file;
        }

        internal static void EnsureRegistered() { }

        sealed class SpriteJson
        {
            public long FileId { get; set; }
            public string Name { get; set; }
            public float[] Rect { get; set; }
            public float[] Pivot { get; set; }
            public float[] Border { get; set; }
            public float PixelsPerUnit { get; set; }
            public int MeshType { get; set; }
            public MeshJson Mesh { get; set; }
            public float[] TextureRectBounds { get; set; }
            public float[][][] Physics { get; set; }
            public float[][] Vertices { get; set; }
            public int[] Indices { get; set; }
            public List<WeightJson> Weights { get; set; }
            public List<BoneJson> Bones { get; set; }
            public List<SecondaryJson> SecondaryTextures { get; set; }
        }

        sealed class MeshJson
        {
            public float[][] Vertices { get; set; }
            public float[][] Uv { get; set; }
            public int[] Triangles { get; set; }
        }

        sealed class SecondaryJson
        {
            public string Name { get; set; }
            public string Guid { get; set; }
        }

        sealed class WeightJson
        {
            public float[] Weights { get; set; }
            public int[] Bones { get; set; }
        }

        sealed class BoneJson
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public float[] Position { get; set; }
            public float[] Rotation { get; set; }
            public float Length { get; set; }
            public int ParentId { get; set; }
            public uint Color { get; set; }
        }

        /// <summary>The authored mesh and skin of an imported sprite, when it has them.</summary>
        static void ImportSkin(Sprite sprite, SpriteJson s)
        {
            var vertices = s.Vertices == null ? null : Array.ConvertAll(s.Vertices, v => new Vector2(v[0], v[1]));
            var triangles = s.Indices == null ? null : Array.ConvertAll(s.Indices, i => (ushort)i);
            var weights = s.Weights == null || s.Weights.Count == 0 ? null : s.Weights.ConvertAll(w => new BoneWeight
            {
                weight0 = w.Weights[0], weight1 = w.Weights[1], weight2 = w.Weights[2], weight3 = w.Weights[3],
                boneIndex0 = w.Bones[0], boneIndex1 = w.Bones[1], boneIndex2 = w.Bones[2], boneIndex3 = w.Bones[3],
            }).ToArray();
            var bones = s.Bones == null ? null : s.Bones.ConvertAll(b => new UnityEngine.U2D.SpriteBone
            {
                name = b.Name,
                guid = b.Guid,
                position = new Vector3(b.Position[0], b.Position[1], b.Position[2]),
                rotation = new Quaternion(b.Rotation[0], b.Rotation[1], b.Rotation[2], b.Rotation[3]),
                length = b.Length,
                parentId = b.ParentId,
                color = new Color32((byte)b.Color, (byte)(b.Color >> 8), (byte)(b.Color >> 16), (byte)(b.Color >> 24)),
            }).ToArray();
            sprite.SetImportedMesh(vertices, triangles, weights, bones);
        }

        sealed class SpritesJson
        {
            public string Name { get; set; }
            public List<SpriteJson> Sprites { get; set; }
        }

        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        static LoadedFile ImportTexture(AssetDatabaseRuntime.AssetInfo info)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(info.Path);
            ulong handle = RenderHandles.Next();
            if (!Native.ImportTexture(handle, info.Guid, out var imported))
                throw new InvalidOperationException("the texture could not be imported");
            if (imported.Dimension == 4)
            {
                var cube = Cubemap.FromImport(handle, name, imported);
                var cubeFile = new LoadedFile { MainObject = cube };
                cubeFile.Objects[CubemapFileId] = cube;
                return cubeFile;
            }
            var texture = Texture2D.FromImport(handle, info.Guid, name, imported);
            var file = new LoadedFile { MainObject = texture };
            file.Objects[Texture2DFileId] = texture;
            if (imported.SpriteCount > 0)
            {
                var json = Native.Call("texture-sprites", JsonSerializer.Serialize(new { guid = info.Guid }));
                var sprites = JsonSerializer.Deserialize<SpritesJson>(json, s_Json);
                foreach (var s in sprites.Sprites)
                {
                    var sprite = Sprite.FromImport(texture, string.IsNullOrEmpty(s.Name) ? name : s.Name,
                        new Rect(s.Rect[0], s.Rect[1], s.Rect[2], s.Rect[3]),
                        new Vector2(s.Pivot[0], s.Pivot[1]),
                        new Vector4(s.Border[0], s.Border[1], s.Border[2], s.Border[3]),
                        s.PixelsPerUnit, s.MeshType, Points(s.Mesh?.Vertices), Points(s.Mesh?.Uv),
                        s.Mesh?.Triangles == null ? null : Array.ConvertAll(s.Mesh.Triangles, i => (ushort)i),
                        s.TextureRectBounds is { Length: 4 } t ? new Rect(t[0], t[1], t[2], t[3]) : (Rect?)null,
                        Paths(s.Physics));
                    ImportSkin(sprite, s);
                    if (s.SecondaryTextures != null && s.SecondaryTextures.Count > 0)
                        sprite.SetImportedSecondaryTextures(s.SecondaryTextures.ConvertAll(t => (t.Name, t.Guid)).ToArray());
                    file.Objects[s.FileId] = sprite;
                }
            }
            return file;
        }

        static Vector2[] Points(float[][] points) => points == null ? null : Array.ConvertAll(points, p => new Vector2(p[0], p[1]));

        static Vector2[][] Paths(float[][][] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            var result = new Vector2[paths.Length][];
            for (int i = 0; i < paths.Length; i++)
            {
                result[i] = new Vector2[paths[i].Length];
                for (int j = 0; j < paths[i].Length; j++) result[i][j] = new Vector2(paths[i][j][0], paths[i][j][1]);
            }
            return result;
        }
    }

    /// <summary>Objects of Unity's built-in resource files, made by Unbound.</summary>
    internal static class BuiltinObjects
    {
        /// <summary>file id → class id, by built-in GUID.</summary>
        internal static readonly Dictionary<long, int> ExtraClasses = new Dictionary<long, int>();
        internal static readonly Dictionary<long, int> DefaultClasses = new Dictionary<long, int>();
        static readonly Dictionary<(string, long), Object> s_Objects = new Dictionary<(string, long), Object>();

        /// <summary>The built-in file an object loaded from (its GUID), if it is a built-in one.</summary>
        internal static bool TryGetGuid(Object obj, out string guid)
        {
            foreach (var entry in s_Objects)
            {
                if (!ReferenceEquals(entry.Value, obj)) continue;
                guid = entry.Key.Item1;
                return true;
            }
            guid = null;
            return false;
        }

        /// <summary>A mesh of a serialized file (Unity's built-in meshes), read by the renderer's
        /// side; null when it cannot be read.</summary>
        internal static Mesh SerializedMesh(string guid, long fileId)
        {
            if (!Native.IsReady) return null;
            try
            {
                var root = System.Text.Json.JsonDocument.Parse(Native.Call("mesh-object", System.Text.Json.JsonSerializer.Serialize(new { guid, fileId }))).RootElement;
                float[] Floats(System.Text.Json.JsonElement e)
                {
                    var a = new float[e.GetArrayLength()];
                    int i = 0;
                    foreach (var x in e.EnumerateArray()) a[i++] = x.GetSingle();
                    return a;
                }
                var p = Floats(root.GetProperty("positions"));
                int count = p.Length / 3;
                var mesh = new Mesh { name = root.GetProperty("name").GetString() ?? "" };
                var vertices = new Vector3[count];
                for (int i = 0; i < count; i++) vertices[i] = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]);
                mesh.vertices = vertices;
                var n = Floats(root.GetProperty("normals"));
                if (n.Length == count * 3)
                {
                    var normals = new Vector3[count];
                    for (int i = 0; i < count; i++) normals[i] = new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]);
                    mesh.normals = normals;
                }
                var t = Floats(root.GetProperty("tangents"));
                if (t.Length == count * 4)
                {
                    var tangents = new Vector4[count];
                    for (int i = 0; i < count; i++) tangents[i] = new Vector4(t[i * 4], t[i * 4 + 1], t[i * 4 + 2], t[i * 4 + 3]);
                    mesh.tangents = tangents;
                }
                var c = Floats(root.GetProperty("colors"));
                if (c.Length == count * 4)
                {
                    var colors = new Color[count];
                    for (int i = 0; i < count; i++) colors[i] = new Color(c[i * 4], c[i * 4 + 1], c[i * 4 + 2], c[i * 4 + 3]);
                    mesh.colors = colors;
                }
                int channel = 0;
                foreach (var uvElement in root.GetProperty("uvs").EnumerateArray())
                {
                    var u = Floats(uvElement);
                    if (u.Length == count * 2)
                    {
                        var uvs = new List<Vector2>(count);
                        for (int i = 0; i < count; i++) uvs.Add(new Vector2(u[i * 2], u[i * 2 + 1]));
                        mesh.SetUVs(channel, uvs);
                    }
                    channel++;
                }
                var indexElement = root.GetProperty("indices");
                var indices = new int[indexElement.GetArrayLength()];
                int k = 0;
                foreach (var x in indexElement.EnumerateArray()) indices[k++] = x.GetInt32();
                var submeshes = root.GetProperty("submeshes");
                mesh.subMeshCount = Math.Max(1, submeshes.GetArrayLength());
                int sub = 0;
                foreach (var s in submeshes.EnumerateArray())
                {
                    int first = s[0].GetInt32(), length = s[1].GetInt32(), baseVertex = s[2].GetInt32();
                    var part = new int[length];
                    Array.Copy(indices, first, part, 0, length);
                    mesh.SetIndices(part, (MeshTopology)s[3].GetInt32(), sub++, true, baseVertex);
                }
                // The Editor lets scripts read the built-in meshes.
                return mesh;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: built-in mesh {fileId} could not be read ({e.Message}); a generated one stands in.");
                return null;
            }
        }

        internal static Object Get(string guid, long fileId, string name)
        {
            guid = guid.ToLowerInvariant();
            if (s_Objects.TryGetValue((guid, fileId), out var cached) && cached) return cached;
            bool extra = guid == BuiltinShaders.ExtraGuid;
            var classes = extra ? ExtraClasses : DefaultClasses;
            classes.TryGetValue(fileId, out var classId);
            Object obj;
            using (ObjectRegistry.AsAssets())
            {
                switch (classId)
                {
                    case 48:
                        obj = BuiltinShaders.ByName(name);
                        break;
                    case 128:
                        obj = BuiltinFonts.Get(name);
                        break;
                    case 21:
                        obj = BuiltinFonts.Get(name) ?? new Material(Shader.Find(MaterialShader(name)) ?? BuiltinShaders.ByName("Standard")) { name = name };
                        break;
                    case 28:
                        obj = BuiltinFonts.Get(name) ?? (name == "Default-Checker-Gray" ? Texture2D.FromSerialized(guid, fileId) : null) ?? BuiltinTexture(name);
                        break;
                    case 43:
                        // The install's own mesh, else a generated primitive.
                        obj = SerializedMesh(guid, fileId) ?? BuiltinMeshes.Create(name);
                        if (obj == null) goto default;
                        break;
                    case 213:
                    {
                        var texture = (Texture2D)Get(guid, fileId - 1, name);
                        float border = name == "Knob" || name == "Checkmark" || name == "DropdownArrow" ? 0f : 10f;
                        obj = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
                        obj.m_Name = name;
                        break;
                    }
                    default:
                        Debug.LogWarning($"Unbound: built-in resource '{name}' (class {classId}) is not available.");
                        return null;
                }
            }
            if (obj != null)
            {
                obj.IsPersistentAsset = true;
                s_Objects[(guid, fileId)] = obj;
            }
            return obj;
        }

        /// <summary>The built-in Sprites-Default material (`unity_builtin_extra`, file 10754), the
        /// material of a sprite renderer added by a script.</summary>
        internal static Material SpritesDefault
        {
            get
            {
                // Without the shader (no Unity install) the renderer stays without a material.
                if (Shader.Find("Sprites/Default") == null) return null;
                ExtraClasses.TryAdd(SpritesDefaultFileId, 21);
                return Get(BuiltinShaders.ExtraGuid, SpritesDefaultFileId, "Sprites-Default") as Material;
            }
        }

        const long SpritesDefaultFileId = 10754;

        /// <summary>The built-in Sprites-Mask material (`unity_builtin_extra`, file 10758), the
        /// material of a sprite mask added by a script.</summary>
        internal static Material SpritesMask
        {
            get
            {
                if (Shader.Find("Sprites/Mask") == null) return null;
                ExtraClasses.TryAdd(SpritesMaskFileId, 21);
                return Get(BuiltinShaders.ExtraGuid, SpritesMaskFileId, "Sprites-Mask") as Material;
            }
        }

        const long SpritesMaskFileId = 10758;

        /// <summary>The built-in Default-Terrain-Standard material (`unity_builtin_extra`, file
        /// 10652), a terrain's material template when it has none.</summary>
        internal static Material DefaultTerrainMaterial
        {
            get
            {
                if (Shader.Find("Nature/Terrain/Standard") == null) return null;
                ExtraClasses.TryAdd(DefaultTerrainFileId, 21);
                return Get(BuiltinShaders.ExtraGuid, DefaultTerrainFileId, "Default-Terrain-Standard") as Material;
            }
        }

        const long DefaultTerrainFileId = 10652;

        /// <summary>The built-in Default-Checker-Gray texture (`unity_builtin_extra`, file 10309):
        /// what a terrain without terrain layers shows.</summary>
        internal static Texture2D DefaultCheckerGray
        {
            get
            {
                ExtraClasses.TryAdd(DefaultCheckerGrayFileId, 28);
                return Get(BuiltinShaders.ExtraGuid, DefaultCheckerGrayFileId, "Default-Checker-Gray") as Texture2D;
            }
        }

        const long DefaultCheckerGrayFileId = 10309;

        static string MaterialShader(string material)
        {
            switch (material)
            {
                case "Sprites-Default": return "Sprites/Default";
                case "Sprites-Mask": return "Sprites/Mask";
                case "Default-Particle": return "Legacy Shaders/Particles/Alpha Blended Premultiply";
                case "Default-ParticleSystem": return "Particles/Standard Unlit";
                case "Default-Line": return "Legacy Shaders/Particles/Alpha Blended Premultiply";
                case "Default-Diffuse": return "Legacy Shaders/Diffuse";
                case "Default-Skybox": return "Skybox/Procedural";
                case "Font Material": return "GUI/Text Shader";
                case "Default-Terrain-Standard": return "Nature/Terrain/Standard";
                case "Default-Terrain-Diffuse": return "Nature/Terrain/Diffuse";
                case "Default-Terrain-Specular": return "Nature/Terrain/Specular";
                default: return "Standard";
            }
        }

        /// <summary>The built-in UI textures, drawn here: rounded rectangles, a circle, a check mark.</summary>
        static Texture2D BuiltinTexture(string name)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float a;
                    float cx = x + 0.5f - size / 2f, cy = y + 0.5f - size / 2f;
                    switch (name)
                    {
                        case "Knob":
                            a = Mathf.Clamp01(size / 2f - 1f - Mathf.Sqrt(cx * cx + cy * cy) + 0.5f);
                            break;
                        case "Checkmark":
                            a = Mathf.Abs(y - (x < 13 ? 26 - x : x - 2)) < 2.5f && x > 5 && x < 28 ? 1f : 0f;
                            break;
                        case "DropdownArrow":
                            a = y > 8 && y < 22 && Mathf.Abs(cx) < (y - 8) * 0.6f ? 1f : 0f;
                            break;
                        default:
                        {
                            // A rounded rectangle with a 4 pixel radius.
                            float dx = Mathf.Max(0f, Mathf.Abs(cx) - (size / 2f - 4f));
                            float dy = Mathf.Max(0f, Mathf.Abs(cy) - (size / 2f - 4f));
                            a = Mathf.Clamp01(4f - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                            break;
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
