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
    // drawn without instancing, relative to the terrain (the shaders compare its vertices with the
    // camera less the terrain's position), drawn with Unity's detail shaders
    // (Hidden/TerrainEngine/Details/...) and a texture atlas of the prototypes; meshes drawn with
    // instancing are their prototype's meshes and materials at each instance. As the Editor builds
    // them (measured: a project overriding the detail shaders dumped the vertices it draws): a
    // grass quad spans its width along its transform's x axis (uv's left at -x) and rises along its y
    // axis, its colour the dry colour lerped to the healthy one by the size noise (top; the
    // bottom 0.625 of it, alpha 0 for no waving), its normal the terrain's; a billboard's four
    // corners sit at the instance with their offsets in the tangent; meshes are baked through their
    // instance's transform (turned, tilted to the ground, scaled), coloured its colour. The
    // shaders' globals: _WaveAndDistance (frame time × speed × 0.04, strength × 0.5, amount × 6,
    // distance²), _CameraPosition (the camera less the terrain's position, 1 / distance²), the
    // camera's right and up, the waving tint.
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
        readonly List<Matrix4x4> m_Placements = new List<Matrix4x4>();
        TerrainData m_DetailsData;
        int m_DetailsVersion = -1, m_DetailsHeights = -1;
        Vector3 m_DetailsOrigin;
        bool m_DetailsLegacy;
        float m_DetailsDensity = float.NaN;

        /// <summary>Whether the built details no longer match what they were built from: the data,
        /// its heights, the terrain's place, the legacy distribution or the detail density (the
        /// terrain's, or the quality level's).</summary>
        internal bool DetailsStale(Terrain terrain, TerrainData data, Vector3 origin) =>
            m_DetailsData != data || m_DetailsVersion != data.m_DetailsVersion || m_DetailsHeights != data.m_HeightsVersion || m_DetailsOrigin != origin
            || m_DetailsLegacy != QualitySettings.useLegacyDetailDistribution || !(m_DetailsDensity == terrain.detailObjectDensity);
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
            if (DetailsStale(terrain, data, origin))
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
                    // As the trees: the terrain renderer's rendering layers (unmeasured).
                    renderer.renderingLayerMask = renderingLayerMask;
                    FrameBuilder.AddMeshDraws(renderer, patch.Meshes[kind], Matrix4x4.Translate(origin), Color.white, camera, into);
                }
            }
            foreach (var detail in m_InstancedDetails)
            {
                if ((detail.Position - camera.Position).sqrMagnitude > squared) continue;
                detail.Renderer.renderingLayerMask = renderingLayerMask;
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

        internal void BuildDetailsForTest(Terrain terrain, TerrainData data, Vector3 origin) => BuildDetails(terrain, data, origin);

        void BuildDetails(Terrain terrain, TerrainData data, Vector3 origin)
        {
            DestroyDetails();
            m_DetailsData = data;
            m_DetailsVersion = data.m_DetailsVersion;
            m_DetailsHeights = data.m_HeightsVersion;
            m_DetailsOrigin = origin;
            m_DetailsLegacy = QualitySettings.useLegacyDetailDistribution;
            m_DetailsDensity = terrain.detailObjectDensity;
            var prototypes = data.detailPrototypes;
            if (prototypes.Length == 0 || data.detailResolution == 0) return;
            var rects = BuildDetailAtlas(prototypes, out var white);
            int patches = data.detailPatchCount;
            var layers = new TerrainData.DetailLayerSetup[prototypes.Length];
            for (int layer = 0; layer < prototypes.Length; layer++) layers[layer] = data.PrepareDetailLayer(layer);
            var placements = m_Placements;
            for (int py = 0; py < patches; py++)
                for (int px = 0; px < patches; px++)
                {
                    var builders = new MeshBuilder[3];
                    var order = data.PatchLayers(px, py);
                    if (order != null)
                    {
                        float density = terrain.detailObjectDensity;
                        // Instanced meshes, layer after layer in the patch's order, the sequence's
                        // row numbering running on from layer to layer: the second instanced layer
                        // of a patch is drawn off the places ComputeDetailInstanceTransforms reports
                        // (matches the Unity 6000.0.71f1 Editor, tests/projects/grass Instanced).
                        int rows = 0;
                        foreach (var index in order)
                        {
                            var setup = layers[index];
                            if (!setup.IsValid || !setup.Prototype.usePrototypeMesh || !setup.Prototype.useInstancing) continue;
                            data.PlaceDetails(setup, px, py, density, placements, out _, rows);
                            rows += data.detailResolutionPerPatch;
                            AddInstancedDetails(terrain, setup.Prototype, placements, origin);
                        }
                        // The patch's mesh of each render mode, its layers in the patch's order.
                        foreach (var mode in s_PatchMeshModes)
                        {
                            int placed = 0, used = 0;
                            foreach (var index in order)
                            {
                                var setup = layers[index];
                                var prototype = setup.Prototype;
                                if (!setup.IsValid || prototype.useInstancing || prototype.renderMode != mode) continue;
                                var mesh = prototype.usePrototypeMesh ? PrototypeMesh(prototype.prototype, out _) : null;
                                int perInstance = mesh != null ? mesh.vertexCount : 4;
                                if (data.PlacePatchMeshDetails(setup, px, py, density, placed, perInstance, ref used, placements))
                                    placed++;
                                if (placements.Count == 0) continue;
                                if (mesh != null) BakeMeshDetails(builders[(int)DetailKind.Mesh] ??= new MeshBuilder(), prototype, placements, white);
                                else AddQuads(data, prototype, placements, mode == DetailRenderMode.GrassBillboard, rects[index], builders);
                            }
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
                        renderer.m_LocalBounds = bounds;
                        bounds.center += origin;
                        patch.Renderers[kind] = renderer;
                        patch.Meshes[kind] = mesh;
                        if (!patch.HasBounds) { patch.Bounds = bounds; patch.HasBounds = true; }
                        else patch.Bounds.Encapsulate(bounds);
                    }
                    m_DetailPatches.Add(patch);
                }
        }

        /// <summary>Grass and billboard quads of a texture prototype's instances.</summary>
        static void AddQuads(TerrainData data, DetailPrototype prototype, List<Matrix4x4> placements, bool billboard, Rect rect, MeshBuilder[] builders)
        {
            var size = data.size;
            var builder = builders[(int)(billboard ? DetailKind.Billboard : DetailKind.Grass)] ??= new MeshBuilder();
            var u0 = new Vector2(rect.xMin, rect.yMin);
            var u1 = new Vector2(rect.xMax, rect.yMax);
            foreach (var model in placements)
            {
                Vector3 position = model.GetColumn(3);
                // The quad spans the transform's x axis (its width) and rises along its y axis
                // (its height): turned, and tilted to the ground.
                Vector3 axis = model.GetColumn(0) * 0.5f, height = model.GetColumn(1);
                float noise = Mathf.PerlinNoise(position.x * prototype.noiseSpread, position.z * prototype.noiseSpread);
                var top = Color.Lerp(prototype.dryColor, prototype.healthyColor, noise);
                var topColor = ToColor32(new Color(top.r, top.g, top.b, 1f));
                var bottomColor = ToColor32(new Color(top.r * 0.625f, top.g * 0.625f, top.b * 0.625f, 0f));
                var normal = data.GetInterpolatedNormal(position.x / size.x, position.z / size.z);
                int first = builder.Vertices.Count;
                if (!billboard)
                {
                    var tangent = new Vector4(1f, 0f, 0f, 1f);
                    builder.Add(position - axis, normal, tangent, bottomColor, new Vector2(u0.x, u0.y));
                    builder.Add(position - axis + height, normal, tangent, topColor, new Vector2(u0.x, u1.y));
                    builder.Add(position + axis + height, normal, tangent, topColor, new Vector2(u1.x, u1.y));
                    builder.Add(position + axis, normal, tangent, bottomColor, new Vector2(u1.x, u0.y));
                }
                else
                {
                    // Billboards face the camera: only their size counts.
                    float half = axis.magnitude, tall = height.magnitude;
                    builder.Add(position, normal, new Vector4(-half, 0f, 0f, 0f), bottomColor, new Vector2(u0.x, u0.y));
                    builder.Add(position, normal, new Vector4(-half, tall, 0f, 0f), topColor, new Vector2(u0.x, u1.y));
                    builder.Add(position, normal, new Vector4(half, tall, 0f, 0f), topColor, new Vector2(u1.x, u1.y));
                    builder.Add(position, normal, new Vector4(half, 0f, 0f, 0f), bottomColor, new Vector2(u1.x, u0.y));
                }
                builder.Quad(first);
            }
        }

        /// <summary>The render modes with a mesh per patch, in the order they are built.</summary>
        static readonly DetailRenderMode[] s_PatchMeshModes = { DetailRenderMode.GrassBillboard, DetailRenderMode.VertexLit, DetailRenderMode.Grass };

        static Mesh PrototypeMesh(GameObject prototype, out Material[] materials)
        {
            materials = null;
            var filter = prototype.GetComponent<MeshFilter>();
            var renderer = prototype.GetComponent<MeshRenderer>();
            if (filter == null || filter.sharedMesh == null) return null;
            materials = renderer != null ? renderer.sharedMaterials : null;
            return filter.sharedMesh;
        }

        /// <summary>Meshes drawn without instancing: each instance's mesh through its transform
        /// (turned, tilted to the ground, scaled), its normals turned and tilted, coloured the
        /// instance's colour, sampling the atlas's white (or the material's texture would be
        /// packed: not yet).</summary>
        static void BakeMeshDetails(MeshBuilder builder, DetailPrototype prototype, List<Matrix4x4> placements, Rect white)
        {
            var mesh = PrototypeMesh(prototype.prototype, out _);
            if (mesh == null) return;
            var vertices = mesh.m_Vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;
            foreach (var model in placements)
            {
                var position = model.GetColumn(3);
                float noise = Mathf.PerlinNoise(position.x * prototype.noiseSpread, position.z * prototype.noiseSpread);
                var c = Color.Lerp(prototype.dryColor, prototype.healthyColor, noise);
                var color = ToColor32(new Color(c.r, c.g, c.b, 1f));
                Vector3 ax = model.GetColumn(0), ay = model.GetColumn(1), az = model.GetColumn(2);
                ax = ax.normalized; ay = ay.normalized; az = az.normalized;
                int first = builder.Vertices.Count;
                for (int v = 0; v < vertices.Length; v++)
                {
                    var n = v < normals.Length ? normals[v] : Vector3.up;
                    builder.Add(model.MultiplyPoint3x4(vertices[v]), ax * n.x + ay * n.y + az * n.z, new Vector4(1f, 0f, 0f, 1f), color, white.position);
                }
                foreach (var t in triangles) builder.Indices.Add(first + t);
            }
        }

        void AddInstancedDetails(Terrain terrain, DetailPrototype prototype, List<Matrix4x4> placements, Vector3 origin)
        {
            var mesh = PrototypeMesh(prototype.prototype, out var materials);
            if (mesh == null || materials == null) return;
            foreach (var placement in placements)
            {
                var renderer = (DetailRenderer)ComponentFactory.Create(typeof(DetailRenderer), terrain.gameObject);
                renderer.hideFlags = HideFlags.HideAndDontSave;
                renderer.m_Materials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.m_LocalBounds = Transformed(placement, mesh.bounds);
                var model = Matrix4x4.Translate(origin) * placement;
                m_InstancedDetails.Add(new InstancedDetail { Renderer = renderer, Mesh = mesh, Model = model, Position = origin + (Vector3)placement.GetColumn(3) });
            }
        }
    }
}
