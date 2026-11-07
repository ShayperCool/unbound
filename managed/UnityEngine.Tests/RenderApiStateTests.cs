using System;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Project release")]
    public class RenderApiStateTests
    {
        [Fact]
        public void PixelGridChangesWorldTranslationWithoutChangingRotationOrScale()
        {
            float saved = PixelPerfectRendering.pixelSnapSpacing;
            try
            {
                var model = Matrix4x4.TRS(new Vector3(.17f, -.19f, .38f), Quaternion.Euler(20, 30, 40), new Vector3(2, 3, 4));
                PixelPerfectRendering.pixelSnapSpacing = .25f;
                var snapped = PixelPerfectRendering.Snap(model);
                Assert.Equal(.25f, snapped.m03);
                Assert.Equal(-.25f, snapped.m13);
                Assert.Equal(.5f, snapped.m23);
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 3; col++) Assert.Equal(model[row,col], snapped[row,col]);
                PixelPerfectRendering.pixelSnapSpacing = -.25f;
                Assert.Equal(snapped, PixelPerfectRendering.Snap(model));
                foreach (float value in new[] {0f})
                {
                    PixelPerfectRendering.pixelSnapSpacing = value;
                    Assert.Equal(model, PixelPerfectRendering.Snap(model));
                }
                foreach (float value in new[] {float.NaN, float.PositiveInfinity})
                {
                    PixelPerfectRendering.pixelSnapSpacing = value;
                    Assert.Equal(value, PixelPerfectRendering.pixelSnapSpacing);
                    Assert.Throws<NotSupportedException>(() => PixelPerfectRendering.Snap(model));
                }
            }
            finally { PixelPerfectRendering.pixelSnapSpacing = saved; }
        }

        [Fact]
        public void GizmoArgumentsAndStateMatchCallsOutsideRepaintCallbacks()
        {
            var color = Gizmos.color; var matrix = Gizmos.matrix; var exposure = Gizmos.exposure;
            try
            {
                Gizmos.color = new Color(.2f, .3f, .4f, .5f);
                Assert.Equal((Color)new Color32(51,77,102,128), Gizmos.color);
                var transform = Matrix4x4.Translate(new Vector3(3,4,5));
                Gizmos.matrix = transform; Assert.Equal(transform, Gizmos.matrix);
                Assert.True(float.IsNaN(Gizmos.CalculateLOD(Vector3.zero, 1)));
                Assert.Throws<ArgumentException>(() => Gizmos.DrawMesh(null));
                Assert.Throws<ArgumentException>(() => Gizmos.DrawLineStrip(Array.Empty<Vector3>(), true));
                Assert.Throws<UnityException>(() => Gizmos.DrawLineList(new Vector3[3]));
                Assert.Throws<ArgumentException>(() => Gizmos.DrawLineList(new Vector3[4]));
                Gizmos.DrawFrustum(Vector3.zero,60,10,.1f,1);
            }
            finally { Gizmos.color=color;Gizmos.matrix=matrix;Gizmos.exposure=exposure; }
        }

        [Fact]
        public void SortingQueriesAndDelegateSubscriptionDoNotSynthesizeLayerChanges()
        {
            int count = 0;
            SortingLayer.LayerCallback callback = _ => count++;
            SortingLayer.onLayerAdded += callback; SortingLayer.onLayerRemoved += callback;
            try
            {
                foreach (var layer in SortingLayer.layers)
                {
                    Assert.True(SortingLayer.IsValid(layer.id));
                    Assert.Equal(layer.id, SortingLayer.NameToID(layer.name));
                    Assert.Equal(layer.value, SortingLayer.GetLayerValueFromName(layer.name));
                }
                Assert.Equal(0, SortingLayer.NameToID(null));
                Assert.Equal("<unknown layer>", SortingLayer.IDToName(int.MaxValue));
                Assert.Equal(0, count);
            }
            finally { SortingLayer.onLayerAdded -= callback; SortingLayer.onLayerRemoved -= callback; }
        }
    }
}
