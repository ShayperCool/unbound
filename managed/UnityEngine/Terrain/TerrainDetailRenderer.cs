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
    // axis, its colour the dry colour lerped to the healthy one by the size noise in bytes (top;
    // the bottom scaled by 160/255 in bytes, alpha 0 for no waving), its normal the terrain's; a billboard's four
    // corners sit at the instance with their offsets in the tangent; meshes are baked through their
    // instance's transform (turned, tilted to the ground, scaled), their vertex colours (white
    // without them) tinted by its colour. The
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
        // float.Equals(float) treats NaN as equal, so a NaN density does not rebuild every frame.
        float m_DetailsDensity;

        /// <summary>Whether the built details no longer match what they were built from: the data,
        /// its heights, the terrain's place, the legacy distribution or the detail density (the
        /// terrain's, or the quality level's).</summary>
        internal bool DetailsStale(Terrain terrain, TerrainData data, Vector3 origin) =>
            m_DetailsData != data || m_DetailsVersion != data.m_DetailsVersion || m_DetailsHeights != data.m_HeightsVersion || m_DetailsOrigin != origin
            || m_DetailsLegacy != QualitySettings.useLegacyDetailDistribution || !m_DetailsDensity.Equals(terrain.detailObjectDensity);
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
                if (!patch.HasBounds || patch.Bounds.SqrDistanceUnchecked(camera.Position) > squared) continue;
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

        /// <summary>A prototype's place in the detail atlas, and whether a mesh samples a texture
        /// of its own there (its uvs) or white (one point at the white texture's centre).</summary>
        struct DetailTexture
        {
            internal Rect Rect;
            internal bool MeshTexture;

            /// <summary>A uv of the prototype's texture moved into its place in the atlas.</summary>
            internal Vector2 Place(Vector2 uv) => new Vector2(uv.x * Rect.width + Rect.x, uv.y * Rect.height + Rect.y);
        }

        /// <summary>The detail atlas: one texture for each prototype drawn without instancing, in
        /// order: a texture prototype's texture; a mesh's material's main texture (white when the
        /// material has none or no _MainTex); white for a prototype the terrain refuses. They are
        /// packed as Texture2D.PackTextures packs them (no padding, at most 2048 texels a side),
        /// into an sRGB atlas when any of them is sRGB, with transparent black between them. The
        /// atlas samples with clamped edges (measured along v) and without blending mip levels, and
        /// its mip levels are rebuilt from the first, so a texture beside empty texels keeps its own
        /// texels in the lower mips (matches the Unity 6000.0.71f1 Editor, tests/projects/grass:
        /// for a 16 × 32 grass texture and white the atlas is 32 × 32, the white 4 × 4 at (16, 0)).</summary>
        DetailTexture[] BuildDetailAtlas(DetailPrototype[] prototypes)
        {
            var result = new DetailTexture[prototypes.Length];
            var textures = new List<Texture2D>();
            var owners = new List<int>();
            bool srgb = false;
            for (int i = 0; i < prototypes.Length; i++)
            {
                var prototype = prototypes[i];
                // Instanced prototypes take no place, refused ones included.
                if (prototype == null || prototype.useInstancing) continue;
                Texture2D texture = null;
                if (prototype.Validate())
                {
                    if (!prototype.usePrototypeMesh) texture = prototype.prototypeTexture;
                    else
                    {
                        PrototypeMesh(prototype.prototype, out var materials);
                        var material = materials != null && materials.Length > 0 ? materials[0] : null;
                        if (material != null && material.HasProperty("_MainTex")) texture = material.GetTexture("_MainTex") as Texture2D;
                        result[i].MeshTexture = texture != null;
                    }
                }
                texture ??= Texture2D.whiteTexture;
                srgb |= !texture.m_Linear;
                textures.Add(texture);
                owners.Add(i);
            }
            if (m_DetailAtlas != null && m_DetailAtlas.m_Linear == srgb)
            {
                DestroyImmediate(m_DetailAtlas);
                m_DetailAtlas = null;
            }
            m_DetailAtlas ??= new Texture2D(4, 4, TextureFormat.RGBA32, true, !srgb) { name = "Unbound Detail Atlas", hideFlags = HideFlags.HideAndDontSave };
            if (textures.Count == 0) return result;
            var rects = TexturePacker.Pack(m_DetailAtlas, textures.ToArray(), 0, DetailAtlasMaximumSize, false);
            if (rects == null)
            {
                Warn("Unbound: the terrain's detail textures do not fit a 2048 × 2048 atlas; its texture details are drawn without them.");
                return result;
            }
            m_DetailAtlas.wrapMode = TextureWrapMode.Clamp;
            m_DetailAtlas.filterMode = FilterMode.Bilinear;
            for (int k = 0; k < owners.Count; k++) result[owners[k]].Rect = rects[k];
            return result;
        }

        /// <summary>The detail atlas's largest side (matches the Unity 6000.0.71f1 Editor).</summary>
        const int DetailAtlasMaximumSize = 2048;

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
            // Every kind samples the atlas: meshes their own texture's place in it, or white.
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

        /// <summary>An instance's colour: the healthy colour × the noise plus the dry colour × (1 −
        /// the noise), each channel clamped to [0, 1] and quantized to a byte (×255 + 0.5, truncated).</summary>
        static Color32 DetailColor(DetailPrototype prototype, float noise)
        {
            Color healthy = prototype.healthyColor, dry = prototype.dryColor;
            float rest = 1f - noise;
            static byte Quantize(float v) => (byte)(int)(Mathf.Clamp01(v) * 255f + 0.5f);
            return new Color32(Quantize(healthy.r * noise + dry.r * rest), Quantize(healthy.g * noise + dry.g * rest),
                Quantize(healthy.b * noise + dry.b * rest), Quantize(healthy.a * noise + dry.a * rest));
        }

        /// <summary>A grass quad's vertex colours: 160/255 grey and transparent at its foot (no
        /// waving there), white at its top (matches the Unity 6000.0.71f1 Editor, tests/projects/grass).</summary>
        static readonly Color32 s_QuadBottom = new Color32(160, 160, 160, 0), s_QuadTop = new Color32(255, 255, 255, 255);

        internal void BuildDetailsForTest(Terrain terrain, TerrainData data, Vector3 origin) => BuildDetails(terrain, data, origin);
        internal Texture2D DetailAtlasForTest => m_DetailAtlas;
        /// <summary>Each prototype's place in the detail atlas and whether a mesh samples its own texture.</summary>
        internal (Rect rect, bool meshTexture)[] DetailPlacesForTest(DetailPrototype[] prototypes)
        {
            var places = BuildDetailAtlas(prototypes);
            var result = new (Rect, bool)[places.Length];
            for (int i = 0; i < places.Length; i++) result[i] = (places[i].Rect, places[i].MeshTexture);
            return result;
        }
        internal static List<Vector3> BakedMeshNormalsForTest(TerrainData data, DetailPrototype prototype, Matrix4x4 placement) =>
            BakedMeshForTest(data, prototype, placement).Normals;
        internal static List<Color32> BakedMeshColorsForTest(TerrainData data, DetailPrototype prototype, Matrix4x4 placement) =>
            BakedMeshForTest(data, prototype, placement).Colors;
        static MeshBuilder BakedMeshForTest(TerrainData data, DetailPrototype prototype, Matrix4x4 placement)
        {
            var builder = new MeshBuilder();
            BakeMeshDetails(data, builder, prototype, new List<Matrix4x4> { placement }, new DetailTexture { Rect = new Rect(0f, 0f, 1f, 1f) });
            return builder;
        }
        internal static List<Color32> QuadColorsForTest(TerrainData data, DetailPrototype prototype, Matrix4x4 placement)
        {
            var builders = new MeshBuilder[3];
            AddQuads(data, prototype, new List<Matrix4x4> { placement }, false, new DetailTexture { Rect = new Rect(0f, 0f, 1f, 1f) }, builders);
            return builders[(int)DetailKind.Grass].Colors;
        }

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
            int patches = data.detailPatchCount;
            var layers = new TerrainData.DetailLayerSetup[prototypes.Length];
            for (int layer = 0; layer < prototypes.Length; layer++) layers[layer] = data.PrepareDetailLayer(layer);
            var places = BuildDetailAtlas(prototypes);
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
                                if (mesh != null) BakeMeshDetails(data, builders[(int)DetailKind.Mesh] ??= new MeshBuilder(), prototype, placements, places[index]);
                                else AddQuads(data, prototype, placements, mode == DetailRenderMode.GrassBillboard, places[index], builders);
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
        static void AddQuads(TerrainData data, DetailPrototype prototype, List<Matrix4x4> placements, bool billboard, DetailTexture place, MeshBuilder[] builders)
        {
            var size = data.size;
            var builder = builders[(int)(billboard ? DetailKind.Billboard : DetailKind.Grass)] ??= new MeshBuilder();
            // The texture's corners inset by half a texel, moved into its place in the atlas.
            var texture = prototype.prototypeTexture;
            float insetX = 0.5f / texture.width, insetY = 0.5f / texture.height;
            var u0 = place.Place(new Vector2(insetX, insetY));
            var u1 = place.Place(new Vector2(1f - insetX, 1f - insetY));
            foreach (var model in placements)
            {
                Vector3 position = model.GetColumn(3);
                // The quad spans the transform's x axis (its width) and rises along its y axis
                // (its height): turned, and tilted to the ground.
                Vector3 axis = model.GetColumn(0) * 0.5f, height = model.GetColumn(1);
                var color = DetailColor(prototype, Mathf.PerlinNoise(position.x * prototype.noiseSpread, position.z * prototype.noiseSpread));
                Color32 topColor = Tinted(s_QuadTop, color), bottomColor = Tinted(s_QuadBottom, color);
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
        /// (turned, tilted to the ground, scaled), its normals turned and tilted (in the Grass render
        /// mode the terrain's normal at every vertex), its vertex colours (white without them)
        /// tinted by the instance's colour, sampling the atlas at the material's texture (the mesh's
        /// uvs) or at white.</summary>
        static void BakeMeshDetails(TerrainData data, MeshBuilder builder, DetailPrototype prototype, List<Matrix4x4> placements, DetailTexture place)
        {
            var mesh = PrototypeMesh(prototype.prototype, out _);
            if (mesh == null) return;
            var vertices = mesh.m_Vertices;
            // With a texture of its own a mesh keeps its uvs, else every vertex samples the white
            // texture's centre; both moved into the place in the atlas. (A mesh drawn without
            // instancing has uvs: DetailPrototype.Validate refuses one without them.)
            var uvs = place.MeshTexture ? mesh.m_UVs[0] : null;
            var white = place.Place(new Vector2(0.5f, 0.5f));
            var normals = mesh.normals;
            var colors = mesh.m_Colors ?? Array.Empty<Color32>();
            var triangles = mesh.triangles;
            bool grass = prototype.renderMode == DetailRenderMode.Grass;
            foreach (var model in placements)
            {
                var position = model.GetColumn(3);
                var color = DetailColor(prototype, Mathf.PerlinNoise(position.x * prototype.noiseSpread, position.z * prototype.noiseSpread));
                Vector3 ax = model.GetColumn(0), ay = model.GetColumn(1), az = model.GetColumn(2);
                ax = ax.normalized; ay = ay.normalized; az = az.normalized;
                // In the Grass render mode every vertex takes the terrain's normal there, as a grass
                // quad does: the mesh's faces are lit alike (matches the Unity 6000.0.71f1 Editor,
                // tests/projects/grass Refusals; measured on flat terrain, where that normal is up).
                var ground = grass ? data.GetInterpolatedNormal(position.x / data.size.x, position.z / data.size.z) : Vector3.zero;
                int first = builder.Vertices.Count;
                for (int v = 0; v < vertices.Length; v++)
                {
                    var n = v < normals.Length ? normals[v] : Vector3.up;
                    var tint = v < colors.Length ? Tinted(colors[v], color) : color;
                    var uv = uvs == null ? white : v < uvs.Length ? place.Place(new Vector2(uvs[v].x, uvs[v].y)) : place.Place(Vector2.zero);
                    builder.Add(model.MultiplyPoint3x4(vertices[v]), grass ? ground : ax * n.x + ay * n.y + az * n.z, new Vector4(1f, 0f, 0f, 1f), tint, uv);
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
