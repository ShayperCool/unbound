using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Where the lightmapper's Open Image Denoise cannot be had (no release for the platform,
    /// offline), lightmaps the Lighting Settings denoise are not made denoised: the filtering is
    /// declined (said, the lighting data not written), so the bake keeps its layout instead of
    /// failing when it reaches the denoiser.
    /// </summary>
    [Collection("Frames")]
    public class DenoiserUnavailableTests
    {
        static string WithDenoiser(string why, System.Func<string> check)
        {
            var previous = RealtimeGI.DenoiserUnavailable;
            RealtimeGI.DenoiserUnavailable = () => why;
            try { return check(); }
            finally { RealtimeGI.DenoiserUnavailable = previous; }
        }

        [Fact]
        public void TheDefaultAutoFilteringIsDeclinedWithoutTheDenoiser()
        {
            var settings = new LightingSettings { filteringMode = LightingSettings.FilterMode.Auto };
            Assert.Contains("Open Image Denoise", WithDenoiser("no release for this platform", () => RealtimeGI.UnmadeFiltering(settings)));
            Assert.True(WithDenoiser("offline", () => RealtimeGI.Filtering(settings) == null ? "declined" : null) == "declined");
            // With the denoiser the Auto filtering is made.
            Assert.Null(WithDenoiser(null, () => RealtimeGI.UnmadeFiltering(settings)));
            Assert.NotNull(WithDenoiser(null, () => RealtimeGI.Filtering(settings) == null ? null : "made"));
        }

        [Fact]
        public void OnlyFilteringThatDenoisesNeedsTheDenoiser()
        {
            var settings = new LightingSettings
            {
                filteringMode = LightingSettings.FilterMode.Advanced,
                denoiserTypeDirect = LightingSettings.DenoiserType.None,
                denoiserTypeIndirect = LightingSettings.DenoiserType.None,
                denoiserTypeAO = LightingSettings.DenoiserType.OpenImage,
                filterTypeDirect = LightingSettings.FilterType.Gaussian,
                filterTypeIndirect = LightingSettings.FilterType.None,
                ao = false,
            };
            Assert.Null(WithDenoiser("offline", () => RealtimeGI.UnmadeFiltering(settings)));
            // The ambient occlusion target's denoiser counts only with ambient occlusion on.
            settings.ao = true;
            Assert.Contains("Open Image Denoise", WithDenoiser("offline", () => RealtimeGI.UnmadeFiltering(settings)));
            settings.ao = false;
            settings.denoiserTypeIndirect = LightingSettings.DenoiserType.OpenImage;
            Assert.Contains("offline", WithDenoiser("offline", () => RealtimeGI.UnmadeFiltering(settings)));
            settings.filteringMode = LightingSettings.FilterMode.None;
            Assert.Null(WithDenoiser("offline", () => RealtimeGI.UnmadeFiltering(settings)));
        }
    }
}
