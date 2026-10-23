using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class HexArrayTests
    {
        enum Cell { A = 1, B = 2, C = 5 }
        enum Small : byte { X = 1, Y = 7 }

        [Fact]
        public void EnumsUseTheirUnderlyingSize()
        {
            var cells = (Cell[])BuiltinReaders.HexArray("010000000500000002000000", typeof(Cell));
            Assert.Equal(new[] { Cell.A, Cell.C, Cell.B }, cells);
            var small = (Small[])BuiltinReaders.HexArray("0107", typeof(Small));
            Assert.Equal(new[] { Small.X, Small.Y }, small);
        }

        [Fact]
        public void CharsAreTwoBytes()
        {
            var chars = (char[])BuiltinReaders.HexArray("41004200", typeof(char));
            Assert.Equal(new[] { 'A', 'B' }, chars);
        }
    }
}
