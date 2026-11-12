using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.SceneManagement;
using Xunit;
using UObject = UnityEngine.Object;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class EditorShutdownTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThrowingUnloadHandlerDoesNotInterruptOtherScenesOrFinalization(bool saveThrows)
        {
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            var persistent = SceneRegistry.DontDestroyOnLoad;
            var previousRoots = persistent.Roots.ToArray();
            persistent.Roots.Clear();
            var first = scenes.Loaded("FirstQuit");
            var firstObject = new GameObject("first");
            var second = scenes.Loaded("SecondQuit");
            SceneRegistry.Active = second;
            var secondObject = new GameObject("second");
            var keep = new GameObject("persistent");
            UObject.DontDestroyOnLoad(keep);
            var calls = new List<string>();
            UnityEngine.Events.UnityAction<Scene> throws = scene =>
            {
                Assert.True(firstObject == null && secondObject == null && keep == null);
                Assert.True(scene.IsValid());
                Assert.False(scene.isLoaded);
                calls.Add(scene.name);
                throw new InvalidOperationException("shutdown control");
            };
            UnityEngine.Events.UnityAction<Scene> afterThrow = scene => calls.Add("after throw");
            SceneManager.sceneUnloaded += throws;
            SceneManager.sceneUnloaded += afterThrow;
            try
            {
                void Save()
                {
                    Assert.Empty(SceneRegistry.Loaded);
                    calls.Add("save");
                    if (saveThrows) throw new InvalidOperationException("save control");
                }
                void Finish() => PlayerLoopDriver.FinishQuit(true, Save, () => calls.Add("plugins"));
                if (saveThrows) Assert.Throws<InvalidOperationException>(Finish);
                else Finish();
                Assert.Equal(new[] { "FirstQuit", "SecondQuit", "save", "plugins" }, calls);
                Assert.Empty(SceneRegistry.Loaded);
                Assert.Null(SceneRegistry.Active);
                Assert.Null(SceneRegistry.Get(first.Handle));
                Assert.Null(SceneRegistry.Get(second.Handle));
            }
            finally
            {
                SceneManager.sceneUnloaded -= throws;
                SceneManager.sceneUnloaded -= afterThrow;
                UObject.DestroyImmediate(firstObject);
                UObject.DestroyImmediate(secondObject);
                UObject.DestroyImmediate(keep);
                persistent.Roots.Clear();
                persistent.Roots.AddRange(previousRoots);
            }
        }
    }
}
