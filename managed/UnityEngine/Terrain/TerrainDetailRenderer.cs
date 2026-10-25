using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A terrain's details as the lights see them: one patch's mesh of a kind (grass,
    /// billboards, meshes), or a mesh detail drawn on its own.</summary>
    internal sealed class DetailRenderer : Renderer
    {
        internal Bounds m_LocalBounds;

        internal override Bounds GetLocalBounds() => m_LocalBounds;
    }

    // The details: per patch a mesh of the grass quads, one of the billboards and one of the meshes
    // drawn without instancing, in world space, drawn with Unity's detail shaders
    // (Hidden/TerrainEngine/Details/...) and a texture atlas of the prototypes; meshes drawn with
    // instancing are their prototype's meshes and materials at each instance. As the Editor builds
    // them (measured: a project overriding the detail shaders dumped the vertices it draws):
    // a grass quad spans its width along its turned x axis (uv's left at -x), from its position's
    // height up, its colour the dry colour lerped to the healthy one by the size noise (top; the
    // bottom 0.625 of it, alpha 0 for no waving), its normal the terrain's; a billboard's four
    // corners sit at the instance with their offsets in the tangent; meshes are baked turned and
    // scaled, coloured the instance's colour. The shaders' globals: _WaveAndDistance (frame time ×
    // speed × 0.04, strength × 0.5, amount × 6, distance²), _CameraPosition (the camera less the
    // terrain's position, 1 / distance²), the camera's right and up, the waving tint.
    internal sealed partial class TerrainRenderer
    {
        enum DetailKind { Grass, Billboard, Mesh }

        sealed class DetailPatch
        {
            internal readonly DetailRenderer[] Renderers = new DetailRenderer[3];
            internal readonly Mesh[] Meshes = new Mesh[3];
            internal Bounds Bounds;
            internal bool HasBounds;
        }

        struct InstancedDetail
        {
            internal DetailRenderer Renderer;
            internal Mesh Mesh;
            internal Matrix4x4 Model;
            internal Vector3 Position;
        }

        readonly List<DetailPatch> m_DetailPatches = new List<DetailPatch>();
        readonly List<InstancedDetail> m_InstancedDetails = new List<InstancedDetail>();
        TerrainData m_DetailsData;
        int m_DetailsVersion = -1, m_DetailsHeights = -1;
        Vector3 m_DetailsOrigin;
        Texture2D m_DetailAtlas;
        readonly Material[] m_DetailMaterials = new Material[3];
        static readonly string[] s_DetailShaders =
        {
            "Hidden/TerrainEngine/Details/WavingDoublePass",
            "Hidden/TerrainEngine/Details/BillboardWavingDoublePass",
            "Hidden/TerrainEngine/Details/Vertexlit",
        };

        void CollectDetails(Terrain terrain, TerrainData data, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var origin = terrain.GetPosition();
            if (m_DetailsData != data || m_DetailsVersion != data.m_DetailsVersion || m_DetailsHeights != data.m_HeightsVersion || m_DetailsOrigin != origin)
                BuildDetails(terrain, data, origin);
            float distance = terrain.detailObjectDistance;
            if (distance <= 0f) return;
            float squared = distance * distance;
            var cam = camera.Camera;
            var right = cam != null ? cam.transform.right : Vector3.right;
            var up = cam != null ? cam.transform.up : Vector3.up;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var tint = linear ? data.wavingGrassTint.linear : data.wavingGrassTint;
            var block = new MaterialPropertyBlock();
            block.SetVector("_WaveAndDistance", new Vector4(Time.deltaTime * data.wavingGrassSpeed * 0.04f, data.wavingGrassStrength * 0.5f, data.wavingGrassAmount * 6f, squared));
            var relative = camera.Position - origin;
            block.SetVector("_CameraPosition", new Vector4(relative.x, relative.y, relative.z, 1f / squared));
            block.SetVector("_CameraRight", right);
            block.SetVector("_CameraUp", up);
            block.SetColor("_WavingTint", tint);
            foreach (var patch in m_DetailPatches)
            {
                if (!patch.HasBounds || patch.Bounds.SqrDistance(camera.Position) > squared) continue;
                for (int kind = 0; kind < 3; kind++)
                {
                    var renderer = patch.Renderers[kind];
                    if (renderer == null) continue;
                    renderer.m_PropertyBlock = block;
                    FrameBuilder.AddMeshDraws(renderer, patch.Meshes[kind], Matrix4x4.identity, Color.white, camera, into);
                }
            }
            foreach (var detail in m_InstancedDetails)
            {
                if ((detail.Position - camera.Position).sqrMagnitude > squared) continue;
                FrameBuilder.AddMeshDraws(detail.Renderer, detail.Mesh, detail.Model, Color.white, camera, into);
            }
        }

        void DestroyDetails()
        {
            foreach (var patch in m_DetailPatches)
                for (int kind = 0; kind < 3; kind++)
                {
                    if (patch.Renderers[kind] != null) patch.Renderers[kind].OnEngineDestroy();
                    if (patch.Meshes[kind] != null) DestroyImmediate(patch.Meshes[kind]);
                }
            m_DetailPatches.Clear();
            foreach (var detail in m_InstancedDetails) detail.Renderer.OnEngineDestroy();
            m_InstancedDetails.Clear();
        }

        /// <summary>The prototypes' textures side by side, then a white 4 × 4 the meshes sample, in
        /// the smallest power-of-two texture they fit (the Editor's atlas for one 16 × 32 texture
        /// is 32 × 32); the rects inset by half a texel, as the Editor's uvs.</summary>
        Rect[] BuildDetailAtlas(DetailPrototype[] prototypes, out Rect white)
        {
            var textures = new List<Texture2D>();
            var index = new int[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
            {
                index[i] = -1;
                var texture = prototypes[i].usePrototypeMesh ? null : prototypes[i].prototypeTexture;
                if (texture == null) continue;
                index[i] = textures.IndexOf(texture);
                if (index[i] < 0) { index[i] = textures.Count; textures.Add(texture); }
            }
            int width = 4, height = 4;
            foreach (var t in textures) { width += t.width; height = Math.Max(height, t.height); }
            width = Mathf.NextPowerOfTwo(width);
            height = Mathf.NextPowerOfTwo(height);
            if (m_DetailAtlas == null)
                m_DetailAtlas = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "Unbound Detail Atlas", hideFlags = HideFlags.HideAndDontSave };
            else
                m_DetailAtlas.Reinitialize(width, height);
            var pixels = new Color32[width * height];
            var rects = new Rect[textures.Count];
            int x = 0;
            for (int i = 0; i < textures.Count; i++)
            {
                var t = textures[i];
                var source = t.EnginePixels32();
                for (int row = 0; row < t.height; row++)
                    for (int column = 0; column < t.width; column++)
                        pixels[row * width + x + column] = source[row * t.width + column];
                rects[i] = new Rect((x + 0.5f) / width, 0.5f / height, (t.width - 1f) / width, (t.height - 1f) / height);
                x += t.width;
            }
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    pixels[row * width + x + column] = new Color32(255, 255, 255, 255);
            white = new Rect((x + 2f) / width, 2f / height, 0f, 0f);
            m_DetailAtlas.SetPixels32(pixels);
            m_DetailAtlas.Apply(true);
            var result = new Rect[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++) result[i] = index[i] >= 0 ? rects[index[i]] : white;
            return result;
        }

        Material DetailMaterial(int kind)
        {
            if (m_DetailMaterials[kind] == null)
            {
                var shader = Shader.Find(s_DetailShaders[kind]);
                if (shader == null)
                {
                    Warn($"Unbound: the terrain detail shader {s_DetailShaders[kind]} is missing; those details are not drawn.");
                    return null;
                }
                m_DetailMaterials[kind] = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                m_DetailMaterials[kind].SetFloat("_Cutoff", 0.375f);
            }
            m_DetailMaterials[kind].mainTexture = m_DetailAtlas;
            return m_DetailMaterials[kind];
        }

        sealed class MeshBuilder
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector3> Normals = new List<Vector3>();
            internal readonly List<Vector4> Tangents = new List<Vector4>();
            internal readonly List<Color32> Colors = new List<Color32>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<int> Indices = new List<int>();

            internal void Add(Vector3 position, Vector3 normal, Vector4 tangent, Color32 color, Vector2 uv)
            {
                Vertices.Add(position);
                Normals.Add(normal);
                Tangents.Add(tangent);
                Colors.Add(color);
                Uvs.Add(uv);
            }

            internal void Quad(int first)
            {
                Indices.Add(first); Indices.Add(first + 1); Indices.Add(first + 2);
                Indices.Add(first); Indices.Add(first + 2); Indices.Add(first + 3);
            }
        }

        static Color32 ToColor32(Color c) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255));

        void BuildDetails(Terrain terrain, TerrainData data, Vector3 origin)
        {
            DestroyDetails();
            m_DetailsData = data;
            m_DetailsVersion = data.m_DetailsVersion;
            m_DetailsHeights = data.m_HeightsVersion;
            m_DetailsOrigin = origin;
            var prototypes = data.detailPrototypes;
            if (prototypes.Length == 0 || data.detailResolution == 0) return;
            var rects = BuildDetailAtlas(prototypes, out var white);
            var size = data.size;
            int patches = data.detailPatchCount;
            for (int py = 0; py < patches; py++)
                for (int px = 0; px < patches; px++)
                {
                    var builders = new MeshBuilder[3];
                    for (int layer = 0; layer < prototypes.Length; layer++)
                    {
                        var prototype = prototypes[layer];
                        var instances = data.ComputeDetailInstanceTransforms(px, py, layer, terrain.detailObjectDensity, out _);
                        if (instances.Length == 0) continue;
                        if (prototype.usePrototypeMesh)
                        {
                            if (prototype.prototype == null) continue;
                            if (prototype.useInstancing) AddInstancedDetails(terrain, prototype, instances, origin);
                            else BakeMeshDetails(builders[(int)DetailKind.Mesh] ??= new MeshBuilder(), prototype, instances, origin, white);
                            continue;
                        }
                        var kind = prototype.renderMode == DetailRenderMode.GrassBillboard ? DetailKind.Billboard : DetailKind.Grass;
                        var builder = builders[(int)kind] ??= new MeshBuilder();
                        var rect = rects[layer];
                        foreach (var i in instances)
                        {
                            float noise = Mathf.PerlinNoise(i.posX * prototype.noiseSpread, i.posZ * prototype.noiseSpread);
                            var top = Color.Lerp(prototype.dryColor, prototype.healthyColor, noise);
                            var topColor = ToColor32(new Color(top.r, top.g, top.b, 1f));
                            var bottomColor = ToColor32(new Color(top.r * 0.625f, top.g * 0.625f, top.b * 0.625f, 0f));
                            var normal = data.GetInterpolatedNormal(i.posX / size.x, i.posZ / size.z);
                            var position = origin + new Vector3(i.posX, i.posY, i.posZ);
                            float half = i.scaleXZ * 0.5f;
                            int first = builder.Vertices.Count;
                            var u0 = new Vector2(rect.xMin, rect.yMin);
                            var u1 = new Vector2(rect.xMax, rect.yMax);
                            if (kind == DetailKind.Grass)
                            {
                                var axis = new Vector3(Mathf.Cos(i.rotationY), 0f, -Mathf.Sin(i.rotationY)) * half;
                                var height = new Vector3(0f, i.scaleY, 0f);
                                var tangent = new Vector4(1f, 0f, 0f, 1f);
                                builder.Add(position - axis, normal, tangent, bottomColor, new Vector2(u0.x, u0.y));
                                builder.Add(position - axis + height, normal, tangent, topColor, new Vector2(u0.x, u1.y));
                                builder.Add(position + axis + height, normal, tangent, topColor, new Vector2(u1.x, u1.y));
                                builder.Add(position + axis, normal, tangent, bottomColor, new Vector2(u1.x, u0.y));
                            }
                            else
                            {
                                builder.Add(position, normal, new Vector4(-half, 0f, 0f, 0f), bottomColor, new Vector2(u0.x, u0.y));
                                builder.Add(position, normal, new Vector4(-half, i.scaleY, 0f, 0f), topColor, new Vector2(u0.x, u1.y));
                                builder.Add(position, normal, new Vector4(half, i.scaleY, 0f, 0f), topColor, new Vector2(u1.x, u1.y));
                                builder.Add(position, normal, new Vector4(half, 0f, 0f, 0f), bottomColor, new Vector2(u1.x, u0.y));
                            }
                            builder.Quad(first);
                        }
                    }
                    var patch = new DetailPatch();
                    for (int kind = 0; kind < 3; kind++)
                    {
                        var builder = builders[kind];
                        if (builder == null || builder.Vertices.Count == 0) continue;
                        var material = DetailMaterial(kind);
                        if (material == null) continue;
                        var mesh = new Mesh { name = $"Unbound Details {px},{py} {(DetailKind)kind}", hideFlags = HideFlags.HideAndDontSave };
                        if (builder.Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                        mesh.SetVertices(builder.Vertices);
                        mesh.SetNormals(builder.Normals);
                        mesh.SetTangents(builder.Tangents);
                        mesh.SetColors(builder.Colors);
                        mesh.SetUVs(0, builder.Uvs);
                        mesh.SetTriangles(builder.Indices, 0);
                        mesh.RecalculateBounds();
                        var renderer = (DetailRenderer)ComponentFactory.Create(typeof(DetailRenderer), terrain.gameObject);
                        renderer.hideFlags = HideFlags.HideAndDontSave;
                        renderer.m_Materials = new[] { material };
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = true;
                        var bounds = mesh.bounds;
                        // Billboards reach their size around their position.
                        if (kind == (int)DetailKind.Billboard) bounds.Expand(4f);
                        renderer.m_LocalBounds = new Bounds(bounds.center - origin, bounds.size);
                        patch.Renderers[kind] = renderer;
                        patch.Meshes[kind] = mesh;
                        if (!patch.HasBounds) { patch.Bounds = bounds; patch.HasBounds = true; }
                        else patch.Bounds.Encapsulate(bounds);
                    }
                    m_DetailPatches.Add(patch);
                }
        }

        static Mesh PrototypeMesh(GameObject prototype, out Material[] materials)
        {
            materials = null;
            var filter = prototype.GetComponent<MeshFilter>();
            var renderer = prototype.GetComponent<MeshRenderer>();
            if (filter == null || filter.sharedMesh == null) return null;
            materials = renderer != null ? renderer.sharedMaterials : null;
            return filter.sharedMesh;
        }

        static Matrix4x4 DetailPlacement(Vector3 origin, DetailInstanceTransform i) =>
            Matrix4x4.TRS(origin + new Vector3(i.posX, i.posY, i.posZ), Quaternion.Euler(0f, i.rotationY * Mathf.Rad2Deg, 0f), new Vector3(i.scaleXZ, i.scaleY, i.scaleXZ));

        /// <summary>Meshes drawn without instancing: each instance's mesh turned, scaled and placed,
        /// coloured the instance's colour, sampling the atlas's white (or the material's texture
        /// would be packed: not yet).</summary>
        static void BakeMeshDetails(MeshBuilder builder, DetailPrototype prototype, DetailInstanceTransform[] instances, Vector3 origin, Rect white)
        {
            var mesh = PrototypeMesh(prototype.prototype, out _);
            if (mesh == null) return;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;
            foreach (var i in instances)
            {
                float noise = Mathf.PerlinNoise(i.posX * prototype.noiseSpread, i.posZ * prototype.noiseSpread);
                var c = Color.Lerp(prototype.dryColor, prototype.healthyColor, noise);
                var color = ToColor32(new Color(c.r, c.g, c.b, 1f));
                var model = DetailPlacement(origin, i);
                var turn = Quaternion.Euler(0f, i.rotationY * Mathf.Rad2Deg, 0f);
                int first = builder.Vertices.Count;
                for (int v = 0; v < vertices.Length; v++)
                    builder.Add(model.MultiplyPoint3x4(vertices[v]), v < normals.Length ? turn * normals[v] : Vector3.up, new Vector4(1f, 0f, 0f, 1f), color, white.position);
                foreach (var t in triangles) builder.Indices.Add(first + t);
            }
        }

        void AddInstancedDetails(Terrain terrain, DetailPrototype prototype, DetailInstanceTransform[] instances, Vector3 origin)
        {
            var mesh = PrototypeMesh(prototype.prototype, out var materials);
            if (mesh == null || materials == null) return;
            foreach (var i in instances)
            {
                var renderer = (DetailRenderer)ComponentFactory.Create(typeof(DetailRenderer), terrain.gameObject);
                renderer.hideFlags = HideFlags.HideAndDontSave;
                renderer.m_Materials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                var model = DetailPlacement(origin, i);
                renderer.m_LocalBounds = Transformed(Matrix4x4.Translate(-origin) * model, mesh.bounds);
                m_InstancedDetails.Add(new InstancedDetail { Renderer = renderer, Mesh = mesh, Model = model, Position = origin + new Vector3(i.posX, i.posY, i.posZ) });
            }
        }
    }
}
