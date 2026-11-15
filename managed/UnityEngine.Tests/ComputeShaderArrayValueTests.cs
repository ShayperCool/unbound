using System.Linq;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>ComputeShader's array setters take the elements an array has at the call: a caller
    /// may fill one scratch array for several variables (the GPU Resident Drawer sets its wind
    /// parameter addresses and its history addresses from one array before a dispatch).</summary>
    public class ComputeShaderArrayValueTests
    {
        static float[] Values(ComputeShader shader, string name) =>
            shader.Properties(0, null).Single(p => p.Name == name).Array;

        [Fact]
        public void ArraysAreCopiedAtTheCall()
        {
            var shader = ComputeShader.FromFile("arrays.compute", "arrays", "#pragma kernel Main");
            var scratch = new int[8];
            for (int i = 0; i < 8; i++) scratch[i] = i;
            shader.SetInts("_First", scratch);
            for (int i = 0; i < 8; i++) scratch[i] = 100 + i;
            shader.SetInts("_Second", scratch);
            Assert.Equal(new float[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Values(shader, "_First"));
            Assert.Equal(new float[] { 100, 101, 102, 103, 104, 105, 106, 107 }, Values(shader, "_Second"));

            // Setting a variable again with a changed array takes the new elements, also with
            // another length.
            scratch[0] = 42;
            shader.SetInts("_First", scratch);
            Assert.Equal(42f, Values(shader, "_First")[0]);
            shader.SetInts("_First", 9, 8);
            Assert.Equal(new float[] { 9, 8 }, Values(shader, "_First"));

            var floats = new[] { 1f, 2f };
            shader.SetFloats("_Floats", floats);
            floats[0] = 5f;
            Assert.Equal(new[] { 1f, 2f }, Values(shader, "_Floats"));

            var vectors = new[] { new Vector4(1, 2, 3, 4) };
            shader.SetVectorArray("_Vectors", vectors);
            vectors[0] = Vector4.zero;
            Assert.Equal(new[] { 1f, 2f, 3f, 4f }, Values(shader, "_Vectors"));

            var matrices = new[] { Matrix4x4.identity };
            shader.SetMatrixArray("_Matrices", matrices);
            matrices[0] = Matrix4x4.zero;
            Assert.Equal(1f, Values(shader, "_Matrices")[0]);
            Object.DestroyImmediate(shader);
        }
    }
}
