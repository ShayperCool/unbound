using System.Linq;
using UnityEditorInternal;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>InternalEditorUtility members that editor scripts use (tests/projects/editorinternals
    /// records the Editor's): the Inspector's expanded state and the layer popups' masks.</summary>
    public class EditorInternalsTests
    {
        public class Collapsible : MonoBehaviour { }

        public class OtherCollapsible : MonoBehaviour { }

        public class CollapsibleAsset : ScriptableObject { }

        [Fact]
        public void TheExpandedStateIsOneForATypeAndAMaterialsOwn()
        {
            var go = new GameObject("a");
            var go2 = new GameObject("b");
            var material = new Material(Shader.Create("GUI/Text Shader", 3000));
            var material2 = new Material(Shader.Create("GUI/Text Shader", 3000));
            var asset = ScriptableObject.CreateInstance<CollapsibleAsset>();
            var asset2 = ScriptableObject.CreateInstance<CollapsibleAsset>();
            try
            {
                var behaviour = go.AddComponent<Collapsible>();
                var behaviour2 = go2.AddComponent<Collapsible>();
                var other = go2.AddComponent<OtherCollapsible>();
                // Expanded until collapsed; a material collapsed until expanded; nothing for null.
                Assert.True(InternalEditorUtility.GetIsInspectorExpanded(behaviour));
                Assert.True(InternalEditorUtility.GetIsInspectorExpanded(asset));
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(material));
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(null));
                InternalEditorUtility.SetIsInspectorExpanded(null, true);
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(null));

                // Collapsing one object collapses its type: the other object of the same script
                // too, and one made afterwards; another script stays.
                InternalEditorUtility.SetIsInspectorExpanded(behaviour, false);
                InternalEditorUtility.SetIsInspectorExpanded(asset, false);
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(behaviour));
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(behaviour2));
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(asset2));
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(go.AddComponent<Collapsible>()));
                Assert.True(InternalEditorUtility.GetIsInspectorExpanded(other));
                Assert.True(InternalEditorUtility.GetIsInspectorExpanded(go.transform));
                InternalEditorUtility.SetIsInspectorExpanded(behaviour2, true);
                Assert.True(InternalEditorUtility.GetIsInspectorExpanded(behaviour));

                // A material's state is its own.
                InternalEditorUtility.SetIsInspectorExpanded(material2, true);
                Assert.True(InternalEditorUtility.GetIsInspectorExpanded(material2));
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(material));
                Object.DestroyImmediate(material2);
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(material2));
                InternalEditorUtility.SetIsInspectorExpanded(material2, true);
                Assert.False(InternalEditorUtility.GetIsInspectorExpanded(material2));
            }
            finally
            {
                InternalEditorUtility.SetIsInspectorExpanded(asset, true);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(go2);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(asset2);
            }
        }

        // With the built-in layers: Default 0, TransparentFX 1, Ignore Raycast 2, Water 4, UI 5.
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(4, 4)]
        [InlineData(8, 16)]
        [InlineData(16, 32)]
        [InlineData(31, 55)]
        [InlineData(5, 5)]
        [InlineData(-1, 55)]
        [InlineData(32, 0)]
        [InlineData(int.MinValue, 0)]
        public void ABitOfAConcatenatedMaskIsTheNthNamedLayer(int concatenated, int mask)
        {
            Assert.Equal(new[] { "Default", "TransparentFX", "Ignore Raycast", "Water", "UI" }, InternalEditorUtility.layers);
            Assert.Equal(mask, InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(concatenated).value);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(4, 4)]
        [InlineData(16, 8)]
        [InlineData(32, 16)]
        [InlineData(55, 31)]
        [InlineData(17, 9)]
        [InlineData(-1, 31)]
        // A layer without a name counts as the named layer before it.
        [InlineData(8, 4)]
        [InlineData(64, 16)]
        [InlineData(int.MinValue, 16)]
        public void ALayerMaskBecomesAMaskOverTheNamedLayers(int mask, int concatenated)
        {
            Assert.Equal(new[] { "Default", "TransparentFX", "Ignore Raycast", "Water", "UI" }, InternalEditorUtility.layers);
            Assert.Equal(concatenated, InternalEditorUtility.LayerMaskToConcatenatedLayersMask(mask));
        }

        [Fact]
        public void NoReloadIsPendingAndRepaintingDoesNothing()
        {
            Assert.False(InternalEditorUtility.IsScriptReloadRequested());
            InternalEditorUtility.RepaintAllViews();
        }
    }
}
