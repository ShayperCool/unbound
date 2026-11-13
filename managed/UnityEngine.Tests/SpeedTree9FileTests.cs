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
    }
}
