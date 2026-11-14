using System;
using System.Text;
using Unbound.SpeedTree;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The SpeedTree 9 reader's checks (the files of tests/projects/speedtree9 cover what it
    /// reads).</summary>
    public class SpeedTree9FileTests
    {
        static byte[] WithSignature(params byte[] rest)
        {
            var signature = Encoding.ASCII.GetBytes(SpeedTree9File.Signature);
            var data = new byte[signature.Length + rest.Length];
            signature.CopyTo(data, 0);
            rest.CopyTo(data, signature.Length);
            return data;
        }

        [Fact]
        public void TheSignatureMustLeadTheFile()
        {
            Assert.True(SpeedTree9File.HasSignature(WithSignature()));
            Assert.False(SpeedTree9File.HasSignature(Encoding.ASCII.GetBytes("SpeedTree8______")));
            Assert.False(SpeedTree9File.HasSignature(new byte[3]));
        }

        [Fact]
        public void ATableOrArrayOutsideTheFileIsAFormatError()
        {
            // A root table of 21 entries whose offsets point past the end.
            var root = new byte[4 + 21 * 4];
            BitConverter.GetBytes(21u).CopyTo(root, 0);
            for (int i = 0; i < 21; i++) BitConverter.GetBytes(0x7fff0000u).CopyTo(root, 4 + i * 4);
            Assert.Throws<FormatException>(() => SpeedTree9File.Read(WithSignature(root)));
            // An empty file after the signature.
            Assert.Throws<FormatException>(() => SpeedTree9File.Read(WithSignature()));
            // A count larger than the file holds.
            var huge = new byte[8];
            BitConverter.GetBytes(0x40000000u).CopyTo(huge, 0);
            Assert.Throws<FormatException>(() => SpeedTree9File.Read(WithSignature(huge)));
        }

        /// <summary>Mesh bounds centre on (min + max) * 0.5, which differs in the last bit from
        /// Bounds.SetMinMax's min + extents for these corners (tests/projects/speedtree9).</summary>
        [Fact]
        public void MeshBoundsCentreOnTheMeanOfTheCorners()
        {
            var min = new Vector3(BitConverter.Int32BitsToSingle(unchecked((int)0xbd3b44e7u)), 0f, 0f);
            var max = new Vector3(BitConverter.Int32BitsToSingle(0x3dbb44e7), 1f, 2f);
            var mesh = new Mesh { vertices = new[] { min, max } };
            var expected = (min + max) * 0.5f;
            var other = new Bounds();
            other.SetMinMax(min, max);
            Assert.NotEqual(BitConverter.SingleToInt32Bits(expected.x), BitConverter.SingleToInt32Bits(other.center.x));
            Assert.Equal(BitConverter.SingleToInt32Bits(expected.x), BitConverter.SingleToInt32Bits(mesh.bounds.center.x));
        }

        /// <summary>A wind curve keeps its first 20 samples: 21 are cut, 19 are refused.</summary>
        [Fact]
        public void WindCurvesKeepTheirFirstTwentySamples()
        {
            float[] Curve(int n)
            {
                var c = new float[n];
                for (int i = 0; i < n; i++) c[i] = i + 1;
                return c;
            }
            var target = new float[SpeedTreeWindCurves.Samples];
            SpeedTreeWindCurves.Copy(Curve(21), target);
            Assert.Equal(Curve(20), target);
            SpeedTreeWindCurves.Scale(Curve(21), target, 0.5f);
            Assert.Equal(20 * 0.5f, target[19]);
            SpeedTreeWindCurves.Copy(Curve(20), target);
            Assert.Equal(Curve(20), target);
            Assert.Throws<FormatException>(() => SpeedTreeWindCurves.Copy(Curve(19), target));
            Assert.Throws<FormatException>(() => SpeedTreeWindCurves.Scale(Curve(19), target, 2f));
        }
    }
}
