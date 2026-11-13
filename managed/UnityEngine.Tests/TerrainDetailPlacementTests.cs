using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Where terrain details go and which prototypes get any (tests/projects/grass):
    /// prototype validation, the footprint behind coverage, coverage candidates and the legacy
    /// distribution, on prototypes other than the probe's.</summary>
    public class TerrainDetailPlacementTests : IDisposable
    {
        readonly List<Object> m_Objects = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            QualitySettings.useLegacyDetailDistribution = false;
        }

        T Own<T>(T o) where T : Object { m_Objects.Add(o); return o; }

        /// <summary>A prefab of a box (center, half size) with uvs or not, and its renderer's
        /// materials.</summary>
        GameObject Box(Vector3 center, Vector3 half, bool uvs = true, int materials = 1)
        {
            var mesh = Own(new Mesh());
            var vertices = new Vector3[8];
            var uv = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                vertices[i] = center + new Vector3((i & 1) != 0 ? half.x : -half.x, (i & 2) != 0 ? half.y : -half.y, (i & 4) != 0 ? half.z : -half.z);
                uv[i] = new Vector2(i & 1, (i >> 1) & 1);
            }
            mesh.vertices = vertices;
            if (uvs) mesh.uv = uv;
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            mesh.RecalculateBounds();
            var go = Own(new GameObject("box"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = Own(new Material(Shader.Create("Standard", 2000)));
            var list = new Material[materials];
            for (int i = 0; i < materials; i++) list[i] = material;
            go.AddComponent<MeshRenderer>().sharedMaterials = list;
            return go;
        }

        TerrainData Data(DetailScatterMode mode, params DetailPrototype[] prototypes)
        {
            var data = Own(new TerrainData { heightmapResolution = 33 });
            data.size = new Vector3(16f, 2f, 16f);
            data.SetDetailScatterMode(mode);
            data.SetDetailResolution(32, 8);
            data.detailPrototypes = prototypes;
            return data;
        }

        static void Fill(TerrainData data, int layer, int value)
        {
            var values = new int[32, 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    values[y, x] = value;
            data.SetDetailLayer(0, 0, layer, values);
        }

        static string Error(DetailPrototype prototype)
        {
            Assert.Equal(prototype.Validate(out var error), error.Length == 0);
            return error;
        }

        /// <summary>A patch keeps its layers in the order they got values there; a layer whose values
        /// there all return to zero leaves, and comes back at the end. Writes clamp counts to 16.</summary>
        [Fact]
        public void PatchesKeepTheirLayersInTheOrderTheyGotValues()
        {
            var texture = Own(new Texture2D(4, 4));
            var data = Data(DetailScatterMode.InstanceCountMode,
                new DetailPrototype { prototypeTexture = texture }, new DetailPrototype { prototypeTexture = texture }, new DetailPrototype { prototypeTexture = texture });
            data.SetDetailLayer(2, 2, 2, new int[,] { { 3 } });
            data.SetDetailLayer(3, 1, 0, new int[,] { { 40 } });
            Assert.Equal(new byte[] { 2, 0 }, data.PatchLayers(0, 0));
            Assert.Null(data.PatchLayers(1, 0));
            Assert.Equal(16, data.GetDetailLayer(3, 1, 1, 1, 0)[0, 0]);
            data.SetDetailLayer(2, 2, 2, new int[,] { { 0 } });
            Assert.Equal(new byte[] { 0 }, data.PatchLayers(0, 0));
            data.SetDetailLayer(7, 7, 2, new int[,] { { 1 } });
            Assert.Equal(new byte[] { 0, 2 }, data.PatchLayers(0, 0));
            // An area over two patches adds the layer to both.
            data.SetDetailLayer(7, 0, 1, new int[,] { { 1, 1 } });
            Assert.Equal(new byte[] { 0, 2, 1 }, data.PatchLayers(0, 0));
            Assert.Equal(new byte[] { 1 }, data.PatchLayers(1, 0));
            data.RemoveDetailPrototype(0);
            Assert.Equal(new byte[] { 1, 0 }, data.PatchLayers(0, 0));
        }

        /// <summary>Setting the prototypes again keeps the patches' layers (fewer prototypes drop the
        /// rest), and switching the scatter mode empties every patch.</summary>
        [Fact]
        public void PrototypesAndModeChangesKeepOrEmptyThePatches()
        {
            var texture = Own(new Texture2D(4, 4));
            var prototypes = new[] { new DetailPrototype { prototypeTexture = texture, noiseSeed = 1 }, new DetailPrototype { prototypeTexture = texture, noiseSeed = 2 } };
            var data = Data(DetailScatterMode.InstanceCountMode, prototypes);
            data.SetDetailLayer(1, 1, 1, new int[,] { { 2 } });
            data.SetDetailLayer(2, 2, 0, new int[,] { { 3 } });
            int before = data.ComputeDetailInstanceTransforms(0, 0, 0, 1f, out _).Length;
            data.detailPrototypes = prototypes;
            Assert.Equal(new byte[] { 1, 0 }, data.PatchLayers(0, 0));
            Assert.Equal(before, data.ComputeDetailInstanceTransforms(0, 0, 0, 1f, out _).Length);
            Assert.Equal(3, before);
            data.detailPrototypes = new[] { prototypes[0] };
            Assert.Equal(new byte[] { 0 }, data.PatchLayers(0, 0));
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            Assert.Equal(new byte[] { 0 }, data.PatchLayers(0, 0));
            data.SetDetailScatterMode(DetailScatterMode.CoverageMode);
            Assert.Null(data.PatchLayers(0, 0));
            Assert.Equal(0, data.GetDetailLayer(2, 2, 1, 1, 0)[0, 0]);
        }

        /// <summary>A zero noiseSeed seeds a layer by its place in the patch: the second layer of a
        /// patch of 8 × 8 samples gets the seed 64.</summary>
        [Fact]
        public void AZeroNoiseSeedSeedsByTheLayersPlaceInThePatch()
        {
            var texture = Own(new Texture2D(4, 4));
            var data = Data(DetailScatterMode.InstanceCountMode,
                new DetailPrototype { prototypeTexture = texture, noiseSeed = 5 }, new DetailPrototype { prototypeTexture = texture },
                new DetailPrototype { prototypeTexture = texture, noiseSeed = 64 });
            Fill(data, 0, 2);
            Fill(data, 1, 2);
            Fill(data, 2, 2);
            var second = data.ComputeDetailInstanceTransforms(1, 1, 1, 1f, out _);
            var seeded = data.ComputeDetailInstanceTransforms(1, 1, 2, 1f, out _);
            Assert.Equal(seeded.Length, second.Length);
            for (int i = 0; i < second.Length; i++)
            {
                Assert.Equal(seeded[i].posX, second[i].posX);
                Assert.Equal(seeded[i].rotationY, second[i].rotationY);
            }
        }

        /// <summary>Patch meshes place their instances by their own numbering, within their
        /// sample; billboards draw no turn; the mesh holds at most 65536 vertices.</summary>
        [Fact]
        public void PatchMeshesPlaceByTheirOwnRules()
        {
            var texture = Own(new Texture2D(4, 4));
            var grass = new DetailPrototype { prototypeTexture = texture, noiseSeed = 9, positionJitter = 0.7f };
            var billboard = new DetailPrototype { prototypeTexture = texture, noiseSeed = 9, positionJitter = 0.7f, renderMode = DetailRenderMode.GrassBillboard };
            var data = Data(DetailScatterMode.InstanceCountMode, grass, billboard);
            Fill(data, 0, 3);
            Fill(data, 1, 3);
            var reported = new List<Matrix4x4>();
            var drawn = new List<Matrix4x4>();
            var billboards = new List<Matrix4x4>();
            data.PlaceDetails(data.PrepareDetailLayer(0), 1, 2, 1f, reported, out _);
            int used = 0;
            Assert.True(data.PlacePatchMeshDetails(data.PrepareDetailLayer(0), 1, 2, 1f, 0, 4, ref used, drawn));
            Assert.Equal(8 * 8 * 3 * 4, used);
            used = 0;
            Assert.True(data.PlacePatchMeshDetails(data.PrepareDetailLayer(1), 1, 2, 1f, 0, 4, ref used, billboards));
            Assert.Equal(reported.Count, drawn.Count);
            Assert.NotEqual(reported[0].GetColumn(3), drawn[0].GetColumn(3));
            for (int i = 0; i < drawn.Count; i++)
            {
                // Sample i / 3 of the patch (8 × 8 samples of 0.5 units, the patch at (4, 8)).
                int sample = i / 3, lx = sample % 8, ly = sample / 8;
                Vector3 p = drawn[i].GetColumn(3);
                Assert.InRange(p.x, 4f + lx * 0.5f, 4f + (lx + 1) * 0.5f);
                Assert.InRange(p.z, 8f + ly * 0.5f, 8f + (ly + 1) * 0.5f);
                // Billboards are not turned.
                Assert.Equal(0f, billboards[i].m02, 6);
            }
            // The first billboard's draws match the grass's (no turn drawn yet); later ones drift.
            Assert.Equal(drawn[0].GetColumn(3), billboards[0].GetColumn(3));
            Assert.NotEqual(drawn[1].GetColumn(3), billboards[1].GetColumn(3));
            // A budget for two instances: two, then the layer stops.
            used = 0x10000 - 2 * 20000;
            Assert.False(data.PlacePatchMeshDetails(data.PrepareDetailLayer(0), 1, 2, 1f, 0, 20000, ref used, drawn));
            Assert.Equal(2, drawn.Count);
            Assert.Equal(0x10000, used);
        }

        [Fact]
        public void PrototypesAreCheckedInTheEditorsOrder()
        {
            var texture = Own(new Texture2D(4, 4));
            var box = Box(Vector3.zero, Vector3.one * 0.5f);
            Assert.Equal("Prototype is missing texture.", Error(new DetailPrototype { usePrototypeMesh = false }));
            Assert.Equal("Render mode cannot be VertexLit.", Error(new DetailPrototype { prototypeTexture = texture, renderMode = DetailRenderMode.VertexLit }));
            Assert.Equal("Detail instancing is only supported on mesh details.", Error(new DetailPrototype { prototypeTexture = texture, useInstancing = true }));
            Assert.Equal("", Error(new DetailPrototype { prototypeTexture = texture, renderMode = DetailRenderMode.GrassBillboard }));
            Assert.Equal("Prototype is missing Game Object.", Error(new DetailPrototype { usePrototypeMesh = true }));
            Assert.Equal("Render mode cannot be GrassBillboard.", Error(new DetailPrototype { prototype = box, usePrototypeMesh = true, renderMode = DetailRenderMode.GrassBillboard }));
            Assert.Equal("Render mode must be VertexLit.", Error(new DetailPrototype { prototype = box, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.Grass }));
            // A mesh drawn without instancing may use the grass shader.
            Assert.Equal("", Error(new DetailPrototype { prototype = box, usePrototypeMesh = true, renderMode = DetailRenderMode.Grass }));
            Assert.Equal("A detail prototype must have exactly one material.", Error(new DetailPrototype { prototype = Box(Vector3.zero, Vector3.one, materials: 2), usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit }));
            var bare = Box(Vector3.zero, Vector3.one, uvs: false);
            Assert.Equal("No texcoord is available.", Error(new DetailPrototype { prototype = bare, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit }));
            // Instanced meshes need no uvs.
            Assert.Equal("", Error(new DetailPrototype { prototype = bare, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit }));
            var empty = Own(new GameObject("no mesh"));
            empty.AddComponent<MeshRenderer>().sharedMaterial = Own(new Material(Shader.Create("Standard", 2000)));
            Assert.Equal("Mesh Filter component is missing.", Error(new DetailPrototype { prototype = empty, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit }));
            empty.AddComponent<MeshFilter>();
            Assert.Equal("Mesh is missing.", Error(new DetailPrototype { prototype = empty, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit }));
            var lod = Box(Vector3.zero, Vector3.one);
            lod.AddComponent<LODGroup>();
            Assert.Equal("LOD Group component is not supported.", Error(new DetailPrototype { prototype = lod, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit }));
        }

        [Fact]
        public void InvalidPrototypesGetNoInstancesOrCoverage()
        {
            var bare = Box(Vector3.zero, Vector3.one * 0.5f, uvs: false);
            var data = Data(DetailScatterMode.CoverageMode, new DetailPrototype { prototype = bare, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit });
            Fill(data, 0, 255);
            Assert.Equal(0f, data.ComputeDetailCoverage(0));
            Assert.Empty(data.ComputeDetailInstanceTransforms(1, 1, 0, 1f, out _));
        }

        /// <summary>The footprint behind coverage: the average width times twice the vertices'
        /// largest distance from the y axis without instancing, the bounds' width with it.</summary>
        [Fact]
        public void CoverageFollowsThePrototypesWidthAcrossX()
        {
            var box = Box(new Vector3(0.5f, 0f, 0.2f), new Vector3(1f, 0.3f, 0.4f));
            var plain = new DetailPrototype { prototype = box, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, minWidth = 1f, maxWidth = 3f, density = 2f };
            var instanced = new DetailPrototype { prototype = box, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit, minWidth = 1f, maxWidth = 3f, density = 2f };
            var grass = new DetailPrototype { prototypeTexture = Own(new Texture2D(4, 4)), minWidth = 1f, maxWidth = 3f, density = 2f };
            var data = Data(DetailScatterMode.CoverageMode, plain, instanced, grass);
            float radius = Mathf.Sqrt(1.5f * 1.5f + 0.6f * 0.6f);
            Assert.Equal(2f / (2f * 2f * radius), data.ComputeDetailCoverage(0), 5);
            Assert.Equal(2f / (2f * 2f), data.ComputeDetailCoverage(1), 5);
            Assert.Equal(2f / 2f, data.ComputeDetailCoverage(2), 5);
        }

        [Fact]
        public void CoverageOfABadIndexOrInInstanceCountModeIsAnError()
        {
            var errors = new Errors();
            var previous = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = errors;
                var counts = Data(DetailScatterMode.InstanceCountMode, new DetailPrototype { prototypeTexture = Own(new Texture2D(4, 4)) });
                Assert.Equal(0f, counts.ComputeDetailCoverage(-1));
                Assert.Equal(0f, counts.ComputeDetailCoverage(0));
                Assert.Equal(new[] { "Invalid detail prototype index", "Detail coverage unsupported when terrain is set to InstanceCount scattering mode." }, errors.Messages);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
        }

        /// <summary>Coverage candidates: the steps of the spacing that start within a sample,
        /// so a prototype wider than a sample still gets instances on some samples, and full
        /// coverage keeps every candidate.</summary>
        [Fact]
        public void WidePrototypesStillGetSomeInstances()
        {
            var texture = Own(new Texture2D(4, 4));
            // Samples 0.5 units across; footprint 2 units: a quarter of an instance per sample side.
            var data = Data(DetailScatterMode.CoverageMode, new DetailPrototype { prototypeTexture = texture, minWidth = 2f, maxWidth = 2f, noiseSeed = 11 });
            Fill(data, 0, 255);
            int total = 0;
            for (int py = 0; py < 4; py++)
                for (int px = 0; px < 4; px++)
                    total += data.ComputeDetailInstanceTransforms(px, py, 0, 1f, out _).Length;
            // About one instance per 2 × 2 units of the 16 × 16 terrain.
            Assert.InRange(total, 32, 96);
        }

        /// <summary>SetDetailResolution keeps whole patches, and instances still span the terrain.</summary>
        [Fact]
        public void DetailResolutionKeepsWholePatches()
        {
            var warnings = new Errors(LogType.Warning);
            var previous = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = warnings;
                var texture = Own(new Texture2D(4, 4));
                var data = Data(DetailScatterMode.InstanceCountMode, new DetailPrototype { prototypeTexture = texture, noiseSeed = 3 });
                data.SetDetailResolution(100, 32);
                Assert.Equal(96, data.detailResolution);
                Assert.Equal(3, data.detailPatchCount);
                Assert.Empty(warnings.Messages);
                var values = new int[96, 96];
                values[95, 95] = 1;
                data.SetDetailLayer(0, 0, 0, values);
                var last = Assert.Single(data.ComputeDetailInstanceTransforms(2, 2, 0, 1f, out _));
                Assert.InRange(last.posX, 16f * 95 / 96, 16f);
                Assert.InRange(last.posZ, 16f * 95 / 96, 16f);
                data.SetDetailResolution(-5, 4);
                Assert.Equal(0, data.detailResolution);
                Assert.Equal(8, data.detailResolutionPerPatch);
                Assert.Equal(new[] { "detailResolution must not be negative.", "resolutionPerPatch is clamped to the range of [8, 1000]." }, warnings.Messages);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
        }

        [Fact]
        public void LegacyDistributionMovesInstancesButKeepsTheirCount()
        {
            var texture = Own(new Texture2D(4, 4));
            var data = Data(DetailScatterMode.InstanceCountMode, new DetailPrototype { prototypeTexture = texture, noiseSeed = 5, positionJitter = 0.5f });
            Fill(data, 0, 3);
            var r2 = data.ComputeDetailInstanceTransforms(2, 1, 0, 1f, out _);
            QualitySettings.useLegacyDetailDistribution = true;
            var legacy = data.ComputeDetailInstanceTransforms(2, 1, 0, 1f, out _);
            Assert.Equal(8 * 8 * 3, r2.Length);
            Assert.Equal(r2.Length, legacy.Length);
            Assert.NotEqual(r2[0].posX, legacy[0].posX);
            // The size and turn draws are the same: only the place within the sample differs.
            Assert.Equal(r2[0].rotationY, legacy[0].rotationY);
            for (int i = 0; i < legacy.Length; i++)
            {
                Assert.InRange(legacy[i].posX, 8f, 12f);
                Assert.InRange(legacy[i].posZ, 4f, 8f);
            }
        }

        /// <summary>ComputeDetailInstanceTransforms reads an untilted turn back from the transform,
        /// on both halves of the turn and at its ends.</summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(3.1f)]
        [InlineData(3.4f)]
        [InlineData(6.1f)]
        public void TheTurnReadsBackFromAnUntiltedTransform(float turn)
        {
            var model = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.AngleAxis(turn * Mathf.Rad2Deg, Vector3.up), new Vector3(2f, 3f, 2f));
            var i = TerrainData.InstanceTransform(model);
            Assert.Equal(turn, i.rotationY, 3);
            Assert.Equal(2f, i.scaleXZ, 4);
            Assert.Equal(3f, i.scaleY, 4);
            Assert.Equal(new Vector3(1f, 2f, 3f), new Vector3(i.posX, i.posY, i.posZ));
        }

        /// <summary>A zero-width instance reports a zero width and a NaN turn (as the Editor does),
        /// and its transform stays finite for drawing.</summary>
        [Fact]
        public void ZeroWidthInstancesReportANaNTurn()
        {
            var texture = Own(new Texture2D(4, 4));
            var data = Data(DetailScatterMode.InstanceCountMode, new DetailPrototype { prototypeTexture = texture, minWidth = 0f, maxWidth = 0f, alignToGround = 1f, noiseSeed = 2 });
            Fill(data, 0, 2);
            var placements = new List<Matrix4x4>();
            data.PlaceDetails(data.PrepareDetailLayer(0), 0, 0, 1f, placements, out var bounds);
            Assert.Equal(128, placements.Count);
            foreach (var model in placements)
                for (int k = 0; k < 16; k++) Assert.False(float.IsNaN(model[k]));
            Assert.False(float.IsNaN(bounds.extents.x));
            var reported = data.ComputeDetailInstanceTransforms(0, 0, 0, 1f, out _);
            Assert.All(reported, i => { Assert.Equal(0f, i.scaleXZ); Assert.True(float.IsNaN(i.rotationY)); });
        }

        /// <summary>alignToGround tilts an instance's up axis from up to the terrain's normal; the
        /// bounds hold every instance's transformed mesh bounds.</summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(0.5f)]
        [InlineData(1f)]
        public void AlignToGroundTiltsInstancesTowardsTheNormal(float align)
        {
            var box = Box(new Vector3(0.2f, 0.5f, 0f), new Vector3(0.6f, 0.5f, 0.3f));
            var data = Data(DetailScatterMode.InstanceCountMode,
                new DetailPrototype { prototype = box, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit, alignToGround = align, noiseSeed = 8 });
            var heights = new float[33, 33];
            for (int y = 0; y < 33; y++)
                for (int x = 0; x < 33; x++)
                    heights[y, x] = x / 32f;
            data.SetHeights(0, 0, heights);
            Fill(data, 0, 1);
            var placements = new List<Matrix4x4>();
            data.PlaceDetails(data.PrepareDetailLayer(0), 1, 2, 1f, placements, out var bounds);
            Assert.Equal(64, placements.Count);
            var mesh = box.GetComponent<MeshFilter>().sharedMesh;
            foreach (var model in placements)
            {
                Vector3 position = model.GetColumn(3);
                var normal = data.GetInterpolatedNormal(position.x / 16f, position.z / 16f);
                var expected = Vector3.Slerp(Vector3.up, normal, align).normalized;
                var up = ((Vector3)model.GetColumn(1)).normalized;
                Assert.True(Vector3.Angle(expected, up) < 0.05f, $"{expected} vs {up}");
                for (int c = 0; c < 8; c++)
                {
                    var e = mesh.bounds.extents;
                    var corner = mesh.bounds.center + new Vector3((c & 1) != 0 ? e.x : -e.x, (c & 2) != 0 ? e.y : -e.y, (c & 4) != 0 ? e.z : -e.z);
                    var p = model.MultiplyPoint3x4(corner);
                    Assert.True(bounds.Contains(p) || bounds.SqrDistance(p) < 1e-8f, $"{p} outside {bounds}");
                }
            }
            if (align != 0f)
            {
                // Tilted instances' reported turns include the tilt: they no longer equal the
                // untilted turn of the same draws.
                var tilted = data.ComputeDetailInstanceTransforms(1, 2, 0, 1f, out _);
                var flat = Data(DetailScatterMode.InstanceCountMode,
                    new DetailPrototype { prototype = box, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit, noiseSeed = 8 });
                flat.SetHeights(0, 0, heights);
                Fill(flat, 0, 1);
                var straight = flat.ComputeDetailInstanceTransforms(1, 2, 0, 1f, out _);
                Assert.Contains(tilted, t => Array.FindIndex(straight, s => Math.Abs(s.rotationY - t.rotationY) < 1e-3f) < 0);
            }
        }

        /// <summary>useDensityScaling: the density scales counts through an ordered dither (each
        /// 8 × 8 block keeps the density's share) and coverage's instances per unit; prototypes
        /// without it ignore the density.</summary>
        [Theory]
        [InlineData(0.25f)]
        [InlineData(0.6f)]
        public void TheDensityScalesOnlyPrototypesThatAskForIt(float density)
        {
            var texture = Own(new Texture2D(4, 4));
            var data = Data(DetailScatterMode.InstanceCountMode,
                new DetailPrototype { prototypeTexture = texture, noiseSeed = 3, useDensityScaling = true },
                new DetailPrototype { prototypeTexture = texture, noiseSeed = 4 });
            Fill(data, 0, 2);
            Fill(data, 1, 2);
            // An 8 × 8 patch of value 2: (2 × density + dither) rounded down over the 64 dither steps.
            int expected = 0;
            for (int step = 1; step <= 64; step++) expected += (int)((step - 0.5f) / 64f + 2f * density);
            Assert.Equal(expected, data.ComputeDetailInstanceTransforms(1, 1, 0, density, out _).Length);
            Assert.Equal(128, data.ComputeDetailInstanceTransforms(1, 1, 1, density, out _).Length);
            var coverage = Data(DetailScatterMode.CoverageMode, new DetailPrototype { prototypeTexture = texture, noiseSeed = 3, useDensityScaling = true, density = 3f });
            Fill(coverage, 0, 255);
            int full = coverage.ComputeDetailInstanceTransforms(1, 1, 0, 1f, out _).Length;
            int thinned = coverage.ComputeDetailInstanceTransforms(1, 1, 0, density, out _).Length;
            Assert.InRange(thinned, (int)(full * density * density * 0.5f), (int)(full * density * density * 1.6f) + 4);
        }

        /// <summary>A terrain's quality-dependent settings read the level's overrides unless the
        /// terrain ignores quality settings.</summary>
        [Fact]
        public void TerrainSettingsFollowTheQualityOverrides()
        {
            var go = Own(new GameObject("overrides"));
            var terrain = go.AddComponent<Terrain>();
            terrain.detailObjectDensity = 0.8f;
            terrain.heightmapPixelError = 9f;
            var previous = QualitySettings.terrainQualityOverrides;
            float previousScale = QualitySettings.terrainDetailDensityScale;
            try
            {
                QualitySettings.terrainQualityOverrides = TerrainQualityOverrides.DetailDensity;
                QualitySettings.terrainDetailDensityScale = 0.3f;
                Assert.Equal(0.3f, terrain.detailObjectDensity);
                Assert.Equal(9f, terrain.heightmapPixelError);
                terrain.ignoreQualitySettings = true;
                Assert.Equal(0.8f, terrain.detailObjectDensity);
            }
            finally
            {
                QualitySettings.terrainQualityOverrides = previous;
                QualitySettings.terrainDetailDensityScale = previousScale;
            }
        }

        /// <summary>Drawn details are rebuilt when the detail density changes, the terrain's or the
        /// quality level's.</summary>
        [Fact]
        public void DrawnDetailsFollowTheDensity()
        {
            var texture = Own(new Texture2D(4, 4));
            var data = Data(DetailScatterMode.InstanceCountMode, new DetailPrototype { prototypeTexture = texture, useDensityScaling = true });
            Fill(data, 0, 2);
            var go = Own(new GameObject("drawn density"));
            go.SetActive(false);
            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            go.SetActive(true);
            var renderer = terrain.Renderer;
            var origin = terrain.GetPosition();
            Assert.True(renderer.DetailsStale(terrain, data, origin));
            renderer.BuildDetailsForTest(terrain, data, origin);
            Assert.False(renderer.DetailsStale(terrain, data, origin));
            terrain.detailObjectDensity = 0.3f;
            Assert.True(renderer.DetailsStale(terrain, data, origin));
            renderer.BuildDetailsForTest(terrain, data, origin);
            var previous = QualitySettings.terrainQualityOverrides;
            float previousScale = QualitySettings.terrainDetailDensityScale;
            try
            {
                QualitySettings.terrainQualityOverrides = TerrainQualityOverrides.DetailDensity;
                QualitySettings.terrainDetailDensityScale = 0.7f;
                Assert.True(renderer.DetailsStale(terrain, data, origin));
            }
            finally
            {
                QualitySettings.terrainQualityOverrides = previous;
                QualitySettings.terrainDetailDensityScale = previousScale;
            }
        }

        sealed class Errors : ILogHandler
        {
            readonly LogType m_Type;
            internal readonly List<string> Messages = new List<string>();
            internal Errors(LogType type = LogType.Error) => m_Type = type;
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == m_Type) Messages.Add(string.Format(format, args));
            }
            public void LogException(Exception error, Object context) => throw error;
        }
    }
}
