using System.IO;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>An imported text asset unloaded as unused is no loaded object until it is used or
    /// obtained again, the same object (tests/projects/sceneunload `res.text`).</summary>
    public class TextAssetUnusedUnloadTests
    {
        [Fact]
        public void AnUnusedUnloadedTextAssetIsListedAgainOnceUsed()
        {
            var file = Path.GetTempFileName();
            File.WriteAllText(file, "text");
            var asset = new TextAsset(file, "probe", 4);
            try
            {
                Assert.Contains(asset, Resources.FindObjectsOfTypeAll<TextAsset>());
                // Never read: it still unloads (the Editor unloads it whether or not it was read).
                Assert.True(asset.UnloadUnused());
                Assert.False(asset.UnloadUnused());
                Assert.DoesNotContain(asset, Resources.FindObjectsOfTypeAll<TextAsset>());
                Assert.DoesNotContain(asset, (Object[])Resources.FindObjectsOfTypeAll(typeof(TextAsset)));
                Assert.Equal("text", asset.text);
                Assert.Contains(asset, Resources.FindObjectsOfTypeAll<TextAsset>());
                // A text asset no file made is not unloaded.
                var made = new TextAsset("made");
                Assert.False(made.UnloadUnused());
                Object.DestroyImmediate(made);
            }
            finally
            {
                Object.DestroyImmediate(asset);
                File.Delete(file);
            }
        }

        static class Holder
        {
            // A class with class initialization: the unused-asset walk does not read its statics.
            internal static readonly System.Collections.Generic.List<TextAsset> Held = new System.Collections.Generic.List<TextAsset>();
        }

        [Fact]
        public void ATextAssetWithAViewOutIsNotUnloaded()
        {
            var file = Path.GetTempFileName();
            File.WriteAllText(file, "text");
            var asset = new TextAsset(file, "viewed", 4);
            try
            {
                Holder.Held.Add(asset);
                var view = asset.GetData<byte>();
                Assert.False(asset.UnloadUnused());
                Assert.Contains(asset, Resources.FindObjectsOfTypeAll<TextAsset>());
                Assert.Equal((byte)'t', view[0]);
            }
            finally
            {
                Holder.Held.Remove(asset);
                Object.DestroyImmediate(asset);
                File.Delete(file);
            }
        }
    }
}
