using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unbound.SpeedTree;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Where the SpeedTree 7/8 sample trees are, which are never part of the repository:
    /// <c>UNBOUND_SPEEDTREE_SAMPLES</c>, else <c>$XDG_CACHE_HOME/unbound/speedtree-samples</c>
    /// (<c>~/.cache</c> by default), a directory with <c>v7/</c> and <c>v8/</c> holding the
    /// SpeedTree sample trees of Unity's Graphics test projects.
    /// </summary>
    static class SpeedTreeSamples
    {
        internal const string Variable = "UNBOUND_SPEEDTREE_SAMPLES";

        internal static string Directory
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable(Variable);
                if (string.IsNullOrEmpty(dir))
                {
                    string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                    if (string.IsNullOrEmpty(cache)) cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                    dir = Path.Combine(cache, "unbound", "speedtree-samples");
                }
                return System.IO.Directory.Exists(Path.Combine(dir, "v8")) ? dir : null;
            }
        }

        internal static byte[] Read(string relative) => File.ReadAllBytes(Path.Combine(Directory, relative));
    }

    /// <summary>A test on the SpeedTree sample trees, skipped (with the reason) where they are absent.</summary>
    sealed class SpeedTreeSamplesFactAttribute : FactAttribute
    {
        public SpeedTreeSamplesFactAttribute()
        {
            if (SpeedTreeSamples.Directory == null)
                Skip = $"the SpeedTree sample trees are absent: set {SpeedTreeSamples.Variable} to a directory with v7/ and v8/";
        }
    }

    /// <summary>The SpeedTree 8 reader. The layout and the expected values are the ones the
    /// Unity 6000.0.71f1 Editor imports from the sample trees: mesh vertex and index counts,
    /// material settings, collider sizes and the wind asset's values.</summary>
    public class SpeedTree8FileTests
    {
        const float FeetToMeters = 0.3048f;

        /// <summary>Writes a SpeedTree 8 file of the measured layout from its root entries.</summary>
        sealed class Writer
        {
            readonly bool m_WideBools;
            internal Writer(bool wideBools) => m_WideBools = wideBools;

            internal static byte[] U(uint v) => BitConverter.GetBytes(v);
            internal static byte[] F(params float[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
            internal byte[] B(bool v) => m_WideBools ? U(v ? 1u : 0u) : new[] { (byte)(v ? 1 : 0) };

            internal static byte[] S(string s)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                return U((uint)bytes.Length + 1).Concat(bytes).Concat(new byte[1]).ToArray();
            }

            internal static byte[] Table(params byte[][] entries)
            {
                int header = 4 + 4 * entries.Length;
                var data = new List<byte>(U((uint)entries.Length));
                int at = header;
                foreach (var e in entries) { data.AddRange(U((uint)at)); at += e.Length; }
                foreach (var e in entries) data.AddRange(e);
                return data.ToArray();
            }

            internal static byte[] File(params byte[][] root) =>
                Encoding.ASCII.GetBytes(SpeedTree8File.Signature).Concat(Table(root)).ToArray();
        }

        /// <summary>A wind entry whose word i (after the leading uint32) holds the float i, and whose
        /// options are on for every third one.</summary>
        static byte[] NumberedWind()
        {
            var wind = new List<byte>(Writer.U(4));
            for (int i = 1; i < 320; i++) wind.AddRange(Writer.F(i));
            for (int i = 0; i < SpeedTree8File.WindOptionCount; i++) wind.Add((byte)(i % 3 == 0 ? 1 : 0));
            Assert.Equal(SpeedTree8File.WindSize, wind.Count);
            return wind.ToArray();
        }

        static byte[] Vertex(float first)
        {
            var words = new float[35];
            for (int i = 0; i < words.Length; i++) words[i] = first + i;
            return Writer.F(words);
        }

        static byte[] SyntheticFile(uint major, bool wideBools, bool lastLodIsBillboard = true, byte[] wind = null)
        {
            var w = new Writer(wideBools);
            var lod = Writer.Table(
                Writer.U(2).Concat(Vertex(100)).Concat(Vertex(200)).ToArray(),
                Writer.U(3).Concat(Writer.U(0)).Concat(Writer.U(1)).Concat(Writer.U(1)).ToArray(),
                Writer.U(1).Concat(Writer.U(1)).Concat(Writer.U(3)).Concat(Writer.U(0)).Concat(Writer.U(3)).ToArray());
            var map = Writer.Table(w.B(true), Writer.S("bark.png"), Writer.F(1f, 0.5f, 0.25f, 0f));
            var material = Writer.Table(Writer.S("Bark_Mat"), w.B(true), w.B(false), Writer.Table(map));
            var collision = Writer.Table(Writer.F(1, 2, 3), Writer.F(4, 5, 6), Writer.F(7), Writer.S(""));
            return Writer.File(
                Writer.U(major), Writer.U(0), w.B(lastLodIsBillboard),
                Writer.Table(lod), Writer.Table(collision), Writer.Table(material),
                Writer.U(128), wind ?? NumberedWind(), Writer.U(0));
        }

        [Fact]
        public void TheSignatureMustLeadTheFile()
        {
            Assert.True(SpeedTree8File.HasSignature(Encoding.ASCII.GetBytes("SpeedTree___rest")));
            Assert.False(SpeedTree8File.HasSignature(Encoding.ASCII.GetBytes("SpeedTree9______")));
            Assert.False(SpeedTree8File.HasSignature(new byte[3]));
        }

        /// <summary>Version 8 files keep booleans in one byte, version 10 files in four: both read.</summary>
        [Theory]
        [InlineData(8u, false)]
        [InlineData(10u, true)]
        public void EntriesReadAtTheirPlaces(uint major, bool wideBools)
        {
            var file = SpeedTree8File.Read(SyntheticFile(major, wideBools));
            Assert.Equal(major, file.VersionMajor);
            Assert.Equal(0u, file.VersionMinor);
            Assert.True(file.LastLodIsBillboard);
            Assert.False(SpeedTree8File.Read(SyntheticFile(major, wideBools, lastLodIsBillboard: false)).LastLodIsBillboard);

            var lod = Assert.Single(file.Lods);
            Assert.Equal(new uint[] { 0, 1, 1 }, lod.Indices);
            var draw = Assert.Single(lod.DrawCalls);
            Assert.Equal(1u, draw.MaterialIndex);
            Assert.Equal(SpeedTree8File.GeometryType.FacingLeaf, draw.GeometryType);
            Assert.Equal(0u, draw.IndexStart);
            Assert.Equal(3u, draw.IndexCount);
            Assert.Equal(2, lod.Vertices.Length);
            var v = lod.Vertices[1];
            // Word i of the second vertex holds 200 + i.
            Assert.Equal((200f, 201f, 202f), (v.Anchor.X, v.Anchor.Y, v.Anchor.Z));
            Assert.Equal(203f, v.Offset.X);
            Assert.Equal(206f, v.LodOffset.X);
            Assert.Equal(209f, v.Normal.X);
            Assert.Equal(212f, v.Tangent.X);
            Assert.Equal(215f, v.Binormal.X);
            Assert.Equal((218f, 219f, 220f, 221f), (v.U, v.V, v.LightmapU, v.LightmapV));
            Assert.Equal((222f, 223f, 224f), (v.Color.X, v.Color.Y, v.Color.Z));
            Assert.Equal((225f, 226f), (v.BranchWindX, v.BranchWindY));
            Assert.Equal((227f, 228f, 229f), (v.LeafWind.X, v.LeafWind.Y, v.LeafWind.Z));
            Assert.Equal((231f, 232f), (v.AmbientOcclusion, v.BlendWeight));

            var collision = Assert.Single(file.Collisions);
            Assert.Equal((1f, 6f, 7f), (collision.Position.X, collision.Position2.Z, collision.Radius));

            var material = Assert.Single(file.Materials);
            Assert.Equal("Bark_Mat", material.Name);
            Assert.True(material.TwoSided);
            Assert.False(material.Billboard);
            var map = Assert.Single(material.Maps);
            Assert.True(map.Used);
            Assert.Equal("bark.png", map.Path);
            Assert.Equal((1f, 0.5f, 0.25f, 0f), (map.R, map.G, map.B, map.A));
        }

        /// <summary>The wind's fields sit at the words the Editor's wind asset takes them from
        /// (word 0 is the leading uint32; word i holds i).</summary>
        [Fact]
        public void WindFieldsSitAtTheirWords()
        {
            var wind = SpeedTree8File.Read(SyntheticFile(8, false)).WindConfig;
            Assert.Equal(4u, wind.Leading);
            Assert.Equal((1f, 2f, 3f, 4f), (wind.StrengthResponse, wind.DirectionResponse, wind.AnchorOffset, wind.AnchorDistanceScale));
            Assert.Equal((5f, 6f, 7f, 8f, 9f, 10f, 11f), (wind.GustFrequency, wind.GustStrengthMin, wind.GustStrengthMax,
                wind.GustDurationMin, wind.GustDurationMax, wind.GustRiseScalar, wind.GustFallScalar));
            Assert.Equal(10, wind.Oscillation.Length);
            Assert.Equal(12f, wind.Oscillation[0][0]);
            Assert.Equal(111f, wind.Oscillation[9][9]);
            Assert.Equal((112f, 113f), (wind.GlobalHeight, wind.GlobalHeightExponent));
            Assert.Equal(114f, wind.GlobalDistance[0]);
            Assert.Equal(133f, wind.GlobalDirectionAdherence[9]);
            Assert.Equal(134f, wind.BranchLevels[0].Distance[0]);
            Assert.Equal(144f, wind.BranchLevels[0].DirectionAdherence[0]);
            Assert.Equal(154f, wind.BranchLevels[0].Whip[0]);
            Assert.Equal((164f, 165f, 166f), (wind.BranchLevels[0].Turbulence, wind.BranchLevels[0].Twitch, wind.BranchLevels[0].TwitchFreqScale));
            Assert.Equal(167f, wind.BranchLevels[1].Distance[0]);
            Assert.Equal(199f, wind.BranchLevels[1].TwitchFreqScale);
            Assert.Equal((200f, 201f, 202f, 203f), (wind.BranchWindAnchor.X, wind.BranchWindAnchor.Y, wind.BranchWindAnchor.Z, wind.MaxBranchLevel1Length));
            var leaf = wind.LeafGroups[0];
            Assert.Equal((204f, 214f, 224f, 234f, 244f), (leaf.RippleDistance[0], leaf.TumbleFlip[0], leaf.TumbleTwist[0], leaf.TumbleDirectionAdherence[0], leaf.TwitchThrow[0]));
            Assert.Equal((254f, 255f), (leaf.TwitchSharpness, leaf.LeewardScalar));
            Assert.Equal((256f, 306f, 307f), (wind.LeafGroups[1].RippleDistance[0], wind.LeafGroups[1].TwitchSharpness, wind.LeafGroups[1].LeewardScalar));
            Assert.Equal((308f, 317f), (wind.FrondRippleDistance[0], wind.FrondRippleDistance[9]));
            Assert.Equal((318f, 319f), (wind.FrondRippleTile, wind.FrondRippleLightingScalar));
            Assert.Equal(SpeedTree8File.WindOptionCount, wind.Options.Length);
            for (int i = 0; i < wind.Options.Length; i++) Assert.Equal(i % 3 == 0, wind.Options[i]);
            Assert.True(wind[SpeedTree8File.WindOption.GlobalWind]);
            Assert.True(wind[SpeedTree8File.WindOption.Rolling]);
            Assert.False(wind[SpeedTree8File.WindOption.LeafOcclusion2]);
        }

        [Theory]
        [InlineData(7u)]
        [InlineData(9u)]
        [InlineData(11u)]
        public void AnUnmeasuredVersionIsRefused(uint major)
        {
            var e = Assert.Throws<NotSupportedException>(() => SpeedTree8File.Read(SyntheticFile(major, false)));
            Assert.Contains($"version {major}.0 is not supported", e.Message);
        }

        [Fact]
        public void ATruncatedFileIsAFormatError()
        {
            var whole = SyntheticFile(8, false);
            // Cut inside the wind.
            Assert.Throws<FormatException>(() => SpeedTree8File.Read(whole.Take(whole.Length - 100).ToArray()));
            // An empty file after the signature.
            Assert.Throws<FormatException>(() => SpeedTree8File.Read(Encoding.ASCII.GetBytes(SpeedTree8File.Signature)));
            // A vertex count larger than the file holds.
            var lods = Writer.Table(Writer.Table(Writer.U(0x40000000), Writer.U(0), Writer.U(0)));
            var huge = Writer.File(Writer.U(8), Writer.U(0), new byte[] { 0 }, lods, Writer.U(0), Writer.U(0), Writer.U(0), NumberedWind(), Writer.U(0));
            Assert.Throws<FormatException>(() => SpeedTree8File.Read(huge));
            // A wind entry shorter than its layout, at the end of the file but for the bones' entry.
            Assert.Throws<FormatException>(() => SpeedTree8File.Read(SyntheticFile(8, false, wind: NumberedWind().Take(1200).ToArray())));
        }

        static void AssertBits(float expected, float actual) =>
            Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

        /// <summary>A version 10 file (four-byte booleans), three mesh LODs, camera-facing leaves.</summary>
        [SpeedTreeSamplesFact]
        public void TheCardTreeSampleReads()
        {
            var file = SpeedTree8File.Read(SpeedTreeSamples.Read("v8/Card_Tree_Sample/card-tree-sample.st"));
            Assert.Equal((10u, 0u), (file.VersionMajor, file.VersionMinor));
            Assert.False(file.LastLodIsBillboard);
            // The Editor's meshes: vertex and index counts per LOD; one material, so one submesh of both draws.
            Assert.Equal(new[] { 8141, 5293, 3428 }, file.Lods.Select(l => l.Vertices.Length));
            Assert.Equal(new[] { 27135, 19167, 13326 }, file.Lods.Select(l => l.Indices.Length));
            foreach (var lod in file.Lods)
            {
                Assert.Equal(new[] { SpeedTree8File.GeometryType.Branch, SpeedTree8File.GeometryType.FacingLeaf }, lod.DrawCalls.Select(d => d.GeometryType));
                Assert.All(lod.DrawCalls, d => Assert.Equal(0u, d.MaterialIndex));
                Assert.Equal((uint)lod.Indices.Length, lod.DrawCalls[1].IndexStart + lod.DrawCalls[1].IndexCount);
            }
            var material = Assert.Single(file.Materials);
            Assert.Equal("card-tree-sample_Mat", material.Name);
            // The Editor's material: _TwoSided 0 (no culling), doubleSidedGI on, no billboard.
            Assert.True(material.TwoSided);
            Assert.False(material.Billboard);
            Assert.Equal(new[] { "card-tree-sample_Color.png", "", "card-tree-sample_Extra.png", "", "" }, material.Maps.Select(m => m.Path));
            Assert.Equal(new[] { true, true, true, false, false }, material.Maps.Select(m => m.Used));
            // The Editor's capsule radii, in metres.
            Assert.Equal(2, file.Collisions.Length);
            AssertBits(0.323088f, file.Collisions[0].Radius * FeetToMeters);
            AssertBits(2.359146f, file.Collisions[1].Radius * FeetToMeters);

            var wind = file.WindConfig;
            Assert.Equal(14f, wind.StrengthResponse);
            Assert.Equal(0.5f, wind.GustFallScalar);
            AssertBits(26.44241f, wind.GlobalHeight * FeetToMeters);
            AssertBits(0.0121920006f, wind.FrondRippleDistance[9] * FeetToMeters);
            Assert.Equal(10f, wind.FrondRippleTile);
            Assert.Equal(0.5f, wind.LeafGroups[1].LeewardScalar);
            // The Editor keeps the branch wind anchor as the file has it.
            AssertBits(-0.0565005653f, wind.BranchWindAnchor.X);
            AssertBits(42.3625336f, wind.BranchWindAnchor.Y);
            AssertBits(-0.07640057f, wind.BranchWindAnchor.Z);
            AssertBits(5.36846066f, wind.MaxBranchLevel1Length * FeetToMeters);
            Assert.Equal(new[] { 1, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0, 1, 1, 1, 1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 0, 0, 1, 0 },
                wind.Options.Select(o => o ? 1 : 0));
        }

        /// <summary>A version 8 file (one-byte booleans): three mesh LODs and a billboard LOD.</summary>
        [SpeedTreeSamplesFact]
        public void TheBroadleafFieldSampleReads()
        {
            var file = SpeedTree8File.Read(SpeedTreeSamples.Read("v8/Broadleaf_Desktop_Field.st"));
            Assert.Equal((8u, 0u), (file.VersionMajor, file.VersionMinor));
            Assert.True(file.LastLodIsBillboard);
            Assert.Equal(new[] { 13888, 6873, 3401, 54 }, file.Lods.Select(l => l.Vertices.Length));
            // The Editor's submeshes: the draws of one material share one.
            Assert.Equal(new[] { 25968 + 15426, 10506 + 8067, 3648 + 4536, 108 }, file.Lods.Select(l => l.Indices.Length));
            Assert.Equal(new[] { SpeedTree8File.GeometryType.Branch, SpeedTree8File.GeometryType.Branch, SpeedTree8File.GeometryType.Leaf },
                file.Lods[0].DrawCalls.Select(d => d.GeometryType));
            Assert.Equal(new uint[] { 0, 2, 2 }, file.Lods[0].DrawCalls.Select(d => d.MaterialIndex));
            var billboard = Assert.Single(file.Lods[3].DrawCalls);
            Assert.Equal(SpeedTree8File.GeometryType.Billboard, billboard.GeometryType);
            Assert.Equal(1u, billboard.MaterialIndex);
            // The Editor's materials: Main_Bark and the billboard cull back faces, the leaves do not;
            // only the billboard material is a billboard.
            Assert.Equal(new[] { "Main_Bark_Mat", "Broadleaf_Desktop_Field_Billboard_Mat", "Broadleaf_Desktop_Field_Mat" }, file.Materials.Select(m => m.Name));
            Assert.Equal(new[] { false, false, true }, file.Materials.Select(m => m.TwoSided));
            Assert.Equal(new[] { false, true, false }, file.Materials.Select(m => m.Billboard));
            Assert.Equal(new[] { "Main_Bark_Color.png", "Main_Bark_Normal.png", "Main_Bark_Extra.png", "", "" }, file.Materials[0].Maps.Select(m => m.Path));
            Assert.Equal("Broadleaf_Desktop_Field_Subsurface.png", file.Materials[2].Maps[3].Path);
            Assert.Equal(2, file.Collisions.Length);
            AssertBits(4.92252f, file.Collisions[0].Radius * FeetToMeters);
            AssertBits(0.1920243f, file.Collisions[1].Radius * FeetToMeters);

            var wind = file.WindConfig;
            AssertBits(18.4603767f, wind.GlobalHeight * FeetToMeters);
            AssertBits(1.2192f, wind.FrondRippleDistance[9] * FeetToMeters);
            AssertBits(10.34f, wind.FrondRippleTile);
            AssertBits(-0.391773552f, wind.BranchWindAnchor.X);
            AssertBits(-0.0230067577f, wind.BranchWindAnchor.Y);
            AssertBits(28.8850822f, wind.BranchWindAnchor.Z);
            AssertBits(4.827353f, wind.MaxBranchLevel1Length * FeetToMeters);
        }
    }
}
