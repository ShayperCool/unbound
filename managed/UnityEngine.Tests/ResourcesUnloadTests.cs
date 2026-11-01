using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class ResourcesUnloadTests
    {
        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        static List<string> Errors(System.Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error) errors.Add(message); };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return errors;
        }

        [Fact]
        public void AMaterialSharingItsFileIsNotReadAgain()
        {
            var material = new Material { name = "shared", color = Color.red, IsPersistentAsset = true };
            var texture = new Texture2D(2, 2) { name = "atlas", IsPersistentAsset = true };
            material.mainTexture = texture;
            var file = new LoadedFile { Guid = "0123456789abcdef0123456789abcdef", Path = "Assets/Shared.asset", MainObject = material };
            file.Objects[1] = material;
            file.Objects[2] = texture;
            AssetDatabaseRuntime.AddLoaded(file);
            const string member = "Resources.UnloadAsset(Material)";
            int before = Hits(member);
            try
            {
                var errors = Errors(() => Resources.UnloadAsset(material));
                Assert.Empty(errors);
                Assert.Equal(before + 1, Hits(member));
                // Neither reverted nor its sibling destroyed.
                Assert.Equal(Color.red, material.color);
                Assert.True(texture != null);
                Assert.Same(texture, material.mainTexture);
                Assert.Same(material, AssetDatabaseRuntime.Load(file.Guid, 1, typeof(Material)));
                Assert.Same(texture, AssetDatabaseRuntime.Load(file.Guid, 2, typeof(Texture2D)));
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(file.Guid);
                Object.DestroyImmediate(material, true);
                Object.DestroyImmediate(texture, true);
            }
        }

        [Fact]
        public void ARuntimeCustomRenderTextureIsRefusedAsANonAsset()
        {
            var crt = new CustomRenderTexture(8, 8) { m_Loaded = true };
            int unloaded = 0;
            System.Action<CustomRenderTexture> onUnloaded = t => { if (t == crt) unloaded++; };
            CustomRenderTextureManager.textureUnloaded += onUnloaded;
            try
            {
                var errors = Errors(() => Resources.UnloadAsset(crt));
                Assert.Equal(new[] { "UnloadAsset can only be used on assets;" }, errors);
                Assert.Equal(0, unloaded);
                var all = new List<CustomRenderTexture>();
                CustomRenderTextureManager.GetAllCustomRenderTextures(all);
                Assert.Contains(crt, all);
            }
            finally
            {
                CustomRenderTextureManager.textureUnloaded -= onUnloaded;
                CustomRenderTextureManager.Unregister(crt);
                Object.DestroyImmediate(crt);
            }
        }

        [Fact]
        public void ACustomRenderTextureAssetIsUnloaded()
        {
            var crt = new CustomRenderTexture(8, 8) { m_Loaded = true, IsPersistentAsset = true };
            int unloaded = 0;
            System.Action<CustomRenderTexture> onUnloaded = t => { if (t == crt) unloaded++; };
            CustomRenderTextureManager.textureUnloaded += onUnloaded;
            try
            {
                var errors = Errors(() => Resources.UnloadAsset(crt));
                Assert.Empty(errors);
                Assert.Equal(1, unloaded);
                var all = new List<CustomRenderTexture>();
                CustomRenderTextureManager.GetAllCustomRenderTextures(all);
                Assert.DoesNotContain(crt, all);
            }
            finally
            {
                CustomRenderTextureManager.textureUnloaded -= onUnloaded;
                Object.DestroyImmediate(crt, true);
            }
        }
    }
}
