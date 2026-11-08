using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The colors a mesh uploads are converted again only when they changed: by a
    /// setter, written in place into the mesh's own array, or as float colors (kept unrounded).</summary>
    public sealed class MeshRenderColorsTests
    {
        static Mesh Triangle()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.colors32 = new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255) };
            return mesh;
        }

        [Fact]
        public void UnchangedColorsKeepTheirConversion()
        {
            var mesh = Triangle();
            try
            {
                var first = mesh.RenderColors();
                var converted = (Vector4[])first.Clone();
                first[0] = new Vector4(9f, 9f, 9f, 9f);
                // Not converted again: the same array, as it was left.
                Assert.Same(first, mesh.RenderColors());
                Assert.Equal(new Vector4(9f, 9f, 9f, 9f), mesh.RenderColors()[0]);
                first[0] = converted[0];
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void EveryChangeOfTheColorsReachesTheUpload()
        {
            var mesh = Triangle();
            try
            {
                Assert.Equal(new Vector4(1f, 0f, 0f, 1f), mesh.RenderColors()[0]);
                // Written in place into the array the mesh keeps.
                mesh.m_Colors[1] = new Color32(0, 0, 0, 0);
                Assert.Equal(Vector4.zero, mesh.RenderColors()[1]);
                // A setter.
                mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255) };
                Assert.Equal(Vector4.one, mesh.RenderColors()[0]);
                // Float colors keep their values (an HDR one above 1).
                mesh.colors = new[] { new Color(2f, 0.5f, 0.25f, 1f), Color.green, Color.blue };
                Assert.Equal(new Vector4(2f, 0.5f, 0.25f, 1f), mesh.RenderColors()[0]);
                mesh.colors = new[] { new Color(3f, 0.5f, 0.25f, 1f), Color.green, Color.blue };
                Assert.Equal(new Vector4(3f, 0.5f, 0.25f, 1f), mesh.RenderColors()[0]);
                // Back to 8-bit colors.
                mesh.colors32 = new[] { new Color32(0, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255) };
                Assert.Equal(new Vector4(0f, 0f, 0f, 1f), mesh.RenderColors()[0]);
                mesh.colors32 = null;
                Assert.Null(mesh.RenderColors());
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
