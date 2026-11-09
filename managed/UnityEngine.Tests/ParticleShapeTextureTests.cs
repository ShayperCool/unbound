using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The shape module's texture (Unity 6000.0 ShapeModule.texture, textureClipChannel,
    /// textureClipThreshold, textureColorAffectsParticles, textureAlphaAffectsParticles,
    /// textureUVChannel), the box shell and edge shapes, line meshes and the obsolete shell types.
    /// Mapping and arithmetic follow tests/projects/particlemeshshape (6000.0.71f1).</summary>
    public class ParticleShapeTextureTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly string m_Version = ProjectInfo.UnityVersion;

        public ParticleShapeTextureTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
            ProjectInfo.UnityVersion = "6000.0.71f1";
        }

        public void Dispose()
        {
            m_Logger.logHandler = m_PreviousHandler;
            ProjectInfo.UnityVersion = m_Version;
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;

            internal CaptureHandler(ILogHandler inner, List<string> logs)
            {
                m_Inner = inner;
                m_Logs = logs;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                    m_Logs.Add($"{type}: " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        /// <summary>W×H texels, texel (x, y) = (x·step, y·step, 77, x·step) so a colour names its texel.</summary>
        static Texture2D Coords(int size)
        {
            int step = 256 / size;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color32((byte)(x * step), (byte)(y * step), 77, (byte)(x * step));
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        static ParticleSystem Make(ParticleSystemShapeType type, Texture2D texture, Mesh mesh = null)
        {
            var go = new GameObject("shape-texture");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 99;
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            main.maxParticles = 1000;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.texture = texture;
            if (mesh != null) shape.mesh = mesh;
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Particle[] Read(ParticleSystem ps)
        {
            var p = new ParticleSystem.Particle[ps.particleCount];
            Assert.Equal(p.Length, ps.GetParticles(p));
            return p;
        }

        static int Texel(float unit, int size) => Mathf.Clamp((int)Mathf.Floor(unit * size), 0, size - 1);

        [Fact]
        public void ARectangleReadsTheTexelUnderItsUnitPointBeforeTheShapeTransform()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, Coords(8));
            try
            {
                var shape = ps.shape;
                shape.position = new Vector3(3f, -1f, 0f);
                shape.scale = new Vector3(4f, 2f, 1f);
                ps.Emit(200);
                foreach (var p in Read(ps))
                {
                    float ux = (p.position.x - 3f) / 4f + 0.5f, uy = (p.position.y + 1f) / 2f + 0.5f;
                    Assert.Equal(Texel(ux, 8) * 32, p.startColor.r);
                    Assert.Equal(Texel(uy, 8) * 32, p.startColor.g);
                    Assert.Equal(77, p.startColor.b);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ASphereReadsItsAzimuthAndHeightNotItsRadius()
        {
            var ps = Make(ParticleSystemShapeType.Sphere, Coords(16));
            try
            {
                var shape = ps.shape; shape.radius = 3f; shape.radiusThickness = 0.5f;
                ps.Emit(200);
                foreach (var p in Read(ps))
                {
                    var d = p.position.normalized;
                    float azimuth = Mathf.Repeat(Mathf.Atan2(d.y, d.x), 2f * Mathf.PI) / (2f * Mathf.PI);
                    float height = (d.z + 1f) * 0.5f;
                    // Away from texel borders, where the reconstruction is exact enough.
                    if (Mathf.Abs(azimuth * 16f - Mathf.Round(azimuth * 16f)) < 1e-3f || Mathf.Abs(height * 16f - Mathf.Round(height * 16f)) < 1e-3f) continue;
                    Assert.Equal(Texel(azimuth, 16) * 16, p.startColor.r);
                    Assert.Equal(Texel(height, 16) * 16, p.startColor.g);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void TheClipChannelDiscardsParticlesBelowTheThresholdAndKeepsTheOthersInOrder()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, Coords(4));
            try
            {
                var shape = ps.shape;
                shape.textureClipThreshold = 0.5f;
                ps.Emit(64);
                var kept = Read(ps);
                Assert.InRange(kept.Length, 1, 63);
                foreach (var p in kept) Assert.True(p.startColor.a >= 128, "a discarded particle stayed");
                // Threshold 0 keeps every particle, also those on a zero-alpha texel.
                ps.Clear();
                shape.textureClipThreshold = 0f;
                ps.Emit(64);
                Assert.Equal(64, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ColourAndAlphaSwitchesMultiplyTheStartColourBytesWithRounding()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, Coords(4));
            try
            {
                // The start colour as bytes rounds half up: (128, 64, 255, 153).
                var main = ps.main;
                main.startColor = new Color(0.5f, 0.25f, 1f, 0.6f);
                var shape = ps.shape;
                shape.textureAlphaAffectsParticles = false;
                ps.Emit(32);
                foreach (var p in Read(ps))
                {
                    int tx = Texel((p.position.x + 0.5f), 4) * 64, ty = Texel((p.position.y + 0.5f), 4) * 64;
                    Assert.Equal((128 * tx + 127) / 255, p.startColor.r);
                    Assert.Equal((64 * ty + 127) / 255, p.startColor.g);
                    Assert.Equal(77, p.startColor.b);
                    Assert.Equal(153, p.startColor.a);
                }
                ps.Clear();
                shape.textureAlphaAffectsParticles = true;
                shape.textureColorAffectsParticles = false;
                ps.Emit(32);
                foreach (var p in Read(ps))
                {
                    int tx = Texel((p.position.x + 0.5f), 4) * 64;
                    Assert.Equal(new Color32(128, 64, 255, (byte)((153 * tx + 127) / 255)), p.startColor);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        static Mesh Quad(bool uv2)
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            if (uv2) m.uv2 = new[] { new Vector2(0.9f, 0.9f), new Vector2(0.9f, 0.9f), new Vector2(0.9f, 0.9f), new Vector2(0.9f, 0.9f) };
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            return m;
        }

        [Fact]
        public void AMeshReadsItsUvChannelAndDiscardsEveryParticleWithoutIt()
        {
            var ps = Make(ParticleSystemShapeType.Mesh, Coords(4), Quad(true));
            try
            {
                var shape = ps.shape;
                shape.textureUVChannel = 1;
                ps.Emit(20);
                foreach (var p in Read(ps)) Assert.Equal(new Color32(192, 192, 77, 192), p.startColor);
                ps.Clear();
                shape.mesh = Quad(false);
                ps.Emit(20);
                Assert.Equal(0, ps.particleCount);
                // Channel 0 reads the vertex coordinates (times 0.9999): the texel under the point.
                shape.textureUVChannel = 0;
                ps.Emit(20);
                foreach (var p in Read(ps)) Assert.Equal(Texel(p.position.x * 0.9999f, 4) * 64, p.startColor.r);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ATextureMadeNonReadableStillTintsAndAChangedTextureIsReadAgain()
        {
            var texture = Coords(4);
            var ps = Make(ParticleSystemShapeType.Rectangle, texture);
            try
            {
                ps.Emit(16);
                var before = Read(ps);
                var px = texture.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(9, 8, 7, 255);
                texture.SetPixels32(px);
                texture.Apply(false, true);
                Assert.False(texture.isReadable);
                ps.Clear();
                ps.Emit(16);
                foreach (var p in Read(ps)) Assert.Equal(new Color32(9, 8, 7, 255), p.startColor);
                Assert.Contains(before, p => p.startColor.r != 9);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void DiscardedNewbornsLeaveAlsoFromRingBufferSlots()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, null);
            try
            {
                var main = ps.main;
                main.maxParticles = 4;
                main.ringBufferMode = ParticleSystemRingBufferMode.LoopUntilReplaced;
                ps.Emit(4);
                var shape = ps.shape;
                shape.texture = Coords(4);
                shape.textureClipThreshold = 0.5f;
                for (int i = 0; i < 4; i++) ps.Emit(8);
                var particles = Read(ps);
                Assert.InRange(particles.Length, 1, 8);
                foreach (var p in particles) Assert.True(p.startColor.a >= 128, "a discarded newborn stayed in a reused slot");
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ClippingInARingBufferStartsABatchWithoutAWarning()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, Coords(4));
            try
            {
                var main = ps.main;
                main.maxParticles = 12;
                main.ringBufferMode = ParticleSystemRingBufferMode.LoopUntilReplaced;
                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 100f;
                var shape = ps.shape;
                shape.textureClipThreshold = 0.5f;
                ps.Simulate(0.2f, true, true, true);
                ps.Emit(7);
                Assert.Empty(m_Logs);
                Assert.InRange(ps.particleCount, 1, 24);
                foreach (var p in Read(ps)) Assert.True(p.startColor.a >= 128);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ARingBufferPastItsMaximumPausesAtItOrLoopsUpToTwice()
        {
            // PauseUntilReplaced keeps the maximum, the new particles taking the ring's next slots;
            // LoopUntilReplaced swaps them into those slots and keeps the old ones, up to twice the
            // maximum (6000.0.71f1, tests/projects/particlerandom ring/*).
            int Count(ParticleSystemRingBufferMode mode, out uint[] seeds)
            {
                var ps = Make(ParticleSystemShapeType.Rectangle, null);
                try
                {
                    var main = ps.main; main.maxParticles = 12; main.ringBufferMode = mode;
                    ps.Emit(10);
                    var first = Array.ConvertAll(Read(ps), p => p.randomSeed);
                    ps.Emit(10);
                    seeds = Array.ConvertAll(Read(ps), p => p.randomSeed);
                    Assert.Equal(first[8..10], mode == ParticleSystemRingBufferMode.PauseUntilReplaced ? seeds[8..10] : seeds[18..20]);
                    ps.Emit(10);
                    return ps.particleCount;
                }
                finally { Object.DestroyImmediate(ps.gameObject); }
            }
            Assert.Equal(12, Count(ParticleSystemRingBufferMode.PauseUntilReplaced, out var pause));
            Assert.Equal(12, pause.Length);
            Assert.Equal(24, Count(ParticleSystemRingBufferMode.LoopUntilReplaced, out var loop));
            Assert.Equal(20, loop.Length);
        }

        [Fact]
        public void ClippingInAnUnmeasuredReleaseWarns()
        {
            ProjectInfo.UnityVersion = "2022.3.0f1";
            var ps = Make(ParticleSystemShapeType.Rectangle, Coords(4));
            try
            {
                var shape = ps.shape;
                shape.textureClipThreshold = 0.5f;
                ps.Emit(40);
                Assert.Single(m_Logs, l => l.Contains("shape texture clipping outside a start batch"));
                foreach (var p in Read(ps)) Assert.True(p.startColor.a >= 128);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ClippedBatchLanesEachLowerTheCountStartedPaddingLanesToo()
        {
            // Half of the rectangle is clipped. Emit(1) starts four lanes (one used, three padding):
            // any discarded lane lowers the count, so a particle stays only when all four are kept,
            // about one call in sixteen; one Emit of the same total keeps about half.
            int Kept(int calls, int perCall)
            {
                var ps = Make(ParticleSystemShapeType.Rectangle, Coords(4));
                try
                {
                    var shape = ps.shape;
                    shape.textureClipThreshold = 0.5f;
                    for (int i = 0; i < calls; i++) ps.Emit(perCall);
                    var particles = Read(ps);
                    foreach (var p in particles) Assert.True(p.startColor.a >= 128, "a discarded particle stayed");
                    Assert.Empty(m_Logs);
                    return particles.Length;
                }
                finally { Object.DestroyImmediate(ps.gameObject); }
            }
            int single = Kept(320, 1), batch = Kept(1, 320);
            Assert.InRange(batch, 120, 200);
            Assert.InRange(single, 5, 45);
        }

        [Fact]
        public void EmitWithEmitParamsKeepsDiscardedParticlesUntilTheNextUpdate()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, Coords(4));
            try
            {
                var shape = ps.shape; shape.textureClipThreshold = 0.5f;
                ps.Emit(new ParticleSystem.EmitParams(), 40);
                var all = Read(ps);
                Assert.Equal(40, all.Length);
                int clipped = Array.FindAll(all, p => p.startColor.a == 0).Length;
                Assert.InRange(clipped, 5, 35);
                Assert.Empty(m_Logs);
                ps.Simulate(0.02f, true, false, false);
                var kept = Read(ps);
                Assert.Equal(40 - clipped, kept.Length);
                foreach (var p in kept) Assert.True(p.startColor.a >= 128);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void DiscardedLanesFireDeathSubEmittersWhileTheCountLasts()
        {
            // Every discarded lane of a burst fires the Death sub-emitter while the burst's count is
            // above zero; with nothing clipped none fires.
            int Children(float threshold)
            {
                var parent = Make(ParticleSystemShapeType.Rectangle, Coords(4));
                var child = Make(ParticleSystemShapeType.Rectangle, null);
                try
                {
                    child.transform.SetParent(parent.transform, false);
                    var cm = child.main; cm.startLifetime = 5f;
                    child.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                    var subs = parent.subEmitters; subs.enabled = true;
                    subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
                    var shape = parent.shape; shape.textureClipThreshold = threshold;
                    var e = parent.emission; e.enabled = true; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10) });
                    parent.Simulate(0.02f, true, true, true);
                    Assert.Equal(10 - Math.Min(10, child.particleCount), parent.particleCount);
                    return child.particleCount;
                }
                finally { Object.DestroyImmediate(parent.gameObject); }
            }
            Assert.Equal(0, Children(0f));
            Assert.InRange(Children(0.5f), 1, 10);
            // A threshold of 1 discards all twelve lanes: only the ten while the count lasts fire.
            Assert.Equal(10, Children(1f));
        }

        [Fact]
        public void AnEmitAfterAnUnalignedCountFillsTheGapWithItsLastParticles()
        {
            // The batch starts at the next multiple of four; its last particles move down into the
            // free slots before it (the unmeasured releases' per-particle path keeps emission order).
            uint[] Seeds()
            {
                var ps = Make(ParticleSystemShapeType.Rectangle, null);
                try
                {
                    ps.Emit(5);
                    ps.Emit(10);
                    return Array.ConvertAll(Read(ps), p => p.randomSeed);
                }
                finally { Object.DestroyImmediate(ps.gameObject); }
            }
            var batch = Seeds();
            ProjectInfo.UnityVersion = "2022.3.0f1";
            var sequential = Seeds();
            Assert.Equal(15, batch.Length);
            Assert.Equal(sequential[..5], batch[..5]);
            Assert.Equal(sequential[12..15], batch[5..8]);
            Assert.Equal(sequential[5..12], batch[8..15]);
        }

        [Fact]
        public void ModuleColourDataWithoutGradientsReadsTheDefaultOnes()
        {
            var g = ParticleSystem.MinMaxGradient.Read(TestYaml.DocumentBody("Thing:\n  minMaxState: 1\n"));
            Assert.Equal(ParticleSystemGradientMode.Gradient, g.mode);
            Assert.NotNull(g.gradientMax);
            Assert.NotNull(g.gradientMin);
            Assert.Equal(Color.white, g.Evaluate(0.5f));
        }

        [Fact]
        public void TheSettersKeepTheThresholdInZeroToOneAndTheChannelAtZeroOrMore()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, null);
            try
            {
                var shape = ps.shape;
                shape.textureClipThreshold = 1.5f; Assert.Equal(1f, shape.textureClipThreshold);
                shape.textureClipThreshold = -1f; Assert.Equal(0f, shape.textureClipThreshold);
                shape.textureUVChannel = -2; Assert.Equal(0, shape.textureUVChannel);
                shape.textureUVChannel = 7; Assert.Equal(7, shape.textureUVChannel);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void EmittingFromACachedTextureDoesNotAllocatePerParticle()
        {
            var ps = Make(ParticleSystemShapeType.Rectangle, Coords(16));
            try
            {
                var main = ps.main; main.maxParticles = 5000;
                // Warm up: the particle buffers and the texture cache reach their size.
                for (int i = 0; i < 2; i++) { ps.Emit(2000); ps.Clear(); }
                long Measure(int count)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    ps.Emit(count);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    ps.Clear();
                    return bytes;
                }
                long few = Measure(100), many = Measure(2000);
                // Twenty times the particles, no more memory: nothing is allocated per particle.
                Assert.True(many <= few + 256, $"{few} then {many} bytes");
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // ------------------------------------------------------------------ box shell and edge

        [Theory]
        [InlineData(ParticleSystemShapeType.BoxShell, 1)]
        [InlineData(ParticleSystemShapeType.BoxEdge, 2)]
        public void BoxShellAndEdgePointsLieOnTheirSideOfTheFaceOrEdge(ParticleSystemShapeType type, int onSides)
        {
            var ps = Make(type, null);
            try
            {
                ps.Emit(200);
                var axes = new int[3];
                foreach (var p in Read(ps))
                {
                    int sides = 0;
                    for (int k = 0; k < 3; k++)
                        if (Mathf.Abs(Mathf.Abs(p.position[k]) - 0.5f) < 1e-6f) { sides++; axes[k]++; }
                    Assert.Equal(onSides, sides);
                }
                foreach (int n in axes) Assert.True(n > 0, "every axis is chosen");
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // ------------------------------------------------------------------ line meshes

        static Mesh Lines(MeshTopology topology, int[] indices, params (int[] Indices, MeshTopology Topology)[] more)
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 9f, 0f), new Vector3(0f, 0f, 1f) };
            m.subMeshCount = 1 + more.Length;
            m.SetIndices(indices, topology, 0);
            for (int i = 0; i < more.Length; i++) m.SetIndices(more[i].Indices, more[i].Topology, i + 1);
            return m;
        }

        [Theory]
        [InlineData(ParticleSystemMeshShapeType.Vertex)]
        [InlineData(ParticleSystemMeshShapeType.Edge)]
        [InlineData(ParticleSystemMeshShapeType.Triangle)]
        public void LineSegmentsEmitAlongThemselvesWeighedByLength(ParticleSystemMeshShapeType meshType)
        {
            // Segment 0→1 of length 1, 1→2 of length 9.
            var ps = Make(ParticleSystemShapeType.Mesh, null, Lines(MeshTopology.Lines, new[] { 0, 1, 1, 2 }));
            try
            {
                var shape = ps.shape; shape.meshShapeType = meshType;
                ps.Emit(400);
                int longSide = 0;
                foreach (var p in Read(ps))
                {
                    bool first = Mathf.Abs(p.position.y) < 1e-5f && p.position.x >= -1e-5f && p.position.x <= 1f + 1e-5f;
                    bool second = Mathf.Abs(p.position.x - 1f) < 1e-5f && p.position.y >= -1e-5f && p.position.y <= 9f + 1e-5f;
                    Assert.True(first || second, $"{p.position} lies on no segment");
                    if (meshType == ParticleSystemMeshShapeType.Vertex)
                        Assert.True(p.position == Vector3.zero || p.position == Vector3.right || p.position == new Vector3(1f, 9f, 0f));
                    if (second && p.position.y > 1e-5f) longSide++;
                }
                if (meshType != ParticleSystemMeshShapeType.Vertex) Assert.InRange(longSide, 320, 395);
                Assert.DoesNotContain(m_Logs, l => l.Contains("not valid") || l.Contains("zero surface"));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void AMeshMixingPrimitiveTypesAssertsOnceAndEmitsFromBoth()
        {
            var mesh = Lines(MeshTopology.Triangles, new[] { 0, 1, 3 }, (new[] { 1, 2 }, MeshTopology.Lines));
            var ps = Make(ParticleSystemShapeType.Mesh, null, mesh);
            try
            {
                var shape = ps.shape; shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                ps.Emit(300);
                ps.Emit(300);
                Assert.Single(m_Logs, l => l == "Assert: Particle System can only spawn from mesh if all submeshes use the same primitive type");
                var particles = Read(ps);
                Assert.Contains(particles, p => p.position.y > 0.5f);
                Assert.Contains(particles, p => p.position.z > 1e-4f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void QuadsAreNotValidAndEmitAtTheOrigin()
        {
            var ps = Make(ParticleSystemShapeType.Mesh, null, Lines(MeshTopology.Quads, new[] { 0, 1, 2, 3 }));
            try
            {
                ps.Emit(5);
                foreach (var p in Read(ps)) Assert.Equal(Vector3.zero, p.position);
                Assert.Contains(m_Logs, l => l.Contains("Mesh used in Particle System Shape Module is not valid"));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // ------------------------------------------------------------------ obsolete shell types

        [Fact]
        public void AnObsoleteShellTypeSetFromScriptEmitsAtTheOriginAndAssertsOnce()
        {
#pragma warning disable 612, 618
            var ps = Make(ParticleSystemShapeType.SphereShell, Coords(4));
#pragma warning restore 612, 618
            try
            {
                ps.Emit(8);
                ps.Emit(8);
                foreach (var p in Read(ps))
                {
                    Assert.Equal(Vector3.zero, p.position);
                    Assert.Equal(new Color32(255, 255, 255, 255), p.startColor);
                }
                Assert.Single(m_Logs, l => l.StartsWith("Assert:") && l.Contains("Shape not implemented"));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void DataWrittenBeforeRadiusThicknessReadsAShellTypeAsTheShapeWithThicknessZero()
        {
            string yaml = ParticleFixtures.CoinsSystem.Replace("    type: 10\n", "    type: 1\n").Replace("    radiusThickness: 1\n", "");
            Assert.NotEqual(ParticleFixtures.CoinsSystem, yaml);
            var go = new GameObject("legacy");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            try
            {
                ps.ReadSerialized(TestYaml.DocumentBody(yaml), new NullResolver());
                Assert.Equal(ParticleSystemShapeType.Sphere, ps.shape.shapeType);
                Assert.Equal(0f, ps.shape.radiusThickness);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
