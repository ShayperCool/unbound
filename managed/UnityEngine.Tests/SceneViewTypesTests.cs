using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>SceneView.CameraMode and SceneView.SceneViewState as the Unity 6000.0.71f1 Editor
    /// answers them (tests/projects/sceneviewtypes).</summary>
    public sealed class SceneViewTypesTests
    {
        [Fact]
        public void ACameraModeIsItsDrawModeNameAndSectionWhateverItsShownFlag()
        {
            var shaded = new SceneView.CameraMode(DrawCameraMode.Textured, "Shaded", "Shading Mode", true);
            var hidden = new SceneView.CameraMode(DrawCameraMode.Textured, "Shaded", "Shading Mode", false);
            Assert.True(shaded == hidden);
            Assert.True(shaded.Equals((object)hidden));
            Assert.Equal(shaded.GetHashCode(), hidden.GetHashCode());
            Assert.True(shaded != new SceneView.CameraMode(DrawCameraMode.Wireframe, "Shaded", "Shading Mode"));
            Assert.False(shaded.Equals(null));
            Assert.Equal("Wireframe||Wire||Shading", new SceneView.CameraMode(DrawCameraMode.Wireframe, "Wire", "Shading").ToString());
            Assert.Equal("Textured||||", default(SceneView.CameraMode).ToString());
            // Null and empty names differ, though their texts (and hashes) agree.
            var nulls = new SceneView.CameraMode(DrawCameraMode.Textured, null, null);
            var empties = new SceneView.CameraMode(DrawCameraMode.Textured, "", "");
            Assert.False(nulls == empties);
            Assert.Equal(nulls.GetHashCode(), empties.GetHashCode());
        }

        [Fact]
        public void TheStatesEnabledAnswersNeedTheEffectsSwitch()
        {
            var state = new SceneView.SceneViewState();
            Assert.False(state.alwaysRefresh);
            Assert.True(state.fogEnabled);
            Assert.False(state.allEnabled);
            state.SetAllEnabled(true);
            Assert.True(state.allEnabled);
            bool? changed = null;
            state.fxEnableChanged += value => changed = value;
            state.fxEnabled = false;
            Assert.False(changed);
            Assert.False(state.fogEnabled);
            Assert.True(state.allEnabled);
            var copy = new SceneView.SceneViewState(state);
            Assert.False(copy.fxEnabled);
            Assert.True(copy.showClouds);
            Assert.Equal("{\"m_AlwaysRefresh\":true,\"showFog\":true,\"showSkybox\":true,\"showFlares\":true,\"showImageEffects\":true,\"showParticleSystems\":true,\"showVisualEffectGraphs\":true,\"m_FxEnabled\":false}",
                JsonUtility.ToJson(copy));
        }
    }
}
