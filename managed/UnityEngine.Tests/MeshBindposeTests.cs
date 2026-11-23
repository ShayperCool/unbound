using System;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MeshBindposeTests
    {
        [Fact]
        public void ResizedOrDestroyedStorageInvalidatesItsBorrowedViews()
        {
            var mesh = new Mesh { bindposes = new[] { Matrix4x4.identity } };
            try
            {
                var onePose = mesh.GetBindposes();
                Assert.True(onePose.IsCreated);
                mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) };
                Assert.Throws<ObjectDisposedException>(() => onePose[0]);

                var twoPoses = mesh.GetBindposes();
                Assert.Equal(Matrix4x4.Translate(Vector3.right), twoPoses[1]);
                Object.DestroyImmediate(mesh);
                Assert.Throws<ObjectDisposedException>(() => twoPoses[1]);
            }
            finally { if (mesh != null) Object.DestroyImmediate(mesh); }
        }
    }
}
