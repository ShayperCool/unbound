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
    }
}
