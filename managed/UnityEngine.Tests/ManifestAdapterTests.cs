using System;
using System.Globalization;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class ManifestAdapterTests
    {
        static AssetBundleManifest Load(string dependency = "base")
        {
            var manifest = new AssetBundleManifest();
            Assert.True(AssetAdapters.TryRead(manifest, TestYaml.DocumentBody(@"
AssetBundleManifest:
  AssetBundleNames:
  - first: 1
    second: " + dependency + @"
  - first: 3
    second: scene
  AssetBundlesWithVariant:
  - scene
  AssetBundleInfos:
  - first: 1
    second:
      AssetBundleHash:
        bytes[0]: 17
      AssetBundleDependencies: []
  - first: 3
    second:
      AssetBundleHash:
        bytes[0]: 29
      AssetBundleDependencies:
      - 1
"), null));
            return manifest;
        }

        [Fact]
        public void ManifestCopiesOwnTheLoadedPayloadAndReturnedArrays()
        {
            var source = Load("z-base");
            source.name = "source";
            source.hideFlags = HideFlags.DontSave;
            var copy = Object.Instantiate(source);
            try
            {
                Assert.NotSame(source, copy);
                Assert.Equal("source(Clone)", copy.name);
                Assert.Equal(HideFlags.HideAndDontSave, copy.hideFlags);
                Assert.Equal(HideFlags.DontSave, source.hideFlags);
                Assert.Equal(new[] { "scene", "z-base" }, copy.GetAllAssetBundles());
                Assert.Equal(new[] { "scene" }, copy.GetAllAssetBundlesWithVariant());
                Assert.Equal(new[] { "z-base" }, copy.GetDirectDependencies("scene"));
                Assert.Equal(source.GetAssetBundleHash("scene"), copy.GetAssetBundleHash("scene"));
                copy.GetAllAssetBundles()[0] = "changed";
                copy.GetDirectDependencies("scene")[0] = "changed";
                copy.GetAllAssetBundlesWithVariant()[0] = "changed";
                Assert.Equal("z-base", copy.GetAllDependencies("scene")[0]);
                Assert.Equal("z-base", source.GetAllDependencies("scene")[0]);
                Assert.True(AssetAdapters.TryRead(source, TestYaml.DocumentBody(@"
AssetBundleManifest:
  AssetBundleInfos:
  - first: 3
    second:
      AssetBundleDependencies: []
"), null));
                Assert.Empty(source.GetDirectDependencies("scene"));
                Assert.Equal(new[] { "z-base" }, copy.GetDirectDependencies("scene"));
                Object.DestroyImmediate(source);
                Assert.Equal(new[] { "scene", "z-base" }, copy.GetAllAssetBundles());
            }
            finally { if (source) Object.DestroyImmediate(source); Object.DestroyImmediate(copy); }
        }

        [Fact]
        public void ManifestLookupFoldsAsciiWithoutUsingTheCurrentCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            var manifest = Load();
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.Equal(manifest.GetAssetBundleHash("scene"), manifest.GetAssetBundleHash("SCENE"));
                Assert.Equal(new[] { "base" }, manifest.GetDirectDependencies("ScEnE"));
                foreach (var name in new[] { "ſcene", "scene ", " scene", "scene/", "", null })
                {
                    Assert.Empty(manifest.GetAllDependencies(name));
                    Assert.Equal(default, manifest.GetAssetBundleHash(name));
                }
            }
            finally { CultureInfo.CurrentCulture = previous; Object.DestroyImmediate(manifest); }
        }

        [Fact]
        public void ManifestAdapterRemovesTheEngineInterfaceAndRetainsReaderDiscovery()
        {
            Assert.False(typeof(ISerializedAsset).IsAssignableFrom(typeof(AssetBundleManifest)));
            Assert.Equal(typeof(AssetBundleManifest), AssetAdapters.Reader(typeof(AssetBundleManifest)).DeclaringType);
            var first = Load("first"); var second = Load("second");
            try
            {
                Assert.Equal(new[] { "first" }, first.GetAllDependencies("scene"));
                Assert.Equal(new[] { "second" }, second.GetAllDependencies("scene"));
                Assert.Empty(first.GetAllDependencies("missing"));
                Assert.Equal(default, first.GetAssetBundleHash("missing"));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
    }
}
