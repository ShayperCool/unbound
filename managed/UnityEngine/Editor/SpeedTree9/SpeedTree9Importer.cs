using System;
using System.Collections.Generic;
using System.IO;
using Unbound.SpeedTree;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UnityEditor.SpeedTree.Importer
{
    /// <summary>
    /// Imports a SpeedTree 9 (<c>.st9</c>) file into the objects Unity 6000.0.71f1's importer makes
    /// (tests/projects/speedtree9): a main GameObject with an optional kinematic Rigidbody and a
    /// LODGroup, a child <c>LOD&lt;i&gt;</c> per level of detail (MeshFilter, MeshRenderer, Tree), a
    /// mesh <c>LOD&lt;i&gt;_Mesh</c> per level, a material per SpeedTree material (and level when the
    /// level overrides the settings) unless <c>externalObjects</c> remaps it, collider children, the
    /// wind asset and the importer's output data. The settings are the .meta's.
    /// </summary>
    [ScriptedImporter(3, "st9", AllowCaching = true)]
    public partial class SpeedTree9Importer : ScriptedImporter
    {
        [SerializeField] internal MeshSettings m_MeshSettings = new MeshSettings();
        [SerializeField] internal MaterialSettings m_MaterialSettings = new MaterialSettings();
        [SerializeField] internal LightingSettings m_LightingSettings = new LightingSettings();
        [SerializeField] internal AdditionalSettings m_AdditionalSettings = new AdditionalSettings();
        [SerializeField] internal LODSettings m_LODSettings = new LODSettings();
        [SerializeField] internal List<PerLODSettings> m_PerLODSettings = new List<PerLODSettings>();
        [SerializeField] internal WindSettings m_WindSettings = new WindSettings();
        [SerializeField] internal int m_MaterialVersion = 1;
        [SerializeField] internal SpeedTreeImporterOutputData m_OutputImporterData;

        public SpeedTree9Importer() { }

        /// <summary>The pipeline's SpeedTree 9 shader, else the built-in one.</summary>
        internal static bool TryGetShaderForCurrentRenderPipeline(out Shader shader)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            shader = pipeline != null ? pipeline.defaultSpeedTree9Shader : null;
            if (shader == null) shader = Shader.Find("Nature/SpeedTree9");
            return shader != null;
        }

        /// <summary>The most levels of detail the importer keeps (the LODGroup inspector's colors).</summary>
        const int MaxLods = 8;

        /// <summary>Collision objects whose two ends are closer than this (squared) are spheres.</summary>
        const float SphereSqrLength = 0.001f;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] bytes = File.ReadAllBytes(ctx.assetPath);
            if (!SpeedTree9File.HasSignature(bytes))
            {
                ctx.LogImportError("Error while initializing the SpeedTree9 reader: InvalidSignature.");
                return;
            }
            new Import(this, ctx, SpeedTree9File.Read(bytes)).Run();
        }

        /// <summary>One import's state.</summary>
        sealed class Import
        {
            readonly SpeedTree9Importer m_Importer;
            readonly AssetImportContext m_Context;
            readonly SpeedTree9File m_Tree;
            readonly string m_Directory;
            readonly Dictionary<AssetImporter.SourceAssetIdentifier, Object> m_External;
            Shader m_Shader;
            SpeedTreeImporterOutputData m_Output;
            SpeedTreeWindAsset m_WindAsset;
            int m_LodCount;
            bool m_HasFacingData, m_HasBranch2Data, m_LastLodIsBillboard, m_WindEnabled;
            float m_Scale;

            internal Import(SpeedTree9Importer importer, AssetImportContext ctx, SpeedTree9File tree)
            {
                m_Importer = importer;
                m_Context = ctx;
                m_Tree = tree;
                m_Directory = Path.GetDirectoryName(ctx.assetPath)?.Replace('\\', '/') + "/";
                m_External = importer.GetExternalObjectMap();
            }

            List<PerLODSettings> PerLod => m_Importer.m_PerLODSettings;

            internal void Run()
            {
                m_LodCount = m_Tree.Lods.Length;
                if (m_LodCount > MaxLods)
                {
                    Debug.LogWarningFormat("Number of LOD meshes in asset ({0}) is larger than the maximum number supported by Unity GUI ({1}).\nImporting only the first {1} LOD meshes.", m_LodCount, MaxLods);
                    m_LodCount = MaxLods;
                }
                for (int i = 0; i < m_LodCount && !m_HasFacingData; i++)
                    foreach (var draw in m_Tree.Lods[i].DrawCalls)
                        if (draw.ContainsFacingGeometry) { m_HasFacingData = true; break; }
                var wind = m_Tree.WindConfig;
                m_HasBranch2Data = wind.DoBranch2;
                m_LastLodIsBillboard = m_Tree.LastLodIsBillboard;
                m_WindEnabled = (wind.DoShared || wind.DoBranch1 || wind.DoBranch2 || wind.DoRipple) && m_Importer.m_WindSettings.enableWind;

                if (!TryGetShaderForCurrentRenderPipeline(out m_Shader))
                {
                    m_Context.LogImportError("SpeedTree9 shader is invalid, cannot create Materials for this SpeedTree asset.");
                    return;
                }
                m_Output = SpeedTreeImporterOutputData.Create();
                m_Output.hasBillboard = m_LastLodIsBillboard;
                m_Scale = ScaleFactor(m_Importer.m_MeshSettings);
                if (m_WindEnabled)
                {
                    m_Output.m_WindConfig = WindConfig(wind);
                    var settings = m_Importer.m_WindSettings;
                    m_Output.m_WindConfig.strengthResponse = settings.strenghResponse;
                    m_Output.m_WindConfig.directionResponse = settings.directionResponse;
                    m_Output.m_WindConfig.windIndependence = settings.randomness;
                    m_WindAsset = new SpeedTreeWindAsset(1, m_Output.m_WindConfig) { name = "SpeedTreeWind" };
                }

                var main = new GameObject("SpeedTree");
                if (m_Importer.m_AdditionalSettings.generateRigidbody)
                {
                    var body = main.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = true;
                }
                m_Context.AddObjectToAsset("SpeedTree", main);
                m_Context.SetMainObject(main);
                m_Output.mainObject = main;

                FitPerLodSettings();
                CreateLods(main);
                AddMaterials();
                if (m_Importer.m_AdditionalSettings.generateColliders && m_Tree.Collisions.Length != 0) CreateColliders(main);
                if (m_WindEnabled) m_Context.AddObjectToAsset("SpeedTreeWind", m_WindAsset);
                m_Context.AddObjectToAsset(m_Output.name, m_Output);
                RunMaterialCallbacks(main);
            }

            static float ScaleFactor(MeshSettings settings)
            {
                switch (settings.unitConversion)
                {
                    case SpeedTreeImporterCommon.STUnitConversion.kLeaveAsIs: settings.scaleFactor = 1f; break;
                    case SpeedTreeImporterCommon.STUnitConversion.kFeetToMeters: settings.scaleFactor = SpeedTreeConstants.kFeetToMetersRatio; break;
                    case SpeedTreeImporterCommon.STUnitConversion.kCentimetersToMeters: settings.scaleFactor = SpeedTreeConstants.kCentimetersToMetersRatio; break;
                    case SpeedTreeImporterCommon.STUnitConversion.kInchesToMeters: settings.scaleFactor = SpeedTreeConstants.kInchesToMetersRatio; break;
                }
                return settings.scaleFactor;
            }

            /// <summary>Per-level settings: the .meta's when it lists one per level; when it lists fewer,
            /// new ones (the billboard level without probes and reflections, overriding), each half the
            /// height of the one before from 0.5, the last at 0.01. More than the levels fail the import
            /// as in the Editor.</summary>
            void FitPerLodSettings()
            {
                var perLod = PerLod;
                if (perLod.Count > m_LodCount)
                {
                    perLod.RemoveRange(m_LodCount, perLod.Count);
                }
                else if (perLod.Count < m_LodCount)
                {
                    perLod.Clear();
                    var material = m_Importer.m_MaterialSettings;
                    for (int i = 0; i < m_LodCount; i++)
                    {
                        bool billboard = m_LastLodIsBillboard && i == m_LodCount - 1;
                        perLod.Add(new PerLODSettings
                        {
                            enableSettingOverride = billboard,
                            enableBump = material.enableBumpMapping,
                            enableHue = material.enableHueVariation,
                            enableSubsurface = material.enableSubsurfaceScattering,
                            castShadows = true,
                            receiveShadows = true,
                            useLightProbes = !billboard,
                            reflectionProbeUsage = billboard ? ReflectionProbeUsage.Off : ReflectionProbeUsage.BlendProbes,
                        });
                    }
                    for (int i = 0; i < m_LodCount; i++) perLod[i].height = i == 0 ? 0.5f : perLod[i - 1].height * 0.5f;
                    if (m_LodCount != 0) perLod[perLod.Count - 1].height = 0.01f;
                }
            }

            bool IsBillboardLod(int lod) => m_LastLodIsBillboard && lod == m_LodCount - 1;

            void CreateLods(GameObject main)
            {
                var lods = new LOD[m_LodCount];
                for (int i = 0; i < m_LodCount; i++)
                {
                    var lod = m_Tree.Lods[i];
                    var mesh = CreateMesh(lod, i);
                    for (int d = 0; d < lod.DrawCalls.Length; d++)
                    {
                        var draw = lod.DrawCalls[d];
                        UseMaterial(m_Tree.Materials[draw.MaterialIndex], i);
                        var indices = new int[draw.IndexCount];
                        for (int k = 0; k < indices.Length; k++) indices[k] = (int)lod.Indices[draw.IndexStart + k];
                        mesh.SetIndices(indices, MeshTopology.Triangles, d, calculateBounds: true);
                    }
                    mesh.RecalculateUVDistributionMetrics();
                    var go = new GameObject("LOD" + i);
                    go.transform.parent = main.transform;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    lods[i] = new LOD(PerLod[i].height, new Renderer[] { renderer });
                    SetRendererSettings(renderer, i);
                    var tree = go.AddComponent<UnityEngine.Tree>();
                    if (m_WindEnabled) tree.windAsset = m_WindAsset;
                    m_Context.AddObjectToAsset(mesh.name, mesh);
                }
                var group = main.AddComponent<LODGroup>();
                group.SetLODs(lods);
                var lodSettings = m_Importer.m_LODSettings;
                if (lodSettings.enableSmoothLODTransition && lods.Length > 0)
                {
                    group.fadeMode = LODFadeMode.CrossFade;
                    group.animateCrossFading = lodSettings.animateCrossFading;
                    group.lastLODBillboard = m_LastLodIsBillboard;
                    // The fade widths of the settings do not reach the group: its levels keep 0
                    // (measured: Settings.st9, which does not animate cross-fading).
                }
                group.RecalculateBounds();
            }

            Mesh CreateMesh(SpeedTree9File.Lod lod, int index)
            {
                bool billboard = IsBillboardLod(index);
                int count = lod.Vertices.Length;
                var positions = new Vector3[count];
                var normals = new Vector3[count];
                var tangents = new Vector4[count];
                var colors = new Color32[count];
                var uvs = new List<Vector4>[billboard ? 2 : 4];
                for (int c = 0; c < uvs.Length; c++) uvs[c] = new List<Vector4>(count);
                float scale = m_Scale;
                for (int i = 0; i < count; i++)
                {
                    var v = lod.Vertices[i];
                    // Camera-facing vertices store their corner mirrored in x.
                    var p = new Vector3(v.Anchor.X + (v.CameraFacing ? -v.Offset.X : v.Offset.X), v.Anchor.Y + v.Offset.Y, v.Anchor.Z + v.Offset.Z);
                    positions[i] = p * scale;
                    var normal = new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z);
                    normals[i] = normal;
                    var tangent = new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z);
                    // The handedness compares only the binormal's x and y with normal x tangent.
                    float side = Vector3.Dot(Vector3.Cross(normal, tangent), new Vector3(v.Binormal.X, v.Binormal.Y, 0f));
                    tangents[i] = new Vector4(v.Tangent.X, v.Tangent.Y, v.Tangent.Z, side < 0f ? -1f : 1f);
                    colors[i] = new Color(v.Color.X * v.AmbientOcclusion, v.Color.Y * v.AmbientOcclusion, v.Color.Z * v.AmbientOcclusion, v.BlendWeight);
                    uvs[0].Add(new Vector4(v.U, v.V, v.BranchWind1.X, v.BranchWind1.Y));
                    uvs[1].Add(new Vector4(v.LightmapU, v.LightmapV, v.BranchWind1.Z, v.RippleWeight));
                    if (billboard) continue;
                    // Without branch 2 wind the facing anchor (scaled) and flag fill the third channel;
                    // with branch 2 wind and facing leaves, the fourth.
                    var anchor = m_HasFacingData ? new Vector3(v.Anchor.X * scale, v.Anchor.Y * scale, v.Anchor.Z * scale) : Vector3.zero;
                    float facing = v.CameraFacing ? 1f : 0f;
                    uvs[2].Add(m_HasBranch2Data
                        ? new Vector4(v.BranchWind2.X, v.BranchWind2.Y, v.BranchWind2.Z, 0f)
                        : new Vector4(anchor.x, anchor.y, anchor.z, facing));
                    uvs[3].Add(m_HasBranch2Data && m_HasFacingData ? new Vector4(anchor.x, anchor.y, anchor.z, facing) : Vector4.zero);
                }
                var mesh = new Mesh
                {
                    name = "LOD" + index + "_Mesh",
                    indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    subMeshCount = lod.DrawCalls.Length,
                    vertices = positions,
                    normals = normals,
                    tangents = tangents,
                    colors32 = colors,
                };
                for (int c = 0; c < uvs.Length; c++) mesh.SetUVs(c, uvs[c]);
                return mesh;
            }

            void SetRendererSettings(MeshRenderer renderer, int lod)
            {
                var lighting = m_Importer.m_LightingSettings;
                bool cast = lighting.enableShadowCasting, probes = lighting.enableLightProbes, receive = lighting.enableShadowReceiving;
                var reflection = lighting.reflectionProbeEnumValue;
                var level = PerLod[lod];
                if (level.enableSettingOverride)
                {
                    cast = level.castShadows;
                    probes = level.useLightProbes;
                    receive = level.receiveShadows;
                    reflection = level.reflectionProbeUsage;
                }
                renderer.sharedMaterials = LodMaterials(lod);
                renderer.motionVectorGenerationMode = m_Importer.m_AdditionalSettings.motionVectorModeEnumValue;
                renderer.receiveShadows = receive;
                renderer.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.lightProbeUsage = probes ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;
                renderer.reflectionProbeUsage = reflection;
            }

            Material[] LodMaterials(int lod)
            {
                var all = m_Output.lodMaterials.materials;
                if (all == null || all.Count == 0) return null;
                var indices = m_Output.lodMaterials.lodToMaterials[lod];
                var result = new Material[indices.Count];
                for (int i = 0; i < indices.Count; i++) result[i] = all[indices[i]].material;
                return result;
            }

            /// <summary>The material a draw call of level <paramref name="lod"/> uses, made once per
            /// name: the SpeedTree material's name, with <c>LOD&lt;i&gt;</c> added when the level
            /// overrides the settings (not the billboard level). A remapped name takes the external
            /// material.</summary>
            void UseMaterial(SpeedTree9File.Material source, int lod)
            {
                string name = source.Name;
                if (PerLod[lod].enableSettingOverride && !IsBillboardLod(lod)) name += "LOD" + lod;
                var bookkeeping = m_Output.lodMaterials;
                if (TryGetExternal(name, out var external))
                {
                    NoteSpecialProperties(external);
                    int index = bookkeeping.materials.FindIndex(m => m.defaultName == name);
                    if (index == -1)
                    {
                        bookkeeping.materials.Add(new MaterialInfo { material = external, defaultName = name, exported = true });
                        index = bookkeeping.materials.Count - 1;
                    }
                    bookkeeping.matNameToIndex[name] = index;
                }
                else if (!bookkeeping.matNameToIndex.ContainsKey(name))
                {
                    bookkeeping.materials.Add(new MaterialInfo { material = CreateMaterial(source, lod, name), defaultName = name, exported = false });
                    bookkeeping.matNameToIndex.Add(name, bookkeeping.materials.Count - 1);
                }
                bookkeeping.AddLodMaterialIndex(lod, bookkeeping.matNameToIndex[name]);
            }

            bool TryGetExternal(string name, out Material material)
            {
                foreach (var pair in m_External)
                    if (pair.Key.name == name)
                    {
                        material = pair.Value as Material;
                        return material != null;
                    }
                material = null;
                return false;
            }

            void AddMaterials()
            {
                m_Output.materialsIdentifiers.Clear();
                foreach (var info in m_Output.lodMaterials.materials)
                {
                    m_Output.hasEmbeddedMaterials |= !info.exported;
                    if (!info.exported) m_Context.AddObjectToAsset(info.material.name, info.material);
                    m_Output.materialsIdentifiers.Add(new AssetIdentifier(info.material.GetType(), info.defaultName));
                }
            }

            void NoteSpecialProperties(Material material)
            {
                m_Output.hasAlphaClipThreshold |= material.HasProperty(SpeedTreeImporterCommon.MaterialProperties.AlphaClipThresholdID);
                m_Output.hasTransmissionScale |= material.HasProperty(SpeedTreeImporterCommon.MaterialProperties.TransmissionScaleID);
            }

            Material CreateMaterial(SpeedTree9File.Material source, int lod, string name)
            {
                var material = new Material(m_Shader) { name = name };
                NoteSpecialProperties(material);
                SetTexturesAndColors(source, material, lod);
                SetOtherProperties(source, material);
                SetWindToggles(material, source.Billboard);
                return material;
            }

            /// <summary>Sets map <paramref name="index"/>'s texture when the map is used and its path
            /// names a texture; whether it did.</summary>
            bool SetTexture(Material material, SpeedTree9File.Material source, int index, int property)
            {
                if (source.Maps.Length <= index) return false;
                var map = source.Maps[index];
                if (!map.Used || string.IsNullOrEmpty(map.Path)) return false;
                var texture = LoadTexture(map.Path);
                if (texture == null) return false;
                material.SetTexture(property, texture);
                return true;
            }

            /// <summary>A map's texture: the path relative to the tree, else the first asset whose name
            /// matches the file's.</summary>
            Texture2D LoadTexture(string mapPath)
            {
                if (m_Context.GetReferenceToAssetMainObject(m_Directory + mapPath) is Texture2D texture) return texture;
                var found = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(mapPath));
                if (found == null || found.Length == 0) return null;
                return m_Context.GetReferenceToAssetMainObject(AssetDatabase.GUIDToAssetPath(found[0])) as Texture2D;
            }

            /// <summary>Whether the material has the texture property and holds a texture in it.</summary>
            static bool HoldsTexture(Material material, int property) =>
                material.HasProperty(property) && material.GetTexture(property) != null;

            void SetTexturesAndColors(SpeedTree9File.Material source, Material material, int lod)
            {
                var settings = m_Importer.m_MaterialSettings;
                bool hue = settings.enableHueVariation, bump = settings.enableBumpMapping, subsurface = settings.enableSubsurfaceScattering;
                var level = PerLod[lod];
                if (level.enableSettingOverride)
                {
                    hue = level.enableHue;
                    bump = level.enableBump;
                    subsurface = level.enableSubsurface;
                }
                // A map set into a property the shader lacks takes the map's colour instead.
                if (SetTexture(material, source, 0, SpeedTreeImporterCommon.MaterialProperties.MainTexID))
                    material.SetColor(SpeedTreeImporterCommon.MaterialProperties.ColorTintID,
                        HoldsTexture(material, SpeedTreeImporterCommon.MaterialProperties.MainTexID) ? settings.mainColor : settings.mainColor * MapColor(source.Maps[3]));
                bool normal = SetTexture(material, source, 1, SpeedTreeImporterCommon.MaterialProperties.NormalMapID);
                material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.NormalMapKwToggleID, normal && bump ? 1f : 0f);
                bool extra = SetTexture(material, source, 2, SpeedTreeImporterCommon.MaterialProperties.ExtraTexID);
                bool extraHeld = extra && HoldsTexture(material, SpeedTreeImporterCommon.MaterialProperties.ExtraTexID);
                if (extraHeld)
                {
                    material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.GlossinessID, 1f);
                    material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.MetallicID, 1f);
                }
                else if (extra)
                {
                    material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.GlossinessID, source.Maps[2].R);
                    material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.MetallicID, source.Maps[2].G);
                }
                material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.ExtraMapKwToggleID, extraHeld ? 1f : 0f);
                if (source.TwoSided || source.Billboard)
                {
                    bool subsurfaceMap = SetTexture(material, source, 3, SpeedTreeImporterCommon.MaterialProperties.SubsurfaceTexID);
                    material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.SubsurfaceKwToggleID, subsurfaceMap && subsurface ? 1f : 0f);
                    if (subsurfaceMap)
                        material.SetColor(SpeedTreeImporterCommon.MaterialProperties.SubsurfaceColorID,
                            HoldsTexture(material, SpeedTreeImporterCommon.MaterialProperties.SubsurfaceTexID) ? Color.white : MapColor(source.Maps[3]));
                    if (m_Output.hasAlphaClipThreshold && material.HasFloat(SpeedTreeImporterCommon.MaterialProperties.AlphaClipThresholdID))
                        material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.AlphaClipThresholdID, settings.alphaClipThreshold);
                    if (m_Output.hasTransmissionScale && material.HasFloat(SpeedTreeImporterCommon.MaterialProperties.TransmissionScaleID))
                        material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.TransmissionScaleID, settings.transmissionScale);
                }
                material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.HueVariationKwToggleID, hue ? 1f : 0f);
                material.SetColor(SpeedTreeImporterCommon.MaterialProperties.HueVariationColorID, settings.hueVariation);
            }

            static Color MapColor(SpeedTree9File.Map map) => new Color(map.R, map.G, map.B, map.A);

            void SetOtherProperties(SpeedTree9File.Material source, Material material)
            {
                material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.BillboardKwToggleID, source.Billboard ? 1f : 0f);
                if (source.Billboard) material.EnableKeyword(SpeedTreeImporterCommon.MaterialKeywords.BillboardID);
                material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.LeafFacingKwToggleID, m_HasFacingData ? 1f : 0f);
                // Each pipeline's shader names its two-sided controls differently.
                float normalMode = source.FlipNormalsOnBackside ? 0f : 2f;
                SetIfPresent(material, SpeedTreeImporterCommon.MaterialProperties.DoubleSidedToggleID, source.TwoSided ? 1f : 0f);
                SetIfPresent(material, SpeedTreeImporterCommon.MaterialProperties.DoubleSidedNormalModeID, normalMode);
                if (material.HasVector(SpeedTreeImporterCommon.MaterialProperties.DiffusionProfileAssetID))
                    material.SetVector(SpeedTreeImporterCommon.MaterialProperties.DiffusionProfileAssetID, m_Importer.m_MaterialSettings.diffusionProfileAssetID);
                SetIfPresent(material, SpeedTreeImporterCommon.MaterialProperties.DiffusionProfileID, m_Importer.m_MaterialSettings.diffusionProfileID);
                SetIfPresent(material, SpeedTreeImporterCommon.MaterialProperties.BackfaceNormalModeID, normalMode);
                SetIfPresent(material, SpeedTreeImporterCommon.MaterialProperties.TwoSidedID, source.TwoSided ? 0f : 2f);
                material.enableInstancing = true;
                material.doubleSidedGI = source.TwoSided;
            }

            static void SetIfPresent(Material material, int property, float value)
            {
                if (material.HasFloat(property)) material.SetFloat(property, value);
            }

            /// <summary>The wind toggles of the enabled wind (a billboard takes only the shared one;
            /// shimmer goes with ripple).</summary>
            void SetWindToggles(Material material, bool billboard)
            {
                var wind = m_Output.m_WindConfig;
                if (wind.doShared != 0) material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.WindSharedKwToggle, 1f);
                if (billboard) return;
                if (wind.doBranch2 != 0) material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.WindBranch2KwToggle, 1f);
                if (wind.doBranch1 != 0) material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.WindBranch1KwToggle, 1f);
                if (wind.doRipple != 0)
                {
                    material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.WindRippleKwToggle, 1f);
                    if (wind.doShimmer != 0) material.SetFloat(SpeedTreeImporterCommon.MaterialProperties.WindShimmerKwToggle, 1f);
                }
            }

            void CreateColliders(GameObject main)
            {
                for (int i = 0; i < m_Tree.Collisions.Length; i++)
                {
                    var collision = m_Tree.Collisions[i];
                    var go = new GameObject("Collider" + (i + 1));
                    go.transform.parent = main.transform;
                    var a = new Vector3(collision.Position.X, collision.Position.Y, collision.Position.Z) * m_Scale;
                    var b = new Vector3(collision.Position2.X, collision.Position2.Y, collision.Position2.Z) * m_Scale;
                    go.transform.position = (a + b) * 0.5f;
                    if ((a - b).sqrMagnitude < SphereSqrLength)
                    {
                        go.AddComponent<SphereCollider>().radius = collision.Radius * m_Scale;
                    }
                    else
                    {
                        var capsule = go.AddComponent<CapsuleCollider>();
                        capsule.direction = 2;
                        capsule.radius = collision.Radius * m_Scale;
                        capsule.height = (a - b).magnitude;
                        go.transform.LookAt(b);
                    }
                    m_Context.AddObjectToAsset(go.name, go);
                }
            }

            /// <summary>The wind configuration: the common gust values, the stretch limits and extents
            /// scaled, each enabled branch's and the ripple's curves (bend, oscillation, planar and
            /// directional scaled), enabled when no curve has a NaN sample and the shaping curves are
            /// not all zero.</summary>
            unsafe SpeedTreeWindConfig9 WindConfig(SpeedTree9File.Wind wind)
            {
                float scale = m_Scale;
                var config = new SpeedTreeWindConfig9
                {
                    strengthResponse = wind.StrengthResponse,
                    directionResponse = wind.DirectionResponse,
                    gustFrequency = wind.GustFrequency,
                    gustStrengthMin = wind.GustStrengthMin,
                    gustStrengthMax = wind.GustStrengthMax,
                    gustDurationMin = wind.GustDurationMin,
                    gustDurationMax = wind.GustDurationMax,
                    gustRiseScalar = wind.GustRiseScalar,
                    gustFallScalar = wind.GustFallScalar,
                    branch1StretchLimit = wind.Branch1StretchLimit * scale,
                    branch2StretchLimit = wind.Branch2StretchLimit * scale,
                    importScale = scale,
                    treeExtentX = (m_Tree.BoundsMax.X - m_Tree.BoundsMin.X) * scale,
                    treeExtentY = (m_Tree.BoundsMax.Y - m_Tree.BoundsMin.Y) * scale,
                    treeExtentZ = (m_Tree.BoundsMax.Z - m_Tree.BoundsMin.Z) * scale,
                };
                if (wind.DoShared)
                {
                    var b = wind.Shared;
                    Scaled(b.Bend, config.bendShared, scale);
                    Scaled(b.Oscillation, config.oscillationShared, scale);
                    Copied(b.Speed, config.speedShared);
                    Copied(b.Turbulence, config.turbulenceShared);
                    Copied(b.Flexibility, config.flexibilityShared);
                    config.independenceShared = b.Independence;
                    config.sharedHeightStart = wind.SharedStartHeight;
                    if (ValidBranch(b)) config.doShared = 1;
                }
                if (wind.DoBranch1)
                {
                    var b = wind.Branch1;
                    Scaled(b.Bend, config.bendBranch1, scale);
                    Scaled(b.Oscillation, config.oscillationBranch1, scale);
                    Copied(b.Speed, config.speedBranch1);
                    Copied(b.Turbulence, config.turbulenceBranch1);
                    Copied(b.Flexibility, config.flexibilityBranch1);
                    config.independenceBranch1 = b.Independence;
                    if (ValidBranch(b)) config.doBranch1 = 1;
                }
                if (wind.DoBranch2)
                {
                    var b = wind.Branch2;
                    Scaled(b.Bend, config.bendBranch2, scale);
                    Scaled(b.Oscillation, config.oscillationBranch2, scale);
                    Copied(b.Speed, config.speedBranch2);
                    Copied(b.Turbulence, config.turbulenceBranch2);
                    Copied(b.Flexibility, config.flexibilityBranch2);
                    config.independenceBranch2 = b.Independence;
                    if (ValidBranch(b)) config.doBranch2 = 1;
                }
                if (wind.DoRipple)
                {
                    var r = wind.Ripple;
                    Scaled(r.Planar, config.planarRipple, scale);
                    Scaled(r.Directional, config.directionalRipple, scale);
                    Copied(r.Speed, config.speedRipple);
                    Copied(r.Flexibility, config.flexibilityRipple);
                    config.independenceRipple = r.Independence;
                    if (ValidCurve(r.Planar, true) && ValidCurve(r.Directional, true) && ValidCurve(r.Speed, true) && ValidCurve(r.Flexibility, false))
                    {
                        config.doRipple = 1;
                        if (wind.DoShimmer)
                        {
                            config.doShimmer = 1;
                            config.shimmerRipple = r.Shimmer;
                        }
                    }
                }
                return config;
            }

            static unsafe void Copied(float[] source, float* target) =>
                SpeedTreeWindCurves.Copy(source, new Span<float>(target, SpeedTreeWindCurves.Samples));

            static unsafe void Scaled(float[] source, float* target, float scale) =>
                SpeedTreeWindCurves.Scale(source, new Span<float>(target, SpeedTreeWindCurves.Samples), scale);

            static bool ValidBranch(SpeedTree9File.Branch b) =>
                ValidCurve(b.Bend, true) && ValidCurve(b.Oscillation, true) && ValidCurve(b.Speed, true)
                && ValidCurve(b.Turbulence, true) && ValidCurve(b.Flexibility, false);

            /// <summary>No sample is NaN and, when <paramref name="nonZero"/>, one is not zero.</summary>
            static bool ValidCurve(float[] curve, bool nonZero)
            {
                bool any = false;
                foreach (var v in curve)
                {
                    if (float.IsNaN(v)) return false;
                    any |= v != 0f;
                }
                return !nonZero || any;
            }

            /// <summary>Calls the project's [MaterialSettingsCallback] methods (render pipelines
            /// post-process the materials) with the main object.</summary>
            static void RunMaterialCallbacks(GameObject main)
            {
                foreach (var method in TypeCache.GetMethodsWithAttribute(typeof(MaterialSettingsCallbackAttribute)))
                    if (Delegate.CreateDelegate(typeof(OnAssetPostProcess), method, throwOnBindFailure: false) is OnAssetPostProcess callback)
                        callback(main);
            }
        }
    }

    [Serializable]
    internal partial class MeshSettings
    {
        public SpeedTreeImporterCommon.STUnitConversion unitConversion = SpeedTreeImporterCommon.STUnitConversion.kFeetToMeters;
        public float scaleFactor = SpeedTreeConstants.kFeetToMetersRatio;
    }

    [Serializable]
    internal partial class MaterialSettings
    {
        public Color mainColor = Color.white;
        public Color hueVariation = new Color(1f, 0.5f, 0f, 0.1f);
        public float alphaClipThreshold = 0.1f;
        public float transmissionScale = 0f;
        public bool enableHueVariation = false;
        public bool enableBumpMapping = true;
        public bool enableSubsurfaceScattering = true;
        public Vector4 diffusionProfileAssetID = Vector4.zero;
        public uint diffusionProfileID = 0u;
    }

    [Serializable]
    internal partial class LightingSettings
    {
        public bool enableShadowCasting = true;
        public bool enableShadowReceiving = true;
        public bool enableLightProbes = true;
        public ReflectionProbeUsage reflectionProbeEnumValue = ReflectionProbeUsage.BlendProbes;
    }

    [Serializable]
    internal partial class AdditionalSettings
    {
        public MotionVectorGenerationMode motionVectorModeEnumValue = MotionVectorGenerationMode.Object;
        public bool generateColliders = true;
        public bool generateRigidbody = true;
    }

    [Serializable]
    internal partial class LODSettings
    {
        public bool enableSmoothLODTransition = true;
        public bool animateCrossFading = true;
        public float billboardTransitionCrossFadeWidth = 0.25f;
        public float fadeOutWidth = 0.25f;
        public bool hasBillboard = false;
    }

    [Serializable]
    internal partial class PerLODSettings
    {
        public bool enableSettingOverride = false;
        public float height = 0.5f;
        public bool castShadows = false;
        public bool receiveShadows = false;
        public bool useLightProbes = false;
        public ReflectionProbeUsage reflectionProbeUsage = ReflectionProbeUsage.Off;
        public bool enableBump = false;
        public bool enableHue = false;
        public bool enableSubsurface = false;
    }

    [Serializable]
    internal partial class WindSettings
    {
        public bool enableWind = true;
        [Range(1f, 20f)] public float strenghResponse = 5f;
        [Range(1f, 20f)] public float directionResponse = 2.5f;
        [Range(0f, 1f)] public float randomness = 0.5f;
    }

    [Serializable]
    internal partial class MaterialInfo
    {
        public Material material = null;
        public string defaultName = null;
        public bool exported = false;
    }

    [Serializable]
    internal partial class LODMaterials
    {
        public List<MaterialInfo> materials = new List<MaterialInfo>();
        public Dictionary<int, List<int>> lodToMaterials = new Dictionary<int, List<int>>();
        public Dictionary<string, int> matNameToIndex = new Dictionary<string, int>();

        public void AddLodMaterialIndex(int lod, int matIndex)
        {
            if (lodToMaterials.TryGetValue(lod, out var list)) list.Add(matIndex);
            else lodToMaterials.Add(lod, new List<int> { matIndex });
        }
    }

    [Serializable]
    internal partial class AssetIdentifier
    {
        public string type;
        public string name;

        public AssetIdentifier(Object asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            type = asset.GetType().ToString();
            name = asset.name;
        }

        public AssetIdentifier(Type type, string name)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("The name is empty", nameof(name));
            this.type = type.ToString();
            this.name = name;
        }
    }

    internal partial class SpeedTreeImporterOutputData : ScriptableObject
    {
        public GameObject mainObject;
        public LODMaterials lodMaterials = new LODMaterials();
        public List<AssetIdentifier> materialsIdentifiers = new List<AssetIdentifier>();
        public SpeedTreeWindConfig9 m_WindConfig = new SpeedTreeWindConfig9();
        public bool hasEmbeddedMaterials = false;
        public bool hasAlphaClipThreshold = false;
        public bool hasTransmissionScale = false;
        public bool hasBillboard = false;

        public SpeedTreeImporterOutputData() { }

        public static SpeedTreeImporterOutputData Create()
        {
            var data = CreateInstance<SpeedTreeImporterOutputData>();
            data.name = "ImporterOutputData";
            return data;
        }
    }
}

namespace Unbound.SpeedTree
{
    /// <summary>A SpeedTree 9 wind curve as the wind asset stores it: its first
    /// <see cref="Samples"/> samples. A file's curve with fewer samples is refused; one with more is
    /// cut to its first samples (unmeasured: the probe's trees have curves of exactly 20).</summary>
    internal static class SpeedTreeWindCurves
    {
        /// <summary>The samples per curve (<c>SpeedTreeWindConfig9</c>'s fixed arrays).</summary>
        internal const int Samples = 20;

        static void Check(float[] source)
        {
            if (source.Length < Samples)
                throw new FormatException($"a SpeedTree 9 wind curve has {source.Length} samples, fewer than {Samples}");
        }

        internal static void Copy(float[] source, Span<float> target)
        {
            Check(source);
            source.AsSpan(0, Samples).CopyTo(target);
        }

        internal static void Scale(float[] source, Span<float> target, float scale)
        {
            Check(source);
            for (int i = 0; i < Samples; i++) target[i] = source[i] * scale;
        }
    }
}
